using System.Drawing;
using System.Runtime.InteropServices;

namespace TermServMultiScreen.Core.Rdp;

public sealed class RdpMoveResult
{
    public required bool Success { get; init; }
    public required string Message { get; init; }
    public Rectangle Before { get; init; }
    public Rectangle After { get; init; }
}

/// <summary>
/// Déplace une session Bureau à distance <b>déjà ouverte</b> d'un écran à l'autre.
///
/// Rien n'est réécrit : ni le fichier .rdp, ni <c>selectedmonitors</c>, ni la connexion
/// enregistrée. Seule la position de la fenêtre change, exactement comme si Windows l'avait
/// déplacée — la prochaine ouverture retrouvera donc les écrans configurés.
///
/// C'est la seule voie possible : en plein écran multi-écrans, mstsc ne laisse pas attraper sa
/// fenêtre à la souris, et il n'existe aucune commande pour lui demander de changer d'écran.
/// </summary>
public static class RdpWindowMover
{
    /// <summary>mstsc peut arrondir de quelques pixels : le replacement reste considéré comme fait.</summary>
    private const int Tolerance = 4;

    /// <summary>Part d'un écran qu'une fenêtre doit couvrir pour compter comme « présente dessus ».</summary>
    private const double CoverageRatio = 0.25;

    /// <summary>Coordonnées à utiliser pour poser une fenêtre en plein écran sur cet écran.</summary>
    private static Rectangle ScreenBounds(MonitorInfo monitor) =>
        monitor.WindowBounds is { Width: > 0, Height: > 0 } ? monitor.WindowBounds : monitor.LayoutBounds;

    /// <summary>Zone hors barre des tâches, pour une session en fenêtre.</summary>
    private static Rectangle WorkBounds(MonitorInfo monitor) =>
        monitor.WindowWorkArea is { Width: > 0, Height: > 0 } ? monitor.WindowWorkArea : ScreenBounds(monitor);

    /// <summary>L'écran qui porte la plus grande partie de la fenêtre.</summary>
    public static MonitorInfo? CurrentMonitor(RdpSessionWindow window, IEnumerable<MonitorInfo> monitors)
    {
        MonitorInfo? best = null;
        long bestArea = 0;

        foreach (var monitor in monitors)
        {
            var shared = Rectangle.Intersect(ScreenBounds(monitor), window.Bounds);
            long area = (long)Math.Max(0, shared.Width) * Math.Max(0, shared.Height);
            if (area > bestArea) { bestArea = area; best = monitor; }
        }

        return bestArea > 0 ? best : null;
    }

    /// <summary>Écran voisin dans l'ordre gauche → droite : -1 pour le précédent, +1 pour le suivant.</summary>
    public static MonitorInfo? Neighbour(RdpSessionWindow window, IEnumerable<MonitorInfo> monitors, int direction)
    {
        var ordered = MonitorEnumerator.LeftToRight(monitors);
        if (ordered.Count == 0) return null;
        if (ordered.Count == 1) return ordered[0];

        var current = CurrentMonitor(window, ordered);
        int index = current is null ? 0 : Math.Max(0, ordered.IndexOf(current));

        // Le tour de tous les écrans, sans jamais se retrouver bloqué au bord.
        int next = ((index + direction) % ordered.Count + ordered.Count) % ordered.Count;
        return ordered[next];
    }

