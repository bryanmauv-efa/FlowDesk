using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace TermServMultiScreen
{
    /// <summary>Fabrique le contenu du fichier .rdp (le seul moyen de choisir les ecrans : mstsc n'a pas d'option en ligne de commande pour ca).</summary>
    public static class RdpFile
    {
        /// <summary>Cles que l'application pilote elle-meme : elles sont retirees des reglages importes.</summary>
        private static readonly string[] ManagedKeys = new[]
        {
            "screen mode id", "use multimon", "selectedmonitors", "singlemoninwindowedmode",
            "maximizetocurrentdisplays", "desktopwidth", "desktopheight", "winposstr",
            "smart sizing", "span monitors", "dynamic resolution", "session bpp",
            "full address", "username", "redirectclipboard", "redirectprinters",
            "audiomode", "drivestoredirect", "displayconnectionbar",
            "autoreconnection enabled", "keyboardhook"
        };

        /// <summary>Cles jamais recopiees : secrets et signatures (un mot de passe enregistre reste dans le fichier d'origine).</summary>
        private static readonly string[] BlockedKeys = new[]
        {
            "password 51", "signature", "signscope"
        };

        /// <summary>Reglages appliques seulement s'ils ne viennent pas deja d'un fichier importe.</summary>
        private static readonly string[] DefaultLines = new[]
        {
            "compression:i:1",
            "bitmapcachepersistenable:i:1",
            "connection type:i:7",
            "networkautodetect:i:1",
            "bandwidthautodetect:i:1",
            "videoplaybackmode:i:1",
            "audiocapturemode:i:0",
            "allow font smoothing:i:1",
            "allow desktop composition:i:1",
            "disable wallpaper:i:0",
            "disable themes:i:0",
            "disable menu anims:i:0",
            "disable full window drag:i:0",
            "authentication level:i:2",
            "prompt for credentials:i:0",
            "negotiate security layer:i:1",
            "enableworkspacereconnect:i:0",
            "redirectcomports:i:0",
            "redirectsmartcards:i:1",
            "redirectlocation:i:0",
            "remoteapplicationmode:i:0"
        };

        /// <summary>Identifiant mstsc effectif d'un ecran (avec correspondance manuelle si activee).</summary>
        public static int EffectiveRdpId(MonitorInfo monitor, AppConfig config)
        {
            if (config != null)
            {
                int? manual = config.ManualRdpId(monitor.StableKey);
                if (manual.HasValue) return manual.Value;
            }
            return monitor.RdpId;
        }

        public static string BuildSelectedMonitors(IEnumerable<MonitorInfo> selection, AppConfig config)
        {
            var ids = selection.Select(m => EffectiveRdpId(m, config)).Distinct().OrderBy(i => i).ToList();
            return string.Join(",", ids.Select(i => i.ToString(CultureInfo.InvariantCulture)).ToArray());
        }

        /// <summary>Contenu complet du fichier .rdp pour un profil et une selection d'ecrans.</summary>
        public static string Build(Profile profile, IList<MonitorInfo> selection, IList<MonitorInfo> allMonitors, AppConfig config)
        {
            if (profile == null) throw new ArgumentNullException("profile");
            if (selection == null || selection.Count == 0) throw new InvalidOperationException("Aucun écran sélectionné.");

            var lines = new List<string>();
            bool allSelected = allMonitors != null && selection.Count == allMonitors.Count;
            bool singleWindowed = selection.Count == 1 && profile.WindowedWhenSingle;

            if (singleWindowed)
            {
                // Repli de compatibilite : fenetre maximisee sur l'ecran choisi.
                Rectangle area = selection[0].WindowWorkArea.Width > 0 ? selection[0].WindowWorkArea : selection[0].WindowBounds;
                lines.Add("screen mode id:i:1");
                lines.Add("use multimon:i:0");
                lines.Add(string.Format(CultureInfo.InvariantCulture, "winposstr:s:0,3,{0},{1},{2},{3}",
                    area.Left, area.Top, area.Right, area.Bottom));
                lines.Add(string.Format(CultureInfo.InvariantCulture, "desktopwidth:i:{0}", area.Width));
                lines.Add(string.Format(CultureInfo.InvariantCulture, "desktopheight:i:{0}", area.Height));
                lines.Add("smart sizing:i:1");
            }
            else
            {
                lines.Add("screen mode id:i:2");
                lines.Add("use multimon:i:1");
                if (!(allSelected && profile.MultimonSwitchWhenAll))
                {
                    // La liste explicite d'ecrans : c'est elle qui fait tout le travail.
                    lines.Add("selectedmonitors:s:" + BuildSelectedMonitors(selection, config));
                }
                lines.Add("singlemoninwindowedmode:i:1");
                lines.Add("maximizetocurrentdisplays:i:0");
            }

            lines.Add("session bpp:i:32");
            lines.Add("displayconnectionbar:i:" + (profile.ConnectionBar ? "1" : "0"));
            lines.Add("autoreconnection enabled:i:" + (profile.AutoReconnect ? "1" : "0"));
            lines.Add("keyboardhook:i:" + profile.KeyboardHook.ToString(CultureInfo.InvariantCulture));
            lines.Add("audiomode:i:" + (profile.Sound ? "0" : "2"));
            lines.Add("redirectclipboard:i:" + (profile.Clipboard ? "1" : "0"));
            lines.Add("redirectprinters:i:" + (profile.Printers ? "1" : "0"));
            lines.Add("drivestoredirect:s:" + (profile.LocalDrives ? "*" : ""));
            lines.Add("full address:s:" + (profile.Address ?? "").Trim());
            if (!string.IsNullOrEmpty(profile.UserName)) lines.Add("username:s:" + profile.UserName.Trim());

            // Reglages importes conserves tels quels (passerelle RD, imprimante par defaut, etc.).
            var extras = CleanExtraLines(profile.ExtraRdpLines);
            var present = new HashSet<string>(lines.Select(KeyOf), StringComparer.OrdinalIgnoreCase);
            foreach (var extra in extras)
            {
                string key = KeyOf(extra);
                if (present.Contains(key)) continue;
                present.Add(key);
                lines.Add(extra);
            }

            foreach (var def in DefaultLines)
            {
                string key = KeyOf(def);
                if (present.Contains(key)) continue;
                present.Add(key);
                lines.Add(def);
            }

            return string.Join("\r\n", lines.ToArray()) + "\r\n";
        }

        private static string KeyOf(string line)
        {
            if (string.IsNullOrEmpty(line)) return "";
            int i = line.IndexOf(':');
            return i <= 0 ? line.Trim() : line.Substring(0, i).Trim();
        }

        /// <summary>Retire des lignes importees ce que l'application pilote et ce qui est sensible.</summary>
        public static List<string> CleanExtraLines(IEnumerable<string> raw)
        {
            var result = new List<string>();
            if (raw == null) return result;
            foreach (var line in raw)
            {
                if (string.IsNullOrEmpty(line)) continue;
                string trimmed = line.Trim();
                if (trimmed.Length == 0) continue;
                string key = KeyOf(trimmed);
                if (key.Length == 0) continue;
                if (ManagedKeys.Any(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase))) continue;
                if (BlockedKeys.Any(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase))) continue;
                if (trimmed.IndexOf(':') <= 0) continue;
                result.Add(trimmed);
            }
            return result;
        }

        /// <summary>Lit un .rdp existant : adresse, utilisateur et reglages a conserver (jamais le mot de passe).</summary>
        public static void Import(string path, Profile target)
        {
            string[] lines = ReadAllLinesAnyEncoding(path);
            var extras = new List<string>();
            foreach (var line in lines)
            {
                string trimmed = (line ?? "").Trim();
                if (trimmed.Length == 0) continue;
                string key = KeyOf(trimmed);
                string value = trimmed.Length > key.Length ? trimmed.Substring(key.Length) : "";
                // value commence par ":i:" ou ":s:" ou ":b:"
                string payload = "";
                int sep = value.IndexOf(':');
                if (sep >= 0)
                {
                    int sep2 = value.IndexOf(':', sep + 1);
                    if (sep2 >= 0) payload = value.Substring(sep2 + 1);
                }

                if (string.Equals(key, "full address", StringComparison.OrdinalIgnoreCase)) target.Address = payload.Trim();
                else if (string.Equals(key, "username", StringComparison.OrdinalIgnoreCase)) target.UserName = payload.Trim();
                else if (string.Equals(key, "redirectclipboard", StringComparison.OrdinalIgnoreCase)) target.Clipboard = payload.Trim() == "1";
                else if (string.Equals(key, "redirectprinters", StringComparison.OrdinalIgnoreCase)) target.Printers = payload.Trim() == "1";
                else if (string.Equals(key, "audiomode", StringComparison.OrdinalIgnoreCase)) target.Sound = payload.Trim() == "0";
                else if (string.Equals(key, "drivestoredirect", StringComparison.OrdinalIgnoreCase)) target.LocalDrives = payload.Trim().Length > 0;
                else if (string.Equals(key, "displayconnectionbar", StringComparison.OrdinalIgnoreCase)) target.ConnectionBar = payload.Trim() != "0";
                else if (string.Equals(key, "autoreconnection enabled", StringComparison.OrdinalIgnoreCase)) target.AutoReconnect = payload.Trim() != "0";
                else if (string.Equals(key, "keyboardhook", StringComparison.OrdinalIgnoreCase))
                {
                    int k;
                    if (int.TryParse(payload.Trim(), out k) && k >= 0 && k <= 2) target.KeyboardHook = k;
                }
                else extras.Add(trimmed);
            }
            target.ExtraRdpLines = CleanExtraLines(extras);
        }

        /// <summary>mstsc ecrit ses .rdp en UTF-16 ; les fichiers faits main sont souvent en ANSI. On accepte les deux.</summary>
        public static string[] ReadAllLinesAnyEncoding(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            Encoding encoding = Encoding.Default;
            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE) encoding = Encoding.Unicode;
            else if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF) encoding = Encoding.BigEndianUnicode;
            else if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) encoding = new UTF8Encoding(true);
            else if (LooksLikeUtf16(bytes)) encoding = Encoding.Unicode;

            string text = encoding.GetString(bytes);
            // Retire la marque d'ordre des octets si elle est encore la (U+FEFF).
            if (text.Length > 0 && text[0] == (char)0xFEFF) text = text.Substring(1);
            return text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        }

        private static bool LooksLikeUtf16(byte[] bytes)
        {
            int checkLength = Math.Min(bytes.Length, 64);
            if (checkLength < 4) return false;
            int zeros = 0;
            for (int i = 1; i < checkLength; i += 2) if (bytes[i] == 0) zeros++;
            return zeros > checkLength / 4;
        }

        /// <summary>Ecrit le fichier au format natif de mstsc (UTF-16LE avec BOM).</summary>
        public static void Write(string path, string content)
        {
            string folder = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
            File.WriteAllText(path, content, new UnicodeEncoding(false, true));
        }
    }

    public static class Launcher
    {
        /// <summary>Genere le .rdp du profil et demarre mstsc dessus. Renvoie le chemin du fichier utilise.</summary>
        public static string Launch(Profile profile, IList<MonitorInfo> selection, IList<MonitorInfo> allMonitors, AppConfig config)
        {
            Paths.EnsureFolders();
            string content = RdpFile.Build(profile, selection, allMonitors, config);
            string name = Paths.SafeFileName(string.IsNullOrEmpty(profile.Name) ? profile.Address : profile.Name);
            string path = Path.Combine(Paths.SessionsFolder, name + ".rdp");
            RdpFile.Write(path, content);

            Log.Write("Lancement : " + path);
            Log.Write("  écrans  : " + Describe(selection, config));

            var psi = new ProcessStartInfo();
            psi.FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "mstsc.exe");
            if (!File.Exists(psi.FileName)) psi.FileName = "mstsc.exe";
            psi.Arguments = "\"" + path + "\"";
            psi.UseShellExecute = true;
            Process.Start(psi);
            return path;
        }

        public static string Describe(IEnumerable<MonitorInfo> selection, AppConfig config)
        {
            return string.Join(" + ", selection.Select(m => string.Format(CultureInfo.CurrentCulture,
                "{0} {1} [Windows {2}, id RDP {3}]", m.Order, m.PositionLabel, m.WindowsNumber, RdpFile.EffectiveRdpId(m, config))).ToArray());
        }
    }
}
