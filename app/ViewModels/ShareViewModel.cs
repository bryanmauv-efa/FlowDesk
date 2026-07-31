using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using TermServMultiScreen.Core;
using TermServMultiScreen.Core.Rdp;
using TermServMultiScreen.Core.Share;
using TermServMultiScreen.Services;

namespace TermServMultiScreen.ViewModels;

/// <summary>
/// Page « Partage » : transmettre une session à quelqu'un qui utilise la même application, ou
/// recevoir la sienne. Le code contient toutes les données : les deux applications ne se
/// parlent jamais, rien ne transite par le réseau.
/// </summary>
public sealed partial class ShareViewModel : ObservableObject
{
    private readonly AppConfig _config;

    public ShareViewModel(AppConfig config)
    {
        _config = config;
        GeneratedCode = "";
        CodeInfo = "";
        EnteredCode = "";
        ReceivedSummary = "";
        ReceiveStatus = "";
        ShareScreensSummary = "";
        Reload();
    }

    // ------------------------------------------------------------------ mode

    /// <summary>0 = rien choisi, 1 = partager, 2 = recevoir.</summary>
    [ObservableProperty]
    public partial int Mode { get; set; }

    public Visibility ChooserVisibility => Mode == 0 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ShareVisibility => Mode == 1 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ReceiveVisibility => Mode == 2 ? Visibility.Visible : Visibility.Collapsed;

    partial void OnModeChanged(int value)
    {
        OnPropertyChanged(nameof(ChooserVisibility));
        OnPropertyChanged(nameof(ShareVisibility));
        OnPropertyChanged(nameof(ReceiveVisibility));
    }

    [RelayCommand]
    private void ChooseShare()
    {
        Reload();
        Mode = 1;
    }

    [RelayCommand]
    private void ChooseReceive()
    {
        EnteredCode = "";
        ReceivedSummary = "";
        ReceiveStatus = "";
        _received = null;
        Mode = 2;
    }

    [RelayCommand]
    private void Back() => Mode = 0;

    // ------------------------------------------------------------------ partager

    public ObservableCollection<string> ProfileNames { get; } = [];

    [ObservableProperty]
    public partial string? SelectedProfileName { get; set; }

    [ObservableProperty]
    public partial bool IncludeUserName { get; set; }

    [ObservableProperty]
    public partial string GeneratedCode { get; set; }

    [ObservableProperty]
    public partial string CodeInfo { get; set; }

    [ObservableProperty]
    public partial string ShareScreensSummary { get; set; }

    public Visibility CodeVisibility => GeneratedCode.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

    partial void OnGeneratedCodeChanged(string value) => OnPropertyChanged(nameof(CodeVisibility));

    partial void OnSelectedProfileNameChanged(string? value)
    {
        GeneratedCode = "";
        CodeInfo = "";
        UpdateShareSummary();
    }

    partial void OnIncludeUserNameChanged(bool value)
    {
        GeneratedCode = "";
        CodeInfo = "";
    }

    public void Reload()
    {
        string? current = SelectedProfileName;
        ProfileNames.Clear();
        foreach (var name in _config.Profiles.Select(p => p.Name)
                     .OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase))
            ProfileNames.Add(name);

