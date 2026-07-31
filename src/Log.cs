using System;
using System.Globalization;
using System.IO;

namespace TermServMultiScreen
{
    /// <summary>Dossiers de travail de l'application.</summary>
    public static class Paths
    {
        public static string DataFolder
        {
            get
            {
                string root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                return Path.Combine(root, "TermServMultiScreen");
            }
        }

        public static string SessionsFolder
        {
            get { return Path.Combine(DataFolder, "sessions"); }
        }

        public static string ConfigFile
        {
            get { return Path.Combine(DataFolder, "config.json"); }
        }

        public static string LogFile
        {
            get { return Path.Combine(DataFolder, "journal.log"); }
        }

        public static void EnsureFolders()
        {
            Directory.CreateDirectory(DataFolder);
            Directory.CreateDirectory(SessionsFolder);
        }

        /// <summary>Rend un nom de fichier utilisable a partir d'un nom de profil.</summary>
        public static string SafeFileName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "session";
            var invalid = Path.GetInvalidFileNameChars();
            var chars = name.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                foreach (char bad in invalid)
                {
                    if (chars[i] == bad) { chars[i] = '_'; break; }
                }
            }
            string result = new string(chars).Trim();
            return string.IsNullOrEmpty(result) ? "session" : result;
        }
    }

    /// <summary>Journal simple : indispensable pour diagnostiquer sans deviner.</summary>
    public static class Log
    {
        private static readonly object Gate = new object();
        private const long MaxBytes = 512 * 1024;

        public static void Write(string message)
        {
            try
            {
                lock (Gate)
                {
                    Directory.CreateDirectory(Paths.DataFolder);
                    string file = Paths.LogFile;
                    if (File.Exists(file) && new FileInfo(file).Length > MaxBytes)
                    {
                        string old = file + ".1";
                        if (File.Exists(old)) File.Delete(old);
                        File.Move(file, old);
                    }
                    File.AppendAllText(file,
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + "  " + message + Environment.NewLine);
                }
            }
            catch
            {
                // Un probleme de journal ne doit jamais empecher l'application de fonctionner.
            }
        }
    }
}
