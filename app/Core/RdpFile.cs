using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Text;

namespace TermServMultiScreen.Core;

/// <summary>
/// Fabrique le contenu du fichier .rdp. C'est le seul moyen de choisir les écrans :
/// mstsc.exe n'a aucune option de ligne de commande pour ça.
/// </summary>
public static class RdpFile
{
    /// <summary>Clés que l'application pilote elle-même : retirées des réglages importés.</summary>
    private static readonly string[] ManagedKeys =
    [
        "screen mode id", "use multimon", "selectedmonitors", "singlemoninwindowedmode",
        "maximizetocurrentdisplays", "desktopwidth", "desktopheight", "winposstr",
        "smart sizing", "span monitors", "dynamic resolution", "session bpp",
        "full address", "username", "redirectclipboard", "redirectprinters",
        "audiomode", "drivestoredirect", "displayconnectionbar",
        "autoreconnection enabled", "keyboardhook"
    ];

    /// <summary>Clés jamais recopiées : secrets et signatures restent dans le fichier d'origine.</summary>
    private static readonly string[] BlockedKeys = ["password 51", "signature", "signscope"];

    /// <summary>Réglages appliqués seulement s'ils ne viennent pas déjà d'un fichier importé.</summary>
    private static readonly string[] DefaultLines =
    [
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
    ];

    /// <summary>Identifiant mstsc effectif d'un écran (correspondance manuelle prise en compte).</summary>
    public static int EffectiveRdpId(MonitorInfo monitor, AppConfig? config) =>
        config?.ManualRdpId(monitor.StableKey) ?? monitor.RdpId;

    public static string BuildSelectedMonitors(IEnumerable<MonitorInfo> selection, AppConfig? config) =>
        string.Join(",", selection.Select(m => EffectiveRdpId(m, config)).Distinct().Order()
            .Select(i => i.ToString(CultureInfo.InvariantCulture)));

    /// <summary>Contenu complet du fichier .rdp pour une connexion et une sélection d'écrans.</summary>
    public static string Build(Profile profile, IList<MonitorInfo> selection, IList<MonitorInfo>? allMonitors, AppConfig? config)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (selection.Count == 0) throw new InvalidOperationException("Aucun écran sélectionné.");

        List<string> lines = [];
        bool allSelected = allMonitors is not null && selection.Count == allMonitors.Count;
        bool singleWindowed = selection.Count == 1 && profile.WindowedWhenSingle;

        if (singleWindowed)
        {
            // Repli de compatibilité : fenêtre maximisée sur l'écran choisi.
            Rectangle area = selection[0].WindowWorkArea.Width > 0 ? selection[0].WindowWorkArea : selection[0].WindowBounds;
            lines.Add("screen mode id:i:1");
            lines.Add("use multimon:i:0");
            lines.Add(string.Create(CultureInfo.InvariantCulture,
                $"winposstr:s:0,3,{area.Left},{area.Top},{area.Right},{area.Bottom}"));
            lines.Add(string.Create(CultureInfo.InvariantCulture, $"desktopwidth:i:{area.Width}"));
            lines.Add(string.Create(CultureInfo.InvariantCulture, $"desktopheight:i:{area.Height}"));
            lines.Add("smart sizing:i:1");
        }
        else
        {
            lines.Add("screen mode id:i:2");
            lines.Add("use multimon:i:1");
            if (!(allSelected && profile.MultimonSwitchWhenAll))
            {
                // La liste explicite d'écrans : c'est elle qui fait tout le travail.
                lines.Add("selectedmonitors:s:" + BuildSelectedMonitors(selection, config));
            }
            lines.Add("singlemoninwindowedmode:i:1");
            lines.Add("maximizetocurrentdisplays:i:0");
        }

