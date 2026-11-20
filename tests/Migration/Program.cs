using FlowDesk.Core;

string root = Path.Combine(Path.GetTempPath(), "FlowDesk-migration-tests-" + Guid.NewGuid());
Directory.CreateDirectory(root);
try
{
    string legacy = Path.Combine(root, DataMigration.LegacyFolderName);
    string target = Path.Combine(root, "FlowDesk");
    DataMigration.Migrate(target);
    Check(!Directory.Exists(target), "fresh profile stays untouched");
    Directory.CreateDirectory(Path.Combine(legacy, "sessions"));
    File.WriteAllText(Path.Combine(legacy, "config.json"), "original-config");
    File.WriteAllText(Path.Combine(legacy, "sessions", "office.rdp"), "original-session");
    DataMigration.Migrate(target);
    Check(!Directory.Exists(legacy), "legacy folder is renamed");
    Check(File.ReadAllText(Path.Combine(target, "config.json")) == "original-config", "configuration preserved");
    Check(File.ReadAllText(Path.Combine(target, "sessions", "office.rdp")) == "original-session", "sessions preserved");
    DataMigration.Migrate(target);
    Directory.CreateDirectory(Path.Combine(legacy, "sessions"));
    File.WriteAllText(Path.Combine(legacy, "config.json"), "obsolete-config");
    File.WriteAllText(Path.Combine(legacy, "sessions", "office.rdp"), "obsolete-session");
    File.WriteAllText(Path.Combine(legacy, "sessions", "missing.rdp"), "missing-session");
    DataMigration.Migrate(target);
    Check(File.ReadAllText(Path.Combine(target, "config.json")) == "original-config", "current configuration wins");
    Check(File.ReadAllText(Path.Combine(target, "sessions", "office.rdp")) == "original-session", "current session wins");
    Check(File.Exists(Path.Combine(target, "sessions", "missing.rdp")), "missing session recovered");
    File.Delete(Path.Combine(target, "sessions", "missing.rdp"));
    DataMigration.Migrate(target);
    Check(!File.Exists(Path.Combine(target, "sessions", "missing.rdp")), "deleted session not resurrected");
    Console.WriteLine("Migration: all checks passed.");
}
finally { Directory.Delete(root, recursive: true); }

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}
