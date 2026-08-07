using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using TermServMultiScreen.Core;
using TermServMultiScreen.Core.Rdp;
using TermServMultiScreen.Services;

namespace TermServMultiScreen.ViewModels;

/// <summary>Une session ouverte, telle que la liste déroulante la présente.</summary>
public sealed partial class LiveWindowViewModel : ObservableObject
{
    public LiveWindowViewModel(RdpSessionWindow window, IEnumerable<MonitorInfo> monitors)
    {
        Handle = window.Handle;
        Window = window;
        Label = "";
        Update(window, monitors);
    }

    public nint Handle { get; }

    /// <summary>Dernier état lu de la fenêtre.</summary>
    public RdpSessionWindow Window { get; private set; }

    [ObservableProperty]
    public partial string Label { get; set; }

    public void Update(RdpSessionWindow window, IEnumerable<MonitorInfo> monitors)
    {
        Window = window;
        var screen = RdpWindowMover.CurrentMonitor(window, monitors);
        string where = window.Minimized
            ? "réduite"
            : screen is null ? "écran inconnu" : $"écran {screen.Order} {screen.PositionLabel}";

        Label = $"{window.Server}   —   {where}   ({window.ModeText}, {window.SizeText})";
    }

    public override string ToString() => Label;
}

/// <summary>Un bouton « déplacer la session sur cet écran ».</summary>
public sealed partial class ScreenTargetViewModel : ObservableObject
{
    private readonly Func<int, Task> _move;

    public ScreenTargetViewModel(MonitorInfo monitor, Func<int, Task> move)
    {
        _move = move;
        Order = monitor.Order;
        Label = $"{monitor.Order} · {monitor.PositionLabel}";
        ToolTipText = $"Déplacer la session sur l'écran {monitor.Order} ({monitor.PositionLabel}, "
                    + $"{monitor.ResolutionText}, Windows {monitor.WindowsNumber})."
                    + Environment.NewLine
                    + "La connexion enregistrée et son fichier .rdp ne sont pas modifiés.";
    }

    public int Order { get; }
    public string Label { get; }
    public string ToolTipText { get; }

    [RelayCommand]
    private Task MoveAsync() => _move(Order);
}

/// <summary>
/// Sessions Bureau à distance ouvertes : les déplacer d'un écran à l'autre <b>pendant</b> leur
/// utilisation. C'est le seul moyen de sortir une session du plein écran multi-écrans, où mstsc
/// n'autorise pas le déplacement à la souris.
///
/// Aucune configuration n'est touchée : ni la connexion enregistrée, ni son fichier .rdp, ni
/// <c>selectedmonitors</c>. La session suivante s'ouvrira donc sur les écrans configurés.
/// </summary>
public sealed partial class LiveSessionsViewModel : ObservableObject
{
    private readonly AppConfig _config;
    private readonly MonitorService _monitors;
    private readonly RdpFollowService _follow;
    private readonly DispatcherQueue? _dispatcher;
    private readonly DispatcherTimer _watch = new() { Interval = TimeSpan.FromSeconds(3) };
    private string _targetSignature = "";
    private bool _moving;
    private bool _loading;

    public LiveSessionsViewModel(AppConfig config, MonitorService monitors, RdpFollowService follow)
    {
        _config = config;
        _monitors = monitors;
        _follow = follow;

        // Capturé sur le fil de l'interface : les raccourcis clavier arrivent, eux, sur un autre fil.
        _dispatcher = DispatcherQueue.GetForCurrentThread();

        _loading = true;
        StatusText = "";
        HotkeyText = RdpHotkeyService.Description;
        FollowScreen = config.FollowScreenOnMove;
        _loading = false;

        _watch.Tick += (_, _) => Refresh();
        _monitors.Changed += (_, _) => Refresh();

        // Le suivi tourne en permanence : son compte rendu s'affiche si la page est ouverte.
        _follow.Snapped += result =>
        {
            StatusText = result.Message;
            Refresh();
        };
    }

    public ObservableCollection<LiveWindowViewModel> Windows { get; } = [];
    public ObservableCollection<ScreenTargetViewModel> Targets { get; } = [];

    [ObservableProperty]
    public partial LiveWindowViewModel? Selected { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; }

