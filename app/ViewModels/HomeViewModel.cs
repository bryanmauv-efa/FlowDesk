using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using TermServMultiScreen.Core;
using TermServMultiScreen.Services;

namespace TermServMultiScreen.ViewModels;

/// <summary>Page d'accueil : choix de la connexion, choix des écrans, lancement de la session.</summary>
public sealed partial class HomeViewModel : ObservableObject
{
    private readonly AppConfig _config;
    private readonly MonitorService _monitorService;
    private bool _loading;
    private bool _syncingPreset;

    public HomeViewModel(AppConfig config, MonitorService monitorService)
    {
        _config = config;
        _monitorService = monitorService;
        _monitorService.Changed += (_, _) => OnMonitorsChanged();

        KeyboardOptions = ["Sur cet ordinateur", "Dans la session", "En plein écran (défaut)"];

        _loading = true;
        Address = "";
        UserName = "";
        SelectionSummary = "";
        WarningText = "";
        ScreensHeader = "Écrans — classés de gauche à droite, comme dans Windows";
        StatusTitle = "Prêt à se connecter";
        StatusDetail = "";
        IdentifyEnabled = true;
        SelectedPresetIndex = -1;
        KeyboardHookIndex = 2;
        _loading = false;

        ReloadProfileList();

        var initial = _config.Find(_config.LastProfile) ?? _config.Profiles.FirstOrDefault();
        _loading = true;
        SelectedProfileName = initial?.Name;
        _loading = false;
        LoadProfile(initial);

        if (_config.Profiles.Count == 0)
        {
            SetStatus("Aucune connexion enregistrée",
                "Renseignez le serveur et l'utilisateur, choisissez vos écrans, puis « Nouvelle » pour enregistrer.");
        }
    }

    // ------------------------------------------------------------------ connexions

    public ObservableCollection<string> ProfileNames { get; } = [];
    public ObservableCollection<string> KnownAddresses { get; } = [];
    public IReadOnlyList<string> KeyboardOptions { get; }

    [ObservableProperty]
    public partial string? SelectedProfileName { get; set; }

    [ObservableProperty]
    public partial string Address { get; set; }

    [ObservableProperty]
    public partial string UserName { get; set; }

    partial void OnSelectedProfileNameChanged(string? value)
    {
        if (_loading) return;
        LoadProfile(_config.Find(value));
    }

    partial void OnAddressChanged(string value) => MarkDirty();
    partial void OnUserNameChanged(string value) => MarkDirty();

    // ------------------------------------------------------------------ écrans

    public ObservableCollection<MonitorCardViewModel> Cards { get; } = [];
    public ObservableCollection<string> Presets { get; } = [];

    [ObservableProperty]
    public partial int SelectedPresetIndex { get; set; }

    [ObservableProperty]
    public partial string SelectionSummary { get; set; }

    [ObservableProperty]
    public partial string WarningText { get; set; }

    [ObservableProperty]
    public partial bool IdentifyEnabled { get; set; }

    [ObservableProperty]
    public partial string ScreensHeader { get; set; }

    public Visibility WarningVisibility => WarningText.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

    partial void OnWarningTextChanged(string value) => OnPropertyChanged(nameof(WarningVisibility));

    partial void OnSelectedPresetIndexChanged(int value)
    {
        if (_loading || _syncingPreset || value < 0) return;
        ApplyPreset(value);
    }

    // ------------------------------------------------------------------ options

    [ObservableProperty] public partial bool Clipboard { get; set; }
    [ObservableProperty] public partial bool Printers { get; set; }
    [ObservableProperty] public partial bool Sound { get; set; }
    [ObservableProperty] public partial bool LocalDrives { get; set; }
    [ObservableProperty] public partial bool ConnectionBar { get; set; }
    [ObservableProperty] public partial bool AutoReconnect { get; set; }
    [ObservableProperty] public partial bool WindowedWhenSingle { get; set; }
    [ObservableProperty] public partial bool MultimonSwitchWhenAll { get; set; }
    [ObservableProperty] public partial int KeyboardHookIndex { get; set; }

