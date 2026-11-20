using FlowDesk.Core;
using System.Runtime.InteropServices;

Paths.EnsureFolders();
File.WriteAllText(Paths.ConfigFile, """{"profiles":[{"name":"Ancienne connexion","address":"old.example"}]}""");
var config = AppConfig.Load();
var profile = config.Profiles.Single();
var id = profile.Id;
Check(id != Guid.Empty, "Migration attribue un identifiant");
config.Save(true);
Check(AppConfig.Load().Profiles.Single().Id == id, "Identifiant conservé après relecture");
string executable = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "notepad.exe");
string file = Path.Combine(Paths.DataFolder, "Connexion test.lnk");
DesktopShortcut.Write(file, executable, profile);
object shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
object link = ((dynamic)shell).CreateShortcut(file);
string arguments;
try
{
    Check(string.Equals((string)((dynamic)link).TargetPath, executable, StringComparison.OrdinalIgnoreCase), "Cible du raccourci");
    arguments = (string)((dynamic)link).Arguments;
}
finally { Marshal.FinalReleaseComObject(link); Marshal.FinalReleaseComObject(shell); }
var command = new CommandLine(arguments.Split(' '));
Check(command.IsHeadless && command.Has("connect"), "Connexion directe");
profile.Name = "Nom modifié";
profile.Address = "new.example";
profile.Clipboard = false;
profile.Screens = [new ScreenRef { Key = "screen-2", Order = 2 }];
config.Save(true);
var changed = command.ResolveProfile(AppConfig.Load());
Check(changed?.Name == "Nom modifié" && changed.Address == "new.example" && !changed.Clipboard
    && changed.Screens.Single().Key == "screen-2", "Même raccourci après changement du nom, serveur, options et écrans");
config.Profiles.Clear();
config.Profiles.Add(new Profile { Name = "Autre connexion" });
Check(command.ResolveProfile(config) is null, "Connexion supprimée sans repli vers une autre session");
Check(new CommandLine(["--profile-id", "invalide", "--connect"]).ResolveProfile(config) is null, "Identifiant invalide refusé");
Check(new CommandLine(["--profile", "Autre connexion"]).ResolveProfile(config) is not null, "Compatibilité des commandes par nom");
Console.WriteLine("Tous les contrôles des raccourcis ont réussi. Dossier de test : " + Paths.DataFolder);

static void Check(bool condition, string label)
{
    if (!condition) throw new Exception(label);
    Console.WriteLine("OK : " + label);
}

namespace FlowDesk.Core
{
    public static class Paths
    {
        public static string DataFolder { get; } = Path.Combine(Path.GetTempPath(), "FlowDesk-shortcut-tests-" + Guid.NewGuid());
        public static string ConfigFile => Path.Combine(DataFolder, "config.json");
        public static void EnsureFolders() => Directory.CreateDirectory(DataFolder);
    }
    public static class Log { public static void Write(string message) => Console.WriteLine(message); }
    public class MonitorInfo
    {
        public string StableKey { get; set; } = "";
        public string GdiDeviceName { get; set; } = "";
        public int Order { get; set; }
    }
    public static class MonitorEnumerator
    {
        public static List<MonitorInfo> LeftToRight(IEnumerable<MonitorInfo> monitors) => monitors.OrderBy(m => m.Order).ToList();
    }
}
