using System.Globalization;
using System.Text.RegularExpressions;
using BoCoach.Vision.Capture;
using BoCoach.Vision.Loading;
using BoCoach.Vision.Ocr;

namespace BoCoach.Vision.Hud;

/// <summary>Un renglón del panel de puntajes.</summary>
/// <param name="Slot">Número de jugador (1 a 8) por el color del texto, o null si no se entiende el color.</param>
/// <param name="Text">El renglón como lo leyó el OCR ("UKyriokes: 779/779").</param>
/// <param name="Score">El puntaje.</param>
/// <param name="DarkAge">El ícono de edad es el de la Edad Oscura (naranja, "I").</param>
public sealed record ScoreRow(int? Slot, string Text, int Score, bool DarkAge);

/// <summary>
/// El panel de puntajes (región <c>scores</c>, abajo a la derecha; verificado en la grabación 20261019-202635): un
/// renglón por jugador con el número en un cuadrado de su color, "Nombre: puntaje/puntaje" en su color, la civ
/// abreviada y un ícono de edad (naranja con "I" en la Edad Oscura; oscuro con rojo y "II" en Feudal). Se reordena
/// por puntaje: cada jugador se sigue por su color. El OCR lee bien nombre y puntaje (el número del cuadrado no).
/// </summary>
public sealed partial class ScoresPanelReader(OcrReader ocr)
{
    private const int Zoom = 2;
    private const int Margin = 12;

    /// <summary>Ancho del ícono de edad y del escudo de la civ a la derecha del panel (a 1080p), que no son texto del jugador.</summary>
    private const int IconWidth = 80;

    public async Task<IReadOnlyList<ScoreRow>> ReadAsync(CapturedImage panel)
    {
        var lines = await ReadRawAsync(panel);
        var rows = new List<ScoreRow>();
        foreach (var line in lines)
        {
            if (Parse(line.Text) is not { } score)
                continue;
            var top = Math.Clamp((int)line.Top, 0, panel.Height - 1);
            var bottom = Math.Clamp((int)Math.Ceiling(line.Top + line.Height), top + 1, panel.Height);
            rows.Add(new ScoreRow(SlotOf(panel, top, bottom), line.Text, score, IsDarkAge(panel, top, bottom)));
        }
        return rows;
    }

    /// <summary>
    /// Fondo del panel: el cuartil bajo del canal más alto de la mitad derecha de la fila (el panel es translúcido:
    /// 55 sobre mapa oscuro, 72 sobre arena; el mapa sin panel pasa de 100). Debajo del panel pueden quedar hasta
    /// <see cref="SkipBelow"/> filas de mapa, y la banda tiene que tener al menos <see cref="MinBand"/>.
    /// </summary>
    private const int PanelBackgroundMax = 90;
    private const int SkipBelow = 16;
    private const int MinBand = 10;

    /// <summary>
    /// La imagen como va al OCR: texto negro sobre blanco, y solo la banda del panel. La región trae mapa arriba del
    /// panel (el panel crece hacia arriba en partidas por equipos) y, con todo en gris invertido, el OCR de Windows
    /// a veces no leía nada en capturas casi iguales a otras que sí (20261101-125353: la 1364 se leía y la 1368,
    /// 1372 y 1376 no; en toda la partida se perdía el 40 % de las lecturas). La banda son las filas de abajo cuyo
    /// lado derecho es casi todo oscuro; el texto se toma por el canal más alto (el rojo del jugador 2 es oscuro en
    /// gris) y lo de afuera de la banda queda blanco. Sin banda (otro panel), se invierte todo como antes.
    /// </summary>
    public static CapturedImage Prepare(CapturedImage panel)
    {
        var top = PanelTop(panel);
        if (top is null)
            return LoadingScreenReader.DarkOnLight(panel.Resample(Zoom)).Padded(Margin);
        // El fondo del panel (la mediana de los cuartiles bajos de sus filas) va a blanco y lo que lo supera en
        // TextContrast o más, a negro; el mapa a la izquierda del panel y arriba de la banda queda blanco.
        var levels = Enumerable.Range(top.Value, panel.Height - top.Value).Select(y => Background(panel, y)).OrderBy(v => v).ToList();
        var background = levels[levels.Count / 2] + 10;
        var left = PanelLeft(panel, top.Value, background);
        var output = new byte[panel.Bgra.Length];
        for (var y = 0; y < panel.Height; y++)
        {
            for (var x = 0; x < panel.Width; x++)
            {
                var i = (y * panel.Width + x) * 4;
                var max = Math.Max(panel.Bgra[i], Math.Max(panel.Bgra[i + 1], panel.Bgra[i + 2]));
                var v = y < top || x < left ? (byte)255 : (byte)(255 - Math.Clamp((max - background) * 255 / TextContrast, 0, 255));
                (output[i], output[i + 1], output[i + 2], output[i + 3]) = (v, v, v, 255);
            }
        }
        return new CapturedImage(panel.Width, panel.Height, output).Resample(Zoom).Padded(Margin);
    }