    partial void OnClipboardChanged(bool value) => MarkDirty();
    partial void OnPrintersChanged(bool value) => MarkDirty();
    partial void OnSoundChanged(bool value) => MarkDirty();
    partial void OnLocalDrivesChanged(bool value) => MarkDirty();
    partial void OnConnectionBarChanged(bool value) => MarkDirty();
    partial void OnAutoReconnectChanged(bool value) => MarkDirty();
    partial void OnKeyboardHookIndexChanged(int value) => MarkDirty();
    partial void OnWindowedWhenSingleChanged(bool value) { MarkDirty(); UpdateSummary(); }
    partial void OnMultimonSwitchWhenAllChanged(bool value) { MarkDirty(); UpdateSummary(); }

    // ------------------------------------------------------------------ état / bandeau

    [ObservableProperty] public partial string StatusTitle { get; set; }
    [ObservableProperty] public partial string StatusDetail { get; set; }
    [ObservableProperty] public partial bool StatusIsWarning { get; set; }

    public Visibility OkVisibility => StatusIsWarning ? Visibility.Collapsed : Visibility.Visible;
    public Visibility WarnVisibility => StatusIsWarning ? Visibility.Visible : Visibility.Collapsed;

    partial void OnStatusIsWarningChanged(bool value)
    {
        OnPropertyChanged(nameof(OkVisibility));
        OnPropertyChanged(nameof(WarnVisibility));
    }

    private void SetStatus(string title, string detail = "", bool warning = false)
    {
        StatusTitle = title;
        StatusDetail = detail;
        StatusIsWarning = warning;
    }

    // ------------------------------------------------------------------ chargement

