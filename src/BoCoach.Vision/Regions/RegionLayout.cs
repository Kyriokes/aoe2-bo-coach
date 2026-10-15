using System.Text.Json;
using BoCoach.Vision.Capture;

namespace BoCoach.Vision.Regions;

/// <summary>Las regiones del HUD que se leen.</summary>
public static class RegionNames
{
    public const string Clock = "clock";
    public const string Food = "food";
    public const string Wood = "wood";
    public const string Gold = "gold";
    public const string Stone = "stone";
    public const string Population = "population";
    public const string EventLog = "event_log";
    public const string IdleVillagers = "idle_villagers";
    public const string Age = "age";

    /// <summary>
    /// Cada caja de recurso toma el ícono, la cantidad de aldeanos que el HUD muestra debajo y el stock:
    /// los dos números se separan por su posición dentro de la caja. Población igual: aldeanos y pop/máximo.
    /// </summary>
    public static IReadOnlyList<string> All { get; } =
        [Wood, Food, Gold, Stone, Population, IdleVillagers, Age, Clock, EventLog];

    public static string Label(string name) => name switch
    {
        Clock => "Reloj",
        Food => "Comida",
        Wood => "Madera",
        Gold => "Oro",
        Stone => "Piedra",
        Population => "Población",
        EventLog => "Log de eventos",
        IdleVillagers => "Inactivos",
        Age => "Edad",
        _ => name,
    };
}

/// <summary>Rectángulo como fracción de la pantalla (x, y, ancho, alto entre 0 y 1).</summary>
public readonly record struct RegionRect(double X, double Y, double Width, double Height)
{
    public PixelRect ToPixels(int screenWidth, int screenHeight)
    {
        var x = (int)Math.Round(X * screenWidth);
        var y = (int)Math.Round(Y * screenHeight);
        var right = (int)Math.Round((X + Width) * screenWidth);
        var bottom = (int)Math.Round((Y + Height) * screenHeight);
        x = Math.Clamp(x, 0, screenWidth - 1);
        y = Math.Clamp(y, 0, screenHeight - 1);
        return new PixelRect(x, y, Math.Clamp(right, x + 1, screenWidth) - x, Math.Clamp(bottom, y + 1, screenHeight) - y);
    }

    /// <summary>Rectángulo absoluto en pantalla, relativo a la zona donde está el juego.</summary>
    public PixelRect ToPixels(PixelRect area)
    {
        var local = ToPixels(area.Width, area.Height);
        return local with { X = local.X + area.X, Y = local.Y + area.Y };
    }

    /// <summary>Lo mismo dentro de la pantalla: sin salirse de los bordes y con un tamaño mínimo.</summary>
    public RegionRect Clamped(double minSize = 0.005)
    {
        var w = Math.Clamp(Width, minSize, 1);
        var h = Math.Clamp(Height, minSize, 1);
        return new RegionRect(Math.Clamp(X, 0, 1 - w), Math.Clamp(Y, 0, 1 - h), w, h);
    }
}

/// <summary>
/// regions.json: dónde está cada región del HUD, en coordenadas relativas para soportar cambios de resolución.
/// Lo genera la calibración y se guarda en la carpeta del usuario.
/// </summary>
public sealed class RegionLayout
{
    public RegionLayout(int referenceWidth, int referenceHeight, IReadOnlyDictionary<string, RegionRect> regions)
    {
        ReferenceWidth = referenceWidth;
        ReferenceHeight = referenceHeight;
        Regions = regions;
    }

    /// <summary>Resolución con la que se calibró (solo informativa: las regiones son relativas).</summary>
    public int ReferenceWidth { get; }
    public int ReferenceHeight { get; }

    public IReadOnlyDictionary<string, RegionRect> Regions { get; }

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BoCoach", "regions.json");

    /// <summary>
    /// Punto de partida: el HUD por defecto de AoE2:DE a 1920×1080, medido sobre una partida real.
    /// La calibración lo ajusta.
    /// </summary>
    public static RegionLayout Default { get; } = new(1920, 1080, new Dictionary<string, RegionRect>
    {
        [RegionNames.Wood] = new(0.0031, 0.0046, 0.0380, 0.0370),
        [RegionNames.Food] = new(0.0495, 0.0046, 0.0370, 0.0370),
        [RegionNames.Gold] = new(0.0932, 0.0046, 0.0370, 0.0370),
        [RegionNames.Stone] = new(0.1396, 0.0046, 0.0365, 0.0370),
        [RegionNames.Population] = new(0.1839, 0.0046, 0.0453, 0.0370),
        [RegionNames.IdleVillagers] = new(0.2370, 0.0037, 0.0208, 0.0389),
        [RegionNames.Age] = new(0.3070, 0.0080, 0.0580, 0.0220),
        [RegionNames.Clock] = new(0.3839, 0.0037, 0.0313, 0.0167),
        [RegionNames.EventLog] = new(0.0047, 0.1361, 0.2510, 0.0907),
    });

    public PixelRect PixelsOf(string region, int screenWidth, int screenHeight) =>
        Regions[region].ToPixels(screenWidth, screenHeight);

    /// <summary>Rectángulo absoluto de una región dentro de la zona del juego.</summary>
    public PixelRect PixelsOf(string region, PixelRect area) => Regions[region].ToPixels(area);

    public RegionLayout With(string region, RegionRect rect)
    {
        var regions = new Dictionary<string, RegionRect>(Regions) { [region] = rect.Clamped() };
        return new RegionLayout(ReferenceWidth, ReferenceHeight, regions);
    }

    public RegionLayout WithReference(int width, int height) => new(width, height, Regions);

    /// <summary>Carga regions.json; si no existe, devuelve <see cref="Default"/>. Las regiones que falten salen del default.</summary>
    public static RegionLayout LoadOrDefault(string path)
    {
        if (!File.Exists(path))
            return Default;

        Dto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<Dto>(File.ReadAllText(path), JsonOptions);
        }
        catch (JsonException e)
        {
            throw new InvalidDataException($"{Path.GetFileName(path)} está mal formado: {e.Message}", e);
        }

        var regions = new Dictionary<string, RegionRect>(Default.Regions);
        foreach (var (name, values) in dto?.Regions ?? [])
        {
            if (values is not { Length: 4 })
                throw new InvalidDataException($"{Path.GetFileName(path)}: la región '{name}' tiene que tener 4 números (x, y, ancho, alto).");
            regions[name] = new RegionRect(values[0], values[1], values[2], values[3]).Clamped();
        }
        var reference = dto?.ReferenceResolution is { Length: 2 } r ? r : [Default.ReferenceWidth, Default.ReferenceHeight];
        return new RegionLayout(reference[0], reference[1], regions);
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var dto = new Dto(
            [ReferenceWidth, ReferenceHeight],
            Regions.ToDictionary(r => r.Key, r => new[] { Round(r.Value.X), Round(r.Value.Y), Round(r.Value.Width), Round(r.Value.Height) }));
        File.WriteAllText(path, JsonSerializer.Serialize(dto, JsonOptions) + "\n");
    }

    private static double Round(double v) => Math.Round(v, 5);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private sealed record Dto(int[]? ReferenceResolution, Dictionary<string, double[]>? Regions);
}
