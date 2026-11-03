using System.Text.Json;
using System.Text.Json.Serialization;
using BoCoach.Core.Engine;
using BoCoach.Vision.Capture;

namespace BoCoach.Vision.Hud;

/// <summary>
/// La cola de producción del juego (arriba a la izquierda, debajo del panel): un ícono por cosa que se está haciendo
/// (aldeanos, unidades, tecnologías), de 33×33 px a 1920×1080 y uno al lado del otro, con la cantidad en cola arriba a
/// la izquierda. Detrás puede haber negro o el mapa, así que no se buscan huecos: cada lugar se compara con los íconos
/// conocidos (<see cref="IconLibrary"/>) y la cola termina en el primero que no se parece a ninguno.
/// </summary>
public static class ProductionQueue
{
    /// <summary>Lado del ícono y distancia entre íconos, en píxeles, a 1920×1080.</summary>
    public const int IconSize = 33;

    /// <summary>El ícono se resume en una grilla de Grid×Grid colores promedio (bloques de 3×3 píxeles).</summary>
    public const int Grid = 11;

    /// <summary>Lugares que se miran como máximo (la caja calibrada puede ser más ancha).</summary>
    public const int MaxSlots = 12;

    /// <summary>Corrimiento vertical que se prueba para encontrar la fila de íconos dentro de la caja calibrada.</summary>
    public const int MaxOffset = 12;

    /// <summary>
    /// Corrimiento horizontal que se prueba para el primer ícono en una imagen reescalada (0 a esto, a la derecha): el
    /// redondeo de la caja lo corre 1 o 2 px (en la captura del amigo, a 1,09, cae en x 1).
    /// </summary>
    public const int MaxStartDrift = 2;

    /// <summary>
    /// Cuánto se busca cada ícono alrededor de donde se lo espera (el anterior + <see cref="IconSize"/>) en una imagen
    /// reescalada. La escala medida no es exacta (con las franjas del mod viejo, ±1,4 %): con 1,5 % de error los íconos
    /// quedan de 32,5 o 33,5 px, y con 1 px de corrimiento la distancia sube unos 0,05 (el umbral es 0,09).
    /// </summary>
    public const int MaxDrift = 2;

    /// <summary>Lo que cuesta cada píxel de corrimiento al elegir dónde cae un ícono: a igual parecido, el lugar esperado.</summary>
    public const float DriftCost = 0.005f;

    /// <summary>El resumen del ícono que empieza en (x, y): Grid×Grid colores (R, G, B entre 0 y 1).</summary>
    public static float[] Features(CapturedImage image, int x, int y)
    {
        var block = IconSize / Grid;
        var features = new float[Grid * Grid * 3];
        for (var gy = 0; gy < Grid; gy++)
        {
            for (var gx = 0; gx < Grid; gx++)
            {
                int r = 0, g = 0, b = 0;
                for (var dy = 0; dy < block; dy++)
                {
                    for (var dx = 0; dx < block; dx++)
                    {
                        var (pr, pg, pb) = image.Pixel(x + gx * block + dx, y + gy * block + dy);
                        (r, g, b) = (r + pr, g + pg, b + pb);
                    }
                }
                var n = block * block * 255f;
                var i = (gy * Grid + gx) * 3;
                (features[i], features[i + 1], features[i + 2]) = (r / n, g / n, b / n);
            }
        }
        return features;
    }

    /// <summary>
    /// Celdas que no se comparan: arriba a la izquierda va la cantidad en cola (un número blanco que cambia), y el
    /// borde, que se ilumina si el mouse pasa por encima.
    /// </summary>
    public static bool Ignored(int gx, int gy) => (gx < 4 && gy < 5) || gx == 0 || gy == 0 || gx == Grid - 1 || gy == Grid - 1;

    /// <summary>Diferencia promedio entre dos resúmenes (0 = iguales, 1 = opuestos), sin las celdas ignoradas.</summary>
    public static float Distance(float[] a, float[] b)
    {
        float sum = 0;
        var count = 0;
        for (var gy = 0; gy < Grid; gy++)
        {
            for (var gx = 0; gx < Grid; gx++)
            {
                if (Ignored(gx, gy))
                    continue;
                var i = (gy * Grid + gx) * 3;
                sum += Math.Abs(a[i] - b[i]) + Math.Abs(a[i + 1] - b[i + 1]) + Math.Abs(a[i + 2] - b[i + 2]);
                count += 3;
            }
        }
        return sum / count;
    }

