using System.Drawing;

namespace TermServMultiScreen.Core;

/// <summary>
/// Géométrie pure des dispositions d'écrans : rangées, ordre de lecture, libellés de position et
/// groupes d'écrans voisins. Ne dépend d'aucun élément d'interface et fonctionne pour n'importe
/// quelle disposition — tailles différentes, écran en dessous, écran en portrait, décalages.
/// </summary>
public static class MonitorLayout
{
    /// <summary>
    /// Regroupe les écrans en rangées. Un écran rejoint une rangée si sa bande verticale recouvre
    /// au moins la moitié du cœur de cette rangée : c'est ce qui permet de traiter ensemble un
    /// 1080p et un 1200p côte à côte, sans confondre un écran placé franchement en dessous.
    ///
    /// Le cœur d'une rangée est l'intersection de ses membres, jamais leur union : sans cela, un
    /// écran en portrait couvrant deux rangées élargirait la bande et absorberait la rangée du
    /// dessous.
    /// </summary>
    public static List<List<int>> DetectRows(IReadOnlyList<Rectangle> bounds)
    {
        List<List<int>> rows = [];
        List<(int Top, int Bottom)> cores = [];

        foreach (int index in Enumerable.Range(0, bounds.Count).OrderBy(i => bounds[i].Top).ThenBy(i => bounds[i].Left))
        {
            var rectangle = bounds[index];
            int match = -1;
            int bestOverlap = 0;

            for (int row = 0; row < cores.Count; row++)
            {
                int overlap = Math.Min(rectangle.Bottom, cores[row].Bottom) - Math.Max(rectangle.Top, cores[row].Top);
                int reference = Math.Min(rectangle.Height, cores[row].Bottom - cores[row].Top);
                if (reference <= 0 || overlap * 2 < reference) continue;

                // En cas d'ambiguïté, la rangée la mieux recouverte gagne.
                if (overlap > bestOverlap) { bestOverlap = overlap; match = row; }
            }

            if (match < 0)
            {
                rows.Add([index]);
                cores.Add((rectangle.Top, rectangle.Bottom));
            }
            else
            {
                rows[match].Add(index);
                cores[match] = (Math.Max(cores[match].Top, rectangle.Top), Math.Min(cores[match].Bottom, rectangle.Bottom));
            }
        }

        // Rangées de haut en bas, écrans de gauche à droite dans chaque rangée.
        var order = Enumerable.Range(0, rows.Count).OrderBy(r => cores[r].Top).ToList();
        return order.Select(r => rows[r].OrderBy(i => bounds[i].Left).ToList()).ToList();
    }

    /// <summary>
    /// Ordre affiché à l'utilisateur : de gauche à droite, rangée par rangée en partant du haut.
    /// Pour une disposition sur une seule rangée — le cas courant — c'est exactement l'ordre de
    /// gauche à droite d'avant.
    /// </summary>
    public static List<int> ReadingOrder(IReadOnlyList<Rectangle> bounds) =>
        DetectRows(bounds).SelectMany(row => row).ToList();

    /// <summary>
    /// Libellés de position, indexés comme <paramref name="bounds"/>. Une seule rangée donne
    /// Gauche / Centre / Droite ; plusieurs rangées donnent « Haut gauche », « Bas », etc.
    /// </summary>
    public static string[] PositionLabels(IReadOnlyList<Rectangle> bounds)
    {
        var labels = new string[bounds.Count];
        if (bounds.Count == 0) return labels;
        if (bounds.Count == 1) { labels[0] = "Écran unique"; return labels; }

        var rows = DetectRows(bounds);

        for (int row = 0; row < rows.Count; row++)
        {
            string vertical = rows.Count == 1 ? "" : VerticalLabel(row, rows.Count);
            var line = rows[row];

            for (int column = 0; column < line.Count; column++)
            {
                string horizontal = line.Count == 1 && rows.Count > 1 ? "" : HorizontalLabel(column, line.Count);

                labels[line[column]] = (vertical, horizontal) switch
                {
                    ("", var h) => h,
                    (var v, "") => v,
                    var (v, h) => $"{v} {h.ToLowerInvariant()}"
                };
            }
        }
        return labels;
    }

    private static string HorizontalLabel(int index, int count) => count switch
    {
        1 => "Écran unique",
        _ when index == 0 => "Gauche",
        _ when index == count - 1 => "Droite",
        3 => "Centre",
        _ => $"Milieu {index}"
    };

    private static string VerticalLabel(int row, int count) => count switch
    {
        2 => row == 0 ? "Haut" : "Bas",
        3 => row switch { 0 => "Haut", 1 => "Milieu", _ => "Bas" },
        _ => $"Rangée {row + 1}"
    };

    // ------------------------------------------------------------------ adjacence

