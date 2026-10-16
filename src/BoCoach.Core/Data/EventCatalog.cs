using System.Text.Json;

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
