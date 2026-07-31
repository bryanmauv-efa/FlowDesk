using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace TermServMultiScreen.Core.Share;

/// <summary>Contenu d'un code de partage, tel qu'il sera recréé chez le destinataire.</summary>
public sealed class SharedSession
{
    public required string Name { get; init; }
    public required string Address { get; init; }
    public required string UserName { get; init; }
    public required List<int> ScreenOrders { get; init; }

    public bool Clipboard { get; init; }
    public bool Printers { get; init; }
    public bool Sound { get; init; }
    public bool LocalDrives { get; init; }
    public bool ConnectionBar { get; init; }
    public bool AutoReconnect { get; init; }
    public bool WindowedWhenSingle { get; init; }
    public bool MultimonSwitchWhenAll { get; init; }
    public int KeyboardHook { get; init; }
    public List<string> ExtraRdpLines { get; init; } = [];

    public bool HasUserName => UserName.Length > 0;

    /// <summary>Profil prêt à être enregistré chez le destinataire.</summary>
    public Profile ToProfile(string name) => new()
    {
        Name = name,
        Address = Address,
        UserName = UserName,
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

    public string Describe()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Connexion   : {Name}");
        sb.AppendLine($"Serveur     : {Address}");
        sb.AppendLine($"Utilisateur : {(HasUserName ? UserName : "non partagé — à renseigner après import")}");
        sb.AppendLine($"Écrans      : {(ScreenOrders.Count == 0 ? "non précisés" : string.Join(", ", ScreenOrders.Select(o => $"n°{o}")) + " (de gauche à droite)")}");

        List<string> options = [];
        if (Clipboard) options.Add("presse-papiers");
        if (Printers) options.Add("imprimantes");
        if (Sound) options.Add("son");
        if (LocalDrives) options.Add("disques locaux");
        if (ConnectionBar) options.Add("barre de connexion");
        if (AutoReconnect) options.Add("reconnexion auto");
        if (WindowedWhenSingle) options.Add("fenêtre si 1 écran");
        sb.AppendLine($"Options     : {(options.Count > 0 ? string.Join(" · ", options) : "aucune")}");
        sb.Append($"Réglages .rdp repris : {ExtraRdpLines.Count}");
        return sb.ToString();
    }
}

/// <summary>
/// Code de partage d'une session, transportable par n'importe quel moyen (message, courriel,
/// papier). Aucune connexion n'est établie entre les deux applications : le code contient
/// lui-même toutes les données de la session.
///
/// Compacité : les champs sont sérialisés en binaire (pas de JSON), les réglages .rdp qui
/// correspondent déjà aux valeurs par défaut de l'application sont retirés puisqu'ils seront
/// régénérés à l'identique, le tout est compressé en Brotli puis encodé en Base64 URL. Un
/// contrôle d'intégrité de 2 octets rejette un code tronqué au lieu de produire n'importe quoi.
///
/// Aucun mot de passe ne circule : l'application n'en conserve aucun.
/// </summary>
public static class SessionShareCode
{
    private const string Prefix = "FD1";
    private const byte Version = 1;

    // ------------------------------------------------------------------ encodage

    public static string Encode(Profile profile, bool includeUserName, IEnumerable<int> screenOrders)
    {
        using var payload = new MemoryStream();
        using (var writer = new BinaryWriter(payload, Encoding.UTF8, leaveOpen: true))
        {
            byte flags = 0;
            if (profile.Clipboard) flags |= 1 << 0;
            if (profile.Printers) flags |= 1 << 1;
            if (profile.Sound) flags |= 1 << 2;
            if (profile.LocalDrives) flags |= 1 << 3;
            if (profile.ConnectionBar) flags |= 1 << 4;
            if (profile.AutoReconnect) flags |= 1 << 5;
            if (profile.WindowedWhenSingle) flags |= 1 << 6;
            if (profile.MultimonSwitchWhenAll) flags |= 1 << 7;

            var orders = screenOrders.Where(o => o is > 0 and < 256).Distinct().Order().ToList();
            var extras = CompactExtras(profile.ExtraRdpLines);

            writer.Write(Version);
            writer.Write(flags);
            writer.Write((byte)Math.Clamp(profile.KeyboardHook, 0, 2));
            writer.Write((byte)orders.Count);
            foreach (int order in orders) writer.Write((byte)order);

            writer.Write(profile.Name ?? "");
            writer.Write(profile.Address ?? "");
            writer.Write(includeUserName ? profile.UserName ?? "" : "");

            writer.Write7BitEncodedInt(extras.Count);
            foreach (string extra in extras) writer.Write(extra);
        }

        byte[] compressed = Compress(payload.ToArray());
        byte[] checksum = SHA256.HashData(compressed);

        byte[] framed = new byte[2 + compressed.Length];
        framed[0] = checksum[0];
        framed[1] = checksum[1];
        compressed.CopyTo(framed, 2);

        return Prefix + ToBase64Url(framed);
    }

