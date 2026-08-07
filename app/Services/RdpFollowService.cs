using System.Drawing;
using Microsoft.UI.Xaml;
using TermServMultiScreen.Core;
using TermServMultiScreen.Core.Rdp;

namespace TermServMultiScreen.Services;

/// <summary>
/// Suivi automatique des sessions : dès qu'une fenêtre de Bureau à distance est <b>posée</b> sur un
/// autre écran, elle est mise en plein écran sur cet écran. Rien à cliquer, aucun raccourci — le
/// geste suffit, et il se fait entièrement dans la session.
///
/// C'est le complément de <c>maximizetocurrentdisplays:i:1</c> : ce réglage corrige le
/// comportement de mstsc pour les prochaines connexions, ce service le corrige tout de suite, y
/// compris pour une session déjà ouverte.
///
/// Deux garde-fous font tout le travail :
/// <list type="bullet">
///   <item>on n'agit qu'une fois la fenêtre <b>immobile</b> et le bouton de la souris relâché,
///         sinon on lutterait contre le déplacement en cours ;</item>
///   <item>on n'agit que si l'écran majoritaire a <b>changé</b> : déplacer une fenêtre à l'intérieur
///         de son écran ne déclenche rien, et la position d'arrivée devient la nouvelle référence,
///         ce qui exclut toute boucle.</item>
/// </list>
///
/// Rien n'est réécrit dans la configuration : seule la position de la fenêtre change.
/// </summary>
public sealed class RdpFollowService
{
    /// <summary>État suivi pour une fenêtre de session.</summary>
    private sealed class Tracked
    {
        public Rectangle LastRect;
        /// <summary>Écran sur lequel la fenêtre était posée la dernière fois qu'elle s'est arrêtée.</summary>
        public Rectangle SettledScreen;
        public int StableTicks;
    }

    /// <summary>Assez court pour réagir aussitôt, assez long pour ne rien coûter.</summary>
    private static readonly TimeSpan Beat = TimeSpan.FromMilliseconds(400);

    /// <summary>Deux battements d'immobilité : le geste est fini, on peut agir.</summary>
    private const int StableTicksBeforeSnap = 2;

    /// <summary>Recensement complet des fenêtres toutes les deux secondes.</summary>
    private const int TicksBetweenScans = 5;

    private readonly MonitorService _monitors;
    private readonly DispatcherTimer _timer = new() { Interval = Beat };
    private readonly Dictionary<nint, Tracked> _tracked = [];
    private int _ticksSinceScan;
    private bool _started;
    private bool _enabled = true;
    private bool _snapping;

    public RdpFollowService(MonitorService monitors)
    {
        _monitors = monitors;
        _timer.Tick += (_, _) => Tick();

        // Brancher ou débrancher un écran fait bouger les fenêtres par la volonté de Windows, pas
        // par celle de l'utilisateur : on repart d'un recensement neuf plutôt que d'y voir des
        // déplacements à corriger.
        _monitors.Changed += (_, _) => Apply();
    }

