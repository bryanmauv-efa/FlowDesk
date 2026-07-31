using System.Diagnostics;
using System.Text;
using Microsoft.Win32;

namespace TermServMultiScreen.Core.Rdp;

/// <summary>Ce que la stratégie « éditeurs .rdp approuvés » contient réellement sur cette machine.</summary>
public sealed class RdpPolicyState
{
    public required bool Trusted { get; init; }
    public required string ExpectedEntry { get; init; }
    public required bool PrefixSupported { get; init; }
    public required bool Sha1Disabled { get; init; }
    public string? MachineValue { get; init; }
    public string? UserValue { get; init; }
    public string? MatchedIn { get; init; }
    public required bool PoliciesKeyWritable { get; init; }
    public string? AccessError { get; init; }

    public string Summary => Trusted
        ? $"empreinte approuvée dans {MatchedIn}"
        : "aucune empreinte FlowDesk dans TrustedCertThumbprints";
}

/// <summary>
/// Déclare le certificat FlowDesk comme éditeur .rdp approuvé, via le mécanisme officiel
/// Windows : la stratégie « Spécifier les empreintes numériques des certificats représentant
/// des éditeurs .rdp approuvés » (valeur TrustedCertThumbprints).
///
/// Format vérifié dans TerminalServer.adml de CETTE machine (28/07/2026) :
/// « Les empreintes peuvent être préfixées par l'algorithme de hachage utilisé pour les
/// produire. Les préfixes pris en charge sont sha256:, sha384: et sha512:. Si aucun préfixe
/// n'est spécifié, l'entrée est traitée comme une empreinte SHA-1 héritée. »
///
/// Volontairement, ce service ne touche NI AllowSignedFiles NI AllowUnsignedFiles : ces deux
/// stratégies élargiraient la confiance à d'autres éditeurs. Seule l'empreinte FlowDesk est
/// déclarée. PublisherBypassList n'est pas utilisé : ce n'est pas un mécanisme de stratégie
/// mais le stockage de la case « Ne plus me demander » cochée par l'utilisateur.
/// </summary>
public static class RdpPublisherTrustService
{
    private const string PolicySubKey = @"SOFTWARE\Policies\Microsoft\Windows NT\Terminal Services";
    private const string ThumbprintsValue = "TrustedCertThumbprints";
    private const string DisableSha1Value = "DisableSHA1CertThumbprints";
    private static readonly char[] Separators = [';', ','];

