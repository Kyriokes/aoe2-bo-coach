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
    private readonly List<(string EventId, string Phrase, bool Rare)> _phrases;

    public EventMatcher(EventCatalog catalog)
    {
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
        var result = new List<string>();
        var unrecognized = Walk(lines, (_, _) => { }, result);
        return (result, unrecognized);
    }

    /// <summary>Recorre los renglones: avisa cuáles forman un mensaje (el primero y cuántos) y devuelve cuántos no se reconocieron.</summary>
    private int Walk(IReadOnlyList<string> lines, Action<int, int> recognized, List<string>? ids = null)
    {
        var unrecognized = 0;
        for (var i = 0; i < lines.Count; i++)
        {
            var single = Best(lines[i]);
            // Puede ser la primera mitad de un mensaje largo: si junto con el siguiente se parece más a algo, es eso
            // (partida 20261031-183635: "Leather Archer Armor Research" solo ya se parecía a Padded Archer Armor).
            var joined = i + 1 < lines.Count && !Closes(lines[i]) && !Opens(lines[i + 1]) ? Best(lines[i] + " " + lines[i + 1]) : null;
            if (joined is { } pair && (single is null || pair.Score < single.Value.Score))
            {
                ids?.Add(pair.EventId);
                recognized(i, 2);
                i++;
                continue;
            }
            if (single is { } one)
            {
                ids?.Add(one.EventId);
                recognized(i, 1);
                continue;
            }
            unrecognized++;
        }
        return unrecognized;
    }

    /// <summary>
    /// Lo que reemplaza a un renglón que no es un mensaje del juego en el registro de partida. Termina en guion para
    /// no juntarse con el renglón siguiente (<see cref="Closes"/>), y no tiene letras: junto con el anterior da lo mismo
    /// que el anterior solo.
    /// </summary>
    public const string Scrubbed = BoCoach.Core.Record.RecordHealth.Unrecognized;

    /// <summary>
    /// Los renglones con lo que no se reconoce tapado (<see cref="Scrubbed"/>): por la caja del log puede pasar el chat,
    /// con el nombre de quien escribe. Lo tapado se sigue contando como no reconocido y lo demás se reconoce igual, así
    /// que <see cref="MatchAll"/> da lo mismo con estos renglones.
    /// </summary>
    public IReadOnlyList<string> Scrub(IReadOnlyList<string> lines)
    {
        var result = new string[lines.Count];
        Array.Fill(result, Scrubbed);
        Walk(lines, (first, count) =>
        {
            for (var i = first; i < first + count; i++)
                result[i] = lines[i];
        });
        return result;
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
            if (score < bestScore)
                (best, bestScore) = (eventId, score);
        }
        return best is not null && bestScore <= MaxRelativeDistance ? (best, bestScore) : null;
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
