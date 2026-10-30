using BoCoach.Core.BuildOrders;

namespace BoCoach.Core.Engine;

/// <summary>
/// Las líneas en las que el coach sigue la BO (spec 2026-10-30, coach por líneas paralelas). Cada una avanza por su
/// lado: una investigación sin recursos no frena a los aldeanos. El orden es el de los renglones del overlay.
/// </summary>
public enum Lane
{
    /// <summary>Entrenar aldeanos y moverlos.</summary>
    Villagers,

    /// <summary>Construir edificios (casas y granjas incluidas).</summary>
    Build,

    /// <summary>Investigar tecnologías y subir de edad.</summary>
    Research,

    /// <summary>Entrenar unidades que no son aldeanos.</summary>
    Produce,
}

public static class Lanes
{
    public static IReadOnlyList<Lane> All { get; } = [Lane.Villagers, Lane.Build, Lane.Research, Lane.Produce];

    /// <summary>La línea de un paso, por su acción.</summary>
    public static Lane Of(Step step) => step.Do switch
    {
        StepAction.Train when step.What == "villager" => Lane.Villagers,
        StepAction.Train => Lane.Produce,
        StepAction.Build => Lane.Build,
        StepAction.Research => Lane.Research,
        _ => Lane.Villagers,
    };
}
