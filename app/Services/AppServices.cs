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
    public static SettingsViewModel Settings { get; private set; } = null!;
    public static ShareViewModel Share { get; private set; } = null!;

    public static Window? MainWindow { get; set; }
    public static XamlRoot? XamlRoot => MainWindow?.Content?.XamlRoot;

    /// <summary>Affecté par la fenêtre principale : permet à une page d'en ouvrir une autre.</summary>
    public static Action<Type>? Navigate { get; set; }

    public static bool IsInitialized { get; private set; }

    public static void Initialize()
    {
        if (IsInitialized) return;

        Paths.EnsureFolders();
        Config = AppConfig.Load();
        ApplyStartupTheme();
        Monitors = new MonitorService();

        Home = new HomeViewModel(Config, Monitors);
        Connections = new ConnectionsViewModel(Config);
        Sessions = new SessionsViewModel();
        Settings = new SettingsViewModel(Config, Monitors);
        Share = new ShareViewModel(Config);

        IsInitialized = true;
        Log.Write($"Démarrage — {Monitors.Monitors.Count} écran(s) : {string.Join(" | ",
            MonitorEnumerator.LeftToRight(Monitors.Monitors)
                .Select(m => $"{m.ShortLabel} id={RdpFile.EffectiveRdpId(m, Config)}"))}");
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
