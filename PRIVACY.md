# Confidentialité de FlowDesk

Cette description concerne le code actuellement présent dans le dépôt. FlowDesk
ne contient pas de service de télémétrie ou de publicité propre au projet.

## Données locales

Les connexions (adresses des serveurs, noms d'utilisateurs, options et choix d'écrans),
les fichiers RDP et les journaux sont stockés dans `%LOCALAPPDATA%\FlowDesk`.
Les anciennes données sont migrées au premier lancement. Les journaux peuvent contenir
le nom du poste, le compte Windows, les chemins, les versions et les détails d'erreurs.
Ils ne sont pas envoyés automatiquement aux mainteneurs. Vérifier et masquer les
informations internes avant de publier un journal dans une issue GitHub.

FlowDesk ne demande pas le mot de passe RDP : l'authentification est gérée par le client
Windows. Les profils importés, leurs options et les codes de partage peuvent néanmoins
contenir des informations sensibles ; ne les publier qu'après vérification.

## Communications réseau

- Au démarrage de l'interface, FlowDesk interroge automatiquement l'API GitHub pour la
  dernière release. GitHub reçoit notamment l'adresse IP et les informations habituelles
  de la requête HTTP. Aucun profil de connexion n'est joint à cette requête.
- Après accord pour une mise à jour portable, l'application télécharge l'EXE et sa
  signature depuis GitHub. Une installation MSI ouvre la page des releases après accord.
- Lors d'une connexion demandée par l'utilisateur, `mstsc.exe` communique avec le serveur
  et les éventuelles passerelles configurés. Les redirections (presse-papiers, lecteurs,
  imprimantes et audio) suivent les options choisies.
- L'analyse d'un certificat serveur contacte uniquement l'hôte/port indiqué par
  l'utilisateur ; il ne s'agit pas d'un balayage de réseau. Les validations de certificats
  Windows peuvent contacter les services des autorités de certification.
- Le code de partage est fabriqué localement et transmis par le moyen choisi par
  l'utilisateur. FlowDesk n'héberge pas de serveur de partage.

Services tiers : [confidentialité GitHub](https://docs.github.com/en/site-policy/privacy-policies/github-general-privacy-statement)
et [confidentialité Microsoft](https://privacy.microsoft.com/privacystatement).
Les composants Windows et les runtimes Microsoft ont leurs propres conditions.

La version actuelle ne propose pas encore de réglage désactivant la vérification
GitHub au démarrage. Ce point est déclaré dans le dossier de candidature SignPath,
et doit être résolu ou accepté explicitement avant la signature de production.

## Modifications du système et suppression

Le MSI installe FlowDesk pour l'utilisateur courant et ajoute des raccourcis. Il se
désinstalle depuis les applications Windows ; les données personnelles sont conservées.
Après fermeture de FlowDesk, leur suppression est possible via le script
`reinitialiser.cmd` (réinitialisation des connexions) ou la suppression volontaire du
dossier de données (y compris les journaux).

Pour signer les fichiers RDP, FlowDesk peut créer une clé et un certificat utilisateur
persistants, et ajouter ce certificat aux magasins utilisateur de confiance. Cette
initialisation est actuellement automatique. L'ajout de l'empreinte dans la stratégie
machine des éditeurs RDP demande une confirmation et une élévation Windows distinctes.
L'import d'une autorité de certification de serveur est une action explicite.
La désinstallation MSI ne retire pas automatiquement ces certificats ni les stratégies.
La présentation préalable des changements de confiance est un point de revue SignPath.
