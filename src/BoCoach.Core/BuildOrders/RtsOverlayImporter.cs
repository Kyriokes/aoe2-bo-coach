using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using BoCoach.Core.Data;
using BoCoach.Core.Economy;

namespace BoCoach.Core.BuildOrders;

/// <summary>Una BO convertida desde otro formato, con lo que no se pudo convertir o no cierra con la original.</summary>
public sealed record ImportResult(BuildOrder BuildOrder, IReadOnlyList<string> Warnings);

/// <summary>
/// Convierte las BO del formato de RTS Overlay (el exportable de buildorderguide.com): cada paso trae el total de
/// aldeanos, cuántos hay en cada recurso y notas con íconos ("Create 4 @resource/MaleVillDE.jpg@ on
/// @resource/Aoe2de_wood.png@", "@resource/Aoe2de_hammer.png@ 2 archeryRange", "Research doubleBitAxe",
/// "Click up to feudalAge"). Las notas se leen por los íconos y las palabras clave; lo que no se entiende queda como
/// nota del paso y en los avisos.
/// <para>
/// Esas guías dan por sabidas cosas que el coach necesita para planear y confirmar con el log, así que se agregan:
/// las 2 casas de los 3 aldeanos iniciales, el campamento maderero, el molino y el campamento minero la primera vez
/// que alguien va a esos recursos, las casas que hacen falta para que los aldeanos no se queden sin población, y una
/// granja por cada aldeano que va a granjas.
/// </para>
/// </summary>
public static partial class RtsOverlayImporter
{
    /// <summary>Si el JSON parece de RTS Overlay (tiene la lista "build_order").</summary>
    public static bool IsRtsOverlay(string json)
    {
        try
        {
            return JsonNode.Parse(json, documentOptions: Options) is JsonObject obj && obj["build_order"] is JsonArray;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static ImportResult Convert(string json, GameData data)
    {
        JsonObject root;
        try
        {
            root = JsonNode.Parse(json, documentOptions: Options) as JsonObject
                ?? throw new BuildOrderFormatException("La build order tiene que ser un objeto JSON.");
        }
        catch (JsonException e)
        {
            throw new BuildOrderFormatException($"JSON inválido (línea {e.LineNumber + 1}): {e.Message}", e);
        }
        if (root["build_order"] is not JsonArray guide || guide.Count == 0)
            throw new BuildOrderFormatException("No tiene pasos ('build_order').");
        return new Converter(data, Civ(Text(root["civilization"]), data)).Run(root, guide);
    }

    private static readonly JsonDocumentOptions Options = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    /// <summary>
    /// Arma la BO paso a paso con lo que las guías dan por sabido. Lo usan este importador (que lee notas de texto)
    /// y <see cref="BuildOrderGuideImporter"/> (que lee el formato estructurado de buildorderguide.com).
    /// </summary>
    internal sealed class Converter(GameData data, string civ = "any")
    {
        private readonly List<Step> _steps = [];
        private readonly List<string> _warnings = [];
        private readonly Dictionary<GatherSource, int> _on = Enum.GetValues<GatherSource>().ToDictionary(s => s, _ => 0);
        private readonly HashSet<string> _camps = [];
        private readonly HashSet<string> _forAge = [];
        private int _villagers = Engine.PlanStart.Standard.IdleVillagers;
        private int _military;
        private int _cap;
        private int _groups;
        private string? _pendingNote;
        private VillagerDistribution? _atFeudal;
        private VillagerDistribution? _atCastle;

        public ImportResult Run(JsonObject root, JsonArray guide)
        {
            Opening(guide);
            for (var i = 0; i < guide.Count; i++)
            {
                if (guide[i] is not JsonObject step)
                    continue;
                var notes = (step["notes"] as JsonArray ?? []).Select(n => n?.GetValue<string>() ?? "").ToList();
                var created = Number(step["villager_count"]) is { } count ? count - _villagers : (int?)null;
                var resources = step["resources"] as JsonObject;
                foreach (var note in notes)
                    Note(note, i + 1, ref created);
                EndStep(i + 1, Number(step["villager_count"]), resources);
            }

            var source = Text(root["source"]);
            var description = string.Join(" ", new[] { Text(root["description"])?.Replace('\n', ' '), source is null ? null : $"Fuente: {source}." }.Where(t => t is not null));
            return Finish(Text(root["name"]) ?? "BO importada", Civ(Text(root["civilization"])), Text(root["author"]), description);
        }

        /// <summary>Cierra un paso de la guía: lo compara con los aldeanos que dice la guía y guarda el reparto de los clics de edad.</summary>
        internal void EndStep(int guideStep, int? villagerCount, JsonObject? resources)
        {
            Check(guideStep, villagerCount, resources);
            if (_steps.Count > 0 && _pendingNote is not null)
                AppendNote(_steps[^1], _pendingNote);
            _pendingNote = null;
            if (resources is not null && _steps.Any(s => s is { Do: StepAction.Research, What: "feudal_age" }) && _atFeudal is null)
                _atFeudal = Distribution(resources);
            if (resources is not null && _steps.Any(s => s is { Do: StepAction.Research, What: "castle_age" }) && _atCastle is null)
                _atCastle = Distribution(resources);
        }

        internal ImportResult Finish(string name, string civ, string? author, string description)
        {
            for (var i = 0; i < _steps.Count; i++)
                _steps[i].Id = $"s{i + 1}";
            var bo = new BuildOrder
            {
                Id = "",
                Name = name,
                Civ = civ,
                Author = author,
                Description = description,
                Tags = ["importada"],
                Steps = _steps,
                Targets = _atFeudal is null && _atCastle is null ? null : new Targets { VillagersAtFeudal = _atFeudal, VillagersAtCastle = _atCastle },
            };
            return new ImportResult(bo, _warnings);
        }

        /// <summary>Los aldeanos que lleva la conversión (los 3 iniciales incluidos).</summary>
        internal int VillagerCount => _villagers;

        internal int On(GatherSource source) => _on[source];

        internal bool HasCamp(string camp) => _camps.Contains(camp);

        internal bool Has(StepAction action, string what) => _steps.Any(s => s.Do == action && s.What == what);

        /// <summary>Una nota del autor para el paso siguiente; con <paramref name="warning"/>, también un aviso.</summary>
        internal void Remark(string note, string? warning = null)
        {
            _pendingNote = _pendingNote is null ? note : $"{_pendingNote}. {note}";
            if (warning is not null)
                _warnings.Add(warning);
        }

        internal void Warn(string warning) => _warnings.Add(warning);

        /// <summary>Clic de edad o investigación.</summary>
        internal void Research(string key)
        {
            AgeBuildings(key);
            Add(new Step { Do = StepAction.Research, What = key, AtPop = key.EndsWith("_age", StringComparison.Ordinal) ? Population : null });
        }

        /// <summary>
        /// Los 2 edificios que pide la edad, si la guía no los hizo antes del clic (la de lituanos sube a Castillos solo con
        /// el establo; la de bizantinos hace la universidad después del clic en Imperial): los más baratos que falten. El
        /// juego no deja hacer clic sin ellos terminados. Si la guía lo construye después, ese no se repite.
        /// </summary>
        private void AgeBuildings(string tech)
        {
            if (BuildOrderValidator.AgeUpRequirement(tech) is not { } req || data.Civ(civ).NoAgeBuildings)
                return;
            while (BuildOrderValidator.AgeUpProgress(tech, BuiltKeys()) < 2)
            {
                var built = BuiltKeys();
                var cheapest = req.Buildings
                    .Where(b => b is not ("dock" or "castle") && !built.Contains(b) && data.CanMake(civ, b) && data.TryGet(b, out _))
                    .OrderBy(b => (data.Civ(civ).Requires(data.Get(b)) is { } r && !built.Contains(r) ? 1 : 0, Total(data.Civ(civ).Cost(data.Get(b), req.Requires))))
                    .FirstOrDefault();
                if (cheapest is null)
                    return;
                Build(1, cheapest, $"Agregado al importar: {StepText.Name(tech)} pide 2 edificios de la edad");
                _forAge.Add(cheapest);
            }
        }

        private static double Total(ResourceAmounts cost) => cost.Food + cost.Wood + cost.Gold + cost.Stone;

        private HashSet<string> BuiltKeys() =>
            _steps.Where(s => s is { Do: StepAction.Build, What: not null })
                .SelectMany(s => data.TryGet(s.What!, out var e) ? e.CountsAs.Prepend(s.What!) : [s.What!]).ToHashSet();

        /// <summary>Unidades que no son aldeanos, con las casas que hagan falta.</summary>
        internal void Train(Entity unit, int count)
        {
            var pop = count * Math.Max(1, unit.Pop);
            HousesFor(_villagers, pop);
            Add(new Step { Do = StepAction.Train, What = unit.Key, Count = count });
            _military += pop;
        }

        internal bool TryEntity(string key, out Entity entity) => data.TryGet(key, out entity!);

        private void Opening(JsonArray guide) => Begin(guide.OfType<JsonObject>()
            .SelectMany(s => s["notes"] as JsonArray ?? [])
            .Select(n => Tokenize(n?.GetValue<string>() ?? ""))
            .Select(t => Words(t).Contains("create") ? SourceAfter(t, "on") : null)
            .FirstOrDefault(s => s is not null) ?? GatherSource.Sheep);

        /// <summary>
        /// Los 3 aldeanos iniciales hacen 2 casas y van a donde van los primeros aldeanos (casi siempre ovejas). En una
        /// civ sin casas (hunos) van directo. Los aldeanos de más del arranque de la civ (chinos, mayas) ya están en las
        /// ovejas, como en <see cref="Engine.PlanStart.For"/>.
        /// </summary>
        internal void Begin(GatherSource first)
        {
            var extra = data.Civ(civ).Start.ExtraVillagers;
            _villagers += extra;
            _on[GatherSource.Sheep] += extra;
            _on[first] += Engine.PlanStart.Standard.IdleVillagers;
            if (NoHouses)
            {
                _cap = BuildOrderValidator.MaxPopulation;
                _steps.Add(new Step
                {
                    Do = StepAction.Move, Count = Engine.PlanStart.Standard.IdleVillagers, Assign = first,
                    Note = "Los 3 aldeanos iniciales van directo: la civ no necesita casas (agregado al importar)",
                });
                return;
            }
            _cap = TownCenterPop + 2 * HousePop;
            _steps.Add(new Step
            {
                Do = StepAction.Build, What = "house", Count = 2, Builders = 3, Assign = first,
                Note = "Los 3 aldeanos iniciales hacen 2 casas (agregado al importar)",
            });
        }

        /// <summary>La civ no hace casas (los hunos): la población no tiene tope y las casas de la guía no van.</summary>
        private bool NoHouses => data.Civ(civ).Start.NoHouses || !data.CanMake(civ, "house");

        /// <summary>
        /// Lo que hace la civ en lugar de un campamento o un molino que no tiene: el carro de mulas (armenios y
        /// georgianos, por los campamentos) o el asentamiento (incas y sudamericanos, también por el molino); null si nada.
        /// </summary>
        private string? ForCiv(string building)
        {
            if (data.CanMake(civ, building))
                return building;
            if (building is "lumber_camp" or "mining_camp" && data.CanMake(civ, "mule_cart") && data.TryGet("mule_cart", out _))
                return "mule_cart";
            if (building is "lumber_camp" or "mining_camp" or "mill" && data.CanMake(civ, "settlement") && data.TryGet("settlement", out _))
                return "settlement";
            return building == "house" ? null : building; // lo demás queda: el validador dice que la civ no lo tiene
        }

        private void Note(string note, int guideStep, ref int? created)
        {
            var tokens = Tokenize(note);
            var words = Words(tokens);
            var numbers = tokens.Where(t => t.Number is not null).Select(t => t.Number!.Value).ToList();
            // Separadores ("-------"): sin palabras ni íconos, no son una acción ni una nota.
            if (tokens.Count == 0)
                return;
            // Entre paréntesis van consejos ("(2 aldeanos hacen el mercado, 1 la herrería)"): repiten acciones de la
            // nota anterior y leídas como acciones duplicaban edificios.
            if (note.TrimStart().StartsWith('('))
            {
                _pendingNote = _pendingNote is null ? Clean(note) : $"{_pendingNote}. {Clean(note)}";
                return;
            }

            if (words.Contains("create") && SourceAfter(tokens, "on") is { } to)
            {
                var n = numbers.FirstOrDefault(1);
                // "Create 6 on sheep" en el primer paso cuenta a los 3 iniciales, que ya están ahí.
                if (created is { } expected && n > expected)
                    n = Math.Max(0, expected);
                created = created - n;
                Villagers(n, to);
                return;
            }
            if (words.Contains("move") && SourceAfter(tokens, "from") is { } from && SourceAfter(tokens, "to") is { } destination)
            {
                Move(numbers.FirstOrDefault(1), from, destination);
                return;
            }
            if (words.Contains("click") || words.Contains("age"))
            {
                if (Entities(tokens).FirstOrDefault(e => e.Kind == EntityKind.Tech && e.Key.EndsWith("_age", StringComparison.Ordinal)) is { } ageUp)
                {
                    Research(ageUp.Key);
                    return;
                }
            }
            if (tokens.Any(t => t.Icon == Hammer) || words.Contains("build"))
            {
                var any = false;
                int? count = null;
                foreach (var token in tokens)
                {
                    if (token.Number is { } c)
                        count = c;
                    else if (Entity(token) is { Kind: EntityKind.Building } building)
                    {
                        Build(count ?? 1, building.Key);
                        (count, any) = (null, true);
                    }
                }
                if (any)
                    return;
            }
            if (tokens.Any(t => t.Text == "-->"))
            {
                // "50% @age@ --> @barracks@": el edificio a la mitad de la investigación.
                var after = tokens.SkipWhile(t => t.Text != "-->").Skip(1).ToList();
                if (after.Select(Entity).FirstOrDefault(e => e is { Kind: EntityKind.Building }) is { } building)
                {
                    Build(1, building.Key, Clean(note));
                    return;
                }
            }
            if (words.Contains("research"))
            {
                var techs = Entities(tokens).Where(e => e.Kind == EntityKind.Tech).ToList();
                foreach (var tech in techs)
                    Research(tech.Key);
                if (techs.Count > 0)
                    return;
            }
            if (words.Contains("train"))
            {
                int? count = null;
                var any = false;
                foreach (var token in tokens)
                {
                    if (token.Number is { } c)
                        count = c;
                    else if (Entity(token) is { Kind: EntityKind.Unit } unit && unit.Key != "villager")
                    {
                        Train(unit, count ?? 1);
                        (count, any) = (null, true);
                    }
                }
                if (any)
                    return;
            }
            // "Before Feudal" / "In Feudal": solo ordenan la guía.
            if ((words.Contains("before") || words.Contains("in")) && tokens.Count(t => t.Text.Length > 0) <= 2)
                return;

            _pendingNote = _pendingNote is null ? Clean(note) : $"{_pendingNote}. {Clean(note)}";
            if (!LooksLikeAdvice(words) && !note.Contains('→')) // "FC → Boom": un título, no una acción
                _warnings.Add($"Paso {guideStep} de la guía: no entendí \"{Clean(note)}\" (quedó como nota).");
        }

        /// <summary>Notas que son consejos y no acciones ("Lure boar"): van como nota sin aviso.</summary>
        private static bool LooksLikeAdvice(IReadOnlyCollection<string> words) =>
            words.Contains("lure") || words.Contains("%");

        internal void Villagers(int count, GatherSource to)
        {
            if (count <= 0)
                return;
            HousesFor(_villagers + count);
            if (to == GatherSource.Farm)
            {
                // Cada uno hace su granja: los aldeanos nuevos a granjas y las granjas hechas por ellos, en un solo paso.
                var group = $"granjas{++_groups}";
                Add(new Step { Do = StepAction.Train, What = "villager", Count = _on[to] + count, Assign = to, Group = group });
                Add(new Step { Do = StepAction.Build, What = "farm", Count = count, From = to, Assign = to, Group = group });
            }
            else
            {
                var step = Add(new Step { Do = StepAction.Train, What = "villager", Count = _on[to] + count, Assign = to });
                Camp(to, from: to, with: step);
            }
            _on[to] += count;
            _villagers += count;
        }

        internal void Move(int count, GatherSource from, GatherSource to)
        {
            if (from == to)
                return;
            // Las guías llaman "jabalí" a todos los que están en animales: lo que falta en uno sale del otro.
            if (_on[from] < count && from is GatherSource.Hunt or GatherSource.Sheep)
            {
                var other = from == GatherSource.Hunt ? GatherSource.Sheep : GatherSource.Hunt;
                var rest = other == to ? 0 : Math.Min(count - _on[from], _on[other]);
                if (rest > 0)
                    Move(rest, other, to);
                count -= rest;
            }
            count = Math.Min(count, _on[from]);
            if (count <= 0)
                return;
            if (to == GatherSource.Farm)
            {
                Add(new Step { Do = StepAction.Build, What = "farm", Count = count, From = from, Assign = GatherSource.Farm });
            }
            else
            {
                // El primero que va a un recurso nuevo hace el campamento y se queda ahí.
                if (CampFor(to) is { } camp && !_camps.Contains(camp))
                {
                    Camp(to, from, with: null);
                    count--;
                }
                if (count > 0)
                    Add(new Step { Do = StepAction.Move, Count = count, From = from, Assign = to });
            }
            _on[from] -= count;
            _on[to] += count;
        }

        /// <summary>El campamento (o molino) de una fuente, la primera vez que alguien va ahí.</summary>
        private void Camp(GatherSource source, GatherSource from, Step? with)
        {
            if (CampFor(source) is not { } camp || !_camps.Add(camp))
                return;
            if (ForCiv(camp) is not { } made)
                return;
            var step = new Step { Do = StepAction.Build, What = made, From = from, Assign = source, Note = "Agregado al importar" };
            if (with is not null && camp == "mill")
            {
                // El molino se hace mientras siguen llegando aldeanos a las bayas.
                with.Group = step.Group = $"molino{++_groups}";
            }
            Add(step);
            if (with is null)
            {
                _on[from]--;
                _on[source]++;
            }
        }

        private static string? CampFor(GatherSource source) => source switch
        {
            GatherSource.Wood => "lumber_camp",
            GatherSource.Berries => "mill",
            GatherSource.Gold or GatherSource.Stone => "mining_camp",
            _ => null,
        };

        internal void Build(int count, string what, string? note = null)
        {
            if (ForCiv(what) is not { } made)
                return;
            // Ya lo agregó el importador para subir de edad (la guía lo hace recién después del clic).
            if (_forAge.Remove(made) && --count == 0)
                return;
            // Un campamento o molino de la guía cuenta como el de su recurso: el que va después no agrega otro.
            if (what is "lumber_camp" or "mill" or "mining_camp")
                _camps.Add(what);
            what = made;
            // El que pide antes (la galería, el cuartel) si la guía no lo hizo: el juego no deja ponerlo sin él.
            if (data.TryGet(what, out var entity) && data.Civ(civ).Requires(entity) is { } required && !Built(required))
                Build(1, required, $"Agregado al importar: {StepText.Name(what)} pide {StepText.Name(required)} antes");
            if (what == "house")
                _cap += count * HousePop;
            if (what == "farm")
            {
                Add(new Step { Do = StepAction.Build, What = "farm", Count = count, From = Builders(), Assign = GatherSource.Farm, Note = note });
                return;
            }
            var from = Builders();
            Add(new Step { Do = StepAction.Build, What = what, Count = count, From = from, Assign = from, Note = note });
        }

        /// <summary>
        /// Casas para que un paso no se quede sin población: las guías no anotan las de la Alta Edad Media y en Feudal
        /// se quedan cortas (en la de arqueros, 35 aldeanos y 24 arqueros con casas para 45), y sin ellas el coach
        /// esperaría para siempre.
        /// </summary>
        private void HousesFor(int villagers, int military = 0)
        {
            while (!NoHouses && villagers + 1 + _military + military > _cap && _cap < BuildOrderValidator.MaxPopulation)
            {
                var from = Builders();
                Add(new Step { Do = StepAction.Build, What = "house", From = from, Assign = from, Note = "Agregada al importar: sin esta casa no hay población" });
                _cap += HousePop;
            }
        }

        /// <summary>Si algún paso de antes construye el edificio, o uno que vale por él (el asentamiento por el molino).</summary>
        private bool Built(string key) => _steps.Any(s => s is { Do: StepAction.Build, What: { } what }
            && (what == key || data.TryGet(what, out var e) && e.CountsAs.Contains(key)));

        /// <summary>Los que construyen: los de madera, o si no hay, los del recurso con más aldeanos.</summary>
        private GatherSource Builders() =>
            _on[GatherSource.Wood] > 0 ? GatherSource.Wood : _on.MaxBy(o => o.Value).Key;

        /// <summary>
        /// La población para el clic de edad: aldeanos y el explorador. Si antes hay unidades militares, no se pone:
        /// salen de a poco y no se sabe cuántas habrá al hacer clic (con todas, el coach esperaría para siempre).
        /// </summary>
        private int? Population => _military == 0 ? _villagers + 1 : null;

        private int HousePop => data.TryGet("house", out var house) && house.PopBonus > 0 ? house.PopBonus : 5;

        private int TownCenterPop => data.TryGet("town_center", out var tc) && tc.PopBonus > 0 ? tc.PopBonus : 5;

        private Step Add(Step step)
        {
            if (_pendingNote is not null)
            {
                AppendNote(step, _pendingNote);
                _pendingNote = null;
            }
            _steps.Add(step);
            return step;
        }

        private static void AppendNote(Step step, string note) =>
            step.Note = string.IsNullOrEmpty(step.Note) ? note : $"{step.Note}. {note}";

        /// <summary>Si los aldeanos convertidos no coinciden con los de la guía en ese paso, se avisa.</summary>
        private void Check(int guideStep, int? villagerCount, JsonObject? resources)
        {
            if (villagerCount is { } expected && expected != _villagers)
                _warnings.Add($"Paso {guideStep} de la guía: dice {expected} aldeanos y la conversión llega a {_villagers}.");
            if (resources is null)
                return;
            int On(Resource r) => _on.Where(o => o.Key.Resource() == r).Sum(o => o.Value);
            foreach (var (key, resource) in new[] { ("food", Resource.Food), ("wood", Resource.Wood), ("gold", Resource.Gold), ("stone", Resource.Stone) })
            {
                if (Number(resources[key]) is { } want && want != On(resource))
                    _warnings.Add($"Paso {guideStep} de la guía: dice {want} en {Suggestion.Name(resource)} y la conversión da {On(resource)}.");
            }
        }

        private static VillagerDistribution Distribution(JsonObject r) =>
            new(Number(r["food"]) ?? 0, Number(r["wood"]) ?? 0, Number(r["gold"]) ?? 0, Number(r["stone"]) ?? 0);

        internal string Civ(string? name)
        {
            var key = RtsOverlayImporter.Civ(name, data);
            if (key == "any" && name is not null && !name.Equals("any", StringComparison.OrdinalIgnoreCase))
                _warnings.Add($"No conozco la civ \"{name}\": quedó para cualquier civ.");
            return key;
        }

        private Entity? Entity(Token token) => token.Key is { } key && data.TryGet(key, out var entity) ? entity : null;

        private IEnumerable<Entity> Entities(IEnumerable<Token> tokens) => tokens.Select(Entity).OfType<Entity>();
    }

    /// <summary>La clave de una civ por su clave o su nombre ("Mayans", "Inca"); "any" si no se conoce.</summary>
    internal static string Civ(string? name, GameData data)
    {
        if (name is null || name.Equals("any", StringComparison.OrdinalIgnoreCase))
            return "any";
        var key = name.Trim().ToLowerInvariant().Replace(' ', '_');
        return data.IsKnownCiv(key) ? key : data.CivByName(name) ?? "any";
    }

    private const string Hammer = "hammer";

    /// <summary>Un pedazo de una nota: un ícono (@carpeta/Archivo.png@), un número o una palabra.</summary>
    private sealed record Token(string Text, string? Icon = null, int? Number = null)
    {
        /// <summary>La clave de game_data que nombra (ícono o palabra en camelCase), o null.</summary>
        public string? Key => Icon is { } icon ? KeyOf(icon) : Number is null ? KeyOf(Text) : null;
    }

    private static List<Token> Tokenize(string note)
    {
        var tokens = new List<Token>();
        foreach (Match m in TokenPattern().Matches(note))
        {
            if (m.Groups["icon"].Success)
                tokens.Add(new Token(m.Value, Icon: IconName(m.Groups["icon"].Value)));
            else if (int.TryParse(m.Groups["number"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var n) && m.Groups["number"].Success)
                tokens.Add(new Token(m.Value, Number: n));
            else
                tokens.Add(new Token(m.Value));
        }
        return tokens;
    }

    [GeneratedRegex(@"@(?<icon>[^@]+)@|\d+%|(?<number>\d+)|-->|[\p{L}_]+")]
    private static partial Regex TokenPattern();

    private static HashSet<string> Words(IEnumerable<Token> tokens) =>
        tokens.Where(t => t.Icon is null && t.Number is null).Select(t => t.Text.EndsWith('%') ? "%" : t.Text.ToLowerInvariant()).ToHashSet();

    /// <summary>La fuente del primer ícono después de la palabra ("on", "from", "to").</summary>
    private static GatherSource? SourceAfter(IReadOnlyList<Token> tokens, string word)
    {
        var i = tokens.ToList().FindIndex(t => t.Icon is null && t.Text.Equals(word, StringComparison.OrdinalIgnoreCase));
        return i < 0 ? null : tokens.Skip(i + 1).Select(t => t.Icon is { } icon ? SourceOf(icon) : null).FirstOrDefault(s => s is not null);
    }

    /// <summary>"animal/Sheep_aoe2DE.png" → "sheep"; "age/FeudalAgeIconDE_alpha.png" → "feudalage".</summary>
    private static string IconName(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path.Replace('\\', '/').Split('/')[^1]);
        name = AoePrefix().Replace(name, "");
        name = IconSuffix().Replace(name, "");
        return name;
    }

    [GeneratedRegex(@"^aoe2de_", RegexOptions.IgnoreCase)]
    private static partial Regex AoePrefix();

    // "DE" solo en mayúsculas: en minúsculas es parte del nombre (Blade, Crusade).
    [GeneratedRegex(@"(_alpha|_aoe2DE|_aoe2de|_aoe2|_?Icon|DE)+$")]
    private static partial Regex IconSuffix();

    internal static GatherSource? SourceOf(string icon) => Flat(icon) switch
    {
        "sheep" or "goat" or "llama" or "turkey" or "pig" or "cow" or "goose" or "buffalo" => GatherSource.Sheep,
        "boar" or "deer" or "elephant" or "rhinoceros" or "ostrich" or "zebra" or "javelina" or "wildboar" or "ibex" => GatherSource.Hunt,
        "berrybush" or "berry" or "berries" or "fruitbush" => GatherSource.Berries,
        "farm" or "farming" => GatherSource.Farm,
        "fish" or "shorefish" or "fishingship" => GatherSource.Fish,
        "wood" or "tree" or "lumbercamp" => GatherSource.Wood,
        "gold" or "goldmine" => GatherSource.Gold,
        "stone" or "stonemine" => GatherSource.Stone,
        _ => null,
    };

    /// <summary>
    /// La clave de game_data de un ícono o una palabra: "Archer" → archer, "archeryRange" → archery_range,
    /// "FeudalAge" → feudal_age, "MaleVill" → villager, "Lumber_camp" → lumber_camp.
    /// </summary>
    internal static string? KeyOf(string name) => Flat(name) switch
    {
        "malevill" or "femalevill" or "villager" or "vill" => "villager",
        "hammer" => Hammer,
        "feudalage" => "feudal_age",
        "castleage" => "castle_age",
        "imperialage" => "imperial_age",
        "farm" => "farm",
        "scout" or "scoutcavalry" => "scout_cavalry",
        "maa" or "manatarms" => "man_at_arms",
        "tc" or "towncenter" => "town_center",
        "staple" => "stable", // errata común ("22p Scouts" de Cicero7800)
        _ => Snake(name),
    };

    private static string Flat(string name) => new(name.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    private static string Snake(string name)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsUpper(c) && i > 0 && name[i - 1] != '_' && !char.IsUpper(name[i - 1]))
                sb.Append('_');
            sb.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '_');
        }
        return sb.ToString().Trim('_');
    }

    /// <summary>
    /// La nota en castellano y sin la ruta de los íconos: "@animal/Boar_aoe2DE.png@ 50% --> Lure @animal/Boar_aoe2DE.png@"
    /// → "Al 50% de jabalí: atraé jabalí".
    /// </summary>
    private static string Clean(string note)
    {
        var text = IconPattern().Replace(note, m => Spanish(IconName(m.Groups[1].Value)));
        text = LureWord().Replace(text, "atraé");
        text = HalfWay().Replace(text, m => $"Al {m.Groups["pct"].Value} de {m.Groups["what"].Value.Trim()}: {m.Groups["then"].Value.Trim()}");
        return HalfWayFirst().Replace(text, m => $"Al {m.Groups["pct"].Value} de {m.Groups["what"].Value.Trim()}: {m.Groups["then"].Value.Trim()}").Trim();
    }

    /// <summary>El nombre en castellano de un ícono, si se conoce ("Boar" → "jabalí", "Barracks" → "cuartel").</summary>
    private static string Spanish(string icon)
    {
        if (SourceOf(icon) is { } source)
            return Flat(icon) == "boar" ? "jabalí" : StepText.Source(source);
        if (KeyOf(icon) is { } key && StepText.HasName(key))
            return StepText.Name(key);
        return icon;
    }

    [GeneratedRegex("@([^@]+)@")]
    private static partial Regex IconPattern();

    [GeneratedRegex(@"\bLure\b", RegexOptions.IgnoreCase)]
    private static partial Regex LureWord();

    // "jabalí 50% --> atraé jabalí" y "50% Feudal --> cuartel".
    [GeneratedRegex(@"^(?<what>[^%]+?)\s+(?<pct>\d+%)\s*-->\s*(?<then>.+)$")]
    private static partial Regex HalfWay();

    [GeneratedRegex(@"^(?<pct>\d+%)\s+(?<what>.+?)\s*-->\s*(?<then>.+)$")]
    private static partial Regex HalfWayFirst();

    internal static int? Number(JsonNode? node)
    {
        try
        {
            return node?.GetValue<int>();
        }
        catch (Exception e) when (e is FormatException or InvalidOperationException)
        {
            return null;
        }
    }

    internal static string? Text(JsonNode? node)
    {
        try
        {
            return node?.GetValue<string>() is { Length: > 0 } text ? text.Trim() : null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }
}
