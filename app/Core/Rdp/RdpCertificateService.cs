using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace TermServMultiScreen.Core.Rdp;

/// <summary>
/// Tout ce que l'on sait du certificat de signature, y compris ce que les outils natifs
/// (rdpsign.exe) voient réellement de sa clé privée.
/// </summary>
public sealed class RdpCertificateInfo
{
    public required X509Certificate2 Certificate { get; init; }
    public required string StorePath { get; init; }

    /// <summary>
    /// Empreinte SHA-1 du certificat, c'est-à-dire la propriété « Thumbprint » du magasin.
    /// C'est CETTE valeur que rdpsign.exe attend, y compris derrière l'option /sha256.
    /// </summary>
    public required string Sha1 { get; init; }

    /// <summary>
    /// SHA-256 du certificat encodé DER. Sert à la stratégie TrustedCertThumbprints
    /// (préfixée « sha256: »), jamais à rdpsign.
    /// </summary>
    public required string Sha256 { get; init; }

    public required bool HasPrivateKey { get; init; }
    public required string KeyProvider { get; init; }
    public required string KeyContainer { get; init; }
    public required bool KeyIsPersisted { get; init; }
    public required bool KeyIsMachineKey { get; init; }
    public required bool KeyCanSign { get; init; }
    public required string KeyDetail { get; init; }

    public required bool HasDigitalSignatureUsage { get; init; }
    public required bool HasCodeSigningEku { get; init; }

    public string Subject => Certificate.Subject;
    public DateTime NotBefore => Certificate.NotBefore;
    public DateTime NotAfter => Certificate.NotAfter;
    public bool IsTimeValid => DateTime.Now >= NotBefore && DateTime.Now <= NotAfter;

    /// <summary>Utilisable pour signer un .rdp (hors vérification rdpsign elle-même).</summary>
    public bool IsUsable =>
        HasPrivateKey && KeyCanSign && IsTimeValid && HasDigitalSignatureUsage && HasCodeSigningEku;
}

/// <summary>
/// Cycle de vie du certificat d'éditeur FlowDesk : un seul certificat, persistant, réutilisé
/// d'un lancement à l'autre et d'un redémarrage de Windows à l'autre.
/// </summary>
public static class RdpCertificateService
{
    public const string SubjectName = "CN=FlowDesk RDP Publisher";
    private const string CodeSigningOid = "1.3.6.1.5.5.7.3.3";
    private const string RemoteDesktopAuthOid = "1.3.6.1.4.1.311.54.1.2";

    /// <summary>Sujets utilisés par les versions précédentes du projet, pour la migration.</summary>
    private static readonly string[] LegacySubjects =
    [
        "CN=FlowDesk RDP Publisher",
        "CN=TermServMultiScreen RDP Publisher",
        "CN=FlowDesk",
        "CN=FlowDesk Publisher"
    ];

    private static readonly SemaphoreSlim Gate = new(1, 1);

    public static RdpCertificateInfo? Current { get; private set; }

    /// <summary>
    /// Renvoie le certificat de signature, en le créant seulement s'il n'en existe aucun
    /// d'exploitable. Idempotent : appels répétés = même certificat.
    /// </summary>
    public static async Task<RdpCertificateInfo?> GetOrCreateAsync()
    {
        if (Current is { IsUsable: true }) return Current;

        await Gate.WaitAsync();
        try
        {
            if (Current is { IsUsable: true }) return Current;

            var found = FindBest(out var superseded);
            if (found is not null)
            {
                Log.Write($"Certificat de signature existant réutilisé : {found.Sha1}");
                ReportSuperseded(superseded);
                await Task.Run(() => EnsureLocalTrust(found.Certificate));
                Current = found;
                return Current;
            }

            Log.Write("Aucun certificat de signature exploitable : création d'un nouveau certificat.");
            var created = Create();
            if (created is null) return null;

            await Task.Run(() => EnsureLocalTrust(created.Certificate));
            Current = created;
            Log.Write($"Certificat créé : {created.Sha1}");
            return Current;
        }
        catch (Exception ex)
        {
            Log.Write($"RdpCertificateService : {ex}");
            return null;
        }
        finally
        {
            Gate.Release();
        }
    }

