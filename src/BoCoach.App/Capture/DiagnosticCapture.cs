using System.Globalization;
using System.IO;
using System.Text;
using BoCoach.Core;
using BoCoach.Core.Data;
using BoCoach.Vision.Capture;
using BoCoach.Vision.Hud;
using BoCoach.Vision.Ocr;
using BoCoach.Vision.Regions;

namespace BoCoach.App.Capture;

/// <summary>Los dos archivos de una captura de diagnóstico.</summary>
public sealed record DiagnosticFiles(string Image, string Report);

/// <summary>
/// Captura de diagnóstico (pedido del 2026-10-20: las capturas de los amigos llegaban comprimidas por WhatsApp y sin
/// saber qué vio la app): la pantalla entera sin pérdida y, al lado, un .txt con la versión, el juego, el idioma y los
/// OCR instalados, el mod, la calibración sola y lo que se lee de cada caja. Solo mira la pantalla, como siempre.
/// </summary>
public static class DiagnosticCapture
{
    public static string DefaultFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BoCoach", "diagnostico");

    /// <param name="saved">La calibración guardada (si la calibración sola no encuentra el HUD, se lee con esta).</param>
    /// <param name="savedPath">regions.json, para decir de cuándo es.</param>
    public static async Task<DiagnosticFiles> SaveAsync(
        IScreenSource screen, RegionLayout saved, string savedPath, HudFonts fonts, GameLanguage language,
        string folder, Func<string?> findGame, string profilesRoot, DateTime now)
    {
        var area = screen.CaptureArea;
        var image = screen.Capture(area);
        Directory.CreateDirectory(folder);
        var name = now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var files = new DiagnosticFiles(Path.Combine(folder, $"{name}.png"), Path.Combine(folder, $"{name}.txt"));
        Png.Write(image, files.Image);

        var report = new StringBuilder();
        void Line(string text = "") => report.AppendLine(text);

        Line("Captura de diagnóstico de AoE2 BO Coach");
        Line($"Fecha: {now:yyyy-MM-dd HH:mm:ss}");
        Line($"Versión: {AppVersion.Text}");
        Line($"Windows: {Environment.OSVersion.VersionString}");
        Line($"Juego: {(screen.GameFound ? "encontrado" : "no encontrado (se capturó la pantalla principal)")}, área {area.Width}×{area.Height} en ({area.X}, {area.Y})");
        Line($"Carpeta del juego: {findGame() ?? "no encontrada"}");
        var ocr = language.CreateOcr();
        var installed = OcrReader.AvailableLanguages();
        Line($"Idioma del juego elegido: {language.Label}. OCR instalados: {(installed.Count > 0 ? string.Join(", ", installed) : "ninguno")}. OCR que usa: {ocr?.Language ?? "ninguno"}");
        Line();
        Line($"Mod {HudMod.Name}:");
        foreach (var profile in HudMod.Describe(profilesRoot))
            Line($"  {profile}");
        Line();

        Line(File.Exists(savedPath)
            ? $"Calibración guardada: {File.GetLastWriteTime(savedPath):yyyy-MM-dd HH:mm}, para {saved.ReferenceWidth}×{saved.ReferenceHeight}, HUD a escala {saved.HudScale:0.00}"
            : "Calibración guardada: ninguna (posiciones aproximadas)");
        RegionLayout layout;
        if (AutoCalibration.Detect(image, saved) is { } found)
        {
            Line($"Calibración sola: {found.Squares.Count} cuadrados y el reloj, HUD a escala {found.Scale:0.00}");
            Line($"  cuadrados: {string.Join(" ", found.Squares.Select(Rect))}");
            layout = found.Layout;
        }
        else
        {
            Line("Calibración sola: no encontró el HUD del mod. Rectángulos de color parejo de la franja de arriba:");
            foreach (var (rect, band, color) in AutoCalibration.Candidates(image).Take(30))
                Line($"  {Rect(rect)} color {color.R},{color.G},{color.B} franja {band ?? "-"}");
            layout = saved;
        }
        Line("Cajas con que se lee:");
        foreach (var region in RegionNames.All)
            Line($"  {region,-17} {Rect(layout.PixelsOf(region, area.Width, area.Height))}");
        Line();

        // Lo mismo que en vivo: cada caja recortada y llevada a los píxeles de 1080p de las letras.
        var boxes = RegionNames.All.ToDictionary(r => r, r => image.Crop(layout.PixelsOf(r, area.Width, area.Height)).AtHudScale(layout.HudScale));
        var reading = new HudReader(fonts).Read(boxes.GetValueOrDefault);
        Line("Lectura:");
        Line($"  reloj       {(reading.Clock is { } clock ? GameClock.Format(clock) : "-")}");
        foreach (var resource in new[] { Resource.Wood, Resource.Food, Resource.Gold, Resource.Stone })
            Line($"  {Label(resource),-11} stock {Value(reading.Stock(resource))} · aldeanos {Value(reading.VillagersOn?.GetValueOrDefault(resource, -1) is >= 0 and var v ? v : null)}");
        Line($"  población   aldeanos {Value(reading.Villagers)} · {Value(reading.Population)}/{Value(reading.PopulationCap)}");
        Line($"  inactivos   {Value(reading.IdleVillagers)}");
        if (reading.Clipped.Count > 0)
            Line($"  cortados    {string.Join(", ", reading.Clipped)}");
        if (fonts.QueueIcons is { } icons)
        {
            var queue = icons.Read(boxes[RegionNames.ProductionQueue]);
            Line($"  cola        {(queue.Count == 0 ? "vacía" : string.Join(" ", queue.Select(q => q.Waiting ? $"{q.Key} (espera)" : q.Key)))}");
        }
        if (ocr is not null)
        {
            var age = await ocr.ReadAsync(boxes[RegionNames.Age]);
            Line($"  edad        \"{age.ReplaceLineEndings(" ")}\" → {AgeText.Parse(age)?.ToString() ?? "-"}");
            var log = await Vision.Events.EventLogReader.ReadLinesAsync(ocr, boxes[RegionNames.EventLog]);
            Line($"  log         {(log.Count == 0 ? "sin texto" : string.Join(" | ", log.Select(l => $"\"{l.Text}\"")))}");
        }
        File.WriteAllText(files.Report, report.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        return files;
    }

    private static string Rect(PixelRect r) => $"x {r.X} y {r.Y} {r.Width}×{r.Height}";

    private static string Value(int? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "-";

    private static string Label(Resource resource) => resource switch
    {
        Resource.Wood => "madera",
        Resource.Food => "comida",
        Resource.Gold => "oro",
        _ => "piedra",
    };
}
