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
    /// <param name="skip">Las celdas que tampoco se comparan: las del color del jugador (<see cref="QueueIconCatalog.PlayerCells"/>).</param>
    public static float Distance(float[] a, float[] b, bool[]? skip = null)
    {
        float sum = 0;
        var count = 0;
        for (var gy = 0; gy < Grid; gy++)
        {
            for (var gx = 0; gx < Grid; gx++)
            {
                if (Ignored(gx, gy) || (skip is not null && skip[gy * Grid + gx]))
                    continue;
                var i = (gy * Grid + gx) * 3;
                sum += Math.Abs(a[i] - b[i]) + Math.Abs(a[i + 1] - b[i + 1]) + Math.Abs(a[i + 2] - b[i + 2]);
                count += 3;
            }
        }
        return sum / Math.Max(count, 1);
    }

    /// <summary>
    /// Sin población, el juego tiñe la unidad de rojo oscuro: casi nada de verde ni de azul, sea cual sea la unidad y el
    /// color del jugador (por eso no se reconoce con los tintes de <see cref="QueueIconCatalog"/>).
    /// </summary>
    public static bool LooksHoused(float[] features)
    {
        float red = 0, others = 0;
        for (var gy = 0; gy < Grid; gy++)
        {
            for (var gx = 0; gx < Grid; gx++)
            {
                if (Ignored(gx, gy))
                    continue;
                var i = (gy * Grid + gx) * 3;
                red += features[i];
                others += features[i + 1] + features[i + 2];
            }
        }
        return red >= 69 * HousedMinRed && others <= red * HousedMaxOthers;
    }

    /// <summary>
    /// Si en (x, y) hay el marco de un ícono: líneas sin color abajo y a los costados, porque en el mapa tienen color (pasto:
    /// saturación 0,15). Sin esto, con los 543 íconos de <see cref="QueueIconCatalog"/> el pasto oscuro salía "galleon"
    /// (20261018-132830, captura 690). El mapa sin explorar también pasa. Las muestras reales no lo necesitan: la biblioteca
    /// tiene pedazos de mapa para compararlos.
    /// </summary>
    public static bool HasIconFrame(CapturedImage image, int x, int y)
    {
        if (x < 0 || y < 0 || x + IconSize > image.Width || y + IconSize > image.Height)
            return false;
        if (HasFrameLines(image, x, y))
            return true;
        var bottom = Math.Min(LineSaturation(image, x + 6, y + 31, 1, 0, 21), LineSaturation(image, x + 6, y + 32, 1, 0, 21));
        var side = Math.Min(LineSaturation(image, x, y + 6, 0, 1, 21), LineSaturation(image, x + 31, y + 6, 0, 1, 21));
        return bottom <= FrameMaxSaturation && side <= FrameMaxSaturation;
    }

    /// <summary>
    /// Si hay un ícono seguro, aunque se parezca a un pedazo de mapa (uno parejo en espera se parece a la arena). Abajo de
    /// cada ícono el juego dibuja dos líneas que ni los tintes ni la unidad cambian, (48, 44, 37) y (29, 29, 29), mientras
    /// que los costados los tiñe el amarillo (20261103-193834). Si no están (con el mouse encima, sobre el fondo de la cola
    /// del mod completo), un marco sin color y con algo de luz: el mapa sin explorar es negro.
    /// </summary>
    public static bool HasIcon(CapturedImage image, int x, int y)
    {
        if (x < 0 || y < 0 || x + IconSize > image.Width || y + IconSize > image.Height)
            return false;
        return HasFrameLines(image, x, y) || (HasIconFrame(image, x, y) && HasLight(Features(image, x, y)));
    }

    private static bool HasFrameLines(CapturedImage image, int x, int y) =>
        LineNear(image, x + 6, y + 31, 48, 44, 37) && LineNear(image, x + 6, y + 32, 29, 29, 29);

    private static bool HasLight(float[] features)
    {
        float light = 0;
        var count = 0;
        for (var gy = 0; gy < Grid; gy++)
        {
            for (var gx = 0; gx < Grid; gx++)
            {
                if (Ignored(gx, gy))
                    continue;
                var i = (gy * Grid + gx) * 3;
                light += 0.3f * features[i] + 0.59f * features[i + 1] + 0.11f * features[i + 2];
                count++;
            }
        }
        return light / count >= MinIconLight;
    }

    private const float MinIconLight = 0.2f;

    private static float LineSaturation(CapturedImage image, int x, int y, int dx, int dy, int length)
    {
        var sum = 0;
        for (var i = 0; i < length; i++)
        {
            var (r, g, b) = image.Pixel(x + i * dx, y + i * dy);
            sum += Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b));
        }
        return sum / (length * 255f);
    }

    private const float FrameMaxSaturation = 0.08f;

    private static bool LineNear(CapturedImage image, int x, int y, int red, int green, int blue)
    {
        const int length = 21;
        int r = 0, g = 0, b = 0;
        for (var i = 0; i < length; i++)
        {
            var (pr, pg, pb) = image.Pixel(x + i, y);
            (r, g, b) = (r + pr, g + pg, b + pb);
        }
        return Math.Abs(r / length - red) <= FrameTolerance && Math.Abs(g / length - green) <= FrameTolerance && Math.Abs(b / length - blue) <= FrameTolerance;
    }

    /// <summary>Lo que se puede alejar cada canal: el mapa oscuro anda cerca.</summary>
    private const int FrameTolerance = 6;

    private const float HousedMinRed = 0.12f;
    private const float HousedMaxOthers = 0.3f;

    /// <summary>
    /// El ícono que empieza en (x, y) espera detrás de otra cosa del mismo edificio: el juego lo tiñe de amarillo o
    /// naranja, así que en promedio (sin la esquina del número) tiene mucho rojo y casi nada de azul. En la
    /// 20261018-132830 los que esperan dan rojo 148 a 171 y azul 3 a 27; los que avanzan, rojo hasta 109 (el telar) o
    /// azul de más de un tercio del rojo (la pluma naranja del emplumado: 83, 60, 27).
    /// </summary>
    /// <remarks>
    /// El tinte nunca baja de rojo 127, así que con 130 se escapaban los íconos oscuros (20261103-193834, captura 1908: una
    /// tecnología en espera se leía "telar" avanzando). Con 120 hay que mirar el verde: los que avanzan tienen tanto como rojo.
    /// </remarks>
    /// <param name="inset">Píxeles de más que se dejan contra los bordes. En una imagen reescalada el ícono puede medir
    /// 32,5 px y la ventana de siempre cae sobre su borde blanco: el telar en espera daba azul 0,21 del rojo.</param>
    public static bool IsWaiting(CapturedImage image, int x, int y, int inset = 0)
    {
        var (red, green, blue) = MeanColor(image, x, y, inset);
        return red > 120 && blue < red * 0.26 && red > green * 1.15;
    }

    /// <summary>El color promedio del ícono que empieza en (x, y), sin la esquina del número.</summary>
    public static (double R, double G, double B) MeanColor(CapturedImage image, int x, int y, int inset = 0)
    {
        long red = 0, green = 0, blue = 0;
        var total = 0;
        for (var py = y + 4 + inset; py < y + IconSize - 1 - inset; py++)
        {
            for (var px = x + 12 + inset; px < x + IconSize - 2 - inset; px++)
            {
                var (r, g, b) = image.Pixel(px, py);
                (red, green, blue) = (red + r, green + g, blue + b);
                total++;
            }
        }
        return (red / (double)total, green / (double)total, blue / (double)total);
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
public sealed class IconLibrary(IReadOnlyList<QueueIcon> icons, QueueIconCatalog? catalog = null)
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

    /// <summary>Los íconos de los archivos del juego (data/icons/queue_game.json), si están.</summary>
    public QueueIconCatalog? Catalog { get; } = catalog;

    private readonly WaitingShapes? _waiting = catalog is null ? null : new WaitingShapes(catalog, icons);

    /// <summary>
    /// Las muestras son de partidas en azul: en otro color, sus celdas del color del jugador harían más distinta a la
    /// unidad (llevadas a amarillo o naranja, 3 de 5 muestras del arquero a caballo pasaban el umbral). Se toma lo mejor
    /// de las dos: sin esas celdas, o con ellas si el jugador también es azul (si no, en azul a veces daba peor: con el
    /// color del jugador afuera, lo que queda de un explorador a medio teñir pasaba el umbral).
    /// </summary>
    private readonly bool[]?[] _playerCells = [.. icons.Select(icon => catalog?.PlayerCells(icon.Key))];

    /// <param name="framed">El lugar tiene el marco de un ícono (<see cref="ProductionQueue.HasIconFrame"/>): recién ahí se mira lo que nunca se vio en una partida.</param>
    public (string Key, float Distance)? Match(float[] features, bool iconsOnly = false, bool framed = false)
    {
        (string Key, float Distance)? best = null;
        for (var i = 0; i < Icons.Count; i++)
        {
            var icon = Icons[i];
            if (iconsOnly && icon.Key == Nothing)
                continue;
            var d = ProductionQueue.Distance(features, icon.Features);
            if (_playerCells[i] is { } cells)
                d = Math.Min(d, ProductionQueue.Distance(features, icon.Features, cells));
            if (best is null || d < best.Value.Distance)
                best = (icon.Key, d);
        }
        // Lo que nunca se vio en una partida, por los archivos del juego. Nada de eso le gana a una muestra real igual de parecida.
        if (framed && Catalog?.Best(features, best?.Distance ?? float.MaxValue) is { } game)
            best = game;
        // En rojo el juego no deja ver de qué unidad se trata, pero el color alcanza.
        return best is { Distance: <= MatchThreshold } || !ProductionQueue.LooksHoused(features) ? best : (Housed, 0f);
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
            var waiting = ProductionQueue.IsWaiting(image, x, y);
            var framed = ProductionQueue.HasIconFrame(image, x, y);
            var features = ProductionQueue.Features(image, x, y);
            if (Match(features, framed: framed) is not { } m)
                break;
            if (m.Key == Nothing && m.Distance <= MatchThreshold && !(Catalog is not null && ProductionQueue.HasIcon(image, x, y)))
                break;
            if (m.Distance > MatchThreshold && !(framed && Catalog is not null))
                break;
            // Con el marco hay un ícono aunque no se sepa cuál: lo que viene después se sigue leyendo (20261101-165020,
            // captura 1528: un Pozo de oro a medio teñir escondía los arqueros a caballo que venían atrás).
            slots.Add(new QueueSlot(x, y, KeyOf(features, m, waiting), waiting));
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
            var waiting = ProductionQueue.IsWaiting(image, m.X, start.Y, inset: 1);
            var features = ProductionQueue.Features(image, m.X, start.Y);
            slots.Add(new QueueSlot(m.X, start.Y, KeyOf(features, (m.Key, m.Distance), waiting), waiting));
            expected = m.X + ProductionQueue.IconSize;
        }
        return slots;
    }

    /// <summary>
    /// Qué es un lugar que ya se sabe que está en la cola. En espera, por la forma (<see cref="WaitingShapes"/>): el
    /// amarillo les borra el color y por colores casi cualquiera se parecía a las muestras de Castillos o del ballestero.
    /// </summary>
    private string KeyOf(float[] features, (string Key, float Distance) match, bool waiting)
    {
        if (_waiting is not null && waiting && match.Key != Housed)
            return _waiting.Identify(features, match.Distance <= MatchThreshold ? match.Key : null) ?? Unknown;
        if (match.Key == Nothing)
            match = Match(features, iconsOnly: true, framed: true) is { } icon ? icon : match;
        return match.Distance <= MatchThreshold ? match.Key : Unknown;
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
            if (Match(ProductionQueue.Features(image, sx, y), iconsOnly, ProductionQueue.HasIconFrame(image, sx, y)) is not { } m)
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
            if (image.Width >= ProductionQueue.IconSize && Match(ProductionQueue.Features(image, 0, dy), iconsOnly: true, framed: ProductionQueue.HasIconFrame(image, 0, dy)) is { } m && m.Distance < bestDistance)
                (bestOffset, bestDistance) = (dy, m.Distance);
        }
        return bestOffset >= 0 && bestDistance <= MatchThreshold ? bestOffset : FramedRow(image);
    }

    /// <summary>
    /// La fila de un primer ícono que no se reconoce, por su marco: sin esto la cola entera salía vacía (20261103-193834,
    /// captura 4074: ocho íconos, el primero a medio teñir).
    /// </summary>
    private int? FramedRow(CapturedImage image)
    {
        if (Catalog is null)
            return null;
        for (var dy = 0; dy <= ProductionQueue.MaxOffset && dy + ProductionQueue.IconSize <= image.Height; dy++)
        {
            if (image.Width >= ProductionQueue.IconSize && ProductionQueue.HasIcon(image, 0, dy))
                return dy;
        }
        return null;
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
            return (new IconLibrary(icons, Catalog), $"el ícono {nearest.i} (\"?\", a {nearest.d:0.000}) pasa a \"{key}\"");
        }
        icons.Add(new QueueIcon(key, features));
        var was = icons.Count > 1 ? $" (el más parecido era \"{icons[nearest.i].Key}\", a {nearest.d:0.000})" : "";
        return (new IconLibrary(icons, Catalog), $"ícono nuevo \"{key}\"{was}");
    }

    /// <param name="withGame">Sumar los íconos de los archivos del juego, que van en el mismo lugar (queue_game.json).</param>
    public static IconLibrary Load(string path, bool withGame = true) =>
        new(JsonSerializer.Deserialize<List<QueueIcon>>(File.ReadAllText(path)) ?? [],
            withGame ? QueueIconCatalog.TryLoad(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, "queue_game.json")) : null);

    public static IconLibrary? TryLoad(string path) => File.Exists(path) ? Load(path) : null;

    public void Save(string path) =>
        File.WriteAllText(path, JsonSerializer.Serialize(Icons) + "\n");
}
