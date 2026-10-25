using System.Text.Json;
using System.Text.Json.Serialization;
using BoCoach.Vision.Capture;
using BoCoach.Vision.Regions;

namespace BoCoach.Vision.Hud;

/// <summary>Color del diseño, con cada canal de 0 a 1 (como lo escribe el juego en sus paneles).</summary>
public sealed record HudColor(double R, double G, double B)
{
    /// <summary>Lo que captura la pantalla: cada canal redondeado a 0..255.</summary>
    public (int R, int G, int B) Captured => (Channel(R), Channel(G), Channel(B));

    private static int Channel(double value) => (int)Math.Round(Math.Clamp(value, 0, 1) * 255);
}

/// <summary>Rectángulo en unidades del panel del juego.</summary>
public sealed record UnitRect(int X, int Y, int Width, int Height);

public sealed record HudIcon(int X, int Y, int Size);

/// <summary>El casillero de color de una celda: ancla de la calibración y lugar de la GD (en población, el total de aldeanos).</summary>
public sealed record HudChip(int X, int Y, int Width, int Height, HudColor Color);

/// <summary>Un texto que el juego escribe y el lector lee: con qué letra (<see cref="Font"/>) y a qué tamaño lo escribe el mod.</summary>
public sealed record HudField(int X, int Y, int Width, int Height, string Font, int Points);

/// <summary>El "/" entre aldeanos y stock. Sin <see cref="Text"/>, una barrita del color.</summary>
public sealed record HudSeparator(int X, int Y, int Width, int Height, string? Text, int Points, HudColor Color);

/// <summary>Una celda de la barra, con todo adentro en coordenadas relativas a ella (el alto es el de la barra).</summary>
public sealed record HudCell(
    string Name,
    int X,
    int Width,
    HudIcon Icon,
    HudChip? Chip,
    IReadOnlyDictionary<string, HudField> Fields,
    IReadOnlyList<UnitRect> Backgrounds,
    HudSeparator? Separator);

public sealed record HudBar(int Width, int Height, HudColor Background, int BorderWidth, HudColor BorderColor);

/// <summary>El reloj va en otro panel del juego: a <see cref="Gap"/> unidades del final de la barra.</summary>
public sealed record HudClock(int Gap, int Y, int Width, int Height, string Font, int Points);

/// <summary>
/// El diseño del panel de recursos del mod BoCoach HUD (data/hud_layout.json), en unidades del panel del juego (el
/// ícono original mide 84). De acá salen el mod que arma la app, la calibración sola (los casilleros de color son las
/// anclas), el lector (cada campo se recorta en proporción dentro de la caja de su celda) y el lugar de la GD.
/// </summary>
public sealed class HudLayout
{
    public const string FileName = "hud_layout.json";

    /// <summary>Las celdas, en el orden en que van en la barra.</summary>
    public static readonly string[] CellOrder = ["wood", "food", "gold", "stone", "population", "idle", "age"];

    private static readonly Dictionary<string, string[]> FieldsOf = new()
    {
        ["wood"] = ["villagers", "stock"],
        ["food"] = ["villagers", "stock"],
        ["gold"] = ["villagers", "stock"],
        ["stone"] = ["villagers", "stock"],
        ["population"] = ["total", "cap"],
        ["idle"] = ["count"],
        ["age"] = ["text"],
    };

    private static readonly string[] Fonts = ["hud_numbers", "mod_clock", "ocr"];

    private static readonly Dictionary<string, string> Regions = new()
    {
        ["wood"] = RegionNames.Wood,
        ["food"] = RegionNames.Food,
        ["gold"] = RegionNames.Gold,
        ["stone"] = RegionNames.Stone,
        ["population"] = RegionNames.Population,
        ["idle"] = RegionNames.IdleVillagers,
        ["age"] = RegionNames.Age,
    };

    private HudLayout(string name, double pixelsPerUnitAt1080p, HudBar bar, IReadOnlyList<HudCell> cells, HudClock clock)
    {
        Name = name;
        PixelsPerUnitAt1080p = pixelsPerUnitAt1080p;
        Bar = bar;
        Cells = cells;
        Clock = clock;
    }

    public string Name { get; }

    /// <summary>Píxeles por unidad a 1920×1080 con la escala de HUD de referencia (84 unidades = 36 px).</summary>
    public double PixelsPerUnitAt1080p { get; }

    public HudBar Bar { get; }

    /// <summary>En el orden de <see cref="CellOrder"/>.</summary>
    public IReadOnlyList<HudCell> Cells { get; }

    public HudClock Clock { get; }

    public HudCell Cell(string name) => Cells.First(c => c.Name == name);

    /// <summary>Las celdas con casillero, en orden: las anclas de la calibración.</summary>
    public IEnumerable<HudCell> Anchors => Cells.Where(c => c.Chip is not null);

    public static string RegionOf(string cell) => Regions[cell];

