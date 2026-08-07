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
- **Déplacer une session en cours** d'un écran à l'autre — voir ci-dessous.
- **Diagnostic** et **journal** : tout lancement est tracé, avec les trois numérotations.

Les mots de passe ne sont ni demandés, ni affichés, ni enregistrés : c'est Windows qui s'en charge.

---

## Déplacer une session en cours d'utilisation

Le geste suffit : **quittez le plein écran, faites glisser la fenêtre sur un autre écran, lâchez.**
Elle s'y remet en plein écran toute seule. Rien à cliquer dans l'application, aucun raccourci à
connaître.

Deux choses rendent ça possible.

### 1. `maximizetocurrentdisplays:i:1` — la cause du problème

Cette ligne du fichier `.rdp` décide où le plein écran atterrit quand on le rétablit en cours de
session :

| Valeur | Comportement |
|---|---|
| `0` | `mstsc` revient **toujours** sur les écrans de `selectedmonitors`. On peut déplacer la fenêtre à la main : elle repart sur l'écran de départ dès qu'on la remet en plein écran. |
| `1` | le plein écran se fait sur l'écran **où la fenêtre se trouve**. |

L'application écrit désormais `1`. Les écrans choisis ne changent pas pour autant : la session
s'ouvre toujours sur ceux de `selectedmonitors`. Comme le réglage est lu à la connexion, il prend
effet à la **prochaine** ouverture de session.

### 2. Le plein écran automatique

Le réglage ci-dessus corrige `mstsc` pour les prochaines connexions ; l'application, elle, agit
tout de suite — y compris sur une session déjà ouverte. Dès qu'une fenêtre de session est **posée**
sur un autre écran, elle est mise en plein écran sur cet écran.

Deux garde-fous évitent toute surprise : on n'agit qu'une fois la fenêtre immobile **et** le bouton
de la souris relâché, et seulement si l'écran a réellement changé — déplacer une fenêtre à
l'intérieur de son écran ne déclenche rien. L'interrupteur *Plein écran automatique sur l'écran
d'arrivée* (page **Sessions**, activé par défaut) le désactive au besoin.

### En secours : une session qu'on ne peut pas attraper du tout

Une session en plein écran n'a ni bordure ni barre de titre : la souris n'a rien à saisir. La page
**Sessions** la déplace quand même :

- **boutons `1 · Gauche`, `2 · Centre`, `3 · Droite`…** : un bouton par écran, dans le même ordre
  que le plan de la page d'accueil ;
- **flèches précédent / suivant** : l'écran voisin, en faisant le tour ;
- **raccourcis** `Ctrl + Alt + Maj + ← / →` et `Ctrl + Alt + Maj + 1…9`, qui agissent sur la session
  au premier plan — mais **seulement quand la fenêtre de l'application a le focus** : une session
  Bureau à distance active capte le clavier avant Windows. C'est précisément pourquoi le plein écran
  automatique est la voie principale.

**Rien n'est modifié dans la configuration** : ni la connexion enregistrée, ni `selectedmonitors`.
Seule la position de la fenêtre change, le temps de cette session — la prochaine ouverture retrouve
exactement les écrans configurés. La session n'est ni coupée ni rouverte : les applications
distantes continuent de tourner.

Quelques précisions utiles :

- **Écran d'arrivée de taille différente** : la fenêtre est redimensionnée pour couvrir l'écran ;
  le serveur suit si la résolution dynamique est active.
- **Session étalée sur plusieurs écrans** : elle est translatée sans jamais être redimensionnée —
  la réduire à un seul écran changerait la résolution de la session.
- **`Ctrl + Alt + Attn`** fait entrer et sortir du plein écran, depuis la session.
- Chaque déplacement est tracé dans le journal, avec les positions avant et après.

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


## Dispositions d'écrans prises en charge

Le plan reproduit la **géométrie réelle** du bureau, comme Paramètres › Affichage : chaque carte a
la position et les proportions de son écran. Sont donc gérées sans cas particulier :

- des écrans de **tailles et résolutions différentes** ;
- un écran **en dessous ou au-dessus** des autres, et plus généralement plusieurs rangées ;
- des écrans en **portrait** comme en **paysage**, y compris un portrait couvrant deux rangées ;
- **jusqu'à 5 écrans** et au-delà (la limite est celle de mstsc, 16) ;
- des écrans non alignés, décalés, ou non adjacents.

Les rangs affichés suivent l'**ordre de lecture** : de gauche à droite, rangée par rangée en
partant du haut. Sur une rangée unique — le cas courant — c'est exactement l'ordre de gauche à
droite. Les libellés s'adaptent : `Gauche / Centre / Droite` sur une rangée, `Haut gauche`,
`Bas droite`… sur plusieurs.

