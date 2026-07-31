using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace TermServMultiScreen.Core.Rdp;

/// <summary>État de la chaîne de confiance, tel que l'interface peut l'afficher.</summary>
public sealed class RdpTrustStatus
{
    public RdpCertificateInfo? Certificate { get; init; }
    public RdpSignRecipe? Recipe { get; init; }
    public RdpPolicyState? Policy { get; init; }

    /// <summary>La signature des fichiers .rdp est opérationnelle.</summary>
    public bool CanSign => Certificate is { IsUsable: true } && Recipe is not null;

    /// <summary>Le certificat est déclaré éditeur approuvé : mstsc n'affichera plus d'invite.</summary>
    public bool PublisherTrusted => Policy?.Trusted == true;

    /// <summary>Une élévation administrateur unique reste nécessaire.</summary>
    public bool NeedsProvisioning => CanSign && !PublisherTrusted;
}

/// <summary>
/// Point d'entrée unique de la confiance .rdp : certificat, calibrage de rdpsign, état de la
/// stratégie d'éditeur approuvé. Remplace l'ancien RdpTrustManager appelé en « fire and forget ».
/// </summary>
public static class RdpTrust
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static RdpTrustStatus? _status;

    public static RdpTrustStatus? Status => _status;

    /// <summary>
    /// Prépare la signature. Idempotent, sûr à appeler plusieurs fois, et surtout : attendu
    /// par l'appelant, jamais lancé en tâche de fond ignorée.
    /// </summary>
    public static async Task<RdpTrustStatus> InitializeAsync()
    {
        if (_status is { CanSign: true }) return _status;

        await Gate.WaitAsync();
        try
        {
            if (_status is { CanSign: true }) return _status;

            Log.Write("--- Confiance RDP FlowDesk : initialisation ---");

            var certificate = await RdpCertificateService.GetOrCreateAsync();
            if (certificate is null)
            {
                _status = new RdpTrustStatus();
                Log.Write("Aucun certificat : la signature des fichiers .rdp est indisponible.");
                return _status;
            }

            var recipe = await RdpSigningService.ResolveRecipeAsync(certificate);
            var policy = RdpPublisherTrustService.Read(certificate);

            _status = new RdpTrustStatus { Certificate = certificate, Recipe = recipe, Policy = policy };
            Log.Write(Describe(_status));

            // Le script d'approbation est écrit dès qu'il est utile : un administrateur peut
            // l'exécuter sans passer par l'interface.
            if (_status.NeedsProvisioning)
            {
                string script = RdpPublisherTrustService.WriteProvisioningScript(certificate);
                Log.Write($"Script d'approbation de l'éditeur disponible : {script}");
            }
            return _status;
        }
        catch (Exception ex)
        {
            Log.Write($"Initialisation de la confiance RDP : {ex}");
            _status = new RdpTrustStatus();
            return _status;
        }
        finally { Gate.Release(); }
    }

    /// <summary>Relit tout après une intervention externe (script d'élévation, GPO…).</summary>
    public static async Task<RdpTrustStatus> RefreshAsync()
    {
        _status = null;
        return await InitializeAsync();
    }

    public static string Describe(RdpTrustStatus status)
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== FlowDesk RDP trust ===");
        sb.AppendLine();

        if (status.Certificate is { } certificate)
        {
            sb.AppendLine("Certificate:");
            sb.AppendLine($"  Subject           : {certificate.Subject}");
            sb.AppendLine($"  Store             : {certificate.StorePath}");
            sb.AppendLine($"  Validity          : {certificate.NotBefore:dd/MM/yyyy} → {certificate.NotAfter:dd/MM/yyyy}");
            sb.AppendLine($"  HasPrivateKey     : {certificate.HasPrivateKey}");
            sb.AppendLine($"  Key provider      : {certificate.KeyProvider}");
            sb.AppendLine($"  Key container     : {certificate.KeyContainer}");
            sb.AppendLine($"  Key persistent    : {certificate.KeyIsPersisted}");
            sb.AppendLine($"  Key can sign      : {certificate.KeyCanSign}");
            sb.AppendLine($"  SHA1              : {certificate.Sha1}");
            sb.AppendLine($"  SHA256            : {certificate.Sha256}");
            sb.AppendLine($"  Root store        : {(RdpCertificateService.IsInStore(StoreName.Root, certificate.Sha1) ? "OK" : "ABSENT")}");
            sb.AppendLine($"  TrustedPublisher  : {(RdpCertificateService.IsInStore(StoreName.TrustedPublisher, certificate.Sha1) ? "OK" : "ABSENT")}");
        }
        else sb.AppendLine("Certificate         : AUCUN");

        sb.AppendLine();
        sb.AppendLine($"rdpsign recipe      : {(status.Recipe is null ? "AUCUNE (signature impossible)" : $"{status.Recipe.Switch} + {status.Recipe.HashKind}")}");

        sb.AppendLine();
        if (status.Policy is { } policy)
        {
            sb.AppendLine("Publisher trust policy:");
            sb.AppendLine($"  Expected entry    : {policy.ExpectedEntry}");
            sb.AppendLine($"  Prefix support    : {policy.PrefixSupported}");
            sb.AppendLine($"  HKLM value        : {policy.MachineValue ?? "(absente)"}");
            sb.AppendLine($"  HKCU value        : {policy.UserValue ?? "(absente)"}");
            sb.AppendLine($"  Trusted           : {(policy.Trusted ? "OUI — " + policy.MatchedIn : "NON")}");
            sb.AppendLine($"  Policies writable : {policy.PoliciesKeyWritable}");
            if (!policy.PoliciesKeyWritable && policy.AccessError is not null)
                sb.AppendLine($"  Access detail     : {policy.AccessError}");
        }

        sb.AppendLine();
        sb.Append(status.CanSign
            ? status.PublisherTrusted
                ? "READY — fichiers signés et éditeur approuvé."
                : "SIGNATURE OK — éditeur non encore approuvé : une élévation unique est requise."
            : "NON PRÊT — la signature des fichiers .rdp est impossible.");
        return sb.ToString();
    }
}
