using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace TermServMultiScreen
{
    /// <summary>Reference persistante vers un ecran : trois niveaux de repli pour ne jamais se tromper.</summary>
    [DataContract]
    public sealed class ScreenRef
    {
        [DataMember(Name = "key")] public string Key;      // identite du port physique (la plus fiable)
        [DataMember(Name = "gdi")] public string Gdi;      // \\.\DISPLAYn
        [DataMember(Name = "order")] public int Order;     // rang gauche -> droite au moment de l'enregistrement
    }

    /// <summary>Correspondance manuelle ecran -> identifiant mstsc (soupape de securite).</summary>
    [DataContract]
    public sealed class MapEntry
    {
        [DataMember(Name = "key")] public string Key;
        [DataMember(Name = "rdpId")] public int RdpId;
    }

    [DataContract]
    public sealed class Profile
    {
        [DataMember(Name = "name")] public string Name = "";
        [DataMember(Name = "address")] public string Address = "";
        [DataMember(Name = "userName")] public string UserName = "";
        [DataMember(Name = "screens")] public List<ScreenRef> Screens = new List<ScreenRef>();

        [DataMember(Name = "clipboard")] public bool Clipboard = true;
        [DataMember(Name = "printers")] public bool Printers = true;
        [DataMember(Name = "sound")] public bool Sound = true;
        [DataMember(Name = "localDrives")] public bool LocalDrives;
        [DataMember(Name = "connectionBar")] public bool ConnectionBar = true;
        [DataMember(Name = "autoReconnect")] public bool AutoReconnect = true;
        [DataMember(Name = "windowedWhenSingle")] public bool WindowedWhenSingle;
        [DataMember(Name = "multimonSwitchWhenAll")] public bool MultimonSwitchWhenAll = true;
        [DataMember(Name = "keyboardHook")] public int KeyboardHook = 2;   // 0 local, 1 session, 2 plein ecran
        [DataMember(Name = "extraRdpLines")] public List<string> ExtraRdpLines = new List<string>();

        [OnDeserializing]
        private void OnDeserializing(StreamingContext ctx)
        {
            Name = ""; Address = ""; UserName = "";
            Screens = new List<ScreenRef>();
            Clipboard = true; Printers = true; Sound = true; LocalDrives = false;
            ConnectionBar = true; AutoReconnect = true;
            WindowedWhenSingle = false; MultimonSwitchWhenAll = true;
            KeyboardHook = 2;
            ExtraRdpLines = new List<string>();
        }

        public Profile Clone()
        {
            var p = new Profile();
            p.Name = Name; p.Address = Address; p.UserName = UserName;
            p.Screens = Screens == null ? new List<ScreenRef>() : Screens.Select(s => new ScreenRef { Key = s.Key, Gdi = s.Gdi, Order = s.Order }).ToList();
            p.Clipboard = Clipboard; p.Printers = Printers; p.Sound = Sound; p.LocalDrives = LocalDrives;
            p.ConnectionBar = ConnectionBar; p.AutoReconnect = AutoReconnect;
            p.WindowedWhenSingle = WindowedWhenSingle; p.MultimonSwitchWhenAll = MultimonSwitchWhenAll;
            p.KeyboardHook = KeyboardHook;
            p.ExtraRdpLines = ExtraRdpLines == null ? new List<string>() : new List<string>(ExtraRdpLines);
            return p;
        }
    }

    [DataContract]
    public sealed class AppConfig
    {
        [DataMember(Name = "profiles")] public List<Profile> Profiles = new List<Profile>();
        [DataMember(Name = "lastProfile")] public string LastProfile = "";
        [DataMember(Name = "manualMappingEnabled")] public bool ManualMappingEnabled;
        [DataMember(Name = "manualMappings")] public List<MapEntry> ManualMappings = new List<MapEntry>();

        [OnDeserializing]
        private void OnDeserializing(StreamingContext ctx)
        {
            Profiles = new List<Profile>();
            LastProfile = "";
            ManualMappingEnabled = false;
            ManualMappings = new List<MapEntry>();
        }

        public Profile Find(string name)
        {
            if (string.IsNullOrEmpty(name) || Profiles == null) return null;
            return Profiles.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.CurrentCultureIgnoreCase));
        }

        public int? ManualRdpId(string stableKey)
        {
            if (!ManualMappingEnabled || ManualMappings == null || string.IsNullOrEmpty(stableKey)) return null;
            var e = ManualMappings.FirstOrDefault(m => m.Key == stableKey);
            return e == null ? (int?)null : e.RdpId;
        }

        public void SetManualRdpId(string stableKey, int rdpId)
        {
            if (ManualMappings == null) ManualMappings = new List<MapEntry>();
            var e = ManualMappings.FirstOrDefault(m => m.Key == stableKey);
            if (e == null) ManualMappings.Add(new MapEntry { Key = stableKey, RdpId = rdpId });
            else e.RdpId = rdpId;
        }

        // ---------- persistance ----------

        public static AppConfig Load()
        {
            try
            {
                Paths.EnsureFolders();
                if (File.Exists(Paths.ConfigFile))
                {
                    using (var fs = File.OpenRead(Paths.ConfigFile))
                    {
                        var ser = new DataContractJsonSerializer(typeof(AppConfig));
                        var cfg = ser.ReadObject(fs) as AppConfig;
                        if (cfg != null)
                        {
                            if (cfg.Profiles == null) cfg.Profiles = new List<Profile>();
                            if (cfg.ManualMappings == null) cfg.ManualMappings = new List<MapEntry>();
                            return cfg;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Write("Configuration illisible, mise de cote : " + ex.Message);
                try
                {
                    string backup = Paths.ConfigFile + ".invalide";
                    if (File.Exists(backup)) File.Delete(backup);
                    if (File.Exists(Paths.ConfigFile)) File.Move(Paths.ConfigFile, backup);
                }
                catch { }
            }
            return new AppConfig();
        }

        public void Save()
        {
            try
            {
                Paths.EnsureFolders();
                string tmp = Paths.ConfigFile + ".tmp";
                using (var ms = new MemoryStream())
                {
                    var ser = new DataContractJsonSerializer(typeof(AppConfig));
                    ser.WriteObject(ms, this);
                    File.WriteAllBytes(tmp, ms.ToArray());
                }
                // Ecriture atomique : la configuration ne peut pas etre corrompue a mi-chemin.
                if (File.Exists(Paths.ConfigFile)) File.Delete(Paths.ConfigFile);
                File.Move(tmp, Paths.ConfigFile);
            }
            catch (Exception ex)
            {
                Log.Write("Echec d'enregistrement de la configuration : " + ex.Message);
            }
        }

        /// <summary>Selection d'ecrans enregistree -> ecrans reels, avec repli cle > nom GDI > rang.</summary>
        public static List<MonitorInfo> ResolveScreens(Profile profile, IList<MonitorInfo> monitors)
        {
            var result = new List<MonitorInfo>();
            if (profile == null || profile.Screens == null || monitors == null) return result;

            foreach (var reference in profile.Screens)
            {
                MonitorInfo hit = null;
                if (!string.IsNullOrEmpty(reference.Key))
                    hit = monitors.FirstOrDefault(m => m.StableKey == reference.Key && !result.Contains(m));
                if (hit == null && !string.IsNullOrEmpty(reference.Gdi))
                    hit = monitors.FirstOrDefault(m => string.Equals(m.GdiDeviceName, reference.Gdi, StringComparison.OrdinalIgnoreCase) && !result.Contains(m));
                if (hit == null && reference.Order >= 1)
                    hit = monitors.FirstOrDefault(m => m.Order == reference.Order && !result.Contains(m));
                if (hit != null) result.Add(hit);
            }
            return MonitorEnumerator.LeftToRight(result);
        }

        public static List<ScreenRef> CaptureScreens(IEnumerable<MonitorInfo> selection)
        {
            return selection.Select(m => new ScreenRef { Key = m.StableKey, Gdi = m.GdiDeviceName, Order = m.Order }).ToList();
        }

        /// <summary>
        /// Au tout premier demarrage, recupere les fichiers .rdp deja presents a cote de
        /// l'application pour en faire des connexions pretes a l'emploi (le mot de passe
        /// enregistre, lui, n'est jamais recopie).
        /// </summary>
        public void ImportNeighbourFiles()
        {
            if (Profiles.Count > 0) return;
            try
            {
                var folders = new List<string>();
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                folders.Add(baseDir);
                var parent = Directory.GetParent(baseDir.TrimEnd(Path.DirectorySeparatorChar));
                if (parent != null) folders.Add(parent.FullName);

                foreach (string folder in folders.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    if (!Directory.Exists(folder)) continue;
                    foreach (string file in Directory.GetFiles(folder, "*.rdp"))
                    {
                        try
                        {
                            var profile = new Profile();
                            profile.Name = Path.GetFileNameWithoutExtension(file);
                            RdpFile.Import(file, profile);
                            if (string.IsNullOrEmpty(profile.Address)) continue;
                            if (Profiles.Any(p => string.Equals(p.Name, profile.Name, StringComparison.CurrentCultureIgnoreCase))) continue;
                            Profiles.Add(profile);
                            Log.Write("Connexion importee au premier demarrage : " + profile.Name + " -> " + profile.Address);
                        }
                        catch (Exception ex) { Log.Write("Import de " + file + " : " + ex.Message); }
                    }
                    if (Profiles.Count > 0) break;
                }
                if (Profiles.Count > 0) Save();
            }
            catch (Exception ex) { Log.Write("Import initial : " + ex.Message); }
        }
    }
}