    public void ReloadProfileList()
    {
        bool previous = _loading;
        _loading = true;
        try
        {
            string? current = SelectedProfileName;
            ProfileNames.Clear();
            foreach (var name in _config.Profiles.Select(p => p.Name).OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase))
                ProfileNames.Add(name);

            KnownAddresses.Clear();
            foreach (var address in _config.Profiles.Select(p => p.Address)
                         .Where(a => a.Length > 0)
                         .Distinct(StringComparer.OrdinalIgnoreCase)
                         .OrderBy(a => a, StringComparer.CurrentCultureIgnoreCase))
                KnownAddresses.Add(address);

            SelectedProfileName = current is not null && ProfileNames.Contains(current)
                ? current
                : ProfileNames.FirstOrDefault();
        }
        finally { _loading = previous; }
    }

    /// <summary>Charge une connexion enregistrée dans l'interface.</summary>
    public void LoadProfile(Profile? profile)
    {
        _loading = true;
        try
        {
            Address = profile?.Address ?? "";
            UserName = profile?.UserName ?? "";
            Clipboard = profile?.Clipboard ?? true;
            Printers = profile?.Printers ?? true;
            Sound = profile?.Sound ?? true;
            LocalDrives = profile?.LocalDrives ?? false;
            ConnectionBar = profile?.ConnectionBar ?? true;
            AutoReconnect = profile?.AutoReconnect ?? true;
            WindowedWhenSingle = profile?.WindowedWhenSingle ?? false;
            MultimonSwitchWhenAll = profile?.MultimonSwitchWhenAll ?? true;
            KeyboardHookIndex = Math.Clamp(profile?.KeyboardHook ?? 2, 0, 2);

            var selection = AppConfig.ResolveScreens(profile, _monitorService.Monitors);
            if (selection.Count == 0) selection = DefaultSelection();
            RebuildCards(selection);
        }
        finally { _loading = false; }

        UpdateSummary();
        if (profile is not null) SetStatus("Prêt à se connecter", $"Connexion « {profile.Name} » chargée.");
    }

    private List<MonitorInfo> DefaultSelection()
    {
        var primary = _monitorService.Monitors.FirstOrDefault(m => m.IsPrimary) ?? _monitorService.Monitors.FirstOrDefault();
        return primary is null ? [] : [primary];
    }

    private void OnMonitorsChanged()
    {
        var keep = CurrentSelection.Select(m => m.StableKey).ToHashSet();
        var selection = _monitorService.Monitors.Where(m => keep.Contains(m.StableKey)).ToList();
        if (selection.Count == 0)
        {
            selection = AppConfig.ResolveScreens(_config.Find(SelectedProfileName), _monitorService.Monitors);
            if (selection.Count == 0) selection = DefaultSelection();
        }

        bool previous = _loading;
        _loading = true;
        try { RebuildCards(selection); }
        finally { _loading = previous; }

        UpdateSummary();
        SetStatus("Prêt à se connecter",
            $"Configuration des écrans mise à jour : {_monitorService.Monitors.Count} écran(s) détecté(s).");
    }

    /// <summary>Reconstruit le plan des écrans et les boutons de raccourci.</summary>
    private void RebuildCards(IList<MonitorInfo> selection)
    {
        var monitors = MonitorEnumerator.LeftToRight(_monitorService.Monitors);
        int count = monitors.Count;
        double cardHeight = count switch
        {
            <= 1 => 240,
            2 => 228,
            3 => 208,
            4 or 5 => 150,
            _ => 112
        };

        int minTop = monitors.Count > 0 ? monitors.Min(m => m.LayoutBounds.Top) : 0;
        int referenceHeight = monitors.Count > 0 ? monitors.Max(m => m.LayoutBounds.Height) : 1;

        Cards.Clear();
        foreach (var monitor in monitors)
        {
            double scale = referenceHeight > 0 ? cardHeight / referenceHeight : 1;
            double offset = Math.Clamp((monitor.LayoutBounds.Top - minTop) * scale, 0, 90);
            Cards.Add(new MonitorCardViewModel(
                monitor, selection.Contains(monitor), cardHeight, offset, OnCardToggled));
        }

        Presets.Clear();
        if (count <= 1) Presets.Add("Écran unique");
        else
        {
            Presets.Add("Tous les écrans");
            for (int n = 1; n <= count; n++) Presets.Add(n == 1 ? "1 écran" : $"{n} écrans");
            Presets.Add("Écran principal");
        }

        ScreensHeader = count switch
        {
            1 => "Écran — un seul écran détecté",
            _ => "Écrans — classés de gauche à droite, comme dans Windows"
        };
    }

    public List<MonitorInfo> CurrentSelection =>
        MonitorEnumerator.LeftToRight(Cards.Where(c => c.IsSelected).Select(c => c.Monitor));

    private void OnCardToggled(MonitorCardViewModel card)
    {
        if (_loading) return;

        // Il faut toujours au moins un écran : on refuse la dernière décoche.
        if (!card.IsSelected && Cards.Count(c => c.IsSelected) == 0)
        {
            card.SetSelectedQuiet(true);
            SetStatus("Au moins un écran est nécessaire",
                "Sélectionnez d'abord un autre écran pour libérer celui-ci.", warning: true);
            return;
        }

        MarkDirty();
        UpdateSummary();
    }

    public void ApplySelection(IEnumerable<MonitorInfo> monitors)
    {
        var wanted = monitors.ToHashSet();
        bool previous = _loading;
        _loading = true;
        try
        {
            foreach (var card in Cards) card.SetSelectedQuiet(wanted.Contains(card.Monitor));
        }
        finally { _loading = previous; }
        UpdateSummary();
    }

    private void ApplyPreset(int index)
    {
        var monitors = MonitorEnumerator.LeftToRight(_monitorService.Monitors);
        if (monitors.Count == 0) return;

        if (monitors.Count <= 1) { ApplySelection(monitors); return; }

        if (index == 0) ApplySelection(monitors);
        else if (index == monitors.Count + 1) ApplySelection(DefaultSelection());
        else ApplySelection(BestRun(index));

        MarkDirty();
    }

    /// <summary>
    /// Meilleur groupe de N écrans voisins : on privilégie celui qui contient l'écran principal,
    /// puis le plus à gauche. mstsc exige des écrans adjacents.
    /// </summary>
    private List<MonitorInfo> BestRun(int count)
    {
        var ordered = MonitorEnumerator.LeftToRight(_monitorService.Monitors);
        if (count >= ordered.Count) return ordered;

        List<MonitorInfo>? best = null;
        int bestScore = int.MinValue;
        for (int start = 0; start + count <= ordered.Count; start++)
        {
            var run = ordered.Skip(start).Take(count).ToList();
            if (!MonitorEnumerator.IsContiguous(run)) continue;
            int score = (run.Any(m => m.IsPrimary) ? 1000 : 0) + (ordered.Count - start);
            if (score > bestScore) { bestScore = score; best = run; }
        }
        return best ?? ordered.Take(count).ToList();
    }

    private void SyncPresetSelection()
    {
        var selection = CurrentSelection;
        var monitors = MonitorEnumerator.LeftToRight(_monitorService.Monitors);
        int index = -1;

        if (monitors.Count <= 1) index = 0;
        else if (selection.Count == monitors.Count) index = 0;
        else if (selection.Count >= 1 && selection.SequenceEqual(BestRun(selection.Count))) index = selection.Count;
        else if (selection.Count == 1 && selection[0].IsPrimary) index = monitors.Count + 1;

        _syncingPreset = true;
        try { SelectedPresetIndex = index; }
        finally { _syncingPreset = false; }
    }

    private void UpdateSummary()
    {
        var selection = CurrentSelection;
        if (selection.Count == 0)
        {
            SelectionSummary = "Aucun écran sélectionné.";
            WarningText = "";
            return;
        }

        string names = string.Join("  +  ", selection.Select(m => $"{m.Order} {m.PositionLabel} (Windows {m.WindowsNumber})"));
        bool plainMultimon = selection.Count == _monitorService.Monitors.Count && MultimonSwitchWhenAll;
        string technical = plainMultimon
            ? "use multimon:i:1 (tous les écrans)"
            : "selectedmonitors:s:" + RdpFile.BuildSelectedMonitors(selection, _config);

        SelectionSummary = $"{selection.Count} écran(s) : {names}      →  {technical}";

        List<string> warnings = [];
        if (!MonitorEnumerator.IsContiguous(selection))
            warnings.Add("Ces écrans ne se touchent pas : le Bureau à distance refuse en général les écrans non adjacents.");
        else if (selection.Count > 1 && !MonitorEnumerator.FillsBoundingBox(selection))
            warnings.Add("La zone couverte n'est pas un rectangle plein : si la session s'ouvre mal, choisissez des écrans alignés.");
        if (selection.Count == 1 && WindowedWhenSingle)
            warnings.Add("Mode « fenêtre maximisée » actif pour un écran unique.");

        WarningText = string.Join(Environment.NewLine, warnings);
        SyncPresetSelection();
    }

    private void MarkDirty()
    {
        if (_loading) return;
        StatusDetail = _config.Find(SelectedProfileName) is null
            ? "Renseignez le serveur et l'utilisateur, choisissez vos écrans, puis « Nouvelle » pour enregistrer."
            : "Modifications non enregistrées — « Enregistrer » pour les conserver.";
    }

    // ------------------------------------------------------------------ commandes

    private Profile BuildEffectiveProfile()
    {
        var stored = _config.Find(SelectedProfileName);
        var profile = stored?.Clone() ?? new Profile { Name = SelectedProfileName ?? "session" };
        CollectInto(profile);
        if (profile.Name.Length == 0) profile.Name = "session";
        return profile;
    }

    private void CollectInto(Profile profile)
    {
        profile.Address = Address.Trim();
        profile.UserName = UserName.Trim();
        profile.Clipboard = Clipboard;
        profile.Printers = Printers;
        profile.Sound = Sound;
        profile.LocalDrives = LocalDrives;
        profile.ConnectionBar = ConnectionBar;
        profile.AutoReconnect = AutoReconnect;
        profile.WindowedWhenSingle = WindowedWhenSingle;
        profile.MultimonSwitchWhenAll = MultimonSwitchWhenAll;
        profile.KeyboardHook = Math.Clamp(KeyboardHookIndex, 0, 2);
        profile.Screens = AppConfig.CaptureScreens(CurrentSelection);
    }

    [RelayCommand]
    private async Task NewProfileAsync()
    {
        string? name = await DialogService.PromptAsync("Nouvelle connexion", "Nom de la connexion :", "Nouvelle connexion");
        if (string.IsNullOrWhiteSpace(name)) return;
        if (_config.Find(name) is not null)
        {
            await DialogService.InfoAsync("Nouvelle connexion", "Une connexion porte déjà ce nom.");
            return;
        }

        var profile = new Profile { Name = name.Trim() };
        CollectInto(profile);
        _config.Profiles.Add(profile);
        _config.LastProfile = profile.Name;
        _config.Save();
        ReloadProfileList();
        SelectedProfileName = profile.Name;
        AppServices.Connections.Reload();

        string? file = RdpFile.WriteSession(profile, CurrentSelection, _monitorService.Monitors, _config);
        AppServices.Sessions.Reload();
        SetStatus("Prêt à se connecter", file is null
            ? $"Connexion « {profile.Name} » créée. Renseignez le serveur pour générer son fichier .rdp."
            : $"Connexion « {profile.Name} » créée — {Path.GetFileName(file)} enregistré dans le dossier des sessions.");
    }

    [RelayCommand]
    private async Task SaveProfileAsync()
    {
        var stored = _config.Find(SelectedProfileName);
        if (stored is null) { await NewProfileAsync(); return; }

        CollectInto(stored);
        _config.LastProfile = stored.Name;
        _config.Save();
        ReloadProfileList();
        AppServices.Connections.Reload();

        string? file = RdpFile.WriteSession(stored, CurrentSelection, _monitorService.Monitors, _config);
        AppServices.Sessions.Reload();
        SetStatus("Prêt à se connecter", file is null
            ? $"Connexion « {stored.Name} » enregistrée. Renseignez le serveur pour générer son fichier .rdp."
            : $"Connexion « {stored.Name} » enregistrée avec {CurrentSelection.Count} écran(s) — "
              + $"{Path.GetFileName(file)} mis à jour dans le dossier des sessions.");
    }

    [RelayCommand]
    private async Task DeleteProfileAsync()
    {
        var stored = _config.Find(SelectedProfileName);
        if (stored is null) return;
        if (!await DialogService.ConfirmAsync("Supprimer la connexion",
                $"Supprimer définitivement « {stored.Name} » ?", "Supprimer")) return;

        _config.Profiles.Remove(stored);
        _config.Save();
        RdpFile.DeleteSession(stored.Name);
        ReloadProfileList();
        LoadProfile(_config.Find(SelectedProfileName));
        AppServices.Connections.Reload();
        AppServices.Sessions.Reload();
        SetStatus(_config.Profiles.Count == 0 ? "Aucune connexion enregistrée" : "Prêt à se connecter",
            $"Connexion « {stored.Name} » supprimée, avec son fichier .rdp.");
    }

    [RelayCommand]
    private async Task ImportRdpAsync()
    {
        try
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker
            {
                SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.Desktop
            };
            picker.FileTypeFilter.Add(".rdp");

            if (AppServices.MainWindow is null) return;
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(AppServices.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var file = await picker.PickSingleFileAsync();
            if (file is null) return;

            string name = Path.GetFileNameWithoutExtension(file.Path);
            var profile = _config.Find(name);
            bool isNew = profile is null;
            profile ??= new Profile { Name = name };

            RdpFile.Import(file.Path, profile);
            profile.Screens = AppConfig.CaptureScreens(CurrentSelection.Count > 0 ? CurrentSelection : DefaultSelection());
            if (isNew) _config.Profiles.Add(profile);
            _config.LastProfile = profile.Name;
            _config.Save();

            ReloadProfileList();
            SelectedProfileName = profile.Name;
            LoadProfile(profile);
            AppServices.Connections.Reload();

            RdpFile.WriteSession(profile, CurrentSelection, _monitorService.Monitors, _config);
            AppServices.Sessions.Reload();
            SetStatus("Prêt à se connecter",
                $"Importé : {Path.GetFileName(file.Path)} — réglages conservés, mot de passe non recopié.");
        }
        catch (Exception ex)
        {
            Log.Write($"Import .rdp : {ex}");
            await DialogService.InfoAsync("Importer un .rdp", $"Import impossible : {ex.Message}");
        }
    }

    [RelayCommand]
    private void Identify()
    {
        IdentifyEnabled = false;
        IdentifyService.ShowAll(
            MonitorEnumerator.LeftToRight(_monitorService.Monitors),
            CurrentSelection,
            _config,
            TimeSpan.FromSeconds(3.5),
            () =>
            {
                IdentifyEnabled = true;
                AppServices.MainWindow?.Activate();
            });
    }

    [RelayCommand]
    private void RefreshMonitors()
    {
        bool changed = _monitorService.Refresh(force: true);
        SetStatus("Prêt à se connecter",
            $"{_monitorService.Monitors.Count} écran(s) détecté(s){(changed ? " — liste actualisée." : ".")}");
    }

    private async Task<bool> ValidateAsync(Profile profile, IList<MonitorInfo> selection)
    {
        if (profile.Address.Length == 0)
        {
            await DialogService.InfoAsync("Serveur manquant", "Indiquez le nom ou l'adresse du serveur.");
            return false;
        }
        if (selection.Count == 0)
        {
            await DialogService.InfoAsync("Écrans", "Sélectionnez au moins un écran.");
            return false;
        }
        if (selection.Count > 1 && !MonitorEnumerator.IsContiguous(selection))
        {
            return await DialogService.ConfirmAsync("Écrans non adjacents",
                "Les écrans choisis ne se touchent pas.\n\n"
                + "Le Bureau à distance n'accepte en général que des écrans adjacents : la session "
                + "risque de s'ouvrir sur un seul écran.\n\nContinuer quand même ?");
        }
        return true;
    }

    [RelayCommand]
    private async Task ConnectAsync()
    {
        try
        {
            var selection = CurrentSelection;
            var profile = BuildEffectiveProfile();
            if (!await ValidateAsync(profile, selection)) return;

            // La sélection en cours est mémorisée : au prochain démarrage tout est déjà prêt.
            var stored = _config.Find(SelectedProfileName);
            if (stored is not null)
            {
                CollectInto(stored);
                _config.LastProfile = stored.Name;
                _config.Save();
            }

            string path = Launcher.Launch(profile, selection, _monitorService.Monitors, _config);
            AppServices.Sessions.Reload();
            SetStatus("Session lancée",
                $"{selection.Count} écran(s) : {string.Join(" + ", selection.Select(m => $"{m.Order} {m.PositionLabel}"))}"
                + $"   ({Path.GetFileName(path)})");
        }
        catch (Exception ex)
        {
            Log.Write($"Connexion : {ex}");
            await DialogService.InfoAsync("Bureau à distance", $"Le lancement a échoué : {ex.Message}");
            SetStatus("Le lancement a échoué", ex.Message, warning: true);
        }
    }

    [RelayCommand]
    private async Task PreviewAsync()
    {
        try
        {
            var selection = CurrentSelection;
            var profile = BuildEffectiveProfile();
            string content = RdpFile.Build(profile, selection, _monitorService.Monitors, _config);
            string header =
                $"Écrans utilisés : {Launcher.Describe(selection, _config)}{Environment.NewLine}" +
                $"Fichier généré  : {Path.Combine(Paths.SessionsFolder, Paths.SafeFileName(profile.Name))}.rdp{Environment.NewLine}" +
                new string('-', 78) + Environment.NewLine + Environment.NewLine;
            await DialogService.ShowTextAsync("Aperçu du fichier .rdp", header + content);
        }
        catch (Exception ex)
        {
            await DialogService.InfoAsync("Aperçu", ex.Message);
        }
    }

    [RelayCommand]
    private async Task DiagnosticAsync() =>
        await DialogService.ShowTextAsync("Diagnostic des écrans",
            DiagnosticReport.Build(_monitorService.Monitors, _config));

    [RelayCommand]
    private void OpenMapping() => AppServices.Navigate?.Invoke(typeof(Pages.SettingsPage));

    [RelayCommand]
    private async Task MstscListAsync()
    {
        try { Process.Start(new ProcessStartInfo("mstsc.exe", "/l") { UseShellExecute = true }); }
        catch (Exception ex) { await DialogService.InfoAsync("mstsc", $"Impossible de lancer mstsc /l : {ex.Message}"); }
    }

    [RelayCommand]
    private void OpenSessionsFolder()
    {
        try
        {
            Paths.EnsureFolders();
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{Paths.SessionsFolder}\"") { UseShellExecute = true });
        }
        catch (Exception ex) { Log.Write($"Ouverture dossier : {ex.Message}"); }
    }

    [RelayCommand]
    private async Task CopyToDesktopAsync()
    {
        try
        {
            var selection = CurrentSelection;
            var profile = BuildEffectiveProfile();
            if (!await ValidateAsync(profile, selection)) return;

            string content = RdpFile.Build(profile, selection, _monitorService.Monitors, _config);
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string suffix = selection.Count > 1 ? $"{selection.Count} écrans" : "1 écran";
            string file = Path.Combine(desktop, $"{Paths.SafeFileName(profile.Name)} ({suffix}).rdp");

            if (File.Exists(file) && !await DialogService.ConfirmAsync("Fichier existant",
                    $"Le fichier existe déjà :\n{file}\n\nLe remplacer ?", "Remplacer")) return;

            RdpFile.Write(file, content);
            Log.Write($"Fichier bureau : {file}");
            SetStatus("Raccourci créé", file);
        }
        catch (Exception ex)
        {
            await DialogService.InfoAsync("Bureau", $"Création impossible : {ex.Message}");
        }
    }
}
