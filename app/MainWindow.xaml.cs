using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using TermServMultiScreen.Core;
using TermServMultiScreen.Pages;
using TermServMultiScreen.Services;
using Windows.Graphics;
using Windows.UI;

namespace TermServMultiScreen;

public sealed partial class MainWindow : Window
{
    private static readonly Dictionary<string, Type> Destinations = new()
    {
        ["home"] = typeof(HomePage),
        ["connections"] = typeof(ConnectionsPage),
        ["sessions"] = typeof(SessionsPage),
        ["share"] = typeof(SharePage),
        ["settings"] = typeof(SettingsPage),
        ["about"] = typeof(AboutPage)
    };

    public MainWindow()
    {
        StartupLog.Step("chargement de MainWindow.xaml");
        InitializeComponent();

        // La barre de titre personnalisée dépend de fonctions du système : sur une build de
        // Windows plus ancienne, mieux vaut une barre standard qu'une application qui se ferme.
        StartupLog.Try("barre de titre personnalisée", () =>
        {
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(AppTitleBar);
            AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        });

        TrySetIcon();
        SizeAndCentre();

        StartupLog.Step("mise en place de la fenêtre et du thème");
        AppServices.MainWindow = this;
        AppServices.Navigate = NavigateTo;
        AppServices.ApplyTheme(AppServices.Config.Theme);
        UpdateThemeIcon();
        UpdateCaptionButtonColours();

        // Couvre le changement de thème demandé ici comme celui venant de Windows.
        RootGrid.ActualThemeChanged += (_, _) => UpdateCaptionButtonColours();
        RootGrid.Loaded += async (_, _) =>
        {
            StartupLog.Step("interface affichée");
            AppServices.ApplyTheme(AppServices.Config.Theme);
            UpdateThemeIcon();
            UpdateCaptionButtonColours();

            // Configuration unique de la confiance .rdp, une fois la fenêtre affichée : la
            // signature est préparée et l'approbation de l'éditeur proposée si elle manque.
            try
            {
                StartupLog.Step("vérification de la confiance des fichiers .rdp");
                await Core.Rdp.RdpTrustOnboarding.OfferIfNeededAsync();
            }
            catch (Exception ex)
            {
                Log.Write("Préparation de la confiance .rdp impossible (l'application reste utilisable)"
                        + Environment.NewLine + StartupLog.Describe(ex));
            }
        };

        StartupLog.Step("affichage de la page d'accueil");
        NavFrame.Navigate(typeof(HomePage), null, new SuppressNavigationTransitionInfo());
        AppServices.Monitors.StartWatching();

        // Déplacement d'une session en cours d'utilisation, sans quitter la session : les
        // raccourcis sont le seul moyen d'agir depuis un plein écran qui capte tout le clavier.
        StartupLog.Try("raccourcis de déplacement des sessions", AppServices.StartHotkeys);

        Closed += (_, _) =>
        {
            Log.Write("Fermeture de la fenêtre principale demandée.");
            AppServices.Monitors.StopWatching();
            AppServices.StopHotkeys();
            IdentifyService.CloseAll();
            AppServices.Config.Save();
        };
    }

    private void TrySetIcon()
    {
        try
        {
            string icon = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
            if (File.Exists(icon)) AppWindow.SetIcon(icon);
        }
        catch (Exception ex) { Log.Write($"Icône de fenêtre : {ex.Message}"); }
    }

