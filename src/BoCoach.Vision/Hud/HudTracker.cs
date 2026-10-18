using BoCoach.Core.Data;
using BoCoach.Vision.Events;
using BoCoach.Vision.Smoothing;

namespace BoCoach.Vision.Hud;

/// <summary>El estado del HUD ya suavizado, en un instante.</summary>
/// <param name="RealTime">Segundos reales desde que se empezó a leer.</param>
/// <param name="Clock">Reloj de la partida en segundos.</param>
/// <param name="NewEvents">Eventos del log que aparecieron en esta captura.</param>
/// <param name="Clipped">Regiones con el número cortado por la caja (avisar que hay que agrandarla).</param>
/// <param name="ClockRead">Si en esta captura se leyó el reloj (<paramref name="Clock"/> conserva el último valor).</param>
/// <param name="Population">"N" de "N/M" (solo con el mod BoCoach HUD).</param>
/// <param name="PopulationCap">"M" de "N/M" (solo con el mod BoCoach HUD).</param>
/// <param name="VillagersOn">Aldeanos en cada recurso según el HUD (solo con el mod BoCoach HUD).</param>
/// <param name="ModBands">Franja de color del cuadrado de cada recurso en su caja (con el mod), para dibujar la GD.</param>
public sealed record HudState(
    double RealTime,
    double? Clock,
    double? Food,
    double? Wood,
    double? Gold,
    double? Stone,
    int? Villagers,
    int? IdleVillagers,
    Age? Age,
    IReadOnlyList<string> NewEvents,
    IReadOnlyList<string> Clipped,
    bool ClockRead = true,
    int? Population = null,
    int? PopulationCap = null,
    IReadOnlyDictionary<Resource, int>? VillagersOn = null,
    IReadOnlyDictionary<string, Capture.PixelRect>? ModBands = null)
{
    /// <summary>
    /// Lo que hay en la cola de producción, de izquierda a derecha (claves de game_data, "housed" o "?"), o null si
    /// no se lee (región sin calibrar o sin los íconos).
    /// </summary>
    public IReadOnlyList<string>? Queue { get; init; }

    /// <summary>Stock completo si se leyeron los cuatro recursos.</summary>
    public ResourceAmounts? Stock => Food is { } f && Wood is { } w && Gold is { } g && Stone is { } s
        ? new ResourceAmounts(f, w, s, g)
        : null;
}

/// <summary>
/// Junta las lecturas captura por captura y las suaviza: mediana y descarte de saltos imposibles por valor
/// (<see cref="ValueFilter"/>), edad estable y eventos nuevos del log.
/// </summary>
public sealed class HudTracker
{
    // Recursos: en 0,5 s pueden entrar varias descargas juntas (la caza trae 35 por aldeano).
    private ValueFilter Resource() => new() { Window = 3, MaxRisePerSecond = 150, Slack = 40, ConfirmJump = 3, ResetOnDrop = 20 };

    private readonly ValueFilter _clock = new() { Window = 1, MaxRisePerSecond = 3, MaxDropPerSecond = 0, Slack = 1.5, ConfirmJump = 6 };
    private readonly ValueFilter _food;
    private readonly ValueFilter _wood;
    private readonly ValueFilter _gold;
    private readonly ValueFilter _stone;
    private readonly ValueFilter _villagers = new() { Window = 1, MaxRisePerSecond = 1, Slack = 2, ConfirmJump = 3 };
    private readonly ValueFilter _idle = new() { Window = 3, ConfirmJump = 2 };
    private readonly ValueFilter _population = new() { Window = 3, ConfirmJump = 2 };
    private readonly ValueFilter _cap = new() { Window = 3, ConfirmJump = 2 };
    private readonly Dictionary<Resource, ValueFilter> _villagersOn = Enum.GetValues<Resource>().ToDictionary(r => r, _ => new ValueFilter { Window = 3, ConfirmJump = 2 });
    private Age? _age;
    private Age? _pendingAge;
    private int _pendingAgeCount;

    public HudTracker()
    {
        _food = Resource();
        _wood = Resource();
        _gold = Resource();
        _stone = Resource();
    }

    /// <summary>Capturas seguidas leyendo la misma edad para darla por buena.</summary>
    public int ConfirmAge { get; init; } = 3;

    public HudState Update(double realTime, HudReading reading, IReadOnlyList<string>? newEvents = null, Age? ageRead = null)
    {
        UpdateAge(ageRead);
        var villagersOn = new Dictionary<Resource, int>();
        foreach (var (resource, filter) in _villagersOn)
        {
            int? read = reading.VillagersOn is { } on && on.TryGetValue(resource, out var v) ? v : null;
            if (filter.Add(read, realTime) is { } smoothed)
                villagersOn[resource] = (int)smoothed;
        }
        var population = (int?)_population.Add(reading.Population, realTime);
        var cap = (int?)_cap.Add(reading.PopulationCap, realTime);
        return new HudState(
            realTime,
            _clock.Add(reading.Clock, realTime),
            _food.Add(reading.Food, realTime),
            _wood.Add(reading.Wood, realTime),
            _gold.Add(reading.Gold, realTime),
            _stone.Add(reading.Stone, realTime),
            (int?)_villagers.Add(reading.Villagers, realTime),
            (int?)_idle.Add(reading.IdleVillagers, realTime),
            _age,
            newEvents ?? [],
            reading.Clipped,
            reading.Clock is not null,
            population,
            cap,
            villagersOn.Count > 0 ? villagersOn : null,
            reading.ModBands);
    }

    private void UpdateAge(Age? read)
    {
        if (read is null || read == _age)
        {
            _pendingAge = null;
            _pendingAgeCount = 0;
            return;
        }
        if (read == _pendingAge)
            _pendingAgeCount++;
        else
            (_pendingAge, _pendingAgeCount) = (read, 1);
        if (_pendingAgeCount >= ConfirmAge || _age is null)
            _age = read;
    }
}

/// <summary>Lee la edad del texto del HUD ("Dark Age", "Feudal Age", ...), tolerando errores del OCR.</summary>
public static class AgeText
{
    private static readonly (Age Age, string Text)[] Names =
    [
        (Age.Dark, "dark age"),
        (Age.Feudal, "feudal age"),
        (Age.Castle, "castle age"),
        (Age.Imperial, "imperial age"),
    ];

    public static Age? Parse(string? text)
    {
        var normalized = EventMatcher.Normalize(text ?? "");
        if (normalized.Length < 4)
            return null;
        var best = Names
            .Select(n => (n.Age, Distance: EventMatcher.Levenshtein(normalized, n.Text) / (double)n.Text.Length))
            .MinBy(n => n.Distance);
        return best.Distance <= 0.35 ? best.Age : null;
    }
}
