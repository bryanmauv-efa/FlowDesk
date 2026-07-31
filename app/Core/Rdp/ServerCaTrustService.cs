using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace TermServMultiScreen.Core.Rdp;

/// <summary>Ce que le serveur RDP présente réellement comme certificat TLS.</summary>
public sealed class ServerCertificateProbe
{
    public required string Host { get; init; }
    public required bool Reached { get; init; }
    public string? Error { get; init; }

    public string Subject { get; init; } = "";
    public string Issuer { get; init; } = "";
    public string Sha1 { get; init; } = "";
    public DateTime NotBefore { get; init; }
    public DateTime NotAfter { get; init; }
    public List<string> DnsNames { get; init; } = [];

    public bool NameMatches { get; init; }
    public bool ChainTrusted { get; init; }
    public bool RevocationUnknown { get; init; }
    public string ChainDetail { get; init; } = "";

    /// <summary>Nom porté par le certificat, à utiliser comme adresse de connexion.</summary>
    public string? SuggestedHost => DnsNames.Count > 0 ? DnsNames[0] : null;

    public string Summary
    {
        get
        {
            if (!Reached) return $"serveur injoignable : {Error}";
            List<string> problems = [];
            if (!NameMatches) problems.Add($"le certificat ne couvre pas « {Host} »");
            if (!ChainTrusted) problems.Add("autorité émettrice non approuvée sur ce poste");
            if (RevocationUnknown) problems.Add("révocation non vérifiable");
            return problems.Count == 0
                ? "certificat du serveur vérifié : aucun avertissement attendu"
                : string.Join(" · ", problems);
        }
    }
}

public sealed record CaImportCandidate(
    X509Certificate2 Certificate, string Subject, string Issuer, string Sha1, string Sha256,
    DateTime NotBefore, DateTime NotAfter, bool IsCertificateAuthority, bool AlreadyTrusted);

/// <summary>
/// Confiance envers l'autorité qui a émis le certificat TLS du serveur RDP.
///
/// Rien n'est désactivé ici : on installe le certificat public d'une autorité dans
/// CurrentUser\Root, ce qui est le mécanisme Windows prévu et ne demande aucun droit
/// administrateur. L'empreinte est toujours affichée avant installation, car approuver une
/// autorité revient à faire confiance à tout ce qu'elle émet.
/// </summary>
public static class ServerCaTrustService
{
    /// <summary>
    /// Récupère le certificat présenté par un serveur RDP. Le protocole RDP échange d'abord un
    /// X.224 Connection Request annonçant TLS, puis bascule en TLS : on s'arrête juste après la
    /// poignée de main, aucune donnée d'authentification n'est envoyée.
    /// </summary>
    public static async Task<ServerCertificateProbe> ProbeAsync(string host, int port = 3389, int timeoutMs = 10000)
    {
        host = (host ?? "").Trim();
        if (host.Length == 0)
            return new ServerCertificateProbe { Host = "", Reached = false, Error = "aucune adresse de serveur" };

        // Une adresse peut porter un port explicite (serveur:3390).
        int separator = host.LastIndexOf(':');
        if (separator > 0 && int.TryParse(host[(separator + 1)..], out int explicitPort))
        {
            port = explicitPort;
            host = host[..separator];
        }

        byte[]? leafRaw = null;
        var chainRaw = new List<byte[]>();
        SslPolicyErrors policyErrors = SslPolicyErrors.None;
        string chainDetail = "";
        bool revocationUnknown = false;

        try
        {
            using var client = new TcpClient();
            using var cancellation = new CancellationTokenSource(timeoutMs);
            await client.ConnectAsync(host, port, cancellation.Token);

            var stream = client.GetStream();

            // TPKT + X.224 CR + RDP_NEG_REQ (protocoles demandés : TLS | CredSSP).
            byte[] negotiation =
            [
                0x03, 0x00, 0x00, 0x13, 0x0E, 0xE0, 0x00, 0x00, 0x00, 0x00, 0x00,
                0x01, 0x00, 0x08, 0x00, 0x03, 0x00, 0x00, 0x00
            ];
            await stream.WriteAsync(negotiation, cancellation.Token);
            await stream.FlushAsync(cancellation.Token);

            // Réponse TPKT : 4 octets d'en-tête dont la longueur totale, puis le reste.
            byte[] header = new byte[4];
            await stream.ReadExactlyAsync(header, cancellation.Token);
            int length = (header[2] << 8) | header[3];
            if (length > 4)
            {
                byte[] rest = new byte[length - 4];
                await stream.ReadExactlyAsync(rest, cancellation.Token);
            }

            using var ssl = new SslStream(stream, leaveInnerStreamOpen: false,
                (_, certificate, chain, errors) =>
                {
                    // On accepte uniquement pour inspecter : la connexion est refermée
                    // immédiatement après et aucune information d'identification ne circule.
                    policyErrors = errors;
                    if (certificate is not null) leafRaw = certificate.GetRawCertData();
                    if (chain is not null)
                    {
                        foreach (var element in chain.ChainElements)
                            chainRaw.Add(element.Certificate.GetRawCertData());

                        chainDetail = chain.ChainStatus.Length == 0
                            ? "aucun statut"
                            : string.Join(" | ", chain.ChainStatus.Select(s => s.Status.ToString()));
                        revocationUnknown = chain.ChainStatus.Any(s =>
                            s.Status is X509ChainStatusFlags.RevocationStatusUnknown or X509ChainStatusFlags.OfflineRevocation);
                    }
                    return true;
                });

            await ssl.AuthenticateAsClientAsync(host);
        }
        catch (Exception ex) when (leafRaw is null)
        {
            Log.Write($"Analyse du certificat de {host}:{port} : {ex.Message}");
            return new ServerCertificateProbe { Host = host, Reached = false, Error = ex.Message };
        }
        catch (Exception ex)
        {
            // Le certificat a été capturé : l'échec postérieur n'empêche pas le diagnostic.
            Log.Write($"Analyse de {host} interrompue après la poignée de main : {ex.Message}");
        }

        if (leafRaw is null)
            return new ServerCertificateProbe { Host = host, Reached = false, Error = "aucun certificat présenté" };

        using var leaf = X509CertificateLoader.LoadCertificate(leafRaw);
        var dnsNames = ReadDnsNames(leaf);

        var probe = new ServerCertificateProbe
        {
            Host = host,
            Reached = true,
            Subject = leaf.Subject.Length > 0 ? leaf.Subject : "(sujet vide)",
            Issuer = leaf.Issuer,
            Sha1 = leaf.Thumbprint.ToUpperInvariant(),
            NotBefore = leaf.NotBefore,
            NotAfter = leaf.NotAfter,
            DnsNames = dnsNames,
            NameMatches = !policyErrors.HasFlag(SslPolicyErrors.RemoteCertificateNameMismatch),
            ChainTrusted = !policyErrors.HasFlag(SslPolicyErrors.RemoteCertificateChainErrors),
            RevocationUnknown = revocationUnknown,
            ChainDetail = chainDetail.Length > 0 ? chainDetail : policyErrors.ToString()
        };

        Log.Write($"Certificat serveur {host} : émis par {probe.Issuer}, SAN [{string.Join(", ", dnsNames)}], "
                + $"nom correspondant : {probe.NameMatches}, chaîne approuvée : {probe.ChainTrusted}, "
                + $"révocation inconnue : {probe.RevocationUnknown}");
        return probe;
    }