        SelectedProfileName = current is not null && ProfileNames.Contains(current)
            ? current
            : ProfileNames.FirstOrDefault();
        UpdateShareSummary();
    }

    private void UpdateShareSummary()
    {
        var profile = _config.Find(SelectedProfileName);
        if (profile is null)
        {
            ShareScreensSummary = _config.Profiles.Count == 0
                ? "Aucune connexion enregistrée à partager."
                : "";
            return;
        }

        var orders = profile.Screens.Select(s => s.Order).Where(o => o > 0).Distinct().Order().ToList();
        ShareScreensSummary =
            $"Serveur : {(profile.Address.Length > 0 ? profile.Address : "(non renseigné)")}"
            + $"   ·   Écrans partagés : {(orders.Count > 0 ? string.Join(", ", orders.Select(o => $"n°{o}")) + " (rangs de gauche à droite)" : "aucun enregistré")}"
            + $"   ·   Réglages .rdp : {profile.ExtraRdpLines.Count}";
    }

    [RelayCommand]
    private async Task GenerateAsync()
    {
        var profile = _config.Find(SelectedProfileName);
        if (profile is null)
        {
            await DialogService.InfoAsync("Partager", "Sélectionnez une connexion à partager.");
            return;
        }
        if (profile.Address.Trim().Length == 0)
        {
            await DialogService.InfoAsync("Partager",
                "Cette connexion n'a pas d'adresse de serveur : renseignez-la avant de la partager.");
            return;
        }

        var orders = profile.Screens.Select(s => s.Order).Where(o => o > 0);
        GeneratedCode = SessionShareCode.Encode(profile, IncludeUserName, orders);

        // Relecture immédiate : on ne propose jamais un code qu'on n'a pas su relire.
        if (!SessionShareCode.TryDecode(GeneratedCode, out _, out string error))
        {
            GeneratedCode = "";
            CodeInfo = "";
            await DialogService.InfoAsync("Partager", $"Le code produit n'est pas relisible : {error}");
            return;
        }

        CodeInfo = $"{GeneratedCode.Length} caractères"
                 + (IncludeUserName ? " · utilisateur inclus" : " · sans l'utilisateur")
                 + " · aucun mot de passe";
        Log.Write($"Code de partage produit pour « {profile.Name} » : {GeneratedCode.Length} caractères, "
                + $"utilisateur {(IncludeUserName ? "inclus" : "exclu")}.");
    }

    [RelayCommand]
    private async Task CopyCodeAsync()
    {
        if (GeneratedCode.Length == 0) return;
        try
        {
            var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
            package.SetText(GeneratedCode);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
            CodeInfo = $"{GeneratedCode.Length} caractères · copié dans le presse-papiers";
        }
        catch (Exception ex)
        {
            await DialogService.InfoAsync("Copier", $"Copie impossible : {ex.Message}");
        }
    }

    // ------------------------------------------------------------------ recevoir

    private SharedSession? _received;

    [ObservableProperty]
    public partial string EnteredCode { get; set; }

    [ObservableProperty]
    public partial string ReceivedSummary { get; set; }

    [ObservableProperty]
    public partial string ReceiveStatus { get; set; }

    public Visibility ReceivedVisibility => ReceivedSummary.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Un code doit avoir été analysé avec succès avant de pouvoir être importé.</summary>
    public bool CanImport => ReceivedSummary.Length > 0;

    partial void OnReceivedSummaryChanged(string value)
    {
        OnPropertyChanged(nameof(ReceivedVisibility));
        OnPropertyChanged(nameof(CanImport));
    }

    partial void OnEnteredCodeChanged(string value)
    {
        ReceivedSummary = "";
        ReceiveStatus = "";
        _received = null;
    }

    [RelayCommand]
    private void Analyse()
    {
        if (!SessionShareCode.TryDecode(EnteredCode, out var session, out string error) || session is null)
        {
            _received = null;
            ReceivedSummary = "";
            ReceiveStatus = error;
            return;
        }

        _received = session;
        ReceivedSummary = session.Describe();

        int available = AppServices.Monitors.Monitors.Count;
        var missing = session.ScreenOrders.Where(o => o > available).ToList();
        ReceiveStatus = missing.Count == 0
            ? "Code valide. Vérifiez le contenu puis importez la connexion."
            : $"Code valide. Attention : ce partage utilise l'écran n°{string.Join(", n°", missing)} "
              + $"alors que ce poste en compte {available} : la sélection sera ramenée aux écrans disponibles.";
    }

    [RelayCommand]
    private async Task ImportAsync()
    {
        if (_received is null)
        {
            ReceiveStatus = "Analysez d'abord le code.";
            return;
        }

        // Nom libre : on ne remplace jamais une connexion existante sans le demander.
        string name = _received.Name.Trim().Length > 0 ? _received.Name.Trim() : "Session partagée";
        if (_config.Find(name) is not null)
        {
            string proposal = name;
            for (int i = 2; _config.Find(proposal) is not null; i++) proposal = $"{name} ({i})";

            string? chosen = await DialogService.PromptAsync("Nom de la connexion",
                $"Une connexion « {name} » existe déjà sur ce poste. Sous quel nom importer celle-ci ?",
                proposal);
            if (string.IsNullOrWhiteSpace(chosen)) return;
            name = chosen.Trim();

            if (_config.Find(name) is not null)
            {
                bool replace = await DialogService.ConfirmAsync("Remplacer",
                    $"« {name} » existe déjà. Remplacer son contenu par la session partagée ?", "Remplacer");
                if (!replace) return;
                _config.Profiles.RemoveAll(p => string.Equals(p.Name, name, StringComparison.CurrentCultureIgnoreCase));
                RdpFile.DeleteSession(name);
            }
        }

        var profile = _received.ToProfile(name);

        // Les écrans sont partagés par rang de gauche à droite : c'est la seule notion qui a un
        // sens d'un poste à l'autre. On les remappe sur les écrans réellement présents.
        var monitors = MonitorEnumerator.LeftToRight(AppServices.Monitors.Monitors);
        var selection = monitors.Where(m => _received.ScreenOrders.Contains(m.Order)).ToList();
        if (selection.Count == 0)
        {
            var primary = monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors.FirstOrDefault();
            if (primary is not null) selection = [primary];
        }
        profile.Screens = AppConfig.CaptureScreens(selection);

        _config.Profiles.Add(profile);
        _config.LastProfile = profile.Name;
        _config.Save();

        string? path = await RdpFile.WriteSessionAsync(profile, selection, AppServices.Monitors.Monitors, _config);

        AppServices.Home.ReloadProfileList();
        AppServices.Home.SelectedProfileName = profile.Name;
        AppServices.Home.LoadProfile(profile);
        AppServices.Connections.Reload();
        AppServices.Sessions.Reload();
        Reload();

        Log.Write($"Session partagée importée : « {profile.Name} » → {profile.Address}, "
                + $"{selection.Count} écran(s), utilisateur {(_received.HasUserName ? "fourni" : "à renseigner")}.");

        string suite = _received.HasUserName
            ? ""
            : Environment.NewLine + Environment.NewLine
              + "L'utilisateur n'était pas partagé : renseignez-le sur la page Accueil avant de vous connecter.";

        await DialogService.InfoAsync("Session importée",
            $"« {profile.Name} » est disponible dans vos connexions."
            + Environment.NewLine + Environment.NewLine
            + $"Serveur : {profile.Address}" + Environment.NewLine
            + $"Écrans : {(selection.Count > 0 ? string.Join(" + ", selection.Select(m => $"{m.Order} {m.PositionLabel}")) : "aucun")}"
            + (path is not null ? Environment.NewLine + "Fichier .rdp généré et signé." : "")
            + suite);

        EnteredCode = "";
        ReceivedSummary = "";
        ReceiveStatus = "";
        _received = null;
        AppServices.Navigate?.Invoke(typeof(Pages.HomePage));
    }
}
