using BoCoach.Core.Data;

namespace BoCoach.Core.Engine;

/// <summary>
/// Lo acumulado de la partida a partir de los snapshots: último stock, población y todos los eventos vistos.
/// Si la población sube y el log no mostró qué unidad salió, se asume un aldeano (evento sintético);
/// si el evento real llega después, se descarta para no contarlo dos veces.
/// </summary>
public sealed class PlayerState
{
    /// <summary>Margen para emparejar un evento real con uno sintético ya creado (el log puede aparecer con atraso).</summary>
    public const double MatchWindowSeconds = 8;

    private readonly List<GameEvent> _events = [];
    private int _lastPopulation;

    public PlayerState(int startingPopulation = 4, int startingPopulationCap = 5)
    {
        Population = _lastPopulation = startingPopulation;
        PopulationCap = startingPopulationCap;
    }

    public double Time { get; private set; }
    public ResourceAmounts Stock { get; private set; } = PlanStart.Standard.Stock;
    public int Population { get; private set; }
    public int PopulationCap { get; private set; }

    /// <summary>Eventos en orden de llegada, incluidos los sintéticos.</summary>
    public IReadOnlyList<GameEvent> Events => _events;

    /// <summary>Cuántos eventos nuevos se agregaron en el último <see cref="Apply"/>.</summary>
    public int LastAdded { get; private set; }

    public void Apply(GameSnapshot snapshot)
    {
        var before = _events.Count;
        Time = snapshot.Time;
        Stock = snapshot.Stock;
        PopulationCap = snapshot.PopulationCap;

        // Las unidades de este snapshot explican primero la población que subió ahora; las que sobran
        // son eventos atrasados de una suba anterior, que ya se había contado como aldeano sintético.
        var growth = Math.Max(0, snapshot.Population - _lastPopulation);
        var unitsSeen = 0;
        foreach (var e in snapshot.Events)
        {
            if (EventConventions.IsUnitCreated(e.EventId) && ++unitsSeen > growth && TryReplaceSynthetic(e))
                continue;
            _events.Add(e);
        }

        for (var i = unitsSeen; i < growth; i++)
            _events.Add(new GameEvent(EventConventions.VillagerCreated, snapshot.Time, Synthetic: true));

        Population = _lastPopulation = snapshot.Population;
        LastAdded = _events.Count - before;
    }

    public int Count(string eventId) => _events.Count(e => e.EventId == eventId);

    /// <summary>
    /// Un evento real de unidad que llega después de que la población ya subió: si hay un aldeano sintético
    /// reciente, el real lo reemplaza (mismo lugar en la lista) en vez de sumarse.
    /// </summary>
    private bool TryReplaceSynthetic(GameEvent real)
    {
        var index = _events.FindLastIndex(e => e.Synthetic && real.Time - e.Time <= MatchWindowSeconds);
        if (index < 0)
            return false;
        _events[index] = real with { Time = _events[index].Time };
        return true;
    }
}