Deux détails qui comptent :

- Les rangées sont détectées par recouvrement vertical, en comparant chaque écran au **cœur** de la
  rangée (l'intersection de ses membres, jamais leur union). Sans cela un écran en portrait couvrant
  deux rangées absorberait la rangée du dessous.
- Les raccourcis « 2 écrans », « 3 écrans »… explorent les groupes **réellement voisins** en deux
  dimensions et privilégient ceux qui forment un **rectangle plein**, que mstsc gère mieux. Sur une
  disposition 2×2 plus un écran à droite, « 4 écrans » propose donc le carré, pas les quatre
  premiers de la liste.

Vérification sans avoir le matériel sous la main :

```bash
TermServMultiScreen.exe --maptest    # rapport texte : rangées, rangs, libellés, groupes
TermServMultiScreen.exe --mapdemo    # aperçu graphique de dispositions de référence
```

---

## Partager une session

Onglet **Partage**. Il demande d'abord le sens : partager ou recevoir.

**Partager** : choisir la connexion, décider si le nom d'utilisateur est inclus, puis récupérer un
code à transmettre par message, courriel ou papier. Exemple réel pour une session complète — nom,
serveur, utilisateur, écran sélectionné et toutes les options — **90 caractères** :

```
FD1wFAbOgD4HQduLM1ovJZZCwQI3Va3WZLB0P_ykjUkyzhbOgqaMUJ0JYlYNYAzpS62BgX7mwinoIeT9LqJ6uxWswE
```

**Recevoir** : coller le code, l'analyser — le contenu exact s'affiche avant tout import — puis
importer. La connexion apparaît dans la liste, son `.rdp` est généré et signé immédiatement.

Points de conception :

- **Les deux applications ne communiquent jamais.** Le code porte lui-même toutes les données :
  aucun réseau, aucun serveur, aucun compte. Il fonctionne même dicté au téléphone.
- **Aucun mot de passe** ne circule, dans aucun cas : l'application n'en conserve aucun. Le nom
  d'utilisateur, lui, n'est inclus que si l'expéditeur le demande.
- **Compacité** : sérialisation binaire (pas de JSON), options empaquetées en bits, réglages `.rdp`
  identiques aux valeurs par défaut retirés puisqu'ils seront régénérés, compression Brotli, puis
  Base64 URL — donc sans `+`, `/` ni `=`, sûr dans un message ou une URL.
- **Contrôle d'intégrité** de 2 octets : un code tronqué ou mal recopié est refusé avec un message
  clair, jamais interprété de travers.
- **Les écrans sont partagés par rang de gauche à droite**, la seule notion qui garde un sens d'un
  poste à l'autre. Si le destinataire a moins d'écrans, la sélection est ramenée à ce qui existe et
  il en est averti.
- Un nom déjà pris n'est jamais écrasé en silence : l'application propose un autre nom.
- Le code n'est pas chiffré : le nom du serveur y est lisible par qui le détient. Transmettez-le
  comme vous transmettriez l'adresse du serveur.

---

## Signature des fichiers .rdp

Chaque `.rdp` généré est signé par `rdpsign.exe` avec le certificat **CN=FlowDesk RDP Publisher**
(auto-signé, 10 ans, RSA 3072, EKU Code Signing), conservé dans `CurrentUser\My` avec sa clé privée
CNG persistante. Le pipeline est strict :

```
générer → écrire → fermer → rdpsign /l (test) → rdpsign (réel)
        → vérifier signscope + signature → vérifier que le fichier n'a pas changé → mstsc
```

**Si la signature échoue, mstsc n'est pas lancé** : l'application affiche l'erreur et journalise le
diagnostic. Rien n'est modifié dans le fichier après signature (contrôle par empreinte SHA-256).

Deux pièges, tous deux vérifiés sur cette machine :

- **L'option de `rdpsign` nomme l'algorithme de signature du fichier, pas le format de l'empreinte
  du certificat.** Sur Windows 26200, `/sha1` a disparu et seul `/sha256` existe — mais la valeur
  attendue reste l'**empreinte SHA-1** du certificat (`Thumbprint`). Lui passer le SHA-256 donne
  `0x80092004 CRYPT_E_NOT_FOUND`. L'application auto-calibre la bonne combinaison au démarrage avec
  `/l`, qui teste sans modifier le fichier : elle fonctionne donc aussi sur les builds à `/sha1`.
- **Faire disparaître l'avertissement d'éditeur exige la stratégie Windows**
  « Spécifier les empreintes numériques des certificats représentant des éditeurs .rdp approuvés »
  (`TrustedCertThumbprints`). Le magasin *Éditeurs approuvés* ne suffit pas. Cette clé de registre
  est en lecture seule pour les comptes standard, par conception : une élévation administrateur
  unique est nécessaire. Paramètres › **Confiance des fichiers .rdp** › *Déclarer l'éditeur*
  génère et lance le script correspondant, qui n'ajoute que l'empreinte FlowDesk et ne désactive
  aucune protection.

Sur les builds de juillet 2026 et suivantes, l'entrée est préfixée : `sha256:<empreinte>`. Sans
préfixe, Windows la lit comme une empreinte SHA-1 héritée. Le format retenu est déduit de
`TerminalServer.admx` de la machine, pas supposé.

Diagnostic complet (25 contrôles) : Paramètres › Confiance des fichiers .rdp › *Diagnostic*, ou

```bash
TermServMultiScreen.exe --rdptrust
```

---

## Où sont les fichiers

| Quoi | Où |
|---|---|
| Connexions enregistrées | `%LOCALAPPDATA%\TermServMultiScreen\config.json` |
| Fichiers `.rdp` générés | `%LOCALAPPDATA%\TermServMultiScreen\sessions\` |
| Journal | `%LOCALAPPDATA%\TermServMultiScreen\journal.log` |
| Script d'approbation de l'éditeur | `%LOCALAPPDATA%\TermServMultiScreen\FlowDesk-EditeurApprouve.ps1` |

---

## Organisation du code

```
app\
  Core\          logique sans interface : écrans, fichiers .rdp, configuration, journal
    MonitorInfo.cs      énumération Win32 et les trois numérotations
    MonitorLayout.cs    géométrie pure : rangées, ordre de lecture, libellés, groupes voisins
    RdpFile.cs          fabrication et import des .rdp
    AppConfig.cs        connexions enregistrées (System.Text.Json)
    Rdp\                signature et confiance des .rdp
      RdpCertificateService.cs    certificat FlowDesk, clé persistante, confiance locale
      RdpSigningService.cs        rdpsign.exe, auto-calibrage, vérification du résultat
      RdpPublisherTrustService.cs stratégie TrustedCertThumbprints et provisionnement élevé
      RdpLauncher.cs              pipeline strict : pas de signature = pas de mstsc
      RdpTrust.cs                 orchestration et état affiché dans l'interface
      RdpTrustDiagnostics.cs      25 contrôles réels, PASS / FAIL / WARNING
      RdpSessionWindows.cs        repérage des fenêtres de session réellement ouvertes
      RdpWindowMover.cs           déplacement d'une session en cours, sans toucher au .rdp
    Share\              code de partage d'une session, hors ligne et autoporteur
      SessionShareCode.cs         sérialisation binaire, Brotli, Base64 URL, contrôle d'intégrité
  Controls\      MonitorMapPanel : place les cartes à l'échelle et à leur position réelle
  Themes\        gabarit de carte partagé (accueil et aperçu), avec liaisons compilées
  Services\      composition, surveillance des écrans, dialogues, pastilles d'identification
    RdpFollowService.cs  plein écran automatique sur l'écran où la session vient d'être posée
    RdpHotkeyService.cs  raccourcis globaux de déplacement, sur leur propre boucle de messages
  ViewModels\    MVVM (CommunityToolkit.Mvvm)
  Pages\         Accueil, Connexions, Sessions, Partage, Paramètres, À propos
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
- Le déplacement d'une session en cours passe uniquement par `SetWindowPos` sur la fenêtre de
  `mstsc`. Une session en plein écran n'est **jamais** rétablie puis réagrandie : elle se
  retrouverait dans la zone de travail, barre des tâches visible. Le résultat est relu et une
  seconde tentative est faite si `mstsc` a replacé sa fenêtre, puis vérifié — l'interface annonce
  un échec plutôt que de laisser croire à une réussite.
- Le plein écran automatique n'agit qu'une fois la fenêtre immobile **et** `GetAsyncKeyState`
  confirmant le bouton de la souris relâché : c'est le seul moyen de savoir qu'un déplacement à la
  souris est terminé sur la fenêtre d'un autre processus. La position d'arrivée devient la nouvelle
  référence avant même que le déplacement soit appliqué, ce qui exclut toute boucle.
- Les raccourcis globaux sont posés par `RegisterHotKey(0, …)` depuis un fil dédié qui a sa propre
  boucle `GetMessage` : détourner le `WndProc` de la fenêtre WinUI suffirait à faire tomber
  l'interface. Ils ne remontent cependant pas depuis une session Bureau à distance active, qui
  installe son propre crochet clavier — d'où le plein écran automatique comme voie principale.

---

## Version précédente (WinForms)

`src\` contient la première version, en WinForms. Elle fonctionne toujours et rend le même service ;
la version WinUI 3 la remplace.
