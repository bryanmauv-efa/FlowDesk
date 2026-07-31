using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TermServMultiScreen.Services;
using TermServMultiScreen.ViewModels;

namespace TermServMultiScreen.Pages;

public sealed partial class HomePage : Page
{
    public HomePage() => InitializeComponent();

    /// <summary>Le ViewModel est partagé : l'état survit à la navigation entre les pages.</summary>
    public HomeViewModel ViewModel => AppServices.Home;

    /// <summary>Propose les serveurs déjà utilisés, sans empêcher d'en saisir un nouveau.</summary>
    private void ServerHistory_Click(object sender, RoutedEventArgs e)
    {
        var flyout = new MenuFlyout();
        foreach (var address in ViewModel.KnownAddresses)
        {
            string value = address;
            var item = new MenuFlyoutItem { Text = value };
            item.Click += (_, _) => ViewModel.Address = value;
            flyout.Items.Add(item);
        }
        if (flyout.Items.Count == 0)
            flyout.Items.Add(new MenuFlyoutItem { Text = "Aucun serveur enregistré", IsEnabled = false });

        flyout.ShowAt(ServerHistoryButton);
    }
}