    public static string? CellOf(string region) => Regions.FirstOrDefault(r => r.Value == region).Key;

    public UnitRect CellRect(HudCell cell) => new(cell.X, 0, cell.Width, Bar.Height);

    /// <summary>El campo de texto de la edad, en coordenadas de la barra.</summary>
    public UnitRect AgeTextRect()
    {
        var age = Cell("age");
        var text = age.Fields["text"];
        return new UnitRect(age.X + text.X, text.Y, text.Width, text.Height);
    }

    /// <summary>El rectángulo negro del reloj, en coordenadas de la barra (sigue a la barra).</summary>
    public UnitRect ClockRect() => new(Bar.Width + Clock.Gap, Clock.Y, Clock.Width, Clock.Height);

    /// <summary>Dónde cae un campo dentro de la caja de su celda, en proporción.</summary>
    public PixelRect FieldRect(HudCell cell, string field, PixelRect box)
    {
        var f = cell.Fields[field];
        return Proportional(cell, box, f.X, f.Y, f.Width, f.Height);
    }

    /// <summary>Dónde cae el casillero dentro de la caja de su celda, en proporción.</summary>
    public PixelRect ChipRect(HudCell cell, PixelRect box)
    {
        var chip = cell.Chip ?? throw new ArgumentException($"La celda {cell.Name} no tiene casillero.");
        return Proportional(cell, box, chip.X, chip.Y, chip.Width, chip.Height);
    }

    private PixelRect Proportional(HudCell cell, PixelRect box, int x, int y, int width, int height) => new(
        box.X + Scale(x, box.Width, cell.Width),
        box.Y + Scale(y, box.Height, Bar.Height),
        Scale(width, box.Width, cell.Width),
        Scale(height, box.Height, Bar.Height));

    private static int Scale(int units, int pixels, int totalUnits) => (int)Math.Round((double)units * pixels / totalUnits);

    public static HudLayout Load(string path)
    {
        try
        {
            return Parse(File.ReadAllText(path), Path.GetFileName(path));
        }
        catch (JsonException e)
        {
            throw new InvalidDataException($"{Path.GetFileName(path)} está mal formado: {e.Message}", e);
        }
    }

    public static HudLayout Parse(string json, string source = FileName)
    {
        var dto = JsonSerializer.Deserialize<Dto>(json, JsonOptions) ?? throw new InvalidDataException($"{source} está vacío.");
        string Missing(string what) => $"{source}: falta {what}.";
        if (string.IsNullOrWhiteSpace(dto.Name))
            throw new InvalidDataException(Missing("name"));
        if (dto.PixelsPerUnitAt1080p is not > 0)
            throw new InvalidDataException(Missing("pixels_per_unit_at_1080p"));
        var barDto = dto.Bar ?? throw new InvalidDataException(Missing("bar"));
        var bar = new HudBar(barDto.Width, barDto.Height, Color(barDto.Background, "bar.background", source),
            barDto.Border?.Width ?? 0, Color(barDto.Border?.Color, "bar.border.color", source));
        var clockDto = dto.Clock ?? throw new InvalidDataException(Missing("clock"));
        var clock = new HudClock(clockDto.Gap, clockDto.Y, clockDto.Width, clockDto.Height, clockDto.Font ?? "", clockDto.Points);
        if (!Fonts.Contains(clock.Font))
            throw new InvalidDataException($"{source}: la letra del reloj \"{clock.Font}\" no existe (vale {string.Join(", ", Fonts)}).");

        var cells = new List<HudCell>();
        foreach (var name in CellOrder)
        {
            if (dto.Cells is null || !dto.Cells.TryGetValue(name, out var c))
                throw new InvalidDataException(Missing($"la celda {name}"));
            var icon = c.Icon ?? throw new InvalidDataException(Missing($"el ícono de {name}"));
            var fields = new Dictionary<string, HudField>();
            foreach (var field in FieldsOf[name])
            {
                if (c.Fields is null || !c.Fields.TryGetValue(field, out var f))
                    throw new InvalidDataException(Missing($"el campo {field} de {name}"));
                if (!Fonts.Contains(f.Font ?? ""))
                    throw new InvalidDataException($"{source}: la letra \"{f.Font}\" del campo {field} de {name} no existe (vale {string.Join(", ", Fonts)}).");
                fields[field] = new HudField(f.X, f.Y, f.Width, f.Height, f.Font!, f.Points);
            }
            foreach (var extra in (c.Fields?.Keys ?? Enumerable.Empty<string>()).Except(FieldsOf[name]))
                throw new InvalidDataException($"{source}: la celda {name} no tiene un campo \"{extra}\" (tiene {string.Join(", ", FieldsOf[name])}).");
            HudChip? chip = c.Chip is { } ch ? new HudChip(ch.X, ch.Y, ch.Width, ch.Height, Color(ch.Color, $"{name}.chip.color", source)) : null;
            if (chip is null && name is not ("idle" or "age"))
                throw new InvalidDataException(Missing($"el casillero de {name}"));
            var backgrounds = (c.Backgrounds ?? []).Select(b => new UnitRect(b.X, b.Y, b.Width, b.Height)).ToList();
            HudSeparator? separator = c.Separator is { } s
                ? new HudSeparator(s.X, s.Y, s.Width, s.Height, s.Text, s.Points, Color(s.Color, $"{name}.separator.color", source))
                : null;
            cells.Add(new HudCell(name, c.X, c.Width, new HudIcon(icon.X, icon.Y, icon.Size), chip, fields, backgrounds, separator));
        }
        var layout = new HudLayout(dto.Name, dto.PixelsPerUnitAt1080p.Value, bar, cells, clock);
        layout.Validate(source);
        return layout;
    }

