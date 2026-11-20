using Microsoft.UI.Xaml.Controls;
using FlowDesk.Services;
using FlowDesk.ViewModels;

namespace FlowDesk.Pages;

public sealed partial class SharePage : Page
{
    public SharePage()
    {
        InitializeComponent();
        ViewModel.Reload();
    }

    public ShareViewModel ViewModel => AppServices.Share;
}
