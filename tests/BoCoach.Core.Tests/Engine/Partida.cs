using BoCoach.Core.BuildOrders;
using BoCoach.Core.Data;
using BoCoach.Core.Engine;

namespace BoCoach.Core.Tests.Engine;

/// <summary>Partida simulada para tests: genera snapshots como los mandaría Vision.</summary>
internal sealed class Partida
{
    public PlayerState State { get; } = new();
    public double Time { get; private set; }
    public int Population { get; private set; } = 4;
    public int PopulationCap { get; private set; } = 5;
    public ResourceAmounts Stock { get; set; } = PlanStart.Standard.Stock;

    /// <summary>Un evento del log, <paramref name="dt"/> segundos después del anterior. Las unidades suben la población.</summary>
    public Partida Event(string eventId, double dt = 10)
    {
        Time += dt;
        if (EventConventions.IsUnitCreated(eventId))
            Population++;
        if (eventId is "house_built" or "town_center_built")
            PopulationCap += 5;
        State.Apply(new GameSnapshot(Time, Stock, Population, PopulationCap, new GameEvent(eventId, Time)));
        return this;
    }

    public Partida Events(string eventId, int count, double dt = 10)
    {
        for (var i = 0; i < count; i++)
            Event(eventId, dt);
        return this;
    }

    /// <summary>Sube la población sin que el log muestre nada (por ejemplo, Vision no leyó el mensaje).</summary>
    public Partida PopulationGrows(int count, double dt = 10)
    {
        Time += dt;
        Population += count;
        State.Apply(new GameSnapshot(Time, Stock, Population, PopulationCap));
        return this;
    }

    public Partida Wait(double seconds)
    {
        Time += seconds;
        State.Apply(new GameSnapshot(Time, Stock, Population, PopulationCap));
        return this;
    }

    /// <summary>Juega los pasos indicados de la BO tal cual, emitiendo su evento de confirmación.</summary>
    public Partida Play(BuildOrder bo, int fromStep, int toStep, double dt = 10)
    {
        foreach (var step in bo.Steps.Skip(fromStep).Take(toStep - fromStep + 1))
        {
            if (EventConventions.EventFor(step) is { } e)
                Events(e, step.Do is StepAction.Train or StepAction.Build ? step.Count : 1, dt);
        }
        return this;
    }
}
