using System.Globalization;
using System.Text.RegularExpressions;
using BoCoach.Core;
using BoCoach.Core.Data;
using BoCoach.Vision.Capture;
using BoCoach.Vision.Digits;
using BoCoach.Vision.Regions;

namespace BoCoach.Vision.Hud;

/// <summary>
/// Las letras del HUD (data/fonts): reloj, números blancos del stock y total de aldeanos en azul. Con el mod viejo
/// BoCoach HUD, todos los números van en una sola letra blanca sobre negro (<paramref name="ModNumbers"/>), el reloj en
/// otra (<paramref name="ModClock"/>) y el "N/M" en <paramref name="ModPopulation"/>. Con el diseño nuevo
/// (data/hud_layout.json), todos los números del panel van en <paramref name="HudNumbers"/> (48 pt, dígitos y "/") y
/// el reloj sigue en <paramref name="ModClock"/>. Todas son opcionales mientras no estén armadas.
/// </summary>
public sealed record HudFonts(
    GlyphFont Clock, GlyphFont Stock, GlyphFont Villagers,
    GlyphFont? ModNumbers = null, GlyphFont? ModClock = null, GlyphFont? ModPopulation = null, GlyphFont? HudNumbers = null)
{
    /// <summary>Los íconos de la cola de producción (data/icons/production_queue.json), o null si no están.</summary>
    public IconLibrary? QueueIcons { get; init; }

    /// <param name="directory">data/fonts; los íconos de la cola están al lado, en data/icons.</param>
    public static HudFonts Load(string directory) => new(
        GlyphFont.Load(Path.Combine(directory, "clock.json")),
        GlyphFont.Load(Path.Combine(directory, "stock.json")),
        GlyphFont.Load(Path.Combine(directory, "villagers.json")),
        Optional(Path.Combine(directory, "mod_numbers.json")),
        Optional(Path.Combine(directory, "mod_clock.json")),
        Optional(Path.Combine(directory, "mod_population.json")),
        Optional(Path.Combine(directory, "hud_numbers.json")))
    {
        QueueIcons = IconLibrary.TryLoad(Path.Combine(directory, "..", "icons", "production_queue.json")),
    };

    private static GlyphFont? Optional(string path) => File.Exists(path) ? GlyphFont.Load(path) : null;
}

/// <summary>
/// Lo leído del HUD en una captura, sin suavizar. null = no se pudo leer (región sin calibrar, número cortado,
/// carácter desconocido).
/// </summary>
/// <param name="Clipped">Regiones donde el número toca el borde de la caja: hay que agrandarla.</param>
/// <param name="VillagersOn">Aldeanos en cada recurso (solo con el mod BoCoach HUD).</param>
/// <param name="Population">Población "N" de "N/M" (solo con el mod BoCoach HUD).</param>
/// <param name="PopulationCap">Tope "M" de "N/M" (solo con el mod BoCoach HUD).</param>
/// <param name="ModBands">Con el mod viejo, la franja de color del cuadrado de cada recurso, en píxeles de su caja: ahí va la GD. Con el diseño nuevo, el casillero de cada recurso y, en inactivos, la celda entera (el marco del aviso).</param>
/// <param name="LayoutName">El diseño con que se leyó al menos una celda (data/hud_layout.json), o null (mod viejo o sin mod).</param>
public sealed record HudReading(
    double? Clock,
    int? Food,
    int? Wood,
    int? Gold,
    int? Stone,
    int? Villagers,
    int? IdleVillagers,
    IReadOnlyList<string> Clipped,
    IReadOnlyDictionary<Resource, int>? VillagersOn = null,
    int? Population = null,
    int? PopulationCap = null,
    IReadOnlyDictionary<string, PixelRect>? ModBands = null,
    string? LayoutName = null)
{
    public int? Stock(Resource resource) => resource switch
    {
        Resource.Food => Food,
        Resource.Wood => Wood,
        Resource.Gold => Gold,
        _ => Stone,
    };

    public bool Equals(HudReading? other) =>
        other is not null && Clock == other.Clock && Food == other.Food && Wood == other.Wood && Gold == other.Gold && Stone == other.Stone
        && Villagers == other.Villagers && IdleVillagers == other.IdleVillagers && Clipped.SequenceEqual(other.Clipped)
        && SameMap(VillagersOn, other.VillagersOn) && Population == other.Population && PopulationCap == other.PopulationCap
        && SameMap(ModBands, other.ModBands) && LayoutName == other.LayoutName;

    public override int GetHashCode() => HashCode.Combine(Clock, Food, Wood, Gold, Stone, Villagers, IdleVillagers, LayoutName);

    private static bool SameMap<TKey, TValue>(IReadOnlyDictionary<TKey, TValue>? a, IReadOnlyDictionary<TKey, TValue>? b) where TValue : struct =>
        a is null ? b is null : b is not null && a.Count == b.Count && a.All(p => b.TryGetValue(p.Key, out var v) && v.Equals(p.Value));
}

