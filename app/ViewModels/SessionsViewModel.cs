using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TermServMultiScreen.Core;
using TermServMultiScreen.Services;

namespace TermServMultiScreen.ViewModels;

/// <summary>Un fichier .rdp généré, prêt à être relancé tel quel.</summary>
public sealed class SessionItem
{
    public required string Name { get; init; }
    public required string Path { get; init; }
    public required string WhenText { get; init; }
    public required string ScreensText { get; init; }
}

/// <summary>Page « Sessions » : fichiers générés et historique des lancements.</summary>
public sealed partial class SessionsViewModel : ObservableObject
{
    public SessionsViewModel()
    {
        EmptyMessage = "";
        Reload();
    }

    public ObservableCollection<SessionItem> Items { get; } = [];
    public ObservableCollection<string> History { get; } = [];

    [ObservableProperty]
    public partial SessionItem? Selected { get; set; }

    [ObservableProperty]
    public partial string EmptyMessage { get; set; }

    public void Reload()
    {
        Items.Clear();
        try
        {
            Paths.EnsureFolders();
            foreach (var file in new DirectoryInfo(Paths.SessionsFolder)
                         .GetFiles("*.rdp")
                         .OrderByDescending(f => f.LastWriteTime))
            {
                Items.Add(new SessionItem
                {
                    Name = System.IO.Path.GetFileNameWithoutExtension(file.Name),
                    Path = file.FullName,
                    WhenText = "Généré le " + file.LastWriteTime.ToString("dddd d MMMM yyyy 'à' HH:mm", CultureInfo.CurrentCulture),
                    ScreensText = DescribeFile(file.FullName)
                });
            }
        }
        catch (Exception ex)
        {
            Log.Write($"Lecture du dossier des sessions : {ex.Message}");
        }

        EmptyMessage = Items.Count == 0
            ? "Aucun fichier généré pour le moment. Lancez une session depuis l'accueil."
            : "";
        Selected = Items.FirstOrDefault();

        History.Clear();
        foreach (var line in Log.Tail(400).Where(l => l.Contains("écrans  :") || l.Contains("Lancement :")).Take(40))
            History.Add(line);
    }

    /// <summary>Résume les écrans utilisés en relisant le fichier .rdp.</summary>
    private static string DescribeFile(string path)
    {
        try
        {
            var lines = RdpFile.ReadAllLinesAnyEncoding(path);
            string? selected = lines.FirstOrDefault(l => l.StartsWith("selectedmonitors:s:", StringComparison.OrdinalIgnoreCase));
            bool multimon = lines.Any(l => l.Equals("use multimon:i:1", StringComparison.OrdinalIgnoreCase));
            string? address = lines.FirstOrDefault(l => l.StartsWith("full address:s:", StringComparison.OrdinalIgnoreCase));
            string server = address is null ? "serveur inconnu" : address["full address:s:".Length..];

            string screens = selected is not null
                ? DescribeIds(selected["selectedmonitors:s:".Length..])
                : multimon ? "tous les écrans" : "un seul écran, en fenêtre";

            return $"{server} — {screens}";
        }
        catch
        {
            return "(contenu illisible)";
        }
    }

    /// <summary>Traduit les identifiants mstsc du fichier en positions physiques compréhensibles.</summary>
    private static string DescribeIds(string rawIds)
    {
        var ids = rawIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var monitors = AppServices.Monitors.Monitors;
        List<string> parts = [];

        foreach (var raw in ids)
        {
            if (!int.TryParse(raw, out int id)) continue;
            var match = monitors.FirstOrDefault(m => RdpFile.EffectiveRdpId(m, AppServices.Config) == id);
            parts.Add(match is not null ? $"{match.Order} {match.PositionLabel}" : $"id {id}");
        }

        return parts.Count == 0
            ? $"écrans mstsc {rawIds}"
            : $"{parts.Count} écran(s) : {string.Join(" + ", parts)}";
    }

    [RelayCommand]
    private async Task RelaunchAsync(SessionItem? item)
    {
        var target = item ?? Selected;
        if (target is null) return;
        try
        {
            string mstsc = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "mstsc.exe");
            if (!File.Exists(mstsc)) mstsc = "mstsc.exe";
            Process.Start(new ProcessStartInfo(mstsc, $"\"{target.Path}\"") { UseShellExecute = true });
            Log.Write($"Relance : {target.Path}");
        }
        catch (Exception ex)
        {
            await DialogService.InfoAsync("Relancer", $"Lancement impossible : {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task ShowContentAsync(SessionItem? item)
    {
        var target = item ?? Selected;
        if (target is null) return;
        try
        {
            string content = string.Join(Environment.NewLine, RdpFile.ReadAllLinesAnyEncoding(target.Path));
            await DialogService.ShowTextAsync(target.Name + ".rdp", content);
        }
        catch (Exception ex)
        {
            await DialogService.InfoAsync("Contenu", $"Lecture impossible : {ex.Message}");
        }
    }

    [RelayCommand]
    private void OpenFolder()
    {
        try
        {
            Paths.EnsureFolders();
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{Paths.SessionsFolder}\"") { UseShellExecute = true });
        }
        catch (Exception ex) { Log.Write($"Ouverture dossier : {ex.Message}"); }
    }

    [RelayCommand]
    private async Task DeleteAsync(SessionItem? item)
    {
        var target = item ?? Selected;
        if (target is null) return;
        if (!await DialogService.ConfirmAsync("Supprimer le fichier",
                $"Supprimer le fichier généré « {target.Name}.rdp » ?\n\n"
                + "La connexion enregistrée n'est pas touchée : le fichier sera régénéré au prochain lancement.",
                "Supprimer")) return;
        try
        {
            File.Delete(target.Path);
            Reload();
        }
        catch (Exception ex)
        {
            await DialogService.InfoAsync("Supprimer", $"Suppression impossible : {ex.Message}");
        }
    }
}
