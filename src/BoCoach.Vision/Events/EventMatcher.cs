using System.Text;
using BoCoach.Core.Data;

namespace BoCoach.Vision.Events;

/// <summary>
/// Reconoce renglones del log de eventos leídos por OCR ("-IJock Built--", "Ga11ey Created") comparándolos
/// con las frases conocidas de data/events.json por distancia de edición, después de corregir las confusiones
/// típicas del OCR (1 → l, 0 → o) y sacar todo lo que no es letra.
/// </summary>
public sealed class EventMatcher
{
    private readonly EventCatalog _catalog;
    private readonly List<(string EventId, string Phrase, bool Rare)> _phrases;
    private readonly Dictionary<string, string> _savedLines = [];

    public EventMatcher(EventCatalog catalog)
    {
        _catalog = catalog;
        _phrases = catalog.Phrases
            .SelectMany(e => e.Value.Select((p, i) => (e.Key, Normalize(p), !catalog.IsCommon(e.Key, i))))
            .Where(p => p.Item2.Length > 0)
            .ToList();
    }

    /// <summary>Distancia relativa máxima (errores / largo de la frase) para aceptar una coincidencia.</summary>
    public double MaxRelativeDistance { get; init; } = 0.38;

    /// <summary>Lo que se suma a la distancia de un evento poco común (ver <see cref="EventCatalog.Common"/>).</summary>
    public double RarePenalty { get; init; } = 0.15;

    /// <summary>
    /// Ids de evento de los renglones, de arriba hacia abajo. Un renglón que no coincide solo se prueba junto con
    /// el siguiente (los mensajes largos ocupan dos renglones); lo que no se reconoce se ignora.
    /// </summary>
    public IReadOnlyList<string> MatchLines(IReadOnlyList<string> lines) => MatchAll(lines).EventIds;

    /// <summary>Como <see cref="MatchLines"/>, y además cuántos renglones quedaron sin reconocer.</summary>
    public (IReadOnlyList<string> EventIds, int Unrecognized) MatchAll(IReadOnlyList<string> lines)
    {
        var messages = Messages(lines);
        return (messages.OfType<string>().ToList(), messages.Count(m => m is null));
    }

    /// <summary>De arriba hacia abajo, el evento de cada mensaje (de uno o dos renglones) y null por cada renglón que no se reconoció.</summary>
    private List<string?> Messages(IReadOnlyList<string> lines)
    {
        var messages = new List<string?>(lines.Count);
        for (var i = 0; i < lines.Count; i++)
        {
            var single = Best(lines[i]);
            // Puede ser la primera mitad de un mensaje largo: si junto con el siguiente se parece más a algo, es eso
            // (partida 20261031-183635: "Leather Archer Armor Research" solo ya se parecía a Padded Archer Armor).
            var joined = i + 1 < lines.Count && !Closes(lines[i]) && !Opens(lines[i + 1]) ? Best(lines[i] + " " + lines[i + 1]) : null;
            if (joined is { } pair && (single is null || pair.Score < single.Value.Score))
            {
                messages.Add(pair.EventId);
                i++;
                continue;
            }
            messages.Add(single?.EventId);
        }
        return messages;
    }

    /// <summary>
    /// Lo que reemplaza a un renglón que no es un mensaje del juego en el registro de partida. Termina en guion para
    /// no juntarse con el renglón siguiente (<see cref="Closes"/>), y no tiene letras: no se reconoce como nada.
    /// </summary>
    public const string Scrubbed = BoCoach.Core.Record.RecordHealth.Unrecognized;

    /// <summary>
    /// Lo que va al registro de partida en vez de los renglones: un renglón por mensaje, con la frase del catálogo, y
    /// <see cref="Scrubbed"/> por cada renglón que no se reconoció. Nunca lo leído: por la caja del log pasa el chat con el
    /// nombre de quien escribe ("Ana: gg" junto a un mensaje real se reconoce como un mensaje partido en dos) y el aviso de
    /// ataque lleva el nombre del jugador. <see cref="MatchAll"/> da lo mismo con estos renglones: con los guiones ninguno
    /// se junta con el vecino y cada frase vuelve a su evento.
    /// </summary>
    public IReadOnlyList<string> Scrub(IReadOnlyList<string> lines) =>
        Messages(lines).Select(id => id is null ? Scrubbed : SavedLine(id)).ToList();

    /// <summary>La primera frase del evento que vuelve a él: dos eventos pueden compartir una frase.</summary>
    private string SavedLine(string eventId)
    {
        if (!_savedLines.TryGetValue(eventId, out var line))
        {
            var phrases = _catalog.Phrases[eventId];
            var phrase = phrases.FirstOrDefault(p => Best(p)?.EventId == eventId) ?? phrases[0];
            _savedLines[eventId] = line = $"--{phrase}--";
        }
        return line;
    }

