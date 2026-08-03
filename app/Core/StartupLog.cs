using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace TermServMultiScreen.Core;

/// <summary>
/// Journalisation du démarrage. Le point d'entrée est un initialiseur de module : il s'exécute au
/// chargement de l'assembly, donc AVANT le constructeur de l'application et avant tout XAML. Cela
/// permet de tracer les fermetures immédiates, qui autrefois ne laissaient aucune trace.
///
/// Trois filets complémentaires attrapent tout ce qui peut tuer le processus :
/// l'exception non gérée du domaine, celle du dispatcher XAML, et les tâches non observées.
/// Un fichier témoin marque un démarrage en cours : s'il est encore là au lancement suivant,
/// c'est que le précédent s'est arrêté brutalement.
/// </summary>
public static class StartupLog
{
    private static readonly object Gate = new();
    private static bool _initialised;
    private static int _step;

    private static string MarkerFile => Path.Combine(Paths.DataFolder, "demarrage-en-cours.marqueur");

    [ModuleInitializer]
    internal static void Initialize()
    {
        // Rien ici ne doit pouvoir lever : une exception dans un initialiseur de module
        // empêcherait purement et simplement l'assembly de se charger.
        try
        {
            lock (Gate)
            {
                if (_initialised) return;
                _initialised = true;
            }

            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            {
                Log.Write("!!! EXCEPTION FATALE (domaine) — le processus va s'arrêter"
                        + Environment.NewLine + Describe(e.ExceptionObject as Exception));
                ClearMarker();
            };

            TaskScheduler.UnobservedTaskException += (_, e) =>
            {
                Log.Write("Exception de tâche non observée" + Environment.NewLine + Describe(e.Exception));
                e.SetObserved();
            };

            AppDomain.CurrentDomain.ProcessExit += (_, _) =>
            {
                Log.Write($"Fin du processus (code {Environment.ExitCode}) après {Age()}.");
                ClearMarker();
            };

            Log.Write(new string('=', 78));
            Log.Write($"Chargement de l'assembly — étape 0. Journal : {Paths.LogFile}");
            ReportPreviousCrash();
            WriteMarker();
            Log.Write(EnvironmentReport());
        }
        catch (Exception ex)
        {
            // Dernier recours : si même la journalisation échoue, on tente une trace brute.
            try
            {
                File.AppendAllText(Path.Combine(Path.GetTempPath(), "TermServMultiScreen-demarrage.log"),
                    $"{DateTime.Now:O} StartupLog : {ex}{Environment.NewLine}");
            }
            catch { }
        }
    }

    /// <summary>Fil d'Ariane : la dernière étape journalisée situe l'endroit exact d'un plantage.</summary>
    public static void Step(string what)
    {
        int number = Interlocked.Increment(ref _step);
        Log.Write($"[étape {number:00}] {what}");
    }

    /// <summary>Exécute une étape en journalisant son résultat, sans laisser passer d'exception.</summary>
    public static bool Try(string what, Action action)
    {
        Step(what);
        try
        {
            action();
            return true;
        }
        catch (Exception ex)
        {
            Log.Write($"ÉCHEC de l'étape « {what} »" + Environment.NewLine + Describe(ex));
            return false;
        }
    }

    /// <summary>Le démarrage est allé jusqu'au bout : le témoin peut disparaître.</summary>
    public static void StartupCompleted()
    {
        Log.Write($"Démarrage terminé sans erreur fatale après {Age()}.");
        ClearMarker();
    }

    public static string Describe(Exception? exception)
    {
        if (exception is null) return "  (aucun détail d'exception)";

        var sb = new StringBuilder();
        for (var current = exception; current is not null; current = current.InnerException)
        {
            sb.AppendLine($"  {current.GetType().FullName} : {current.Message}");
            if (current is COMException com) sb.AppendLine($"    HRESULT : 0x{com.HResult:X8}");
            else if (current.HResult != 0) sb.AppendLine($"    HRESULT : 0x{current.HResult:X8}");
            if (!string.IsNullOrWhiteSpace(current.StackTrace)) sb.AppendLine(current.StackTrace.TrimEnd());
            if (current.InnerException is not null) sb.AppendLine("  ---- exception interne ----");
        }
        return sb.ToString().TrimEnd();
    }

    // ------------------------------------------------------------------ environnement