    /// <summary>
    /// Les boutons Réduire / Agrandir / Fermer sont dessinés par Windows : leurs couleurs doivent
    /// être alignées à la main sur le thème de l'application, sinon ils restent invisibles.
    /// </summary>
    private void UpdateCaptionButtonColours()
    {
        try
        {
            bool dark = RootGrid.ActualTheme == ElementTheme.Dark;
            Color foreground = dark ? Colors.White : Color.FromArgb(255, 26, 26, 26);
            Color inactive = dark ? Color.FromArgb(255, 150, 150, 150) : Color.FromArgb(255, 120, 120, 120);
            Color hover = dark ? Color.FromArgb(25, 255, 255, 255) : Color.FromArgb(20, 0, 0, 0);
            Color pressed = dark ? Color.FromArgb(50, 255, 255, 255) : Color.FromArgb(40, 0, 0, 0);

            var bar = AppWindow.TitleBar;
            bar.ButtonBackgroundColor = Colors.Transparent;
            bar.ButtonInactiveBackgroundColor = Colors.Transparent;
            bar.ButtonForegroundColor = foreground;
            bar.ButtonHoverForegroundColor = foreground;
            bar.ButtonPressedForegroundColor = foreground;
            bar.ButtonInactiveForegroundColor = inactive;
            bar.ButtonHoverBackgroundColor = hover;
            bar.ButtonPressedBackgroundColor = pressed;
        }
        catch (Exception ex) { Log.Write($"Couleurs des boutons de fenêtre : {ex.Message}"); }
    }

    /// <summary>Ouvre la fenêtre à une taille confortable, centrée sur l'écran courant.</summary>
    private void SizeAndCentre()
    {
        try
        {
            var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
            double scale = NativeMethods.GetDpiForWindow(
                WinRT.Interop.WindowNative.GetWindowHandle(this)) / 96.0;
            if (scale <= 0) scale = 1;

            int width = (int)Math.Min(1440 * scale, area.Width - 60 * scale);
            int height = (int)Math.Min(1000 * scale, area.Height - 60 * scale);

            AppWindow.MoveAndResize(new RectInt32(
                area.X + (area.Width - width) / 2,
                area.Y + (area.Height - height) / 2,
                width, height));
        }
        catch (Exception ex) { Log.Write($"Dimensionnement de la fenêtre : {ex.Message}"); }
    }

    private void NavigateTo(Type page)
    {
        if (NavFrame.CurrentSourcePageType != page)
            NavFrame.Navigate(page, null, new EntranceNavigationTransitionInfo());

        foreach (var item in NavView.MenuItems.OfType<NavigationViewItem>())
        {
            if (item.Tag is string tag && Destinations.TryGetValue(tag, out var type) && type == page)
            {
                NavView.SelectedItem = item;
                break;
            }
        }
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is not NavigationViewItem item) return;
        if (item.Tag is not string tag || !Destinations.TryGetValue(tag, out var page)) return;
        if (NavFrame.CurrentSourcePageType == page) return;

        NavFrame.Navigate(page, null, new EntranceNavigationTransitionInfo());
    }

    /// <summary>Le bouton du menu vit dans la barre de titre : il reste atteignable menu replié.</summary>
    private void TitleBar_PaneToggleRequested(TitleBar sender, object args) =>
        NavView.IsPaneOpen = !NavView.IsPaneOpen;

    /// <summary>Bascule simple clair / sombre. Le choix « comme Windows » est dans Paramètres.</summary>
    private void ThemeButton_Click(object sender, RoutedEventArgs e)
    {
        var config = AppServices.Config;
        bool currentlyDark = RootGrid.ActualTheme == ElementTheme.Dark;
        config.Theme = currentlyDark ? AppTheme.Clair : AppTheme.Sombre;
        config.Save();

        AppServices.ApplyTheme(config.Theme);
        AppServices.Settings.SyncTheme();
        UpdateThemeIcon();
        UpdateCaptionButtonColours();
    }

    private void UpdateThemeIcon()
    {
        bool dark = AppServices.Config.Theme == AppTheme.Sombre
                    || (AppServices.Config.Theme == AppTheme.Systeme && RootGrid.ActualTheme == ElementTheme.Dark);

        // Lune en thème sombre, soleil en thème clair.
        ThemeIcon.Glyph = dark ? "" : "";
        ToolTipService.SetToolTip(ThemeButton, dark
            ? "Thème sombre — cliquer pour passer en clair"
            : "Thème clair — cliquer pour passer en sombre");
    }
}
