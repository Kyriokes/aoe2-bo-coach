using BoCoach.Core.BuildOrders;

namespace BoCoach.Core.Engine;

/// <summary>
/// D1 "si no sabe, que se calle" (criterios del usuario, 2026-10-20): cuándo el jugador dejó la BO. Con la BO
/// abandonada, el coach deja de pedir los pasos viejos y se queda con lo que siempre vale (centro urbano produciendo,
/// casas, inactivos y la GD contra lo que hay).
/// <list type="bullet">
/// <item>Abandonada si produjo más de <see cref="MaxForeignUnits"/> unidades militares que la BO no tiene y hace
/// <see cref="BoUnitPause"/> segundos o más que no sale una unidad militar de la BO (o la BO no tiene ninguna).</item>
/// <item>Abandonada si pasan <see cref="MaxStall"/> segundos sin que avance ningún paso de la BO (uno hecho, uno
/// empezado, un aldeano o una unidad más de un paso). El usuario aprobó "3 minutos de atraso respecto del paso que la
/// BO esperaba", pero contra el plan del simulador (optimista: pone el clic de Feudal de la BO de scouts a las 7:30
/// con el objetivo en 9:00) la partida de scouts del 2026-10-15, siguiendo la BO a ritmo humano, quedaba abandonada a
/// las 13:05 esperando madera para el establo. Sin avanzar nada, en cambio, el hueco más largo de esa partida es de
/// unos 2 minutos.</item>
/// <item>Gris: muchas unidades ajenas pero la de la BO sigue saliendo. Sigue la BO, pero lo militar no queda
/// atrasado.</item>
/// </list>
/// Hasta 5 unidades de reacción (piqueros en una BO de arqueros) no cuentan. Queda abandonada hasta que el jugador
/// la retoma (<see cref="Resume"/>, el atajo "Atrás"): desde ahí las unidades ajenas y el tiempo se cuentan de nuevo.
/// </summary>
public sealed class BuildOrderAbandonment
{
    /// <summary>Unidades ajenas a la BO que se toleran (reacción al rival).</summary>
    public const int MaxForeignUnits = 5;

    /// <summary>Segundos sin producir la unidad de la BO para que las ajenas sean un cambio de build.</summary>
    public const double BoUnitPause = 60;

    /// <summary>Segundos sin que avance ningún paso de la BO para darla por abandonada.</summary>
    public const double MaxStall = 180;

    /// <summary>Unidades que no son militares: no cambian la build.</summary>
    private static readonly IReadOnlySet<string> Economic = new HashSet<string>
    {
        "villager", "fishing_ship", "trade_cart", "trade_cog", "transport_ship", "merchant_ship",
    };

    private readonly IReadOnlySet<string> _boUnits;
    private int _countFrom;
    private int? _progress;
    private double _progressAt;

    public BuildOrderAbandonment(BuildOrder bo)
    {
        _boUnits = bo.Steps.Where(s => s.Do == StepAction.Train && s.What is { } what && !Economic.Contains(what))
            .Select(s => s.What!).ToHashSet();
    }

    /// <summary>Por qué se dio la BO por abandonada (para el overlay), o null si se sigue.</summary>
    public string? Reason { get; private set; }

    public bool Abandoned => Reason is not null;

    /// <summary>Muchas unidades ajenas pero la de la BO sigue saliendo: no se marca atrasado lo militar.</summary>
    public bool Gray { get; private set; }

    public void Update(PlayerState state, IReadOnlyList<StepProgress> steps)
    {
        var progress = Progress(steps);
        if (_progress != progress)
            (_progress, _progressAt) = (progress, state.Time);
        if (Abandoned)
            return;

        var foreign = ForeignUnits(state);
        var lastBoUnit = state.Events.Where(e => EventConventions.Parse(e.EventId) is { Action: StepAction.Train } unit && _boUnits.Contains(unit.What))
            .Select(e => e.Time).DefaultIfEmpty(double.NegativeInfinity).Max();
        var paused = state.Time - lastBoUnit >= BoUnitPause;
        Gray = foreign.Count > MaxForeignUnits && !paused;
        if (foreign.Count > MaxForeignUnits && paused)
        {
            var names = string.Join(", ", foreign.GroupBy(u => u).OrderByDescending(g => g.Count()).Select(g => StepText.Name(g.Key, 2)));
            Reason = _boUnits.Count == 0
                ? $"hiciste {foreign.Count} unidades que la BO no tiene ({names})"
                : $"hiciste {foreign.Count} unidades que la BO no tiene ({names}) y hace {BoUnitPause:0} s que no sale {string.Join(" ni ", _boUnits.Select(u => StepText.Name(u)))}";
            return;
        }
        var finished = steps.All(s => s.State is StepState.Done or StepState.Skipped);
        if (!finished && state.Time - _progressAt >= MaxStall)
            Reason = $"hace {GameClock.Format(state.Time - _progressAt)} que no avanza ningún paso de la BO";
    }

    /// <summary>El jugador retoma la BO: lo ajeno y el tiempo sin avanzar se cuentan desde ahora.</summary>
    public void Resume(PlayerState state)
    {
        Reason = null;
        Gray = false;
        _countFrom = state.Events.Count;
        _progressAt = state.Time;
    }

    private List<string> ForeignUnits(PlayerState state) => state.Events.Skip(_countFrom)
        .Select(e => EventConventions.Parse(e.EventId))
        .Where(u => u is { Action: StepAction.Train } unit && !Economic.Contains(unit.What) && !_boUnits.Contains(unit.What))
        .Select(u => u!.Value.What)
        .ToList();

    /// <summary>Un número que crece con cada avance de un paso: lo hecho de cada uno y su estado.</summary>
    private static int Progress(IReadOnlyList<StepProgress> steps) => steps.Sum(s => s.Completed * 4 + s.State switch
    {
        StepState.InProgress => 1,
        StepState.Done => 2,
        StepState.Skipped => 3,
        _ => 0,
    });
}
