using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TermServMultiScreen.Core;
using TermServMultiScreen.Core.Rdp;
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
        RdpSignatureState = "Vérification…";
        RdpPublisherState = "Vérification…";
        RdpCertificateDetail = "";
        ServerCertificateState = "Non analysé.";
        ServerCertificateDetail = "";
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

    // ------------------------------------------------------------------ confiance .rdp

    [ObservableProperty]
    public partial string RdpSignatureState { get; set; }

    [ObservableProperty]
    public partial string RdpPublisherState { get; set; }

    [ObservableProperty]
    public partial string RdpCertificateDetail { get; set; }

    [ObservableProperty]
    public partial bool RdpNeedsProvisioning { get; set; }

    /// <summary>Relit l'état réel de la signature et de la stratégie d'éditeur approuvé.</summary>
    public async Task RefreshRdpTrustAsync()
    {
        var status = await RdpTrust.RefreshAsync();

        RdpSignatureState = status.CanSign
            ? $"Opérationnelle — {status.Recipe!.Switch} avec l'empreinte SHA-1 du certificat"
            : "Indisponible : les fichiers .rdp ne peuvent pas être signés";

        RdpPublisherState = status.PublisherTrusted
            ? $"Éditeur approuvé ({status.Policy!.MatchedIn}) — aucune invite mstsc"
            : "Éditeur non approuvé : mstsc demandera confirmation à chaque connexion";

        RdpCertificateDetail = status.Certificate is { } certificate
            ? $"{certificate.Subject} · SHA-1 {certificate.Sha1} · SHA-256 {certificate.Sha256} · "
              + $"clé {certificate.KeyProvider}, valide jusqu'au {certificate.NotAfter:dd/MM/yyyy}"
            : "Aucun certificat de signature.";

        RdpNeedsProvisioning = status.NeedsProvisioning;
    }

    [RelayCommand]
    private async Task RdpDiagnosticsAsync()
    {
        string report = "";
        await DialogService.RunWithProgressAsync("Diagnostic de la confiance .rdp",
            "Vérification du certificat, de rdpsign et de la stratégie…",
            async () => { (_, report) = await RdpTrustDiagnostics.RunAsync(); });

        await DialogService.ShowTextAsync("Diagnostic de la confiance .rdp", report);
        await RefreshRdpTrustAsync();
    }

    /// <summary>
    /// Déclare l'empreinte FlowDesk comme éditeur .rdp approuvé. La ruche des stratégies est en
    /// lecture seule pour les comptes non administrateurs : Windows affiche donc une invite UAC,
    /// que l'utilisateur accepte ou refuse. Aucune ACL n'est modifiée, aucune sécurité désactivée.
    /// </summary>
    [RelayCommand]
    private async Task ProvisionPublisherTrustAsync()
    {
        var status = await RdpTrust.InitializeAsync();
        if (status.Certificate is null)
        {
            await DialogService.InfoAsync("Éditeur approuvé", "Aucun certificat de signature disponible.");
            return;
        }

        // Tentative directe : elle réussit seulement si l'application tourne déjà élevée.
        var (outcome, detail) = RdpPublisherTrustService.TryConfigure(status.Certificate);
        if (outcome is RdpPublisherTrustService.ConfigureOutcome.Configured
                     or RdpPublisherTrustService.ConfigureOutcome.AlreadyConfigured)
        {
            await RefreshRdpTrustAsync();
            await DialogService.InfoAsync("Éditeur approuvé", $"Stratégie en place.{Environment.NewLine}{detail}");
            return;
        }

        string script = RdpPublisherTrustService.WriteProvisioningScript(status.Certificate);
        bool go = await DialogService.ConfirmAsync(
            "Déclarer FlowDesk comme éditeur approuvé",
            "Cette étape s'effectue une seule fois et demande des droits administrateur, car la "
            + "zone des stratégies de Windows est volontairement en lecture seule pour les comptes "
            + "standard." + Environment.NewLine + Environment.NewLine
            + "Le script ajoute uniquement l'empreinte du certificat FlowDesk à la stratégie "
            + "« éditeurs .rdp approuvés ». Il ne désactive aucune vérification, ne touche pas au "
            + "certificat TLS des serveurs et ne modifie aucune permission." + Environment.NewLine + Environment.NewLine
            + $"Empreinte : {RdpPublisherTrustService.BuildEntry(status.Certificate)}" + Environment.NewLine
            + $"Script : {script}" + Environment.NewLine + Environment.NewLine
            + "Windows va demander une autorisation administrateur.",
            "Continuer");
        if (!go) return;

        var elevation = await RdpPublisherTrustService.RunProvisioningElevatedAsync(status.Certificate);
        await RefreshRdpTrustAsync();

        if (RdpTrust.Status?.PublisherTrusted == true)
        {
            await DialogService.InfoAsync("Éditeur approuvé",
                "FlowDesk est déclaré éditeur .rdp approuvé." + Environment.NewLine + Environment.NewLine
                + $"Empreinte : {RdpTrust.Status.Policy!.ExpectedEntry}" + Environment.NewLine
                + $"Emplacement : {RdpTrust.Status.Policy.MatchedIn}");
            return;
        }

        await DialogService.InfoAsync("Éditeur approuvé",
            (elevation.Cancelled ? "Autorisation administrateur refusée." : elevation.Detail)
            + Environment.NewLine + Environment.NewLine
            + "Un administrateur peut exécuter le script manuellement :" + Environment.NewLine + script);
    }

    [RelayCommand]
    private async Task ShowProvisioningScriptAsync()
    {
        var status = await RdpTrust.InitializeAsync();
        if (status.Certificate is null)
        {
            await DialogService.InfoAsync("Script", "Aucun certificat de signature disponible.");
            return;
        }
        string path = RdpPublisherTrustService.WriteProvisioningScript(status.Certificate);
        await DialogService.ShowTextAsync($"Script d'approbation ({path})",
            RdpPublisherTrustService.BuildProvisioningScript(status.Certificate));
    }

    [RelayCommand]
    private async Task RefreshRdpTrust() => await RefreshRdpTrustAsync();

    // ------------------------------------------------------------------ autorité du serveur

    [ObservableProperty]
    public partial string ServerCertificateState { get; set; }

    [ObservableProperty]
    public partial string ServerCertificateDetail { get; set; }

    private ServerCertificateProbe? _probe;

    /// <summary>Analyse le certificat TLS du serveur de la connexion couramment sélectionnée.</summary>
    [RelayCommand]
    private async Task ProbeServerCertificateAsync()
    {
        string host = (AppServices.Home.Address ?? "").Trim();
        if (host.Length == 0)
        {
            ServerCertificateState = "Aucun serveur renseigné sur la page Accueil.";
            ServerCertificateDetail = "";
            return;
        }

        ServerCertificateState = $"Analyse de {host}…";
        await DialogService.RunWithProgressAsync("Certificat du serveur",
            $"Interrogation de {host}…",
            async () => { _probe = await ServerCaTrustService.ProbeAsync(host); });

        if (_probe is null) return;
        ServerCertificateState = _probe.Summary;
        ServerCertificateDetail = _probe.Reached
            ? $"émis par {_probe.Issuer} · noms couverts : {(_probe.DnsNames.Count > 0 ? string.Join(", ", _probe.DnsNames) : "(aucun)")}"
              + $" · valide jusqu'au {_probe.NotAfter:dd/MM/yyyy} · SHA-1 {_probe.Sha1}"
            : "";

        if (_probe is { Reached: true, NameMatches: false, SuggestedHost: { } suggested })
        {
            bool change = await DialogService.ConfirmAsync("Nom de serveur",
                $"Le certificat du serveur ne couvre pas « {host} », mais « {suggested} »."
                + Environment.NewLine + Environment.NewLine
                + "Utiliser ce nom dans la connexion supprime l'avertissement de nom sans rien affaiblir. "
                + "Vérifiez simplement que les deux noms désignent bien la même machine."
                + Environment.NewLine + Environment.NewLine
                + $"Remplacer l'adresse par « {suggested} » ?",
                "Remplacer");
            if (change)
            {
                AppServices.Home.Address = suggested;
                ServerCertificateState = $"Adresse remplacée par {suggested} — enregistrez la connexion sur la page Accueil.";
            }
        }
    }

    /// <summary>
    /// Importe l'autorité qui a émis le certificat du serveur, dans CurrentUser\Root.
    /// L'empreinte est affichée et doit être validée avant l'installation.
    /// </summary>
    [RelayCommand]
    private async Task ImportServerCaAsync()
    {
        try
        {
            if (AppServices.MainWindow is null) return;

            var picker = new Windows.Storage.Pickers.FileOpenPicker
            {
                SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.Downloads
            };
            foreach (string extension in new[] { ".cer", ".crt", ".der", ".p7b", ".p7c", ".pem" })
                picker.FileTypeFilter.Add(extension);

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(AppServices.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var file = await picker.PickSingleFileAsync();
            if (file is null) return;

            var (candidates, error) = ServerCaTrustService.Inspect(file.Path);
            if (error is not null || candidates.Count == 0)
            {
                await DialogService.InfoAsync("Importer l'autorité",
                    $"Fichier illisible : {error ?? "aucun certificat trouvé"}");
                return;
            }

            var authority = candidates.FirstOrDefault(c => c.IsCertificateAuthority);
            if (authority is null)
            {
                await DialogService.InfoAsync("Importer l'autorité",
                    "Ce fichier contient un certificat de serveur, pas une autorité de certification."
                    + Environment.NewLine + Environment.NewLine
                    + $"Sujet : {candidates[0].Subject}" + Environment.NewLine
                    + $"Émis par : {candidates[0].Issuer}" + Environment.NewLine + Environment.NewLine
                    + "Demandez le certificat de l'autorité émettrice : c'est lui qui doit être approuvé, "
                    + "pas le certificat du serveur.");
                return;
            }

            if (authority.AlreadyTrusted)
            {
                await DialogService.InfoAsync("Importer l'autorité",
                    $"Cette autorité est déjà approuvée pour votre compte.{Environment.NewLine}{Environment.NewLine}"
                    + $"{authority.Subject}{Environment.NewLine}SHA-1 {authority.Sha1}");
                return;
            }

            bool confirmed = await DialogService.ConfirmAsync(
                "Approuver cette autorité de certification",
                ServerCaTrustService.DescribeForConfirmation(authority),
                "Approuver");
            if (!confirmed) return;

            var (success, detail) = ServerCaTrustService.Install(authority);
            if (!success)
            {
                await DialogService.InfoAsync("Importer l'autorité", $"Installation impossible : {detail}");
                return;
            }

            // Nouvelle analyse : elle prouve l'effet réel de l'installation.
            string host = (AppServices.Home.Address ?? "").Trim();
            if (host.Length > 0) _probe = await ServerCaTrustService.ProbeAsync(host);

            string outcome = _probe is null ? "" : Environment.NewLine + Environment.NewLine + "État du serveur : " + _probe.Summary;
            if (_probe is not null)
            {
                ServerCertificateState = _probe.Summary;
                if (_probe.RevocationUnknown && _probe.ChainTrusted)
                {
                    outcome += Environment.NewLine + Environment.NewLine
                        + "La liste de révocation reste inaccessible depuis ce poste : c'est le seul point "
                        + "qui subsiste et il se règle côté serveur (publication d'un point de distribution "
                        + "joignable hors du domaine).";
                }
            }

            await DialogService.InfoAsync("Autorité approuvée", detail + outcome);
        }
        catch (Exception ex)
        {
            Log.Write($"Import de l'autorité : {ex}");
            await DialogService.InfoAsync("Importer l'autorité", $"Échec : {ex.Message}");
        }
    }
}
