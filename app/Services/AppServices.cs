using Microsoft.UI.Xaml;
using TermServMultiScreen.Core;
using TermServMultiScreen.ViewModels;

namespace TermServMultiScreen.Services;

/// <summary>
/// Racine de composition : un seul endroit qui crée et partage l'état de l'application.
/// Volontairement simple — pas de conteneur d'injection pour un outil de cette taille.
/// </summary>
public static class AppServices
{
    public static AppConfig Config { get; private set; } = new();
    public static MonitorService Monitors { get; private set; } = null!;

    public static HomeViewModel Home { get; private set; } = null!;
    public static ConnectionsViewModel Connections { get; private set; } = null!;
    public static SessionsViewModel Sessions { get; private set; } = null!;
    public static LiveSessionsViewModel LiveSessions { get; private set; } = null!;
    public static SettingsViewModel Settings { get; private set; } = null!;
    public static ShareViewModel Share { get; private set; } = null!;

    /// <summary>
    /// Suivi automatique : une session déposée sur un autre écran y passe en plein écran. C'est la
    /// voie principale, celle qui ne demande ni clic dans l'application ni raccourci.
    /// </summary>
    public static RdpFollowService Follow { get; private set; } = null!;

    /// <summary>
    /// Raccourcis clavier globaux de déplacement des sessions. Null tant que la fenêtre principale
    /// ne les a pas démarrés (mode sans interface, par exemple).
    /// </summary>
    public static RdpHotkeyService? Hotkeys { get; private set; }

    public static Window? MainWindow { get; set; }
    public static XamlRoot? XamlRoot => MainWindow?.Content?.XamlRoot;

    /// <summary>Affecté par la fenêtre principale : permet à une page d'en ouvrir une autre.</summary>
    public static Action<Type>? Navigate { get; set; }

    public static bool IsInitialized { get; private set; }

    public static void Initialize()
    {
        if (IsInitialized) return;

        // Chaque sous-étape est tracée : sur un poste où l'application se ferme au démarrage,
        // le journal indique laquelle n'a pas abouti au lieu de laisser deviner.
        StartupLog.Step("dossiers de travail");
        Paths.EnsureFolders();

        StartupLog.Step("lecture de la configuration");
        Config = AppConfig.Load();

        StartupLog.Step("thème initial");
        ApplyStartupTheme();

        StartupLog.Step("énumération des écrans");
        Monitors = new MonitorService();

        StartupLog.Step("suivi automatique des sessions");
        Follow = new RdpFollowService(Monitors) { Enabled = Config.FollowScreenOnMove };

        StartupLog.Step("création des vues");
        Home = new HomeViewModel(Config, Monitors);
        Connections = new ConnectionsViewModel(Config);
        Sessions = new SessionsViewModel();
        LiveSessions = new LiveSessionsViewModel(Config, Monitors, Follow);
        Settings = new SettingsViewModel(Config, Monitors);
        Share = new ShareViewModel(Config);

        IsInitialized = true;
        Log.Write($"Démarrage — {Monitors.Monitors.Count} écran(s) : {string.Join(" | ",
            MonitorEnumerator.LeftToRight(Monitors.Monitors)
                .Select(m => $"{m.ShortLabel} id={RdpFile.EffectiveRdpId(m, Config)}"))}");
    }

    /// <summary>
    /// Démarre les raccourcis clavier globaux de déplacement des sessions. Appelé une fois la
    /// fenêtre affichée : un échec ici ne doit jamais empêcher l'application de fonctionner.
    /// </summary>
    public static void StartHotkeys()
    {
        if (Hotkeys is not null || !IsInitialized) return;

        var service = new RdpHotkeyService();
        LiveSessions.Attach(service);
        service.Start();
        Hotkeys = service;
    }

    public static void StopHotkeys()
    {
        var service = Hotkeys;
        Hotkeys = null;
        service?.Dispose();
    }

    /// <summary>
    /// Fixe le thème avant qu'aucun élément visuel n'existe : c'est la seule façon d'avoir aussi
    /// les boutons Réduire / Agrandir / Fermer à la bonne couleur dès la première image.
    /// </summary>
    private static void ApplyStartupTheme()
    {
        try
        {
            if (Config.Theme == AppTheme.Clair) Application.Current.RequestedTheme = ApplicationTheme.Light;
            else if (Config.Theme == AppTheme.Sombre) Application.Current.RequestedTheme = ApplicationTheme.Dark;
        }
        catch (Exception ex)
        {
            Log.Write($"Thème initial : {ex.Message}");
        }
    }

    /// <summary>Applique le thème demandé à toute l'application, sans redémarrage.</summary>
    public static void ApplyTheme(AppTheme theme)
    {
        if (MainWindow?.Content is not FrameworkElement root) return;

        root.RequestedTheme = theme switch
        {
            AppTheme.Clair => ElementTheme.Light,
            AppTheme.Sombre => ElementTheme.Dark,
            // ElementTheme.Default suivrait le thème fixé au démarrage, pas celui de Windows :
            // on lit donc la préférence système pour que le changement soit immédiat.
            _ => WindowsPrefersLightTheme() ? ElementTheme.Light : ElementTheme.Dark
        };
    }

    /// <summary>Préférence de thème des applications dans Windows (clair par défaut).</summary>
    private static bool WindowsPrefersLightTheme()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is not int value || value != 0;
        }
        catch (Exception ex)
        {
            Log.Write($"Lecture du thème de Windows : {ex.Message}");
            return true;
        }
    }
}
