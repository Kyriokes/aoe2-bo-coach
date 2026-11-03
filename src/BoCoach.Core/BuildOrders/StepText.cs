using BoCoach.Core.Data;

namespace BoCoach.Core.BuildOrders;

/// <summary>
/// Textos para mostrar pasos y entidades (editor, overlay y web), en castellano (<see cref="Spanish"/>, el idioma por
/// defecto: la app y ReplaySession no pasan ninguno) o en inglés (<see cref="English"/>). Un idioma que no se conoce
/// se muestra en castellano.
/// </summary>
public static class StepText
{
    public const string Spanish = "es";
    public const string English = "en";

    /// <summary>Los idiomas que tienen diccionario.</summary>
    public static IReadOnlyList<string> Languages { get; } = [Spanish, English];

    private static readonly Dictionary<string, (string Singular, string Plural)> NamesEs = new()
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
        ["crop_rotation"] = ("Rotación de cultivos", "Rotación de cultivos"),
        ["two_man_saw"] = ("Sierra de dos manos", "Sierra de dos manos"),
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
        ["thumb_ring"] = ("Anillo del pulgar", "Anillo del pulgar"),
        ["parthian_tactics"] = ("Tácticas partas", "Tácticas partas"),
        ["heavy_cavalry_archer"] = ("Arquero a caballo pesado", "Arquero a caballo pesado"),
        ["bodkin_arrow"] = ("Flecha punzante", "Flecha punzante"),
        ["leather_archer_armor"] = ("Armadura de cuero para arqueros", "Armadura de cuero para arqueros"),
        ["ballistics"] = ("Balística", "Balística"),
        ["coinage"] = ("Acuñación", "Acuñación"),
    };

    /// <summary>Los nombres del juego en inglés (las tecnologías con mayúscula, como en el castellano).</summary>
    private static readonly Dictionary<string, (string Singular, string Plural)> NamesEn = new()
    {
        ["villager"] = ("villager", "villagers"),
        ["militia"] = ("militia", "militia"),
        ["spearman"] = ("spearman", "spearmen"),
        ["archer"] = ("archer", "archers"),
        ["skirmisher"] = ("skirmisher", "skirmishers"),
        ["scout_cavalry"] = ("scout", "scouts"),
        ["knight"] = ("knight", "knights"),
        ["monk"] = ("monk", "monks"),
        ["fishing_ship"] = ("fishing ship", "fishing ships"),
        ["trade_cart"] = ("trade cart", "trade carts"),
        ["mangonel"] = ("mangonel", "mangonels"),
        ["eagle_scout"] = ("eagle scout", "eagle scouts"),
        ["cavalry_archer"] = ("cavalry archer", "cavalry archers"),
        ["hand_cannoneer"] = ("hand cannoneer", "hand cannoneers"),
        ["camel_rider"] = ("camel rider", "camel riders"),
        ["battle_elephant"] = ("battle elephant", "battle elephants"),
        ["steppe_lancer"] = ("steppe lancer", "steppe lancers"),
        ["galley"] = ("galley", "galleys"),
        ["fire_galley"] = ("fire galley", "fire galleys"),
        ["demolition_raft"] = ("demolition raft", "demolition rafts"),
        ["transport_ship"] = ("transport ship", "transport ships"),
        ["battering_ram"] = ("battering ram", "battering rams"),
        ["scorpion"] = ("scorpion", "scorpions"),
        ["town_center"] = ("town center", "town centers"),
        ["house"] = ("house", "houses"),
        ["mill"] = ("mill", "mills"),
        ["lumber_camp"] = ("lumber camp", "lumber camps"),
        ["mining_camp"] = ("mining camp", "mining camps"),
        ["settlement"] = ("settlement", "settlements"),
        ["mule_cart"] = ("mule cart", "mule carts"),
        ["farm"] = ("farm", "farms"),
        ["dock"] = ("dock", "docks"),
        ["barracks"] = ("barracks", "barracks"),
        ["archery_range"] = ("archery range", "archery ranges"),
        ["stable"] = ("stable", "stables"),
        ["blacksmith"] = ("blacksmith", "blacksmiths"),
        ["market"] = ("market", "markets"),
        ["monastery"] = ("monastery", "monasteries"),
        ["university"] = ("university", "universities"),
        ["siege_workshop"] = ("siege workshop", "siege workshops"),
        ["palisade_wall"] = ("palisade wall", "palisade walls"),
        ["stone_wall"] = ("stone wall", "stone walls"),
        ["outpost"] = ("outpost", "outposts"),
        ["watch_tower"] = ("watch tower", "watch towers"),
        ["castle"] = ("castle", "castles"),
        ["loom"] = ("Loom", "Loom"),
        ["town_watch"] = ("Town Watch", "Town Watch"),
        ["wheelbarrow"] = ("Wheelbarrow", "Wheelbarrow"),
        ["hand_cart"] = ("Hand Cart", "Hand Cart"),
        ["double_bit_axe"] = ("Double-Bit Axe", "Double-Bit Axe"),
        ["bow_saw"] = ("Bow Saw", "Bow Saw"),
        ["horse_collar"] = ("Horse Collar", "Horse Collar"),
        ["heavy_plow"] = ("Heavy Plow", "Heavy Plow"),
        ["crop_rotation"] = ("Crop Rotation", "Crop Rotation"),
        ["two_man_saw"] = ("Two-Man Saw", "Two-Man Saw"),
        ["gold_mining"] = ("Gold Mining", "Gold Mining"),
        ["gold_shaft_mining"] = ("Gold Shaft Mining", "Gold Shaft Mining"),
        ["stone_mining"] = ("Stone Mining", "Stone Mining"),
        ["stone_shaft_mining"] = ("Stone Shaft Mining", "Stone Shaft Mining"),
        ["feudal_age"] = ("Feudal Age", "Feudal Age"),
        ["castle_age"] = ("Castle Age", "Castle Age"),
        ["imperial_age"] = ("Imperial Age", "Imperial Age"),
        ["man_at_arms"] = ("Man-at-Arms", "Man-at-Arms"),
        ["crossbowman"] = ("Crossbowman", "Crossbowman"),
        ["light_cavalry"] = ("Light Cavalry", "Light Cavalry"),
        ["fletching"] = ("Fletching", "Fletching"),
        ["forging"] = ("Forging", "Forging"),
        ["scale_mail_armor"] = ("Scale Mail Armor", "Scale Mail Armor"),
        ["scale_barding_armor"] = ("Scale Barding Armor", "Scale Barding Armor"),
        ["padded_archer_armor"] = ("Padded Archer Armor", "Padded Archer Armor"),
        ["bloodlines"] = ("Bloodlines", "Bloodlines"),
        ["husbandry"] = ("Husbandry", "Husbandry"),
        ["thumb_ring"] = ("Thumb Ring", "Thumb Ring"),
        ["parthian_tactics"] = ("Parthian Tactics", "Parthian Tactics"),
        ["heavy_cavalry_archer"] = ("Heavy Cavalry Archer", "Heavy Cavalry Archer"),
        ["bodkin_arrow"] = ("Bodkin Arrow", "Bodkin Arrow"),
        ["leather_archer_armor"] = ("Leather Archer Armor", "Leather Archer Armor"),
        ["ballistics"] = ("Ballistics", "Ballistics"),
        ["coinage"] = ("Coinage", "Coinage"),
    };

    private static Dictionary<string, (string Singular, string Plural)> NamesFor(string language) =>
        language == English ? NamesEn : NamesEs;

    private static bool IsEnglish(string language) => language == English;

    /// <summary>Si la entidad tiene nombre en ese idioma (sin nombre, sale la clave con espacios).</summary>
    public static bool HasName(string key, string language = Spanish) => NamesFor(language).ContainsKey(key);

    /// <summary>Nombre de una entidad en el idioma; si no hay traducción, la clave con espacios.</summary>
    public static string Name(string key, int count = 1, string language = Spanish) =>
        NamesFor(language).TryGetValue(key, out var n) ? (count == 1 ? n.Singular : n.Plural) : key.Replace('_', ' ');

    public static string Source(GatherSource source, string language = Spanish) => IsEnglish(language)
        ? source switch
        {
            GatherSource.Sheep => "sheep",
            GatherSource.Hunt => "hunt",
            GatherSource.Berries => "berries",
            GatherSource.Fish => "fish",
            GatherSource.Farm => "farms",
            GatherSource.Wood => "wood",
            GatherSource.Gold => "gold",
            _ => "stone",
        }
        : source switch
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

    public static string Action(StepAction action, string language = Spanish) => IsEnglish(language)
        ? action switch
        {
            StepAction.Train => "Train",
            StepAction.Build => "Build",
            StepAction.Research => "Research",
            _ => "Move",
        }
        : action switch
        {
            StepAction.Train => "Entrenar",
            StepAction.Build => "Construir",
            StepAction.Research => "Investigar",
            _ => "Mover",
        };

    /// <summary>
    /// Una línea que describe el paso, por ejemplo "Entrenar 6 aldeanos → ovejas" ("Train 6 villagers → sheep" en
    /// inglés). La web tiene una copia en TypeScript (<c>web/src/lib/stepText.ts</c>) atada por un test: si cambia
    /// algo acá, cambia allá.
    /// </summary>
    public static string Describe(Step step, string language = Spanish)
    {
        var en = IsEnglish(language);
        if (step.Do == StepAction.Move)
        {
            var to = step.Assign is { } t ? Source(t, language) : "?";
            if (step.From is null)
            {
                return en
                    ? $"Send {step.Count} idle {(step.Count == 1 ? "villager" : "villagers")} to {to}"
                    : $"Mandar {step.Count} {(step.Count == 1 ? "aldeano sin tarea" : "aldeanos sin tarea")} a {to}";
            }
            var from = Source(step.From.Value, language);
            return en ? $"Move {step.Count} from {from} to {to}" : $"Mover {step.Count} de {from} a {to}";
        }

        // Formato 2: la cantidad de un paso de aldeanos es el total en la fuente, como el contador del HUD.
        if (step is { Do: StepAction.Train, What: "villager", Assign: { } source })
        {
            return en
                ? $"{step.Count} {Name("villager", step.Count, language)} on {Source(source, language)}"
                : $"{step.Count} {Name("villager", step.Count, language)} en {Source(source, language)}";
        }

        var what = step.What is null ? "?" : Name(step.What, step.Count, language);
        var text = step.Do == StepAction.Research
            ? $"{Action(StepAction.Research, language)} {what}"
            : $"{Action(step.Do, language)} {step.Count} {what}";
        if (step.Do == StepAction.Build && step.Builders is > 1)
            text += en ? $" ({step.Builders} builders)" : $" ({step.Builders} constructores)";
        if (step.Assign is { } assign)
            text += $" → {Source(assign, language)}";
        if (step.AtPop is { } pop)
            text += en ? $" at {pop} pop" : $" a {pop} de población";
        return text;
    }
}
