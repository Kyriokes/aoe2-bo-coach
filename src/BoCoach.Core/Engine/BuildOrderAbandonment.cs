using BoCoach.Core.BuildOrders;
using BoCoach.Core.Data;
using static BoCoach.Core.Lang;

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
/// unos 2 minutos. Juntar lo que le falta al paso actual también es avanzar: en la partida 20261031-163414 el
/// jugador juntó para Castillos de 15:05 a 20:53 (con granjas, que el juego no avisa) y la BO quedaba abandonada a las
/// 18:05.</item>
/// <item>Gris: muchas unidades ajenas pero la de la BO sigue saliendo. Sigue la BO, pero lo militar no queda
/// atrasado.</item>
/// </list>
/// Hasta 5 unidades de reacción (piqueros en una BO de arqueros) no cuentan. Queda abandonada hasta que el jugador
/// la retoma (<see cref="Resume"/>, el atajo "Atrás", o sola cuando avanza un paso que delata que volvió, ver
/// <see cref="Returned"/>, o la BO termina): desde ahí las unidades ajenas y el tiempo se cuentan de nuevo.
/// </summary>
public sealed class BuildOrderAbandonment
{
    /// <summary>Unidades ajenas a la BO que se toleran (reacción al rival).</summary>
    public const int MaxForeignUnits = 5;

    /// <summary>Segundos sin producir la unidad de la BO para que las ajenas sean un cambio de build.</summary>
    public const double BoUnitPause = 60;

    /// <summary>Segundos sin que avance ningún paso de la BO para darla por abandonada.</summary>
    public const double MaxStall = 180;

    /// <summary>Cuánto tiene que bajar lo que falta para el paso actual para contar como avance (que no cuente un vaivén).</summary>
    private const double SavingStep = 10;

    /// <summary>Unidades que no son militares: no cambian la build.</summary>
    private static readonly IReadOnlySet<string> Economic = new HashSet<string>
    {
        "villager", "fishing_ship", "trade_cart", "trade_cog", "transport_ship", "merchant_ship",
    };

    private readonly IReadOnlySet<string> _boUnits;
    private readonly GameData? _data;
    private int _countFrom;
    private int? _progress;
    private double _progressAt;
    private double _leastMissing = double.PositiveInfinity;
    private int _returned;
    private HashSet<int> _started = [];

    /// <param name="data">Para saber qué cuenta como casa (el asentamiento de los incas); sin datos, solo la casa.</param>
    public BuildOrderAbandonment(BuildOrder bo, GameData? data = null)
    {
        _data = data;
        _boUnits = bo.Steps.Where(s => s.Do == StepAction.Train && s.What is { } what && !Economic.Contains(what))
            .Select(s => s.What!).ToHashSet();
    }

    /// <summary>Por qué se dio la BO por abandonada (para el overlay), o null si se sigue.</summary>
    public string? Reason { get; private set; }

    public bool Abandoned => Reason is not null;

    /// <summary>Muchas unidades ajenas pero la de la BO sigue saliendo: no se marca atrasado lo militar.</summary>
    public bool Gray { get; private set; }

