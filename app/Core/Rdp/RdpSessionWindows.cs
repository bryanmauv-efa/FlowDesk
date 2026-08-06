using System.Diagnostics;
using System.Drawing;
using System.Globalization;

namespace TermServMultiScreen.Core.Rdp;

/// <summary>
/// Une fenêtre de session Bureau à distance ouverte en ce moment. C'est une photographie : la
/// fenêtre peut bouger, se réduire ou se fermer juste après. Seul <see cref="Handle"/> reste
/// valable d'une lecture à l'autre.
/// </summary>
public sealed class RdpSessionWindow
{
    public required nint Handle { get; init; }
    public required string Title { get; init; }
    public required string ClassName { get; init; }
    public required string ProcessName { get; init; }
    public required uint ProcessId { get; init; }

    /// <summary>Position et taille réelles sur le bureau virtuel, en pixels.</summary>
    public required Rectangle Bounds { get; init; }

    /// <summary>
    /// Session en plein écran : ni barre de titre ni bordure. C'est l'état produit par
    /// <c>screen mode id:i:2</c> avec <c>use multimon:i:1</c>, celui qu'on ne peut pas déplacer
    /// à la souris.
    /// </summary>
    public required bool FullScreen { get; init; }

    public required bool Maximized { get; init; }
    public required bool Minimized { get; init; }

    /// <summary>Nom du serveur, extrait du titre « serveur - Connexion Bureau à distance ».</summary>
    public string Server
    {
        get
        {
            int cut = Title.LastIndexOf(" - ", StringComparison.Ordinal);
            string server = (cut > 0 ? Title[..cut] : Title).Trim();
            return server.Length > 0 ? server : "session Bureau à distance";
        }
    }

    public string ModeText => FullScreen ? "plein écran" : Maximized ? "fenêtre agrandie" : "fenêtre";

    public string SizeText => string.Create(CultureInfo.InvariantCulture, $"{Bounds.Width} × {Bounds.Height}");
}

/// <summary>
/// Retrouve les fenêtres de session Bureau à distance ouvertes sur ce poste. On n'interroge
/// jamais un fichier .rdp pour ça : ce qui compte est ce qui est réellement affiché.
/// </summary>
public static class RdpWindowFinder
{
    /// <summary>Classe de la fenêtre de session de mstsc : le repère le plus fiable.</summary>
    public const string SessionClassName = "TscShellContainerClass";

    /// <summary>mstsc.exe (client Windows) et msrdc.exe (client Bureau à distance récent).</summary>
    private static readonly string[] ClientProcesses = ["mstsc", "msrdc"];

    /// <summary>
    /// Boîtes de dialogue et fenêtres de service du client : la fenêtre « Connexion Bureau à
    /// distance » d'avant connexion et l'invite de reconnexion sont des #32770.
    /// </summary>
    private static readonly string[] IgnoredClasses =
        ["#32770", "tooltips_class32", "OleMainThreadWndClass", "IME", "MSCTFIME UI"];

    /// <summary>En dessous, ce n'est pas une session : c'est la barre de connexion ou une invite.</summary>
    private const int MinimumWidth = 320;
    private const int MinimumHeight = 240;

    /// <summary>Gabarit d'une barre de connexion : large et très basse.</summary>
    private const int MaximumBarHeight = 60;
    private const int MinimumBarWidth = 200;

    /// <summary>Toutes les fenêtres de session ouvertes, de la gauche vers la droite du bureau.</summary>
    public static List<RdpSessionWindow> Find()
    {
        var clients = ClientProcessIds();
        List<RdpSessionWindow> found = [];

        bool Callback(nint hwnd, nint _)
        {
            try
            {
                var window = Describe(hwnd, clients);
                if (window is not null) found.Add(window);
            }
            catch
            {
                // Une fenêtre illisible (elle vient de se fermer) ne doit pas arrêter le balayage.
            }
            return true;
        }

        try
        {
            NativeMethods.EnumWindows(Callback, 0);
        }
        catch (Exception ex)
        {
            Log.Write($"Énumération des fenêtres Bureau à distance : {ex.Message}");
        }

        return found.OrderBy(w => w.Bounds.X).ThenBy(w => w.Bounds.Y).ToList();
    }

    /// <summary>
    /// La session au premier plan, ou null si l'utilisateur n'est pas dedans. C'est elle que
    /// visent les raccourcis clavier : on déplace la session dans laquelle on travaille.
    /// </summary>
    public static RdpSessionWindow? Foreground()
    {
        try
        {
            nint hwnd = NativeMethods.GetForegroundWindow();
            return hwnd == 0 ? null : Describe(hwnd, ClientProcessIds());
        }
        catch (Exception ex)
        {
            Log.Write($"Fenêtre au premier plan : {ex.Message}");
            return null;
        }
    }

