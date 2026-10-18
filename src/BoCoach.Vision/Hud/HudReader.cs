using System.Globalization;
using System.Text.RegularExpressions;
using BoCoach.Core;
using BoCoach.Core.Data;
using BoCoach.Vision.Capture;
using BoCoach.Vision.Digits;
using BoCoach.Vision.Regions;

namespace BoCoach.Vision.Hud;

/// <summary>
/// Las letras del HUD (data/fonts): reloj, números blancos del stock y total de aldeanos en azul. Con el mod
/// BoCoach HUD, todos los números van en una sola letra blanca sobre negro (<paramref name="ModNumbers"/>) y el reloj
/// en otra (<paramref name="ModClock"/>); son opcionales mientras no estén armadas.
/// </summary>
public sealed record HudFonts(
    GlyphFont Clock, GlyphFont Stock, GlyphFont Villagers,
    GlyphFont? ModNumbers = null, GlyphFont? ModClock = null, GlyphFont? ModPopulation = null)
{
    public static HudFonts Load(string directory) => new(
        GlyphFont.Load(Path.Combine(directory, "clock.json")),
        GlyphFont.Load(Path.Combine(directory, "stock.json")),
        GlyphFont.Load(Path.Combine(directory, "villagers.json")),
        Optional(Path.Combine(directory, "mod_numbers.json")),
        Optional(Path.Combine(directory, "mod_clock.json")),
        Optional(Path.Combine(directory, "mod_population.json")));

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
/// <param name="ModBands">Franja de color del cuadrado de cada recurso, en píxeles de su caja (con el mod): ahí va la GD. También la del botón de inactivos, donde parpadea el aviso.</param>
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
    IReadOnlyDictionary<string, PixelRect>? ModBands = null)
{
    public int? Stock(Resource resource) => resource switch
    {
        Resource.Food => Food,
        Resource.Wood => Wood,
        Resource.Gold => Gold,
        _ => Stone,
    };
}

/// <summary>
/// Lee los números del HUD de una captura con template matching. Cada caja de recurso puede tener solo el número
/// o también el ícono y los aldeanos del recurso: el stock es el grupo de números blancos de más a la derecha.
/// Con el mod BoCoach HUD (y su letra), cada caja tiene un cuadrado negro con los aldeanos y otro con el stock.
/// </summary>
public sealed class HudReader(HudFonts fonts)
{
    private readonly GlyphReader _clock = new(fonts.Clock);
    private readonly GlyphReader _stock = new(fonts.Stock);
    private readonly GlyphReader _villagers = new(fonts.Villagers);
    private readonly GlyphReader? _modNumbers = fonts.ModNumbers is { } numbers ? new(numbers) : null;
    private readonly GlyphReader? _modClock = fonts.ModClock is { } clock ? new(clock) : null;
    private readonly GlyphReader? _modPopulation = fonts.ModPopulation is { } population ? new(population) : null;
    private static readonly Regex PopulationText = new(@"^(\d{1,3})/(\d{1,3})$", RegexOptions.Compiled);
    private static readonly Regex ModClockTime = new(@"^\d\d:\d\d:\d\d", RegexOptions.Compiled);

    /// <summary>
    /// En pausa el juego oscurece todo: los números del mod (blancos sobre negro puro) se estiran para que lo más
    /// claro vuelva a ser blanco.
    /// </summary>
    private const double PauseGain = 5;

    /// <param name="image">Imagen de cada región, o null si no está calibrada o grabada.</param>
    public HudReading Read(Func<string, CapturedImage?> image)
    {
        var clipped = new List<string>();
        var villagersOn = new Dictionary<Resource, int>();
        var bands = new Dictionary<string, PixelRect>();

        int? Parse(GlyphReading reading, string region)
        {
            if (reading.Clipped)
                clipped.Add(region);
            return reading.IsUsable && int.TryParse(reading.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : null;
        }

        // Con el mod, las dos partes de la caja (el cuadrado de los aldeanos y el fondo negro de la derecha), ya
        // estiradas de brillo por si el juego está en pausa; sin el mod, null.
        (CapturedImage Square, CapturedImage? Right)? ModParts(CapturedImage img) =>
            _modNumbers is not null && ModPanel.Split(img) is { } parts
                ? (parts.Square.Stretched(PauseGain), parts.Stock?.Stretched(PauseGain))
                : null;

        int? Stock(Resource resource, string region)
        {
            if (image(region) is not { } img)
                return null;
            if (ModParts(img) is not { } parts)
                return Parse(_stock.ReadRightmostGroup(img), region);
            if (Parse(_modNumbers!.Read(parts.Square), region) is { } villagers)
                villagersOn[resource] = villagers;
            if (ModPanel.Band(img) is { } band)
                bands[region] = band;
            return parts.Right is { } right ? Parse(_modNumbers.Read(right), region) : null;
        }

        // Inactivos: con el mod, el número del cuadrado; sin él, el de más a la derecha.
        int? Idle()
        {
            if (image(RegionNames.IdleVillagers) is not { } img)
                return null;
            // Su franja también: ahí parpadea el aviso de inactivos.
            if (_modNumbers is not null && ModPanel.Band(img) is { } band)
                bands[RegionNames.IdleVillagers] = band;
            return ModParts(img) is { } parts
                ? Parse(_modNumbers!.Read(parts.Square), RegionNames.IdleVillagers)
                : Parse(_stock.ReadRightmostGroup(img), RegionNames.IdleVillagers);
        }

        // Población: con el mod, el total de aldeanos en el cuadrado y "N/M" a la derecha; sin él, el total azul.
        (int? Villagers, int? Population, int? Cap) Population()
        {
            if (image(RegionNames.Population) is not { } img)
                return (null, null, null);
            if (ModParts(img) is not { } parts)
                return (Parse(_villagers.ReadRightmostGroup(img), RegionNames.Population), null, null);
            var villagers = Parse(_modNumbers!.Read(parts.Square), RegionNames.Population);
            if (_modPopulation is null || parts.Right is not { } right)
                return (villagers, null, null);
            var reading = _modPopulation.Read(right);
            var match = PopulationText.Match(reading.Text);
            return match.Success && !reading.Clipped
                ? (villagers, int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture), int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture))
                : (villagers, null, null);
        }

        double? clockSeconds = null;
        if (image(RegionNames.Clock) is { } clockImage)
        {
            if (_modClock is not null && ModPanel.HasBlackBackground(clockImage))
            {
                // El reloj del mod sigue con " (Normal - 1.7)…": vale el "hh:mm:ss" del principio, aunque lo de
                // después toque el borde de la caja.
                var reading = _modClock.Read(clockImage.Stretched(PauseGain));
                var time = ModClockTime.Match(reading.Text);
                if (time.Success && GameClock.TryParse(time.Value, out var seconds))
                    clockSeconds = seconds;
                else if (reading.Clipped)
                    clipped.Add(RegionNames.Clock);
            }
            else
            {
                var reading = _clock.Read(clockImage);
                if (reading.Clipped)
                    clipped.Add(RegionNames.Clock);
                if (reading.IsUsable && reading.Text.Contains(':') && GameClock.TryParse(reading.Text, out var seconds))
                    clockSeconds = seconds;
            }
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
            bands.Count > 0 ? bands : null);
    }

    /// <summary>Lee una captura grabada.</summary>
    public HudReading Read(Recording.RecordedSession session, int frame) =>
        Read(region => session.Has(frame, region) ? session.Image(frame, region) : null);
}
