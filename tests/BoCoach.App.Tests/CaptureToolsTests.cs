using System.Text.Json;
using BoCoach.App.Capture;
using BoCoach.Vision.Capture;
using BoCoach.Vision.Regions;

namespace BoCoach.App.Tests;

public sealed class CaptureToolsTests : IDisposable
{
    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("bocoach-tools-");

    public void Dispose() => _dir.Delete(recursive: true);

    private string RegionsPath => Path.Combine(_dir.FullName, "regions.json");
    private string RecordingsRoot => Path.Combine(_dir.FullName, "recordings");

    private sealed class FakeScreen : IScreenSource
    {
        public PixelRect CaptureArea => new(1920, 0, 2560, 1440);
        public bool GameFound => true;

        public CapturedImage Capture(PixelRect rect) =>
            new(rect.Width, rect.Height, new byte[rect.Width * rect.Height * 4]);
    }

    private sealed class FakeHost : ICaptureHost
    {
        public Func<RegionLayout, RegionLayout?> OnCalibrate { get; set; } = _ => null;
        public string? OpenedFolder { get; private set; }

        public (string Text, bool Warning)? Message { get; private set; }

        public RegionLayout? Calibrate(RegionLayout current) => OnCalibrate(current);
        public void OpenFolder(string path) => OpenedFolder = path;
        public void ShowMessage(string title, string text, bool warning) => Message = (text, warning);
    }

    private string DiagnosticsFolder => Path.Combine(_dir.FullName, "diagnostico");

    private CaptureToolsViewModel Tools(FakeHost host, Func<string?>? findGame = null, IScreenSource? screen = null) =>
        new(screen ?? new FakeScreen(), host, RegionsPath, RecordingsRoot, findGame ?? (() => null), Path.Combine(_dir.FullName, "perfiles"),
            diagnosticsFolder: DiagnosticsFolder) { DiagnosticDelay = TimeSpan.Zero };

    private static readonly string HudFixture = Path.Combine(AppContext.BaseDirectory, "fixtures", "hud-20261018-132830");