    private void Validate(string source)
    {
        HudCell? previous = null;
        foreach (var cell in Cells)
        {
            if (cell.Width <= 0 || cell.X < 0 || cell.X + cell.Width > Bar.Width)
                throw new InvalidDataException($"{source}: la celda {cell.Name} se sale de la barra.");
            if (previous is not null && cell.X < previous.X + previous.Width)
                throw new InvalidDataException($"{source}: la celda {cell.Name} se solapa con {previous.Name}.");
            void Inside(string what, int x, int y, int w, int h)
            {
                if (w <= 0 || h <= 0 || x < 0 || y < 0 || x + w > cell.Width || y + h > Bar.Height)
                    throw new InvalidDataException($"{source}: {what} de {cell.Name} se sale de la celda.");
            }
            Inside("el ícono", cell.Icon.X, cell.Icon.Y, cell.Icon.Size, cell.Icon.Size);
            if (cell.Chip is { } chip)
                Inside("el casillero", chip.X, chip.Y, chip.Width, chip.Height);
            foreach (var (name, f) in cell.Fields)
                Inside($"el campo {name}", f.X, f.Y, f.Width, f.Height);
            foreach (var b in cell.Backgrounds)
                Inside("un fondo", b.X, b.Y, b.Width, b.Height);
            if (cell.Separator is { } s)
                Inside("el separador", s.X, s.Y, s.Width, s.Height);
            previous = cell;
        }
    }

    public string ToJson() => JsonSerializer.Serialize(new Dto(
        Name,
        PixelsPerUnitAt1080p,
        new BarDto(Bar.Width, Bar.Height, Array(Bar.Background), new BorderDto(Bar.BorderWidth, Array(Bar.BorderColor))),
        Cells.ToDictionary(c => c.Name, c => new CellDto(
            c.X,
            c.Width,
            new IconDto(c.Icon.X, c.Icon.Y, c.Icon.Size),
            c.Chip is { } chip ? new ChipDto(chip.X, chip.Y, chip.Width, chip.Height, Array(chip.Color)) : null,
            c.Fields.ToDictionary(f => f.Key, f => new FieldDto(f.Value.X, f.Value.Y, f.Value.Width, f.Value.Height, f.Value.Font, f.Value.Points)),
            c.Backgrounds.Select(b => new RectDto(b.X, b.Y, b.Width, b.Height)).ToList(),
            c.Separator is { } s ? new SeparatorDto(s.X, s.Y, s.Width, s.Height, s.Text, s.Points, Array(s.Color)) : null)),
        new ClockDto(Clock.Gap, Clock.Y, Clock.Width, Clock.Height, Clock.Font, Clock.Points)), JsonOptions);

    private static HudColor Color(double[]? values, string what, string source) =>
        values is { Length: 3 } ? new HudColor(values[0], values[1], values[2]) : throw new InvalidDataException($"{source}: {what} tiene que ser [r, g, b] de 0 a 1.");

    private static double[] Array(HudColor color) => [color.R, color.G, color.B];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private sealed record Dto(string? Name, [property: JsonPropertyName("pixels_per_unit_at_1080p")] double? PixelsPerUnitAt1080p, BarDto? Bar, Dictionary<string, CellDto>? Cells, ClockDto? Clock);
    private sealed record BarDto(int Width, int Height, double[]? Background, BorderDto? Border);
    private sealed record BorderDto(int Width, double[]? Color);
    private sealed record IconDto(int X, int Y, int Size);
    private sealed record ChipDto(int X, int Y, int Width, int Height, double[]? Color);
    private sealed record FieldDto(int X, int Y, int Width, int Height, string? Font, int Points);
    private sealed record RectDto(int X, int Y, int Width, int Height);
    private sealed record SeparatorDto(int X, int Y, int Width, int Height, string? Text, int Points, double[]? Color);
    private sealed record CellDto(int X, int Width, IconDto? Icon, ChipDto? Chip, Dictionary<string, FieldDto>? Fields, List<RectDto>? Backgrounds, SeparatorDto? Separator);
    private sealed record ClockDto(int Gap, int Y, int Width, int Height, string? Font, int Points);
}
