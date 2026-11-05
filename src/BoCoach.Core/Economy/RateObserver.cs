using BoCoach.Core.Data;
using BoCoach.Core.Engine;

namespace BoCoach.Core.Economy;

/// <summary>
/// Tasa de recolección observada por recurso en una ventana deslizante: tasa = Δstock / Δt, sin contar las bajas.
/// Una baja es un gasto: el juego cobra al encolar o al hacer clic, y el aviso del log llega al terminar (un aldeano, 25 s
/// después), así que los eventos no dicen nada del stock de ese momento: sumar el costo con el aviso eran +2 de comida por
/// segundo que nadie juntó. Lo juntado en el intervalo de una baja se pierde (poco: un segundo por compra).
/// Una suba imposible de recolectar en ese tiempo es una devolución (se cancelaron aldeanos encolados) y no cuenta.
/// </summary>
public sealed class RateObserver(double windowSeconds = 20)
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
    public void Add(PlayerState state) => Add(state.Time, state.Stock);

    public void Add(double time, ResourceAmounts stock)
    {
        // El reloj se lee en segundos enteros y se mira la pantalla más seguido: lo de la captura anterior del mismo
        // segundo entra en el intervalo siguiente. Si se pisara el stock acá, lo juntado entre las dos se perdería (a
        // 500 ms, la mitad de todo).
        if (time == _lastTime)
            return;
        if (_lastTime is { } last && time > last)
        {
            var delta = stock - _lastStock;
            var gathered = ResourceAmounts.Zero;
            var dt = time - last;
            foreach (var r in Enum.GetValues<Resource>())
            {
                var g = Math.Max(0, delta[r]);
                gathered = gathered.With(r, g > MaxGatherBurst + MaxGatherPerSecond * dt ? 0 : g);
            }
            _intervals.Enqueue((time, gathered, dt));
        }

        _lastTime = time;
        _lastStock = stock;
        while (_intervals.Count > 0 && _intervals.Peek().Time <= time - WindowSeconds)
            _intervals.Dequeue();
    }
}
