using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

namespace TermServMultiScreen
{
    public static class Program
    {
        [STAThread]
        public static int Main(string[] rawArgs)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException += delegate(object s, System.Threading.ThreadExceptionEventArgs e)
            {
                Log.Write("Exception interface : " + e.Exception);
                MessageBox.Show("Une erreur inattendue est survenue :" + Environment.NewLine + Environment.NewLine
                    + e.Exception.Message + Environment.NewLine + Environment.NewLine
                    + "Détails dans : " + Paths.LogFile, "Bureau à distance multi-écrans",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            };
            AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e)
            {
                Log.Write("Exception non gérée : " + e.ExceptionObject);
            };

            var args = new Args(rawArgs);
            if (args.Has("help") || args.Has("?") || args.Has("h"))
            {
                Show("Aide", Usage(), args);
                return 0;
            }

            var config = AppConfig.Load();
            config.ImportNeighbourFiles();
            var monitors = MonitorEnumerator.Enumerate();
            Log.Write("Démarrage — " + monitors.Count + " écran(s) : " + string.Join(" | ",
                MonitorEnumerator.LeftToRight(monitors).Select(m => m.ShortLabel + " id=" + RdpFile.EffectiveRdpId(m, config)).ToArray()));

            if (args.Has("list"))
            {
                Show("Diagnostic des écrans", DiagnosticReport.Build(monitors, config), args);
                return 0;
            }

            if (args.Has("identify"))
            {
                double seconds;
                if (!double.TryParse(args.Value("identify"), NumberStyles.Float, CultureInfo.InvariantCulture, out seconds)
                    || seconds <= 0) seconds = 3.5;
                IdentifyOverlay.ShowAll(monitors, null, config, (int)(seconds * 1000), delegate { Application.ExitThread(); });
                Application.Run();
                return 0;
            }

            Profile profile = null;
            string wanted = args.Value("profile");
            if (!string.IsNullOrEmpty(wanted))
            {
                profile = config.Find(wanted);
                if (profile == null)
                {
                    Fail("Aucune connexion enregistrée ne s'appelle « " + wanted + " ».", args);
                    return 2;
                }
            }

            if (args.Has("print") || args.Has("connect"))
            {
                if (profile == null) profile = config.Find(config.LastProfile) ?? config.Profiles.FirstOrDefault();
                if (profile == null)
                {
                    Fail("Aucune connexion enregistrée : lancez l'application sans paramètre pour en créer une.", args);
                    return 2;
                }

                List<MonitorInfo> selection = SelectionFromArgs(args, profile, monitors);
                if (selection.Count == 0)
                {
                    Fail("Aucun écran n'a pu être déterminé pour la connexion « " + profile.Name + " ».", args);
                    return 3;
                }

                if (args.Has("print"))
                {
                    string content = "Écrans : " + Launcher.Describe(selection, config) + Environment.NewLine
                                   + new string('-', 78) + Environment.NewLine + Environment.NewLine
                                   + RdpFile.Build(profile, selection, monitors, config);
                    Show("Fichier .rdp de « " + profile.Name + " »", content, args);
                    return 0;
                }

                try
                {
                    Launcher.Launch(profile, selection, monitors, config);
                    return 0;
                }
                catch (Exception ex)
                {
                    Log.Write("Lancement direct : " + ex);
                    Fail("Le lancement a échoué : " + ex.Message, args);
                    return 4;
                }
            }

            Application.Run(new MainForm(config, monitors));
            return 0;
        }

