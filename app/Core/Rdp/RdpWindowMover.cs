using System.Drawing;
using System.Runtime.InteropServices;

namespace TermServMultiScreen.Core.Rdp;

public sealed class RdpMoveResult
{
    public required bool Success { get; init; }
    public required string Message { get; init; }
    public Rectangle Before { get; init; }
    public Rectangle After { get; init; }

    /// <summary>Comment le déplacement a été obtenu : tracé dans le journal.</summary>
    public string Strategy { get; init; } = "";
}

/// <summary>
/// Déplace une session Bureau à distance <b>déjà ouverte</b> d'un écran à l'autre, en gardant son
/// plein écran. Rien n'est réécrit : ni le fichier .rdp, ni <c>selectedmonitors</c>, ni la connexion
/// enregistrée.
///
/// Tout tient à une chose : <b>comment mstsc fait son plein écran</b>. Ce n'est pas une fenêtre sans
/// bordure aux dimensions de l'écran, c'est une fenêtre <b>agrandie</b> dont le cadre — barre de
/// titre comprise — dépasse hors de l'écran, mstsc répondant à WM_GETMINMAXINFO pour couvrir
/// l'écran entier et non la seule zone de travail. Deux conséquences dictent tout ce fichier :
///
/// <list type="bullet">
///   <item>la <b>redimensionner</b> aux dimensions exactes de l'écran fait rentrer sa barre de titre
///         dans l'écran : on obtient une grande fenêtre bordée, pas un plein écran. On ne
///         redimensionne donc jamais un plein écran — on le translate en conservant son débord.</item>
///   <item>une fenêtre agrandie ne se déplace pas avec SetWindowPos : Windows la replace sur son
///         écran. Il faut passer par <c>SetWindowPlacement</c>, qui déplace la taille rétablie et
///         laisse Windows refaire l'agrandissement sur le nouvel écran — mstsc y rejoue sa règle et
///         retrouve son plein écran.</item>
/// </list>
///
/// Une session simplement en fenêtre, elle, ne bascule pas en plein écran en l'agrandissant :
/// seule <c>Ctrl + Alt + Attn</c> le fait. C'est donc cette frappe qui est demandée, et uniquement
/// si la session a le focus.
/// </summary>
public static class RdpWindowMover
{
    /// <summary>mstsc peut arrondir de quelques pixels : le replacement reste considéré comme fait.</summary>
    private const int Tolerance = 4;

    /// <summary>Part d'un écran qu'une fenêtre doit couvrir pour compter comme « présente dessus ».</summary>
    private const double CoverageRatio = 0.25;

    /// <summary>Temps laissé à mstsc pour renégocier avec le serveur et redessiner.</summary>
    private const int FullScreenTimeoutMs = 1800;

    // ------------------------------------------------------------------ écrans

    /// <summary>Coordonnées de l'écran entier : celles qui servent à poser une fenêtre dessus.</summary>
    public static Rectangle BoundsOf(MonitorInfo monitor) => ScreenBounds(monitor);

    /// <summary>L'écran qui porte la plus grande partie de la fenêtre.</summary>
    public static MonitorInfo? CurrentMonitor(RdpSessionWindow window, IEnumerable<MonitorInfo> monitors) =>
        MonitorOf(window.Bounds, monitors);

