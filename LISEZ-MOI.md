# Bureau à distance multi-écrans

Ouvre une session Bureau à distance (`mstsc.exe`) sur **les écrans que vous choisissez** — un, deux,
trois, ceux que vous voulez — sans jamais avoir à deviner quel numéro Windows a donné à quel écran.

Application Windows en C# / .NET 10 / WinUI 3 (Windows App SDK), MVVM avec CommunityToolkit.Mvvm
et contrôles du Windows Community Toolkit.

---

## Démarrer

```bash
compilation.bat
```

L'exécutable est produit dans `publication\TermServMultiScreen.exe`. C'est **un seul fichier** autonome (environ 87 Mo). Copiez-le où vous voulez, sur n'importe quel poste Windows 10 (2004+) ou 11 en 64 bits, et double-cliquez.

**Aucun prérequis** : .NET 10, le Windows App SDK, WinUI, l'icône et les XAML compilés sont tous embarqués dans l'exécutable.

Au tout premier lancement, l'exécutable se décompresse dans un cache (`%TEMP%\.net`, environ 200 Mo) : comptez une dizaine de secondes. Les lancements suivants prennent deux à trois secondes.

Trois réglages rendent cela possible et ne doivent pas être retirés du `.csproj` : `EnableMsixTooling` (génère le `resources.pri` qui embarque les XAML), `IncludeAllContentForSelfExtract` et `WindowsAppSdkUndockedRegFreeWinRTInitialize` (activation des types WinRT depuis le cache d'extraction, sans runtime installé).

Au premier lancement l'application est **vide** : aucune connexion, aucun serveur, aucun utilisateur.
Renseignez le serveur et l'utilisateur, choisissez vos écrans, puis **Nouvelle** pour enregistrer —
ou **Importer .rdp** pour reprendre un fichier existant.

Chaque connexion enregistrée écrit son fichier dans
`%LOCALAPPDATA%\TermServMultiScreen\sessions\<nom>.rdp` : c'est ce fichier que `mstsc` ouvre, et il
reste double-cliquable tel quel. Il est mis à jour à chaque **Enregistrer** et supprimé avec la
connexion.

---

## Le problème que ça résout

Un même écran porte **trois numéros différents**, et c'est la source de toutes les erreurs :

| Numérotation | D'où elle vient | Sur ce poste, l'écran de gauche est… |
|---|---|---|
| **Rang gauche → droite** | Position physique réelle, calculée depuis les coordonnées du bureau | **1** |
| **Numéro Windows** | Paramètres › Système › Affichage (`\\.\DISPLAY3`) — suit l'ordre de branchement | **3** |
| **Identifiant RDP** | Ce que `mstsc` attend dans `selectedmonitors`, à partir de 0 | **2** |

L'application n'affiche que le premier (le grand chiffre des cartes), montre le deuxième pour
repère, et calcule le troisième toute seule. Le bouton **Identifier les écrans** affiche un grand
numéro sur chaque écran physique : la vérification se fait à l'œil, sans supposition.

`mstsc.exe` n'a **aucune option de ligne de commande** pour choisir des écrans. La seule voie
officielle est un fichier `.rdp` contenant `use multimon:i:1` et `selectedmonitors:s:…`.
L'application écrit ce fichier puis lance `mstsc` dessus — le bouton **Aperçu du fichier .rdp**
montre exactement ce qui sera envoyé.

---

## Ce que fait l'application

- **Plan des écrans cliquable**, à l'échelle et dans la disposition réelle, classé de gauche à droite.
- **Raccourcis** : tous les écrans, 1 écran, 2 écrans, 3 écrans, écran principal. Le groupe choisi
  privilégie les écrans voisins de l'écran principal, car le Bureau à distance refuse les écrans
  non adjacents.
- **Avertissement avant lancement** si la sélection ne forme pas un bloc contigu.
- **Connexions enregistrées** : serveur, utilisateur, écrans, options — chacune avec son `.rdp` dans
  le dossier des sessions. La sélection d'écrans est mémorisée par identité de port physique, donc
  elle survit aux rebranchements et aux redémarrages.
- **Import de `.rdp` existants** : tous les réglages sont conservés (passerelle RD, imprimante par
  défaut, redirections…), le mot de passe enregistré ne l'est jamais.
- **Détection à chaud** : brancher, débrancher ou réorganiser un écran met la liste à jour sans
  redémarrer l'application.
- **Copier le .rdp sur le Bureau** : produit un fichier double-cliquable qui ouvre directement la
  session sur les écrans choisis, sans passer par l'application.
- **Diagnostic** et **journal** : tout lancement est tracé, avec les trois numérotations.

Les mots de passe ne sont ni demandés, ni affichés, ni enregistrés : c'est Windows qui s'en charge.

---

## Ligne de commande

Utile pour un raccourci sur le Bureau ou la barre des tâches.

```bash
TermServMultiScreen.exe --profile "BN" --screens 1,2 --connect
```

| Paramètre | Effet |
|---|---|
| `--profile "<nom>"` | choisit une connexion enregistrée |
| `--connect` | lance la session immédiatement, sans ouvrir la fenêtre |
| `--screens 1,2` | écrans à utiliser, **numérotés de gauche à droite** |
| `--all` | tous les écrans |
| `--identify [sec]` | affiche un grand numéro sur chaque écran |
| `--list` | rapport de diagnostic des écrans |
| `--print` | affiche le fichier `.rdp` qui serait utilisé |
| `--out <fichier>` | écrit la sortie texte dans un fichier au lieu de l'ouvrir |

---


## Où sont les fichiers

| Quoi | Où |
|---|---|
| Connexions enregistrées | `%LOCALAPPDATA%\TermServMultiScreen\config.json` |
| Fichiers `.rdp` générés | `%LOCALAPPDATA%\TermServMultiScreen\sessions\` |
| Journal | `%LOCALAPPDATA%\TermServMultiScreen\journal.log` |

---

## Organisation du code

```
app\
  Core\          logique sans interface : écrans, fichiers .rdp, configuration, journal
    MonitorInfo.cs      énumération Win32 et les trois numérotations
    RdpFile.cs          fabrication et import des .rdp, lancement de mstsc
    AppConfig.cs        connexions enregistrées (System.Text.Json)
  Services\      composition, surveillance des écrans, dialogues, pastilles d'identification
  ViewModels\    MVVM (CommunityToolkit.Mvvm)
  Pages\         Accueil, Connexions, Sessions, Paramètres, À propos
  Overlays\      fenêtre plein écran d'identification des écrans
AppIcon.ico      icône de l'application (source unique, reprise à chaque compilation)
compilation.bat  compilation de l'exécutable unique et autonome
reinitialiser.cmd  remise à l'état neuf
```

Pour changer l'icône, remplacez `AppIcon.ico` à la racine et recompilez : elle est utilisée pour
l'exécutable, la barre de titre et la page À propos, sans copie à maintenir ailleurs.

`Core\` ne dépend d'aucun élément d'interface : la logique d'écrans et de `.rdp` est testable et
réutilisable telle quelle.

### Choix techniques notables

- Les positions et résolutions viennent de `EnumDisplaySettingsEx` (**DEVMODE**), donc en pixels
  réels et insensibles à la mise à l'échelle Windows ; `EnumDisplayMonitors` sert uniquement à
  retrouver l'ordre d'énumération, celui que `mstsc` utilise pour numéroter les écrans.
- Les fichiers `.rdp` sont écrits en UTF-16LE avec BOM, le format natif de `mstsc`. À la lecture,
  UTF-16, UTF-8 et ANSI sont acceptés.
- La sélection d'écrans est persistée avec trois niveaux de repli : identité du port physique,
  puis nom `\\.\DISPLAYn`, puis rang gauche → droite.
- Le thème est fixé avant la création de la fenêtre, sinon les boutons Réduire / Agrandir / Fermer
  restent invisibles quelques secondes au démarrage.

---

## Version précédente (WinForms)

`src\` contient la première version, en WinForms. Elle fonctionne toujours et rend le même service ;
la version WinUI 3 la remplace.
