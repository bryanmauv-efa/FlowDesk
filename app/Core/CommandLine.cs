using System.Globalization;

namespace TermServMultiScreen.Core;

/// <summary>Lecture très permissive de la ligne de commande : -x, --x, /x, --x=valeur, --x valeur.</summary>
public sealed class CommandLine
{
    private readonly Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);

    public CommandLine(string[] raw)
    {
        for (int i = 0; i < raw.Length; i++)
        {
            string argument = raw[i];
            if (argument.Length == 0 || (argument[0] != '-' && argument[0] != '/')) continue;

            string name = argument.TrimStart('-', '/');
            string value = "";
            int equals = name.IndexOf('=');
            if (equals > 0)
            {
                value = name[(equals + 1)..];
                name = name[..equals];
            }
            else if (i + 1 < raw.Length && raw[i + 1].Length > 0 && raw[i + 1][0] != '-' && raw[i + 1][0] != '/')
            {
                value = raw[i + 1];
                i++;
            }
            _values[name] = value;
        }
    }

    public bool Has(string name) => _values.ContainsKey(name);

    public string? Value(string name) => _values.TryGetValue(name, out var value) ? value : null;

    public double Number(string name, double fallback) =>
        double.TryParse(Value(name), NumberStyles.Float, CultureInfo.InvariantCulture, out double value) && value > 0
            ? value : fallback;

    /// <summary>Vrai si la ligne de commande demande une action sans interface.</summary>
    public bool IsHeadless =>
        Has("connect") || Has("identify") || Has("list") || Has("print") || Has("rdptrust") || Has("resign");

    public static string Usage =>
        """
        Bureau à distance multi-écrans
        ------------------------------------------------------------

        Sans paramètre : ouvre la fenêtre de l'application.

          --profile "<nom>"   choisit une connexion enregistrée
          --connect           lance la session immédiatement
          --screens 1,2       écrans à utiliser, numérotés de gauche à droite
          --all               tous les écrans
          --identify [sec]    affiche un grand numéro sur chaque écran
          --list              rapport de diagnostic des écrans
          --rdptrust          diagnostic complet de la signature et de la confiance .rdp
          --resign            régénère et resigne le .rdp de chaque connexion enregistrée
          --print             affiche le fichier .rdp qui serait utilisé
          --out <fichier>     écrit la sortie texte dans un fichier

        Exemple : TermServMultiScreen.exe --profile "BN" --screens 1,2 --connect
        """;
}