        /// <summary>--all, --screens 1,3 (rangs de gauche à droite) ou la sélection enregistrée.</summary>
        private static List<MonitorInfo> SelectionFromArgs(Args args, Profile profile, List<MonitorInfo> monitors)
        {
            if (args.Has("all")) return MonitorEnumerator.LeftToRight(monitors);

            string screens = args.Value("screens");
            if (!string.IsNullOrEmpty(screens))
            {
                var wanted = new List<MonitorInfo>();
                foreach (string part in screens.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    int order;
                    if (!int.TryParse(part.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out order)) continue;
                    var hit = monitors.FirstOrDefault(m => m.Order == order);
                    if (hit != null && !wanted.Contains(hit)) wanted.Add(hit);
                }
                if (wanted.Count > 0) return MonitorEnumerator.LeftToRight(wanted);
            }

            var stored = AppConfig.ResolveScreens(profile, monitors);
            if (stored.Count > 0) return stored;

            var primary = monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors.FirstOrDefault();
            return primary == null ? new List<MonitorInfo>() : new List<MonitorInfo> { primary };
        }

        /// <summary>Sortie texte : dans le fichier --out s'il est fourni, sinon à l'écran.</summary>
        private static void Show(string title, string text, Args args)
        {
            string outFile = args.Value("out");
            if (!string.IsNullOrEmpty(outFile))
            {
                try
                {
                    string folder = Path.GetDirectoryName(Path.GetFullPath(outFile));
                    if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
                    File.WriteAllText(outFile, text, new UTF8Encoding(false));
                    return;
                }
                catch (Exception ex)
                {
                    Log.Write("Écriture de " + outFile + " impossible : " + ex.Message);
                }
            }
            using (var dlg = new TextDialog(title, text)) dlg.ShowDialog();
        }

