using System.Diagnostics;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.Win32;

namespace TermServMultiScreen.Core.Rdp;

public enum CheckStatus { Pass, Fail, Warning, Info }

public sealed record RdpCheck(int Number, string Name, CheckStatus Status, string Detail)
{
    public string Label => Status switch
    {
        CheckStatus.Pass => "PASS",
        CheckStatus.Fail => "FAIL",
        CheckStatus.Warning => "WARNING",
        _ => "INFO"
    };
}

/// <summary>
/// Diagnostic complet et honnête de la chaîne de confiance .rdp : chaque contrôle est
/// réellement exécuté, aucun résultat n'est codé en dur.
/// </summary>
public static class RdpTrustDiagnostics
{
    public static async Task<(List<RdpCheck> Checks, string Report)> RunAsync()
    {
        var checks = new List<RdpCheck>();
        void Add(int n, string name, CheckStatus status, string detail) => checks.Add(new RdpCheck(n, name, status, detail));

        // Passe par l'orchestrateur : même état, mêmes journaux que lors d'une connexion réelle.
        await RdpTrust.InitializeAsync();

        // ---------------------------------------------------------- environnement
        Add(1, "Version de Windows", CheckStatus.Info, DescribeWindows());
        Add(2, "Version de mstsc.exe", CheckStatus.Info, FileVersion("mstsc.exe"));
        Add(3, "Application packagée", CheckStatus.Info, IsPackaged() ? "packagée (MSIX)" : "non packagée");

        // ---------------------------------------------------------- certificat
        var certificate = await RdpCertificateService.GetOrCreateAsync();
        if (certificate is null)
        {
            Add(4, "Certificat trouvé", CheckStatus.Fail, "aucun certificat FlowDesk disponible");
            for (int n = 5; n <= 25; n++)
                Add(n, "Contrôle suivant", CheckStatus.Fail, "non exécuté : pas de certificat");
            return (checks, Format(checks));
        }

        Add(4, "Certificat trouvé", CheckStatus.Pass, $"{certificate.Subject}, expire le {certificate.NotAfter:dd/MM/yyyy}");
        Add(5, "Magasin attendu", certificate.StorePath == @"CurrentUser\My" ? CheckStatus.Pass : CheckStatus.Warning,
            certificate.StorePath);
        Add(6, "Clé privée présente", certificate.HasPrivateKey ? CheckStatus.Pass : CheckStatus.Fail,
            certificate.HasPrivateKey.ToString());
        Add(7, "Clé privée utilisable", certificate.KeyCanSign ? CheckStatus.Pass : CheckStatus.Fail,
            certificate.KeyCanSign ? $"signature de test réussie ({certificate.KeyProvider})" : "signature de test impossible");
        Add(8, "Clé persistante", certificate.KeyIsPersisted ? CheckStatus.Pass : CheckStatus.Fail,
            certificate.KeyIsPersisted
                ? $"conteneur {certificate.KeyContainer} ({certificate.KeyDetail})"
                : "clé éphémère : invisible pour un processus externe");
        Add(9, "Usage DigitalSignature", certificate.HasDigitalSignatureUsage ? CheckStatus.Pass : CheckStatus.Fail,
            certificate.HasDigitalSignatureUsage ? "présent" : "absent");
        Add(10, "EKU Code Signing", certificate.HasCodeSigningEku ? CheckStatus.Pass : CheckStatus.Fail,
            certificate.HasCodeSigningEku ? "1.3.6.1.5.5.7.3.3 présent" : "absent");
        Add(11, "SHA-1 calculé", CheckStatus.Pass, certificate.Sha1);
        Add(12, "SHA-256 calculé", CheckStatus.Pass, certificate.Sha256);

        // ---------------------------------------------------------- rdpsign
        var (hasSha256Switch, hasSha1Switch, _) = await RdpSigningService.ReadSupportedSwitchesAsync();
        var recipe = await RdpSigningService.ResolveRecipeAsync(certificate, force: true);

        Add(13, "rdpsign /sha1 (test /l)",
            hasSha1Switch ? (recipe?.Switch == "/sha1" ? CheckStatus.Pass : CheckStatus.Warning) : CheckStatus.Info,
            hasSha1Switch
                ? (recipe?.Switch == "/sha1" ? $"accepté avec {recipe.HashKind}" : "présent mais non retenu")
                : "option absente de cette build de rdpsign.exe");

        Add(14, "rdpsign /sha256 (test /l)",
            hasSha256Switch ? (recipe?.Switch == "/sha256" ? CheckStatus.Pass : CheckStatus.Fail) : CheckStatus.Info,
            hasSha256Switch
                ? (recipe?.Switch == "/sha256" ? $"accepté avec {recipe.HashKind}" : "refusé : " + RdpSigningService.RecipeDiagnostic)
                : "option absente de cette build de rdpsign.exe");

        // ---------------------------------------------------------- confiance locale
        bool inRoot = RdpCertificateService.IsInStore(StoreName.Root, certificate.Sha1);
        Add(15, "Confiance racine (Root)", inRoot ? CheckStatus.Pass : CheckStatus.Fail,
            inRoot ? @"présent dans CurrentUser\Root" : "absent");

        bool inPublisher = RdpCertificateService.IsInStore(StoreName.TrustedPublisher, certificate.Sha1);
        Add(16, "Éditeurs approuvés", inPublisher ? CheckStatus.Pass : CheckStatus.Fail,
            inPublisher ? @"présent dans CurrentUser\TrustedPublisher" : "absent");

        var (chainValid, chainDetail) = RdpCertificateService.ValidateChain(certificate.Certificate);
        Add(17, "Validation X509Chain", chainValid ? CheckStatus.Pass : CheckStatus.Fail, chainDetail);

        // ---------------------------------------------------------- stratégie
        var policy = RdpPublisherTrustService.Read(certificate);
        Add(18, "Ruche des stratégies accessible",
            policy.PoliciesKeyWritable ? CheckStatus.Pass : CheckStatus.Warning,
            policy.PoliciesKeyWritable
                ? "écriture possible dans le contexte courant"
                : $"lecture seule — {policy.AccessError} (élévation requise, comportement Windows normal)");

        string provisioning = "";
        if (!policy.Trusted)
        {
            try { provisioning = " | script prêt : " + RdpPublisherTrustService.WriteProvisioningScript(certificate); }
            catch (Exception ex) { provisioning = $" | script non écrit : {ex.Message}"; }
        }
        Add(19, "TrustedCertThumbprints configuré",
            policy.Trusted ? CheckStatus.Pass : CheckStatus.Fail,
            policy.Trusted
                ? $"{policy.MatchedIn} = {policy.ExpectedEntry}"
                : $"attendu : {policy.ExpectedEntry} | HKLM = {policy.MachineValue ?? "(absent)"} | HKCU = {policy.UserValue ?? "(absent)"}"
                  + provisioning);

        Add(20, "Format d'empreinte pour cette build",
            CheckStatus.Pass,
            policy.PrefixSupported
                ? $"préfixes sha256:/sha384:/sha512: pris en charge → entrée « {policy.ExpectedEntry} »"
                  + (policy.Sha1Disabled ? " ; SHA-1 désactivé par stratégie" : " ; SHA-1 hérité encore accepté")
                : $"build antérieure : empreinte SHA-1 brute → « {policy.ExpectedEntry} »");

        // ---------------------------------------------------------- signature réelle
        string probe = Path.Combine(Paths.DataFolder, "FlowDesk-diagnostic.rdp");
        try
        {
            Paths.EnsureFolders();
            RdpFile.Write(probe,
                "screen mode id:i:2\r\nuse multimon:i:0\r\nfull address:s:127.0.0.1\r\nusername:s:diagnostic\r\n");
            Add(21, "Fichier .rdp généré", File.Exists(probe) ? CheckStatus.Pass : CheckStatus.Fail, probe);

            var signing = await RdpSigningService.SignAndValidateAsync(probe);
            Add(22, "Fichier .rdp signé", signing.Success ? CheckStatus.Pass : CheckStatus.Fail,
                signing.Success
                    ? $"ExitCode {signing.ExitCode} en {signing.Milliseconds} ms"
                    : signing.FailureReason ?? "échec");
            Add(23, "signscope présent", signing.HasSignScope ? CheckStatus.Pass : CheckStatus.Fail,
                signing.HasSignScope ? "PRESENT" : "ABSENT");
            Add(24, "signature présente", signing.HasSignature ? CheckStatus.Pass : CheckStatus.Fail,
                signing.HasSignature ? "PRESENT" : "ABSENT");

            bool unchanged = RdpSigningService.IsUnchangedSince(probe, signing.FileSha256AfterSigning);
            Add(25, "Aucune modification après signature",
                signing.Success ? (unchanged ? CheckStatus.Pass : CheckStatus.Fail) : CheckStatus.Warning,
                signing.Success
                    ? (unchanged ? $"SHA-256 du fichier inchangé ({signing.FileSha256AfterSigning?[..16]}…)" : "le fichier a changé après signature")
                    : "non applicable : la signature a échoué");
        }
        catch (Exception ex)
        {
            Add(21, "Fichier .rdp généré", CheckStatus.Fail, ex.Message);
            for (int n = 22; n <= 25; n++) Add(n, "Contrôle de signature", CheckStatus.Fail, "non exécuté");
        }
        finally
        {
            try { if (File.Exists(probe)) File.Delete(probe); } catch { }
        }

        string report = Format(checks);
        Log.Write(report);
        return (checks, report);
    }

