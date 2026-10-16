using BoCoach.Vision.Capture;

namespace BoCoach.Vision.Events;

/// <summary>
/// Prepara la captura del log de eventos para el OCR. El texto del log es blanco puro (con bordes grises por el
/// antialiasing) y se dibuja sobre el mapa, que casi siempre tiene algo de color (pasto, tierra, agua):
/// <list type="number">
/// <item>Se estira el brillo, porque en pausa el juego oscurece todo ×0,2.</item>
/// <item>Cada píxel es tinta según qué tan claro y qué tan sin color es: queda texto negro sobre fondo blanco,
/// sin el mapa de atrás.</item>
/// <item>Se agrega un margen blanco (el renglón de arriba suele quedar pegado al borde de la caja) y se agranda
/// con interpolación.</item>
/// </list>
/// Contra el gris invertido de antes, sobre 3 partidas grabadas: muchos menos renglones sin reconocer, y lee el
/// texto sobre zonas negras sin explorar y con el juego en pausa.
/// </summary>
public static class LogImage
{
    /// <summary>Cuántas veces se agranda la captura para el OCR (×4 lee algo más exacto, pero a veces no lee nada).</summary>
    public const int Scale = 3;

    /// <summary>Margen blanco alrededor, en píxeles de la captura original.</summary>
    public const int Margin = 8;

    /// <summary>Ganancia máxima al estirar el brillo (la pausa oscurece ×0,2, o sea que hace falta ×5).</summary>
    public const double MaxGain = 5.5;

    // Saturación (canal máximo - canal mínimo): hasta ColorlessFull es blanco o gris; desde ColorlessNone es color.
    private const int ColorlessFull = 15;
    private const int ColorlessNone = 35;

    // Brillo (canal mínimo): desde InkLow empieza a ser tinta, desde InkFull es tinta entera.
    private const int InkLow = 60;
    private const int InkFull = 180;

    public static CapturedImage Prepare(CapturedImage image) =>
        WhiteText(image.Stretched(MaxGain)).Padded(Margin).ScaleSmooth(Scale);

    /// <summary>Texto blanco → negro sobre fondo blanco; lo que tiene color o es oscuro queda blanco.</summary>
    public static CapturedImage WhiteText(CapturedImage image)
    {
        var source = image.Bgra;
        var output = new byte[source.Length];
        for (var i = 0; i < source.Length; i += 4)
        {
            int b = source[i], g = source[i + 1], r = source[i + 2];
            var min = Math.Min(r, Math.Min(g, b));
            var saturation = Math.Max(r, Math.Max(g, b)) - min;
            var colorless = Math.Clamp((ColorlessNone - saturation) / (double)(ColorlessNone - ColorlessFull), 0, 1);
            var bright = Math.Clamp((min - InkLow) / (double)(InkFull - InkLow), 0, 1);
            var gray = (byte)(255 - Math.Round(255 * bright * colorless));
            (output[i], output[i + 1], output[i + 2], output[i + 3]) = (gray, gray, gray, 255);
        }
        return new CapturedImage(image.Width, image.Height, output);
    }
}
