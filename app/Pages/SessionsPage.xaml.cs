using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using TermServMultiScreen.Services;
using TermServMultiScreen.ViewModels;

namespace TermServMultiScreen.Pages;

public sealed partial class SessionsPage : Page
{
    public SessionsPage()
    {
        InitializeComponent();
        ViewModel.Reload();

        // Les fenêtres ouvertes ne sont suivies que pendant l'affichage de la page : inutile de
        // balayer les fenêtres du système en permanence.
        Loaded += (_, _) => Live.StartWatching();
        Unloaded += (_, _) => Live.StopWatching();
    }

    public SessionsViewModel ViewModel => AppServices.Sessions;

    /// <summary>Sessions réellement ouvertes, déplaçables d'un écran à l'autre.</summary>
    public LiveSessionsViewModel Live => AppServices.LiveSessions;

    private void List_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e) =>
        ViewModel.RelaunchCommand.Execute(ViewModel.Selected);
}
