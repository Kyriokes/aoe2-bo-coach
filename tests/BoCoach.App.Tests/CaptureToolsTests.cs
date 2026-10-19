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

    private CaptureToolsViewModel Tools(FakeHost host, Func<string?>? findGame = null) =>
        new(new FakeScreen(), host, RegionsPath, RecordingsRoot, findGame ?? (() => null), Path.Combine(_dir.FullName, "perfiles"));

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
        await Task.Delay(700);
        tools.Tick();
        Assert.StartsWith("Grabando 0:0", tools.RecordingStatus);

        tools.ToggleRecordingCommand.Execute(null);

        Assert.False(tools.IsRecording);
        Assert.StartsWith("Grabación guardada:", tools.RecordingStatus);
        var folder = Assert.Single(Directory.EnumerateDirectories(RecordingsRoot));
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
