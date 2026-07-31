using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using TermServMultiScreen.Core;
using Windows.Graphics;
using Windows.UI;

namespace TermServMultiScreen.Overlays;

/// <summary>
/// Grande pastille plein écran affichant le numéro d'un écran : la vérification ultime que le
/// plan de l'application correspond bien à la réalité physique.
/// </summary>
public sealed partial class IdentifyWindow : Window
{
    public event EventHandler? Dismissed;

    public IdentifyWindow(MonitorInfo monitor, bool selected, int rdpId)
    {
        InitializeComponent();

        Title = $"Identification écran {monitor.Order}";
        BigNumber.Text = monitor.Order.ToString();
        PositionLabel.Text = monitor.PositionLabel.ToUpperInvariant();
        Line1.Text = $"Numéro Windows : {monitor.WindowsNumber}          {monitor.ResolutionText}";
        Line2.Text = $"Identifiant RDP : {rdpId}{(monitor.IsPrimary ? "          ÉCRAN PRINCIPAL" : "")}";
        UsedText.Text = selected ? "UTILISÉ PAR LA SESSION" : "non utilisé";
        Root.Background = new SolidColorBrush(selected
            ? Color.FromArgb(255, 0, 95, 170)
            : Color.FromArgb(255, 38, 38, 42));
        UsedBadge.Visibility = selected ? Visibility.Visible : Visibility.Collapsed;

        var appWindow = AppWindow;
        appWindow.IsShownInSwitchers = false;
        if (appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsResizable = false;
            presenter.IsMinimizable = false;
            presenter.IsMaximizable = false;
            presenter.IsAlwaysOnTop = true;
        }

        var bounds = monitor.WindowBounds.Width > 0 ? monitor.WindowBounds : monitor.LayoutBounds;
        appWindow.MoveAndResize(new RectInt32(bounds.X, bounds.Y, bounds.Width, bounds.Height));
    }

    private void Root_PointerPressed(object sender, PointerRoutedEventArgs e) =>
        Dismissed?.Invoke(this, EventArgs.Empty);
}
