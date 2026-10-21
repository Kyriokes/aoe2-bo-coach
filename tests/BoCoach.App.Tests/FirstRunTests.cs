using BoCoach.App.Coaching;
using BoCoach.App.FirstRun;
using BoCoach.Vision.Capture;
using BoCoach.Vision.Loading;
using BoCoach.Vision.Ocr;

namespace BoCoach.App.Tests;

/// <summary>C4: el tutorial de primer uso (pantalla de carga de una partida rápida contra la IA y calibración sola).</summary>
public sealed class FirstRunTests : IDisposable
{
    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("bocoach-firstrun-");
    private readonly CoachData _data;

    public FirstRunTests()
    {
        _data = CoachData.Load(_dir.FullName);
    }

    public void Dispose() => _dir.Delete(recursive: true);

    /// <summary>Una pantalla guardada (la de carga del usuario, enmascarada), o negra.</summary>
    private sealed class StillScreen(string? name) : IScreenSource
    {
        private readonly CapturedImage _screen = name is null
            ? new CapturedImage(1920, 1080, new byte[1920 * 1080 * 4])
            : Png.Read(Path.Combine(AppContext.BaseDirectory, "fixtures", "screens", name));

        public PixelRect CaptureArea => new(0, 0, _screen.Width, _screen.Height);
        public bool GameFound => true;
        public int Captures { get; private set; }

        public CapturedImage Capture(PixelRect rect)
        {
            Captures++;
            return _screen.Crop(rect);
        }
    }

    private FirstRunViewModel Tutorial(IScreenSource screen, Func<bool>? calibrate = null, Action<string>? save = null, string current = "") =>
        new(screen, _data.GameData, () => GameLanguage.English, calibrate ?? (() => false), save ?? (_ => { }), current);

    [Fact]
    public async Task En_la_pantalla_de_carga_lista_los_jugadores_y_propone_al_jugador_1()
    {
        if (OcrReader.TryCreate() is null)
            return; // sin el OCR en inglés (puede pasar en CI)
        var tutorial = Tutorial(new StillScreen("carga-ia-en-1080p.png"));
        tutorial.ToggleWatchingCommand.Execute(null);

        await tutorial.TickAsync();

        Assert.Equal(8, tutorial.Players.Count);
        Assert.Equal(1, tutorial.SelectedPlayer?.Slot);
        Assert.Equal("Kvriokes", tutorial.PlayerName); // como lo lee el OCR: el jugador lo corrige en la caja
        Assert.True(tutorial.Watching); // falta calibrar con el HUD de la partida
    }

    [Fact]
    public async Task Con_el_nombre_ya_puesto_propone_ese_jugador()
    {
        if (OcrReader.TryCreate() is null)
            return;
        var tutorial = Tutorial(new StillScreen("carga-ia-en-1080p.png"), current: "Kayqubad I");
        tutorial.ToggleWatchingCommand.Execute(null);

        await tutorial.TickAsync();

        Assert.Equal("Kayqubad I", tutorial.SelectedPlayer?.Name);
    }

    [Fact]
    public async Task Deja_de_mirar_cuando_tiene_los_jugadores_y_la_calibracion()
    {
        if (OcrReader.TryCreate() is null)
            return;
        var screen = new StillScreen("carga-ia-en-1080p.png");
        var calibrations = 0;
        var tutorial = Tutorial(screen, () => ++calibrations >= 2);
        tutorial.ToggleWatchingCommand.Execute(null);

        await tutorial.TickAsync(); // lee la pantalla de carga; el HUD todavía no está
        Assert.False(tutorial.Calibrated);
        var captures = screen.Captures;
        await tutorial.TickAsync(); // ya en la partida: se calibra

        Assert.True(tutorial.Calibrated);
        Assert.False(tutorial.Watching);
        Assert.Equal(captures, screen.Captures); // la pantalla de carga no se vuelve a leer
        await tutorial.TickAsync();
        Assert.Equal(2, calibrations);
    }

    [Fact]
    public async Task Sin_mirar_no_captura_nada()
    {
        var screen = new StillScreen(null);
        var tutorial = Tutorial(screen, () => throw new InvalidOperationException("no tenía que calibrar"));

        await tutorial.TickAsync();

        Assert.Equal(0, screen.Captures);
    }

    [Fact]
    public void Guardar_pide_un_nombre_y_lo_guarda_sin_espacios()
    {
        string? saved = null;
        var tutorial = Tutorial(new StillScreen(null), save: name => saved = name);
        Assert.False(tutorial.FinishCommand.CanExecute(null));

        tutorial.PlayerName = " Kyriokes ";
        tutorial.FinishCommand.Execute(null);

        Assert.Equal("Kyriokes", saved);
        Assert.True(tutorial.Finished);
    }

    [Fact]
    public void Sin_el_nombre_ni_el_jugador_1_propone_el_primero()
    {
        var reading = new LoadingReading([
            new LoadingPlayer(3, "Ana", "MAYANS", "mayans", null, (0, 255, 0)),
            new LoadingPlayer(4, "Beto", "FRANKS", "franks", null, (255, 255, 0)),
        ]);

        Assert.Equal("Ana", FirstRunViewModel.Guess(reading, null)?.Name);
        Assert.Equal("Beto", FirstRunViewModel.Guess(reading, "beto")?.Name);
    }

    [Fact]
    public void El_tutorial_hecho_queda_en_los_ajustes()
    {
        var path = Path.Combine(_dir.FullName, "settings.json");
        Assert.False(CoachSettings.Load(path).FirstRunDone);

        new CoachSettings { FirstRunDone = true, PlayerName = "Kyriokes" }.Save(path);

        Assert.True(CoachSettings.Load(path).FirstRunDone);
    }

    [Fact]
    public async Task Un_error_al_mirar_se_muestra_en_el_estado_y_se_sigue_mirando()
    {
        // Con el juego minimizado la captura tira ArgumentException ("El rectángulo está vacío"): antes no se
        // atrapaba y la ventana mostraba un error cada 2 s.
        var tutorial = new FirstRunViewModel(new StillScreen(null), _data.GameData, () => GameLanguage.English,
            () => throw new ArgumentException("El rectángulo está vacío."), _ => { }, createOcr: _ => null);
        tutorial.ToggleWatchingCommand.Execute(null);

        await tutorial.TickAsync();

        Assert.Contains("El rectángulo está vacío", tutorial.WatchStatus);
        Assert.True(tutorial.Watching);
    }
}