    /// <summary>L'écran qui porte la plus grande partie de ce rectangle, null s'il flotte hors écran.</summary>
    public static MonitorInfo? MonitorOf(Rectangle bounds, IEnumerable<MonitorInfo> monitors)
    {
        MonitorInfo? best = null;
        long bestArea = 0;

        foreach (var monitor in monitors)
        {
            var shared = Rectangle.Intersect(ScreenBounds(monitor), bounds);
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

        // Le tour de tous les écrans, sans jamais rester bloqué au bord.
        int next = ((index + direction) % ordered.Count + ordered.Count) % ordered.Count;
        return ordered[next];
    }

    // ------------------------------------------------------------------ déplacement

    /// <summary>
    /// Pose la session sur l'écran demandé, en plein écran. La méthode est choisie d'après l'état
    /// réel de la fenêtre — les trois cas sont journalisés, ce qui permet de savoir sur un poste
    /// donné laquelle a servi.
    /// </summary>
    public static async Task<RdpMoveResult> MoveToAsync(
        RdpSessionWindow window, MonitorInfo target, IEnumerable<MonitorInfo> monitors)
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

        Rectangle before = CurrentRect(hwnd);
        if (before.Width <= 0)
            return Failed("La position actuelle de la fenêtre n'a pas pu être lue.");

        Rectangle screen = ScreenBounds(target);
        var source = MonitorOf(before, monitors);
        Rectangle sourceScreen = source is null ? before : ScreenBounds(source);

        bool zoomed = NativeMethods.IsZoomed(hwnd);
        bool coversItsScreen = before.Contains(sourceScreen);
        int spanned = CoveredScreens(before, monitors);

        Log.Write($"Déplacement de « {window.Title} » vers écran {target.Order} {target.PositionLabel} — "
                + $"état : {Describe(before)}, "
                + $"{(IsBorderless(hwnd) ? "sans bordure" : "avec bordure")}, "
                + $"{(zoomed ? "agrandie" : "non agrandie")}, "
                + $"couvre son écran : {(coversItsScreen ? "oui" : "non")}, écrans occupés : {spanned}");

        // Session étalée sur plusieurs écrans : translation pure, sa taille ne doit pas bouger.
        if (spanned > 1)
            return await TranslateAsync(window, before, sourceScreen, target, monitors,
                "translation (session multi-écrans)");

        // A. Plein écran de mstsc, c'est-à-dire fenêtre agrandie : c'est Windows qui doit refaire la
        //    géométrie sur l'écran visé, sinon il la ramènerait sur celui de départ.
        if (zoomed)
        {
            var maximised = await ReMaximiseAsync(window, before, target, monitors);
            if (maximised is not null) return maximised;

            // Échec : on relit l'état, il a pu changer, avant de tenter la translation.
            before = CurrentRect(hwnd);
            source = MonitorOf(before, monitors);
            sourceScreen = source is null ? before : ScreenBounds(source);
            coversItsScreen = before.Contains(sourceScreen);
        }

        // B. Couvre déjà son écran sans être agrandie : plein écran sans bordure, ou cadre débordant
        //    hors de l'écran. Dans les deux cas, on translate en conservant exactement ce débord.
        if (coversItsScreen)
            return await TranslateAsync(window, before, sourceScreen, target, monitors,
                "translation du plein écran");

        // C. Vraie fenêtre : la poser sur l'écran visé, puis demander à mstsc son plein écran.
        return await WindowToFullScreenAsync(window, before, target, monitors);
    }