    private static List<string> ReadDnsNames(X509Certificate2 certificate)
    {
        List<string> names = [];
        foreach (var extension in certificate.Extensions)
        {
            if (extension is X509SubjectAlternativeNameExtension san)
            {
                names.AddRange(san.EnumerateDnsNames());
            }
            else if (extension.Oid?.Value == "2.5.29.17")
            {
                // Repli : lecture textuelle si le type fort n'est pas disponible.
                foreach (string line in extension.Format(true).Split('\n'))
                {
                    int equals = line.IndexOf('=');
                    if (equals > 0 && line.Contains("DNS", StringComparison.OrdinalIgnoreCase))
                        names.Add(line[(equals + 1)..].Trim());
                }
            }
        }
        return names.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    // ------------------------------------------------------------------ import

    /// <summary>
    /// Lit un fichier d'autorité (.cer, .crt, .der, .p7b) et décrit ce qu'il contient, SANS rien
    /// installer : l'appelant doit faire valider l'empreinte avant d'appeler <see cref="Install"/>.
    /// </summary>
    public static (List<CaImportCandidate> Candidates, string? Error) Inspect(string path)
    {
        try
        {
            var collection = LoadCertificates(path);
            if (collection.Count == 0)
                return ([], "Le fichier ne contient aucun certificat.");

            List<CaImportCandidate> candidates = [];
            foreach (var certificate in collection)
            {
                bool isCa = IsCertificateAuthority(certificate);
                candidates.Add(new CaImportCandidate(
                    certificate,
                    certificate.Subject,
                    certificate.Issuer,
                    certificate.Thumbprint.ToUpperInvariant(),
                    Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(certificate.RawData)).ToUpperInvariant(),
                    certificate.NotBefore,
                    certificate.NotAfter,
                    isCa,
                    RdpCertificateService.IsInStore(StoreName.Root, certificate.Thumbprint)));
            }
            return (candidates, null);
        }
        catch (Exception ex)
        {
            return ([], ex.Message);
        }
    }