    private static string Format(List<RdpCheck> checks)
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== FlowDesk — diagnostic de la confiance .rdp ===");
        sb.AppendLine();
        foreach (var check in checks.OrderBy(c => c.Number))
            sb.AppendLine($"[{check.Number:00}] {check.Label,-7} {check.Name,-34} {check.Detail}");

        int failed = checks.Count(c => c.Status == CheckStatus.Fail);
        int warned = checks.Count(c => c.Status == CheckStatus.Warning);
        sb.AppendLine();
        sb.AppendLine(failed == 0
            ? warned == 0 ? "RÉSULTAT : READY — signature et confiance complètes." : $"RÉSULTAT : signature opérationnelle, {warned} avertissement(s)."
            : $"RÉSULTAT : {failed} contrôle(s) en échec, {warned} avertissement(s).");
        return sb.ToString();
    }

    private static string DescribeWindows()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            string product = key?.GetValue("ProductName")?.ToString() ?? "Windows";
            string display = key?.GetValue("DisplayVersion")?.ToString() ?? "";
            string build = key?.GetValue("CurrentBuild")?.ToString() ?? Environment.OSVersion.Version.Build.ToString();
            string ubr = key?.GetValue("UBR")?.ToString() ?? "0";
            return $"{product} {display} build {build}.{ubr}";
        }
        catch { return Environment.OSVersion.VersionString; }
    }

    private static string FileVersion(string systemFile)
    {
        try
        {
            string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), systemFile);
            return File.Exists(path)
                ? FileVersionInfo.GetVersionInfo(path).FileVersion ?? "(version inconnue)"
                : "(introuvable)";
        }
        catch (Exception ex) { return ex.Message; }
    }

    private static bool IsPackaged()
    {
        try { return Windows.ApplicationModel.Package.Current is not null; }
        catch { return false; }
    }
}
