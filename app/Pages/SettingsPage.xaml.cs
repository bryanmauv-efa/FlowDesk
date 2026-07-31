using Microsoft.UI.Xaml.Controls;
using TermServMultiScreen.Services;
using TermServMultiScreen.ViewModels;

namespace TermServMultiScreen.Pages;

public sealed partial class SettingsPage : Page
{
    public SettingsPage()
    {
        InitializeComponent();
        ViewModel.SyncTheme();
        Loaded += async (_, _) => await ViewModel.RefreshRdpTrustAsync();
    }

    public SettingsViewModel ViewModel => AppServices.Settings;
}
