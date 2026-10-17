using BoCoach.Core.Data;
using BoCoach.Core.Engine;

namespace BoCoach.Core.Economy;

/// <summary>
/// El centro urbano siempre produce: mientras no se llegue al máximo de aldeanos, cada centro urbano saca un aldeano
/// tras otro. Esto calcula la comida por segundo que eso consume y cuántos aldeanos en comida hacen falta para
/// sostenerla, sea cual sea la BO.
/// </summary>
public static class VillagerFloor
{
    /// <summary>Comida por segundo que consumen <paramref name="townCenters"/> centros urbanos sacando aldeanos sin parar.</summary>
    public static double FoodPerSecond(GameData data, CivBonuses civ, Age age, int townCenters)
    {
        if (townCenters <= 0 || !data.TryGet("villager", out var villager))
            return 0;
        var train = civ.Time(villager, age) / civ.WorkRate("town_center", age);
        return train > 0 ? townCenters * civ.Cost(villager, age).Food / train : 0;
    }

    /// <summary>Aldeanos en comida que juntan <paramref name="foodPerSecond"/>, con lo que junta uno por segundo.</summary>
    public static int FoodVillagers(double foodPerSecond, double ratePerVillager) =>
        foodPerSecond <= 0 || ratePerVillager <= 0 ? 0 : (int)Math.Ceiling(foodPerSecond / ratePerVillager - 1e-9);

    /// <summary>Centros urbanos del jugador: el inicial más los construidos (pasos hechos o avisos del log, lo que dé más).</summary>
    public static int TownCenters(Tracker tracker)
    {
        var steps = tracker.Steps.Count(s => s.Step is { Do: BuildOrders.StepAction.Build, What: "town_center" } && s.State == StepState.Done);
        var built = tracker.State.Events.Count(e => e.EventId == "town_center_built");
        return 1 + Math.Max(steps, built);
    }
}
