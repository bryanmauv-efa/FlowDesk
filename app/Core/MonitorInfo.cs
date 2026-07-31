using System.Drawing;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace TermServMultiScreen.Core;

/// <summary>
/// Un écran physique, avec les trois identités qui comptent :
/// <list type="bullet">
///   <item><b>RdpId</b> : l'identifiant attendu par mstsc (selectedmonitors) = rang d'énumération Win32.</item>
///   <item><b>WindowsNumber</b> : le numéro affiché par Paramètres &gt; Système &gt; Affichage (\\.\DISPLAYn).</item>
///   <item><b>Order</b> : le rang de gauche à droite (1..N), le seul que l'utilisateur manipule.</item>
/// </list>
/// </summary>
public sealed class MonitorInfo
{
    public int RdpId { get; set; }
    public string GdiDeviceName { get; set; } = "";
    public int WindowsNumber { get; set; }
    public bool IsPrimary { get; set; }

    /// <summary>Coordonnées utilisables pour positionner une fenêtre.</summary>
    public Rectangle WindowBounds { get; set; }
    public Rectangle WindowWorkArea { get; set; }
    /// <summary>Vérité DEVMODE : pixels réels, insensible au DPI.</summary>
    public Rectangle PhysicalBounds { get; set; }
    /// <summary>Rectangles retenus pour le plan et le tri gauche → droite.</summary>
    public Rectangle LayoutBounds { get; set; }
    public bool HasDevMode { get; set; }

    public string AdapterName { get; set; } = "";
    public string MonitorName { get; set; } = "";
    public int RefreshHz { get; set; }
    public int Bpp { get; set; }

    /// <summary>Identité persistante du port physique (survit aux redémarrages).</summary>
    public string StableKey { get; set; } = "";
    /// <summary>1..N de gauche à droite.</summary>
    public int Order { get; set; }
    /// <summary>Gauche / Centre / Droite / Haut / Bas / Écran n.</summary>
    public string PositionLabel { get; set; } = "";

    public Size Resolution => HasDevMode ? PhysicalBounds.Size : WindowBounds.Size;

    public string ResolutionText => string.Create(CultureInfo.InvariantCulture,
        $"{Resolution.Width} × {Resolution.Height}");

    /// <summary>Libellé court sans ambiguïté : « 2 · Centre (Windows 2) ».</summary>
    public string ShortLabel => $"{Order} · {PositionLabel} (Windows {WindowsNumber})";

    public string DetailText
    {
        get
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Écran {Order} ({PositionLabel})");
            sb.AppendLine($"Numéro Windows  : {WindowsNumber}   ({GdiDeviceName})");
            sb.AppendLine($"Identifiant RDP : {RdpId}   (selectedmonitors)");
            sb.AppendLine($"Résolution      : {ResolutionText}{(RefreshHz > 0 ? $"  @ {RefreshHz} Hz" : "")}");
            sb.AppendLine($"Position        : X={LayoutBounds.X}  Y={LayoutBounds.Y}");
            sb.AppendLine($"Écran principal : {(IsPrimary ? "oui" : "non")}");
            if (MonitorName.Length > 0) sb.AppendLine($"Moniteur        : {MonitorName}");
            if (AdapterName.Length > 0) sb.AppendLine($"Carte vidéo     : {AdapterName}");
            return sb.ToString().TrimEnd();
        }
    }
}