    /// <summary>Deux écrans se touchent : côte à côte, ou l'un au-dessus de l'autre.</summary>
    public static bool Touches(Rectangle a, Rectangle b)
    {
        if (a.IntersectsWith(b)) return true;

        bool verticalOverlap = a.Top < b.Bottom && b.Top < a.Bottom;
        bool horizontalOverlap = a.Left < b.Right && b.Left < a.Right;

        if (verticalOverlap && (a.Right == b.Left || b.Right == a.Left)) return true;
        if (horizontalOverlap && (a.Bottom == b.Top || b.Bottom == a.Top)) return true;
        return false;
    }

    /// <summary>La sélection forme-t-elle un seul bloc, de proche en proche ?</summary>
    public static bool IsContiguous(IReadOnlyList<Rectangle> selection)
    {
        if (selection.Count <= 1) return true;

        var seen = new bool[selection.Count];
        var queue = new Queue<int>();
        queue.Enqueue(0);
        seen[0] = true;
        int visited = 1;

        while (queue.Count > 0)
        {
            int current = queue.Dequeue();
            for (int i = 0; i < selection.Count; i++)
            {
                if (seen[i] || !Touches(selection[current], selection[i])) continue;
                seen[i] = true;
                visited++;
                queue.Enqueue(i);
            }
        }
        return visited == selection.Count;
    }

    /// <summary>La zone couverte est-elle un rectangle plein, sans trou ni décrochement ?</summary>
    public static bool FillsBoundingBox(IReadOnlyList<Rectangle> selection)
    {
        if (selection.Count == 0) return true;
        var box = Union(selection);
        long area = selection.Sum(r => (long)r.Width * r.Height);
        return area >= (long)box.Width * box.Height;
    }

    public static Rectangle Union(IEnumerable<Rectangle> bounds)
    {
        Rectangle? box = null;
        foreach (var rectangle in bounds)
            box = box is null ? rectangle : Rectangle.Union(box.Value, rectangle);
        return box ?? Rectangle.Empty;
    }

    /// <summary>
    /// Meilleur groupe de <paramref name="count"/> écrans voisins, valable pour une disposition en
    /// deux dimensions : on fait croître un groupe connexe depuis chaque écran, puis on préfère
    /// celui qui contient l'écran principal, qui forme un rectangle plein, et qui commence le plus
    /// en haut à gauche. mstsc n'accepte que des écrans adjacents.
    /// </summary>
    public static List<int> BestAdjacentGroup(IReadOnlyList<Rectangle> bounds, int count, int primaryIndex)
    {
        var reading = ReadingOrder(bounds);
        if (count >= bounds.Count) return reading;
        if (count <= 0) return [];

        var rank = new int[bounds.Count];
        for (int position = 0; position < reading.Count; position++) rank[reading[position]] = position;

        List<int>? best = null;
        int bestScore = int.MinValue;

        // Au-delà d'une douzaine d'écrans on se contente d'une croissance de proche en proche :
        // l'énumération exhaustive n'aurait plus d'intérêt pratique.
        var candidates = bounds.Count <= 12
            ? Combinations(bounds.Count, count)
            : [GrowGroup(bounds, count, reading, rank)];

        foreach (var group in candidates)
        {
            if (group.Count != count) continue;

            var rectangles = group.Select(i => bounds[i]).ToList();
            if (!IsContiguous(rectangles)) continue;

            int score = 0;
            if (primaryIndex >= 0 && group.Contains(primaryIndex)) score += 1000;
            // mstsc se comporte mieux quand la zone couverte est un rectangle plein.
            if (FillsBoundingBox(rectangles)) score += 500;
            // À égalité, les écrans les plus en haut à gauche.
            score -= group.Sum(i => rank[i]);

            if (score > bestScore) { bestScore = score; best = group; }
        }

        return best is not null
            ? best.OrderBy(i => rank[i]).ToList()
            : reading.Take(count).ToList();
    }

    /// <summary>Toutes les combinaisons de <paramref name="count"/> indices parmi n.</summary>
    private static List<List<int>> Combinations(int n, int count)
    {
        List<List<int>> result = [];
        var current = new int[count];

        void Walk(int start, int depth)
        {
            if (depth == count) { result.Add([.. current]); return; }
            for (int i = start; i < n; i++)
            {
                current[depth] = i;
                Walk(i + 1, depth + 1);
            }
        }

        Walk(0, 0);
        return result;
    }

    /// <summary>Croissance de proche en proche depuis le premier écran, pour les cas très nombreux.</summary>
    private static List<int> GrowGroup(IReadOnlyList<Rectangle> bounds, int count, List<int> reading, int[] rank)
    {
        List<int> group = [reading[0]];
        while (group.Count < count)
        {
            int next = -1;
            foreach (int candidate in reading)
            {
                if (group.Contains(candidate)) continue;
                if (!group.Any(member => Touches(bounds[member], bounds[candidate]))) continue;
                if (next < 0 || rank[candidate] < rank[next]) next = candidate;
            }
            if (next < 0) break;
            group.Add(next);
        }
        return group;
    }

