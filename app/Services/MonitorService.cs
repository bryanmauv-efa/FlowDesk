using Microsoft.UI.Xaml;
using TermServMultiScreen.Core;

namespace TermServMultiScreen.Services;

/// <summary>
/// Source unique de vérité pour la liste des écrans. Réénumère périodiquement : brancher,
/// débrancher ou réorganiser un écran est pris en compte sans redémarrer l'application.
/// </summary>
public sealed class MonitorService
{
    private readonly DispatcherTimer _watch = new() { Interval = TimeSpan.FromSeconds(2) };
    private string _signature = "";

    public List<MonitorInfo> Monitors { get; private set; } = [];

    /// <summary>Levé quand la configuration des écrans a réellement changé.</summary>
    public event EventHandler? Changed;

    public MonitorService()
    {
        Monitors = MonitorEnumerator.Enumerate();
        _signature = Signature(Monitors);
        _watch.Tick += (_, _) => Refresh();
    }

    public void StartWatching() => _watch.Start();

    public void StopWatching() => _watch.Stop();

    /// <summary>Réénumère et prévient si quelque chose a bougé. Renvoie true en cas de changement.</summary>
    public bool Refresh(bool force = false)
    {
        try
        {
            var fresh = MonitorEnumerator.Enumerate();
            string signature = Signature(fresh);
            if (!force && signature == _signature) return false;

            _signature = signature;
            Monitors = fresh;
            Log.Write($"Écrans : {fresh.Count} détecté(s) — {string.Join(" | ",
                MonitorEnumerator.LeftToRight(fresh).Select(m => $"{m.ShortLabel} id={m.RdpId}"))}");
            Changed?.Invoke(this, EventArgs.Empty);
            return true;
        }
        catch (Exception ex)
        {
            Log.Write($"Réénumération des écrans : {ex.Message}");
            return false;
        }
    }

    private static string Signature(IEnumerable<MonitorInfo> monitors) =>
        string.Join(";", monitors.Select(m => $"{m.StableKey}|{m.LayoutBounds}|{m.IsPrimary}|{m.RdpId}"));
}
