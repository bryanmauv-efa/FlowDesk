using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using TermServMultiScreen.Core;

namespace TermServMultiScreen.ViewModels;

/// <summary>Une carte d'écran dans le plan : ce que l'utilisateur voit et clique.</summary>
public sealed partial class MonitorCardViewModel : ObservableObject
{
    private readonly Action<MonitorCardViewModel> _onToggled;
    private bool _suppressCallback;

    public MonitorInfo Monitor { get; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    public MonitorCardViewModel(
        MonitorInfo monitor,
        bool selected,
        double cardHeight,
        double topOffset,
        Action<MonitorCardViewModel> onToggled)
    {
        Monitor = monitor;
        _onToggled = onToggled;

        // Largeur proportionnelle à la forme réelle de l'écran : un écran en portrait
        // apparaît haut et étroit, comme dans les paramètres de Windows.
        double ratio = monitor.Resolution.Height > 0
            ? (double)monitor.Resolution.Width / monitor.Resolution.Height
            : 16d / 9d;
        CardHeight = cardHeight;
        CardWidth = Math.Clamp(cardHeight * ratio, 110, 620);
        CardMargin = new Thickness(0, topOffset, 0, 0);

        SetSelectedQuiet(selected);
    }

    public double CardWidth { get; }
    public double CardHeight { get; }
    public Thickness CardMargin { get; }

    public string Number => Monitor.Order.ToString();
    public string PositionLabel => Monitor.PositionLabel;
    public string WindowsLabel => $"Windows {Monitor.WindowsNumber}";
    public string ResolutionText => Monitor.ResolutionText;
    public Visibility PrimaryVisibility => Monitor.IsPrimary ? Visibility.Visible : Visibility.Collapsed;
    public Visibility BadgeVisibility => IsSelected ? Visibility.Visible : Visibility.Collapsed;

    public string ToolTipText =>
        $"{Monitor.DetailText}{Environment.NewLine}{Environment.NewLine}Cliquez pour ajouter ou retirer cet écran.";

    partial void OnIsSelectedChanged(bool value)
    {
        OnPropertyChanged(nameof(BadgeVisibility));
        if (!_suppressCallback) _onToggled(this);
    }

    /// <summary>Change l'état sans prévenir le parent (mises à jour groupées, préréglages).</summary>
    public void SetSelectedQuiet(bool value)
    {
        _suppressCallback = true;
        try { IsSelected = value; }
        finally { _suppressCallback = false; }
    }
}
