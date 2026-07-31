using System.Collections.ObjectModel;
using System.Drawing;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TermServMultiScreen.Core;
using TermServMultiScreen.ViewModels;
using Windows.Graphics;

namespace TermServMultiScreen.Overlays;

/// <summary>
/// Aperçu du plan des écrans pour une disposition donnée, y compris celles que la machine
/// courante n'a pas physiquement. Utilise le même panneau et le même gabarit de carte que la page
/// d'accueil : ce qui s'affiche ici est exactement ce qui s'affichera avec ce matériel.
/// </summary>
public sealed partial class LayoutPreviewWindow : Window
{
    private readonly ObservableCollection<MonitorCardViewModel> _cards = [];

    public LayoutPreviewWindow()
    {
        InitializeComponent();
        Map.ItemsSource = _cards;

        LayoutChooser.Items.Add("Disposition réelle de ce poste");
        foreach (var (name, bounds, _) in MonitorLayout.ReferenceLayouts)
            LayoutChooser.Items.Add($"{name} ({bounds.Length} écrans)");

        try
        {
            var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
            int width = Math.Min(1200, area.Width - 80);
            int height = Math.Min(820, area.Height - 80);
            AppWindow.MoveAndResize(new RectInt32(
                area.X + (area.Width - width) / 2, area.Y + (area.Height - height) / 2, width, height));
        }
        catch (Exception ex) { Log.Write($"Aperçu des dispositions : {ex.Message}"); }

        LayoutChooser.SelectedIndex = 0;
    }

    private void LayoutChooser_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        int index = LayoutChooser.SelectedIndex;
        if (index < 0) return;

        List<MonitorInfo> monitors;
        string title;

        if (index == 0)
        {
            monitors = MonitorEnumerator.Enumerate();
            title = "Disposition réelle de ce poste";
        }
        else
        {
            var (name, bounds, primary) = MonitorLayout.ReferenceLayouts[index - 1];
            monitors = Build(bounds, primary);
            title = name;
        }

        _cards.Clear();
        foreach (var monitor in MonitorEnumerator.LeftToRight(monitors))
            _cards.Add(new MonitorCardViewModel(monitor, monitor.IsPrimary, _ => { }));

        Summary.Text = MonitorLayout.Describe(title,
            monitors.Select(m => m.LayoutBounds).ToList(),
            monitors.FindIndex(m => m.IsPrimary));
    }

    /// <summary>Écrans synthétiques passant par la même attribution de rangs et de libellés.</summary>
    private static List<MonitorInfo> Build(Rectangle[] bounds, int primary)
    {
        var monitors = bounds.Select((rectangle, index) => new MonitorInfo
        {
            RdpId = index,
            GdiDeviceName = $@"\\.\DISPLAY{index + 1}",
            WindowsNumber = index + 1,
            IsPrimary = index == primary,
            WindowBounds = rectangle,
            WindowWorkArea = rectangle,
            PhysicalBounds = rectangle,
            LayoutBounds = rectangle,
            HasDevMode = true,
            MonitorName = "écran de démonstration",
            StableKey = $"DEMO{index}"
        }).ToList();

        MonitorEnumerator.AssignOrderAndLabels(monitors);
        return monitors;
    }
}
