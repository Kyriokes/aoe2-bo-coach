using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BoCoach.Vision.Hud;

/// <summary>Un ícono de los archivos del juego: su resumen sin teñir y qué celdas son del color del jugador.</summary>
public sealed record GameQueueIcon(
    [property: JsonPropertyName("key")] string Key,
    [property: JsonPropertyName("unit")] bool Unit,
    [property: JsonPropertyName("common")] bool Common,
    [property: JsonPropertyName("skip")] int[] Skip,
    [property: JsonPropertyName("features")] float[] Features);

/// <summary>
/// Los íconos de los archivos del juego (data/icons/queue_game.json, de tools/extractor/queue_icons.py), para reconocer en
/// la cola lo que nunca se vio en una partida. Comparados con las muestras reales de la biblioteca, 60 de 65 dan su clave
/// entre los 543 íconos (2026-11-03). Al cargar se arma cada uno como lo dibuja el juego: tal cual, con el verde que
/// barre de izquierda a derecha según avanza, y en amarillo si espera. No se comparan las celdas del color del jugador,
/// que cambia de una partida a otra: la biblioteca real solo tiene muestras en azul.
/// </summary>
public sealed class QueueIconCatalog
{
    /// <summary>
    /// Lo que suma una clave que ninguna BO pide (una unidad única de otra civ): a igual parecido, gana la de siempre. Sin
    /// esto, el arquero a caballo salía "elite mangudai".
    /// </summary>
    public const float UncommonCost = 0.02f;

    /// <summary>Las partes del ancho que puede tener el verde (con el resto del ícono sin teñir).</summary>
    private static readonly float[] GreenSteps = [0.25f, 0.5f, 0.75f, 1f];

    // Tintes ajustados contra las muestras reales: canal = base + factor × brillo de la celda.
    private static readonly (float R, float G, float B) GreenBase = (0.031f, 0.244f, 0.032f);
    private static readonly (float R, float G, float B) GreenFactor = (0.542f, 0.459f, 0.350f);
    private static readonly (float R, float G, float B) YellowBase = (0.500f, 0.324f, 0f);
    private static readonly (float R, float G, float B) YellowFactor = (0.529f, 0.432f, 0.258f);

    private sealed record Shape(string Key, float Cost, float[] Weights, float Compared, float[][] Variants);

    private readonly Shape[] _shapes;

    private readonly Dictionary<string, bool[]> _playerCells = [];

    private static readonly int[] ComparedCells = [.. Enumerable.Range(0, ProductionQueue.Grid * ProductionQueue.Grid)
        .Where(cell => !ProductionQueue.Ignored(cell % ProductionQueue.Grid, cell / ProductionQueue.Grid))];

    private static int PackedLength => ComparedCells.Length * 3;

    public QueueIconCatalog(IEnumerable<GameQueueIcon> icons)
    {
        var list = icons.ToList();
        _shapes = [.. list.Select(Build)];
        // Una clave puede ser unidad y tecnología (el ballestero): vale la de unidad, que es la que trae color del jugador.
        foreach (var icon in list.Where(i => i.Skip.Length > 0))
        {
            var cells = new bool[ProductionQueue.Grid * ProductionQueue.Grid];
            foreach (var cell in icon.Skip)
                cells[cell] = true;
            _playerCells[icon.Key] = cells;
        }
    }

    public IEnumerable<string> Keys => _shapes.Select(s => s.Key);

    /// <summary>Las celdas del color del jugador de esa clave (índice = fila × 11 + columna), o null si no tiene.</summary>
    public bool[]? PlayerCells(string key) => _playerCells.GetValueOrDefault(key);

    public static QueueIconCatalog? TryLoad(string path) =>
        File.Exists(path) ? new QueueIconCatalog(JsonSerializer.Deserialize<List<GameQueueIcon>>(File.ReadAllText(path)) ?? []) : null;

    private static Shape Build(GameQueueIcon icon)
    {
        var weights = new float[PackedLength];
        var compared = 0;
        for (var i = 0; i < ComparedCells.Length; i++)
        {
            if (icon.Skip.Contains(ComparedCells[i]))
                continue;
            weights[i * 3] = weights[i * 3 + 1] = weights[i * 3 + 2] = 1;
            compared += 3;
        }
        var variants = new List<float[]> { Render(icon.Features, _ => Tint.None) };
        variants.AddRange(GreenSteps.Select(share => Render(icon.Features, gx => (gx + 0.5f) / ProductionQueue.Grid <= share ? Tint.Green : Tint.None)));
        variants.Add(Render(icon.Features, _ => Tint.Yellow));
        return new Shape(icon.Key, icon.Common ? 0 : UncommonCost, weights, Math.Max(compared, 1), [.. variants]);
    }

    private enum Tint
    {
        None,
        Green,
        Yellow,
    }

    private static float[] Render(float[] features, Func<int, Tint> tintAt)
    {
        var packed = new float[PackedLength];
        for (var i = 0; i < ComparedCells.Length; i++)
        {
            var cell = ComparedCells[i];
            var (r, g, b) = (features[cell * 3], features[cell * 3 + 1], features[cell * 3 + 2]);
            var tint = tintAt(cell % ProductionQueue.Grid);
            if (tint != Tint.None)
            {
                var light = 0.3f * r + 0.59f * g + 0.11f * b;
                var (start, factor) = tint == Tint.Green ? (GreenBase, GreenFactor) : (YellowBase, YellowFactor);
                (r, g, b) = (Math.Min(1, start.R + factor.R * light), Math.Min(1, start.G + factor.G * light), Math.Min(1, start.B + factor.B * light));
            }
            (packed[i * 3], packed[i * 3 + 1], packed[i * 3 + 2]) = (r, g, b);
        }
        return packed;
    }

    /// <summary>La clave más parecida con una diferencia menor a <paramref name="below"/>, o null. Es lo más caro de leer la cola: abandona cada variante apenas se pasa.</summary>
    public (string Key, float Distance)? Best(float[] features, float below)
    {
        var observed = new float[PackedLength];
        for (var i = 0; i < ComparedCells.Length; i++)
        {
            var cell = ComparedCells[i];
            (observed[i * 3], observed[i * 3 + 1], observed[i * 3 + 2]) = (features[cell * 3], features[cell * 3 + 1], features[cell * 3 + 2]);
        }
        (string Key, float Distance)? best = null;
        foreach (var shape in _shapes)
        {
            foreach (var variant in shape.Variants)
            {
                var limit = (below - shape.Cost) * shape.Compared;
                if (limit > 0 && Sum(observed, variant, shape.Weights, limit) is { } sum)
                {
                    below = sum / shape.Compared + shape.Cost;
                    best = (shape.Key, below);
                }
            }
        }
        return best;
    }

    /// <summary>La suma de las diferencias, o null si pasa de <paramref name="limit"/>.</summary>
    private static float? Sum(float[] a, float[] b, float[] weights, float limit)
    {
        var width = Vector<float>.Count;
        var total = 0f;
        var i = 0;
        for (; i <= a.Length - width; i += width)
        {
            var difference = Vector.Abs(new Vector<float>(a, i) - new Vector<float>(b, i)) * new Vector<float>(weights, i);
            total += Vector.Sum(difference);
            if (total >= limit)
                return null;
        }
        for (; i < a.Length; i++)
            total += Math.Abs(a[i] - b[i]) * weights[i];
        return total < limit ? total : null;
    }
}
