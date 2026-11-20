using Microsoft.UI.Xaml.Controls;
using FlowDesk.Services;
using FlowDesk.ViewModels;

namespace FlowDesk.Pages;

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