    /// <summary>
    /// Tout ce qui peut différer d'un poste à l'autre. C'est ce bloc qui permet de comparer une
    /// machine où l'application démarre et une machine où elle se ferme.
    /// </summary>
    public static string EnvironmentReport()
    {
        var sb = new StringBuilder();
        sb.AppendLine("--- environnement ---");
        Add(sb, "Application", $"{Assembly.GetExecutingAssembly().GetName().Version} "
                             + $"({Assembly.GetExecutingAssembly().GetName().Name})");
        Add(sb, "Exécutable", Environment.ProcessPath ?? "(inconnu)");
        Add(sb, "Dossier d'exécution", AppContext.BaseDirectory);
        Add(sb, "Fichier unique", IsSingleFile() ? "oui (contenu extrait dans le dossier ci-dessus)" : "non");
        Add(sb, "Ligne de commande", Environment.CommandLine);
        Add(sb, "Windows", DescribeWindows());
        Add(sb, "Architecture", $"OS {RuntimeInformation.OSArchitecture}, processus {RuntimeInformation.ProcessArchitecture}");
        Add(sb, "Runtime .NET", RuntimeInformation.FrameworkDescription);
        Add(sb, "Windows App SDK", DescribeWindowsAppSdk());
        Add(sb, "Runtime Visual C++", DescribeVisualCppRuntime());
        Add(sb, "Dossier temporaire", DescribeTempFolder());
        Add(sb, "Machine / session", $"{Environment.MachineName} / {Environment.UserName}"
                                   + $" (session {Process.GetCurrentProcess().SessionId})");
        Add(sb, "Administrateur", IsElevated() ? "oui" : "non");
        Add(sb, "Culture", $"{CultureInfo.CurrentCulture.Name} / interface {CultureInfo.CurrentUICulture.Name}");
        Add(sb, "Dossier de données", Paths.DataFolder + (Paths.UsingFallbackFolder ? "   (emplacement de repli !)" : ""));
        Add(sb, "Processeurs / mémoire", $"{Environment.ProcessorCount} cœurs, "
                                       + $"{GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (1024 * 1024)} Mo disponibles");
        Add(sb, "Écrans", DescribeMonitors());
        Add(sb, "Session interactive", Environment.UserInteractive ? "oui" : "NON (service ou session détachée)");
        sb.Append("----------------------");
        return sb.ToString();
    }

    private static void Add(StringBuilder sb, string label, string value) =>
        sb.AppendLine($"  {label,-22}: {value}");

    private static bool IsSingleFile()
    {
        try
        {
            string? processFolder = Path.GetDirectoryName(Environment.ProcessPath ?? "");
            return !string.IsNullOrEmpty(processFolder)
                && !string.Equals(processFolder.TrimEnd('\\'), AppContext.BaseDirectory.TrimEnd('\\'),
                    StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private static string DescribeWindows()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            string product = key?.GetValue("ProductName")?.ToString() ?? "Windows";
            string display = key?.GetValue("DisplayVersion")?.ToString() ?? "";
            string build = key?.GetValue("CurrentBuild")?.ToString() ?? Environment.OSVersion.Version.Build.ToString();
            string ubr = key?.GetValue("UBR")?.ToString() ?? "0";
            return $"{product} {display} build {build}.{ubr}";
        }
        catch (Exception ex) { return $"{Environment.OSVersion.VersionString} (registre illisible : {ex.Message})"; }
    }

    /// <summary>
    /// Deux modes possibles. En mode autonome (celui de la publication) les binaires du Windows
    /// App SDK sont extraits à côté de l'application : s'il en manque un, l'interface ne peut pas
    /// se charger et le processus s'arrête sans fenêtre. En mode lié au runtime installé (build de
    /// développement), c'est la machine qui doit fournir le runtime — leur absence est normale.
    /// </summary>
    private static string DescribeWindowsAppSdk()
    {
        try
        {
            string[] embedded =
            [
                "Microsoft.ui.xaml.dll",                    // moteur natif de l'interface
                "Microsoft.ui.xaml.resources.common.dll",   // styles par défaut des contrôles
                "Microsoft.WinUI.dll",                      // projection managée
                "Microsoft.WindowsAppRuntime.dll",
                "Microsoft.Internal.FrameworkUdk.dll",
                "Microsoft.UI.Composition.OSSupport.dll",
                "CoreMessagingXP.dll",
                "DWriteCore.dll"                            // rendu du texte
            ];
            var found = embedded.Where(name => File.Exists(Path.Combine(AppContext.BaseDirectory, name))).ToList();

            // Mode lié : seul l'amorceur accompagne l'application, il va chercher le runtime installé.
            bool selfContained = File.Exists(Path.Combine(AppContext.BaseDirectory, "Microsoft.WindowsAppRuntime.dll"));
            if (!selfContained)
            {
                bool bootstrap = File.Exists(Path.Combine(AppContext.BaseDirectory,
                    "Microsoft.WindowsAppRuntime.Bootstrap.dll"));
                return "non embarqué — le runtime doit être installé sur cette machine "
                     + $"(amorceur {(bootstrap ? "présent" : "ABSENT")})";
            }

            string version = "";
            try
            {
                version = " version " + FileVersionInfo.GetVersionInfo(
                    Path.Combine(AppContext.BaseDirectory, "Microsoft.ui.xaml.dll")).FileVersion;
            }
            catch { }

            return found.Count == embedded.Length
                ? $"embarqué, {found.Count}/{embedded.Length} binaires présents{version}"
                : $"embarqué mais INCOMPLET : {found.Count}/{embedded.Length} présents "
                  + $"(manquants : {string.Join(", ", embedded.Except(found))}){version}";
        }
        catch (Exception ex) { return $"vérification impossible : {ex.Message}"; }
    }

    /// <summary>
    /// WinUI est écrit en C++ : même embarqué, il exige le runtime Visual C++ du système. Son
    /// absence fait échouer le chargement de Microsoft.ui.xaml.dll, donc l'application se ferme
    /// avant d'avoir affiché quoi que ce soit. On charge réellement les bibliothèques plutôt que
    /// de se contenter de chercher les fichiers : c'est le seul test qui prouve quelque chose.
    /// </summary>
    private static string DescribeVisualCppRuntime()
    {
        string[] required = ["vcruntime140.dll", "vcruntime140_1.dll", "msvcp140.dll"];
        List<string> missing = [];
        foreach (string name in required)
        {
            try
            {
                if (NativeLibrary.TryLoad(name, out nint handle)) NativeLibrary.Free(handle);
                else missing.Add(name);
            }
            catch (Exception ex) { missing.Add($"{name} ({ex.GetType().Name})"); }
        }

        string version = "";
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                $@"SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\{RuntimeInformation.OSArchitecture}");
            if (key?.GetValue("Version") is string v) version = $", redistribuable {v}";
        }
        catch { }

