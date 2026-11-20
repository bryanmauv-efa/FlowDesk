# Code signing policy

## Statut

Le projet prépare une candidature au programme gratuit de SignPath Foundation.
Il n'est pas encore accepté et ne prétend pas disposer d'un certificat SignPath.
Après acceptation effective, la mention suivante sera activée sur la page d'accueil
et dans les notes de chaque release signée :

> Free code signing provided by [SignPath.io](https://signpath.io), certificate by [SignPath Foundation](https://signpath.org).

Le titulaire des droits du code original est efa. Le certificat du programme gratuit
sera au nom de SignPath Foundation. Aucun certificat privé de signature n'est publié.

## Responsabilités

| Rôle | Responsable |
| --- | --- |
| Auteur / mainteneur autorisé à modifier les sources | [bryanmauv-efa](https://github.com/bryanmauv-efa) |
| Revue des contributions externes | [bryanmauv-efa](https://github.com/bryanmauv-efa) |
| Approbation de chaque demande de signature | [bryanmauv-efa](https://github.com/bryanmauv-efa) |

Le mainteneur indique avoir activé la double authentification GitHub. Elle sera aussi
obligatoire sur SignPath. Toute contribution externe doit être relue avant fusion.
Chaque release nécessite une approbation humaine dans SignPath ; le jeton CI sera
limité à la soumission et ne devra pas permettre d'approuver une demande.

## Origine et périmètre des fichiers

Les demandes portent uniquement sur les fichiers FlowDesk construits à partir du dépôt
public par le workflow Windows hébergé par GitHub. Une pull request ne peut pas demander
de signature. La demande est réservée au lancement manuel du workflow sur `main`, après
activation explicite de la configuration SignPath.

La configuration prévue signe l'EXE portable, l'EXE FlowDesk contenu dans le MSI, puis
le MSI. Elle ne resigne pas les DLL des fournisseurs tiers. Les contrôles de nom de
produit, d'entreprise et de version doivent être activés côté SignPath.

Avant publication : vérifier le commit, la compilation, les tests, les licences des
composants embarqués, la validité des signatures, l'horodatage et les empreintes finales.
Mettre les sources correspondantes et les notices à disposition avec les binaires.

## Confidentialité

Voir [PRIVACY.md](PRIVACY.md). FlowDesk vérifie actuellement les releases GitHub au
démarrage. Il ne faut donc pas déclarer une absence totale de communications automatiques.
Les conditions d'admission relatives au consentement et aux changements des magasins
de certificats doivent être examinées avant activation de la signature de production.

## Deux signatures différentes

La signature Authenticode SignPath ne remplace pas le fichier RSA `FlowDesk.exe.sig`
utilisé par l'ancien mécanisme de mise à jour portable. Une signature RSA doit être
produite **après** Authenticode avec la clé privée correspondant à la clé déjà intégrée
à l'application. Cette clé n'est pas disponible actuellement : la mise à jour reste
manuelle. Ne jamais désactiver la vérification RSA pour contourner cette absence.