    /// <summary>
    /// Retire les réglages .rdp que l'application régénère de toute façon à l'identique :
    /// inutile de les transporter.
    /// </summary>
    private static List<string> CompactExtras(IEnumerable<string>? extras)
    {
        var cleaned = RdpFile.CleanExtraLines(extras);
        var defaults = new HashSet<string>(RdpFile.DefaultRdpLines, StringComparer.OrdinalIgnoreCase);
        return cleaned.Where(line => !defaults.Contains(line.Trim())).ToList();
    }

    // ------------------------------------------------------------------ décodage

    public static bool TryDecode(string? code, out SharedSession? session, out string error)
    {
        session = null;
        error = "";

        if (string.IsNullOrWhiteSpace(code))
        {
            error = "Aucun code saisi.";
            return false;
        }

        // Tolérant au copier-coller : espaces, retours à la ligne, préfixe présent ou non.
        string cleaned = new string(code.Where(c => !char.IsWhiteSpace(c)).ToArray());
        if (cleaned.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
            cleaned = cleaned[Prefix.Length..];

        byte[] framed;
        try
        {
            framed = FromBase64Url(cleaned);
        }
        catch
        {
            error = "Ce code contient des caractères inattendus : vérifiez qu'il a été copié en entier.";
            return false;
        }

        if (framed.Length < 3)
        {
            error = "Ce code est trop court pour être valide.";
            return false;
        }

        byte[] compressed = framed[2..];
        byte[] checksum = SHA256.HashData(compressed);
        if (checksum[0] != framed[0] || checksum[1] != framed[1])
        {
            error = "Ce code est incomplet ou altéré (contrôle d'intégrité négatif).";
            return false;
        }

        try
        {
            byte[] payload = Decompress(compressed);
            using var stream = new MemoryStream(payload);
            using var reader = new BinaryReader(stream, Encoding.UTF8);

            byte version = reader.ReadByte();
            if (version != Version)
            {
                error = $"Ce code a été produit par une version différente de l'application (format {version}).";
                return false;
            }

            byte flags = reader.ReadByte();
            int keyboardHook = reader.ReadByte();
            int screenCount = reader.ReadByte();

            List<int> orders = [];
            for (int i = 0; i < screenCount; i++) orders.Add(reader.ReadByte());

            string name = reader.ReadString();
            string address = reader.ReadString();
            string userName = reader.ReadString();

            int extraCount = reader.Read7BitEncodedInt();
            List<string> extras = [];
            for (int i = 0; i < extraCount; i++) extras.Add(reader.ReadString());

            session = new SharedSession
            {
                Name = name,
                Address = address,
                UserName = userName,
                ScreenOrders = orders,
                Clipboard = (flags & (1 << 0)) != 0,
                Printers = (flags & (1 << 1)) != 0,
                Sound = (flags & (1 << 2)) != 0,
                LocalDrives = (flags & (1 << 3)) != 0,
                ConnectionBar = (flags & (1 << 4)) != 0,
                AutoReconnect = (flags & (1 << 5)) != 0,
                WindowedWhenSingle = (flags & (1 << 6)) != 0,
                MultimonSwitchWhenAll = (flags & (1 << 7)) != 0,
                KeyboardHook = Math.Clamp(keyboardHook, 0, 2),
                ExtraRdpLines = extras
            };

            if (session.Address.Trim().Length == 0)
            {
                error = "Ce code ne contient aucune adresse de serveur.";
                session = null;
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            error = $"Code illisible : {ex.Message}";
            return false;
        }
    }

    // ------------------------------------------------------------------ outils

    private static byte[] Compress(byte[] data)
    {
        using var output = new MemoryStream();
        using (var brotli = new BrotliStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
            brotli.Write(data, 0, data.Length);
        return output.ToArray();
    }

    private static byte[] Decompress(byte[] data)
    {
        using var input = new MemoryStream(data);
        using var brotli = new BrotliStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        brotli.CopyTo(output);
        return output.ToArray();
    }

    /// <summary>Base64 URL : ni « + », ni « / », ni « = », donc sûr dans un message ou une URL.</summary>
    private static string ToBase64Url(byte[] data) =>
        Convert.ToBase64String(data).Replace('+', '-').Replace('/', '_').TrimEnd('=');

    private static byte[] FromBase64Url(string text)
    {
        string base64 = text.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(base64.PadRight((base64.Length + 3) / 4 * 4, '='));
    }
}
