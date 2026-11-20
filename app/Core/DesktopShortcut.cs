using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace FlowDesk.Core;

public static class DesktopShortcut
{
    public static string InstalledExecutable()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\efa\FlowDesk");
        var folder = key?.GetValue("InstallFolder") as string;
        if (string.IsNullOrWhiteSpace(folder) || !Path.IsPathFullyQualified(folder))
            throw new InvalidOperationException("Installez FlowDesk avec le MSI avant de créer ce raccourci.");
        string executable = Path.Combine(folder, "FlowDesk.exe");
        if (!File.Exists(executable))
            throw new FileNotFoundException("L'installation de FlowDesk est introuvable. Réinstallez le MSI.");
        var version = FileVersionInfo.GetVersionInfo(executable);
        if (new Version(version.FileMajorPart, version.FileMinorPart, version.FileBuildPart) < new Version(1, 0, 12))
            throw new InvalidOperationException("Installez le MSI FlowDesk 1.0.12 ou ultérieur pour utiliser les raccourcis liés aux connexions.");
        return executable;
    }

    public static void Write(string file, string executable, Profile profile)
    {
        object? shell = null;
        object? shortcut = null;
        try
        {
            var type = Type.GetTypeFromProgID("WScript.Shell")
                ?? throw new InvalidOperationException("Le service de raccourcis Windows est indisponible.");
            shell = Activator.CreateInstance(type)!;
            shortcut = ((dynamic)shell).CreateShortcut(file);
            dynamic link = shortcut;
            link.TargetPath = executable;
            link.Arguments = $"--profile-id {profile.Id:D} --connect";
            link.WorkingDirectory = Path.GetDirectoryName(executable)!;
            link.IconLocation = executable + ",0";
            link.Description = $"FlowDesk — {profile.Name}";
            link.Save();
        }
        finally
        {
            if (shortcut is not null) Marshal.FinalReleaseComObject(shortcut);
            if (shell is not null) Marshal.FinalReleaseComObject(shell);
        }
    }
}
