using System.Text.RegularExpressions;
using BoCoach.Core.Data;
using BoCoach.Vision.Capture;
using BoCoach.Vision.Ocr;

namespace BoCoach.Vision.Loading;

/// <summary>
/// Lee la pantalla de carga con el OCR de Windows: primero el título (para saber si es esa pantalla) y después cada
/// renglón de jugador (equipo y nombre, civ) y el mapa. Pasa una vez por partida, así que no importa que sean varias
/// pasadas del OCR (unos 20 ms cada una). Cada celda se lleva al tamaño del diseño del juego (el doble de 1080p)
/// antes de leerla: a 1440p, agrandada ×2, el OCR no leía un nombre que a ×1 y a ×3 sí.
/// </summary>
public sealed partial class LoadingScreenReader(OcrReader ocr, GameData data, string language = "en")
{
    /// <summary>Margen que se agrega a cada celda antes del OCR (lee mejor el texto que no toca el borde).</summary>
    private const int Margin = 16;

    /// <summary>Espacio entre el equipo y el nombre al leerlos juntos.</summary>
    private const int Gap = 40;

    /// <summary>Si la captura de la pantalla entera (el área del juego) es la pantalla de carga.</summary>
    public async Task<bool> IsLoadingScreenAsync(CapturedImage screen) =>
        LoadingScreen.IsTitle(await ReadCellAsync(screen, LoadingScreen.Title(screen.Width, screen.Height)));

    /// <summary>
    /// Lo mismo, con solo la caja del título (<see cref="LoadingScreen.Title"/>) de una pantalla de
    /// <paramref name="screenHeight"/> de alto: en vivo se mira eso cada tanto mientras no se ve el reloj, que es
    /// mucho más barato que capturar la pantalla entera.
    /// </summary>
    public async Task<bool> IsTitleAsync(CapturedImage title, int screenHeight) =>
        LoadingScreen.IsTitle(string.Join(" ", await OcrAsync(Prepared(title, screenHeight))));

    /// <summary>
    /// Los jugadores y el mapa, o null si la captura no es la pantalla de carga. <paramref name="screen"/> es el área
    /// entera del juego.
    /// </summary>
    public async Task<LoadingReading?> ReadAsync(CapturedImage screen)
    {
        if (!await IsLoadingScreenAsync(screen))
            return null;
        var (w, h) = (screen.Width, screen.Height);
        var players = new List<LoadingPlayer>();
        for (var slot = 1; slot <= LoadingScreen.MaxPlayers; slot++)
        {
            var nameBox = LoadingScreen.Name(slot, w, h);
            if (nameBox.IsEmpty || !HasText(screen.Crop(nameBox)))
                continue;
            // El OCR no lee un dígito suelto: el equipo se lee pegado al nombre ("1 Ferrus").
            var joined = Beside(Prepared(screen, LoadingScreen.Team(slot, w, h)), Prepared(screen, nameBox));
            var (team, name) = SplitTeam(string.Join(" ", await OcrAsync(joined)));
            if (name.Length == 0)
                continue;
            var civText = await ReadCellAsync(screen, LoadingScreen.Civ(slot, w, h));
            var color = ColorOf(screen.Crop(LoadingScreen.Color(slot, w, h)));
            players.Add(new LoadingPlayer(slot, name, civText, LoadingScreen.MatchCiv(civText, data, language), team, color));
        }
        var map = LoadingScreen.MapFrom(await ReadLinesAsync(screen, LoadingScreen.Map(w, h)));
        return new LoadingReading(players, map);
    }

    private async Task<string> ReadCellAsync(CapturedImage screen, PixelRect box) =>
        string.Join(" ", await ReadLinesAsync(screen, box)).Trim();

    private async Task<IReadOnlyList<string>> ReadLinesAsync(CapturedImage screen, PixelRect box) =>
        box.IsEmpty ? [] : await OcrAsync(Prepared(screen, box));

    private static CapturedImage Prepared(CapturedImage screen, PixelRect box) => Prepared(screen.Crop(box), screen.Height);

    private async Task<IReadOnlyList<string>> OcrAsync(CapturedImage prepared) =>
        (await ocr.ReadPreparedLinesAsync(prepared.Padded(Margin), 1, Margin)).Select(l => l.Text).ToList();

    /// <summary>La celda llevada al tamaño del diseño (3840×2160) y con texto oscuro sobre claro.</summary>
    private static CapturedImage Prepared(CapturedImage cell, int screenHeight) =>
        DarkOnLight(cell.Resample(LoadingScreen.DesignHeight / screenHeight));

