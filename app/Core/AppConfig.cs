using System.Text.Json;
using System.Text.Json.Serialization;

namespace TermServMultiScreen.Core;

/// <summary>Référence persistante vers un écran : trois niveaux de repli pour ne jamais se tromper.</summary>
public sealed class ScreenRef
{
    /// <summary>Identité du port physique (la plus fiable).</summary>
    public string Key { get; set; } = "";
    /// <summary>\\.\DISPLAYn.</summary>
    public string Gdi { get; set; } = "";
    /// <summary>Rang gauche → droite au moment de l'enregistrement.</summary>
    public int Order { get; set; }
}

/// <summary>Correspondance manuelle écran → identifiant mstsc (soupape de sécurité).</summary>
public sealed class MapEntry
{
    public string Key { get; set; } = "";
    public int RdpId { get; set; }
}

public enum AppTheme { Clair, Sombre, Systeme }

/// <summary>Une connexion enregistrée.</summary>
public sealed class Profile
{
    public string Name { get; set; } = "";
    public string Address { get; set; } = "";
    public string UserName { get; set; } = "";
    public List<ScreenRef> Screens { get; set; } = [];

    public bool Clipboard { get; set; } = true;
    public bool Printers { get; set; } = true;
    public bool Sound { get; set; } = true;
    public bool LocalDrives { get; set; }
    public bool ConnectionBar { get; set; } = true;
    public bool AutoReconnect { get; set; } = true;
    public bool WindowedWhenSingle { get; set; }
    public bool MultimonSwitchWhenAll { get; set; } = true;
    /// <summary>0 = cet ordinateur, 1 = la session, 2 = plein écran (défaut).</summary>
    public int KeyboardHook { get; set; } = 2;
    /// <summary>Réglages repris d'un .rdp importé (passerelle, imprimante par défaut…).</summary>
    public List<string> ExtraRdpLines { get; set; } = [];

    public Profile Clone() => new()
    {
        Name = Name,
        Address = Address,
        UserName = UserName,
        Screens = Screens.Select(s => new ScreenRef { Key = s.Key, Gdi = s.Gdi, Order = s.Order }).ToList(),
        Clipboard = Clipboard,
        Printers = Printers,
        Sound = Sound,
        LocalDrives = LocalDrives,
        ConnectionBar = ConnectionBar,
        AutoReconnect = AutoReconnect,
        WindowedWhenSingle = WindowedWhenSingle,
        MultimonSwitchWhenAll = MultimonSwitchWhenAll,
        KeyboardHook = KeyboardHook,
        ExtraRdpLines = [.. ExtraRdpLines]
    };
}

public sealed class AppConfig
{
    public List<Profile> Profiles { get; set; } = [];
    public string LastProfile { get; set; } = "";
    public bool ManualMappingEnabled { get; set; }
    public List<MapEntry> ManualMappings { get; set; } = [];
    public AppTheme Theme { get; set; } = AppTheme.Clair;

    /// <summary>
    /// Mettre une session en plein écran sur l'écran d'arrivée dès qu'on l'y fait glisser. Activé
    /// par défaut : c'est le comportement attendu quand on déplace une session en cours.
    /// </summary>
    public bool FollowScreenOnMove { get; set; } = true;

    /// <summary>
    /// L'approbation de l'éditeur .rdp a déjà été proposée : on ne redemande pas à chaque
    /// démarrage, même si l'utilisateur a refusé l'élévation.
    /// </summary>
    public bool RdpTrustPromptDone { get; set; }

    /// <summary>Certificat pour lequel la proposition a été faite : un nouveau la relance.</summary>
    public string RdpTrustPromptCertificate { get; set; } = "";

    public Profile? Find(string? name) =>
        string.IsNullOrEmpty(name) ? null
        : Profiles.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.CurrentCultureIgnoreCase));

    public int? ManualRdpId(string stableKey)
    {
        if (!ManualMappingEnabled || stableKey.Length == 0) return null;
        var entry = ManualMappings.FirstOrDefault(m => m.Key == stableKey);
        return entry?.RdpId;
    }

    public void SetManualRdpId(string stableKey, int rdpId)
    {
        var entry = ManualMappings.FirstOrDefault(m => m.Key == stableKey);
        if (entry is null) ManualMappings.Add(new MapEntry { Key = stableKey, RdpId = rdpId });
        else entry.RdpId = rdpId;
    }

    // ---------------------------------------------------------------- persistance

    public static AppConfig Load()
    {
        try
        {
            Paths.EnsureFolders();
            if (File.Exists(Paths.ConfigFile))
            {
                string json = File.ReadAllText(Paths.ConfigFile);
                var config = JsonSerializer.Deserialize(json, ConfigJson.Default.AppConfig);
                if (config is not null) return config;
            }
        }
        catch (Exception ex)
        {
            Log.Write($"Configuration illisible, mise de côté : {ex.Message}");
            try
            {
                string backup = Paths.ConfigFile + ".invalide";
                File.Delete(backup);
                File.Move(Paths.ConfigFile, backup);
            }
            catch { /* rien de plus à faire */ }
        }
        return new AppConfig();
    }

    public void Save()
    {
        try
        {
            Paths.EnsureFolders();
            string temp = Paths.ConfigFile + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(this, ConfigJson.Default.AppConfig));
            // Écriture atomique : la configuration ne peut pas être corrompue à mi-chemin.
            File.Move(temp, Paths.ConfigFile, overwrite: true);
        }
        catch (Exception ex)
        {
            Log.Write($"Échec d'enregistrement de la configuration : {ex.Message}");
        }
    }

    /// <summary>Sélection enregistrée → écrans réels, avec repli clé &gt; nom GDI &gt; rang.</summary>
    public static List<MonitorInfo> ResolveScreens(Profile? profile, IList<MonitorInfo> monitors)
    {
        List<MonitorInfo> result = [];
        if (profile is null) return result;

        foreach (var reference in profile.Screens)
        {
            MonitorInfo? hit = null;
            if (reference.Key.Length > 0)
                hit = monitors.FirstOrDefault(m => m.StableKey == reference.Key && !result.Contains(m));
            if (hit is null && reference.Gdi.Length > 0)
                hit = monitors.FirstOrDefault(m => string.Equals(m.GdiDeviceName, reference.Gdi, StringComparison.OrdinalIgnoreCase) && !result.Contains(m));
            hit ??= monitors.FirstOrDefault(m => m.Order == reference.Order && !result.Contains(m));
            if (hit is not null) result.Add(hit);
        }
        return MonitorEnumerator.LeftToRight(result);
    }

    public static List<ScreenRef> CaptureScreens(IEnumerable<MonitorInfo> selection) =>
        selection.Select(m => new ScreenRef { Key = m.StableKey, Gdi = m.GdiDeviceName, Order = m.Order }).ToList();
}

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    Converters = [typeof(JsonStringEnumConverter<AppTheme>)])]
[JsonSerializable(typeof(AppConfig))]
internal sealed partial class ConfigJson : JsonSerializerContext;
