# Composants tiers et revue des licences

La GPL de FlowDesk ne remplace pas les licences des dépendances. L'affirmation du
mainteneur concernant son code original et ses ressources ne dispense pas de cette revue.

## Constat de préparation (20 novembre 2026)

| Composants | Métadonnées / texte observés | Traitement |
| --- | --- | --- |
| CommunityToolkit.Mvvm, contrôles WinUI et dépendances CommunityToolkit | MIT | Conserver les notices des paquets |
| System.Security.Cryptography.Pkcs et System.Numerics.Tensors | MIT | Conserver les notices des paquets |
| Microsoft.WindowsAppSDK et plusieurs sous-paquets | Conditions Microsoft dans `license.txt` | Revue de redistribution et de l'exception bibliothèques système nécessaire |
| Microsoft.WindowsAppSDK.WinUI 2.3.0 | Texte intitulé « MICROSOFT WINDOWS APP SDK ENGINEERING PREVIEW » | Mention interdisant l'environnement de production sauf autre accord Microsoft : à clarifier/remplacer avant une signature de production |
| Windows ML et composants AI transitifs | Conditions Microsoft, pas une simple déclaration MIT | Examiner les composants réellement embarqués ; ne pas les présenter comme entièrement open source |
| Microsoft.Web.WebView2 | Licence de redistribution dans `LICENSE.txt` | Conserver le texte et vérifier les fichiers distribués |
| Windows SDK BuildTools / MSIX | Conditions Windows SDK / Microsoft | Outils de construction ; distinguer des binaires livrés |
| Runtime .NET, WinUI natif et runtime VC++ embarqués | Notices des distributions concernées | Inventorier les fichiers réellement publiés et leurs droits de redistribution |
| WiX 6.0.2 | Outil de génération du MSI | Vérifier ses conditions applicables ; l'outil lui-même n'est pas livré comme application |

Le fichier `dependencies.json` et le dossier `dependency-licenses` produits par la CI
restituent les métadonnées NuGet et les textes de licences disponibles. Ils constituent
un inventaire de dépendances résolues, pas une certification de compatibilité GPL ni
une liste exhaustive de chaque binaire natif livré. Les textes manquants sont signalés.

**Éligibilité SignPath non établie.** Faire confirmer le traitement des bibliothèques
système et les conditions de redistribution avant d'activer `SIGNPATH_READY`.
Ne pas supprimer une notice ou qualifier une dépendance de MIT sans vérification.

Sources : [conditions SignPath](https://signpath.org/terms), fichiers `.nuspec` et licences
des paquets restaurés depuis NuGet. Les versions exactes figurent dans l'inventaire CI.
