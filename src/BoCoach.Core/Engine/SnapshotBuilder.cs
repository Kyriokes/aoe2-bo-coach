using BoCoach.Core.Data;

namespace BoCoach.Core.Engine;

/// <summary>
/// Lo que se leyó de la pantalla en una captura, ya suavizado. null = no se pudo leer (región sin calibrar,
/// número cortado, pantalla de carga).
/// </summary>
/// <param name="NewEvents">Eventos del log que aparecieron en esta captura (ids de data/events.json).</param>
/// <param name="VillagersOn">Aldeanos en cada recurso según el HUD, o null si no se leyeron los cuatro.</param>
/// <param name="Queue">La cola de producción, o null si no se lee.</param>
/// <param name="Scores">El panel de puntajes, si se leyó en esta captura, o null.</param>
/// <param name="IdleVillagers">Aldeanos inactivos según el HUD, o null si no se leyó.</param>
public sealed record ScreenReading(
    double? Clock,
    double? Food,
    double? Wood,
    double? Gold,
    double? Stone,
    int? Villagers,
    int? Population,
    int? PopulationCap,
    IReadOnlyList<string> NewEvents,
    ResourceAmounts? VillagersOn = null,
    IReadOnlyList<QueueItem>? Queue = null,
    IReadOnlyList<PlayerScore>? Scores = null,
    int? IdleVillagers = null);

/// <summary>
/// Arma los <see cref="GameSnapshot"/> del motor a partir de las lecturas de pantalla, sobre la creencia del estado
/// (<see cref="StateBelief"/>, que fusiona el log, el tope, la cola, el puntaje y la población):
/// <list type="bullet">
/// <item>Sin reloj no hay snapshot (pantalla de carga, menú): lo creído en esa captura se guarda para el siguiente.</item>
/// <item>Un recurso que no se leyó conserva su último valor (al principio, el stock inicial).</item>
/// <item>Si no se ve la población "N/M", se estima: aldeanos + unidades militares creídas + el explorador inicial, y el
/// tope con lo que suman los edificios creídos (casas, centros urbanos). Nunca queda por debajo de la población.</item>
/// <item>Los eventos del snapshot son las completaciones de la creencia (lo que el log avisó y lo que las demás
/// fuentes delataron), con el tiempo de juego de la captura.</item>
/// <item>El tiempo cuenta desde que el jugador empieza a jugar, no desde el 00:00 del reloj: en la 20261017-163738 se
/// quedó 36 minutos quieto con los 3 aldeanos iniciales y todos los objetivos de la BO quedaban atrasados (ver
/// <see cref="StartOffset"/>).</item>
/// </list>
/// </summary>
/// <param name="civ">La civ con que se juega: su arranque (recursos y aldeanos iniciales) y la población que dan sus casas. null = valores base.</param>
public sealed class SnapshotBuilder(GameData data, CivBonuses? civ = null)
{
    private readonly PlanStart _start = PlanStart.For(civ ?? CivBonuses.None, data);
    private readonly StateBelief _belief = new(data, civ);

    private const int MaxPopulation = BuildOrders.BuildOrderValidator.MaxPopulation;

    private double _lastClock;
    private ResourceAmounts _stock = PlanStart.For(civ ?? CivBonuses.None, data).Stock;
    private int _population = PlanStart.For(civ ?? CivBonuses.None, data).Population;
    private int _cap = PlanStart.For(civ ?? CivBonuses.None, data).PopulationCap;

    /// <summary>El número del jugador en el panel de puntajes (de la pantalla de carga); si no se sabe, se deduce.</summary>
    public int? PlayerSlot { get => _belief.PlayerSlot; set => _belief.PlayerSlot = value; }

    /// <summary>Lo que se cree de la partida después de la última captura.</summary>
    public BeliefView Belief => _belief.View;

    /// <summary>Lo que terminó en el último snapshot, con la fuente que lo dijo (el registro de partida lo guarda).</summary>
    public IReadOnlyList<Completion> LastCompletions { get; private set; } = [];

    /// <summary>True si la última población salió de la estimación y no de la pantalla.</summary>
    public bool PopulationEstimated { get; private set; } = true;

    /// <summary>
    /// Segundos del reloj del juego que pasaron antes de que el jugador empezara a jugar: el tiempo de los snapshots
    /// (y de sus eventos) es el reloj menos esto. Se fija en la primera captura con acción (un aldeano más que los
    /// iniciales, el stock distinto del inicial, un evento del log o algo en la cola de producción). Si antes se lo
    /// vio quieto, el inicio es esa captura; si la primera captura ya tiene acción (el coach arrancó a mitad de
    /// partida), se descuentan los aldeanos entrenados y lo demás se toma por idle solo si es casi todo el reloj.
    /// Menos de <see cref="IdleThreshold"/> no cuenta: en una partida normal se actúa en los primeros segundos.
    /// Si después el reloj vuelve más de <see cref="ClockCorrection"/> por debajo de la hora con que se fijó, esa hora
    /// era mala y se vuelve a calcular con la de ahora (ver <see cref="GameTime"/>).
    /// </summary>
    public double StartOffset { get; private set; }

