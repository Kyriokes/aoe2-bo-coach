using System.Globalization;
using BoCoach.Core;
using BoCoach.Core.Data;
using BoCoach.Vision.Capture;
using BoCoach.Vision.Digits;
using BoCoach.Vision.Regions;

namespace BoCoach.Vision.Hud;

/// <summary>Las letras del HUD (data/fonts): reloj, números blancos del stock y total de aldeanos en azul.</summary>
public sealed record HudFonts(GlyphFont Clock, GlyphFont Stock, GlyphFont Villagers)
{
    public static HudFonts Load(string directory) => new(
        GlyphFont.Load(Path.Combine(directory, "clock.json")),
        GlyphFont.Load(Path.Combine(directory, "stock.json")),
        GlyphFont.Load(Path.Combine(directory, "villagers.json")));
}

/// <summary>
/// Lo leído del HUD en una captura, sin suavizar. null = no se pudo leer (región sin calibrar, número cortado,
/// carácter desconocido).
/// </summary>
/// <param name="Clipped">Regiones donde el número toca el borde de la caja: hay que agrandarla.</param>
public sealed record HudReading(
    double? Clock,
    int? Food,
    int? Wood,
    int? Gold,
    int? Stone,
    int? Villagers,
    int? IdleVillagers,
    IReadOnlyList<string> Clipped)
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
/// </summary>
public sealed class HudReader(HudFonts fonts)
{
    private readonly GlyphReader _clock = new(fonts.Clock);
    private readonly GlyphReader _stock = new(fonts.Stock);
    private readonly GlyphReader _villagers = new(fonts.Villagers);

    /// <param name="image">Imagen de cada región, o null si no está calibrada o grabada.</param>
    public HudReading Read(Func<string, CapturedImage?> image)
    {
        var clipped = new List<string>();

        int? Number(GlyphReader reader, string region)
        {
            if (image(region) is not { } img)
                return null;
            var reading = reader.ReadRightmostGroup(img);
            if (reading.Clipped)
                clipped.Add(region);
            return reading.IsUsable && int.TryParse(reading.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : null;
        }

        double? clock = null;
        if (image(RegionNames.Clock) is { } clockImage)
        {
            var reading = _clock.Read(clockImage);
            if (reading.Clipped)
                clipped.Add(RegionNames.Clock);
            if (reading.IsUsable && reading.Text.Contains(':') && GameClock.TryParse(reading.Text, out var seconds))
                clock = seconds;
        }

        return new HudReading(
            clock,
            Number(_stock, RegionNames.Food),
            Number(_stock, RegionNames.Wood),
            Number(_stock, RegionNames.Gold),
            Number(_stock, RegionNames.Stone),
            Number(_villagers, RegionNames.Population),
            Number(_stock, RegionNames.IdleVillagers),
            clipped);
    }

    /// <summary>Lee una captura grabada.</summary>
    public HudReading Read(Recording.RecordedSession session, int frame) =>
        Read(region => session.Has(frame, region) ? session.Image(frame, region) : null);
}