        return missing.Count == 0
            ? $"présent ({required.Length}/{required.Length} bibliothèques chargeables{version})"
            : $"MANQUANT : {string.Join(", ", missing)} — installez « Microsoft Visual C++ "
              + $"Redistributable {RuntimeInformation.OSArchitecture} »{version}";
    }

    /// <summary>
    /// L'exécutable unique se décompresse dans le dossier temporaire au lancement : s'il est
    /// inaccessible, plein, ou bloqué par une stratégie, rien ne peut démarrer.
    /// </summary>
    private static string DescribeTempFolder()
    {
        try
        {
            string temp = Path.GetTempPath();
            string probe = Path.Combine(temp, "TermServMultiScreen.ecriture");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);

            string free = "";
            try
            {
                var drive = new DriveInfo(Path.GetPathRoot(temp) ?? "C:\\");
                free = $", {drive.AvailableFreeSpace / (1024L * 1024L)} Mo libres";
            }
            catch { }

            return $"{temp} inscriptible{free}";
        }
        catch (Exception ex) { return $"{Path.GetTempPath()} NON INSCRIPTIBLE : {ex.Message}"; }
    }

    private static string DescribeMonitors()
    {
        try
        {
            var monitors = MonitorEnumerator.Enumerate();
            return $"{monitors.Count} — " + string.Join(" | ",
                MonitorEnumerator.LeftToRight(monitors).Select(m =>
                    $"{m.Order} {m.PositionLabel} {m.ResolutionText} en ({m.LayoutBounds.X},{m.LayoutBounds.Y})"));
        }
        catch (Exception ex) { return $"énumération impossible : {ex.Message}"; }
    }

    private static bool IsElevated()
    {
        try
        {
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            return new System.Security.Principal.WindowsPrincipal(identity)
                .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch { return false; }
    }

    // ------------------------------------------------------------------ témoin de démarrage

    private static void WriteMarker()
    {
        try
        {
            Directory.CreateDirectory(Paths.DataFolder);
            File.WriteAllText(MarkerFile,
                $"{DateTime.Now:O}{Environment.NewLine}{Environment.ProcessPath}{Environment.NewLine}{Environment.CommandLine}");
        }
        catch (Exception ex) { Log.Write($"Témoin de démarrage : {ex.Message}"); }
    }

    private static void ClearMarker()
    {
        try { if (File.Exists(MarkerFile)) File.Delete(MarkerFile); } catch { }
    }

    private static void ReportPreviousCrash()
    {
        try
        {
            if (!File.Exists(MarkerFile)) return;
            string content = File.ReadAllText(MarkerFile).Replace(Environment.NewLine, " | ");
            Log.Write("!!! Le lancement précédent s'est interrompu sans passer par la fermeture normale : "
                    + content);
        }
        catch { }
    }

    private static string Age()
    {
        try { return $"{(DateTime.Now - Process.GetCurrentProcess().StartTime).TotalSeconds:F1} s"; }
        catch { return "durée inconnue"; }
    }
}
