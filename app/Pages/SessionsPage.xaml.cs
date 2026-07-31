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
    }

    public SessionsViewModel ViewModel => AppServices.Sessions;

    private void List_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e) =>
        ViewModel.RelaunchCommand.Execute(ViewModel.Selected);
}