    /// <summary>
    /// Cas A : refaire l'agrandissement sur l'écran visé. On déplace la <b>taille rétablie</b> vers
    /// cet écran — c'est elle qui dit à Windows où agrandir — puis on redemande l'agrandissement.
    /// Renvoie null si la manœuvre n'a pas abouti, pour laisser essayer autrement.
    /// </summary>
    private static async Task<RdpMoveResult?> ReMaximiseAsync(
        RdpSessionWindow window, Rectangle before, MonitorInfo target, IEnumerable<MonitorInfo> monitors)
    {
        nint hwnd = window.Handle;
        var placement = new NativeMethods.WINDOWPLACEMENT
        {
            length = Marshal.SizeOf<NativeMethods.WINDOWPLACEMENT>()
        };

        if (!NativeMethods.GetWindowPlacement(hwnd, ref placement))
        {
            Log.Write($"GetWindowPlacement a échoué (erreur {Marshal.GetLastWin32Error()}).");
            return null;
        }

        Rectangle work = WorkBounds(target);
        Rectangle normal = ToRectangle(placement.rcNormalPosition);

        // La taille rétablie est conservée telle quelle, seulement recentrée sur l'écran visé : la
        // session reste ce qu'elle était si l'utilisateur quitte le plein écran ensuite.
        int width = normal.Width > 0 ? Math.Min(normal.Width, work.Width) : work.Width * 3 / 4;
        int height = normal.Height > 0 ? Math.Min(normal.Height, work.Height) : work.Height * 3 / 4;
        placement.rcNormalPosition = FromRectangle(new Rectangle(
            work.X + (work.Width - width) / 2, work.Y + (work.Height - height) / 2, width, height));
        placement.showCmd = NativeMethods.SW_MAXIMIZE;

        if (!NativeMethods.SetWindowPlacement(hwnd, ref placement))
        {
            Log.Write($"SetWindowPlacement a échoué (erreur {Marshal.GetLastWin32Error()}).");
            return null;
        }

        Rectangle screen = ScreenBounds(target);
        if (!await WaitForCoverageAsync(hwnd, screen, 900))
        {
            Log.Write($"SetWindowPlacement n'a pas couvert l'écran visé : {Describe(CurrentRect(hwnd))}.");
            return null;
        }

        Rectangle after = CurrentRect(hwnd);
        Log.Write($"Plein écran refait sur écran {target.Order} par SetWindowPlacement : {Describe(after)}");

        return new RdpMoveResult
        {
            Success = true,
            Before = before,
            After = after,
            Strategy = "SetWindowPlacement",
            Message = $"« {window.Server} » est en plein écran sur l'écran {target.Order} {target.PositionLabel}."
        };
    }

    /// <summary>
    /// Cas B : translation vers l'écran visé en conservant le débord de chaque côté. Un plein écran
    /// mstsc déborde de son écran de la largeur de son cadre : reproduire ce débord sur l'écran
    /// d'arrivée est ce qui garde la barre de titre hors de vue.
    /// </summary>
    private static async Task<RdpMoveResult> TranslateAsync(
        RdpSessionWindow window, Rectangle before, Rectangle sourceScreen, MonitorInfo target,
        IEnumerable<MonitorInfo> monitors, string strategy)
    {
        nint hwnd = window.Handle;
        Rectangle screen = ScreenBounds(target);

        Rectangle wanted = Rectangle.FromLTRB(
            screen.Left - (sourceScreen.Left - before.Left),
            screen.Top - (sourceScreen.Top - before.Top),
            screen.Right + (before.Right - sourceScreen.Right),
            screen.Bottom + (before.Bottom - sourceScreen.Bottom));

        // La barre de connexion flottante est repérée avant, tant qu'elle est encore sur l'écran de
        // départ : c'est sa position qui permet de la reconnaître.
        var bars = RdpWindowFinder.FindBars(window.ProcessId, sourceScreen, hwnd);

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

        MoveBars(bars, after.X - before.X, after.Y - before.Y);

        bool success = after.Width > 0 && MajorityScreen(after, monitors) == screen;
        string where = $"écran {target.Order} {target.PositionLabel}";
        Log.Write($"{strategy} : {Describe(before)} → {Describe(after)}, cible {Describe(wanted)} "
                + $"sur {where} — {(success ? "réussi" : "ÉCHEC")}");

        return new RdpMoveResult
        {
            Success = success,
            Before = before,
            After = after,
            Strategy = strategy,
            Message = success
                ? $"« {window.Server} » est en plein écran sur l'{where}."
                : $"Le Bureau à distance a refusé de déplacer « {window.Server} » vers l'{where}. "
                  + "Quittez le plein écran (Ctrl + Alt + Attn), déplacez la fenêtre, puis remettez-la en plein écran."
        };
    }

