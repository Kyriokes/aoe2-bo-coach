namespace BoCoach.Core.Engine;

/// <summary>
/// Las mejoras de una unidad salen del mismo edificio en su lugar: investigados los ballesteros, la galería saca
/// ballesteros y no arqueros. Para la BO son la misma unidad (en la 20261030-203917, "6 unidades que la BO no tiene"
/// eran los ballesteros de una BO de arqueros). Las claves son las de game_data y de events.json.
/// </summary>
public static class UnitLines
{
    private static readonly Dictionary<string, string> BaseOf = new()
    {
        ["crossbowman"] = "archer",
        ["arbalester"] = "archer",
        ["man_at_arms"] = "militia",
        ["long_swordsman"] = "militia",
        ["two_handed_swordsman"] = "militia",
        ["champion"] = "militia",
        ["pikeman"] = "spearman",
        ["halberdier"] = "spearman",
        ["light_cavalry"] = "scout_cavalry",
        ["hussar"] = "scout_cavalry",
        ["winged_hussar"] = "scout_cavalry",
        ["elite_skirmisher"] = "skirmisher",
        ["imperial_skirmisher"] = "skirmisher",
        ["cavalier"] = "knight",
        ["paladin"] = "knight",
        ["heavy_camel_rider"] = "camel_rider",
        ["imperial_camel_rider"] = "camel_rider",
        ["heavy_cavalry_archer"] = "cavalry_archer",
        ["eagle_warrior"] = "eagle_scout",
        ["elite_eagle_warrior"] = "eagle_scout",
        ["elite_battle_elephant"] = "battle_elephant",
        ["elite_steppe_lancer"] = "steppe_lancer",
        ["war_galley"] = "galley",
        ["galleon"] = "galley",
        ["fast_fire_ship"] = "fire_galley",
        ["onager"] = "mangonel",
        ["siege_onager"] = "mangonel",
        ["heavy_scorpion"] = "scorpion",
        ["capped_ram"] = "battering_ram",
        ["siege_ram"] = "battering_ram",
    };

    /// <summary>La unidad base de una mejora ("crossbowman" → "archer"), o la misma clave.</summary>
    public static string Base(string unit) => BaseOf.GetValueOrDefault(unit, unit);

    /// <summary>"crossbowman_created" pasa a "archer_created"; los demás eventos quedan igual.</summary>
    public static GameEvent Normalize(GameEvent e) =>
        EventConventions.IsUnitCreated(e.EventId) && e.EventId[..^"_created".Length] is var unit && Base(unit) is var b && b != unit
            ? e with { EventId = b + "_created" }
            : e;
}
