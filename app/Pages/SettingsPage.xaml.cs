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
    }

    public SettingsViewModel ViewModel => AppServices.Settings;
}