    [ObservableProperty]
    public partial string HotkeyText { get; set; }

    /// <summary>
    /// Mettre la session en plein écran sur l'écran d'arrivée dès qu'on l'y fait glisser. C'est le
    /// réglage principal : avec lui, il n'y a plus rien à cliquer ni aucun raccourci à connaître.
    /// </summary>
    [ObservableProperty]
    public partial bool FollowScreen { get; set; }

    partial void OnFollowScreenChanged(bool value)
    {
        if (_loading) return;

        _follow.Enabled = value;
        _config.FollowScreenOnMove = value;
        _config.Save();

        StatusText = value
            ? "Plein écran automatique activé : faites glisser la fenêtre sur un autre écran, elle s'y met en plein écran."
            : "Plein écran automatique désactivé : utilisez les boutons ci-dessous pour changer d'écran.";
    }

    [ObservableProperty]
    public partial bool HasWindows { get; set; }

    public Visibility WindowsVisibility => HasWindows ? Visibility.Visible : Visibility.Collapsed;
    public Visibility EmptyVisibility => HasWindows ? Visibility.Collapsed : Visibility.Visible;

    partial void OnHasWindowsChanged(bool value)
    {
        OnPropertyChanged(nameof(WindowsVisibility));
        OnPropertyChanged(nameof(EmptyVisibility));
    }

    // ------------------------------------------------------------------ cycle de vie

    /// <summary>Suit les sessions ouvertes tant que la page est affichée.</summary>
    public void StartWatching()
    {
        Refresh();
        _watch.Start();
    }

    public void StopWatching() => _watch.Stop();

    /// <summary>Branche les raccourcis globaux : ils arrivent sur un autre fil, d'où le renvoi.</summary>
    public void Attach(RdpHotkeyService hotkeys)
    {
        hotkeys.ScreenRequested += order => OnUiThread(() => _ = MoveToScreenAsync(order));
        hotkeys.StepRequested += direction => OnUiThread(() => _ = StepAsync(direction));
    }

    private void OnUiThread(Action action)
    {
        var queue = _dispatcher ?? AppServices.MainWindow?.DispatcherQueue;
        if (queue is null)
        {
            // Les collections de l'interface ne se touchent pas depuis un autre fil.
            Log.Write("Raccourci ignoré : le fil de l'interface n'est pas joignable.");
            return;
        }
        queue.TryEnqueue(() => action());
    }

    // ------------------------------------------------------------------ lecture

    /// <summary>Relit les fenêtres ouvertes en préservant la sélection en cours.</summary>
    public void Refresh()
    {
        try
        {
            var monitors = _monitors.Monitors;
            var found = RdpWindowFinder.Find();

            // Mise à jour en place : la liste déroulante ne doit pas perdre son choix toutes les
            // trois secondes.
            for (int i = Windows.Count - 1; i >= 0; i--)
                if (!found.Exists(w => w.Handle == Windows[i].Handle)) Windows.RemoveAt(i);

            foreach (var window in found)
            {
                var known = Windows.FirstOrDefault(w => w.Handle == window.Handle);
                if (known is null) Windows.Add(new LiveWindowViewModel(window, monitors));
                else known.Update(window, monitors);
            }

            HasWindows = Windows.Count > 0;
            if (Selected is null || !Windows.Contains(Selected)) Selected = Windows.FirstOrDefault();

            RebuildTargets(monitors);

            // Formulation honnête : une session au premier plan capte le clavier, les raccourcis ne
            // remontent donc que lorsque cette fenêtre-ci a le focus. Le suivi automatique, lui,
            // fonctionne depuis la session.
            HotkeyText = AppServices.Hotkeys switch
            {
                { Registered: true } => "Quand cette fenêtre a le focus :   " + RdpHotkeyService.Description,
                null => RdpHotkeyService.Description,
                _ => "Raccourcis clavier indisponibles (combinaisons déjà prises par une autre "
                     + "application) : utilisez les boutons ci-dessus."
            };
        }
        catch (Exception ex)
        {
            Log.Write($"Lecture des sessions ouvertes : {ex.Message}");
        }
    }