    /// <summary>Dos celdas del mismo renglón una al lado de la otra, con fondo blanco entre medio.</summary>
    private static CapturedImage Beside(CapturedImage left, CapturedImage right)
    {
        var (w, h) = (left.Width + Gap + right.Width, Math.Max(left.Height, right.Height));
        var bgra = Enumerable.Repeat((byte)255, w * h * 4).ToArray();
        void Copy(CapturedImage part, int x0)
        {
            for (var y = 0; y < part.Height; y++)
                Buffer.BlockCopy(part.Bgra, y * part.Width * 4, bgra, (y * w + x0) * 4, part.Width * 4);
        }
        Copy(left, 0);
        Copy(right, left.Width + Gap);
        return new CapturedImage(w, h, bgra);
    }

    /// <summary>
    /// "1 Ferrus" → equipo 1 y nombre "Ferrus". El equipo es un primer carácter suelto: un dígito de 1 a 8, o una
    /// letra que el OCR confunde con el 1 ("l", "I", "|") o con el 2 ("Z"). Sin equipo, el juego muestra "-", que se
    /// descarta.
    /// </summary>
    public static (int? Team, string Name) SplitTeam(string text)
    {
        var match = TeamAndName().Match(text.Trim());
        int? team = match.Groups["team"].Success
            ? match.Groups["team"].Value switch
            {
                "l" or "I" or "|" => 1,
                "Z" or "z" => 2,
                var digit => int.Parse(digit),
            }
            : null;
        return (team, match.Groups["name"].Value.Trim());
    }

    [GeneratedRegex(@"^(?:(?<team>[1-8lI|Zz])\s+|[-_—–~.]+\s*)?(?<name>.*)$")]
    private static partial Regex TeamAndName();

    /// <summary>
    /// Gris con texto oscuro sobre fondo claro, que es como mejor lee el OCR. El texto del juego es rojo oscuro; si
    /// el fondo resulta oscuro (no se vio la pantalla real todavía), se invierte.
    /// </summary>
    public static CapturedImage DarkOnLight(CapturedImage image)
    {
        var gray = new byte[image.Width * image.Height];
        long sum = 0;
        for (var i = 0; i < gray.Length; i++)
        {
            var (b, g, r) = (image.Bgra[i * 4], image.Bgra[i * 4 + 1], image.Bgra[i * 4 + 2]);
            gray[i] = (byte)((r * 299 + g * 587 + b * 114) / 1000);
            sum += gray[i];
        }
        var invert = sum < gray.Length * 128L;
        var output = new byte[image.Bgra.Length];
        for (var i = 0; i < gray.Length; i++)
        {
            var v = invert ? (byte)(255 - gray[i]) : gray[i];
            (output[i * 4], output[i * 4 + 1], output[i * 4 + 2], output[i * 4 + 3]) = (v, v, v, 255);
        }
        return new CapturedImage(image.Width, image.Height, output);
    }

    /// <summary>
    /// Si la celda tiene algo escrito: al menos un 1 % de píxeles que se alejan bastante del brillo típico del fondo.
    /// Los renglones de los jugadores que no están quedan vacíos y no se pasan por el OCR.
    /// </summary>
    public static bool HasText(CapturedImage cell)
    {
        var gray = DarkOnLight(cell);
        var histogram = new int[256];
        for (var i = 0; i < gray.Bgra.Length; i += 4)
            histogram[gray.Bgra[i]]++;
        var pixels = cell.Width * cell.Height;
        var (median, seen) = (0, 0);
        while (median < 255 && (seen += histogram[median]) < pixels / 2)
            median++;
        var ink = histogram.Take(Math.Max(0, median - 60)).Sum();
        return ink >= pixels / 100;
    }

    /// <summary>El color del cuadrado: promedio del borde (sin el centro, donde va el número), dejando afuera el contorno.</summary>
    private static (byte R, byte G, byte B) ColorOf(CapturedImage square)
    {
        long r = 0, g = 0, b = 0, n = 0;
        var (w, h) = (square.Width, square.Height);
        for (var y = h / 10; y < h - h / 10; y++)
            for (var x = w / 10; x < w - w / 10; x++)
            {
                if (x > w / 4 && x < w * 3 / 4 && y > h / 4 && y < h * 3 / 4)
                    continue;
                var p = square.Pixel(x, y);
                (r, g, b, n) = (r + p.R, g + p.G, b + p.B, n + 1);
            }
        return n == 0 ? ((byte)0, (byte)0, (byte)0) : ((byte)(r / n), (byte)(g / n), (byte)(b / n));
    }
}
