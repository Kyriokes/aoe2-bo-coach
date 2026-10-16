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
/// </list>
/// </summary>
public sealed class SnapshotBuilder(GameData data)
{
    private const int StartingCap = 5;

    private readonly List<string> _pendingEvents = [];
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
        foreach (var id in reading.NewEvents)
        {
            _pendingEvents.Add(id);
            if (EventConventions.IsUnitCreated(id) && id != EventConventions.VillagerCreated)
                _military++;
            if (EventConventions.Parse(id) is { Action: BuildOrders.StepAction.Build } built && data.TryGet(built.What, out var entity))
                _builtCap += entity.PopBonus;
        }

        _stock = new ResourceAmounts(
            reading.Food ?? _stock.Food,
            reading.Wood ?? _stock.Wood,
            reading.Stone ?? _stock.Stone,
            reading.Gold ?? _stock.Gold);
        _villagers = reading.Villagers ?? _villagers;

        if (reading.Population is { } population && reading.PopulationCap is { } cap)
        {
            (_population, _cap) = (population, cap);
            PopulationEstimated = false;
        }
        else if (_villagers is { } villagers)
        {
            _population = villagers + _military + 1;
            _cap = Math.Clamp(_builtCap, _population, 200);
            PopulationEstimated = true;
        }

        if (reading.Clock is not { } clock)
            return null;
        var events = _pendingEvents.Select(id => new GameEvent(id, clock)).ToList();
        _pendingEvents.Clear();
        return new GameSnapshot(clock, _stock, _population, _cap, events) { Villagers = reading.Villagers };
    }
}