/// <summary>
/// Lee los números del HUD de una captura con template matching. Cada caja de recurso puede tener solo el número
/// o también el ícono y los aldeanos del recurso: el stock es el grupo de números blancos de más a la derecha.
/// Con el mod viejo BoCoach HUD (y su letra), cada caja tiene un cuadrado negro con los aldeanos y otro con el stock.
/// Con el diseño nuevo (data/hud_layout.json) y su letra, cada caja es una celda: si el casillero de color está
/// donde el diseño dice, cada campo se recorta por el diseño (<see cref="LayoutPanel"/>); si no, la celda va por el
/// camino de siempre.
/// </summary>
public sealed class HudReader(HudFonts fonts)
{
    private readonly GlyphReader _clock = new(fonts.Clock);
    private readonly GlyphReader _stock = new(fonts.Stock);
    private readonly GlyphReader _villagers = new(fonts.Villagers);
    private readonly GlyphReader? _modNumbers = fonts.ModNumbers is { } numbers ? new(numbers) : null;
    private readonly GlyphReader? _modClock = fonts.ModClock is { } clock ? new(clock) : null;
    private readonly GlyphReader? _modPopulation = fonts.ModPopulation is { } population ? new(population) : null;
    private readonly GlyphReader? _hudNumbers = fonts.HudNumbers is { } hud ? new(hud) : null;
    private static readonly Regex PopulationText = new(@"^(\d{1,3})/(\d{1,3})$", RegexOptions.Compiled);
    private static readonly Regex ModClockTime = new(@"^\d\d:\d\d:\d\d", RegexOptions.Compiled);

    /// <summary>
    /// En pausa el juego oscurece todo: los números del mod (blancos sobre negro puro) se estiran para que lo más
    /// claro vuelva a ser blanco.
    /// </summary>
    private const double PauseGain = 5;

    /// <param name="image">Imagen de cada región, o null si no está calibrada o grabada.</param>
    /// <param name="layout">El diseño del mod nuevo con que se calibró o grabó, o null.</param>
    public HudReading Read(Func<string, CapturedImage?> image, HudLayout? layout = null)
    {
        var clipped = new List<string>();
        var villagersOn = new Dictionary<Resource, int>();
        var bands = new Dictionary<string, PixelRect>();
        string? layoutName = null;

        int? Parse(GlyphReading reading, string region)
        {
            if (reading.Clipped)
                clipped.Add(region);
            return reading.IsUsable && int.TryParse(reading.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : null;
        }

        // Con el diseño y su letra: la caja estirada de brillo (por si el juego está en pausa) y su celda, si el
        // casillero está donde el diseño dice. Si no, null: la celda va por el camino de hoy.
        (CapturedImage Box, HudCell Cell, PixelRect Chip)? LayoutCell(string cellName, string region)
        {
            if (layout is null || _hudNumbers is null || image(region) is not { } img)
                return null;
            var cell = layout.Cell(cellName);
            var box = img.Stretched(PauseGain);
            return LayoutPanel.Chip(layout, cell, box) is { } chip ? (box, cell, chip) : null;
        }

        int? Field(HudLayout design, (CapturedImage Box, HudCell Cell, PixelRect Chip) cell, string field, string region) =>
            Parse(_hudNumbers!.Read(LayoutPanel.Crop(design, cell.Cell, field, cell.Box)), region);

        // Con el mod viejo, las dos partes de la caja (el cuadrado de los aldeanos y el fondo negro de la derecha), ya
        // estiradas de brillo por si el juego está en pausa; sin el mod, null.
        (CapturedImage Square, CapturedImage? Right)? ModParts(CapturedImage img) =>
            _modNumbers is not null && ModPanel.Split(img) is { } parts
                ? (parts.Square.Stretched(PauseGain), parts.Stock?.Stretched(PauseGain))
                : null;

        int? Stock(Resource resource, string region)
        {
            if (LayoutCell(resource.ToString().ToLowerInvariant(), region) is { } cell)
            {
                layoutName = layout!.Name;
                bands[region] = cell.Chip;
                if (Field(layout, cell, "villagers", region) is { } villagers)
                    villagersOn[resource] = villagers;
                return Field(layout, cell, "stock", region);
            }
            if (image(region) is not { } img)
                return null;
            if (ModParts(img) is not { } parts)
                return Parse(_stock.ReadRightmostGroup(img), region);
            if (Parse(_modNumbers!.Read(parts.Square), region) is { } onResource)
                villagersOn[resource] = onResource;
            if (ModPanel.Band(img) is { } band)
                bands[region] = band;
            return parts.Right is { } right ? Parse(_modNumbers.Read(right), region) : null;
        }

        // Inactivos: con el diseño, el campo de la celda (y la celda entera para el marco del aviso); con el mod viejo,
        // el número del cuadrado; sin mod, el de más a la derecha.
        int? Idle()
        {
            if (LayoutCell("idle", RegionNames.IdleVillagers) is { } cell)
            {
                layoutName = layout!.Name;
                bands[RegionNames.IdleVillagers] = cell.Chip;
                return Field(layout, cell, "count", RegionNames.IdleVillagers);
            }
            if (image(RegionNames.IdleVillagers) is not { } img)
                return null;
            // Su franja también: ahí parpadea el aviso de inactivos.
            if (_modNumbers is not null && ModPanel.Band(img) is { } band)
                bands[RegionNames.IdleVillagers] = band;
            return ModParts(img) is { } parts
                ? Parse(_modNumbers!.Read(parts.Square), RegionNames.IdleVillagers)
                : Parse(_stock.ReadRightmostGroup(img), RegionNames.IdleVillagers);
        }

        (int? Villagers, int? Population, int? Cap) PopulationFrom(GlyphReading reading, int? villagers)
        {
            var match = PopulationText.Match(reading.Text);
            return match.Success && !reading.Clipped
                ? (villagers, int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture), int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture))
                : (villagers, null, null);
        }

