using System.Globalization;
using System.Text;

namespace TermServMultiScreen.Core;

/// <summary>Dossiers et fichiers de travail de l'application.</summary>
public static class Paths
{
    /// <summary>
    /// Dossier de travail. L'emplacement normal est %LOCALAPPDATA% ; en cas de profil verrouillé
    /// ou de dossier inaccessible, on se replie sur %TEMP% puis sur le dossier de l'exécutable —
    /// mieux vaut un journal ailleurs que pas de journal du tout.
    /// </summary>
    public static string DataFolder { get; } = ResolveDataFolder();

    /// <summary>Vrai si l'emplacement normal n'a pas pu être utilisé (visible dans le journal).</summary>
    public static bool UsingFallbackFolder { get; private set; }

    private static string ResolveDataFolder()
    {
        List<string> candidates = [];
        try { candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TermServMultiScreen")); } catch { }
        try { candidates.Add(Path.Combine(Path.GetTempPath(), "TermServMultiScreen")); } catch { }
        try { candidates.Add(Path.Combine(AppContext.BaseDirectory, "TermServMultiScreen-data")); } catch { }

        for (int i = 0; i < candidates.Count; i++)
        {
            try
            {
                Directory.CreateDirectory(candidates[i]);
                // Preuve d'écriture : un dossier créable n'est pas forcément inscriptible.
                string probe = Path.Combine(candidates[i], ".ecriture");
                File.WriteAllText(probe, "ok");
                File.Delete(probe);
                UsingFallbackFolder = i > 0;
                return candidates[i];
            }
            catch { /* emplacement suivant */ }
        }

        UsingFallbackFolder = true;
        return candidates.Count > 0 ? candidates[0] : Path.GetTempPath();
    }

    public static string SessionsFolder => Path.Combine(DataFolder, "sessions");
    public static string ConfigFile => Path.Combine(DataFolder, "config.json");
    public static string LogFile => Path.Combine(DataFolder, "journal.log");

    public static void EnsureFolders()
    {
        Directory.CreateDirectory(DataFolder);
        Directory.CreateDirectory(SessionsFolder);
    }

    /// <summary>Rend un nom de fichier utilisable à partir d'un nom de connexion.</summary>
    public static string SafeFileName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "session";
        var invalid = Path.GetInvalidFileNameChars();
        var chars = name.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
            if (Array.IndexOf(invalid, chars[i]) >= 0) chars[i] = '_';
        string result = new string(chars).Trim();
        return result.Length == 0 ? "session" : result;
    }
}

/// <summary>Journal simple : indispensable pour diagnostiquer sans deviner.</summary>
public static class Log
{
    private static readonly Lock Gate = new();
    private const long MaxBytes = 512 * 1024;

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Paths.DataFolder);
                string file = Paths.LogFile;
                if (File.Exists(file) && new FileInfo(file).Length > MaxBytes)
                {
                    string old = file + ".1";
                    File.Delete(old);
                    File.Move(file, old);
                }
                // BOM UTF-8 : les accents restent lisibles dans n'importe quel éditeur.
                if (!File.Exists(file)) File.WriteAllText(file, "", new UTF8Encoding(true));
                File.AppendAllText(file,
                    $"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)}  {message}{Environment.NewLine}",
                    new UTF8Encoding(true));
            }
        }
        catch
        {
            // Un problème de journal ne doit jamais empêcher l'application de fonctionner.
        }
    }

    /// <summary>Dernières lignes du journal, les plus récentes d'abord.</summary>
    public static List<string> Tail(int lines)
    {
        try
        {
            if (!File.Exists(Paths.LogFile)) return [];
            lock (Gate)
            {
                var all = File.ReadAllLines(Paths.LogFile);
                return all.Reverse().Take(lines).ToList();
            }
        }
        catch (Exception ex)
        {
            return [$"(journal illisible : {ex.Message})"];
        }
    }
}