    // ------------------------------------------------------------------ recherche

    /// <summary>
    /// Cherche dans CurrentUser\My — le magasin que rdpsign.exe interroge lorsqu'il tourne
    /// sous le compte de l'utilisateur. Retient le certificat valide le plus récent.
    /// </summary>
    private static RdpCertificateInfo? FindBest(out List<X509Certificate2> superseded)
    {
        superseded = [];
        var candidates = new List<RdpCertificateInfo>();

        using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadOnly);

        foreach (var certificate in store.Certificates)
        {
            if (!LegacySubjects.Contains(certificate.Subject, StringComparer.OrdinalIgnoreCase)) continue;

            var info = Describe(certificate, @"CurrentUser\My");
            if (info.IsUsable) candidates.Add(info);
            else superseded.Add(certificate);
        }

        if (candidates.Count == 0) return null;

        // Le plus récemment émis gagne ; les autres sont signalés comme obsolètes.
        var best = candidates.OrderByDescending(c => c.NotBefore).First();
        superseded.AddRange(candidates.Where(c => c.Sha1 != best.Sha1).Select(c => c.Certificate));
        return best;
    }

    private static void ReportSuperseded(List<X509Certificate2> superseded)
    {
        foreach (var certificate in superseded)
        {
            Log.Write($"Certificat FlowDesk obsolète détecté (non utilisé, non supprimé) : "
                    + $"{certificate.Thumbprint} — {certificate.Subject}, expire le {certificate.NotAfter:dd/MM/yyyy}");
        }
        if (superseded.Count > 0)
        {
            Log.Write("Nettoyage possible depuis Paramètres › Diagnostic RDP si ces certificats ne servent plus.");
        }
    }

    /// <summary>
    /// Supprime les certificats FlowDesk qui ne sont pas le certificat actif, dans les trois
    /// magasins utilisateur. Action explicite : jamais déclenchée automatiquement.
    /// </summary>
    public static int RemoveSuperseded()
    {
        string? keep = Current?.Sha1;
        if (keep is null) return 0;

        int removed = 0;
        foreach (var (name, location) in new[]
                 {
                     (StoreName.My, StoreLocation.CurrentUser),
                     (StoreName.Root, StoreLocation.CurrentUser),
                     (StoreName.TrustedPublisher, StoreLocation.CurrentUser)
                 })
        {
            try
            {
                using var store = new X509Store(name, location);
                store.Open(OpenFlags.ReadWrite);
                foreach (var certificate in store.Certificates)
                {
                    if (!LegacySubjects.Contains(certificate.Subject, StringComparer.OrdinalIgnoreCase)) continue;
                    if (string.Equals(certificate.Thumbprint, keep, StringComparison.OrdinalIgnoreCase)) continue;

                    store.Remove(certificate);
                    removed++;
                    Log.Write($"Certificat obsolète supprimé de {location}\\{name} : {certificate.Thumbprint}");
                }
            }
            catch (Exception ex)
            {
                Log.Write($"Nettoyage {location}\\{name} : {ex.Message}");
            }
        }
        return removed;
    }

    // ------------------------------------------------------------------ description

    /// <summary>Inspecte réellement la clé privée : fournisseur, persistance, capacité à signer.</summary>
    public static RdpCertificateInfo Describe(X509Certificate2 certificate, string storePath)
    {
        string provider = "(aucune clé privée)";
        string container = "";
        string detail = "";
        bool persisted = false;
        bool machineKey = false;
        bool canSign = false;

        if (certificate.HasPrivateKey)
        {
            try
            {
                using var rsa = certificate.GetRSAPrivateKey();
                switch (rsa)
                {
                    case RSACng cng:
                        provider = cng.Key.Provider?.Provider ?? "(fournisseur CNG inconnu)";
                        container = cng.Key.KeyName ?? "";
                        machineKey = cng.Key.IsMachineKey;
                        // Une clé éphémère n'a pas de nom de conteneur : elle serait invisible
                        // pour un processus externe comme rdpsign.exe.
                        persisted = !string.IsNullOrEmpty(container);
                        detail = $"CNG, taille {cng.KeySize} bits, export {cng.Key.ExportPolicy}";
                        break;

                    case RSACryptoServiceProvider csp:
                        provider = csp.CspKeyContainerInfo.ProviderName ?? "(fournisseur CSP inconnu)";
                        container = csp.CspKeyContainerInfo.KeyContainerName ?? "";
                        machineKey = csp.CspKeyContainerInfo.MachineKeyStore;
                        persisted = container.Length > 0;
                        detail = $"CSP, taille {csp.KeySize} bits";
                        break;

                    case null:
                        provider = "(clé privée inaccessible)";
                        break;

                    default:
                        provider = rsa.GetType().Name;
                        detail = $"taille {rsa.KeySize} bits";
                        persisted = true;
                        break;
                }

                if (rsa is not null)
                {
                    // Preuve de capacité : une signature réelle, pas seulement HasPrivateKey.
                    byte[] probe = [0x46, 0x6C, 0x6F, 0x77, 0x44, 0x65, 0x73, 0x6B];
                    byte[] signature = rsa.SignData(probe, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                    canSign = rsa.VerifyData(probe, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                }
            }
            catch (Exception ex)
            {
                detail = $"erreur d'accès à la clé : {ex.Message}";
            }
        }

        bool digitalSignature = false;
        bool codeSigning = false;
        foreach (var extension in certificate.Extensions)
        {
            if (extension is X509KeyUsageExtension keyUsage
                && (keyUsage.KeyUsages & X509KeyUsageFlags.DigitalSignature) != 0)
                digitalSignature = true;

            if (extension is X509EnhancedKeyUsageExtension eku
                && eku.EnhancedKeyUsages.Cast<Oid>().Any(o => o.Value == CodeSigningOid))
                codeSigning = true;
        }

        return new RdpCertificateInfo
        {
            Certificate = certificate,
            StorePath = storePath,
            Sha1 = certificate.Thumbprint.ToUpperInvariant(),
            Sha256 = Convert.ToHexString(SHA256.HashData(certificate.RawData)).ToUpperInvariant(),
            HasPrivateKey = certificate.HasPrivateKey,
            KeyProvider = provider ?? "(inconnu)",
            KeyContainer = container ?? "",
            KeyIsPersisted = persisted,
            KeyIsMachineKey = machineKey,
            KeyCanSign = canSign,
            KeyDetail = detail,
            HasDigitalSignatureUsage = digitalSignature,
            HasCodeSigningEku = codeSigning
        };
    }

    // ------------------------------------------------------------------ création

    /// <summary>
    /// Crée le certificat avec une clé CNG **nommée et persistante** : c'est la condition pour
    /// qu'un processus natif externe (rdpsign.exe) puisse s'en servir après redémarrage.
    /// </summary>
    private static RdpCertificateInfo? Create()
    {
        string keyName = "FlowDesk-RDP-Publisher-" + Guid.NewGuid().ToString("N");
        try
        {
            var parameters = new CngKeyCreationParameters
            {
                Provider = CngProvider.MicrosoftSoftwareKeyStorageProvider,
                KeyCreationOptions = CngKeyCreationOptions.None,   // clé utilisateur persistante
                KeyUsage = CngKeyUsages.Signing,
                ExportPolicy = CngExportPolicies.AllowExport
            };
            parameters.Parameters.Add(new CngProperty("Length", BitConverter.GetBytes(3072), CngPropertyOptions.None));

            using var cngKey = CngKey.Create(CngAlgorithm.Rsa, keyName, parameters);
            using var rsa = new RSACng(cngKey);

            var certificate = BuildSelfSigned(rsa);
            InstallInMyStore(certificate);
            return Describe(ReadBackFromStore(certificate.Thumbprint) ?? certificate, @"CurrentUser\My");
        }
        catch (Exception ex)
        {
            Log.Write($"Création par clé CNG nommée impossible ({ex.Message}) — repli par conteneur PFX.");
            return CreateViaPfx();
        }
    }

    /// <summary>Repli : la clé est persistée en important un PFX avec PersistKeySet.</summary>
    private static RdpCertificateInfo? CreateViaPfx()
    {
        try
        {
            using var rsa = RSA.Create(3072);
            using var ephemeral = BuildSelfSigned(rsa);
            byte[] pfx = ephemeral.Export(X509ContentType.Pfx);

            var certificate = X509CertificateLoader.LoadPkcs12(
                pfx, null,
                X509KeyStorageFlags.PersistKeySet | X509KeyStorageFlags.UserKeySet);

            InstallInMyStore(certificate);
            return Describe(ReadBackFromStore(certificate.Thumbprint) ?? certificate, @"CurrentUser\My");
        }
        catch (Exception ex)
        {
            Log.Write($"Création du certificat impossible : {ex}");
            return null;
        }
    }

    private static X509Certificate2 BuildSelfSigned(RSA key)
    {
        var request = new CertificateRequest(SubjectName, key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(certificateAuthority: false, false, 0, true));
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, critical: true));
        request.CertificateExtensions.Add(
            new X509EnhancedKeyUsageExtension([new Oid(CodeSigningOid), new Oid(RemoteDesktopAuthOid)], critical: false));
        request.CertificateExtensions.Add(
            new X509SubjectKeyIdentifierExtension(request.PublicKey, false));

        // 10 ans : le certificat doit survivre largement aux redémarrages, sans renouvellement surprise.
        return request.CreateSelfSigned(DateTimeOffset.Now.AddMinutes(-5), DateTimeOffset.Now.AddYears(10));
    }

    private static void InstallInMyStore(X509Certificate2 certificate)
    {
        using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadWrite);
        if (store.Certificates.Find(X509FindType.FindByThumbprint, certificate.Thumbprint, false).Count == 0)
        {
            store.Add(certificate);
            Log.Write(@"Certificat installé dans CurrentUser\My.");
        }
    }

    /// <summary>
    /// Relit le certificat depuis le magasin : l'objet obtenu porte alors le lien vers la clé
    /// persistée tel que les outils natifs le verront.
    /// </summary>
    private static X509Certificate2? ReadBackFromStore(string thumbprint)
    {
        using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadOnly);
        var found = store.Certificates.Find(X509FindType.FindByThumbprint, thumbprint, false);
        return found.Count > 0 ? found[0] : null;
    }

    // ------------------------------------------------------------------ confiance locale

    /// <summary>
    /// Installe la seule partie publique dans Root (validité de chaîne) et TrustedPublisher
    /// (éditeur reconnu). La clé privée ne quitte jamais CurrentUser\My.
    /// </summary>
    public static void EnsureLocalTrust(X509Certificate2 certificate)
    {
        byte[] publicOnly = certificate.Export(X509ContentType.Cert);

        foreach (var storeName in new[] { StoreName.Root, StoreName.TrustedPublisher })
        {
            try
            {
                using var store = new X509Store(storeName, StoreLocation.CurrentUser);
                store.Open(OpenFlags.ReadWrite);
                if (store.Certificates.Find(X509FindType.FindByThumbprint, certificate.Thumbprint, false).Count > 0)
                    continue;

                using var publicCertificate = X509CertificateLoader.LoadCertificate(publicOnly);
                store.Add(publicCertificate);
                Log.Write($"Certificat public ajouté à CurrentUser\\{storeName}.");
            }
            catch (Exception ex)
            {
                Log.Write($"Ajout à CurrentUser\\{storeName} : {ex.Message}");
            }
        }
    }

    public static bool IsInStore(StoreName storeName, string sha1)
    {
        try
        {
            using var store = new X509Store(storeName, StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadOnly);
            return store.Certificates.Find(X509FindType.FindByThumbprint, sha1, false).Count > 0;
        }
        catch { return false; }
    }

    /// <summary>Validation de chaîne, avec le détail des statuts en cas d'échec.</summary>
    public static (bool Valid, string Detail) ValidateChain(X509Certificate2 certificate)
    {
        using var chain = new X509Chain();
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;      // certificat auto-signé local
        chain.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;
        chain.ChainPolicy.ApplicationPolicy.Add(new Oid(CodeSigningOid));

        bool valid = chain.Build(certificate);
        string detail = chain.ChainStatus.Length == 0
            ? "aucun statut"
            : string.Join(" | ", chain.ChainStatus.Select(s => $"{s.Status}: {s.StatusInformation.Trim()}"));
        return (valid, detail);
    }
}
