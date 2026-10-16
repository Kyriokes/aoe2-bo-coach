using System.Text.Json;
using System.Text.Json.Serialization;
using BoCoach.Vision.Capture;

namespace BoCoach.Vision.Digits;

/// <summary>
/// Cómo distinguir el texto del fondo en una región del HUD. Cada píxel recibe una "tinta" entre 0 y 1.
/// <list type="bullet">
/// <item><c>white</c>: números blancos (stock, reloj). Tinta según el canal más bajo (el blanco tiene los tres altos).</item>
/// <item><c>blue</c>: números azules (total de aldeanos). Tinta según cuánto domina el azul sobre el rojo.</item>
/// </list>
/// </summary>
public sealed record InkSpec
{
    public string Color { get; init; } = "white";

    /// <summary>Por debajo de esto, tinta 0.</summary>
    public int Low { get; init; } = 130;

    /// <summary>Desde esto, tinta 1.</summary>
    public int High { get; init; } = 200;

    public float Ink(byte r, byte g, byte b)
    {
        var value = Color switch
        {
            "blue" => Math.Min(b, b - r + 120),
            _ => Math.Min(r, Math.Min(g, b)),
        };
        return Math.Clamp((value - Low) / (float)(High - Low), 0, 1);
    }

    public float[] InkMap(CapturedImage image)
    {
        var map = new float[image.Width * image.Height];
        for (var i = 0; i < map.Length; i++)
            map[i] = Ink(image.Bgra[i * 4 + 2], image.Bgra[i * 4 + 1], image.Bgra[i * 4]);
        return map;
    }
}

/// <summary>Una forma de un carácter (el mismo dígito puede verse con más de una forma según el píxel donde cae).</summary>
public sealed record GlyphTemplate(char Char, float[] Cell);

/// <summary>
/// Una letra del HUD (por ejemplo, los números del reloj): cómo leer la tinta y las plantillas de cada carácter.
/// Cada carácter puede tener varias variantes; se comparan todas, porque promediarlas borronea la forma.
/// Se guarda en data/fonts/&lt;nombre&gt;.json y se arma con tools/ReplaySession a partir de una grabación.
/// </summary>
public sealed class GlyphFont
{
    /// <summary>Tamaño de la grilla normalizada de cada carácter (alto fijo, ancho con la proporción del carácter).</summary>
    public const int CellWidth = 12;
    public const int CellHeight = 14;

    public GlyphFont(string name, InkSpec ink, IReadOnlyList<GlyphTemplate> templates, double typicalWidthRatio)
    {
        Name = name;
        Ink = ink;
        Templates = templates;
        TypicalWidthRatio = typicalWidthRatio;
    }

    public string Name { get; }
    public InkSpec Ink { get; }

    /// <summary>Todas las variantes de todos los caracteres: grillas de CellWidth × CellHeight con la tinta.</summary>
    public IReadOnlyList<GlyphTemplate> Templates { get; }

    /// <summary>Caracteres que conoce la letra.</summary>
    public IEnumerable<char> Chars => Templates.Select(t => t.Char).Distinct().Order();

    /// <summary>Ancho típico de un dígito dividido el alto: sirve para partir dígitos pegados.</summary>
    public double TypicalWidthRatio { get; }

    public static GlyphFont Load(string path)
    {
        var dto = JsonSerializer.Deserialize<Dto>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidDataException($"{path} está vacío.");
        var templates = new List<GlyphTemplate>();
        foreach (var (key, variants) in dto.Templates)
        {
            foreach (var rows in variants)
            {
                if (key.Length != 1 || rows.Length != CellHeight || rows.Any(r => r.Length != CellWidth))
                    throw new InvalidDataException($"{Path.GetFileName(path)}: la plantilla '{key}' no tiene {CellHeight} filas de {CellWidth} caracteres.");
                templates.Add(new GlyphTemplate(key[0], rows.SelectMany(r => r.Select(c => (c - '0') / 9f)).ToArray()));
            }
        }
        return new GlyphFont(dto.Name, dto.Ink, templates, dto.TypicalWidthRatio);
    }

    /// <summary>Guarda las plantillas como filas de dígitos 0 a 9 (intensidad de la tinta), legibles en un diff.</summary>
    public void Save(string path)
    {
        var dto = new Dto(
            Name,
            Ink,
            Math.Round(TypicalWidthRatio, 3),
            Templates.GroupBy(t => t.Char).OrderBy(g => g.Key).ToDictionary(
                g => g.Key.ToString(),
                g => g.Select(t => Enumerable.Range(0, CellHeight)
                        .Select(y => new string(Enumerable.Range(0, CellWidth)
                            .Select(x => (char)('0' + (int)Math.Round(t.Cell[y * CellWidth + x] * 9)))
                            .ToArray()))
                        .ToArray())
                    .ToArray()));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(dto, JsonOptions) + "\n");
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private sealed record Dto(string Name, InkSpec Ink, double TypicalWidthRatio, Dictionary<string, string[][]> Templates);
}