        lines.Add("session bpp:i:32");
        lines.Add($"displayconnectionbar:i:{(profile.ConnectionBar ? 1 : 0)}");
        lines.Add($"autoreconnection enabled:i:{(profile.AutoReconnect ? 1 : 0)}");
        lines.Add($"keyboardhook:i:{profile.KeyboardHook}");
        lines.Add($"audiomode:i:{(profile.Sound ? 0 : 2)}");
        lines.Add($"redirectclipboard:i:{(profile.Clipboard ? 1 : 0)}");
        lines.Add($"redirectprinters:i:{(profile.Printers ? 1 : 0)}");
        lines.Add($"drivestoredirect:s:{(profile.LocalDrives ? "*" : "")}");
        lines.Add($"full address:s:{profile.Address.Trim()}");
        if (profile.UserName.Trim().Length > 0) lines.Add($"username:s:{profile.UserName.Trim()}");

        // Réglages importés conservés tels quels (passerelle RD, imprimante par défaut…).
        var present = new HashSet<string>(lines.Select(KeyOf), StringComparer.OrdinalIgnoreCase);
        foreach (var extra in CleanExtraLines(profile.ExtraRdpLines))
            if (present.Add(KeyOf(extra))) lines.Add(extra);

        foreach (var line in DefaultLines)
            if (present.Add(KeyOf(line))) lines.Add(line);

