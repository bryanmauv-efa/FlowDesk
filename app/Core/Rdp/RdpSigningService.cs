using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace TermServMultiScreen.Core.Rdp;

/// <summary>Quelle option de rdpsign.exe utiliser, et quelle empreinte lui passer.</summary>
public sealed record RdpSignRecipe(string Switch, string HashKind, string HashValue)
{
    public override string ToString() => $"rdpsign.exe {Switch} <{HashKind}>";
}

/// <summary>Résultat complet d'une exécution de rdpsign.exe, sans rien masquer.</summary>
public sealed class RdpSigningResult
{
    public required bool Success { get; init; }
    public required string FilePath { get; init; }
    public string Command { get; init; } = "";
    public int ExitCode { get; init; }
    public long Milliseconds { get; init; }
    public string StdOut { get; init; } = "";
    public string StdErr { get; init; } = "";
    public bool HasSignScope { get; init; }
    public bool HasSignature { get; init; }
    public string? FileSha256AfterSigning { get; init; }
    public string? FailureReason { get; init; }

    /// <summary>Message court destiné à l'utilisateur.</summary>
    public string UserMessage => FailureReason ?? "Fichier .rdp signé.";
}

/// <summary>
/// Signature des fichiers .rdp par rdpsign.exe.
///
/// Point capital, vérifié empiriquement sur cette machine : l'option de rdpsign nomme
/// l'algorithme de signature du FICHIER, pas le format de l'empreinte du certificat. Sur
/// Windows 26200, /sha1 a disparu et seul /sha256 existe — mais la valeur attendue reste
/// l'empreinte SHA-1 du certificat (sa propriété « Thumbprint »). Passer le SHA-256 du
/// certificat produit 0x80092004 CRYPT_E_NOT_FOUND. La recette est donc auto-calibrée au
/// démarrage avec l'option /l, qui teste sans modifier le fichier.
/// </summary>
public static class RdpSigningService
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static RdpSignRecipe? _recipe;
    private static string? _recipeDiagnostic;

    public static RdpSignRecipe? Recipe => _recipe;
    public static string RecipeDiagnostic => _recipeDiagnostic ?? "(non calibrée)";

    private static string RdpSignPath
    {
        get
        {
            string system = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "rdpsign.exe");
            return File.Exists(system) ? system : "rdpsign.exe";
        }
    }

    // ------------------------------------------------------------------ calibrage

    /// <summary>Options que cette build de rdpsign.exe annonce dans son aide.</summary>
    public static async Task<(bool HasSha256, bool HasSha1, string Help)> ReadSupportedSwitchesAsync()
    {
        var run = await RunAsync(["/?"], TimeSpan.FromSeconds(15));
        string help = run.StdOut + run.StdErr;
        return (help.Contains("/sha256", StringComparison.OrdinalIgnoreCase),
                help.Contains("/sha1", StringComparison.OrdinalIgnoreCase),
                help);
    }

    /// <summary>
    /// Détermine la combinaison option + empreinte que rdpsign accepte réellement, en mode
    /// test (/l) : aucun fichier de session n'est modifié pendant le calibrage.
    /// </summary>
    public static async Task<RdpSignRecipe?> ResolveRecipeAsync(RdpCertificateInfo certificate, bool force = false)
    {
        if (!force && _recipe is not null) return _recipe;

        await Gate.WaitAsync();
        try
        {
            if (!force && _recipe is not null) return _recipe;

            var (hasSha256, hasSha1, _) = await ReadSupportedSwitchesAsync();
            List<string> switches = [];
            if (hasSha256) switches.Add("/sha256");
            if (hasSha1) switches.Add("/sha1");
            if (switches.Count == 0) switches.AddRange(["/sha256", "/sha1"]);

            (string Kind, string Value)[] hashes =
            [
                ("empreinte SHA-1 du certificat", certificate.Sha1),
                ("SHA-256 du certificat", certificate.Sha256)
            ];

            string probe = CreateProbeFile();
            var report = new StringBuilder();

            foreach (string option in switches)
            {
                foreach (var (kind, value) in hashes)
                {
                    var run = await RunAsync([option, value, "/l", probe], TimeSpan.FromSeconds(30));
                    report.AppendLine($"  {option} + {kind} → ExitCode {run.ExitCode} (0x{run.ExitCode:X8})");

                    if (run.ExitCode == 0)
                    {
                        _recipe = new RdpSignRecipe(option, kind, value);
                        _recipeDiagnostic = report.ToString().TrimEnd();
                        Log.Write($"Calibrage rdpsign réussi : {option} avec {kind}.{Environment.NewLine}{_recipeDiagnostic}");
                        TryDelete(probe);
                        return _recipe;
                    }
                }
            }

            _recipeDiagnostic = report.ToString().TrimEnd();
            Log.Write($"Calibrage rdpsign : aucune combinaison acceptée.{Environment.NewLine}{_recipeDiagnostic}");
            TryDelete(probe);
            return null;
        }
        finally { Gate.Release(); }
    }

    private static string CreateProbeFile()
    {
        Paths.EnsureFolders();
        string path = Path.Combine(Paths.DataFolder, "rdpsign-calibrage.rdp");
        // Fichier minimal mais réaliste, au format natif de mstsc (UTF-16LE + BOM).
        RdpFile.Write(path, "screen mode id:i:2\r\nuse multimon:i:0\r\nfull address:s:127.0.0.1\r\n");
        return path;
    }

    // ------------------------------------------------------------------ signature

    /// <summary>
    /// Signe le fichier puis vérifie réellement le résultat : code de sortie, présence de
    /// signscope et de signature. Aucune exception avalée, aucun succès supposé.
    /// </summary>
    public static async Task<RdpSigningResult> SignAndValidateAsync(string rdpFilePath)
    {
        if (!File.Exists(rdpFilePath))
        {
            return Fail(rdpFilePath, $"Le fichier à signer n'existe pas : {rdpFilePath}");
        }

        var certificate = await RdpCertificateService.GetOrCreateAsync();
        if (certificate is null)
        {
            return Fail(rdpFilePath, "Aucun certificat de signature FlowDesk disponible.");
        }
        if (!certificate.IsUsable)
        {
            return Fail(rdpFilePath,
                "Le certificat FlowDesk n'est pas exploitable "
                + $"(clé privée : {certificate.HasPrivateKey}, signature possible : {certificate.KeyCanSign}, "
                + $"validité : {certificate.IsTimeValid}).");
        }

        var recipe = await ResolveRecipeAsync(certificate);
        if (recipe is null)
        {
            return Fail(rdpFilePath,
                "rdpsign.exe n'accepte aucune combinaison option/empreinte pour ce certificat."
                + Environment.NewLine + RecipeDiagnostic);
        }

        // 1. Essai à blanc : valide le certificat et la clé sans toucher au fichier.
        var dryRun = await RunAsync([recipe.Switch, recipe.HashValue, "/l", rdpFilePath], TimeSpan.FromSeconds(30));
        if (dryRun.ExitCode != 0)
        {
            return new RdpSigningResult
            {
                Success = false,
                FilePath = rdpFilePath,
                Command = dryRun.Command,
                ExitCode = dryRun.ExitCode,
                Milliseconds = dryRun.Milliseconds,
                StdOut = dryRun.StdOut,
                StdErr = dryRun.StdErr,
                FailureReason = $"Test de signature (/l) refusé par rdpsign : code 0x{dryRun.ExitCode:X8}. "
                              + Describe(dryRun.ExitCode)
            };
        }

        // 2. Signature réelle.
        var signing = await RunAsync([recipe.Switch, recipe.HashValue, "/v", rdpFilePath], TimeSpan.FromSeconds(60));
        if (signing.ExitCode != 0)
        {
            return new RdpSigningResult
            {
                Success = false,
                FilePath = rdpFilePath,
                Command = signing.Command,
                ExitCode = signing.ExitCode,
                Milliseconds = signing.Milliseconds,
                StdOut = signing.StdOut,
                StdErr = signing.StdErr,
                FailureReason = $"rdpsign.exe a échoué : code 0x{signing.ExitCode:X8}. {Describe(signing.ExitCode)}"
            };
        }

        // 3. Vérification du contenu : c'est la seule preuve acceptable.
        var (hasScope, hasSignature) = ReadSignatureFields(rdpFilePath);
        string? fileHash = ComputeFileSha256(rdpFilePath);

        if (!hasScope || !hasSignature)
        {
            return new RdpSigningResult
            {
                Success = false,
                FilePath = rdpFilePath,
                Command = signing.Command,
                ExitCode = signing.ExitCode,
                Milliseconds = signing.Milliseconds,
                StdOut = signing.StdOut,
                StdErr = signing.StdErr,
                HasSignScope = hasScope,
                HasSignature = hasSignature,
                FileSha256AfterSigning = fileHash,
                FailureReason = "rdpsign a renvoyé 0 mais le fichier ne contient pas "
                              + $"{(hasScope ? "" : "signscope:s: ")}{(hasSignature ? "" : "signature:s: ")}".Trim()
            };
        }

        var result = new RdpSigningResult
        {
            Success = true,
            FilePath = rdpFilePath,
            Command = signing.Command,
            ExitCode = signing.ExitCode,
            Milliseconds = signing.Milliseconds,
            StdOut = signing.StdOut,
            StdErr = signing.StdErr,
            HasSignScope = true,
            HasSignature = true,
            FileSha256AfterSigning = fileHash
        };

        Log.Write(FormatReport(certificate, recipe, dryRun, result));
        return result;
    }

    private static RdpSigningResult Fail(string path, string reason)
    {
        Log.Write($"Signature .rdp impossible : {reason}");
        return new RdpSigningResult { Success = false, FilePath = path, FailureReason = reason };
    }

    private static string FormatReport(RdpCertificateInfo certificate, RdpSignRecipe recipe,
        ProcessRun dryRun, RdpSigningResult result)
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== FlowDesk RDP signing ===");
        sb.AppendLine();
        sb.AppendLine("Certificate:");
        sb.AppendLine($"  Subject           : {certificate.Subject}");
        sb.AppendLine($"  Store             : {certificate.StorePath}");
        sb.AppendLine($"  HasPrivateKey     : {certificate.HasPrivateKey}");
        sb.AppendLine($"  Key provider      : {certificate.KeyProvider}");
        sb.AppendLine($"  Key container     : {certificate.KeyContainer}");
        sb.AppendLine($"  SHA1              : {certificate.Sha1}");
        sb.AppendLine($"  SHA256            : {certificate.Sha256}");
        sb.AppendLine();
        sb.AppendLine($"Recipe              : {recipe.Switch} + {recipe.HashKind}");
        sb.AppendLine($"Command             : {result.Command}");
        sb.AppendLine();
        sb.AppendLine("rdpsign /l:");
        sb.AppendLine($"  ExitCode          : {dryRun.ExitCode}");
        sb.AppendLine($"  Duration          : {dryRun.Milliseconds} ms");
        sb.AppendLine();
        sb.AppendLine("rdpsign:");
        sb.AppendLine($"  ExitCode          : {result.ExitCode}");
        sb.AppendLine($"  Duration          : {result.Milliseconds} ms");
        sb.AppendLine($"  Output            : {result.StdOut.Replace("\r", "").Replace("\n", " ").Trim()}");
        sb.AppendLine();
        sb.AppendLine("RDP signature:");
        sb.AppendLine($"  signscope         : {(result.HasSignScope ? "PRESENT" : "ABSENT")}");
        sb.AppendLine($"  signature         : {(result.HasSignature ? "PRESENT" : "ABSENT")}");
        sb.AppendLine($"  File SHA256       : {result.FileSha256AfterSigning}");
        sb.Append("READY");
        return sb.ToString();
    }

    // ------------------------------------------------------------------ vérifications

    /// <summary>
    /// Lit les champs de signature en respectant l'encodage réel du fichier (mstsc écrit en
    /// UTF-16LE) et en ancrant la recherche en début de ligne.
    /// </summary>
    public static (bool HasSignScope, bool HasSignature) ReadSignatureFields(string rdpFilePath)
    {
        try
        {
            bool scope = false, signature = false;
            foreach (string line in RdpFile.ReadAllLinesAnyEncoding(rdpFilePath))
            {
                if (line.StartsWith("signscope:s:", StringComparison.OrdinalIgnoreCase)) scope = true;
                else if (line.StartsWith("signature:s:", StringComparison.OrdinalIgnoreCase)) signature = true;
            }
            return (scope, signature);
        }
        catch (Exception ex)
        {
            Log.Write($"Lecture des champs de signature : {ex.Message}");
            return (false, false);
        }
    }

    public static string? ComputeFileSha256(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(stream)).ToUpperInvariant();
        }
        catch { return null; }
    }

    /// <summary>Garantit qu'aucune modification n'a eu lieu entre la signature et le lancement.</summary>
    public static bool IsUnchangedSince(string path, string? expectedSha256) =>
        expectedSha256 is not null && ComputeFileSha256(path) == expectedSha256;

    private static string Describe(int exitCode) => unchecked((uint)exitCode) switch
    {
        0x80092004 => "CRYPT_E_NOT_FOUND : aucun certificat du magasin ne correspond à l'empreinte fournie.",
        0x80070002 => "Fichier introuvable.",
        0x80070005 => "Accès refusé : le fichier .rdp est peut-être en lecture seule ou verrouillé.",
        0x8009200B => "CRYPT_E_NO_KEY_PROPERTY : le certificat n'a pas de clé privée associée.",
        0x8009000F => "L'objet existe déjà.",
        _ => "Voir la sortie de rdpsign.exe ci-dessus."
    };

    // ------------------------------------------------------------------ exécution

    private sealed record ProcessRun(int ExitCode, string StdOut, string StdErr, long Milliseconds, string Command);

    /// <summary>
    /// rdpsign.exe écrit dans la page de codes OEM de la console, pas en UTF-8 : sans cela les
    /// accents de ses messages arrivent illisibles dans le journal.
    /// </summary>
    private static Encoding ConsoleEncoding()
    {
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(System.Globalization.CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
        }
        catch
        {
            return Encoding.UTF8;
        }
    }

    private static async Task<ProcessRun> RunAsync(IReadOnlyList<string> arguments, TimeSpan timeout)
    {
        var info = new ProcessStartInfo
        {
            FileName = RdpSignPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = ConsoleEncoding(),
            StandardErrorEncoding = ConsoleEncoding()
        };
        foreach (string argument in arguments) info.ArgumentList.Add(argument);

        string command = $"\"{info.FileName}\" " + string.Join(" ",
            arguments.Select(a => a.Contains(' ') ? $"\"{a}\"" : a));

        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var process = Process.Start(info);
            if (process is null) return new ProcessRun(-1, "", "Impossible de démarrer rdpsign.exe", 0, command);

            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            using var cancellation = new CancellationTokenSource(timeout);
            try
            {
                await process.WaitForExitAsync(cancellation.Token);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                return new ProcessRun(-2, await stdout, "rdpsign.exe n'a pas répondu dans le délai imparti.",
                    stopwatch.ElapsedMilliseconds, command);
            }

            return new ProcessRun(process.ExitCode, (await stdout).Trim(), (await stderr).Trim(),
                stopwatch.ElapsedMilliseconds, command);
        }
        catch (Exception ex)
        {
            return new ProcessRun(-3, "", ex.Message, stopwatch.ElapsedMilliseconds, command);
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