    /// <summary>Cuánto más claro que el fondo tiene que ser un píxel para ir a negro del todo.</summary>
    private const int TextContrast = 120;

    /// <summary>
    /// Primera columna del panel (está pegado al borde derecho): desde la izquierda de los íconos, hasta encontrar
    /// 6 columnas seguidas en que menos de un cuarto de las filas de la banda son fondo (una columna de texto tiene
    /// menos fondo, pero no tantas seguidas).
    /// </summary>
    private static int PanelLeft(CapturedImage panel, int top, int background)
    {
        var rows = panel.Height - top;
        var mapColumns = 0;
        for (var x = Math.Max(0, panel.Width - IconWidth - 1); x >= 0; x--)
        {
            var dark = 0;
            for (var y = top; y < panel.Height; y++)
            {
                var (r, g, b) = panel.Pixel(x, y);
                if (Math.Max(r, Math.Max(g, b)) <= background + 20)
                    dark++;
            }
            mapColumns = dark < rows / 4 ? mapColumns + 1 : 0;
            if (mapColumns >= 6)
                return x + 6;
        }
        return 0;
    }

    /// <summary>El cuartil bajo del canal más alto en la mitad derecha de la fila: el fondo del panel, si la fila es del panel.</summary>
    private static int Background(CapturedImage panel, int y)
    {
        var from = panel.Width / 2;
        var values = new int[panel.Width - from];
        for (var x = from; x < panel.Width; x++)
        {
            var (r, g, b) = panel.Pixel(x, y);
            values[x - from] = Math.Max(r, Math.Max(g, b));
        }
        Array.Sort(values);
        return values[values.Length / 4];
    }

    /// <summary>Primera fila de la banda del panel, pegada (o casi) al borde de abajo; null si abajo no hay panel.</summary>
    public static int? PanelTop(CapturedImage panel)
    {
        var bottom = -1;
        for (var y = panel.Height - 1; y >= Math.Max(0, panel.Height - SkipBelow); y--)
        {
            if (Background(panel, y) < PanelBackgroundMax)
            {
                bottom = y;
                break;
            }
        }
        if (bottom < 0)
            return null;
        var top = bottom;
        while (top > 0 && Background(panel, top - 1) < PanelBackgroundMax)
            top--;
        return bottom - top + 1 >= MinBand ? top : null;
    }

    /// <summary>Los renglones que lee el OCR, antes de reconocer los puntajes.</summary>
    public Task<IReadOnlyList<OcrTextLine>> ReadRawAsync(CapturedImage panel) => ocr.ReadPreparedLinesAsync(Prepare(panel), Zoom, Margin);

    /// <summary>
    /// "UKyriokes: 779/779" → 779. El segundo número es el puntaje más alto del equipo (en 1 contra 1, el mismo; en la
    /// 20261020-205150, 4 contra 4: "Otakar I: 1330/1343"): el primero no puede pasarlo, así se descartan lecturas dudosas.
    /// </summary>
    public static int? Parse(string text)
    {
        var match = ScoreText().Match(text);
        if (!match.Success)
            return null;
        var score = int.Parse(match.Groups["a"].Value, CultureInfo.InvariantCulture);
        return score <= int.Parse(match.Groups["b"].Value, CultureInfo.InvariantCulture) ? score : null;
    }