    // ------------------------------------------------------------------ vérification

    /// <summary>
    /// Rapport lisible d'une disposition : rangées, ordre, libellés, contiguïté et groupes
    /// proposés par les raccourcis. Sert au mode --maptest pour vérifier des dispositions que la
    /// machine courante n'a pas physiquement.
    /// </summary>
    public static string Describe(string title, IReadOnlyList<Rectangle> bounds, int primaryIndex = 0)
    {
        var sb = new System.Text.StringBuilder();
        var rows = DetectRows(bounds);
        var order = ReadingOrder(bounds);
        var labels = PositionLabels(bounds);
        var box = Union(bounds);

        sb.AppendLine($"=== {title} ===");
        sb.AppendLine($"bureau virtuel : {box.Width} × {box.Height} (origine {box.X},{box.Y})   "
                    + $"écrans : {bounds.Count}   rangées : {rows.Count}");

        for (int position = 0; position < order.Count; position++)
        {
            int index = order[position];
            var r = bounds[index];
            string orientation = r.Height > r.Width ? " portrait" : "";
            sb.AppendLine($"  rang {position + 1} → entrée #{index}  {labels[index],-14} "
                        + $"{r.Width}×{r.Height} en ({r.X},{r.Y}){orientation}"
                        + (index == primaryIndex ? "  [principal]" : ""));
        }

        sb.AppendLine($"  contigu : {(IsContiguous(bounds) ? "oui" : "non")}   "
                    + $"rectangle plein : {(FillsBoundingBox(bounds) ? "oui" : "non")}");

        for (int n = 1; n <= bounds.Count; n++)
        {
            var group = BestAdjacentGroup(bounds, n, primaryIndex);
            var rectangles = group.Select(i => bounds[i]).ToList();
            var ranks = group.Select(i => order.IndexOf(i) + 1);
            sb.AppendLine($"  « {n} écran{(n > 1 ? "s" : "")} » → rangs {string.Join(", ", ranks)}"
                        + $"   contigu : {(IsContiguous(rectangles) ? "oui" : "NON")}"
                        + $"   rectangle plein : {(FillsBoundingBox(rectangles) ? "oui" : "non")}");
        }
        return sb.ToString();
    }

    /// <summary>
    /// Dispositions de référence, dont celles que la machine de développement n'a pas
    /// physiquement. Servent au mode --maptest et à l'aperçu graphique --mapdemo.
    /// </summary>
    public static IReadOnlyList<(string Name, Rectangle[] Bounds, int Primary)> ReferenceLayouts { get; } =
    [
        ("3 écrans identiques en rangée",
            [new(-1920, 0, 1920, 1200), new(0, 0, 1920, 1200), new(1920, 0, 1920, 1200)], 1),

        ("5 écrans en rangée",
            [new(0, 0, 1920, 1080), new(1920, 0, 1920, 1080), new(3840, 0, 1920, 1080),
             new(5760, 0, 1920, 1080), new(7680, 0, 1920, 1080)], 0),

        ("2 écrans en haut, 1 écran en dessous",
            [new(0, 0, 1920, 1080), new(1920, 0, 1920, 1080), new(0, 1080, 1920, 1080)], 0),

        ("écran portrait à droite d'un paysage",
            [new(0, 0, 1920, 1080), new(1920, 0, 1080, 1920)], 0),

        ("tailles inégales alignées en bas",
            [new(0, 312, 1280, 768), new(1280, 0, 2560, 1080), new(3840, 312, 1280, 768)], 1),

        ("5 écrans sur 2 rangées, un portrait",
            [new(0, 0, 1920, 1080), new(1920, 0, 1920, 1080), new(3840, 0, 1080, 1920),
             new(0, 1080, 1920, 1080), new(1920, 1080, 1920, 1080)], 0),

        ("4 écrans en carré, un petit en bas à droite",
            [new(0, 0, 2560, 1440), new(2560, 0, 1920, 1080), new(0, 1440, 2560, 1440),
             new(2560, 1080, 1280, 1024)], 0),

        ("écran isolé, non adjacent",
            [new(0, 0, 1920, 1080), new(4000, 0, 1920, 1080)], 0),

        ("écran unique", [new(0, 0, 2560, 1440)], 0)
    ];

    public static string DescribeReferenceLayouts()
    {
        var sb = new System.Text.StringBuilder();
        foreach (var (name, bounds, primary) in ReferenceLayouts)
            sb.AppendLine(Describe(name, bounds, primary));
        return sb.ToString();
    }
}
