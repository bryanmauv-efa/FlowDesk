using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace TermServMultiScreen
{
    /// <summary>
    /// Un ecran physique, avec les trois identites qui comptent :
    ///  - RdpId          : l'identifiant attendu par mstsc (selectedmonitors), = rang d'enumeration Win32
    ///  - WindowsNumber  : le numero affiche par Parametres > Systeme > Affichage (\\.\DISPLAYn)
    ///  - Order          : le rang de gauche a droite (1..N) -> ce que l'utilisateur voit et choisit
    /// </summary>
    public sealed class MonitorInfo
    {
        public int RdpId;                  // rang d'enumeration EnumDisplayMonitors (= id mstsc)
        public string GdiDeviceName;       // \\.\DISPLAY1
        public int WindowsNumber;          // 1, 2, 3 ... extrait de GdiDeviceName
        public bool IsPrimary;

        public Rectangle WindowBounds;     // coordonnees utilisables pour placer une fenetre
        public Rectangle WindowWorkArea;
        public Rectangle PhysicalBounds;   // verite DEVMODE (pixels reels, insensible au DPI)
        public Rectangle LayoutBounds;     // rectangles retenus pour le plan / le tri
        public bool HasDevMode;

        public string AdapterName = "";
        public string MonitorName = "";
        public int RefreshHz;
        public int Bpp;

        public string StableKey = "";      // identite persistante (port physique)
        public int Order;                  // 1..N de gauche a droite
        public string PositionLabel = "";  // Gauche / Centre / Droite / Haut / Bas / Ecran n

        public Size Resolution
        {
            get { return HasDevMode ? PhysicalBounds.Size : WindowBounds.Size; }
        }

        public string ResolutionText
        {
            get
            {
                return string.Format(CultureInfo.InvariantCulture, "{0} x {1}", Resolution.Width, Resolution.Height);
            }
        }

        /// <summary>Libelle court sans ambiguite : "2 - Centre (Windows 2)".</summary>
        public string ShortLabel
        {
            get
            {
                return string.Format(CultureInfo.CurrentCulture, "{0} - {1} (Windows {2})",
                    Order, PositionLabel, WindowsNumber);
            }
        }

        public string DetailText
        {
            get
            {
                var sb = new StringBuilder();
                sb.AppendLine("Écran " + Order + " (" + PositionLabel + ")");
                sb.AppendLine("Numéro Windows  : " + WindowsNumber + "   (" + GdiDeviceName + ")");
                sb.AppendLine("Identifiant RDP : " + RdpId + "   (selectedmonitors)");
                sb.AppendLine("Résolution      : " + ResolutionText + (RefreshHz > 0 ? "  @ " + RefreshHz + " Hz" : ""));
                sb.AppendLine("Position        : X=" + LayoutBounds.X + "  Y=" + LayoutBounds.Y);
                sb.AppendLine("Écran principal : " + (IsPrimary ? "oui" : "non"));
                if (!string.IsNullOrEmpty(MonitorName)) sb.AppendLine("Moniteur        : " + MonitorName);
                if (!string.IsNullOrEmpty(AdapterName)) sb.AppendLine("Carte vidéo     : " + AdapterName);
                return sb.ToString().TrimEnd();
            }
        }
    }

    public static class MonitorEnumerator
    {
        /// <summary>
        /// Enumere les ecrans dans l'ordre exact de EnumDisplayMonitors (l'ordre que mstsc utilise
        /// pour numeroter selectedmonitors), puis leur attribue un rang de gauche a droite.
        /// </summary>
        public static List<MonitorInfo> Enumerate()
        {
            var list = new List<MonitorInfo>();
            int index = 0;

            NativeMethods.MonitorEnumProc callback = delegate(IntPtr hMonitor, IntPtr hdc, ref NativeMethods.RECT rc, IntPtr data)
            {
                var mi = new NativeMethods.MONITORINFOEX();
                mi.cbSize = System.Runtime.InteropServices.Marshal.SizeOf(typeof(NativeMethods.MONITORINFOEX));
                mi.szDevice = "";

                var info = new MonitorInfo();
                info.RdpId = index++;

                if (NativeMethods.GetMonitorInfo(hMonitor, ref mi))
                {
                    info.GdiDeviceName = (mi.szDevice ?? "").TrimEnd('\0');
                    info.IsPrimary = (mi.dwFlags & NativeMethods.MONITORINFOF_PRIMARY) != 0;
                    info.WindowBounds = ToRectangle(mi.rcMonitor);
                    info.WindowWorkArea = ToRectangle(mi.rcWork);
                }
                else
                {
                    info.GdiDeviceName = "";
                    info.WindowBounds = ToRectangle(rc);
                    info.WindowWorkArea = info.WindowBounds;
                }

                FillFromDevMode(info);
                FillNames(info);
                info.WindowsNumber = ParseDisplayNumber(info.GdiDeviceName, info.RdpId + 1);
                info.StableKey = BuildStableKey(info);

                list.Add(info);
                return true;
            };

            try
            {
                NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero);
            }
            catch (Exception ex)
            {
                Log.Write("EnumDisplayMonitors a echoue : " + ex.Message);
            }

            if (list.Count == 0)
            {
                // Filet de securite : ne jamais rendre une liste vide.
                Log.Write("Repli sur Screen.AllScreens.");
                int i = 0;
                foreach (var s in Screen.AllScreens)
                {
                    var info = new MonitorInfo();
                    info.RdpId = i++;
                    info.GdiDeviceName = s.DeviceName ?? "";
                    info.IsPrimary = s.Primary;
                    info.WindowBounds = s.Bounds;
                    info.WindowWorkArea = s.WorkingArea;
                    info.PhysicalBounds = s.Bounds;
                    info.Bpp = s.BitsPerPixel;
                    info.WindowsNumber = ParseDisplayNumber(info.GdiDeviceName, info.RdpId + 1);
                    info.StableKey = BuildStableKey(info);
                    list.Add(info);
                }
            }

            ChooseLayoutRects(list);
            AssignOrder(list);
            return list;
        }

        private static Rectangle ToRectangle(NativeMethods.RECT r)
        {
            return new Rectangle(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
        }

        private static void FillFromDevMode(MonitorInfo info)
        {
            if (string.IsNullOrEmpty(info.GdiDeviceName)) return;
            try
            {
                var dm = new NativeMethods.DEVMODE();
                dm.dmDeviceName = "";
                dm.dmFormName = "";
                dm.dmSize = (short)System.Runtime.InteropServices.Marshal.SizeOf(typeof(NativeMethods.DEVMODE));
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
                Log.Write("EnumDisplaySettingsEx(" + info.GdiDeviceName + ") : " + ex.Message);
            }
        }

        private static void FillNames(MonitorInfo info)
        {
            if (string.IsNullOrEmpty(info.GdiDeviceName)) return;
            try
            {
                var dd = new NativeMethods.DISPLAY_DEVICE();
                dd.cb = System.Runtime.InteropServices.Marshal.SizeOf(typeof(NativeMethods.DISPLAY_DEVICE));
                dd.DeviceName = ""; dd.DeviceString = ""; dd.DeviceID = ""; dd.DeviceKey = "";

                // Le moniteur branche sur cette sortie.
                if (NativeMethods.EnumDisplayDevices(info.GdiDeviceName, 0, ref dd, 0))
                {
                    info.MonitorName = (dd.DeviceString ?? "").Trim();
                    info.StableKey = (dd.DeviceID ?? "").Trim();
                }

                // La carte graphique.
                for (uint i = 0; i < 32; i++)
                {
                    var ad = new NativeMethods.DISPLAY_DEVICE();
                    ad.cb = System.Runtime.InteropServices.Marshal.SizeOf(typeof(NativeMethods.DISPLAY_DEVICE));
                    ad.DeviceName = ""; ad.DeviceString = ""; ad.DeviceID = ""; ad.DeviceKey = "";
                    if (!NativeMethods.EnumDisplayDevices(null, i, ref ad, 0)) break;
                    if (string.Equals((ad.DeviceName ?? "").Trim(), info.GdiDeviceName, StringComparison.OrdinalIgnoreCase))
                    {
                        info.AdapterName = (ad.DeviceString ?? "").Trim();
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Write("EnumDisplayDevices(" + info.GdiDeviceName + ") : " + ex.Message);
            }
        }

        private static int ParseDisplayNumber(string gdiName, int fallback)
        {
            if (string.IsNullOrEmpty(gdiName)) return fallback;
            var digits = new StringBuilder();
            for (int i = gdiName.Length - 1; i >= 0; i--)
            {
                if (char.IsDigit(gdiName[i])) digits.Insert(0, gdiName[i]);
                else if (digits.Length > 0) break;
            }
            int n;
            if (digits.Length > 0 && int.TryParse(digits.ToString(), out n)) return n;
            return fallback;
        }

        private static string BuildStableKey(MonitorInfo info)
        {
            if (!string.IsNullOrEmpty(info.StableKey)) return info.StableKey;
            if (!string.IsNullOrEmpty(info.GdiDeviceName)) return "GDI:" + info.GdiDeviceName;
            return "IDX:" + info.RdpId;
        }

        /// <summary>
        /// Choisit la source de coordonnees pour le plan : DEVMODE (pixels reels) si elle est
        /// coherente, sinon les rectangles fournis par GetMonitorInfo.
        /// </summary>
        private static void ChooseLayoutRects(List<MonitorInfo> list)
        {
            bool devModeUsable = list.Count > 0 && list.All(m => m.HasDevMode && m.PhysicalBounds.Width > 0 && m.PhysicalBounds.Height > 0);
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

            if (!devModeUsable) Log.Write("Plan base sur GetMonitorInfo (DEVMODE incoherent ou indisponible).");
        }

        /// <summary>Attribue le rang 1..N de gauche a droite (puis de haut en bas), comme Windows.</summary>
        private static void AssignOrder(List<MonitorInfo> list)
        {
            var sorted = list.OrderBy(m => m.LayoutBounds.Left).ThenBy(m => m.LayoutBounds.Top).ToList();
            for (int i = 0; i < sorted.Count; i++) sorted[i].Order = i + 1;

            bool horizontal = sorted.Select(m => m.LayoutBounds.Left).Distinct().Count() == sorted.Count;
            bool vertical = !horizontal && sorted.Select(m => m.LayoutBounds.Top).Distinct().Count() == sorted.Count;

            for (int i = 0; i < sorted.Count; i++)
            {
                var m = sorted[i];
                if (sorted.Count == 1) m.PositionLabel = "Écran unique";
                else if (horizontal) m.PositionLabel = HorizontalLabel(i, sorted.Count);
                else if (vertical) m.PositionLabel = VerticalLabel(i, sorted.Count);
                else m.PositionLabel = "Écran " + (i + 1);
            }
        }

        private static string HorizontalLabel(int i, int count)
        {
            if (i == 0) return "Gauche";
            if (i == count - 1) return "Droite";
            if (count == 3) return "Centre";
            return "Milieu " + i;
        }

        private static string VerticalLabel(int i, int count)
        {
            if (i == 0) return "Haut";
            if (i == count - 1) return "Bas";
            return "Milieu " + i;
        }

        /// <summary>Ordre d'affichage officiel de l'application : gauche -> droite.</summary>
        public static List<MonitorInfo> LeftToRight(IEnumerable<MonitorInfo> monitors)
        {
            return monitors.OrderBy(m => m.Order).ToList();
        }

        /// <summary>
        /// mstsc exige un ensemble d'ecrans contigus. Verifie que la selection forme un seul
        /// bloc (les rectangles se touchent, de proche en proche).
        /// </summary>
        public static bool IsContiguous(IList<MonitorInfo> selection)
        {
            if (selection == null || selection.Count <= 1) return true;

            var seen = new bool[selection.Count];
            var queue = new Queue<int>();
            queue.Enqueue(0);
            seen[0] = true;
            int visited = 1;

            while (queue.Count > 0)
            {
                int cur = queue.Dequeue();
                for (int i = 0; i < selection.Count; i++)
                {
                    if (seen[i]) continue;
                    if (Touches(selection[cur].LayoutBounds, selection[i].LayoutBounds))
                    {
                        seen[i] = true;
                        visited++;
                        queue.Enqueue(i);
                    }
                }
            }
            return visited == selection.Count;
        }

        private static bool Touches(Rectangle a, Rectangle b)
        {
            if (a.IntersectsWith(b)) return true;

            bool vOverlap = a.Top < b.Bottom && b.Top < a.Bottom;
            bool hOverlap = a.Left < b.Right && b.Left < a.Right;

            if (vOverlap && (a.Right == b.Left || b.Right == a.Left)) return true;
            if (hOverlap && (a.Bottom == b.Top || b.Bottom == a.Top)) return true;
            return false;
        }

        /// <summary>Le rectangle englobant de la selection est-il entierement couvert (pas de trou) ?</summary>
        public static bool FillsBoundingBox(IList<MonitorInfo> selection)
        {
            if (selection == null || selection.Count == 0) return true;
            Rectangle box = selection[0].LayoutBounds;
            long area = 0;
            foreach (var m in selection)
            {
                box = Rectangle.Union(box, m.LayoutBounds);
                area += (long)m.LayoutBounds.Width * m.LayoutBounds.Height;
            }
            return area >= (long)box.Width * box.Height;
        }
    }
}
