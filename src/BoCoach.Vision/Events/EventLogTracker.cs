namespace BoCoach.Vision.Events;

/// <summary>
/// Saca los eventos nuevos del log mirando cómo cambia la lista de mensajes visibles (de arriba hacia abajo):
/// <list type="bullet">
/// <item>Los mensajes nuevos entran por abajo y los viejos se van por arriba.</item>
/// <item>Si un mensaje se repite, el juego no agrega otro renglón: mueve el viejo al final.</item>
/// </list>
/// Entonces un evento es nuevo cuando aparece debajo del último mensaje que ya estaba (o se movió ahí).
/// Para no confundir parpadeos del OCR con cambios, la lista tiene que repetirse en dos capturas seguidas
/// antes de tomarse en cuenta.
/// </summary>
public sealed class EventLogTracker
{
    private IReadOnlyList<string> _stable = [];
    private IReadOnlyList<string>? _candidate;
    private int _candidateFrames;

    /// <summary>Capturas seguidas que una lista tiene que repetirse para considerarla real.</summary>
    public int StableFrames { get; init; } = 2;

    /// <summary>
    /// Capturas seguidas sin ningún mensaje para dar el log por vacío. Es más que <see cref="StableFrames"/>
    /// porque un mensaje que se está desvaneciendo a veces no se lee, y al volver a leerlo parecería nuevo.
    /// Tampoco puede ser mucho más: si un mensaje se va y el mismo vuelve a aparecer (otro aldeano), solo se
    /// cuenta si en el medio el log se dio por vacío. En las partidas grabadas el hueco más corto fue de 5 capturas.
    /// </summary>
    public int EmptyFrames { get; init; } = 5;

    /// <summary>Lo que se ve ahora en el log, ya estabilizado.</summary>
    public IReadOnlyList<string> Visible => _stable;

    /// <summary>Recibe los ids reconocidos en la captura actual y devuelve los eventos nuevos.</summary>
    /// <param name="hasUnrecognized">Hubo renglones que no se reconocieron (mensajes mal leídos u otros textos).</param>
    public IReadOnlyList<string> Update(IReadOnlyList<string> current, bool hasUnrecognized = false)
    {
        // Con renglones mal leídos, lo reconocido puede ser un pedazo de lo que hay: si no agrega nada
        // nuevo abajo, la captura no aporta y no se la deja "borrar" mensajes.
        if (hasUnrecognized && NewAtBottom(_stable, current).Count == 0)
            return [];

        if (_candidate is not null && current.SequenceEqual(_candidate))
        {
            _candidateFrames++;
        }
        else
        {
            _candidate = current.ToList();
            _candidateFrames = 1;
        }

        var needed = _candidate.Count == 0 ? EmptyFrames : StableFrames;
        if (_candidateFrames < needed || _candidate.SequenceEqual(_stable))
            return [];

        var news = NewAtBottom(_stable, _candidate);
        _stable = _candidate;
        return news;
    }

    /// <summary>
    /// Los elementos de <paramref name="current"/> que están debajo del último que coincide con
    /// <paramref name="previous"/> (subsecuencia común más larga, eligiendo las coincidencias lo más arriba posible).
    /// </summary>
    public static IReadOnlyList<string> NewAtBottom(IReadOnlyList<string> previous, IReadOnlyList<string> current)
    {
        var n = previous.Count;
        var m = current.Count;
        var lcs = new int[n + 1, m + 1];
        for (var i = n - 1; i >= 0; i--)
        {
            for (var j = m - 1; j >= 0; j--)
            {
                lcs[i, j] = previous[i] == current[j]
                    ? lcs[i + 1, j + 1] + 1
                    : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);
            }
        }

        var lastMatched = -1;
        for (int i = 0, j = 0; i < n && j < m;)
        {
            if (previous[i] == current[j] && lcs[i, j] == lcs[i + 1, j + 1] + 1)
            {
                lastMatched = j;
                i++;
                j++;
            }
            else if (lcs[i + 1, j] >= lcs[i, j + 1])
            {
                i++;
            }
            else
            {
                j++;
            }
        }
        return current.Skip(lastMatched + 1).ToList();
    }
}