    /// <summary>Las cajas en píxeles con que se grabó el fixture del HUD (session.json).</summary>
    private static Dictionary<string, PixelRect> RecordedBoxes()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(HudFixture, "session.json")));
        return doc.RootElement.GetProperty("regions").EnumerateObject().ToDictionary(
            r => r.Name,
            r => { var p = r.Value.GetProperty("pixels").EnumerateArray().Select(v => v.GetInt32()).ToArray(); return new PixelRect(p[0], p[1], p[2], p[3]); });
    }

    /// <summary>Una pantalla de 1920×1080 con el mod: fondo liso y las cajas grabadas (frame 100) donde la calibración manual las tenía.</summary>
    private sealed class HudScreen : IScreenSource
    {
        private readonly CapturedImage _screen;

        public HudScreen()
        {
            const int w = 1920, h = 1080;
            var bgra = new byte[w * h * 4];
            for (var i = 0; i < bgra.Length; i += 4)
                (bgra[i], bgra[i + 1], bgra[i + 2], bgra[i + 3]) = (52, 58, 70, 255);
            foreach (var (name, box) in RecordedBoxes())
            {
                var path = Path.Combine(HudFixture, $"000100_{name}.png");
                if (!File.Exists(path))
                    continue;
                var image = Png.Read(path);
                for (var y = 0; y < image.Height; y++)
                    Buffer.BlockCopy(image.Bgra, y * image.Width * 4, bgra, ((box.Y + y) * w + box.X) * 4, image.Width * 4);
            }
            _screen = new CapturedImage(w, h, bgra);
        }

        public PixelRect CaptureArea => new(0, 0, 1920, 1080);
        public bool GameFound => true;
        public CapturedImage Capture(PixelRect rect) => _screen.Crop(rect);
    }

    // Hoja de ruta B4: con el mod, la calibración se hace sola al empezar el coach; sin el mod queda la que había.
    [Fact]
    public void Con_el_mod_en_pantalla_se_calibra_sola_y_lo_guarda()
    {
        var tools = Tools(new FakeHost(), screen: new HudScreen());

        Assert.True(tools.TryAutoCalibrate());

        Assert.True(tools.IsCalibrated);
        Assert.Contains("sola", tools.CalibrationStatus);
        var boxes = RecordedBoxes();
        var wood = tools.Layout.PixelsOf(RegionNames.Wood, 1920, 1080);
        Assert.InRange(wood.X, boxes["wood"].X - 3, boxes["wood"].X + 3);
        Assert.InRange(wood.Y, boxes["wood"].Y - 3, boxes["wood"].Y + 3);
        var clock = tools.Layout.PixelsOf(RegionNames.Clock, 1920, 1080);
        Assert.InRange(clock.X, boxes["clock"].X - 3, boxes["clock"].X + 3);
        Assert.InRange(clock.Y, boxes["clock"].Y - 3, boxes["clock"].Y + 3);
    }

    /// <summary>Una captura de pantalla entera guardada (las de los amigos, en tests/fixtures/screens).</summary>
    private sealed class PngScreen(string name) : IScreenSource
    {
        private readonly CapturedImage _screen = Png.Read(Path.Combine(AppContext.BaseDirectory, "fixtures", "screens", name));

        public PixelRect CaptureArea => new(0, 0, _screen.Width, _screen.Height);
        public bool GameFound => true;
        public CapturedImage Capture(PixelRect rect) => _screen.Crop(rect);
    }

    // B4 (b): con el HUD más grande (captura del amigo, escala 1,09) las capturas se reescalan al leerlas, así que la
    // escala queda guardada y el estado ya no avisa que puede leer mal.
    [Fact]
    public void Con_el_HUD_mas_grande_guarda_la_escala_y_no_avisa_que_lee_mal()
    {
        var tools = Tools(new FakeHost(), screen: new PngScreen("amigo-20261020-1080p-panel-negro.png"));

        Assert.True(tools.TryAutoCalibrate());

        Assert.InRange(tools.Layout.HudScale, 1.05, 1.15);
        Assert.Contains("escala 1", tools.CalibrationStatus);
        Assert.DoesNotContain("leer mal", tools.CalibrationStatus);
    }

    // 4.3 del traspaso del 2026-10-20: las capturas de los amigos llegaban comprimidas y sin saber qué vio la app.
    [Fact]
    public async Task La_captura_de_diagnostico_guarda_la_pantalla_sin_perdida_y_lo_que_encontro()
    {
        var tools = Tools(new FakeHost(), screen: new HudScreen());

        await tools.DiagnosticCommand.ExecuteAsync(null);

        var png = Assert.Single(Directory.GetFiles(DiagnosticsFolder, "*.png"));
        Assert.Equal((1920, 1080), (Png.Read(png).Width, Png.Read(png).Height));
        var report = File.ReadAllText(Path.ChangeExtension(png, ".txt"));
        Assert.Contains("6 cuadrados", report);
        Assert.Contains("Versión: v", report);
        Assert.Contains("OCR instalados:", report);
        Assert.Contains("BoCoach HUD", report);
        Assert.Contains(DiagnosticsFolder, tools.DiagnosticStatus);
        Assert.Contains("mandá esa carpeta", tools.DiagnosticStatus);
    }

    [Fact]
    public async Task La_captura_de_diagnostico_del_amigo_dice_lo_que_lee_con_el_HUD_mas_grande()
    {
        var tools = Tools(new FakeHost(), screen: new PngScreen("amigo-20261020-1080p-panel-negro.png"));

        await tools.DiagnosticCommand.ExecuteAsync(null);

        var report = File.ReadAllText(Assert.Single(Directory.GetFiles(DiagnosticsFolder, "*.txt")));
        Assert.Contains("escala 1", report);
        Assert.Contains("stock 460 · aldeanos 12", report);
        Assert.Contains("reloj       4:15", report);
    }

    // B2: la captura de diagnóstico en la pantalla de carga dice qué leyó de cada jugador (para verificar la lectura
    // con la pantalla real, que todavía no se vio).
    [Fact]
    public async Task La_captura_de_diagnostico_en_la_pantalla_de_carga_lista_los_jugadores()
    {
        if (Vision.Ocr.OcrReader.TryCreate() is null)
            return; // esta máquina no tiene el OCR en inglés instalado (puede pasar en CI)
        var data = Core.Data.GameDataLoader.LoadLatest(Path.Combine(AppContext.BaseDirectory, "data", "game_data"));
        var tools = new CaptureToolsViewModel(new PngScreen("carga-sintetica-en-1080p.png"), new FakeHost(), RegionsPath, RecordingsRoot,
            () => null, Path.Combine(_dir.FullName, "perfiles"), diagnosticsFolder: DiagnosticsFolder,
            gameData: () => data, playerName: () => "Bombarda88") { DiagnosticDelay = TimeSpan.Zero };

        await tools.DiagnosticCommand.ExecuteAsync(null);

        var report = File.ReadAllText(Assert.Single(Directory.GetFiles(DiagnosticsFolder, "*.txt")));
        Assert.Contains("Pantalla de carga (mapa: Arabia)", report);
        Assert.Contains("→ franks  color 0,190,0  ← vos", report);
        Assert.Contains("→ malians", report);
    }

    [Fact]
    public void Sin_el_mod_en_pantalla_queda_la_calibracion_que_habia()
    {
        var manual = RegionLayout.Default.With(RegionNames.Clock, new RegionRect(0.5, 0.5, 0.05, 0.02));
        manual.Save(RegionsPath);
        var tools = Tools(new FakeHost());

        Assert.False(tools.TryAutoCalibrate());

        Assert.Equal(manual.Regions[RegionNames.Clock], tools.Layout.Regions[RegionNames.Clock]);
        Assert.Contains("No encontré", tools.CalibrationStatus);
    }

    [Fact]
    public void Instalar_el_mod_sin_el_juego_avisa()
    {
        var host = new FakeHost();

        Tools(host).InstallModCommand.Execute(null);

        Assert.True(host.Message?.Warning);
        Assert.Contains("No encontré Age of Empires", host.Message?.Text);
    }

    [Fact]
    public void Instalar_el_mod_cuenta_en_que_perfil_quedo()
    {
        var game = HudModTests.FakeGame(_dir.FullName);
        Directory.CreateDirectory(Path.Combine(_dir.FullName, "perfiles", "76561198000000000", "mods"));
        var host = new FakeHost();

        Tools(host, () => game).InstallModCommand.Execute(null);

        Assert.False(host.Message?.Warning, host.Message?.Text);
        Assert.Contains("quedó instalado", host.Message?.Text);
        Assert.Contains("Perfil 76561198000000000: recursos de original", host.Message?.Text);
    }

    [Fact]
    public void Sin_calibrar_avisa_que_usa_posiciones_aproximadas()
    {
        var tools = Tools(new FakeHost());

        Assert.False(tools.IsCalibrated);
        Assert.Contains("sin calibrar", tools.CalibrationStatus);
        Assert.Same(RegionLayout.Default, tools.Layout);
    }

    [Fact]
    public void Calibrar_guarda_regions_json()
    {
        var host = new FakeHost
        {
            OnCalibrate = current => current
                .With(RegionNames.Food, new RegionRect(0.2, 0.01, 0.04, 0.02))
                .WithReference(2560, 1440),
        };
        var tools = Tools(host);

        tools.CalibrateCommand.Execute(null);

        Assert.True(tools.IsCalibrated);
        Assert.Contains("2560×1440", tools.CalibrationStatus);
        Assert.Equal(0.2, RegionLayout.LoadOrDefault(RegionsPath).Regions[RegionNames.Food].X);
    }

    [Fact]
    public void Cancelar_la_calibracion_no_guarda_nada()
    {
        var tools = Tools(new FakeHost());

        tools.CalibrateCommand.Execute(null);

        Assert.False(File.Exists(RegionsPath));
    }

    [Fact]
    public async Task Grabar_y_detener()
    {
        var tools = Tools(new FakeHost());

        tools.ToggleRecordingCommand.Execute(null);
        Assert.True(tools.IsRecording);
        Assert.Equal("■ Detener grabación", tools.RecordButtonText);
        // Se espera la primera captura completa (las 11 regiones) en vez de un tiempo fijo: con la máquina cargada el
        // loop de la grabación puede tardar en arrancar, y un Stop antes dejaba la carpeta sin capturas.
        var folder = Assert.Single(Directory.EnumerateDirectories(RecordingsRoot));
        var waited = System.Diagnostics.Stopwatch.StartNew();
        while (Directory.EnumerateFiles(folder, "*.png").Count() < RegionNames.All.Count && waited.Elapsed < TimeSpan.FromSeconds(10))
            await Task.Delay(20);
        tools.Tick();
        Assert.StartsWith("Grabando 0:", tools.RecordingStatus);

        tools.ToggleRecordingCommand.Execute(null);

        Assert.False(tools.IsRecording);
        Assert.StartsWith("Grabación guardada:", tools.RecordingStatus);
        Assert.True(File.Exists(Path.Combine(folder, "session.json")));
        Assert.True(Directory.EnumerateFiles(folder, "*.png").Count() >= 7);
    }

    [Fact]
    public void Informa_donde_esta_el_juego()
    {
        var tools = Tools(new FakeHost());

        tools.Tick();

        Assert.Equal("Juego abierto: 2560×1440 en (1920, 0).", tools.GameStatus);
    }

    [Fact]
    public void Abrir_la_carpeta_de_grabaciones()
    {
        var host = new FakeHost();
        var tools = Tools(host);

        tools.OpenRecordingsCommand.Execute(null);

        Assert.Equal(RecordingsRoot, host.OpenedFolder);
        Assert.True(Directory.Exists(RecordingsRoot));
    }
}
