using Microsoft.UI.Xaml;

namespace TermServMultiScreen.Themes;

/// <summary>
/// Code-behind du dictionnaire de gabarits. Sa seule raison d'être : permettre les liaisons
/// compilées x:Bind dans un dictionnaire de ressources partagé.
/// </summary>
public sealed partial class CardTemplates : ResourceDictionary
{
    public CardTemplates() => InitializeComponent();
}
