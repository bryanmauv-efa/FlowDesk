namespace FlowDesk.Core;

/// <summary>Reprise des donnees des versions precedentes, sans ecraser les reglages FlowDesk.</summary>
internal static class DataMigration
{
    // Seule reference au nom historique : necessaire pour retrouver les donnees existantes.
    internal const string LegacyFolderName = "TermServMultiScreen";

    internal static void Migrate(string destination)
    {
        string parent = Path.GetDirectoryName(destination)!;
        string source = Path.Combine(parent, LegacyFolderName);
        if (!Directory.Exists(source)) return;
        if (!Directory.Exists(destination))
        {
            Directory.Move(source, destination);
            return;
        }

        // Si les deux dossiers existent, les donnees actuelles ont priorite.
        // Un temoin evite de reimporter une connexion volontairement supprimee ensuite.
        string marker = Path.Combine(destination, ".migration-complete");
        if (File.Exists(marker)) return;
        CopyIfMissing(Path.Combine(source, "config.json"), Path.Combine(destination, "config.json"));
        string sessions = Path.Combine(source, "sessions");
        if (Directory.Exists(sessions))
        {
            string targetSessions = Path.Combine(destination, "sessions");
            Directory.CreateDirectory(targetSessions);
            foreach (string file in Directory.EnumerateFiles(sessions, "*.rdp"))
                CopyIfMissing(file, Path.Combine(targetSessions, Path.GetFileName(file)));
        }
        File.WriteAllText(marker, "FlowDesk");
    }

    private static void CopyIfMissing(string source, string destination)
    {
        if (File.Exists(source) && !File.Exists(destination)) File.Copy(source, destination);
    }
}
