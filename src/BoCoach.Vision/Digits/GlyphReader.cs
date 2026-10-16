using BoCoach.Vision.Capture;

namespace BoCoach.Vision.Digits;

/// <summary>Lo leído en una región: el texto y qué tan seguro está el lector.</summary>
/// <param name="Confidence">Entre 0 y 1: 1 si cada carácter se parece mucho a su plantilla y poco a la segunda mejor.</param>
/// <param name="Clipped">El texto toca el borde de la región: probablemente falta un carácter (hay que agrandar la caja).</param>
public sealed record GlyphReading(string Text, float Confidence, bool Clipped = false)
{
    /// <summary>Se puede usar: tiene texto, ningún carácter desconocido y no está cortado.</summary>
    public bool IsUsable => Text.Length > 0 && !Clipped && !Text.Contains('?');
}

/// <summary>
/// Lee números del HUD comparando cada carácter contra las plantillas de una letra (template matching).
/// La letra del HUD es siempre la misma, así que esto es más rápido y preciso que un OCR general.
/// </summary>
public sealed class GlyphReader(GlyphFont font)
{
    /// <summary>Distancia a partir de la cual un carácter no se parece a nada (se lee como '?').</summary>
    public float MaxDistance { get; init; } = 28f;

    public GlyphFont Font => font;

    /// <summary>
    /// Lee solo el último grupo de caracteres (el de más a la derecha). Sirve para las cajas que además del número
    /// tienen otras cosas a la izquierda, como el ícono del recurso: un hueco de más de un carácter separa grupos.
    /// </summary>
    public GlyphReading ReadRightmostGroup(CapturedImage image)
    {
        var glyphs = Glyphs.Extract(image, font.Ink, font.TypicalWidthRatio);
        if (glyphs.Count == 0)
            return new GlyphReading("", 0);
        // Un hueco de grupo es más ancho que un dígito típico (por la altura del texto, no por los dígitos de esta
        // caja: un "11" son dos caracteres angostos con el espacio de dígitos normales entre ellos).
        var textHeight = glyphs[0].Width / Math.Max(0.01, glyphs[0].WidthRatio);
        var gap = Math.Max(glyphs.Max(g => g.Width), font.TypicalWidthRatio * textHeight) * 1.2;
        var start = glyphs.Count - 1;
        while (start > 0 && glyphs[start].Left - glyphs[start - 1].Right <= gap)
            start--;
        return Classify(glyphs.Skip(start).ToList(), image.Width);
    }

    public GlyphReading Read(CapturedImage image) =>
        Classify(Glyphs.Extract(image, font.Ink, font.TypicalWidthRatio), image.Width);

    private GlyphReading Classify(IReadOnlyList<Glyph> glyphs, int imageWidth)
    {
        if (glyphs.Count == 0)
            return new GlyphReading("", 0);

        var text = new char[glyphs.Count];
        var confidence = 1f;
        for (var i = 0; i < glyphs.Count; i++)
        {
            var (best, bestDistance, second) = Classify(glyphs[i].Cell);
            text[i] = bestDistance <= MaxDistance ? best : '?';
            // Seguridad: qué tan cerca de la plantilla y qué tan lejos de la alternativa.
            var closeness = 1 - Math.Min(1, bestDistance / MaxDistance);
            var margin = second <= 0 ? 1 : Math.Clamp((second - bestDistance) / second, 0, 1);
            confidence = Math.Min(confidence, 0.5f * closeness + 0.5f * margin);
        }
        var clipped = glyphs[0].Left <= 0 || glyphs[^1].Right >= imageWidth - 1;
        return new GlyphReading(new string(text), clipped ? 0 : confidence, clipped);
    }

    /// <summary>
    /// El carácter más parecido (entre todas las variantes), su distancia y la distancia al mejor carácter
    /// distinto (para saber si la decisión fue clara).
    /// </summary>
    public (char Char, float Distance, float SecondDistance) Classify(float[] cell)
    {
        var bestByChar = new Dictionary<char, float>();
        foreach (var template in font.Templates)
        {
            var d = Glyphs.Distance(cell, template.Cell);
            if (!bestByChar.TryGetValue(template.Char, out var current) || d < current)
                bestByChar[template.Char] = d;
        }
        var ranked = bestByChar.OrderBy(kv => kv.Value).ToList();
        return ranked.Count switch
        {
            0 => ('?', float.MaxValue, 0),
            1 => (ranked[0].Key, ranked[0].Value, 0),
            _ => (ranked[0].Key, ranked[0].Value, ranked[1].Value),
        };
    }
}