        return string.Join("\r\n", lines) + "\r\n";
    }

    private static string KeyOf(string line)
    {
        int i = line.IndexOf(':');
        return i <= 0 ? line.Trim() : line[..i].Trim();
    }

    /// <summary>Retire des lignes importées ce que l'application pilote et ce qui est sensible.</summary>
    public static List<string> CleanExtraLines(IEnumerable<string>? raw)
    {
        List<string> result = [];
        if (raw is null) return result;
        foreach (var line in raw)
        {
            string trimmed = (line ?? "").Trim();
            if (trimmed.Length == 0 || trimmed.IndexOf(':') <= 0) continue;
            string key = KeyOf(trimmed);
            if (key.Length == 0) continue;
            if (ManagedKeys.Contains(key, StringComparer.OrdinalIgnoreCase)) continue;
            if (BlockedKeys.Contains(key, StringComparer.OrdinalIgnoreCase)) continue;
            result.Add(trimmed);
        }
        return result;
    }

    /// <summary>Lit un .rdp existant : adresse, utilisateur et réglages à conserver (jamais le mot de passe).</summary>
    public static void Import(string path, Profile target)
    {
        List<string> extras = [];
        foreach (var raw in ReadAllLinesAnyEncoding(path))
        {
            string trimmed = raw.Trim();
            if (trimmed.Length == 0) continue;
            string key = KeyOf(trimmed);

            // Format d'une ligne : « clé:type:valeur ».
            string payload = "";
            string rest = trimmed.Length > key.Length ? trimmed[key.Length..] : "";
            int first = rest.IndexOf(':');
            if (first >= 0)
            {
                int second = rest.IndexOf(':', first + 1);
                if (second >= 0) payload = rest[(second + 1)..];
            }
            payload = payload.Trim();

            switch (key.ToLowerInvariant())
            {
                case "full address": target.Address = payload; break;
                case "username": target.UserName = payload; break;
                case "redirectclipboard": target.Clipboard = payload == "1"; break;
                case "redirectprinters": target.Printers = payload == "1"; break;
                case "audiomode": target.Sound = payload == "0"; break;
                case "drivestoredirect": target.LocalDrives = payload.Length > 0; break;
                case "displayconnectionbar": target.ConnectionBar = payload != "0"; break;
                case "autoreconnection enabled": target.AutoReconnect = payload != "0"; break;
                case "keyboardhook":
                    if (int.TryParse(payload, out int hook) && hook is >= 0 and <= 2) target.KeyboardHook = hook;
                    break;
                default: extras.Add(trimmed); break;
            }
        }
        target.ExtraRdpLines = CleanExtraLines(extras);
    }

    /// <summary>mstsc écrit ses .rdp en UTF-16 ; les fichiers faits à la main sont souvent en ANSI.</summary>
    public static string[] ReadAllLinesAnyEncoding(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        Encoding encoding = Encoding.Latin1;
        if (bytes is [0xFF, 0xFE, ..]) encoding = Encoding.Unicode;
        else if (bytes is [0xFE, 0xFF, ..]) encoding = Encoding.BigEndianUnicode;
        else if (bytes is [0xEF, 0xBB, 0xBF, ..]) encoding = Encoding.UTF8;
        else if (LooksLikeUtf16(bytes)) encoding = Encoding.Unicode;

        string text = encoding.GetString(bytes);
        const char ByteOrderMark = (char)0xFEFF;
        if (text.Length > 0 && text[0] == ByteOrderMark) text = text[1..];
        return text.ReplaceLineEndings("\n").Split('\n');
    }

    private static bool LooksLikeUtf16(byte[] bytes)
    {
        int checkLength = Math.Min(bytes.Length, 64);
        if (checkLength < 4) return false;
        int zeros = 0;
        for (int i = 1; i < checkLength; i += 2)
            if (bytes[i] == 0) zeros++;
        return zeros > checkLength / 4;
    }

    /// <summary>Écrit le fichier au format natif de mstsc (UTF-16LE avec BOM).</summary>
    public static void Write(string path, string content)
    {
        string? folder = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
        File.WriteAllText(path, content, new UnicodeEncoding(false, true));
    }

    /// <summary>Emplacement du .rdp d'une connexion enregistrée.</summary>
    public static string SessionPath(string profileName) =>
        Path.Combine(Paths.SessionsFolder, Paths.SafeFileName(profileName) + ".rdp");

    /// <summary>
    /// Écrit (ou met à jour) le .rdp de la connexion dans le dossier des sessions. C'est le
    /// fichier que mstsc ouvrira, et il reste double-cliquable tel quel.
    /// </summary>
    public static string? WriteSession(Profile profile, IList<MonitorInfo> selection, IList<MonitorInfo>? allMonitors, AppConfig? config)
    {
        try
        {
            if (profile.Address.Trim().Length == 0 || selection.Count == 0) return null;
            Paths.EnsureFolders();
            string path = SessionPath(profile.Name);
            Write(path, Build(profile, selection, allMonitors, config));
            Log.Write($"Fichier de connexion écrit : {path}");
            return path;
        }
        catch (Exception ex)
        {
            Log.Write($"Écriture du .rdp de « {profile.Name} » : {ex.Message}");
            return null;
        }
    }

    /// <summary>Supprime le .rdp d'une connexion qui n'existe plus.</summary>
    public static void DeleteSession(string profileName)
    {
        try
        {
            string path = SessionPath(profileName);
            if (File.Exists(path))
            {
                File.Delete(path);
                Log.Write($"Fichier de connexion supprimé : {path}");
            }
        }
        catch (Exception ex)
        {
            Log.Write($"Suppression du .rdp de « {profileName} » : {ex.Message}");
        }
    }
}

public static class Launcher
{
    /// <summary>Génère le .rdp de la connexion et démarre mstsc dessus. Renvoie le chemin utilisé.</summary>
    public static string Launch(Profile profile, IList<MonitorInfo> selection, IList<MonitorInfo>? allMonitors, AppConfig? config)
    {
        Paths.EnsureFolders();
        string content = RdpFile.Build(profile, selection, allMonitors, config);
        string path = RdpFile.SessionPath(profile.Name.Length > 0 ? profile.Name : profile.Address);
        RdpFile.Write(path, content);

        Log.Write($"Lancement : {path}");
        Log.Write($"  écrans  : {Describe(selection, config)}");

        string mstsc = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "mstsc.exe");
        if (!File.Exists(mstsc)) mstsc = "mstsc.exe";

        Process.Start(new ProcessStartInfo
        {
            FileName = mstsc,
            Arguments = $"\"{path}\"",
            UseShellExecute = true
        });
        return path;
    }

    public static string Describe(IEnumerable<MonitorInfo> selection, AppConfig? config) =>
        string.Join(" + ", selection.Select(m =>
            $"{m.Order} {m.PositionLabel} [Windows {m.WindowsNumber}, id RDP {RdpFile.EffectiveRdpId(m, config)}]"));
}