    public const double IdleThreshold = 60;

    /// <summary>
    /// Segundos que el reloj tiene que volver para atrás para tomarlo por la corrección de una hora mala (el suavizado
    /// tolera 1,5 s de vaivén): el snapshot lo marca (<see cref="GameSnapshot.ClockCorrected"/>) y, si quedó por debajo de
    /// la hora con que se fijó el inicio, el inicio se calcula de nuevo.
    /// </summary>
    public const double ClockCorrection = 5;

    /// <summary>A mitad de partida, parte del reloj que tiene que quedar sin explicar por los aldeanos para tomarla por idle.</summary>
    private const double MostlyIdle = 0.75;

    private bool _startKnown;
    private bool _sawIdle;
    private double _startClock;

    public GameSnapshot? Build(ScreenReading reading)
    {
        var now = reading.Clock ?? _lastClock;
        _belief.Observe(reading, now);
        var corrected = reading.Clock < _lastClock - ClockCorrection;
        if (reading.Clock is { } c)
            _lastClock = c;

        _stock = new ResourceAmounts(
            reading.Food ?? _stock.Food,
            reading.Wood ?? _stock.Wood,
            reading.Stone ?? _stock.Stone,
            reading.Gold ?? _stock.Gold);
        // Más aldeanos que el tope de población del juego es una lectura mala: se descarta.
        var readVillagers = reading.Villagers is { } read && read <= MaxPopulation ? read : (int?)null;
        var belief = _belief.View;

        if (reading.Population is { } population && reading.PopulationCap is { } cap)
        {
            (_population, _cap) = (population, cap);
            PopulationEstimated = false;
        }
        else if (belief.Villagers is { } villagers)
        {
            _population = villagers + belief.Military + 1;
            _cap = Math.Min(MaxPopulation, Math.Max(belief.BuiltCap, _population));
            PopulationEstimated = true;
        }

        if (reading.Clock is not { } clock)
            return null;
        var time = GameTime(clock, reading);
        LastCompletions = _belief.TakeNew();
        var events = LastCompletions.Select(c => new GameEvent(c.EventId, time)).ToList();
        return new GameSnapshot(time, _stock, _population, _cap, events)
        {
            Villagers = readVillagers,
            VillagersOn = reading.VillagersOn,
            Queue = reading.Queue,
            Scores = reading.Scores,
            IdleVillagers = reading.IdleVillagers,
            Belief = belief,
            ClockCorrected = corrected,
        };
    }

    /// <summary>
    /// El reloj llevado al tiempo de juego (ver <see cref="StartOffset"/>); 0 mientras el jugador no haya hecho nada.
    /// El reloj suavizado solo vuelve para atrás cuando corrige una hora mala que había aceptado (un cuadro tapando la
    /// hora al arrancar, "1:20:00" por "0:20:00"). Si el inicio se fijó con esa hora, se fija de nuevo con la buena: si
    /// no, el tiempo quedaba en 0 hasta que el reloj pasara la hora mala. Si la mala llegó después, el inicio sigue.
    /// </summary>
    private double GameTime(double clock, ScreenReading reading)
    {
        if (_startKnown && clock < _startClock - ClockCorrection)
            _startKnown = false;
        if (!_startKnown)
        {
            var villagers = _belief.View.Villagers ?? _start.IdleVillagers;
            var acted = villagers > _start.IdleVillagers
                || _stock != _start.Stock
                || _belief.HasNew
                || reading.Queue is { Count: > 0 };
            if (!acted)
            {
                _sawIdle = true;
                return 0;
            }
            (_startKnown, _startClock) = (true, clock);
            if (_sawIdle)
            {
                StartOffset = clock >= IdleThreshold ? clock : 0;
            }
            else
            {
                var trained = Math.Max(0, villagers - _start.IdleVillagers);
                var estimate = clock - trained * VillagerTime();
                StartOffset = estimate >= IdleThreshold && estimate >= clock * MostlyIdle ? estimate : 0;
            }
        }
        return Math.Max(0, clock - StartOffset);
    }

    private double VillagerTime() => data.TryGet("villager", out var villager) && villager.Time > 0 ? villager.Time : 25;
}
