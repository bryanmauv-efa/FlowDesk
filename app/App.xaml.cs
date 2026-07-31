using System.Diagnostics;
using System.Text;
using Microsoft.UI.Xaml;
using TermServMultiScreen.Core;
using TermServMultiScreen.Services;

namespace TermServMultiScreen;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) =>
        {
            Log.Write($"Exception non gérée : {e.Exception}");
            e.Handled = true;   // journalisée : l'application reste utilisable
        };

        // Initialisation ici et pas dans OnLaunched : c'est le seul moment où le thème peut
        // encore être fixé pour toute l'application, boutons de la fenêtre compris.
        try { AppServices.Initialize(); }
        catch (Exception ex) { Log.Write($"Initialisation : {ex}"); }
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        AppServices.Initialize();

        var command = new CommandLine(Environment.GetCommandLineArgs()[1..]);
        if (command.IsHeadless)
        {
            RunHeadless(command);
            return;
        }

        try
        {
            _window = new MainWindow();
            AppServices.MainWindow = _window;
            _window.Activate();

            // Lancement asynchrone de la vérification de mise à jour (ne bloque pas l'UI)
            _ = Updater.CheckAndUpdateAsync(
                async (version) =>
                {
                    var tcs = new TaskCompletionSource<bool>();
                    AppServices.MainWindow?.DispatcherQueue.TryEnqueue(async () =>
                    {
                        bool result = await DialogService.ConfirmAsync(
                            "Mise à jour disponible",
                            $"La nouvelle version {version} est disponible.\n\nVoulez-vous la télécharger et l'installer maintenant ?",
                            "Mettre à jour", "Plus tard");
                        tcs.SetResult(result);
                    });
                    return await tcs.Task;
                },
                async (action) =>
                {
                    var tcs = new TaskCompletionSource();
                    AppServices.MainWindow?.DispatcherQueue.TryEnqueue(async () =>
                    {
                        await DialogService.RunWithProgressAsync(
                            "Mise à jour en cours", 
                            "Téléchargement et vérification de la signature...", 
                            action);
                        tcs.SetResult();
                    });
                    await tcs.Task;
                }
            );
        }
        catch (Exception ex)
        {
            // Sans fenêtre, l'application serait un processus fantôme : on le dit clairement.
            Log.Write($"Création de la fenêtre principale — HRESULT=0x{ex.HResult:X8}{Environment.NewLine}{ex}");
            NativeMethods.MessageBox(0,
                "L'interface n'a pas pu s'ouvrir." + Environment.NewLine + Environment.NewLine
                + ex.Message + Environment.NewLine + Environment.NewLine
                + $"HRESULT 0x{ex.HResult:X8}" + Environment.NewLine
                + "Détails complets dans :" + Environment.NewLine + Paths.LogFile,
                "Bureau à distance multi-écrans", NativeMethods.MB_ICONERROR);
            Exit();
        }
    }

    /// <summary>
    /// Actions sans interface, pour les raccourcis du Bureau et le diagnostic :
    /// --connect, --identify, --list, --print.
    /// </summary>
    private void RunHeadless(CommandLine command)
    {
        var config = AppServices.Config;
        var monitors = AppServices.Monitors.Monitors;

        try
        {
            if (command.Has("list"))
            {
                WriteText(command, "diagnostic-ecrans.txt", DiagnosticReport.Build(monitors, config));
                Exit();
                return;
            }

            if (command.Has("identify"))
            {
                IdentifyService.ShowAll(
                    MonitorEnumerator.LeftToRight(monitors),
                    null,
                    config,
                    TimeSpan.FromSeconds(command.Number("identify", 3.5)),
                    Exit);
                return;   // l'application se ferme quand les pastilles disparaissent
            }

            var profile = config.Find(command.Value("profile"))
                          ?? config.Find(config.LastProfile)
                          ?? config.Profiles.FirstOrDefault();
            if (profile is null)
            {
                WriteText(command, "erreur.txt",
                    "Aucune connexion enregistrée : lancez l'application sans paramètre pour en créer une."
                    + Environment.NewLine + Environment.NewLine + CommandLine.Usage);
                Exit();
                return;
            }

            var selection = SelectionFor(command, profile, monitors);
            if (selection.Count == 0)
            {
                WriteText(command, "erreur.txt", $"Aucun écran déterminé pour « {profile.Name} ».");
                Exit();
                return;
            }

            if (command.Has("print"))
            {
                string content = $"Écrans : {Launcher.Describe(selection, config)}{Environment.NewLine}"
                               + new string('-', 78) + Environment.NewLine + Environment.NewLine
                               + RdpFile.Build(profile, selection, monitors, config);
                WriteText(command, $"{Paths.SafeFileName(profile.Name)}-apercu.txt", content);
                Exit();
                return;
            }

            Launcher.Launch(profile, selection, monitors, config);
        }
        catch (Exception ex)
        {
            Log.Write($"Mode ligne de commande : {ex}");
            WriteText(command, "erreur.txt", ex.ToString());
        }

        Exit();
    }

    private static List<MonitorInfo> SelectionFor(CommandLine command, Profile profile, List<MonitorInfo> monitors)
    {
        if (command.Has("all")) return MonitorEnumerator.LeftToRight(monitors);

        string? screens = command.Value("screens");
        if (!string.IsNullOrWhiteSpace(screens))
        {
            List<MonitorInfo> wanted = [];
            foreach (var part in screens.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries))
            {
                if (!int.TryParse(part.Trim(), out int order)) continue;
                var hit = monitors.FirstOrDefault(m => m.Order == order);
                if (hit is not null && !wanted.Contains(hit)) wanted.Add(hit);
            }
            if (wanted.Count > 0) return MonitorEnumerator.LeftToRight(wanted);
        }

        var stored = AppConfig.ResolveScreens(profile, monitors);
        if (stored.Count > 0) return stored;

        var primary = monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors.FirstOrDefault();
        return primary is null ? [] : [primary];
    }

    /// <summary>Écrit la sortie texte dans --out, sinon dans le dossier de l'application, puis l'ouvre.</summary>
    private static void WriteText(CommandLine command, string defaultName, string text)
    {
        string? target = command.Value("out");
        bool openAfter = string.IsNullOrWhiteSpace(target);
        try
        {
            if (openAfter)
            {
                Paths.EnsureFolders();
                target = Path.Combine(Paths.DataFolder, defaultName);
            }

            string? folder = Path.GetDirectoryName(Path.GetFullPath(target!));
            if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
            File.WriteAllText(target!, text, new UTF8Encoding(true));

            if (openAfter)
                Process.Start(new ProcessStartInfo(target!) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Write($"Écriture de {target} impossible : {ex.Message}");
        }
    }
}
