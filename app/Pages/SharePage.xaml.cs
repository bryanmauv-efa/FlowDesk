using Microsoft.UI.Xaml.Controls;
using TermServMultiScreen.Services;
using TermServMultiScreen.ViewModels;

namespace TermServMultiScreen.Pages;

public sealed partial class SharePage : Page
{
    public SharePage()
    {
        InitializeComponent();
        ViewModel.Reload();
    }

    public ShareViewModel ViewModel => AppServices.Share;
}
