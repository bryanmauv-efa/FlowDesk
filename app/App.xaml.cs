using System.Diagnostics;
using System.Text;
using Microsoft.UI.Xaml;
using TermServMultiScreen.Core;
using TermServMultiScreen.Core.Rdp;
using TermServMultiScreen.Services;

namespace TermServMultiScreen;

public partial class App : Application
{
    private Window? _window;
    private Window? _preview;

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
        try
        {
            AppServices.Initialize();
        }
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

            if (command.Has("rdptrust"))
            {
                _ = RunTrustDiagnosticsAsync(command);
                return;   // Exit() est appelé à la fin du diagnostic
            }

            if (command.Has("resign"))
            {
                _ = ResignAllAsync(command);
                return;
            }

            if (command.Has("mapdemo"))
            {
                // Fenêtre d'aperçu : même panneau et même gabarit que la page d'accueil.
                _preview = new Overlays.LayoutPreviewWindow();
                _preview.Closed += (_, _) => Exit();
                _preview.Activate();
                return;
            }

            if (command.Has("maptest"))
            {
                // Dispositions réelles de la machine, puis dispositions de référence.
                string report = MonitorLayout.Describe("Disposition réelle de ce poste",
                                    monitors.Select(m => m.LayoutBounds).ToList(),
                                    monitors.FindIndex(m => m.IsPrimary))
                              + Environment.NewLine
                              + MonitorLayout.DescribeReferenceLayouts();
                WriteText(command, "test-dispositions.txt", report);
                Exit();
                return;
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

            // La signature est obligatoire : le lancement est asynchrone et conditionnel.
            _ = ConnectHeadlessAsync(command, profile, selection, monitors, config);
            return;
        }
        catch (Exception ex)
        {
            Log.Write($"Mode ligne de commande : {ex}");
            WriteText(command, "erreur.txt", ex.ToString());
        }

        Exit();
    }

    /// <summary>Lancement en ligne de commande : mstsc n'est démarré que si la signature est prouvée.</summary>
    private async Task ConnectHeadlessAsync(CommandLine command, Profile profile,
        List<MonitorInfo> selection, List<MonitorInfo> monitors, AppConfig config)
    {
        try
        {
            await RdpTrust.InitializeAsync();

            // Sans interface, on ne corrige rien tout seul : on trace clairement le problème.
            var probe = await RdpLauncher.CheckServerNameAsync(profile.Address);
            if (probe is { Reached: true, NameMatches: false })
            {
                Log.Write($"ATTENTION — « {profile.Address} » est absent du certificat du serveur "
                        + $"[{string.Join(", ", probe.DnsNames)}] : mstsc demandera de confirmer l'identité "
                        + "du serveur. Corrigez l'adresse depuis l'application.");
            }

            var result = await RdpLauncher.LaunchAsync(profile, selection, monitors, config);

            if (!result.Success)
            {
                string message = "La session n'a pas été lancée : le fichier .rdp n'a pas pu être signé."
                               + Environment.NewLine + Environment.NewLine + result.FailureReason
                               + Environment.NewLine + Environment.NewLine
                               + "Diagnostic complet : TermServMultiScreen.exe --rdptrust";
                WriteText(command, "erreur-signature.txt", message);
                NativeMethods.MessageBox(0, message, "FlowDesk — signature .rdp", NativeMethods.MB_ICONERROR);
            }
        }
        catch (Exception ex)
        {
            Log.Write($"Lancement en ligne de commande : {ex}");
            WriteText(command, "erreur.txt", ex.ToString());
        }
        finally { Exit(); }
    }

    /// <summary>
    /// Régénère et resigne le .rdp de chaque connexion enregistrée. Utile après un changement
    /// d'adresse ou de certificat : les fichiers du dossier des sessions redeviennent cohérents.
    /// </summary>
    private async Task ResignAllAsync(CommandLine command)
    {
        var report = new StringBuilder();
        try
        {
            var config = AppServices.Config;
            var monitors = AppServices.Monitors.Monitors;
            await RdpTrust.InitializeAsync();

            foreach (var profile in config.Profiles)
            {
                var screens = AppConfig.ResolveScreens(profile, monitors);
                if (screens.Count == 0)
                {
                    var primary = monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors.FirstOrDefault();
                    if (primary is not null) screens = [primary];
                }

                string? path = await RdpFile.WriteSessionAsync(profile, screens, monitors, config);
                if (path is null)
                {
                    report.AppendLine($"{profile.Name,-24} IGNORÉ (serveur ou écrans manquants)");
                    continue;
                }

                var (scope, signature) = RdpSigningService.ReadSignatureFields(path);
                report.AppendLine($"{profile.Name,-24} {(scope && signature ? "SIGNÉ" : "NON SIGNÉ")}  {path}");
            }

            if (config.Profiles.Count == 0) report.AppendLine("Aucune connexion enregistrée.");
            WriteText(command, "resignature.txt", report.ToString());
            Log.Write("Régénération des .rdp :" + Environment.NewLine + report);
        }
        catch (Exception ex)
        {
            Log.Write($"Régénération des .rdp : {ex}");
            WriteText(command, "erreur.txt", ex.ToString());
        }
        finally { Exit(); }
    }

    private async Task RunTrustDiagnosticsAsync(CommandLine command)
    {
        try
        {
            var (_, report) = await RdpTrustDiagnostics.RunAsync();
            WriteText(command, "diagnostic-rdp.txt", report);
        }
        catch (Exception ex)
        {
            Log.Write($"Diagnostic RDP : {ex}");
            WriteText(command, "erreur.txt", ex.ToString());
        }
        finally { Exit(); }
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
