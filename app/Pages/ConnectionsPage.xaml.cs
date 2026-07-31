using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using TermServMultiScreen.Services;
using TermServMultiScreen.ViewModels;

namespace TermServMultiScreen.Pages;

public sealed partial class ConnectionsPage : Page
{
    public ConnectionsPage()
    {
        InitializeComponent();
        ViewModel.Reload();
    }

    public ConnectionsViewModel ViewModel => AppServices.Connections;

    private void List_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e) =>
        ViewModel.OpenCommand.Execute(ViewModel.Selected);
}
