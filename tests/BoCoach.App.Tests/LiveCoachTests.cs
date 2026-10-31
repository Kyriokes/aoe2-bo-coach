using BoCoach.App.Coaching;
using BoCoach.Core.BuildOrders;
using BoCoach.Vision.Capture;
using BoCoach.Vision.Ocr;
using BoCoach.Vision.Recording;

namespace BoCoach.App.Tests;

public sealed class LiveCoachTests : IDisposable
{
    private readonly DirectoryInfo _library = Directory.CreateTempSubdirectory("bocoach-live-");
    private readonly CoachData _data;

    public LiveCoachTests()
    {
        _data = CoachData.Load();
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

    /// <summary>Capturas sin HUD (sin reloj), al frente, cada 0,5 s; cuenta cuántas veces el coach pidió volver a calibrar.</summary>
    private sealed class BlindFrames : IFrameSource
    {
        private int _served;
        public TimeSpan Interval => TimeSpan.FromMilliseconds(500);
        public PixelRect Area => new(0, 0, 1920, 1080);
        public int Recalibrations { get; private set; }
        public Frame? Next() => new(_ => null, ++_served * 0.5, Area);
        public bool Recalibrate() { Recalibrations++; return true; }
    }

    /// <summary>
    /// Con "Coach automático" el coach arranca en el menú del juego, donde la calibración sola no encuentra nada, y con
    /// la calibración vieja nunca ve el reloj del panel nuevo (20261024-224921: grabación con las regiones viejas y sin
    /// diseño). Sin reloj, se vuelve a probar cada 5 s.
    /// </summary>
    [Fact]
    public async Task Sin_reloj_vuelve_a_calibrar_cada_5_s()
    {
        var frames = new BlindFrames();
        using var live = new LiveCoach(Scouts, _data.GameData, _data.Events, _data.Fonts, null, frames);

        for (var i = 0; i < 9; i++)
            await live.StepAsync(); // hasta 4,5 s
        Assert.Equal(0, frames.Recalibrations);
        for (var i = 0; i < 15; i++)
            await live.StepAsync(); // hasta 12 s
        Assert.Equal(2, frames.Recalibrations);
    }

    /// <summary>
    /// La grabación de scouts (con reloj) como pantalla en vivo que todavía no se calibró sola; cuenta los pedidos de
    /// calibrar y la calibración encuentra el HUD a partir del intento <paramref name="succeedAt"/>.
    /// </summary>
    private sealed class UncalibratedFrames(RecordedSession session, int succeedAt) : IFrameSource
    {
        private int _served;
        public TimeSpan Interval => TimeSpan.FromMilliseconds(500);
        public PixelRect Area => GameWindow.PrimaryScreen;
        public int Recalibrations { get; private set; }
        public bool Calibrated { get; private set; }

        public Frame? Next()
        {
            if (_served >= session.Frames.Count)
                return null;
            var frame = session.Frames[_served++];
            return new Frame(region => session.Has(frame.Index, region) ? session.Image(frame.Index, region) : null, frame.TimeMs / 1000.0, Area);
        }

        public bool Recalibrate()
        {
            Calibrated = ++Recalibrations >= succeedAt;
            return Calibrated;
        }
    }

    /// <summary>
    /// Partida 20261025-230041: el coach automático arrancó en el menú (sin HUD que calibrar) y en la partida la
    /// calibración vieja leía el reloj, así que no volvió a calibrar y la caja de la cola quedó corrida toda la partida.
    /// Con el reloj a la vista y sin calibrar en esta sesión, prueba enseguida y después cada 30 s hasta que encuentra
    /// el HUD.
    /// </summary>
    [Fact]
    public async Task Con_reloj_y_sin_calibrar_en_la_sesion_vuelve_a_calibrar_hasta_encontrar_el_HUD()
    {
        var session = RecordedSession.Load(Fixture);
        var frames = new UncalibratedFrames(session, succeedAt: 2);
        using var live = Live(frames);
        CoachFrame? last = null;
        live.Updated += frame => last = frame;

        double? firstClock = null;
        while (await live.StepAsync() && (firstClock is null || last!.View.Time < firstClock + 90))
            firstClock ??= last?.Hud?.ClockRead == true ? last.View.Time : null;

        // El primero apenas se ve el reloj, el segundo a los 30 s (encuentra el HUD) y después ninguno más.
        Assert.Equal(2, frames.Recalibrations);
    }

    private static string Fixture => Path.Combine(AppContext.BaseDirectory, "fixtures", "rec-20261015-214512");

    /// <summary>
    /// La grabación de scouts con un hueco en el medio: las primeras <paramref name="before"/> capturas tal cual, después
    /// <paramref name="gap"/> capturas sin regiones (sin reloj) cada 0,5 s, al frente o no, y el resto de la grabación
    /// (o, con <paramref name="restart"/>, otra vez desde el principio: el reloj vuelve a 0:03), corrido en el tiempo.
    /// </summary>
    private sealed class GapFrames(RecordedSession session, int before, int gap, bool gapInFront, bool restart = false) : IFrameSource
    {
        private int _served;
        private double _offset;
        private double _last;

        public TimeSpan Interval => TimeSpan.FromMilliseconds(500);
        public PixelRect Area => GameWindow.PrimaryScreen;

        public Frame? Next()
        {
            var i = _served++;
            if (i < before)
                return Recorded(i, 0);
            if (i < before + gap)
            {
                _last += 0.5;
                return new Frame(_ => null, _last, Area, InFront: gapInFront);
            }
            var index = restart ? i - before - gap : i - gap;
            if (index >= session.Frames.Count)
                return null;
            if (_offset == 0)
                _offset = _last + 0.5 - session.Frames[index].TimeMs / 1000.0;
            return Recorded(index, _offset);
        }

        private Frame Recorded(int index, double offset)
        {
            var frame = session.Frames[index];
            _last = frame.TimeMs / 1000.0 + offset;
            return new Frame(region => session.Has(frame.Index, region) ? session.Image(frame.Index, region) : null, _last, Area);
        }
    }

    private LiveCoach Live(IFrameSource frames) =>
        new(Scouts, _data.GameData, _data.Events, _data.Fonts, ocr: null, frames) { MinReportSeconds = 1 };

    // D6 (2026-10-21): alt-tab. Con el juego atrás no se lee nada y el tiempo sin reloj no corre: la partida no
    // termina y el overlay se esconde; al volver, sigue donde estaba.
    [Fact]
    public async Task Con_el_juego_atras_no_cuenta_el_tiempo_sin_reloj_ni_termina_la_partida()
    {
        var session = RecordedSession.Load(Fixture);
        using var live = Live(new GapFrames(session, before: 10, gap: 80, gapInFront: false));
        var reports = 0;
        live.GameFinished += _ => reports++;
        CoachFrame? last = null;
        live.Updated += frame => last = frame;

        for (var i = 0; i < 10; i++)
            await live.StepAsync();
        Assert.False(last!.Waiting);
        var time = last.View.Time;

        for (var i = 0; i < 80; i++)
            await live.StepAsync();
        Assert.False(last!.InFront);
        Assert.False(last.Waiting);
        Assert.True(last.SecondsWithoutClock < 1, $"sin reloj {last.SecondsWithoutClock} s");
        Assert.Equal(0, reports);

        await live.StepAsync();
        Assert.True(last!.InFront);
        Assert.True(last.View.Time > time);
        Assert.NotNull(live.TakePendingReport()); // hubo lecturas nuevas: el informe queda pendiente
    }

    // D6: fin de partida. 30 s sin reloj con el juego a la vista: el informe (una sola vez) y a esperar otra; si el
    // reloj vuelve y sigue (el árbol de tecnologías tapa el HUD), el coach sigue donde estaba.
    [Fact]
    public async Task Treinta_segundos_sin_reloj_terminan_la_partida_y_el_estado_queda_por_si_el_reloj_vuelve()
    {
        var session = RecordedSession.Load(Fixture);
        using var live = Live(new GapFrames(session, before: 10, gap: 70, gapInFront: true));
        var reports = 0;
        live.GameFinished += _ => reports++;
        CoachFrame? last = null;
        live.Updated += frame => last = frame;

        for (var i = 0; i < 10; i++)
            await live.StepAsync();
        var time = last!.View.Time;
        for (var i = 0; i < 70; i++)
            await live.StepAsync();

        Assert.True(last!.Waiting);
        Assert.Equal(1, reports);
        Assert.Equal("Esperando una partida…", WarningOf(last));
        Assert.Null(live.TakePendingReport()); // ya se informó

        for (var i = 0; i < 5; i++)
            await live.StepAsync();
        Assert.False(last!.Waiting);
        Assert.True(last.View.Time > time); // la misma partida, que siguió
        Assert.Equal(1, reports);
        Assert.NotNull(live.TakePendingReport()); // lecturas nuevas desde el informe
    }

    // El panel del coach bajaba en plena partida con 2 s sin leer el reloj y tapaba el minimapa (pedido del usuario,
    // 2026-10-26: "esto jamás debería ocurrir durante el juego"). Un hueco sin reloj de menos de 30 s sigue siendo la
    // partida; recién al terminar deja de serlo.
    [Fact]
    public async Task Un_hueco_sin_reloj_en_plena_partida_sigue_siendo_la_partida()
    {
        var session = RecordedSession.Load(Fixture);
        using var live = Live(new GapFrames(session, before: 10, gap: 70, gapInFront: true));
        CoachFrame? last = null;
        live.Updated += frame => last = frame;

        for (var i = 0; i < 10; i++)
            await live.StepAsync();
        Assert.True(last!.InGame);

        for (var i = 0; i < 20; i++)
            await live.StepAsync();
        Assert.True(last!.SecondsWithoutClock >= 10, $"sin reloj {last.SecondsWithoutClock} s");
        Assert.True(last.InGame);

        for (var i = 0; i < 50; i++)
            await live.StepAsync();
        Assert.True(last!.Waiting);
        Assert.False(last.InGame);
    }

    // D6: partida nueva sin pantalla de carga leída: el reloj vuelve a empezar (3 lecturas seguidas para atrás).
    [Fact]
    public async Task Un_reloj_que_vuelve_a_empezar_es_otra_partida()
    {
        var session = RecordedSession.Load(Fixture);
        // Las primeras 13 capturas llegan al 3:20; después la grabación arranca de nuevo (0:03, 0:06, 0:12...).
        using var live = Live(new GapFrames(session, before: 13, gap: 0, gapInFront: true, restart: true));
        var reports = 0;
        live.GameFinished += _ => reports++;
        CoachFrame? last = null;
        live.Updated += frame => last = frame;

        for (var i = 0; i < 13; i++)
            await live.StepAsync();
        var time = last!.View.Time;
        Assert.True(time > 60, $"tiempo {time}");
        for (var i = 0; i < 2; i++)
            await live.StepAsync();
        Assert.Equal(0, reports); // dos lecturas para atrás pueden ser un error: todavía no
        for (var i = 0; i < 2; i++)
            await live.StepAsync();

        Assert.Equal(1, reports);
        Assert.True(last!.View.Time < time, $"tiempo {last.View.Time}");
        Assert.False(last.Waiting);
        Assert.Equal("any", live.Civ);
    }

    private string? WarningOf(CoachFrame frame)
    {
        var overlay = new Overlay.OverlayViewModel(_data.GameData, "x");
        overlay.Apply(frame);
        return overlay.Warning;
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
