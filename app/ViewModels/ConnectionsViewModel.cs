using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TermServMultiScreen.Core;
using TermServMultiScreen.Services;

namespace TermServMultiScreen.ViewModels;

/// <summary>Une ligne de la liste des connexions enregistrées.</summary>
public sealed class ConnectionItem
{
    public required string Name { get; init; }
    public required string Address { get; init; }
    public required string UserName { get; init; }
    public required string ScreensText { get; init; }
    public required string OptionsText { get; init; }
}

/// <summary>Page « Connexions » : vue d'ensemble et gestion des connexions enregistrées.</summary>
public sealed partial class ConnectionsViewModel : ObservableObject
{
    private readonly AppConfig _config;

    public ConnectionsViewModel(AppConfig config)
    {
        _config = config;
        EmptyMessage = "";
        Reload();
    }

    public ObservableCollection<ConnectionItem> Items { get; } = [];

    [ObservableProperty]
    public partial ConnectionItem? Selected { get; set; }

    [ObservableProperty]
    public partial string EmptyMessage { get; set; }

    public void Reload()
    {
        Items.Clear();
        var monitors = AppServices.Monitors.Monitors;

        foreach (var profile in _config.Profiles.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var screens = AppConfig.ResolveScreens(profile, monitors);
            string screensText = screens.Count > 0
                ? $"{screens.Count} écran(s) — {string.Join(" + ", screens.Select(m => $"{m.Order} {m.PositionLabel}"))}"
                  + $"   (selectedmonitors:s:{RdpFile.BuildSelectedMonitors(screens, _config)})"
                : profile.Screens.Count == 0
                    ? "aucun écran enregistré — à choisir depuis l'accueil"
                    : "écrans enregistrés absents de cette machine — à rechoisir depuis l'accueil";

            List<string> options = [];
            if (profile.Clipboard) options.Add("presse-papiers");
            if (profile.Printers) options.Add("imprimantes");
            if (profile.Sound) options.Add("son");
            if (profile.LocalDrives) options.Add("disques locaux");
            if (profile.ExtraRdpLines.Count > 0) options.Add($"{profile.ExtraRdpLines.Count} réglages importés");

            Items.Add(new ConnectionItem
            {
                Name = profile.Name,
                Address = profile.Address.Length > 0 ? profile.Address : "(serveur non renseigné)",
                UserName = profile.UserName.Length > 0 ? profile.UserName : "(utilisateur non renseigné)",
                ScreensText = screensText,
                OptionsText = options.Count > 0 ? string.Join(" · ", options) : "aucune option"
            });
        }

        EmptyMessage = Items.Count == 0
            ? "Aucune connexion enregistrée. Créez-en une depuis la page Accueil ou importez un fichier .rdp."
            : "";
        Selected = Items.FirstOrDefault();
    }

    [RelayCommand]
    private void Open(ConnectionItem? item)
    {
        var target = item ?? Selected;
        if (target is null) return;

        AppServices.Home.ReloadProfileList();
        AppServices.Home.SelectedProfileName = target.Name;
        AppServices.Home.LoadProfile(_config.Find(target.Name));
        AppServices.Navigate?.Invoke(typeof(Pages.HomePage));
    }

    [RelayCommand]
    private async Task DuplicateAsync(ConnectionItem? item)
    {
        var target = item ?? Selected;
        var source = _config.Find(target?.Name);
        if (source is null) return;

        string baseName = $"{source.Name} (copie)";
        string name = baseName;
        for (int i = 2; _config.Find(name) is not null; i++) name = $"{baseName} {i}";

        string? chosen = await DialogService.PromptAsync("Dupliquer la connexion", "Nom de la copie :", name);
        if (string.IsNullOrWhiteSpace(chosen)) return;
        if (_config.Find(chosen) is not null)
        {
            await DialogService.InfoAsync("Dupliquer", "Une connexion porte déjà ce nom.");
            return;
        }

        var copy = source.Clone();
        copy.Name = chosen.Trim();
        _config.Profiles.Add(copy);
        _config.Save();
        await RdpFile.WriteSessionAsync(copy, AppConfig.ResolveScreens(copy, AppServices.Monitors.Monitors),
            AppServices.Monitors.Monitors, _config);
        Reload();
        AppServices.Home.ReloadProfileList();
        AppServices.Sessions.Reload();
        Selected = Items.FirstOrDefault(i => i.Name == copy.Name);
    }

    [RelayCommand]
    private async Task DeleteAsync(ConnectionItem? item)
    {
        var target = item ?? Selected;
        var source = _config.Find(target?.Name);
        if (source is null) return;
        if (!await DialogService.ConfirmAsync("Supprimer la connexion",
                $"Supprimer définitivement « {source.Name} » ?", "Supprimer")) return;

        _config.Profiles.Remove(source);
        _config.Save();
        RdpFile.DeleteSession(source.Name);
        Reload();
        AppServices.Home.ReloadProfileList();
        AppServices.Home.LoadProfile(_config.Find(AppServices.Home.SelectedProfileName));
        AppServices.Sessions.Reload();
    }

    [RelayCommand]
    private async Task ExportAsync(ConnectionItem? item)
    {
        var target = item ?? Selected;
        var source = _config.Find(target?.Name);
        if (source is null) return;

        try
        {
            var screens = AppConfig.ResolveScreens(source, AppServices.Monitors.Monitors);
            if (screens.Count == 0)
            {
                await DialogService.InfoAsync("Copier sur le Bureau",
                    "Les écrans de cette connexion ne correspondent à aucun écran actuel. "
                    + "Ouvrez-la depuis l'accueil pour choisir les écrans.");
                return;
            }

            string content = RdpFile.Build(source, screens, AppServices.Monitors.Monitors, _config);
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string suffix = screens.Count > 1 ? $"{screens.Count} écrans" : "1 écran";
            string file = Path.Combine(desktop, $"{Paths.SafeFileName(source.Name)} ({suffix}).rdp");

            if (File.Exists(file) && !await DialogService.ConfirmAsync("Fichier existant",
                    $"Le fichier existe déjà :\n{file}\n\nLe remplacer ?", "Remplacer")) return;

            RdpFile.Write(file, content);
            Log.Write($"Fichier bureau : {file}");
            await DialogService.InfoAsync("Copier sur le Bureau", $"Fichier créé :\n{file}");
        }
        catch (Exception ex)
        {
            await DialogService.InfoAsync("Copier sur le Bureau", $"Création impossible : {ex.Message}");
        }
    }
}