    /// <summary>
    /// Pose la fenêtre sur l'écran demandé. <paramref name="fitToScreen"/> autorise le
    /// redimensionnement quand l'écran d'arrivée n'a pas la même taille ; une session étalée sur
    /// plusieurs écrans n'est jamais redimensionnée, elle est seulement translatée.
    /// </summary>
    public static async Task<RdpMoveResult> MoveToAsync(
        RdpSessionWindow window, MonitorInfo target, IEnumerable<MonitorInfo> monitors, bool fitToScreen)
    {
        nint hwnd = window.Handle;
        if (!NativeMethods.IsWindow(hwnd))
            return Failed("Cette fenêtre n'existe plus — actualisez la liste des sessions ouvertes.");

        if (NativeMethods.IsIconic(hwnd))
        {
            // Une fenêtre réduite n'a pas de position utilisable : on la rétablit d'abord.
            NativeMethods.ShowWindow(hwnd, NativeMethods.SW_RESTORE);
            await Task.Delay(150);
        }

        if (!NativeMethods.GetWindowRect(hwnd, out var initial))
            return Failed("La position actuelle de la fenêtre n'a pas pu être lue.");
        Rectangle before = ToRectangle(initial);

        // Plein écran au sens de ce qui compte ici : la fenêtre couvre un écran entier, barre des
        // tâches comprise. L'absence de bordure suffit d'ordinaire à le dire, mais un client qui
        // garderait un style de bordure en plein écran ne doit pas être pris pour une fenêtre
        // ordinaire — on se fie donc aussi à ce qui est réellement recouvert.
        bool fullScreen = window.FullScreen || CoversAnyScreen(before, monitors);

        // Tant qu'elle est agrandie, Windows garde la fenêtre sur son écran : il faut la rétablir,
        // la déplacer, puis l'agrandir de nouveau sur l'écran d'arrivée. Une session en plein écran
        // ne passe jamais par là : rétablir puis réagrandir la ramènerait à la zone de travail,
        // c'est-à-dire barre des tâches visible et plein écran perdu.
        bool maximized = !fullScreen && NativeMethods.IsZoomed(hwnd);
        Rectangle current = before;
        if (maximized)
        {
            NativeMethods.ShowWindow(hwnd, NativeMethods.SW_RESTORE);
            await Task.Delay(150);
            if (NativeMethods.GetWindowRect(hwnd, out var restored)) current = ToRectangle(restored);
        }

        Rectangle screen = fullScreen ? ScreenBounds(target) : WorkBounds(target);
        bool spansSeveralScreens = CoveredScreens(before, monitors) > 1;
        bool fit = fitToScreen && fullScreen && !spansSeveralScreens;
        Rectangle wanted = Place(current, screen, fit);

        // La barre de connexion flottante est repérée avant le déplacement : c'est sa position sur
        // l'écran de départ qui permet de la reconnaître.
        List<nint> bars = fullScreen
            ? RdpWindowFinder.FindBars(window.ProcessId, MajorityScreen(before, monitors), hwnd)
            : [];

        Apply(hwnd, wanted, NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
        await Task.Delay(90);

        if (!Landed(hwnd, wanted, out Rectangle after))
        {
            // Certaines versions de mstsc replacent leur fenêtre au premier essai : on insiste une
            // fois en demandant en plus le recalcul du cadre.
            Apply(hwnd, wanted,
                NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_FRAMECHANGED);
            await Task.Delay(180);
            Landed(hwnd, wanted, out after);
        }

        if (maximized)
        {
            NativeMethods.ShowWindow(hwnd, NativeMethods.SW_MAXIMIZE);
            await Task.Delay(90);
            if (NativeMethods.GetWindowRect(hwnd, out var zoomed)) after = ToRectangle(zoomed);
        }

        // La barre de connexion suit du même décalage que la session.
        MoveBars(bars, after.X - before.X, after.Y - before.Y);

        // Le seul verdict qui compte : la fenêtre est-elle sur l'écran demandé ? On compare donc
        // l'écran qui porte le plus de la fenêtre, et non un rectangle exact — mstsc ajuste.
        bool success = after.Width > 0 && MajorityScreen(after, monitors) == ScreenBounds(target);
        string where = $"écran {target.Order} {target.PositionLabel}";

        Log.Write($"Déplacement de fenêtre « {window.Title} » ({window.ModeText}) : "
                + $"{Describe(before)} → {Describe(after)}, cible {Describe(wanted)} sur {where} — "
                + (success ? "réussi" : "ÉCHEC"));

        return new RdpMoveResult
        {
            Success = success,
            Before = before,
            After = after,
            Message = success
                ? $"« {window.Server} » est maintenant sur l'{where}"
                  + (after.Size != before.Size ? $", en {after.Width} × {after.Height}." : ".")
                  + (spansSeveralScreens ? " La session couvre plusieurs écrans : elle a été translatée sans changer de taille." : "")
                : $"Le Bureau à distance a refusé de déplacer « {window.Server} » vers l'{where}. "
                  + "Quittez le plein écran (Ctrl + Alt + Attn), déplacez la fenêtre, puis remettez-la en plein écran.",
        };
    }

    /// <summary>Met la session au premier plan, sur l'écran où elle se trouve.</summary>
    public static bool Activate(RdpSessionWindow window)
    {
        try
        {
            if (!NativeMethods.IsWindow(window.Handle)) return false;
            if (NativeMethods.IsIconic(window.Handle))
                NativeMethods.ShowWindow(window.Handle, NativeMethods.SW_RESTORE);
            return NativeMethods.SetForegroundWindow(window.Handle);
        }
        catch (Exception ex)
        {
            Log.Write($"Mise au premier plan de la session : {ex.Message}");
            return false;
        }
    }

    // ------------------------------------------------------------------ géométrie

    /// <summary>
    /// Où poser la fenêtre. Sans redimensionnement, elle est centrée sur l'écran d'arrivée ; si
    /// elle est plus grande que lui, elle est calée en haut à gauche — c'est ce qui garde le menu
    /// Démarrer distant et la barre de connexion visibles.
    /// </summary>
    private static Rectangle Place(Rectangle window, Rectangle screen, bool fit)
    {
        int width = fit || window.Width <= 0 ? screen.Width : window.Width;
        int height = fit || window.Height <= 0 ? screen.Height : window.Height;

        int x = screen.X + Math.Max(0, (screen.Width - width) / 2);
        int y = screen.Y + Math.Max(0, (screen.Height - height) / 2);
        return new Rectangle(x, y, width, height);
    }

    private static void Apply(nint hwnd, Rectangle wanted, uint flags)
    {
        if (!NativeMethods.SetWindowPos(hwnd, 0, wanted.X, wanted.Y, wanted.Width, wanted.Height, flags))
            Log.Write($"SetWindowPos a échoué (erreur {Marshal.GetLastWin32Error()}) pour {Describe(wanted)}.");
    }

    /// <summary>
    /// L'écran qui porte la plus grande partie de la fenêtre. Sert deux fois : repérer l'écran de
    /// départ, et vérifier après coup que la fenêtre a bien atterri sur l'écran demandé. Sans
    /// recouvrement, le rectangle de la fenêtre est renvoyé tel quel — donc jamais égal à un écran.
    /// </summary>
    private static Rectangle MajorityScreen(Rectangle window, IEnumerable<MonitorInfo> monitors)
    {
        Rectangle best = window;
        long bestArea = 0;

        foreach (var monitor in monitors)
        {
            Rectangle screen = ScreenBounds(monitor);
            var shared = Rectangle.Intersect(screen, window);
            long area = (long)Math.Max(0, shared.Width) * Math.Max(0, shared.Height);
            if (area > bestArea) { bestArea = area; best = screen; }
        }

        return best;
    }

    private static void MoveBars(List<nint> bars, int deltaX, int deltaY)
    {
        if (bars.Count == 0 || (deltaX == 0 && deltaY == 0)) return;

        foreach (nint bar in bars)
        {
            try
            {
                if (!NativeMethods.IsWindow(bar) || !NativeMethods.GetWindowRect(bar, out var rect)) continue;
                NativeMethods.SetWindowPos(bar, 0, rect.Left + deltaX, rect.Top + deltaY, 0, 0,
                    NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
            }
            catch (Exception ex)
            {
                Log.Write($"Déplacement de la barre de connexion : {ex.Message}");
            }
        }
    }

    private static bool Landed(nint hwnd, Rectangle wanted, out Rectangle actual)
    {
        actual = NativeMethods.GetWindowRect(hwnd, out var rect) ? ToRectangle(rect) : Rectangle.Empty;
        return Math.Abs(actual.X - wanted.X) <= Tolerance && Math.Abs(actual.Y - wanted.Y) <= Tolerance;
    }

    /// <summary>La fenêtre recouvre-t-elle un écran entier, barre des tâches comprise ?</summary>
    private static bool CoversAnyScreen(Rectangle window, IEnumerable<MonitorInfo> monitors)
    {
        foreach (var monitor in monitors)
        {
            Rectangle screen = ScreenBounds(monitor);
            if (screen is { Width: > 0, Height: > 0 } && window.Contains(screen)) return true;
        }
        return false;
    }

    /// <summary>Nombre d'écrans réellement occupés par la fenêtre (un quart de l'écran au moins).</summary>
    private static int CoveredScreens(Rectangle window, IEnumerable<MonitorInfo> monitors)
    {
        int count = 0;
        foreach (var monitor in monitors)
        {
            Rectangle screen = ScreenBounds(monitor);
            double area = (double)screen.Width * screen.Height;
            if (area <= 0) continue;

            var shared = Rectangle.Intersect(screen, window);
            double covered = (double)Math.Max(0, shared.Width) * Math.Max(0, shared.Height);
            if (covered / area >= CoverageRatio) count++;
        }
        return count;
    }

    private static Rectangle ToRectangle(NativeMethods.RECT rect) =>
        new(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);

    private static string Describe(Rectangle rectangle) =>
        $"{rectangle.Width}×{rectangle.Height} en ({rectangle.X},{rectangle.Y})";

    private static RdpMoveResult Failed(string message)
    {
        Log.Write($"Déplacement de fenêtre impossible : {message}");
        return new RdpMoveResult { Success = false, Message = message };
    }
}
