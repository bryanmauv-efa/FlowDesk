using System.Drawing;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using TermServMultiScreen.Core;

namespace TermServMultiScreen.ViewModels;

/// <summary>
/// Une carte d'écran dans le plan. La géométrie n'est plus calculée ici : le panneau de mise en
/// page place et dimensionne la carte d'après <see cref="Bounds"/>, ce qui reproduit fidèlement
/// n'importe quelle disposition — tailles inégales, portrait, écran en dessous.
/// </summary>
public sealed partial class MonitorCardViewModel : ObservableObject
{
    private readonly Action<MonitorCardViewModel> _onToggled;
    private bool _suppressCallback;

    public MonitorInfo Monitor { get; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    public MonitorCardViewModel(MonitorInfo monitor, bool selected, Action<MonitorCardViewModel> onToggled)
    {
        Monitor = monitor;
        _onToggled = onToggled;
        SetSelectedQuiet(selected);
    }

    /// <summary>Position et taille réelles sur le bureau virtuel, en pixels.</summary>
    public Rectangle Bounds => Monitor.LayoutBounds;

    public string Number => Monitor.Order.ToString();
    public string PositionLabel => Monitor.PositionLabel;
    public string WindowsLabel => $"Windows {Monitor.WindowsNumber}";
    public string ResolutionText => Monitor.ResolutionText;

    /// <summary>Portrait, paysage ou carré : affiché pour les dispositions inhabituelles.</summary>
    public string OrientationText => Monitor.Resolution.Height > Monitor.Resolution.Width
        ? "portrait"
        : Monitor.Resolution.Width == Monitor.Resolution.Height ? "carré" : "";

    public Visibility OrientationVisibility =>
        OrientationText.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

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
