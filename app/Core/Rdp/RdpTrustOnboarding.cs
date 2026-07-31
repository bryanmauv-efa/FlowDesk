using TermServMultiScreen.Services;

namespace TermServMultiScreen.Core.Rdp;

/// <summary>
/// Configuration unique au premier démarrage : propose de déclarer FlowDesk comme éditeur .rdp
/// approuvé, ce qui demande une élévation administrateur — la zone des stratégies de Windows est
/// volontairement en lecture seule pour les comptes standard.
///
/// La proposition n'est faite qu'une fois par certificat : refuser ne déclenche aucun rappel au
/// démarrage suivant, le bouton reste disponible dans Paramètres.
/// </summary>
public static class RdpTrustOnboarding
{
    public static async Task OfferIfNeededAsync()
    {
        try
        {
            var status = await RdpTrust.InitializeAsync();
            if (status.Certificate is null) return;
            if (!status.NeedsProvisioning) return;      // déjà approuvé : rien à demander

            var config = AppServices.Config;
            if (config.RdpTrustPromptDone
                && string.Equals(config.RdpTrustPromptCertificate, status.Certificate.Sha1, StringComparison.OrdinalIgnoreCase))
            {
                Log.Write("Approbation de l'éditeur déjà proposée pour ce certificat : pas de rappel.");
                return;
            }

            // Marqué avant l'invite : un refus ne doit pas revenir à chaque lancement.
            config.RdpTrustPromptDone = true;
            config.RdpTrustPromptCertificate = status.Certificate.Sha1;
            config.Save();

            bool accepted = await DialogService.ConfirmAsync(
                "Configuration unique de FlowDesk",
                "FlowDesk signe les fichiers de connexion qu'il génère. Pour que le Bureau à distance "
                + "reconnaisse cette signature et n'affiche plus d'avertissement d'éditeur, l'empreinte du "
                + "certificat FlowDesk doit être déclarée dans la stratégie Windows des éditeurs .rdp approuvés."
                + Environment.NewLine + Environment.NewLine
                + "Cette zone du registre est réservée aux administrateurs : Windows va demander une "
                + "autorisation, une seule fois."
                + Environment.NewLine + Environment.NewLine
                + "Seule l'empreinte FlowDesk est ajoutée. Aucune vérification n'est désactivée et les "
                + "certificats des serveurs distants ne sont pas concernés."
                + Environment.NewLine + Environment.NewLine
                + $"Empreinte : {RdpPublisherTrustService.BuildEntry(status.Certificate)}",
                "Configurer maintenant", "Plus tard");

            if (!accepted)
            {
                Log.Write("Approbation de l'éditeur reportée par l'utilisateur.");
                return;
            }

            var elevation = await RdpPublisherTrustService.RunProvisioningElevatedAsync(status.Certificate);
            var refreshed = await RdpTrust.RefreshAsync();

            if (refreshed.PublisherTrusted)
            {
                await DialogService.InfoAsync("Configuration terminée",
                    "FlowDesk est déclaré éditeur .rdp approuvé." + Environment.NewLine + Environment.NewLine
                    + $"Empreinte : {refreshed.Policy!.ExpectedEntry}" + Environment.NewLine
                    + $"Emplacement : {refreshed.Policy.MatchedIn}" + Environment.NewLine + Environment.NewLine
                    + "Les connexions générées par FlowDesk s'ouvriront désormais sans avertissement d'éditeur.");
                return;
            }

            string reason = elevation.Cancelled
                ? "L'autorisation administrateur a été refusée."
                : elevation.Completed
                    ? "Le script s'est exécuté mais la stratégie n'est pas encore visible. "
                      + "Une stratégie d'entreprise peut la remplacer."
                    : elevation.Detail;

            await DialogService.InfoAsync("Configuration incomplète",
                reason + Environment.NewLine + Environment.NewLine
                + "Les connexions restent signées et fonctionnelles : le Bureau à distance affichera "
                + "simplement une confirmation d'éditeur à chaque ouverture."
                + Environment.NewLine + Environment.NewLine
                + "Vous pouvez réessayer depuis Paramètres › Confiance des fichiers .rdp, ou faire exécuter "
                + "ce script par un administrateur :" + Environment.NewLine + elevation.ScriptPath);
        }
        catch (Exception ex)
        {
            Log.Write($"Proposition d'approbation de l'éditeur : {ex}");
        }
    }
}
