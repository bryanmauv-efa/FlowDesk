using Microsoft.UI.Xaml;
using TermServMultiScreen.Core;
using TermServMultiScreen.Overlays;

namespace TermServMultiScreen.Services;

/// <summary>Affiche (et referme) les pastilles d'identification sur tous les écrans.</summary>
public static class IdentifyService
{
    private static readonly List<IdentifyWindow> Open = [];
    private static DispatcherTimer? _timer;

    public static bool AnyOpen => Open.Count > 0;

    public static void ShowAll(
        IEnumerable<MonitorInfo> monitors,
        IList<MonitorInfo>? selection,
        AppConfig? config,
        TimeSpan duration,
        Action? onClosed = null)
    {
        CloseAll();

        foreach (var monitor in monitors)
        {
            try
            {
                bool selected = selection?.Contains(monitor) == true;
                var window = new IdentifyWindow(monitor, selected, RdpFile.EffectiveRdpId(monitor, config));
                window.Dismissed += (_, _) => Finish(onClosed);
                window.Closed += (_, _) => Open.Remove(window);
                Open.Add(window);
                window.Activate();
            }
            catch (Exception ex)
            {
                Log.Write($"Pastille écran {monitor.Order} : {ex.Message}");
            }
        }

        if (Open.Count == 0)
        {
            onClosed?.Invoke();
            return;
        }

        _timer = new DispatcherTimer { Interval = duration < TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : duration };
        _timer.Tick += (_, _) => Finish(onClosed);
        _timer.Start();
    }

    private static void Finish(Action? onClosed)
    {
        CloseAll();
        onClosed?.Invoke();
    }

    public static void CloseAll()
    {
        _timer?.Stop();
        _timer = null;

        foreach (var window in Open.ToList())
        {
            try { window.Close(); }
            catch (Exception ex) { Log.Write($"Fermeture pastille : {ex.Message}"); }
        }
        Open.Clear();
    }
}
