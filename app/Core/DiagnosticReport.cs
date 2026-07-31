using System.Globalization;
using System.Reflection;
using System.Text;

namespace TermServMultiScreen.Core;

/// <summary>Rapport lisible : ce que l'application voit, dans les trois numérotations.</summary>
public static class DiagnosticReport
{
    public static string Build(IList<MonitorInfo> monitors, AppConfig? config)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Bureau à distance multi-écrans — diagnostic");
        sb.AppendLine($"Version   : {Assembly.GetExecutingAssembly().GetName().Version}");
        sb.AppendLine($"Date      : {DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.CurrentCulture)}");
        sb.AppendLine($"Système   : {Environment.OSVersion.VersionString}{(Environment.Is64BitOperatingSystem ? " (64 bits)" : "")}");
        sb.AppendLine($"Machine   : {Environment.MachineName}");
        sb.AppendLine($"Écrans    : {monitors.Count}");
        sb.AppendLine($"Corresp.  : {(config?.ManualMappingEnabled == true ? "MANUELLE" : "automatique")}");
        sb.AppendLine($"Journal   : {Paths.LogFile}");
        sb.AppendLine();

        var ordered = MonitorEnumerator.LeftToRight(monitors);
        var box = MonitorEnumerator.BoundingBox(ordered);
        sb.AppendLine($"Bureau virtuel : {box.Width} × {box.Height}  (origine X={box.Left} Y={box.Top})");
        sb.AppendLine();

        sb.AppendLine("Ordre de gauche à droite (celui de l'application)");
        sb.AppendLine("Rang  Position      Windows  id RDP  Résolution        Position          Principal  Périphérique");
        sb.AppendLine(new string('-', 114));
        foreach (var m in ordered)
        {
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "{0,-5} {1,-13} {2,-8} {3,-7} {4,-17} {5,-17} {6,-10} {7}",
                m.Order, m.PositionLabel, m.WindowsNumber, RdpFile.EffectiveRdpId(m, config),
                m.ResolutionText, $"X={m.LayoutBounds.X} Y={m.LayoutBounds.Y}",
                m.IsPrimary ? "oui" : "", m.GdiDeviceName));
        }
        sb.AppendLine();

        sb.AppendLine("Ordre d'énumération de Windows (celui que mstsc utilise pour numéroter les écrans)");
        foreach (var m in monitors.OrderBy(m => m.RdpId))
        {
            sb.AppendLine($"  id RDP {RdpFile.EffectiveRdpId(m, config)} = écran {m.Order} "
                + $"({m.PositionLabel}, Windows {m.WindowsNumber}, {m.ResolutionText})");
        }
        sb.AppendLine();

        sb.AppendLine("Détails");
        sb.AppendLine(new string('-', 114));
        foreach (var m in ordered)
        {
            sb.AppendLine(m.DetailText);
            sb.AppendLine($"Clé stable      : {m.StableKey}");
            sb.AppendLine($"Zone fenêtre    : {m.WindowBounds}   travail : {m.WindowWorkArea}");
            sb.AppendLine($"DEVMODE         : {(m.HasDevMode ? m.PhysicalBounds.ToString() : "indisponible")}");
            sb.AppendLine();
        }

        if (config?.Profiles.Count > 0)
        {
            sb.AppendLine("Connexions enregistrées");
            sb.AppendLine(new string('-', 114));
            foreach (var p in config.Profiles)
            {
                var resolved = AppConfig.ResolveScreens(p, monitors);
                string screens = resolved.Count == 0
                    ? "(non résolus)"
                    : string.Join(" + ", resolved.Select(m => $"{m.Order} {m.PositionLabel}"));
                sb.AppendLine($"  {p.Name}  →  {p.Address}   écrans : {screens}");
            }
        }

        return sb.ToString();
    }
}