        // Población: con el diseño, el total en el casillero azul y "N/M" en su campo; con el mod viejo, el total en el
        // cuadrado y "N/M" a la derecha; sin mod, el total azul.
        (int? Villagers, int? Population, int? Cap) Population()
        {
            if (LayoutCell("population", RegionNames.Population) is { } cell)
            {
                layoutName = layout!.Name;
                var total = Field(layout, cell, "total", RegionNames.Population);
                return PopulationFrom(_hudNumbers!.Read(LayoutPanel.Crop(layout, cell.Cell, "cap", cell.Box)), total);
            }
            if (image(RegionNames.Population) is not { } img)
                return (null, null, null);
            if (ModParts(img) is not { } parts)
                return (Parse(_villagers.ReadRightmostGroup(img), RegionNames.Population), null, null);
            var villagers = Parse(_modNumbers!.Read(parts.Square), RegionNames.Population);
            if (_modPopulation is null || parts.Right is not { } right)
                return (villagers, null, null);
            return PopulationFrom(_modPopulation.Read(right), villagers);
        }

        // El reloj sigue con " (Normal - 1.7)…": vale el "hh:mm:ss" del principio, aunque lo de después toque el
        // borde de la caja.
        static double? TimeAtStart(GlyphReading reading) =>
            ModClockTime.Match(reading.Text) is { Success: true } time && GameClock.TryParse(time.Value, out var seconds) ? seconds : null;

        double? clockSeconds = null;
        if (image(RegionNames.Clock) is { } clockImage)
        {
            var clockClipped = false;
            if (_modClock is not null && ModPanel.HasBlackBackground(clockImage))
            {
                var reading = _modClock.Read(clockImage.Stretched(PauseGain));
                clockSeconds = TimeAtStart(reading);
                clockClipped = reading.Clipped;
            }
            // Desde el parche del 2026-10-27 el mod ubica el reloj pero no dibuja el negro de atrás (partida
            // 20261028-232628): con la textura y la sombra de las letras la caja igual parece negra, la letra del mod
            // no lee nada y la de siempre lee bien el reloj grande sobre la textura.
            if (clockSeconds is null)
            {
                var reading = _clock.Read(clockImage);
                clockSeconds = TimeAtStart(reading)
                    ?? (reading.IsUsable && reading.Text.Contains(':') && GameClock.TryParse(reading.Text, out var seconds) ? seconds : null);
                clockClipped |= reading.Clipped;
            }
            if (clockSeconds is null && clockClipped)
                clipped.Add(RegionNames.Clock);
        }

        var food = Stock(Resource.Food, RegionNames.Food);
        var wood = Stock(Resource.Wood, RegionNames.Wood);
        var gold = Stock(Resource.Gold, RegionNames.Gold);
        var stone = Stock(Resource.Stone, RegionNames.Stone);
        var (villagersTotal, population, cap) = Population();
        return new HudReading(
            clockSeconds,
            food,
            wood,
            gold,
            stone,
            villagersTotal,
            Idle(),
            clipped,
            villagersOn.Count > 0 ? villagersOn : null,
            population,
            cap,
            bands.Count > 0 ? bands : null,
            layoutName);
    }

    /// <summary>Lee una captura grabada, con el diseño con que se grabó si lo tiene.</summary>
    public HudReading Read(Recording.RecordedSession session, int frame) =>
        Read(region => session.Has(frame, region) ? session.Image(frame, region) : null, session.Hud);
}
