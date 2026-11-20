# Candidature SignPath Foundation — FlowDesk

## Choix confirmés par le mainteneur

- Titulaire des droits : efa ; autorisation open source confirmée par le mainteneur.
- Licence du code original : GPL-3.0-only, sans double licence commerciale.
- Auteur, relecteur et approbateur : Bryan, compte GitHub `bryanmauv-efa`.
- Double authentification GitHub déclarée active ; à activer aussi sur SignPath.
- Certificat au nom de SignPath Foundation accepté.
- Compilation automatique GitHub Actions acceptée.

## Texte de candidature à relire et copier

Project: FlowDesk

Repository: https://github.com/bryanmauv-efa/FlowDesk

License: GNU GPL version 3 only (original project code; third-party notices documented separately).

Maintainer, reviewer and signing approver: https://github.com/bryanmauv-efa

Description: FlowDesk is a Windows desktop application that launches the Windows Remote
Desktop client with a user-selected set of monitors. It manages local connection profiles,
generates RDP files and provides local diagnostics. Distribution formats are a standalone
x64 EXE and a per-user MSI installer. It does not scan networks for vulnerabilities.

Build: GitHub-hosted Windows Actions build from this public repository. Sources, license
inventory, commit reference, EXE and MSI are attached to each build. Signing is disabled
until acceptance. Each signing request will require manual approval by the maintainer.

Disclosure for review: The app automatically checks GitHub releases at startup and currently
does not offer a setting to disable that check. It initializes an RDP publisher certificate
in the current user's certificate stores; machine-wide publisher trust requires explicit
confirmation/elevation. Please review the privacy/system-change policy and the Microsoft
runtime dependency licenses, including the Engineering Preview text found in the resolved
WinUI package. We are not claiming that these points are already approved or resolved.

Policies: README.md, CODE_SIGNING_POLICY.md, PRIVACY.md and THIRD_PARTY_NOTICES.md in the repository.

## Avant de déposer la demande

1. Relire les documents et remplacer « efa » par la raison sociale exacte si nécessaire.
2. Faire examiner les licences Microsoft signalées dans `THIRD_PARTY_NOTICES.md`, notamment
   le texte Engineering Preview du paquet WinUI. Confirmer les droits de redistribution
   des binaires réellement livrés et leur traitement par SignPath/GPL.
3. Traiter les points consentement : information préalable sur les certificats ajoutés,
   et contrôle de la vérification GitHub automatique. Ne pas cocher une déclaration de
   conformité absolue tant que ces points ne sont pas résolus.
4. Fusionner la préparation et obtenir une compilation verte sur GitHub Actions.
5. Ouvrir https://signpath.org/apply et remplir la demande avec les informations ci-dessus.
   L'envoi et l'acceptation des conditions appartiennent au titulaire/mainteneur.

L'ancienneté et la réputation du projet sont également examinées. La gratuité ne résulte
pas automatiquement de l'ajout d'une licence ou d'un workflow.

## Après acceptation

1. Activer la double authentification SignPath, inviter le responsable, installer
   l'application GitHub SignPath avec accès limité au dépôt FlowDesk.
2. Configurer le système de compilation de confiance GitHub.com, le projet FlowDesk,
   une politique de signature avec approbation humaine, et les restrictions sur le dépôt,
   le workflow et la branche `main`. Ne pas donner le rôle approbateur au jeton CI.
3. Importer `signing/signpath-artifact.xml`. Valider dans SignPath le chemin de l'EXE
   contenu dans le MSI à partir d'un artefact réel. Confirmer l'horodatage et les contrôles
   de métadonnées. Le XML est une configuration candidate, pas une configuration déjà
   acceptée par le service.
4. Dans GitHub → Settings → Secrets and variables → Actions, enregistrer le secret
   `SIGNPATH_API_TOKEN` (jeton de soumission uniquement). Ne pas le mettre dans un fichier,
   une issue, un message ou les logs.
5. Ajouter les variables `SIGNPATH_ORGANIZATION_ID`, `SIGNPATH_PROJECT_SLUG`,
   `SIGNPATH_POLICY_SLUG`, `SIGNPATH_ARTIFACT_SLUG`. Passer `SIGNPATH_READY` à `true`
   uniquement après acceptation et résolution des points de revue.
6. Lancer `Windows build` manuellement sur `main` avec `sign=true`, puis approuver dans
   SignPath. Sans approbation, le workflow n'a pas de voie de contournement.
7. Tester l'EXE portable signé et renommé, l'installation MSI, l'EXE installé et la
   désinstallation sur un poste de test. Vérifier également la signature de l'EXE extrait
   du MSI. Publier les fichiers de l'artefact **signed**, les sources correspondantes,
   les notices et les empreintes recalculées après signature.
8. Ajouter la mention SignPath effective à la page d'accueil et un lien « Code signing
   policy » dans chaque release signée. Ne pas prétendre que les anciennes releases
   étaient signées et ne pas remplacer leurs fichiers silencieusement.

Le workflow ne publie pas de release et ne modifie pas les paramètres de sécurité GitHub.
La signature RSA `.sig` des mises à jour portables reste un mécanisme distinct à rétablir.

## Sources

- https://signpath.org/terms
- https://docs.signpath.io/trusted-build-systems/github
- https://docs.signpath.io/artifact-configuration/examples
- https://www.gnu.org/licenses/gpl-3.0.html