    /// <summary>
    /// Cas C : une session en fenêtre. L'agrandir ne la met pas en plein écran — mstsc ne bascule
    /// que sur Ctrl + Alt + Attn. On la pose donc sur l'écran visé, puis on demande la bascule.
    /// </summary>
    private static async Task<RdpMoveResult> WindowToFullScreenAsync(
        RdpSessionWindow window, Rectangle before, MonitorInfo target, IEnumerable<MonitorInfo> monitors)
    {
        nint hwnd = window.Handle;
        Rectangle screen = ScreenBounds(target);
        string where = $"écran {target.Order} {target.PositionLabel}";

        // La bascule porte sur l'écran occupé par la fenêtre : on l'y rentre entièrement d'abord,
        // par simple translation, donc sans toucher à la résolution de la session.
        if (!screen.Contains(before))
        {
            MoveInside(hwnd, before, screen);
            await Task.Delay(120);
        }

        if (await AskNativeFullScreenAsync(hwnd, screen))
        {
            Rectangle native = CurrentRect(hwnd);
            Log.Write($"Plein écran natif obtenu sur {where} : {Describe(native)}");
            return new RdpMoveResult
            {
                Success = true,
                Before = before,
                After = native,
                Strategy = "Ctrl+Alt+Attn",
                Message = $"« {window.Server} » est en plein écran sur l'{where}."
            };
        }

        // La bascule a peut-être eu lieu, mais sur un autre écran : c'est ce que fait mstsc quand la
        // session a été ouverte avant la correction de maximizetocurrentdisplays. La session est
        // alors agrandie, donc SetWindowPlacement sait la déplacer sans lui faire perdre son plein
        // écran — et l'utilisateur obtient malgré tout ce qu'il demandait.
        if (NativeMethods.IsZoomed(hwnd))
        {
            Log.Write("Plein écran natif obtenu sur un autre écran : déplacement par SetWindowPlacement.");
            var relocated = await ReMaximiseAsync(window, before, target, monitors);
            if (relocated is not null) return relocated;
        }

        // Repli : couvrir l'écran. La barre de titre restera visible — on le dit plutôt que de
        // laisser croire à un plein écran.
        Apply(hwnd, screen, NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
        await Task.Delay(120);

        Rectangle after = CurrentRect(hwnd);
        bool success = after.Width > 0 && MajorityScreen(after, monitors) == screen;
        Log.Write($"Plein écran natif non obtenu : {Describe(after)} — repli sur la couverture de l'{where}.");

        return new RdpMoveResult
        {
            Success = success,
            Before = before,
            After = after,
            Strategy = "couverture de l'écran",
            Message = success
                ? $"« {window.Server} » couvre l'{where}, mais garde sa barre de titre : mettez le "
                  + "focus dans la session puis appuyez sur Ctrl + Alt + Attn pour le plein écran."
                : $"Le Bureau à distance a refusé de déplacer « {window.Server} » vers l'{where}."
        };
    }

    /// <summary>
    /// Envoie Ctrl + Alt + Attn, la bascule plein écran de mstsc, et attend qu'elle ait pris.
    /// La frappe n'est envoyée que si la session a réellement le focus : sinon elle partirait dans
    /// une autre application.
    /// </summary>
    private static async Task<bool> AskNativeFullScreenAsync(nint hwnd, Rectangle screen)
    {
        if (NativeMethods.GetForegroundWindow() != hwnd)
        {
            Log.Write("Plein écran natif non demandé : la session n'a pas le focus.");
            return false;
        }

        SendCtrlAltBreak();
        return await WaitForCoverageAsync(hwnd, screen, FullScreenTimeoutMs);
    }

    private static void SendCtrlAltBreak()
    {
        try
        {
            Down(NativeMethods.VK_CONTROL);
            Down(NativeMethods.VK_MENU);
            Down(NativeMethods.VK_CANCEL);
            Up(NativeMethods.VK_CANCEL);
            Up(NativeMethods.VK_MENU);
            Up(NativeMethods.VK_CONTROL);
        }
        catch (Exception ex)
        {
            Log.Write($"Envoi de Ctrl + Alt + Attn : {ex.Message}");
        }

        // Le code de scan est fourni : le crochet clavier de mstsc s'appuie dessus.
        static void Down(byte key) =>
            NativeMethods.keybd_event(key, (byte)NativeMethods.MapVirtualKey(key, 0), 0, 0);

        static void Up(byte key) =>
            NativeMethods.keybd_event(key, (byte)NativeMethods.MapVirtualKey(key, 0),
                NativeMethods.KEYEVENTF_KEYUP, 0);
    }

    /// <summary>
    /// Attend que la fenêtre couvre l'écran entier — le seul signe qui vaille, quelle que soit la
    /// façon dont le client fabrique son plein écran. L'attente est active : la bascule demande une
    /// renégociation avec le serveur, dont la durée dépend de la liaison.
    /// </summary>
    private static async Task<bool> WaitForCoverageAsync(nint hwnd, Rectangle screen, int timeoutMs)
    {
        for (int waited = 0; waited < timeoutMs; waited += 100)
        {
            await Task.Delay(100);
            if (!NativeMethods.IsWindow(hwnd)) return false;
            if (CurrentRect(hwnd).Contains(screen)) return true;
        }
        return false;
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

    /// <summary>Coordonnées utilisables pour poser une fenêtre en plein écran sur cet écran.</summary>
    private static Rectangle ScreenBounds(MonitorInfo monitor) =>
        monitor.WindowBounds is { Width: > 0, Height: > 0 } ? monitor.WindowBounds : monitor.LayoutBounds;

    /// <summary>Zone hors barre des tâches, pour une taille rétablie.</summary>
    private static Rectangle WorkBounds(MonitorInfo monitor) =>
        monitor.WindowWorkArea is { Width: > 0, Height: > 0 } ? monitor.WindowWorkArea : ScreenBounds(monitor);

    /// <summary>Rentre la fenêtre dans l'écran sans la redimensionner : la bascule fera le reste.</summary>
    private static void MoveInside(nint hwnd, Rectangle window, Rectangle screen)
    {
        int width = Math.Min(window.Width, screen.Width);
        int height = Math.Min(window.Height, screen.Height);
        int x = Math.Clamp(window.X, screen.Left, screen.Right - width);
        int y = Math.Clamp(window.Y, screen.Top, screen.Bottom - height);

        NativeMethods.SetWindowPos(hwnd, 0, x, y, 0, 0,
            NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
    }

    private static void Apply(nint hwnd, Rectangle wanted, uint flags)
    {
        if (!NativeMethods.SetWindowPos(hwnd, 0, wanted.X, wanted.Y, wanted.Width, wanted.Height, flags))
            Log.Write($"SetWindowPos a échoué (erreur {Marshal.GetLastWin32Error()}) pour {Describe(wanted)}.");
    }

    private static bool Landed(nint hwnd, Rectangle wanted, out Rectangle actual)
    {
        actual = CurrentRect(hwnd);
        return Math.Abs(actual.X - wanted.X) <= Tolerance && Math.Abs(actual.Y - wanted.Y) <= Tolerance;
    }

    /// <summary>
    /// L'écran qui porte la plus grande partie de la fenêtre. Sert à vérifier après coup que la
    /// fenêtre a bien atterri sur l'écran demandé. Sans recouvrement, le rectangle de la fenêtre est
    /// renvoyé tel quel — donc jamais égal à un écran.
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

    private static bool IsBorderless(nint hwnd)
    {
        uint style = NativeMethods.GetWindowStyles(hwnd, NativeMethods.GWL_STYLE);
        return (style & (NativeMethods.WS_CAPTION | NativeMethods.WS_THICKFRAME)) == 0;
    }

    private static Rectangle CurrentRect(nint hwnd) =>
        NativeMethods.GetWindowRect(hwnd, out var rect) ? ToRectangle(rect) : Rectangle.Empty;

    private static Rectangle ToRectangle(NativeMethods.RECT rect) =>
        new(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);

    private static NativeMethods.RECT FromRectangle(Rectangle rectangle) => new()
    {
        Left = rectangle.Left,
        Top = rectangle.Top,
        Right = rectangle.Right,
        Bottom = rectangle.Bottom
    };

    private static string Describe(Rectangle rectangle) =>
        $"{rectangle.Width}×{rectangle.Height} en ({rectangle.X},{rectangle.Y})";

    private static RdpMoveResult Failed(string message)
    {
        Log.Write($"Déplacement de fenêtre impossible : {message}");
        return new RdpMoveResult { Success = false, Message = message };
    }
}