    /// <summary>Relit l'état d'une fenêtre connue (position et mode ont pu changer).</summary>
    public static RdpSessionWindow? Reread(nint handle) =>
        NativeMethods.IsWindow(handle) ? Describe(handle, ClientProcessIds()) : null;

    /// <summary>
    /// Barres de connexion flottantes posées sur <paramref name="screen"/> : fenêtres larges et
    /// très basses appartenant au même processus que la session.
    ///
    /// Selon la version du client, la barre du plein écran est une fenêtre à part entière — elle
    /// doit alors suivre la session, sinon elle resterait affichée sur l'écran de départ. Là où
    /// c'est une fenêtre fille, elle se déplace d'elle-même et cette liste est vide.
    /// </summary>
    public static List<nint> FindBars(uint processId, Rectangle screen, nint except)
    {
        List<nint> bars = [];

        bool Callback(nint hwnd, nint _)
        {
            try
            {
                if (hwnd == except || !NativeMethods.IsWindowVisible(hwnd)) return true;

                NativeMethods.GetWindowThreadProcessId(hwnd, out uint owner);
                if (owner != processId) return true;
                if (!NativeMethods.GetWindowRect(hwnd, out var rect)) return true;

                var bounds = new Rectangle(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
                if (bounds.Height is <= 0 or > MaximumBarHeight) return true;
                if (bounds.Width < MinimumBarWidth) return true;

                // Seules les barres réellement posées sur l'écran de départ suivent la session.
                var centre = new Point(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);
                if (screen.Contains(centre)) bars.Add(hwnd);
            }
            catch
            {
                // Fenêtre disparue en cours de route : sans importance.
            }
            return true;
        }

        try
        {
            NativeMethods.EnumWindows(Callback, 0);
        }
        catch (Exception ex)
        {
            Log.Write($"Recherche de la barre de connexion : {ex.Message}");
        }

        return bars;
    }

    private static RdpSessionWindow? Describe(nint hwnd, IReadOnlyDictionary<uint, string> clients)
    {
        if (hwnd == 0 || !NativeMethods.IsWindowVisible(hwnd)) return null;

        // Une fenêtre possédée est une boîte de dialogue de la session, pas la session.
        if (NativeMethods.GetWindow(hwnd, NativeMethods.GW_OWNER) != 0) return null;

        uint style = NativeMethods.GetWindowStyles(hwnd, NativeMethods.GWL_STYLE);
        if ((style & NativeMethods.WS_CHILD) != 0) return null;

        uint extended = NativeMethods.GetWindowStyles(hwnd, NativeMethods.GWL_EXSTYLE);
        if ((extended & NativeMethods.WS_EX_TOOLWINDOW) != 0) return null;

        string className = NativeMethods.GetClassName(hwnd);
        if (className.Length == 0) return null;
        if (IgnoredClasses.Contains(className, StringComparer.OrdinalIgnoreCase)) return null;

        NativeMethods.GetWindowThreadProcessId(hwnd, out uint processId);
        bool fromClient = clients.TryGetValue(processId, out string? processName);
        bool sessionClass = string.Equals(className, SessionClassName, StringComparison.OrdinalIgnoreCase);

        // Le nom du processus suffit dans le cas courant ; la classe couvre les hôtes qui
        // embarquent le contrôle ActiveX du Bureau à distance sous un autre nom.
        if (!fromClient && !sessionClass) return null;

        if (!NativeMethods.GetWindowRect(hwnd, out var rect)) return null;
        var bounds = new Rectangle(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);

        // Une fenêtre réduite est placée hors écran par Windows : sa taille ne prouve rien.
        bool minimized = NativeMethods.IsIconic(hwnd);
        if (!minimized && (bounds.Width < MinimumWidth || bounds.Height < MinimumHeight)) return null;

        return new RdpSessionWindow
        {
            Handle = hwnd,
            Title = NativeMethods.GetWindowTitle(hwnd),
            ClassName = className,
            ProcessName = processName ?? className,
            ProcessId = processId,
            Bounds = bounds,
            FullScreen = (style & (NativeMethods.WS_CAPTION | NativeMethods.WS_THICKFRAME)) == 0,
            Maximized = NativeMethods.IsZoomed(hwnd),
            Minimized = minimized
        };
    }

    /// <summary>Identifiants des processus clients : filtre bien moins coûteux que fenêtre par fenêtre.</summary>
    private static Dictionary<uint, string> ClientProcessIds()
    {
        Dictionary<uint, string> map = [];
        foreach (string name in ClientProcesses)
        {
            try
            {
                foreach (var process in Process.GetProcessesByName(name))
                {
                    try { map[(uint)process.Id] = name; }
                    catch { /* processus déjà terminé */ }
                    finally { process.Dispose(); }
                }
            }
            catch (Exception ex)
            {
                Log.Write($"Recherche du processus {name}.exe : {ex.Message}");
            }
        }
        return map;
    }
}
