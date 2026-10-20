using System.Text.Json;
using BoCoach.Core.BuildOrders;
using BoCoach.Core.Engine;

namespace BoCoach.Core.Data;

/// <summary>
/// Eventos conocidos del log del juego (data/events.json): id de evento -> frases que lo muestran.
/// Ej.: "house_built" -> ["House Built"].
/// </summary>
public sealed class EventCatalog
{
    public EventCatalog(IReadOnlyDictionary<string, IReadOnlyList<string>> phrases)
    {
        Phrases = phrases;
    }

    public IReadOnlyDictionary<string, IReadOnlyList<string>> Phrases { get; }

    public bool Contains(string eventId) => Phrases.ContainsKey(eventId);

    /// <summary>
    /// Eventos frecuentes, o null si todos valen lo mismo. Con el catálogo completo (casi 600 eventos) una lectura
    /// muy mala de "Villager Created" ("L"itger createa") se parece más a "Slinger Created": ante la duda, el lector
    /// del log prefiere estos.
    /// </summary>
    public IReadOnlySet<string>? Common { get; private init; }

    /// <summary>
    /// True si la frase es de las frecuentes: de un evento de <see cref="Common"/> y, si es una acción, con el nombre
    /// de siempre (la primera frase). Los nombres de otras civs ("Lancer Created" en knight_created, de las Crónicas)
    /// cuentan como raros: "L"itger createa" se parecía más a "Lancer" que a "Villager".
    /// </summary>
    public bool IsCommon(string eventId, int phraseIndex) =>
        Common is null || Common.Contains(eventId) && (IsUsualName(eventId, phraseIndex) || EventConventions.Parse(eventId) is null);

    /// <summary>
    /// Frases con el nombre de siempre de cada evento, de cada idioma unido con <see cref="Merged"/>; null = solo la
    /// primera.
    /// </summary>
    private IReadOnlySet<string>? _usual;

    private bool IsUsualName(string eventId, int phraseIndex) =>
        _usual is null ? phraseIndex == 0 : _usual.Contains(UsualKey(eventId, Phrases[eventId][phraseIndex]));

    private static string UsualKey(string eventId, string phrase) => $"{eventId}\n{phrase}";

    private IEnumerable<string> UsualKeys() =>
        _usual ?? Phrases.Where(e => e.Value.Count > 0).Select(e => UsualKey(e.Key, e.Value[0]));

    /// <summary>
    /// Este catálogo más las frases de otro idioma del juego (events.es.json) bajo los mismos ids. La primera frase
    /// de cada idioma sigue siendo el nombre de siempre (ver <see cref="IsCommon"/>).
    /// </summary>
    public EventCatalog Merged(EventCatalog other)
    {
        var phrases = Phrases.ToDictionary(e => e.Key, e => e.Value.ToList());
        foreach (var (id, list) in other.Phrases)
        {
            var mine = phrases.TryGetValue(id, out var existing) ? existing : phrases[id] = [];
            mine.AddRange(list.Where(p => !mine.Contains(p)));
        }
        return new EventCatalog(phrases.ToDictionary(e => e.Key, e => (IReadOnlyList<string>)e.Value))
        {
            Common = Common,
            _usual = UsualKeys().Concat(other.UsualKeys()).ToHashSet(),
        };
    }

    /// <summary>
    /// El mismo catálogo, prefiriendo los eventos de las entidades de game_data (las que pueden usar las BO) y los
    /// avisos que no son acciones ("Farm Exhausted").
    /// </summary>
    public EventCatalog PreferringEntitiesOf(GameData data)
    {
        static EntityKind KindOf(StepAction action) => action switch
        {
            StepAction.Train => EntityKind.Unit,
            StepAction.Build => EntityKind.Building,
            _ => EntityKind.Tech,
        };
        var common = Phrases.Keys
            .Where(id => EventConventions.Parse(id) is not { } action
                         || data.TryGet(action.What, out var entity) && entity.Kind == KindOf(action.Action))
            .ToHashSet();
        return new EventCatalog(Phrases) { Common = common, _usual = _usual };
    }

    public static EventCatalog Load(string path) => Parse(File.ReadAllText(path));

    public static EventCatalog Parse(string json)
    {
        Dictionary<string, List<string>>? raw;
        try
        {
            raw = JsonSerializer.Deserialize(json, CoreJson.Info<Dictionary<string, List<string>>>());
        }
        catch (JsonException e)
        {
            throw new GameDataException($"events.json inválido: {e.Message}", e);
        }

        if (raw is null)
            throw new GameDataException("events.json está vacío.");
        foreach (var (id, phrases) in raw)
        {
            if (phrases is null || phrases.Count == 0 || phrases.Any(string.IsNullOrWhiteSpace))
                throw new GameDataException($"events.json: '{id}' necesita al menos una frase no vacía.");
        }

        return new EventCatalog(raw.ToDictionary(e => e.Key, e => (IReadOnlyList<string>)e.Value));
    }
}