    [GeneratedRegex(@"[:.]\s*(?<a>\d{1,6})\s*/\s*(?<b>\d{1,6})")]
    private static partial Regex ScoreText();

    /// <summary>Tono de cada color de jugador (1 a 8), en grados; el 7 es el gris.</summary>
    private static readonly (int Slot, double Hue)[] PlayerHues =
        [(1, 220), (2, 0), (3, 120), (4, 58), (5, 180), (6, 300), (8, 30)];

    /// <summary>
    /// El jugador por el color de su texto: el tono más común de los píxeles claros y con color del renglón (sin el
    /// ícono de edad). Si casi no tienen color, es el gris (7).
    /// </summary>
    public static int? SlotOf(CapturedImage panel, int top, int bottom)
    {
        var votes = new int[9];
        var grays = 0;
        var right = Math.Max(1, panel.Width - IconWidth);
        // Solo el medio del renglón (arriba y abajo asoman el borde del panel y el renglón vecino) y desde donde
        // empieza el panel oscuro: a la izquierda, con nombres cortos, se ve el mapa, que tiene tierra naranja.
        var margin = (bottom - top) / 5;
        var left = PanelStart(panel, (top + bottom) / 2, right);
        for (var y = top + margin; y < bottom - margin; y++)
        {
            for (var x = left; x < right; x++)
            {
                var (r, g, b) = panel.Pixel(x, y);
                var max = Math.Max(r, Math.Max(g, b));
                var min = Math.Min(r, Math.Min(g, b));
                if (max < 110)
                    continue;
                var saturation = (max - min) / (double)max;
                if (saturation < 0.15)
                {
                    if (max is > 90 and < 200)
                        grays++;
                    continue;
                }
                if (saturation < 0.4)
                    continue; // tierra del mapa, bordes del panel
                var hue = Hue(r, g, b, max, min);
                var slot = PlayerHues.MinBy(p => HueDistance(p.Hue, hue));
                if (HueDistance(slot.Hue, hue) <= 20)
                    votes[slot.Slot]++;
            }
        }
        var best = Enumerable.Range(1, 8).MaxBy(s => votes[s]);
        if (votes[best] >= 20)
            return best;
        return grays >= 40 ? 7 : null;
    }

    /// <summary>Primera columna del panel en ese renglón: donde empiezan 10 píxeles casi todos oscuros.</summary>
    private static int PanelStart(CapturedImage panel, int y, int right)
    {
        for (var x = 0; x + 10 < right; x++)
        {
            var dark = 0;
            for (var i = 0; i < 10; i++)
            {
                var (r, g, b) = panel.Pixel(x + i, y);
                if (Math.Max(r, Math.Max(g, b)) < 80)
                    dark++;
            }
            if (dark >= 8)
                return x;
        }
        return 0;
    }

    /// <summary>El ícono de edad (a la derecha) es el naranja de la Edad Oscura.</summary>
    public static bool IsDarkAge(CapturedImage panel, int top, int bottom)
    {
        int orange = 0, total = 0;
        for (var y = top; y < bottom; y++)
        {
            for (var x = Math.Max(0, panel.Width - 36); x < panel.Width - 6; x++)
            {
                var (r, g, b) = panel.Pixel(x, y);
                total++;
                if (r > 170 && g is > 100 and < 200 && b < 120 && r - b > 90)
                    orange++;
            }
        }
        return total > 0 && orange >= total * 0.2;
    }

    private static double Hue(byte r, byte g, byte b, int max, int min)
    {
        double d = max - min;
        var h = max == r ? (g - b) / d % 6 : max == g ? (b - r) / d + 2 : (r - g) / d + 4;
        return (h * 60 + 360) % 360;
    }

    private static double HueDistance(double a, double b)
    {
        var d = Math.Abs(a - b) % 360;
        return Math.Min(d, 360 - d);
    }
}