    /// <summary>
    /// El ícono que empieza en (x, y) espera detrás de otra cosa del mismo edificio: el juego lo tiñe de amarillo o
    /// naranja, así que en promedio (sin la esquina del número) tiene mucho rojo y casi nada de azul. En la
    /// 20261018-132830 los que esperan dan rojo 148 a 171 y azul 3 a 27; los que avanzan, rojo hasta 109 (el telar) o
    /// azul de más de un tercio del rojo (la pluma naranja del emplumado: 83, 60, 27).
    /// </summary>
    /// <param name="inset">Píxeles de más que se dejan contra los bordes. En una imagen reescalada el ícono puede medir
    /// 32,5 px y la ventana de siempre cae sobre su borde blanco: el telar en espera daba azul 0,21 del rojo.</param>
    public static bool IsWaiting(CapturedImage image, int x, int y, int inset = 0)
    {
        long red = 0, blue = 0;
        var total = 0;
        for (var py = y + 4 + inset; py < y + IconSize - 1 - inset; py++)
        {
            for (var px = x + 12 + inset; px < x + IconSize - 2 - inset; px++)
            {
                var (r, _, b) = image.Pixel(px, py);
                (red, blue) = (red + r, blue + b);
                total++;
            }
        }
        return red > 130L * total && blue < red * 0.2;
    }

    /// <summary>Lugares de la cola que entran en la caja, empezando en (offsetX, offsetY).</summary>
    public static IEnumerable<(int X, int Y)> Slots(CapturedImage image, int offsetX = 0, int offsetY = 0)
    {
        for (var k = 0; k < MaxSlots; k++)
        {
            var x = offsetX + k * IconSize;
            if (x + IconSize > image.Width || offsetY + IconSize > image.Height)
                yield break;
            yield return (x, offsetY);
        }
    }
}

/// <summary>Un lugar de la cola leído: dónde empieza el ícono en la caja (a 1080p), qué es y si espera.</summary>
public sealed record QueueSlot(int X, int Y, string Key, bool Waiting)
{
    public QueueItem Item => new(Key, Waiting);
}

/// <summary>Un ícono conocido de la cola: la clave de game_data ("villager", "loom") y su resumen.</summary>
public sealed record QueueIcon(
    [property: JsonPropertyName("key")] string Key,
    [property: JsonPropertyName("features")] float[] Features);

/// <summary>Los íconos conocidos de la cola (data/icons/production_queue.json), armados con ReplaySession.</summary>
public sealed class IconLibrary(IReadOnlyList<QueueIcon> icons)
{
    /// <summary>Diferencia a partir de la cual un lugar no es ningún ícono conocido.</summary>
    public const float MatchThreshold = 0.09f;

    /// <summary>
    /// Clave de lo que no es un ícono (negro, pedazos de mapa): la biblioteca los tiene para no confundirlos con un
    /// ícono oscuro (el de "sin población", rojo oscuro, se parecía al negro). Donde aparece, termina la cola.
    /// </summary>
    public const string Nothing = "-";

    /// <summary>Clave de un ícono que se vio pero no se identificó (cuenta como algo en la cola).</summary>
    public const string Unknown = "?";

    /// <summary>Clave de los íconos en rojo: la unidad espera porque no hay población.</summary>
    public const string Housed = "housed";

    public IReadOnlyList<QueueIcon> Icons { get; } = icons;

    public (string Key, float Distance)? Match(float[] features, bool iconsOnly = false)
    {
        (string Key, float Distance)? best = null;
        foreach (var icon in Icons)
        {
            if (iconsOnly && icon.Key == Nothing)
                continue;
            var d = ProductionQueue.Distance(features, icon.Features);
            if (best is null || d < best.Value.Distance)
                best = (icon.Key, d);
        }
        return best;
    }

    /// <summary>
    /// Lo que hay en la cola, de izquierda a derecha (claves de game_data). Busca la fila de íconos probando
    /// corrimientos verticales con el primer lugar, y corta en el primer lugar que no se parece a ningún ícono.
    /// Vacía si no hay nada en producción.
    /// </summary>
    public IReadOnlyList<QueueItem> Read(CapturedImage image) => [.. Locate(image).Select(s => s.Item)];

    /// <summary>
    /// Lo mismo que <see cref="Read"/>, con dónde cae cada ícono. Sin reescalar, cada <see cref="ProductionQueue.IconSize"/>
    /// px justos. Reescalada (<see cref="CapturedImage.Rescaled"/>), la escala medida puede estar corrida, así que cada
    /// ícono se busca alrededor de donde cayó el anterior.
    /// </summary>
    public IReadOnlyList<QueueSlot> Locate(CapturedImage image) => image.Rescaled ? Following(image) : Exact(image);

    private List<QueueSlot> Exact(CapturedImage image)
    {
        var slots = new List<QueueSlot>();
        if (RowAtStart(image) is not { } row)
            return slots;
        foreach (var (x, y) in ProductionQueue.Slots(image, 0, row))
        {
            if (Match(ProductionQueue.Features(image, x, y)) is not { Distance: <= MatchThreshold } m || m.Key == Nothing)
                break;
            slots.Add(new QueueSlot(x, y, m.Key, ProductionQueue.IsWaiting(image, x, y)));
        }
        return slots;
    }