    /// <summary>Levé après chaque mise en plein écran automatique : bandeau d'état de la page.</summary>
    public event Action<RdpMoveResult>? Snapped;

    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value) return;
            _enabled = value;
            Log.Write($"Suivi automatique des sessions : {(value ? "activé" : "désactivé")}.");
            Apply();
        }
    }

    /// <summary>Démarre le suivi (la fenêtre principale est prête).</summary>
    public void Start()
    {
        _started = true;
        Apply();
    }

    public void Stop()
    {
        _started = false;
        _timer.Stop();
    }

    private void Apply()
    {
        if (_started && _enabled)
        {
            // Repartir d'un recensement neuf : les fenêtres déjà ouvertes sont là où elles doivent
            // être, elles ne doivent surtout pas être déplacées à l'activation.
            _tracked.Clear();
            _ticksSinceScan = TicksBetweenScans;
            _timer.Start();
        }
        else
        {
            _timer.Stop();
        }
    }

    private void Tick()
    {
        // Une mise en plein écran est en cours : ce battement ne doit rien interpréter.
        if (_snapping) return;

        try
        {
            if (++_ticksSinceScan >= TicksBetweenScans)
            {
                Scan();
                _ticksSinceScan = 0;
            }

            foreach (var (handle, state) in _tracked.ToList())
            {
                if (!NativeMethods.IsWindow(handle)) { _tracked.Remove(handle); continue; }

                // Une fenêtre réduite est placée hors écran par Windows : son rectangle ne veut
                // rien dire, et son rétablissement ne doit pas passer pour un déplacement.
                if (NativeMethods.IsIconic(handle)) { state.StableTicks = 0; continue; }
                if (!NativeMethods.GetWindowRect(handle, out var raw)) continue;

                var rect = new Rectangle(raw.Left, raw.Top, raw.Right - raw.Left, raw.Bottom - raw.Top);
                if (rect != state.LastRect)
                {
                    state.LastRect = rect;
                    state.StableTicks = 0;
                    continue;
                }

                // Bouton gauche encore enfoncé : la fenêtre est peut-être immobile un instant,
                // mais le déplacement n'est pas terminé.
                if (MouseHeld()) { state.StableTicks = 0; continue; }

                state.StableTicks++;
                if (state.StableTicks != StableTicksBeforeSnap) continue;   // une seule fois par pose

                var screen = RdpWindowMover.MonitorOf(rect, _monitors.Monitors);
                if (screen is null) continue;

                Rectangle bounds = RdpWindowMover.BoundsOf(screen);
                if (bounds == state.SettledScreen) continue;                // même écran : rien à faire

                // La référence est mise à jour AVANT d'agir : le déplacement que l'on provoque ne
                // peut donc pas être relu comme un nouveau déplacement.
                state.SettledScreen = bounds;
                _ = SnapAsync(handle, screen);
            }
        }
        catch (Exception ex)
        {
            Log.Write($"Suivi automatique des sessions : {ex.Message}");
        }
    }

    /// <summary>Recense les fenêtres de session : les nouvelles sont mémorisées sans être touchées.</summary>
    private void Scan()
    {
        var found = RdpWindowFinder.Find();
        var alive = found.Select(w => w.Handle).ToHashSet();

        foreach (nint gone in _tracked.Keys.Where(h => !alive.Contains(h)).ToList())
            _tracked.Remove(gone);

        foreach (var window in found)
        {
            if (_tracked.ContainsKey(window.Handle)) continue;

            var screen = RdpWindowMover.CurrentMonitor(window, _monitors.Monitors);
            _tracked[window.Handle] = new Tracked
            {
                LastRect = window.Bounds,
                SettledScreen = screen is null ? Rectangle.Empty : RdpWindowMover.BoundsOf(screen)
            };
        }
    }

    private async Task SnapAsync(nint handle, MonitorInfo target)
    {
        _snapping = true;
        try
        {
            var window = RdpWindowFinder.Reread(handle);
            if (window is null) return;

            Log.Write($"Suivi automatique : « {window.Server} » posée sur l'écran {target.Order} "
                    + $"{target.PositionLabel} — mise en plein écran.");

            var result = await RdpWindowMover.MoveToAsync(
                window, target, _monitors.Monitors, fitToScreen: true, forceFullScreen: true);

            // La position d'arrivée devient la référence : sans ça, le battement suivant y verrait
            // un déplacement de plus.
            if (_tracked.TryGetValue(handle, out var state))
            {
                state.LastRect = result.After;
                state.StableTicks = 0;
            }

            Snapped?.Invoke(result);
        }
        catch (Exception ex)
        {
            Log.Write($"Mise en plein écran automatique : {ex.Message}");
        }
        finally
        {
            _snapping = false;
        }
    }

    private static bool MouseHeld() =>
        (NativeMethods.GetAsyncKeyState(NativeMethods.VK_LBUTTON) & 0x8000) != 0;
}
