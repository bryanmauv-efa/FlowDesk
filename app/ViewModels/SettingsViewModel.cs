using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TermServMultiScreen.Core;
using TermServMultiScreen.Services;

namespace TermServMultiScreen.ViewModels;

/// <summary>Une ligne de correspondance écran → identifiant mstsc.</summary>
public sealed partial class MappingRowViewModel : ObservableObject
{
    private readonly Action _onChanged;

    public MappingRowViewModel(MonitorInfo monitor, int rdpId, Action onChanged)
    {
        Monitor = monitor;
        RdpId = rdpId;
        _onChanged = onChanged;
    }

    public MonitorInfo Monitor { get; }

    public string Header => $"Écran {Monitor.Order} — {Monitor.PositionLabel}";
    public string Description => $"Windows {Monitor.WindowsNumber} · {Monitor.ResolutionText}"
        + $" · {(Monitor.MonitorName.Length > 0 ? Monitor.MonitorName : Monitor.GdiDeviceName)}"
        + (Monitor.IsPrimary ? " · écran principal" : "");

    [ObservableProperty]
    public partial int RdpId { get; set; }

    /// <summary>NumberBox travaille en double : passerelle vers l'entier.</summary>
    public double RdpIdValue
    {
        get => RdpId;
        set
        {
            int wanted = double.IsNaN(value) ? RdpId : (int)Math.Clamp(value, 0, 31);
            if (wanted == RdpId) return;
            RdpId = wanted;
            _onChanged();
        }
    }

    partial void OnRdpIdChanged(int value) => OnPropertyChanged(nameof(RdpIdValue));
}

/// <summary>Page « Paramètres » : thème, correspondance mstsc, diagnostic, journal.</summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly AppConfig _config;
    private readonly MonitorService _monitorService;
    private bool _loading;

    public SettingsViewModel(AppConfig config, MonitorService monitorService)
    {
        _config = config;
        _monitorService = monitorService;
        _monitorService.Changed += (_, _) => BuildRows();

        _loading = true;
        MappingStatus = "";
        ThemeIndex = config.Theme switch
        {
            AppTheme.Clair => 0,
            AppTheme.Sombre => 1,
            _ => 2
        };
        ManualMappingEnabled = config.ManualMappingEnabled;
        _loading = false;

        BuildRows();
    }

    public ObservableCollection<MappingRowViewModel> Rows { get; } = [];
    public IReadOnlyList<string> ThemeOptions { get; } = ["Clair", "Sombre", "Comme Windows"];

    [ObservableProperty]
    public partial int ThemeIndex { get; set; }

    [ObservableProperty]
    public partial bool ManualMappingEnabled { get; set; }

    [ObservableProperty]
    public partial string MappingStatus { get; set; }

    public string VersionText =>
        $"Version {Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)} · WinUI 3 / .NET 10";

    public string ConfigPath => Paths.ConfigFile;
    public string SessionsPath => Paths.SessionsFolder;
    public string LogPath => Paths.LogFile;

    partial void OnThemeIndexChanged(int value)
    {
        if (_loading) return;
        _config.Theme = value switch
        {
            0 => AppTheme.Clair,
            1 => AppTheme.Sombre,
            _ => AppTheme.Systeme
        };
        _config.Save();
        AppServices.ApplyTheme(_config.Theme);
    }

    partial void OnManualMappingEnabledChanged(bool value)
    {
        if (_loading) return;
        _config.ManualMappingEnabled = value;
        _config.Save();
        BuildRows();
        UpdateStatus();
        AppServices.Home.LoadProfile(_config.Find(AppServices.Home.SelectedProfileName));
    }

    /// <summary>Réaligne la liste déroulante quand le thème a été changé depuis le menu latéral.</summary>
    public void SyncTheme()
    {
        _loading = true;
        ThemeIndex = _config.Theme switch
        {
            AppTheme.Clair => 0,
            AppTheme.Sombre => 1,
            _ => 2
        };
        _loading = false;
    }

    private void BuildRows()
    {
        Rows.Clear();
        foreach (var monitor in MonitorEnumerator.LeftToRight(_monitorService.Monitors))
            Rows.Add(new MappingRowViewModel(monitor, RdpFile.EffectiveRdpId(monitor, _config), OnRowChanged));
        UpdateStatus();
    }

    private void OnRowChanged()
    {
        var ids = Rows.Select(r => r.RdpId).ToList();
        if (ids.Count != ids.Distinct().Count())
        {
            MappingStatus = "Deux écrans ne peuvent pas partager le même identifiant : corrigez les doublons.";
            return;
        }

        foreach (var row in Rows) _config.SetManualRdpId(row.Monitor.StableKey, row.RdpId);
        _config.Save();
        UpdateStatus();
        Log.Write("Correspondance mstsc : " + (_config.ManualMappingEnabled
            ? "manuelle — " + string.Join(", ", Rows.Select(r => $"écran {r.Monitor.Order} → id {r.RdpId}"))
            : "automatique"));
        AppServices.Home.LoadProfile(_config.Find(AppServices.Home.SelectedProfileName));
    }

    private void UpdateStatus() => MappingStatus = ManualMappingEnabled
        ? "Correspondance manuelle active : ce sont ces identifiants qui partent dans le fichier .rdp."
        : "Correspondance automatique, déduite de Windows. C'est le réglage recommandé.";

    [RelayCommand]
    private void ResetMapping()
    {
        _config.ManualMappingEnabled = false;
        _config.ManualMappings.Clear();
        _config.Save();

        _loading = true;
        ManualMappingEnabled = false;
        _loading = false;

        BuildRows();
        AppServices.Home.LoadProfile(_config.Find(AppServices.Home.SelectedProfileName));
    }

    [RelayCommand]
    private async Task MstscListAsync()
    {
        try { Process.Start(new ProcessStartInfo("mstsc.exe", "/l") { UseShellExecute = true }); }
        catch (Exception ex) { await DialogService.InfoAsync("mstsc", $"Impossible de lancer mstsc /l : {ex.Message}"); }
    }

    [RelayCommand]
    private void Identify() => IdentifyService.ShowAll(
        MonitorEnumerator.LeftToRight(_monitorService.Monitors),
        AppServices.Home.CurrentSelection,
        _config,
        TimeSpan.FromSeconds(3.5),
        () => AppServices.MainWindow?.Activate());

    [RelayCommand]
    private async Task DiagnosticAsync() =>
        await DialogService.ShowTextAsync("Diagnostic des écrans",
            DiagnosticReport.Build(_monitorService.Monitors, _config));

    [RelayCommand]
    private async Task ShowLogAsync()
    {
        var lines = Log.Tail(300);
        await DialogService.ShowTextAsync("Journal (300 dernières lignes, les plus récentes d'abord)",
            lines.Count == 0 ? "(journal vide)" : string.Join(Environment.NewLine, lines));
    }

    [RelayCommand]
    private void OpenDataFolder()
    {
        try
        {
            Paths.EnsureFolders();
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{Paths.DataFolder}\"") { UseShellExecute = true });
        }
        catch (Exception ex) { Log.Write($"Ouverture dossier : {ex.Message}"); }
    }

    [RelayCommand]
    private void RefreshMonitors()
    {
        _monitorService.Refresh(force: true);
        BuildRows();
    }
}