    private void RebuildTargets(IList<MonitorInfo> monitors)
    {
        var ordered = MonitorEnumerator.LeftToRight(monitors);
        string signature = string.Join("|", ordered.Select(m => $"{m.Order}:{m.PositionLabel}:{m.ResolutionText}"));
        if (signature == _targetSignature && Targets.Count == ordered.Count) return;

        _targetSignature = signature;
        Targets.Clear();
        foreach (var monitor in ordered)
            Targets.Add(new ScreenTargetViewModel(monitor, MoveToScreenAsync));
    }

    // ------------------------------------------------------------------ déplacements

    /// <summary>Déplace la session vers l'écran de rang <paramref name="order"/> (1 = le plus à gauche).</summary>
    public async Task MoveToScreenAsync(int order)
    {
        var target = _monitors.Monitors.FirstOrDefault(m => m.Order == order);
        if (target is null)
        {
            StatusText = $"Ce poste n'a pas d'écran numéro {order} : {_monitors.Monitors.Count} écran(s) détecté(s).";
            return;
        }
        await MoveAsync(PickWindow(), target);
    }

    [RelayCommand]
    private Task MoveNextAsync() => StepAsync(+1);

    [RelayCommand]
    private Task MovePreviousAsync() => StepAsync(-1);

    private async Task StepAsync(int direction)
    {
        var window = PickWindow();
        if (window is null) { ReportNoWindow(); return; }

        var target = RdpWindowMover.Neighbour(window, _monitors.Monitors, direction);
        if (target is null)
        {
            StatusText = "Aucun écran détecté : impossible de déplacer la session.";
            return;
        }
        await MoveAsync(window, target);
    }

    private async Task MoveAsync(RdpSessionWindow? window, MonitorInfo target)
    {
        if (window is null) { ReportNoWindow(); return; }
        if (_moving) return;

        _moving = true;
        try
        {
            // La bascule en plein écran demande une renégociation avec le serveur : elle peut durer
            // une seconde ou deux, autant le dire pendant ce temps-là.
            StatusText = $"Mise en plein écran de « {window.Server} » sur l'écran "
                       + $"{target.Order} {target.PositionLabel}…";

            // Un déplacement demandé explicitement met toujours la session en plein écran sur
            // l'écran d'arrivée : c'est ce que veut dire « déplacer vers cet écran ».
            var result = await RdpWindowMover.MoveToAsync(window, target, _monitors.Monitors);
            StatusText = result.Message;
        }
        catch (Exception ex)
        {
            StatusText = $"Déplacement impossible : {ex.Message}";
            Log.Write($"Déplacement de la session : {ex}");
        }
        finally
        {
            _moving = false;
        }

        Refresh();
    }

    [RelayCommand]
    private void RefreshWindows()
    {
        Refresh();
        StatusText = HasWindows
            ? $"{Windows.Count} session(s) Bureau à distance ouverte(s)."
            : "Aucune session Bureau à distance ouverte en ce moment.";
    }

    [RelayCommand]
    private void ActivateWindow()
    {
        var window = Selected?.Window;
        if (window is null) { ReportNoWindow(); return; }

        StatusText = RdpWindowMover.Activate(window)
            ? $"« {window.Server} » a été mise au premier plan."
            : $"Windows a refusé de mettre « {window.Server} » au premier plan : cliquez sur la session.";
    }

    /// <summary>
    /// La fenêtre à déplacer : celle du premier plan si l'utilisateur est dans une session — c'est
    /// le cas des raccourcis clavier — sinon celle choisie dans la liste.
    /// </summary>
    private RdpSessionWindow? PickWindow()
    {
        var foreground = RdpWindowFinder.Foreground();
        if (foreground is not null) return foreground;

        if (Selected is not null)
        {
            // L'état a pu changer depuis le dernier rafraîchissement (plein écran, réduction…).
            var fresh = RdpWindowFinder.Reread(Selected.Handle);
            if (fresh is not null) return fresh;
        }

        Refresh();
        return Selected?.Window;
    }

    private void ReportNoWindow()
    {
        Refresh();
        StatusText = "Aucune session Bureau à distance ouverte : lancez une session, "
                   + "puis revenez déplacer sa fenêtre.";
    }
}
