using System.Text.Json;
using BoCoach.Vision.Capture;
using BoCoach.Vision.Recording;
using BoCoach.Vision.Regions;

namespace BoCoach.Vision.Tests;

public sealed class SessionRecorderTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("bocoach-rec-");

    public void Dispose() => _root.Delete(recursive: true);

    /// <summary>Pantalla falsa de 1920×1080 que recuerda qué rectángulos le pidieron.</summary>
    private sealed class FakeScreen : IScreenSource
    {
        public List<PixelRect> Requests { get; } = [];
        public PixelRect CaptureArea => new(0, 0, 1920, 1080);
        public bool GameFound => true;

        public CapturedImage Capture(PixelRect rect)
        {
            Requests.Add(rect);
            return PngTests.Gradient(rect.Width, rect.Height);
        }
    }

    [Fact]
    public void Graba_una_imagen_por_region_y_captura_mas_el_indice()
    {
        var screen = new FakeScreen();
        using var recorder = new SessionRecorder(screen, RegionLayout.Default, _root.FullName);

        recorder.Open();
        recorder.CaptureFrame();
        recorder.CaptureFrame();
        recorder.Stop();

        var folder = recorder.Folder!;
        Assert.Equal(2, recorder.Frames);
        Assert.Equal(2 * RegionNames.All.Count, Directory.EnumerateFiles(folder, "*.png").Count());
        Assert.True(File.Exists(Path.Combine(folder, SessionRecorder.FileName(1, RegionNames.Clock))));
        var index = File.ReadAllLines(Path.Combine(folder, "frames.csv"));
        Assert.Equal(3, index.Length);
        Assert.Equal("frame,time_ms", index[0]);
        Assert.StartsWith("0,", index[1]);
        Assert.StartsWith("1,", index[2]);

        var food = RegionLayout.Default.PixelsOf(RegionNames.Food, 1920, 1080);
        Assert.Contains(food, screen.Requests);
        var image = Png.Read(Path.Combine(folder, SessionRecorder.FileName(0, RegionNames.Food)));
        Assert.Equal((food.Width, food.Height), (image.Width, image.Height));
    }

    // B4 (b): los PNG se graban tal cual (sirven para armar letras) y se llevan a 1080p recién al leerlos.
    [Fact]
    public void Con_el_HUD_a_otra_escala_graba_la_captura_tal_cual_y_la_reescala_al_leerla()
    {
        using var recorder = new SessionRecorder(new FakeScreen(), RegionLayout.Default.WithHudScale(1.5), _root.FullName);
        recorder.Open();
        recorder.CaptureFrame();
        recorder.Stop();

        var food = RegionLayout.Default.PixelsOf(RegionNames.Food, 1920, 1080);
        var raw = Png.Read(Path.Combine(recorder.Folder!, SessionRecorder.FileName(0, RegionNames.Food)));
        Assert.Equal((food.Width, food.Height), (raw.Width, raw.Height));

        var session = RecordedSession.Load(recorder.Folder!);
        var read = session.Image(0, RegionNames.Food);
        Assert.Equal(1.5, session.HudScale);
        Assert.Equal(((int)Math.Round(food.Width / 1.5), (int)Math.Round(food.Height / 1.5)), (read.Width, read.Height));
    }

    [Fact]
    public void Sin_espacio_en_el_disco_no_escribe_y_sigue_cuando_se_libera()
    {
        long free = SessionRecorder.MinFreeBytes - 1;
        using var recorder = new SessionRecorder(new FakeScreen(), RegionLayout.Default, _root.FullName) { FreeSpace = _ => free };
        recorder.Open();

        recorder.CaptureFrame();
        recorder.CaptureFrame();

        Assert.Equal(0, recorder.Frames);
        Assert.True(recorder.PausedForSpace);
        Assert.Contains("disco", recorder.LastError);
        Assert.Empty(Directory.GetFiles(recorder.Folder!, "*.png"));

        free = SessionRecorder.MinFreeBytes * 10;
        recorder.CaptureFrame();

        Assert.Equal(1, recorder.Frames);
        Assert.False(recorder.PausedForSpace);
    }

    [Fact]
    public void Una_grabacion_vieja_sin_escala_se_lee_a_escala_1()
    {
        using var recorder = new SessionRecorder(new FakeScreen(), RegionLayout.Default, _root.FullName);
        recorder.Open();
        recorder.Stop();
        var path = Path.Combine(recorder.Folder!, "session.json");
        File.WriteAllText(path, File.ReadAllText(path).Replace("\"hud_scale\"", "\"otra_cosa\""));

        Assert.Equal(1, RecordedSession.Load(recorder.Folder!).HudScale);
    }

    [Fact]
    public void Session_json_guarda_pantalla_y_regiones_en_pixeles()
    {
        using var recorder = new SessionRecorder(new FakeScreen(), RegionLayout.Default, _root.FullName);
        recorder.Open();
        recorder.Stop();

        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(recorder.Folder!, "session.json")));
        var root = json.RootElement;

        Assert.Equal(1920, root.GetProperty("area")[2].GetInt32());
        Assert.True(root.GetProperty("game_found").GetBoolean());
        Assert.Equal(500, root.GetProperty("interval_ms").GetInt32());
        var pixels = root.GetProperty("regions").GetProperty(RegionNames.Wood).GetProperty("pixels");
        Assert.Equal(RegionLayout.Default.PixelsOf(RegionNames.Wood, 1920, 1080).X, pixels[0].GetInt32());
    }

    [Fact]
    public async Task Con_el_temporizador_graba_hasta_que_se_detiene()
    {
        using var recorder = new SessionRecorder(new FakeScreen(), RegionLayout.Default, _root.FullName, TimeSpan.FromMilliseconds(50));

        recorder.Start();
        // Con la máquina cargada (todos los tests en paralelo, el CI) el loop puede tardar en arrancar: se espera la
        // primera captura en vez de un tiempo fijo. Con 400 ms fijos, un Stop antes de que arrancara dejaba 0 capturas.
        var waited = System.Diagnostics.Stopwatch.StartNew();
        while (recorder.Frames < 2 && waited.Elapsed < TimeSpan.FromSeconds(10))
            await Task.Delay(20);
        recorder.Stop();
        var frames = recorder.Frames;
        await Task.Delay(150);

        Assert.InRange(frames, 2, 1000);
        Assert.Equal(frames, recorder.Frames);
        Assert.False(recorder.IsRecording);
        Assert.Null(recorder.LastError);
    }
}
