using System.Security.Cryptography.X509Certificates;

namespace FlowDesk.Core.Rdp;

/// <summary>MSI setup runs in the installing user's context, never in SYSTEM's certificate store.</summary>
public static class RdpInstaller
{
    public static async Task<int> RunAsync(bool interactive)
    {
        try
        {
            Log.Write("MSI : préparation du certificat et de l'éditeur RDP.");
            if (!interactive && !RdpCertificateService.HasTrustedCertificate())
                throw new InvalidOperationException("Installation silencieuse : le certificat RDP doit être préconfiguré pour cet utilisateur. Effectuez une première installation interactive.");

            var status = await RdpTrust.InitializeAsync();
            var certificate = status.Certificate;
            if (!status.CanSign || certificate is null
                || !RdpCertificateService.IsInStore(StoreName.Root, certificate.Sha1)
                || !RdpCertificateService.IsInStore(StoreName.TrustedPublisher, certificate.Sha1))
                throw new InvalidOperationException("Le certificat RDP n'a pas pu être installé ou validé.");

            if (!status.PublisherTrusted)
            {
                var result = RdpPublisherTrustService.TryConfigure(certificate);
                if (result.Outcome == RdpPublisherTrustService.ConfigureOutcome.NeedsElevation && interactive)
                {
                    var elevated = await RdpPublisherTrustService.RunProvisioningElevatedAsync(certificate);
                    if (!elevated.Completed) throw new InvalidOperationException(elevated.Detail);
                }
                status = await RdpTrust.RefreshAsync();
                if (!status.PublisherTrusted)
                    throw new InvalidOperationException("L'approbation de l'éditeur RDP nécessite une autorisation administrateur. " + result.Detail);
            }
            Log.Write("MSI : certificat RDP installé, signature vérifiée et éditeur approuvé.");
            return 0;
        }
        catch (Exception ex)
        {
            Log.Write($"MSI : échec de préparation du certificat RDP : {ex}");
            if (interactive)
                NativeMethods.MessageBox(0, ex.Message + "\n\nJournal : " + Paths.LogFile,
                    "FlowDesk — installation du certificat", NativeMethods.MB_ICONERROR);
            return 1;
        }
    }
}
