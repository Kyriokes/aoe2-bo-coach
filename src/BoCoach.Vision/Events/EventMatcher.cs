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
    private readonly List<(string EventId, string Phrase)> _phrases;

    public EventMatcher(EventCatalog catalog)
    {
        _phrases = catalog.Phrases
            .SelectMany(e => e.Value.Select(p => (e.Key, Normalize(p))))
            .Where(p => p.Item2.Length > 0)
            .ToList();
    }

    /// <summary>Distancia relativa máxima (errores / largo de la frase) para aceptar una coincidencia.</summary>
    public double MaxRelativeDistance { get; init; } = 0.38;

    /// <summary>
    /// Ids de evento de los renglones, de arriba hacia abajo. Un renglón que no coincide solo se prueba junto con
    /// el siguiente (los mensajes largos ocupan dos renglones); lo que no se reconoce se ignora.
    /// </summary>
    public IReadOnlyList<string> MatchLines(IReadOnlyList<string> lines) => MatchAll(lines).EventIds;

    /// <summary>Como <see cref="MatchLines"/>, y además cuántos renglones quedaron sin reconocer.</summary>
    public (IReadOnlyList<string> EventIds, int Unrecognized) MatchAll(IReadOnlyList<string> lines)
    {
        var result = new List<string>();
        var unrecognized = 0;
        for (var i = 0; i < lines.Count; i++)
        {
            if (Match(lines[i]) is { } single)
            {
                result.Add(single);
                continue;
            }
            if (i + 1 < lines.Count && Match(lines[i] + " " + lines[i + 1]) is { } joined)
            {
                result.Add(joined);
                i++;
                continue;
            }
            unrecognized++;
        }
        return (result, unrecognized);
    }

    /// <summary>Id del evento que mejor coincide con el texto, o null si ninguno se parece lo suficiente.</summary>
    public string? Match(string text)
    {
        var normalized = Normalize(text);
        if (normalized.Length < 4)
            return null;

        string? best = null;
        var bestScore = double.MaxValue;
        foreach (var (eventId, phrase) in _phrases)
        {
            var score = Levenshtein(normalized, phrase) / (double)phrase.Length;
            if (score < bestScore)
                (best, bestScore) = (eventId, score);
        }
        return bestScore <= MaxRelativeDistance ? best : null;
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

    private static int Levenshtein(string a, string b)
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
