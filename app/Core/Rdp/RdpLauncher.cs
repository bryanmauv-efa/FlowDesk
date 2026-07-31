using System.Diagnostics;
using System.Globalization;

namespace TermServMultiScreen.Core.Rdp;

public sealed class RdpLaunchResult
{
    public required bool Success { get; init; }
    public string? Path { get; init; }
    public RdpSigningResult? Signing { get; init; }
    public bool PublisherTrusted { get; init; }
    public string? FailureReason { get; init; }

    /// <summary>Signature correcte mais stratégie absente : mstsc demandera confirmation une fois.</summary>
    public bool PublisherPromptExpected => Success && !PublisherTrusted;
}

/// <summary>
/// Pipeline strict : générer → écrire → fermer → signer → vérifier → lancer.
/// mstsc.exe n'est JAMAIS démarré si la signature n'est pas prouvée, et le fichier n'est
/// jamais modifié après signature.
/// </summary>
public static class RdpLauncher
{
    /// <summary>
    /// Contrôle avant vol : le nom utilisé pour se connecter doit figurer dans le certificat du
    /// serveur, sinon mstsc affiche « Impossible de vérifier l'identité de l'ordinateur distant ».
    /// Best-effort : si le serveur ne répond pas, on n'empêche rien, mstsc fera son travail.
    /// </summary>
    public static async Task<ServerCertificateProbe?> CheckServerNameAsync(string address, int timeoutMs = 2500)
    {
        if (string.IsNullOrWhiteSpace(address)) return null;

        var probe = await ServerCaTrustService.ProbeAsync(address, timeoutMs: timeoutMs);
        if (!probe.Reached)
        {
            Log.Write($"  serveur    : {address} non interrogeable avant lancement ({probe.Error})");
            return probe;
        }

        Log.Write($"  serveur    : certificat pour [{string.Join(", ", probe.DnsNames)}] — "
                + $"nom demandé « {probe.Host} » : {(probe.NameMatches ? "conforme" : "NON CONFORME")}");
        return probe;
    }

    /// <summary>Adresse corrigée conservant un éventuel port explicite.</summary>
    public static string ApplySuggestedHost(string address, string suggestedHost)
    {
        int separator = address.LastIndexOf(':');
        return separator > 0 && int.TryParse(address[(separator + 1)..], out _)
            ? suggestedHost + address[separator..]
            : suggestedHost;
    }

    public static async Task<RdpLaunchResult> LaunchAsync(
        Profile profile, IList<MonitorInfo> selection, IList<MonitorInfo>? allMonitors, AppConfig? config)
    {
        // 1. Génération et écriture — dernière étape qui touche le contenu.
        string path;
        try
        {
            Paths.EnsureFolders();
            string content = RdpFile.Build(profile, selection, allMonitors, config);
            path = RdpFile.SessionPath(profile.Name.Length > 0 ? profile.Name : profile.Address);
            RdpFile.Write(path, content);
        }
        catch (Exception ex)
        {
            Log.Write($"Génération du .rdp impossible : {ex}");
            return new RdpLaunchResult { Success = false, FailureReason = $"Génération du fichier .rdp impossible : {ex.Message}" };
        }

        // 2. Signature, puis vérification réelle du contenu.
        var signing = await RdpSigningService.SignAndValidateAsync(path);
        if (!signing.Success)
        {
            Log.Write($"LANCEMENT ANNULÉ — signature non valide pour {path} : {signing.FailureReason}");
            return new RdpLaunchResult
            {
                Success = false,
                Path = path,
                Signing = signing,
                FailureReason = signing.FailureReason ?? "La signature du fichier .rdp a échoué."
            };
        }

        // 3. Garde-fou : le fichier doit être exactement celui qui a été signé.
        if (!RdpSigningService.IsUnchangedSince(path, signing.FileSha256AfterSigning))
        {
            Log.Write($"LANCEMENT ANNULÉ — {path} a été modifié après signature.");
            return new RdpLaunchResult
            {
                Success = false,
                Path = path,
                Signing = signing,
                FailureReason = "Le fichier .rdp a été modifié après sa signature : lancement interrompu."
            };
        }

        // 4. Lancement.
        var status = RdpTrust.Status;
        bool trusted = status?.PublisherTrusted == true;
        try
        {
            string mstsc = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System), "mstsc.exe");
            if (!File.Exists(mstsc)) mstsc = "mstsc.exe";

            Log.Write($"Lancement : {path}");
            Log.Write($"  écrans    : {Describe(selection, config)}");
            Log.Write($"  signature : ExitCode {signing.ExitCode}, signscope PRESENT, signature PRESENT");
            Log.Write($"  éditeur   : {(trusted ? "approuvé par stratégie" : "non approuvé — invite mstsc attendue")}");

            Process.Start(new ProcessStartInfo { FileName = mstsc, Arguments = $"\"{path}\"", UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Write($"Démarrage de mstsc.exe : {ex}");
            return new RdpLaunchResult
            {
                Success = false, Path = path, Signing = signing,
                FailureReason = $"Impossible de démarrer mstsc.exe : {ex.Message}"
            };
        }

        return new RdpLaunchResult { Success = true, Path = path, Signing = signing, PublisherTrusted = trusted };
    }

    public static string Describe(IEnumerable<MonitorInfo> selection, AppConfig? config) =>
        string.Join(" + ", selection.Select(m => string.Create(CultureInfo.CurrentCulture,
            $"{m.Order} {m.PositionLabel} [Windows {m.WindowsNumber}, id RDP {RdpFile.EffectiveRdpId(m, config)}]")));
}
