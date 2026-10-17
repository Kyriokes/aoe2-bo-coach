using BoCoach.Core.Data;

namespace BoCoach.Core.Engine;

/// <summary>
/// Lo que se leyó de la pantalla en una captura, ya suavizado. null = no se pudo leer (región sin calibrar,
/// número cortado, pantalla de carga).
/// </summary>
/// <param name="NewEvents">Eventos del log que aparecieron en esta captura (ids de data/events.json).</param>
public sealed record ScreenReading(
    double? Clock,
    double? Food,
    double? Wood,
    double? Gold,
    double? Stone,
    int? Villagers,
    int? Population,
    int? PopulationCap,
    IReadOnlyList<string> NewEvents);

/// <summary>
/// Arma los <see cref="GameSnapshot"/> del motor a partir de las lecturas de pantalla:
/// <list type="bullet">
/// <item>Sin reloj no hay snapshot (pantalla de carga, menú): los eventos de esa captura se guardan para el siguiente.</item>
/// <item>Un recurso que no se leyó conserva su último valor (al principio, el stock inicial).</item>
/// <item>Si no se ve la población "N/M", se estima: aldeanos + unidades militares vistas en el log + el explorador
/// inicial, y el tope con lo que suman los edificios construidos (casas, centros urbanos). El log junta mensajes
/// repetidos, así que el tope estimado nunca queda por debajo de la población.</item>
/// <item>Si se ve, el tope delata las casas que el log no avisó (una segunda "House Built" mientras la primera sigue
/// siendo el último renglón no cambia nada en el log): una suba que ningún edificio avisado explica es una casa
/// terminada. Si el aviso llega después, no se cuenta de nuevo.</item>
/// </list>
/// </summary>
public sealed class SnapshotBuilder(GameData data)
{
    private const int StartingCap = 5;
    private const int MaxPopulation = BuildOrders.BuildOrderValidator.MaxPopulation;
    private const string House = "house";
    private const string HouseBuilt = "house_built";

    /// <summary>Segundos de partida que un edificio avisado espera a que el tope suba antes de olvidarlo.</summary>
    private const double CreditSeconds = 20;

    private readonly List<string> _pendingEvents = [];
    /// <summary>Si el tope no se leyó en este tiempo (segundos de partida), su próxima lectura es un punto de partida nuevo.</summary>
    private const double CapGapSeconds = 5;

    private readonly List<(double Time, int Pop)> _capCredits = [];
    private readonly List<double> _inferredHouses = [];
    private (int Cap, double Time)? _lastReadCap;
    private double _lastClock;
    private ResourceAmounts _stock = PlanStart.Standard.Stock;
    private int _military;
    private int _builtCap = StartingCap;
    private int _population = PlanStart.Standard.Population;
    private int _cap = PlanStart.Standard.PopulationCap;
    private int? _villagers;

    /// <summary>True si la última población salió de la estimación y no de la pantalla.</summary>
    public bool PopulationEstimated { get; private set; } = true;

    public GameSnapshot? Build(ScreenReading reading)
    {
        var now = reading.Clock ?? _lastClock;
        foreach (var id in reading.NewEvents)
        {
            // Una casa que el tope ya había delatado: el aviso del log llegó tarde y no es otra casa.
            _inferredHouses.RemoveAll(t => now - t > CreditSeconds);
            if (id == HouseBuilt && _inferredHouses.Count > 0)
            {
                _inferredHouses.RemoveAt(0);
                continue;
            }
            _pendingEvents.Add(id);
            if (EventConventions.IsUnitCreated(id) && id != EventConventions.VillagerCreated)
                _military++;
            if (EventConventions.Parse(id) is { Action: BuildOrders.StepAction.Build } built && data.TryGet(built.What, out var entity) && entity.PopBonus > 0)
            {
                _builtCap += entity.PopBonus;
                _capCredits.Add((now, entity.PopBonus));
            }
        }
        if (reading.PopulationCap is { } readCap)
            InferHousesFromCap(readCap, now);
        if (reading.Clock is { } c)
            _lastClock = c;

        _stock = new ResourceAmounts(
            reading.Food ?? _stock.Food,
            reading.Wood ?? _stock.Wood,
            reading.Stone ?? _stock.Stone,
            reading.Gold ?? _stock.Gold);
        // Más aldeanos que el tope de población del juego es una lectura mala: se descarta.
        var readVillagers = reading.Villagers is { } read && read <= MaxPopulation ? read : (int?)null;
        _villagers = readVillagers ?? _villagers;

        if (reading.Population is { } population && reading.PopulationCap is { } cap)
        {
            (_population, _cap) = (population, cap);
            PopulationEstimated = false;
        }
        else if (_villagers is { } villagers)
        {
            _population = villagers + _military + 1;
            _cap = Math.Min(MaxPopulation, Math.Max(_builtCap, _population));
            PopulationEstimated = true;
        }

        if (reading.Clock is not { } clock)
            return null;
        var events = _pendingEvents.Select(id => new GameEvent(id, clock)).ToList();
        _pendingEvents.Clear();
        return new GameSnapshot(clock, _stock, _population, _cap, events) { Villagers = readVillagers };
    }

    private void InferHousesFromCap(int cap, double now)
    {
        _capCredits.RemoveAll(c => now - c.Time > CreditSeconds);
        if (_lastReadCap is { } last && now - last.Time <= CapGapSeconds && cap > last.Cap)
        {
            var rise = cap - last.Cap;
            // Primero, lo que explican los edificios que el log avisó.
            while (rise > 0 && _capCredits.Count > 0)
            {
                var used = Math.Min(rise, _capCredits[0].Pop);
                rise -= used;
                if (used == _capCredits[0].Pop)
                    _capCredits.RemoveAt(0);
                else
                    _capCredits[0] = (_capCredits[0].Time, _capCredits[0].Pop - used);
            }
            var housePop = data.TryGet(House, out var house) && house.PopBonus > 0 ? house.PopBonus : 5;
            for (; rise >= housePop; rise -= housePop)
            {
                _pendingEvents.Add(HouseBuilt);
                _inferredHouses.Add(now);
                _builtCap += housePop;
            }
        }
        _lastReadCap = (cap, now);
    }
}
