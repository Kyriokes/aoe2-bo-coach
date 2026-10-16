using BoCoach.Core.Data;
using BoCoach.Core.Engine;

namespace BoCoach.Core.Economy;

/// <summary>
/// Tasa de recolección observada por recurso en una ventana deslizante: tasa = (Δstock + gasto) / Δt.
/// El gasto de cada intervalo es el costo de los eventos que llegaron en él; si el stock bajó más de lo que
/// explican los eventos (por ejemplo, una tecnología que el log no muestra), se toma la baja como gasto.
/// Una suba imposible de recolectar en ese tiempo es una devolución (se cancelaron aldeanos encolados) y no cuenta.
/// </summary>
public sealed class RateObserver(GameData data, double windowSeconds = 20)
{
    /// <summary>
    /// Lo máximo que se puede recolectar de un recurso: varias descargas juntas (la caza trae 35 por aldeano) más
    /// un ritmo alto sostenido. Cancelar 5 aldeanos devuelve 250 de golpe.
    /// </summary>
    public const double MaxGatherBurst = 120;
    public const double MaxGatherPerSecond = 30;

    private readonly Queue<(double Time, ResourceAmounts Gathered, double Dt)> _intervals = new();
    private double? _lastTime;
    private ResourceAmounts _lastStock;

    public double WindowSeconds { get; } = windowSeconds;

    /// <summary>Segundos de datos dentro de la ventana.</summary>
    public double Coverage => _intervals.Sum(i => i.Dt);

    /// <summary>Recurso por segundo recolectado en la ventana.</summary>
    public ResourceAmounts Rates
    {
        get
        {
            var covered = Coverage;
            if (covered <= 0)
                return ResourceAmounts.Zero;
            var total = _intervals.Aggregate(ResourceAmounts.Zero, (sum, i) => sum + i.Gathered);
            return total * (1 / covered);
        }
    }

    /// <summary>Suma el último snapshot del jugador (llamar después de <see cref="PlayerState.Apply"/>).</summary>
    public void Add(PlayerState state)
    {
        var newEvents = state.Events.Skip(state.Events.Count - state.LastAdded);
        Add(state.Time, state.Stock, newEvents);
    }

    public void Add(double time, ResourceAmounts stock, IEnumerable<GameEvent> newEvents)
    {
        if (_lastTime is { } last && time > last)
        {
            var spent = newEvents.Aggregate(ResourceAmounts.Zero, (sum, e) => sum + CostOf(e.EventId));
            var delta = stock - _lastStock;
            var gathered = ResourceAmounts.Zero;
            var dt = time - last;
            foreach (var r in Enum.GetValues<Resource>())
            {
                var g = Math.Max(0, delta[r] + spent[r]);
                gathered = gathered.With(r, g > MaxGatherBurst + MaxGatherPerSecond * dt ? 0 : g);
            }
            _intervals.Enqueue((time, gathered, dt));
        }

        _lastTime = time;
        _lastStock = stock;
        while (_intervals.Count > 0 && _intervals.Peek().Time <= time - WindowSeconds)
            _intervals.Dequeue();
    }

    private ResourceAmounts CostOf(string eventId) =>
        EventConventions.Parse(eventId) is { } parsed && data.TryGet(parsed.What, out var entity)
            ? entity.Cost
            : ResourceAmounts.Zero;
}