    /// <summary>Guiones con que el OCR lee los "--" que abren y cierran cada mensaje.</summary>
    private static readonly char[] Dashes = ['-', '—', '–', '_', '~'];

    /// <summary>
    /// El renglón termina un mensaje ("Complete--"): no es la primera mitad de uno largo. Lo que queda arriba puede ser
    /// el final de un mensaje con su primer renglón fuera de la caja (partida 20261031-163414: "Complete--" junto con
    /// "--Archer Created--" se leía "Cavalry Archer Created").
    /// </summary>
    private static bool Closes(string line) => line.TrimEnd() is { Length: > 0 } t && Dashes.Contains(t[^1]);

    /// <summary>El renglón empieza un mensaje ("--Archer Created--"): no es la segunda mitad de uno largo.</summary>
    private static bool Opens(string line) => line.TrimStart() is { Length: > 0 } t && Dashes.Contains(t[0]);

    /// <summary>Id del evento que mejor coincide con el texto, o null si ninguno se parece lo suficiente.</summary>
    public string? Match(string text) => Best(text)?.EventId;

    /// <summary>El evento que mejor coincide y su puntaje (distancia relativa más la penalidad), o null.</summary>
    private (string EventId, double Score)? Best(string text)
    {
        var normalized = Normalize(text);
        if (normalized.Length < 4)
            return null;

        string? best = null;
        var bestScore = double.MaxValue;
        foreach (var (eventId, phrase, rare) in _phrases)
        {
            // La distancia nunca es menor que la diferencia de largo: si con eso ya no puede ganar, no se calcula.
            if (Math.Abs(normalized.Length - phrase.Length) > Math.Min(bestScore, MaxRelativeDistance) * phrase.Length)
                continue;
            // Leído tal cual, gana aunque sea poco común: la penalidad es para las lecturas dudosas (partida
            // 20261031-183635: "Town Patrol Research Complete" salía Town Watch, y Leather Archer Armor, Padded). Con
            // letras mal leídas sigue entera: "I aeather Archer Armor" queda Padded.
            var distance = Levenshtein(normalized, phrase);
            var score = distance == 0 ? 0 : distance / (double)phrase.Length + (rare ? RarePenalty : 0);
            if (score < bestScore && !IsAnotherName(normalized, phrase, distance))
                (best, bestScore) = (eventId, score);
        }
        return best is not null && bestScore <= MaxRelativeDistance ? (best, bestScore) : null;
    }

    /// <summary>Largo desde el que lo distinto entre dos frases ya es un nombre, no un par de letras mal leídas.</summary>
    private const int NameLength = 5;

    /// <summary>Parte de ese nombre que tiene que cambiar para que sea otro: una lectura mala se le sigue pareciendo.</summary>
    private const double AnotherNameShare = 0.6;

    /// <summary>
    /// Comparten el principio o el final y lo del medio no se parece: es otro mensaje, aunque la frase entera quede cerca.
    /// En la partida 20261104-095625, "Champi Runner Research Complete" (que no está en el catálogo) daba el anillo del
    /// pulgar por hecho 2 minutos antes de investigarlo: el final del mensaje pesa más que el nombre. Si a una de las
    /// dos le falta un pedazo no es otro nombre: es el primer renglón de un mensaje largo, sin el segundo.
    /// </summary>
    private static bool IsAnotherName(string read, string phrase, int distance)
    {
        var shorter = Math.Min(read.Length, phrase.Length);
        var start = 0;
        while (start < shorter && read[start] == phrase[start])
            start++;
        var end = 0;
        while (end < shorter - start && read[^(end + 1)] == phrase[^(end + 1)])
            end++;
        var middle = Math.Max(read.Length, phrase.Length) - start - end;
        return Math.Min(read.Length, phrase.Length) - start - end > 1 && middle >= NameLength && distance >= AnotherNameShare * middle;
    }

    /// <summary>Minúsculas, solo letras y espacios simples, con las confusiones típicas del OCR corregidas.</summary>
    public static string Normalize(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var raw in text.ToLowerInvariant())
        {
            var c = raw switch
            {
                '1' or '|' or '!' => 'l',
                '0' => 'o',
                '5' or '$' => 's',
                _ => raw,
            };
            if (c is >= 'a' and <= 'z')
                sb.Append(c);
            else if (sb.Length > 0 && sb[^1] != ' ')
                sb.Append(' ');
        }
        return sb.ToString().Trim();
    }

    internal static int Levenshtein(string a, string b)
    {
        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++)
            previous[j] = j;
        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
            }
            (previous, current) = (current, previous);
        }
        return previous[b.Length];
    }
}