public static class MonitorEnumerator
{
    /// <summary>
    /// Énumère les écrans dans l'ordre exact de EnumDisplayMonitors — l'ordre que mstsc utilise
    /// pour numéroter selectedmonitors — puis leur attribue un rang de gauche à droite.
    /// </summary>
    public static List<MonitorInfo> Enumerate()
    {
        List<MonitorInfo> list = [];
        int index = 0;

        bool Callback(nint hMonitor, nint hdc, ref NativeMethods.RECT rc, nint data)
        {
            var mi = new NativeMethods.MONITORINFOEX
            {
                cbSize = Marshal.SizeOf<NativeMethods.MONITORINFOEX>(),
                szDevice = ""
            };

            var info = new MonitorInfo { RdpId = index++ };

            if (NativeMethods.GetMonitorInfo(hMonitor, ref mi))
            {
                info.GdiDeviceName = (mi.szDevice ?? "").TrimEnd('\0');
                info.IsPrimary = (mi.dwFlags & NativeMethods.MONITORINFOF_PRIMARY) != 0;
                info.WindowBounds = ToRectangle(mi.rcMonitor);
                info.WindowWorkArea = ToRectangle(mi.rcWork);
            }
            else
            {
                info.WindowBounds = ToRectangle(rc);
                info.WindowWorkArea = info.WindowBounds;
            }

            FillFromDevMode(info);
            FillNames(info);
            info.WindowsNumber = ParseDisplayNumber(info.GdiDeviceName, info.RdpId + 1);
            info.StableKey = BuildStableKey(info);

            list.Add(info);
            return true;
        }

        try
        {
            NativeMethods.EnumDisplayMonitors(0, 0, Callback, 0);
        }
        catch (Exception ex)
        {
            Log.Write($"EnumDisplayMonitors a échoué : {ex.Message}");
        }

        if (list.Count == 0)
        {
            // Filet de sécurité : ne jamais renvoyer une liste vide.
            Log.Write("Aucun écran énuméré : repli sur l'écran principal.");
            int w = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXSCREEN);
            int h = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYSCREEN);
            if (w <= 0 || h <= 0) { w = 1920; h = 1080; }
            list.Add(new MonitorInfo
            {
                RdpId = 0,
                GdiDeviceName = @"\\.\DISPLAY1",
                WindowsNumber = 1,
                IsPrimary = true,
                WindowBounds = new Rectangle(0, 0, w, h),
                WindowWorkArea = new Rectangle(0, 0, w, h),
                PhysicalBounds = new Rectangle(0, 0, w, h),
                StableKey = @"GDI:\\.\DISPLAY1"
            });
        }

        ChooseLayoutRects(list);
        AssignOrder(list);
        return list;
    }

    private static Rectangle ToRectangle(NativeMethods.RECT r) =>
        new(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);

    private static void FillFromDevMode(MonitorInfo info)
    {
        if (info.GdiDeviceName.Length == 0) return;
        try
        {
            var dm = new NativeMethods.DEVMODE
            {
                dmDeviceName = "",
                dmFormName = "",
                dmSize = (short)Marshal.SizeOf<NativeMethods.DEVMODE>()
            };
            if (NativeMethods.EnumDisplaySettingsEx(info.GdiDeviceName, NativeMethods.ENUM_CURRENT_SETTINGS, ref dm, 0)
                && dm.dmPelsWidth > 0 && dm.dmPelsHeight > 0)
            {
                info.PhysicalBounds = new Rectangle(dm.dmPositionX, dm.dmPositionY, dm.dmPelsWidth, dm.dmPelsHeight);
                info.RefreshHz = dm.dmDisplayFrequency;
                info.Bpp = dm.dmBitsPerPel;
                info.HasDevMode = true;
            }
        }
        catch (Exception ex)
        {
            Log.Write($"EnumDisplaySettingsEx({info.GdiDeviceName}) : {ex.Message}");
        }
    }

    private static void FillNames(MonitorInfo info)
    {
        if (info.GdiDeviceName.Length == 0) return;
        try
        {
            var dd = NewDisplayDevice();
            // Le moniteur branché sur cette sortie.
            if (NativeMethods.EnumDisplayDevices(info.GdiDeviceName, 0, ref dd, 0))
            {
                info.MonitorName = (dd.DeviceString ?? "").Trim();
                info.StableKey = (dd.DeviceID ?? "").Trim();
            }

            // La carte graphique.
            for (uint i = 0; i < 32; i++)
            {
                var adapter = NewDisplayDevice();
                if (!NativeMethods.EnumDisplayDevices(null, i, ref adapter, 0)) break;
                if (string.Equals((adapter.DeviceName ?? "").Trim(), info.GdiDeviceName, StringComparison.OrdinalIgnoreCase))
                {
                    info.AdapterName = (adapter.DeviceString ?? "").Trim();
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Write($"EnumDisplayDevices({info.GdiDeviceName}) : {ex.Message}");
        }
    }

    private static NativeMethods.DISPLAY_DEVICE NewDisplayDevice() => new()
    {
        cb = Marshal.SizeOf<NativeMethods.DISPLAY_DEVICE>(),
        DeviceName = "",
        DeviceString = "",
        DeviceID = "",
        DeviceKey = ""
    };

    private static int ParseDisplayNumber(string gdiName, int fallback)
    {
        if (gdiName.Length == 0) return fallback;
        var digits = new StringBuilder();
        for (int i = gdiName.Length - 1; i >= 0; i--)
        {
            if (char.IsDigit(gdiName[i])) digits.Insert(0, gdiName[i]);
            else if (digits.Length > 0) break;
        }
        return digits.Length > 0 && int.TryParse(digits.ToString(), out int n) ? n : fallback;
    }

    private static string BuildStableKey(MonitorInfo info)
    {
        if (info.StableKey.Length > 0) return info.StableKey;
        if (info.GdiDeviceName.Length > 0) return $"GDI:{info.GdiDeviceName}";
        return $"IDX:{info.RdpId}";
    }

    /// <summary>
    /// Choisit la source de coordonnées du plan : DEVMODE (pixels réels) si elle est cohérente,
    /// sinon les rectangles de GetMonitorInfo.
    /// </summary>
    private static void ChooseLayoutRects(List<MonitorInfo> list)
    {
        bool devModeUsable = list.Count > 0 && list.TrueForAll(m => m.HasDevMode && m.PhysicalBounds is { Width: > 0, Height: > 0 });
        if (devModeUsable)
        {
            for (int i = 0; i < list.Count && devModeUsable; i++)
                for (int j = i + 1; j < list.Count; j++)
                {
                    var inter = Rectangle.Intersect(list[i].PhysicalBounds, list[j].PhysicalBounds);
                    if (inter.Width > 1 && inter.Height > 1) { devModeUsable = false; break; }
                }
        }

        foreach (var m in list)
        {
            m.LayoutBounds = devModeUsable ? m.PhysicalBounds : m.WindowBounds;
            if (m.LayoutBounds.Width <= 0 || m.LayoutBounds.Height <= 0) m.LayoutBounds = m.WindowBounds;
        }

        if (!devModeUsable) Log.Write("Plan basé sur GetMonitorInfo (DEVMODE incohérent ou indisponible).");
    }

    /// <summary>Attribue le rang 1..N de gauche à droite (puis de haut en bas), comme Windows.</summary>
    private static void AssignOrder(List<MonitorInfo> list)
    {
        var sorted = list.OrderBy(m => m.LayoutBounds.Left).ThenBy(m => m.LayoutBounds.Top).ToList();
        for (int i = 0; i < sorted.Count; i++) sorted[i].Order = i + 1;

        bool horizontal = sorted.Select(m => m.LayoutBounds.Left).Distinct().Count() == sorted.Count;
        bool vertical = !horizontal && sorted.Select(m => m.LayoutBounds.Top).Distinct().Count() == sorted.Count;

        for (int i = 0; i < sorted.Count; i++)
        {
            sorted[i].PositionLabel = sorted.Count == 1 ? "Écran unique"
                : horizontal ? HorizontalLabel(i, sorted.Count)
                : vertical ? VerticalLabel(i, sorted.Count)
                : $"Écran {i + 1}";
        }
    }

    private static string HorizontalLabel(int i, int count) => i switch
    {
        0 => "Gauche",
        _ when i == count - 1 => "Droite",
        _ when count == 3 => "Centre",
        _ => $"Milieu {i}"
    };

    private static string VerticalLabel(int i, int count) => i switch
    {
        0 => "Haut",
        _ when i == count - 1 => "Bas",
        _ => $"Milieu {i}"
    };

    /// <summary>Ordre d'affichage officiel de l'application : gauche → droite.</summary>
    public static List<MonitorInfo> LeftToRight(IEnumerable<MonitorInfo> monitors) =>
        monitors.OrderBy(m => m.Order).ToList();

    /// <summary>
    /// mstsc exige un ensemble d'écrans contigus : vérifie que la sélection forme un seul bloc
    /// (les rectangles se touchent, de proche en proche).
    /// </summary>
    public static bool IsContiguous(IList<MonitorInfo> selection)
    {
        if (selection.Count <= 1) return true;

        var seen = new bool[selection.Count];
        var queue = new Queue<int>();
        queue.Enqueue(0);
        seen[0] = true;
        int visited = 1;

        while (queue.Count > 0)
        {
            int current = queue.Dequeue();
            for (int i = 0; i < selection.Count; i++)
            {
                if (seen[i] || !Touches(selection[current].LayoutBounds, selection[i].LayoutBounds)) continue;
                seen[i] = true;
                visited++;
                queue.Enqueue(i);
            }
        }
        return visited == selection.Count;
    }

    private static bool Touches(Rectangle a, Rectangle b)
    {
        if (a.IntersectsWith(b)) return true;

        bool verticalOverlap = a.Top < b.Bottom && b.Top < a.Bottom;
        bool horizontalOverlap = a.Left < b.Right && b.Left < a.Right;

        if (verticalOverlap && (a.Right == b.Left || b.Right == a.Left)) return true;
        if (horizontalOverlap && (a.Bottom == b.Top || b.Bottom == a.Top)) return true;
        return false;
    }

    /// <summary>Le rectangle englobant de la sélection est-il entièrement couvert (aucun trou) ?</summary>
    public static bool FillsBoundingBox(IList<MonitorInfo> selection)
    {
        if (selection.Count == 0) return true;
        var box = selection[0].LayoutBounds;
        long area = 0;
        foreach (var m in selection)
        {
            box = Rectangle.Union(box, m.LayoutBounds);
            area += (long)m.LayoutBounds.Width * m.LayoutBounds.Height;
        }
        return area >= (long)box.Width * box.Height;
    }

    /// <summary>Rectangle englobant de tous les écrans (bureau virtuel).</summary>
    public static Rectangle BoundingBox(IEnumerable<MonitorInfo> monitors)
    {
        Rectangle? box = null;
        foreach (var m in monitors) box = box is null ? m.LayoutBounds : Rectangle.Union(box.Value, m.LayoutBounds);
        return box ?? Rectangle.Empty;
    }
}
