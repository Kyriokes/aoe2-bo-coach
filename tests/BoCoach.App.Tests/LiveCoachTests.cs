using BoCoach.App.Coaching;
using BoCoach.Core.BuildOrders;
using BoCoach.Vision.Capture;
using BoCoach.Vision.Ocr;

namespace BoCoach.App.Tests;

public sealed class LiveCoachTests : IDisposable
{
    private readonly DirectoryInfo _library = Directory.CreateTempSubdirectory("bocoach-live-");
    private readonly CoachData _data;

    public LiveCoachTests()
    {
        _data = CoachData.Load(_library.FullName);
    }

    public void Dispose() => _library.Delete(recursive: true);

    private static readonly BuildOrder Scouts = BuildOrderSerializer.Load(Path.Combine(AppContext.BaseDirectory, "fixtures", "build_orders", "scouts_feudal.json"));

    /// <summary>Capturas que explotan: al pedir la captura o al leer una región, con una excepción cualquiera.</summary>
    private sealed class ExplodingFrames(bool atNext) : IFrameSource
    {
        public TimeSpan Interval => TimeSpan.FromMilliseconds(500);
        public PixelRect Area => new(0, 0, 1920, 1080);
        public int Served { get; private set; }

        public Frame? Next()
        {
            Served++;
            if (atNext)
                throw new KeyNotFoundException("explotó la captura");
            return new Frame(_ => throw new KeyNotFoundException("explotó la lectura"), Served * 0.5, Area);
        }
    }

    /// <summary>Una pantalla entera guardada, sin el HUD (sin reloj): cuenta cuántas veces se capturó algo de ella.</summary>
    private sealed class StillScreen(string name) : IFrameSource
    {
        private readonly CapturedImage _screen = Png.Read(Path.Combine(AppContext.BaseDirectory, "fixtures", "screens", name));

        public TimeSpan Interval => TimeSpan.FromMilliseconds(500);
        public PixelRect Area => new(0, 0, _screen.Width, _screen.Height);
        public int Served { get; private set; }
        public int ScreenCaptures { get; private set; }

        public Frame? Next()
        {
            Served++;
            return new Frame(_ => null, Served * 0.5, Area, rect =>
            {
                ScreenCaptures++;
                return _screen.Crop(rect);
            });
        }
    }

    // B2 (2026-10-20): en la pantalla de carga el coach busca el nombre del jugador y toma su civ; la partida empieza
    // de cero (también si se juegan varias seguidas sin detener el coach).
    [Fact]
    public async Task En_la_pantalla_de_carga_toma_la_civ_del_jugador()
    {
        var ocr = OcrReader.TryCreate();
        if (ocr is null)
            return; // esta máquina no tiene el OCR en inglés instalado (puede pasar en CI)
        using var live = new LiveCoach(Scouts, _data.GameData, _data.Events, _data.Fonts, ocr, new StillScreen("carga-ia-en-1080p.png"),
            playerName: "Kyriokes");
        CoachFrame? last = null;
        live.Updated += frame => last = frame;

        // Se mira después de 1 s sin reloj (lo que tarda el overlay en bajar): a la tercera captura.
        for (var i = 0; i < 3; i++)
            await live.StepAsync();

        Assert.Equal("japanese", live.Civ);
        Assert.Equal("japanese", last?.Civ);
        Assert.Equal(1, last?.Me?.Slot);
        Assert.Equal(8, last?.Loading?.Players.Count);
        Assert.Equal("any", live.BuildOrder.Civ); // la BO con que se empezó no cambia
    }

    [Fact]
    public async Task Sin_el_nombre_lee_la_pantalla_de_carga_pero_sigue_con_la_civ_de_la_BO()
    {
        var ocr = OcrReader.TryCreate();
        if (ocr is null)
            return;
        using var live = new LiveCoach(Scouts, _data.GameData, _data.Events, _data.Fonts, ocr, new StillScreen("carga-ia-en-1080p.png"));
        CoachFrame? last = null;
        live.Updated += frame => last = frame;

        // Se mira después de 1 s sin reloj (lo que tarda el overlay en bajar): a la tercera captura.
        for (var i = 0; i < 3; i++)
            await live.StepAsync();

        Assert.NotNull(last?.Loading);
        Assert.Null(last?.Me);
        Assert.Equal("any", last?.Civ);
    }

    [Fact]
    public async Task Sin_reloj_mira_el_panel_de_jugadores_cada_2_segundos_y_la_pantalla_de_carga_se_lee_una_vez()
    {
        var ocr = OcrReader.TryCreate();
        if (ocr is null)
            return;
        // Otra pantalla (sin reloj porque no hay regiones): el panel de jugadores después de 1 s sin reloj y cada 2 s
        // (a los 1,5 y a los 3,5 s).
        var other = new StillScreen("amigo-20261020-1080p-panel-negro.png");
        using (var live = new LiveCoach(Scouts, _data.GameData, _data.Events, _data.Fonts, ocr, other))
        {
            for (var i = 0; i < 8; i++)
                await live.StepAsync();
        }
        Assert.Equal(2, other.ScreenCaptures);

        // La pantalla de carga: se lee una sola vez (el panel y el mapa), aunque dure varios segundos.
        var loading = new StillScreen("carga-ia-en-1080p.png");
        using (var live = new LiveCoach(Scouts, _data.GameData, _data.Events, _data.Fonts, ocr, loading))
        {
            for (var i = 0; i < 10; i++)
                await live.StepAsync();
        }
        Assert.Equal(2, loading.ScreenCaptures);
    }

    // Antes solo se atrapaban cuatro tipos de excepción: cualquier otra (un error de programación en la lectura o en
    // el coach) cortaba el loop en silencio y el overlay quedaba congelado con la última captura.
    [Fact]
    public async Task Una_excepcion_inesperada_al_leer_una_captura_se_informa_y_el_loop_sigue()
    {
        var frames = new ExplodingFrames(atNext: false);
        using var live = new LiveCoach(Scouts, _data.GameData, _data.Events, _data.Fonts, ocr: null, frames);
        CoachFrame? last = null;
        live.Updated += frame => last = frame;

        Assert.True(await live.StepAsync());
        Assert.Contains("explotó la lectura", last?.Error);
        Assert.True(await live.StepAsync());
        Assert.Equal(2, frames.Served);
    }

    [Fact]
    public async Task Una_excepcion_inesperada_al_capturar_se_informa_y_el_loop_sigue()
    {
        var frames = new ExplodingFrames(atNext: true);
        using var live = new LiveCoach(Scouts, _data.GameData, _data.Events, _data.Fonts, ocr: null, frames);
        CoachFrame? last = null;
        live.Updated += frame => last = frame;

        Assert.True(await live.StepAsync());
        Assert.Contains("explotó la captura", last?.Error);
        Assert.True(await live.StepAsync());
        Assert.Equal(2, frames.Served);
    }
}
