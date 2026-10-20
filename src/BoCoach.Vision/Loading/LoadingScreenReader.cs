using BoCoach.Core.Data;
using BoCoach.Vision.Capture;
using BoCoach.Vision.Ocr;

namespace BoCoach.Vision.Loading;

/// <summary>
/// Lee la pantalla de carga con el OCR de Windows: una sola pasada sobre el panel de jugadores, llevado a 1080p
/// (a 1080p, sin agrandar, leyó los 8 nombres y civs de la captura del usuario; agrandado ×1,5 o ×2 se equivocaba
/// más). Si no aparecen al menos dos jugadores con su civ, no es la pantalla de carga.
/// </summary>
public sealed class LoadingScreenReader(OcrReader ocr, GameData data, string language = "en")
{
    /// <summary>Margen que se agrega al panel antes del OCR (lee mejor el texto que no toca el borde).</summary>
    private const int Margin = 16;

    /// <summary>Jugadores con civ que tiene que haber para que sea la pantalla de carga.</summary>
    public const int MinPlayers = 2;

    /// <summary>Los jugadores, o null si la captura de la pantalla entera (el área del juego) no es la pantalla de carga.</summary>
    public Task<LoadingReading?> ReadAsync(CapturedImage screen) =>
        ReadPanelAsync(screen.Crop(LoadingScreen.Panel(screen.Width, screen.Height)));

    /// <summary>
    /// Lo mismo con solo el panel de jugadores (<see cref="LoadingScreen.Panel"/>): en vivo se captura eso, que es
    /// mucho más barato que la pantalla entera.
    /// </summary>
    public async Task<LoadingReading?> ReadPanelAsync(CapturedImage panel)
    {
        if (panel.Width == 0 || panel.Height == 0)
            return null;
        var at1080 = panel.Resample(LoadingScreen.PanelHeight / panel.Height);
        var lines = await ocr.ReadPreparedLinesAsync(DarkOnLight(at1080).Padded(Margin), 1, Margin);
        var players = LoadingScreen.Parse(lines.Select(l => (l.Text, l.Top + l.Height / 2)),
            middle => LoadingScreen.SquareColor(at1080, middle), data, language);
        return players.Count >= MinPlayers ? new LoadingReading(players) : null;
    }

    /// <summary>
    /// Gris con texto oscuro sobre fondo claro, que es como mejor lee el OCR: el panel tiene letras claras sobre
    /// fondo oscuro, así que se invierte (si el fondo resulta claro, queda como está).
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
}
