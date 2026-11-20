# FlowDesk

Application Windows pour ouvrir une connexion Bureau à distance sur les écrans choisis.
C# / .NET 10 / WinUI 3 ; Windows 10 (2004+) et Windows 11 x64.

- [Téléchargements EXE et MSI](https://github.com/bryanmauv-efa/FlowDesk/releases)
- [Utilisation, installation silencieuse et compilation](LISEZ-MOI.md)
- [Confidentialité et modifications du système](PRIVACY.md)
- [Code signing policy](CODE_SIGNING_POLICY.md)
- [Préparation de la candidature SignPath](docs/SIGNPATH.md)
- [Dépendances et points à vérifier](THIRD_PARTY_NOTICES.md)

## Licence

Copyright (C) 2026 efa. Le code original de FlowDesk est distribué sous
**GNU GPL version 3 uniquement (`GPL-3.0-only`)**, sans garantie. Voir [LICENSE](LICENSE).
Cette déclaration couvre les sources originales, les tests, les scripts et les éléments
originaux du dépôt à partir de ce changement. Les composants tiers conservent leurs licences.
Les versions modifiées distribuées doivent respecter les obligations de la GPL, notamment
la mise à disposition du code source correspondant. Aucune double licence commerciale
n'est proposée. Les noms et marques ne sont pas concédés comme marques par cette licence.

## Compilation

Installer le SDK .NET 10.0.404 ou une version compatible, puis lancer sous Windows :

```powershell
./compilation.ps1 -SkipSignature
dotnet run --project tests/Migration/Migration.csproj
```

Le workflow [Windows build](.github/workflows/windows-build.yml) compile sur une machine
GitHub hébergée, teste la migration et conserve les EXE/MSI comme artefacts. Il ne publie
pas automatiquement de release. Les actions sont fixées par identifiant de commit.

## Code signing policy

**Candidature SignPath en préparation : aucune acceptation ni signature SignPath acquise.**
Les releases actuelles ne deviennent pas signées par l'ajout de cette documentation.
Responsable des sources, des revues et des approbations :
[Bryan / bryanmauv-efa](https://github.com/bryanmauv-efa).
Voir la [politique de signature](CODE_SIGNING_POLICY.md) et les
[conditions de confidentialité](PRIVACY.md).