    private List<QueueSlot> Following(CapturedImage image)
    {
        var slots = new List<QueueSlot>();
        if (FollowingStart(image) is not { } start)
            return slots;
        var expected = start.X;
        while (slots.Count < ProductionQueue.MaxSlots)
        {
            if (Nearest(image, expected, start.Y, -ProductionQueue.MaxDrift, ProductionQueue.MaxDrift) is not { Distance: <= MatchThreshold } m || m.Key == Nothing)
                break;
            slots.Add(new QueueSlot(m.X, start.Y, m.Key, ProductionQueue.IsWaiting(image, m.X, start.Y, inset: 1)));
            expected = m.X + ProductionQueue.IconSize;
        }
        return slots;
    }

    /// <summary>
    /// El ícono más parecido entre x + fromDx y x + toDx (dentro de la imagen), con <see cref="ProductionQueue.DriftCost"/>
    /// por píxel de distancia a x. La distancia devuelta es la del ícono, sin el costo.
    /// </summary>
    private (int X, string Key, float Distance)? Nearest(CapturedImage image, int x, int y, int fromDx, int toDx, bool iconsOnly = false)
    {
        (int X, string Key, float Distance, float Cost)? best = null;
        for (var dx = fromDx; dx <= toDx; dx++)
        {
            var sx = x + dx;
            if (sx < 0 || sx + ProductionQueue.IconSize > image.Width)
                continue;
            if (Match(ProductionQueue.Features(image, sx, y), iconsOnly) is not { } m)
                continue;
            var cost = m.Distance + Math.Abs(dx) * ProductionQueue.DriftCost;
            if (best is null || cost < best.Value.Cost)
                best = (sx, m.Key, m.Distance, cost);
        }
        return best is { } b ? (b.X, b.Key, b.Distance) : null;
    }

    /// <summary>En una imagen reescalada, dónde empieza el primer ícono: la altura y el corrimiento que mejor parecen uno.</summary>
    private (int X, int Y)? FollowingStart(CapturedImage image)
    {
        (int X, int Y, float Cost)? best = null;
        for (var dy = 0; dy <= ProductionQueue.MaxOffset && dy + ProductionQueue.IconSize <= image.Height; dy++)
        {
            if (Nearest(image, 0, dy, 0, ProductionQueue.MaxStartDrift, iconsOnly: true) is not { Distance: <= MatchThreshold } m)
                continue;
            var cost = m.Distance + m.X * ProductionQueue.DriftCost;
            if (best is null || cost < best.Value.Cost)
                best = (m.X, dy, cost);
        }
        return best is { } b ? (b.X, b.Y) : null;
    }

    private int? RowAtStart(CapturedImage image)
    {
        var bestOffset = -1;
        var bestDistance = float.MaxValue;
        for (var dy = 0; dy <= ProductionQueue.MaxOffset && dy + ProductionQueue.IconSize <= image.Height; dy++)
        {
            if (image.Width >= ProductionQueue.IconSize && Match(ProductionQueue.Features(image, 0, dy), iconsOnly: true) is { } m && m.Distance < bestDistance)
                (bestOffset, bestDistance) = (dy, m.Distance);
        }
        return bestOffset < 0 || bestDistance > MatchThreshold ? null : bestOffset;
    }

    /// <summary>Altura de la fila de íconos dentro de la caja (la que mejor parece un ícono en el primer lugar), o null si no hay ninguno.</summary>
    public int? Row(CapturedImage image) => image.Rescaled ? FollowingStart(image)?.Y : RowAtStart(image);

    /// <summary>
    /// Corrige la biblioteca con un lugar visto: si se parece a un ícono sin identificar ("?"), le pone la clave; si
    /// no, lo agrega como ícono nuevo. Devuelve qué hizo.
    /// </summary>
    public (IconLibrary Library, string Change) Teach(float[] features, string key)
    {
        var icons = Icons.ToList();
        var nearest = icons.Select((icon, i) => (i, d: ProductionQueue.Distance(features, icon.Features))).MinBy(x => x.d);
        if (icons.Count > 0 && nearest.d <= MatchThreshold && icons[nearest.i].Key == Unknown)
        {
            icons[nearest.i] = icons[nearest.i] with { Key = key };
            return (new IconLibrary(icons), $"el ícono {nearest.i} (\"?\", a {nearest.d:0.000}) pasa a \"{key}\"");
        }
        icons.Add(new QueueIcon(key, features));
        var was = icons.Count > 1 ? $" (el más parecido era \"{icons[nearest.i].Key}\", a {nearest.d:0.000})" : "";
        return (new IconLibrary(icons), $"ícono nuevo \"{key}\"{was}");
    }

    public static IconLibrary Load(string path) =>
        new(JsonSerializer.Deserialize<List<QueueIcon>>(File.ReadAllText(path)) ?? []);

    public static IconLibrary? TryLoad(string path) => File.Exists(path) ? Load(path) : null;

    public void Save(string path) =>
        File.WriteAllText(path, JsonSerializer.Serialize(Icons) + "\n");
}
