using BoCoach.Core.Data;

namespace BoCoach.Core.BuildOrders;

/// <summary>Textos en español para mostrar pasos y entidades (editor y overlay).</summary>
public static class StepText
{
    private static readonly Dictionary<string, (string Singular, string Plural)> Names = new()
    {
        ["villager"] = ("aldeano", "aldeanos"),
        ["militia"] = ("milicia", "milicias"),
        ["spearman"] = ("lancero", "lanceros"),
        ["archer"] = ("arquero", "arqueros"),
        ["skirmisher"] = ("hostigador", "hostigadores"),
        ["scout_cavalry"] = ("explorador", "exploradores"),
        ["knight"] = ("caballero", "caballeros"),
        ["monk"] = ("monje", "monjes"),
        ["fishing_ship"] = ("barco pesquero", "barcos pesqueros"),
        ["trade_cart"] = ("carro mercante", "carros mercantes"),
        ["mangonel"] = ("mangonel", "mangoneles"),
        ["eagle_scout"] = ("explorador águila", "exploradores águila"),
        ["cavalry_archer"] = ("arquero a caballo", "arqueros a caballo"),
        ["hand_cannoneer"] = ("artillero manual", "artilleros manuales"),
        ["camel_rider"] = ("camello", "camellos"),
        ["battle_elephant"] = ("elefante de combate", "elefantes de combate"),
        ["steppe_lancer"] = ("lancero estepario", "lanceros esteparios"),
        ["galley"] = ("galera", "galeras"),
        ["fire_galley"] = ("galeota incendiaria", "galeotas incendiarias"),
        ["demolition_raft"] = ("balsa de demolición", "balsas de demolición"),
        ["transport_ship"] = ("barco de transporte", "barcos de transporte"),
        ["battering_ram"] = ("ariete", "arietes"),
        ["scorpion"] = ("escorpión", "escorpiones"),
        ["town_center"] = ("centro urbano", "centros urbanos"),
        ["house"] = ("casa", "casas"),
        ["mill"] = ("molino", "molinos"),
        ["lumber_camp"] = ("campamento maderero", "campamentos madereros"),
        ["mining_camp"] = ("campamento minero", "campamentos mineros"),
        ["settlement"] = ("asentamiento", "asentamientos"), // el Settlement de los incas
        ["mule_cart"] = ("carro de mulas", "carros de mulas"), // armenios y georgianos: vale por los campamentos
        ["farm"] = ("granja", "granjas"),
        ["dock"] = ("muelle", "muelles"),
        ["barracks"] = ("cuartel", "cuarteles"),
        ["archery_range"] = ("galería de tiro", "galerías de tiro"),
        ["stable"] = ("establo", "establos"),
        ["blacksmith"] = ("herrería", "herrerías"),
        ["market"] = ("mercado", "mercados"),
        ["monastery"] = ("monasterio", "monasterios"),
        ["university"] = ("universidad", "universidades"),
        ["siege_workshop"] = ("taller de asedio", "talleres de asedio"),
        ["palisade_wall"] = ("empalizada", "empalizadas"),
        ["stone_wall"] = ("muralla de piedra", "murallas de piedra"),
        ["outpost"] = ("puesto avanzado", "puestos avanzados"),
        ["watch_tower"] = ("torre de vigilancia", "torres de vigilancia"),
        ["castle"] = ("castillo", "castillos"),
        ["loom"] = ("Telar", "Telar"),
        ["town_watch"] = ("Guardia urbana", "Guardia urbana"),
        ["wheelbarrow"] = ("Carretilla", "Carretilla"),
        ["hand_cart"] = ("Carro de mano", "Carro de mano"),
        ["double_bit_axe"] = ("Hacha de doble filo", "Hacha de doble filo"),
        ["bow_saw"] = ("Sierra de arco", "Sierra de arco"),
        ["horse_collar"] = ("Collera", "Collera"),
        ["heavy_plow"] = ("Arado pesado", "Arado pesado"),
        ["gold_mining"] = ("Minería de oro", "Minería de oro"),
        ["gold_shaft_mining"] = ("Pozo de oro", "Pozo de oro"),
        ["stone_mining"] = ("Minería de piedra", "Minería de piedra"),
        ["stone_shaft_mining"] = ("Pozo de piedra", "Pozo de piedra"),
        ["feudal_age"] = ("Edad Feudal", "Edad Feudal"),
        ["castle_age"] = ("Edad de los Castillos", "Edad de los Castillos"),
        ["imperial_age"] = ("Edad Imperial", "Edad Imperial"),
        ["man_at_arms"] = ("Hombre de armas", "Hombre de armas"),
        ["crossbowman"] = ("Ballestero", "Ballestero"),
        ["light_cavalry"] = ("Caballería ligera", "Caballería ligera"),
        ["fletching"] = ("Emplumado", "Emplumado"),
        ["forging"] = ("Forja", "Forja"),
        ["scale_mail_armor"] = ("Cota de escamas", "Cota de escamas"),
        ["scale_barding_armor"] = ("Barda de escamas", "Barda de escamas"),
        ["padded_archer_armor"] = ("Armadura acolchada", "Armadura acolchada"),
        ["bloodlines"] = ("Linaje", "Linaje"),
        ["husbandry"] = ("Cría de caballos", "Cría de caballos"),
        ["coinage"] = ("Acuñación", "Acuñación"),
    };

    public static bool HasName(string key) => Names.ContainsKey(key);

    /// <summary>Nombre en español de una entidad; si no hay traducción, la clave con espacios.</summary>
    public static string Name(string key, int count = 1) =>
        Names.TryGetValue(key, out var n) ? (count == 1 ? n.Singular : n.Plural) : key.Replace('_', ' ');

    public static string Source(GatherSource source) => source switch
    {
        GatherSource.Sheep => "ovejas",
        GatherSource.Hunt => "caza",
        GatherSource.Berries => "bayas",
        GatherSource.Fish => "pesca",
        GatherSource.Farm => "granjas",
        GatherSource.Wood => "madera",
        GatherSource.Gold => "oro",
        _ => "piedra",
    };

    public static string Action(StepAction action) => action switch
    {
        StepAction.Train => "Entrenar",
        StepAction.Build => "Construir",
        StepAction.Research => "Investigar",
        _ => "Mover",
    };

    /// <summary>Una línea que describe el paso, por ejemplo "Entrenar 6 aldeanos → ovejas".</summary>
    public static string Describe(Step step)
    {
        if (step.Do == StepAction.Move)
        {
            if (step.From is null)
                return $"Mandar {step.Count} {(step.Count == 1 ? "aldeano sin tarea" : "aldeanos sin tarea")} a {(step.Assign is { } a ? Source(a) : "?")}";
            var from = Source(step.From.Value);
            var to = step.Assign is { } t ? Source(t) : "?";
            return $"Mover {step.Count} de {from} a {to}";
        }

        // Formato 2: la cantidad de un paso de aldeanos es el total en la fuente, como el contador del HUD.
        if (step is { Do: StepAction.Train, What: "villager", Assign: { } source })
            return $"{step.Count} {Name("villager", step.Count)} en {Source(source)}";

        var what = step.What is null ? "?" : Name(step.What, step.Count);
        var text = step.Do == StepAction.Research
            ? $"Investigar {what}"
            : $"{Action(step.Do)} {step.Count} {what}";
        if (step.Do == StepAction.Build && step.Builders is > 1)
            text += $" ({step.Builders} constructores)";
        if (step.Assign is { } assign)
            text += $" → {Source(assign)}";
        if (step.AtPop is { } pop)
            text += $" a {pop} de población";
        return text;
    }
}