    /// <summary>
    /// Lit un fichier de certificats quel que soit son format : DER brut (.cer, .crt, .der),
    /// PEM en base64, ou conteneur PKCS#7 (.p7b) qui porte souvent toute la chaîne.
    /// </summary>
    private static X509Certificate2Collection LoadCertificates(string path)
    {
        var collection = new X509Certificate2Collection();
        byte[] bytes = File.ReadAllBytes(path);

        // PEM : peut contenir plusieurs certificats.
        try
        {
            string text = Encoding.UTF8.GetString(bytes);
            if (text.Contains("-----BEGIN CERTIFICATE-----", StringComparison.Ordinal))
            {
                collection.ImportFromPem(text);
                if (collection.Count > 0) return collection;
            }
        }
        catch (Exception ex) { Log.Write($"Lecture PEM de {Path.GetFileName(path)} : {ex.Message}"); }

        // DER : un seul certificat.
        try
        {
            collection.Add(X509CertificateLoader.LoadCertificate(bytes));
            return collection;
        }
        catch { /* format suivant */ }

        // PKCS#7 : conteneur de chaîne.
        try
        {
            var cms = new System.Security.Cryptography.Pkcs.SignedCms();
            cms.Decode(bytes);
            collection.AddRange(cms.Certificates);
        }
        catch (Exception ex) { Log.Write($"Lecture PKCS#7 de {Path.GetFileName(path)} : {ex.Message}"); }

        return collection;
    }

    /// <summary>Une autorité : contrainte de base CA=true, ou racine auto-signée signant des certificats.</summary>
    private static bool IsCertificateAuthority(X509Certificate2 certificate)
    {
        bool selfSigned = string.Equals(certificate.Subject, certificate.Issuer, StringComparison.OrdinalIgnoreCase);
        foreach (var extension in certificate.Extensions)
        {
            if (extension is X509BasicConstraintsExtension basic && basic.CertificateAuthority) return true;
            if (extension is X509KeyUsageExtension usage
                && (usage.KeyUsages & X509KeyUsageFlags.KeyCertSign) != 0) return true;
        }
        return selfSigned;
    }

    /// <summary>
    /// Installe le certificat public dans CurrentUser\Root. Aucun droit administrateur requis :
    /// la confiance est limitée au compte de l'utilisateur courant.
    /// </summary>
    public static (bool Success, string Detail) Install(CaImportCandidate candidate)
    {
        try
        {
            using var store = new X509Store(StoreName.Root, StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadWrite);

            if (store.Certificates.Find(X509FindType.FindByThumbprint, candidate.Sha1, false).Count > 0)
                return (true, "Cette autorité était déjà approuvée pour votre compte.");

            // Seule la partie publique est installée : un fichier d'autorité n'a pas de clé privée.
            using var publicOnly = X509CertificateLoader.LoadCertificate(candidate.Certificate.RawData);
            store.Add(publicOnly);

            Log.Write($"Autorité installée dans CurrentUser\\Root : {candidate.Subject} "
                    + $"(SHA-1 {candidate.Sha1}, valide jusqu'au {candidate.NotAfter:dd/MM/yyyy})");
            return (true, $"Autorité approuvée pour votre compte : {candidate.Subject}");
        }
        catch (Exception ex)
        {
            Log.Write($"Installation de l'autorité : {ex.Message}");
            return (false, ex.Message);
        }
    }

    /// <summary>Retire une autorité approuvée par erreur (magasin utilisateur uniquement).</summary>
    public static (bool Success, string Detail) Remove(string sha1)
    {
        try
        {
            using var store = new X509Store(StoreName.Root, StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadWrite);
            var found = store.Certificates.Find(X509FindType.FindByThumbprint, sha1, false);
            if (found.Count == 0) return (false, "Autorité absente du magasin utilisateur.");

            foreach (var certificate in found) store.Remove(certificate);
            Log.Write($"Autorité retirée de CurrentUser\\Root : {sha1}");
            return (true, "Autorité retirée.");
        }
        catch (Exception ex) { return (false, ex.Message); }
    }

    /// <summary>Texte à faire valider avant installation.</summary>
    public static string DescribeForConfirmation(CaImportCandidate candidate)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Vérifiez ces informations auprès de votre service informatique avant de continuer.");
        sb.AppendLine();
        sb.AppendLine($"Autorité   : {candidate.Subject}");
        sb.AppendLine($"Émise par  : {candidate.Issuer}");
        sb.AppendLine($"Validité   : {candidate.NotBefore:dd/MM/yyyy} → {candidate.NotAfter:dd/MM/yyyy}");
        sb.AppendLine();
        sb.AppendLine($"Empreinte SHA-1   : {candidate.Sha1}");
        sb.AppendLine($"Empreinte SHA-256 : {candidate.Sha256}");
        sb.AppendLine();
        sb.AppendLine("Approuver une autorité signifie faire confiance à tous les certificats qu'elle émet. "
                    + "N'installez ce fichier que si l'empreinte correspond à celle communiquée par votre "
                    + "service informatique.");
        sb.AppendLine();
        sb.Append("L'installation se limite à votre compte Windows (CurrentUser\\Root) et ne demande aucun "
                + "droit administrateur.");
        return sb.ToString();
    }
}
