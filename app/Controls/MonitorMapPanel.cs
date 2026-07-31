using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TermServMultiScreen.Core;
using TermServMultiScreen.ViewModels;
using Windows.Foundation;

namespace TermServMultiScreen.Controls;

/// <summary>
/// Dispose les cartes d'écran à l'échelle et à leur position réelle sur le bureau virtuel, comme
/// le fait Paramètres › Affichage. Gère donc sans cas particulier les écrans de tailles
/// différentes, un écran placé en dessous ou au-dessus, un écran en portrait, et les décalages.
/// </summary>
public sealed class MonitorMapPanel : Panel
{
    private double _scale = 1;
    private System.Drawing.Rectangle _box;

    /// <summary>Hauteur maximale du plan : au-delà, l'échelle est réduite.</summary>
    public double MaxMapHeight { get; set; } = 300;

    /// <summary>Retrait appliqué à chaque carte pour que deux écrans voisins restent distincts.</summary>
    public double Inset { get; set; } = 3;

    /// <summary>Largeur retenue quand le parent n'impose aucune contrainte.</summary>
    public double FallbackWidth { get; set; } = 900;

    private List<(FrameworkElement Element, System.Drawing.Rectangle Bounds)> Items() =>
        Children.OfType<FrameworkElement>()
            .Select(element => (element, Bounds: (element.DataContext as MonitorCardViewModel)?.Bounds ?? default))
            .Where(item => item.Bounds is { Width: > 0, Height: > 0 })
            .ToList();

    protected override Size MeasureOverride(Size availableSize)
    {
        var items = Items();
        if (items.Count == 0)
        {
            foreach (var child in Children) child.Measure(new Size(0, 0));
            return new Size(0, 0);
        }

        _box = MonitorLayout.Union(items.Select(i => i.Bounds));
        if (_box.Width <= 0 || _box.Height <= 0) return new Size(0, 0);

        double width = double.IsInfinity(availableSize.Width) || availableSize.Width <= 0
            ? FallbackWidth
            : availableSize.Width;

        // L'échelle respecte à la fois la largeur disponible et la hauteur maximale du plan :
        // une disposition sur deux rangées est donc simplement dessinée plus petite.
        _scale = Math.Min(width / _box.Width, MaxMapHeight / _box.Height);
        if (_scale <= 0 || double.IsNaN(_scale) || double.IsInfinity(_scale)) _scale = 1;

        foreach (var (element, bounds) in items)
            element.Measure(CardSize(bounds));

        return new Size(_box.Width * _scale, _box.Height * _scale);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var items = Items();
        if (items.Count == 0 || _box.Width <= 0) return finalSize;

        foreach (var (element, bounds) in items)
        {
            var size = CardSize(bounds);
            element.Arrange(new Rect(
                (bounds.Left - _box.Left) * _scale + Inset,
                (bounds.Top - _box.Top) * _scale + Inset,
                size.Width,
                size.Height));
        }
        return new Size(_box.Width * _scale, _box.Height * _scale);
    }

    private Size CardSize(System.Drawing.Rectangle bounds) => new(
        Math.Max(24, bounds.Width * _scale - 2 * Inset),
        Math.Max(24, bounds.Height * _scale - 2 * Inset));
}