    /// <param name="finished">
    /// La BO no tiene nada más que pedir (<see cref="Tracker.HasOpenSteps"/>): pueden quedar abiertos lo que quedó atrás,
    /// lo que el juego no avisa, la producción a medias o una investigación en curso, y eso nada lo cierra. Null = todos
    /// los pasos hechos o salteados.
    /// </param>
    /// <param name="missing">Lo que le falta juntar al paso actual (en total): cada vez que llega más abajo que nunca, avanza.</param>
    public void Update(PlayerState state, IReadOnlyList<StepProgress> steps, bool? finished = null, double missing = 0)
    {
        // Con la BO terminada no hay nada que abandonar: sigue su postura (partida 20261031-215314, hunos "Scouts → CA":
        // terminada a las 23:58, a las 24:38 quedaba abandonada por los arqueros a caballo que la BO no entrena).
        var done = finished ?? steps.All(s => s.State is StepState.Done or StepState.Skipped);
        var returned = Returned(steps);
        // Abandonada, si avanza un paso de la BO el jugador volvió a ella (partida 20261031-171509: abandonada a las
        // 16:44 por la herrería sin hacer, seguía así a las 31:18 con la herrería, Castillos y los centros urbanos hechos).
        if (Abandoned && (returned > _returned || done))
            Resume(state);
        if (!Abandoned)
            _returned = returned;
        // Solo cuenta lo nunca visto: lo hecho de un paso de aldeanos sale del HUD y baja y sube cuando un leñador va a
        // hacer una casa y vuelve. Un paso que empieza también es avanzar, aunque antes se haya deshecho otro clic.
        var progress = Progress(steps);
        var started = steps.Where(s => s.State == StepState.InProgress).Select(s => s.Index).ToHashSet();
        if (_progress is null || progress > _progress || !started.IsSubsetOf(_started))
            (_progress, _progressAt, _leastMissing) = (Math.Max(progress, _progress ?? 0), state.Time, double.PositiveInfinity);
        _started = started;
        if (missing > 0 && missing < _leastMissing - SavingStep)
            (_leastMissing, _progressAt) = (missing, state.Time);
        if (Abandoned)
            return;
        if (done)
        {
            Gray = false;
            return;
        }

        var foreign = ForeignUnits(state);
        var lastBoUnit = state.Events.Where(e => EventConventions.Parse(e.EventId) is { Action: StepAction.Train } unit && _boUnits.Contains(unit.What))
            .Select(e => e.Time).DefaultIfEmpty(double.NegativeInfinity).Max();
        var paused = state.Time - lastBoUnit >= BoUnitPause;
        Gray = foreign.Count > MaxForeignUnits && !paused;
        if (foreign.Count > MaxForeignUnits && paused)
        {
            var names = string.Join(", ", foreign.GroupBy(u => u).OrderByDescending(g => g.Count()).Select(g => StepText.Name(g.Key, 2)));
            Reason = _boUnits.Count == 0
                ? L($"hiciste {foreign.Count} unidades que la BO no tiene ({names})", $"you made {foreign.Count} units the BO doesn't have ({names})")
                : L($"hiciste {foreign.Count} unidades que la BO no tiene ({names}) y hace {BoUnitPause:0} s que no sale {string.Join(" ni ", _boUnits.Select(u => StepText.Name(u)))}",
                    $"you made {foreign.Count} units the BO doesn't have ({names}) and no {string.Join(" or ", _boUnits.Select(u => StepText.Name(u)))} for {BoUnitPause:0} s");
            return;
        }
        if (state.Time - _progressAt >= MaxStall)
            Reason = L($"hace {GameClock.Format(state.Time - _progressAt)} que no avanza ningún paso de la BO", $"no BO step has advanced for {GameClock.Format(state.Time - _progressAt)}");
    }

    /// <summary>El jugador retoma la BO: lo ajeno y el tiempo sin avanzar se cuentan desde ahora.</summary>
    public void Resume(PlayerState state)
    {
        Reason = null;
        Gray = false;
        _countFrom = state.Events.Count;
        _progressAt = state.Time;
    }

    /// <summary>"Atrás" deshizo un paso: rehacerlo también es avanzar, así que el avance se mide de nuevo desde acá.</summary>
    public void Rewound() => _progress = null;

    private List<string> ForeignUnits(PlayerState state) => state.Events.Skip(_countFrom)
        .Select(e => EventConventions.Parse(e.EventId))
        .Where(u => u is { Action: StepAction.Train } unit && !Economic.Contains(unit.What) && !_boUnits.Contains(unit.What))
        .Select(u => u!.Value.What)
        .ToList();

    /// <summary>Un número que crece con cada avance de un paso: lo hecho de cada uno y su estado.</summary>
    private static int Progress(IReadOnlyList<StepProgress> steps) => steps.Sum(s => s.Completed * 4 + s.State switch
    {
        StepState.Done => 2,
        StepState.Skipped => 3,
        _ => 0,
    });

    /// <summary>
    /// Lo que dice que el jugador volvió a la BO abandonada: lo terminado o lo que salió de un paso que no se hace igual
    /// sin la BO. No cuentan los aldeanos (el centro urbano los saca siga o no la BO), las casas (se hacen cuando las pide
    /// la población), lo que el juego no avisa (una granja queda hecha cuando sale cualquier aldeano posterior) ni un clic
    /// en curso (la baja del stock puede ser otra cosa: cuenta cuando termina).
    /// </summary>
    private int Returned(IReadOnlyList<StepProgress> steps) => steps
        .Where(s => Lanes.Of(s.Step) != Lane.Villagers && !Tracker.IsHouse(s.Step, _data)
                    && EventConventions.EventFor(s.Step) is { } id && StateBelief.CanObserve(id))
        .Sum(s => s.Completed + (s.State is StepState.Done or StepState.Skipped ? 1 : 0));
}