    /// <summary>
    /// Les préfixes d'algorithme ne sont acceptés que par les builds où la stratégie expose
    /// DisableSHA1CertThumbprints : le fichier de définition local est la source de vérité.
    /// </summary>
    public static bool PrefixSupported()
    {
        try
        {
            string admx = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Windows), "PolicyDefinitions", "TerminalServer.admx");
            if (File.Exists(admx))
                return File.ReadAllText(admx).Contains(DisableSha1Value, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            Log.Write($"Lecture de TerminalServer.admx : {ex.Message}");
        }
        // Repli : la prise en charge SHA-2 est arrivée avec les mises à jour de juillet 2026.
        return Environment.OSVersion.Version.Build >= 26100;
    }

    /// <summary>Entrée à déclarer pour ce certificat, au format compris par cette build.</summary>
    public static string BuildEntry(RdpCertificateInfo certificate) =>
        PrefixSupported() ? $"sha256:{certificate.Sha256}" : certificate.Sha1;

    // ------------------------------------------------------------------ lecture

    public static RdpPolicyState Read(RdpCertificateInfo certificate)
    {
        bool prefix = PrefixSupported();
        string expected = BuildEntry(certificate);

        string? machine = ReadValue(Registry.LocalMachine, ThumbprintsValue);
        string? user = ReadValue(Registry.CurrentUser, ThumbprintsValue);
        bool sha1Disabled = (ReadValue(Registry.LocalMachine, DisableSha1Value) ?? "0") == "1";

        string? matched = null;
        if (Contains(machine, certificate, prefix, sha1Disabled)) matched = "HKLM (ordinateur)";
        else if (Contains(user, certificate, prefix, sha1Disabled)) matched = "HKCU (utilisateur)";

        var (writable, error) = TestWritable();

        return new RdpPolicyState
        {
            Trusted = matched is not null,
            ExpectedEntry = expected,
            PrefixSupported = prefix,
            Sha1Disabled = sha1Disabled,
            MachineValue = machine,
            UserValue = user,
            MatchedIn = matched,
            PoliciesKeyWritable = writable,
            AccessError = error
        };
    }

    private static string? ReadValue(RegistryKey hive, string name)
    {
        try
        {
            using var key = hive.OpenSubKey(PolicySubKey, false);
            return key?.GetValue(name)?.ToString();
        }
        catch (Exception ex)
        {
            Log.Write($"Lecture de {hive.Name}\\{PolicySubKey} : {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Une entrée correspond si elle vise le même certificat. Les entrées sans préfixe sont
    /// des SHA-1 héritées, ignorées quand la stratégie interdit SHA-1.
    /// </summary>
    private static bool Contains(string? list, RdpCertificateInfo certificate, bool prefixSupported, bool sha1Disabled)
    {
        if (string.IsNullOrWhiteSpace(list)) return false;

        foreach (string raw in list.Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string entry = raw.Trim();
            if (entry.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
            {
                if (!prefixSupported) continue;
                if (Equals(entry["sha256:".Length..], certificate.Sha256)) return true;
            }
            else if (entry.StartsWith("sha384:", StringComparison.OrdinalIgnoreCase)
                  || entry.StartsWith("sha512:", StringComparison.OrdinalIgnoreCase))
            {
                // Formats acceptés par Windows mais non produits par FlowDesk.
            }
            else if (!sha1Disabled && Equals(entry, certificate.Sha1))
            {
                return true;
            }
        }
        return false;
    }

    private static bool Equals(string a, string b) =>
        string.Equals(a.Replace(" ", "").Trim(), b.Replace(" ", "").Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Teste si la ruche des stratégies est accessible en écriture. Sous Windows,
    /// HKCU\SOFTWARE\Policies appartient à SYSTEM et n'accorde que la lecture aux
    /// utilisateurs : c'est voulu, un utilisateur ne doit pas pouvoir s'octroyer une stratégie.
    /// </summary>
    private static (bool Writable, string? Error) TestWritable()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Policies", true);
            return key is null ? (false, "Clé HKCU\\SOFTWARE\\Policies introuvable.") : (true, null);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public static bool IsElevated()
    {
        try
        {
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            return new System.Security.Principal.WindowsPrincipal(identity)
                .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch { return false; }
    }

    // ------------------------------------------------------------------ écriture

    public enum ConfigureOutcome { AlreadyConfigured, Configured, NeedsElevation, Failed }

    /// <summary>
    /// Tente d'écrire la stratégie dans le contexte courant. N'essaie aucun contournement :
    /// si Windows refuse, l'appelant est informé qu'une élévation est nécessaire.
    /// </summary>
    public static (ConfigureOutcome Outcome, string Detail) TryConfigure(RdpCertificateInfo certificate)
    {
        var state = Read(certificate);
        if (state.Trusted) return (ConfigureOutcome.AlreadyConfigured, state.Summary);

        string entry = state.ExpectedEntry;
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(PolicySubKey, true);
            if (key is null)
                return (ConfigureOutcome.NeedsElevation, "Création de la clé de stratégie refusée.");

            string existing = key.GetValue(ThumbprintsValue)?.ToString() ?? "";
            string updated = existing.Trim().Length == 0 ? entry : existing.TrimEnd(';', ',', ' ') + ";" + entry;
            key.SetValue(ThumbprintsValue, updated, RegistryValueKind.String);

            var confirmation = Read(certificate);
            return confirmation.Trusted
                ? (ConfigureOutcome.Configured, $"TrustedCertThumbprints = {updated}")
                : (ConfigureOutcome.Failed, "Valeur écrite mais non reconnue à la relecture.");
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
        {
            return (ConfigureOutcome.NeedsElevation,
                $"HKCU\\{PolicySubKey} : {ex.Message} — la ruche des stratégies est en lecture seule "
                + "pour les comptes non administrateurs (comportement Windows par défaut).");
        }
        catch (Exception ex)
        {
            return (ConfigureOutcome.Failed, ex.Message);
        }
    }

    // ------------------------------------------------------------------ provisionnement

    /// <summary>
    /// Script d'installation à exécuter UNE FOIS par un administrateur. Il ne fait qu'ajouter
    /// l'empreinte FlowDesk à la stratégie : aucune autre protection n'est modifiée.
    /// </summary>
    public static string BuildProvisioningScript(RdpCertificateInfo certificate)
    {
        string entry = BuildEntry(certificate);
        var sb = new StringBuilder();
        sb.AppendLine("# =====================================================================");
        sb.AppendLine("#  FlowDesk — déclaration de l'éditeur .rdp approuvé");
        sb.AppendLine("#  À exécuter UNE SEULE FOIS, dans une console PowerShell administrateur.");
        sb.AppendLine("#");
        sb.AppendLine("#  Ce script ajoute uniquement l'empreinte du certificat FlowDesk à la");
        sb.AppendLine("#  stratégie « éditeurs .rdp approuvés ». Il ne désactive aucune");
        sb.AppendLine("#  vérification et ne touche pas aux réglages TLS du serveur.");
        sb.AppendLine("# =====================================================================");
        sb.AppendLine();
        sb.AppendLine("$ErrorActionPreference = 'Stop'");
        sb.AppendLine("try {");
        sb.AppendLine();
        sb.AppendLine($"$Empreinte = '{entry}'");
        sb.AppendLine($"$Cle = 'HKLM:\\{PolicySubKey}'");
        sb.AppendLine();
        sb.AppendLine("if (-not (Test-Path $Cle)) { New-Item -Path $Cle -Force | Out-Null }");
        sb.AppendLine();
        sb.AppendLine("$Actuel = (Get-ItemProperty -Path $Cle -Name 'TrustedCertThumbprints' -ErrorAction SilentlyContinue).TrustedCertThumbprints");
        sb.AppendLine("if ([string]::IsNullOrWhiteSpace($Actuel)) {");
        sb.AppendLine("    $Nouveau = $Empreinte");
        sb.AppendLine("} elseif ($Actuel -split '[;,]' | Where-Object { $_.Trim() -ieq $Empreinte }) {");
        sb.AppendLine("    Write-Host 'Empreinte deja presente.' -ForegroundColor Yellow");
        sb.AppendLine("    $Nouveau = $Actuel");
        sb.AppendLine("} else {");
        sb.AppendLine("    $Nouveau = $Actuel.TrimEnd(';',',',' ') + ';' + $Empreinte");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("Set-ItemProperty -Path $Cle -Name 'TrustedCertThumbprints' -Value $Nouveau -Type String");
        sb.AppendLine("Write-Host \"TrustedCertThumbprints = $Nouveau\" -ForegroundColor Green");
        sb.AppendLine();
        sb.AppendLine("# Durcissement optionnel, recommandé par Microsoft : ignorer les empreintes");
        sb.AppendLine("# SHA-1 héritées. À n'activer que si toutes les entrées sont préfixées.");
        sb.AppendLine("# Set-ItemProperty -Path $Cle -Name 'DisableSHA1CertThumbprints' -Value 1 -Type DWord");
        sb.AppendLine();
        sb.AppendLine("gpupdate /target:computer /force | Out-Null");
        sb.AppendLine("Write-Host 'Termine.' -ForegroundColor Green");
        sb.AppendLine("exit 0");
        sb.AppendLine();
        sb.AppendLine("} catch {");
        sb.AppendLine("    Write-Host \"ECHEC : $($_.Exception.Message)\" -ForegroundColor Red");
        sb.AppendLine("    Start-Sleep -Seconds 8");
        sb.AppendLine("    exit 1");
        sb.AppendLine("}");
        return sb.ToString();
    }

    /// <summary>Écrit le script de provisionnement à côté des données de l'application.</summary>
    public static string WriteProvisioningScript(RdpCertificateInfo certificate)
    {
        Paths.EnsureFolders();
        string path = Path.Combine(Paths.DataFolder, "FlowDesk-EditeurApprouve.ps1");
        File.WriteAllText(path, BuildProvisioningScript(certificate), new UTF8Encoding(true));
        return path;
    }

    public sealed record ElevationResult(bool Cancelled, bool Completed, int ExitCode, string Detail, string ScriptPath);

    /// <summary>
    /// Propose l'élévation : Windows affiche l'invite UAC, l'utilisateur accepte ou refuse.
    /// On attend la fin du script pour pouvoir confirmer le résultat dans l'application.
    /// Aucune tentative de contournement, aucune modification d'ACL.
    /// </summary>
    public static async Task<ElevationResult> RunProvisioningElevatedAsync(RdpCertificateInfo certificate)
    {
        string script = WriteProvisioningScript(certificate);
        try
        {
            var info = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\"",
                UseShellExecute = true,
                Verb = "runas"          // déclenche l'invite UAC
            };

            using var process = Process.Start(info);
            if (process is null)
                return new ElevationResult(false, false, -1, "L'élévation n'a pas démarré.", script);

            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                return new ElevationResult(false, false, -2,
                    "Le script d'approbation n'a pas rendu la main dans le délai imparti.", script);
            }

            Log.Write($"Script d'approbation terminé, code {process.ExitCode}.");
            return new ElevationResult(false, process.ExitCode == 0, process.ExitCode,
                process.ExitCode == 0 ? "Stratégie appliquée." : "Le script a signalé une erreur.", script);
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            // ERROR_CANCELLED : invite UAC refusée ou annulée.
            Log.Write("Élévation refusée par l'utilisateur.");
            return new ElevationResult(true, false, 1223, "Élévation refusée.", script);
        }
        catch (Exception ex)
        {
            Log.Write($"Élévation : {ex.Message}");
            return new ElevationResult(false, false, -3, ex.Message, script);
        }
    }
}