        private static void Fail(string message, Args args)
        {
            Log.Write("Erreur : " + message);
            string outFile = args.Value("out");
            if (!string.IsNullOrEmpty(outFile))
            {
                try { File.WriteAllText(outFile, "ERREUR : " + message + Environment.NewLine, new UTF8Encoding(false)); return; }
                catch { }
            }
            MessageBox.Show(message, "Bureau à distance multi-écrans", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private static string Usage()
        {
            return
                "Bureau à distance multi-écrans" + Environment.NewLine +
                new string('-', 60) + Environment.NewLine + Environment.NewLine +
                "Sans paramètre : ouvre la fenêtre de l'application." + Environment.NewLine + Environment.NewLine +
                "  --profile \"<nom>\"   choisit une connexion enregistrée" + Environment.NewLine +
                "  --connect           lance la session immédiatement" + Environment.NewLine +
                "  --screens 1,2       écrans à utiliser, numérotés de gauche à droite" + Environment.NewLine +
                "  --all               tous les écrans" + Environment.NewLine +
                "  --list              rapport de diagnostic des écrans" + Environment.NewLine +
                "  --identify [sec]    affiche un grand numéro sur chaque écran" + Environment.NewLine +
                "  --print             affiche le fichier .rdp qui serait utilisé" + Environment.NewLine +
                "  --out <fichier>     écrit la sortie texte dans un fichier" + Environment.NewLine + Environment.NewLine +
                "Exemple : TermServMultiScreen.exe --profile \"BN\" --screens 1,2 --connect";
        }

        /// <summary>Lecture très permissive de la ligne de commande (-x, --x, /x).</summary>
        private sealed class Args
        {
            private readonly Dictionary<string, string> _values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            public Args(string[] raw)
            {
                for (int i = 0; i < raw.Length; i++)
                {
                    string a = raw[i];
                    if (string.IsNullOrEmpty(a)) continue;
                    if (a[0] != '-' && a[0] != '/') continue;
                    string name = a.TrimStart('-', '/');
                    string value = "";
                    int eq = name.IndexOf('=');
                    if (eq > 0)
                    {
                        value = name.Substring(eq + 1);
                        name = name.Substring(0, eq);
                    }
                    else if (i + 1 < raw.Length && !string.IsNullOrEmpty(raw[i + 1])
                             && raw[i + 1][0] != '-' && raw[i + 1][0] != '/')
                    {
                        value = raw[i + 1];
                        i++;
                    }
                    _values[name] = value;
                }
            }

            public bool Has(string name) { return _values.ContainsKey(name); }

            public string Value(string name)
            {
                string v;
                return _values.TryGetValue(name, out v) ? v : null;
            }
        }
    }

    /// <summary>Rapport lisible : ce que l'application voit, dans les trois numérotations.</summary>
    public static class DiagnosticReport
    {
        public static string Build(IList<MonitorInfo> monitors, AppConfig config)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Bureau à distance multi-écrans — diagnostic");
            sb.AppendLine("Version   : " + Assembly.GetExecutingAssembly().GetName().Version);
            sb.AppendLine("Date      : " + DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.CurrentCulture));
            sb.AppendLine("Système   : " + Environment.OSVersion.VersionString + (Environment.Is64BitOperatingSystem ? " (64 bits)" : ""));
            sb.AppendLine("Machine   : " + Environment.MachineName);
            sb.AppendLine("Écrans    : " + monitors.Count);
            sb.AppendLine("Corresp.  : " + (config != null && config.ManualMappingEnabled ? "MANUELLE" : "automatique"));
            sb.AppendLine("Journal   : " + Paths.LogFile);
            sb.AppendLine();

            var ordered = MonitorEnumerator.LeftToRight(monitors);
            Rectangle box = ordered.Count > 0 ? ordered[0].LayoutBounds : Rectangle.Empty;
            foreach (var m in ordered) box = Rectangle.Union(box, m.LayoutBounds);
            sb.AppendLine("Bureau virtuel : " + box.Width + " x " + box.Height + "  (origine X=" + box.Left + " Y=" + box.Top + ")");
            sb.AppendLine();

            sb.AppendLine("Ordre de gauche à droite (celui de l'application)");
            sb.AppendLine("Rang  Position      Windows  id RDP  Résolution      Position        Principal  Périphérique");
            sb.AppendLine(new string('-', 112));
            foreach (var m in ordered)
            {
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "{0,-5} {1,-13} {2,-8} {3,-7} {4,-15} {5,-15} {6,-10} {7}",
                    m.Order, m.PositionLabel, m.WindowsNumber, RdpFile.EffectiveRdpId(m, config),
                    m.ResolutionText, "X=" + m.LayoutBounds.X + " Y=" + m.LayoutBounds.Y,
                    m.IsPrimary ? "oui" : "", m.GdiDeviceName));
            }
            sb.AppendLine();

            sb.AppendLine("Ordre d'énumération de Windows (celui que mstsc utilise pour numéroter les écrans)");
            foreach (var m in monitors.OrderBy(m => m.RdpId))
            {
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "  id RDP {0} = écran {1} ({2}, Windows {3}, {4})",
                    RdpFile.EffectiveRdpId(m, config), m.Order, m.PositionLabel, m.WindowsNumber, m.ResolutionText));
            }
            sb.AppendLine();

            sb.AppendLine("Détails");
            sb.AppendLine(new string('-', 112));
            foreach (var m in ordered)
            {
                sb.AppendLine(m.DetailText);
                sb.AppendLine("Clé stable      : " + m.StableKey);
                sb.AppendLine("Zone fenêtre    : " + m.WindowBounds + "   travail : " + m.WindowWorkArea);
                sb.AppendLine("DEVMODE         : " + (m.HasDevMode ? m.PhysicalBounds.ToString() : "indisponible"));
                sb.AppendLine();
            }

            if (config != null && config.Profiles != null && config.Profiles.Count > 0)
            {
                sb.AppendLine("Connexions enregistrées");
                sb.AppendLine(new string('-', 112));
                foreach (var p in config.Profiles)
                {
                    var resolved = AppConfig.ResolveScreens(p, monitors);
                    sb.AppendLine("  " + p.Name + "  →  " + p.Address
                        + "   écrans : " + (resolved.Count == 0 ? "(non résolus)" :
                            string.Join(" + ", resolved.Select(m => m.Order + " " + m.PositionLabel).ToArray())));
                }
            }

            return sb.ToString();
        }
    }
}
