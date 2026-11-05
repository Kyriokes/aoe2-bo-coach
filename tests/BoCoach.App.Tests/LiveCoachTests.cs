using System.Globalization;
using BoCoach.App.Coaching;
using BoCoach.Core.BuildOrders;
using BoCoach.Core.Record;
using BoCoach.Vision.Capture;
using BoCoach.Vision.Hud;
using BoCoach.Vision.Ocr;
using BoCoach.Vision.Recording;
using BoCoach.Vision.Regions;

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

    /// <summary>El log a la vista con el reloj sin leerse (algo lo tapa): la calibración sola encuentra el HUD cada vez.</summary>
    private sealed class CoveredClockFrames(CapturedImage log, HudLayout? hudAfterRecalibrating = null) : IFrameSource
    {
        private int _served;
        public TimeSpan Interval => TimeSpan.FromMilliseconds(500);
        public PixelRect Area => new(0, 0, 1920, 1080);
        public HudLayout? Hud { get; private set; }
        public Frame? Next() => new(region => region == RegionNames.EventLog ? log : null, ++_served * 0.5, Area);

        public bool Recalibrate()
        {
            Hud = hudAfterRecalibrating;
            return true;
        }
    }

    private static CapturedImage LogWithFiveMessages => RecordedSession.Load(Fixture).Image(1399, RegionNames.EventLog);

    private async Task<List<string>> EventsReadIn12Seconds(IFrameSource frames, OcrReader ocr)
    {
        using var live = new LiveCoach(Scouts, _data.GameData, _data.Events, _data.Fonts, ocr, frames);
        var events = new List<string>();
        live.Updated += frame => events.AddRange(frame.Hud?.NewEvents ?? []);
        for (var i = 0; i < 24; i++)
            await live.StepAsync();
        return events;
    }

    // AP-16: cada 5 s sin reloj con el HUD a la vista se volvía a armar el lector, y uno nuevo da por nuevos todos los
    // renglones del log: las casas y las unidades se sumaban otra vez, y podían cerrar pasos en falso.
    [Fact]
    public async Task Volver_a_calibrar_con_el_mismo_diseno_no_cuenta_de_nuevo_el_log()
    {
        if (OcrReader.TryCreate() is not { } ocr)
            return; // sin el OCR en inglés (puede pasar en CI)

        var events = await EventsReadIn12Seconds(new CoveredClockFrames(LogWithFiveMessages), ocr);

        Assert.Contains("house_built", events);
        Assert.Equal(events.Distinct(), events);
    }

    // Otro diseño del HUD sí es otro lector: cada campo se recorta según él.
    [Fact]
    public async Task Volver_a_calibrar_con_otro_diseno_arma_otro_lector()
    {
        if (OcrReader.TryCreate() is not { } ocr)
            return;

        var layout = HudLayout.Load(Path.Combine(AppContext.BaseDirectory, "data", HudLayout.FileName));
        var events = await EventsReadIn12Seconds(new CoveredClockFrames(LogWithFiveMessages, layout), ocr);

        Assert.Equal(2, events.Count(e => e == "house_built"));
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

    private LiveCoach Recording(IFrameSource frames, string folder) =>
        new(Scouts with { Author = "Fulano" }, _data.GameData, _data.Events, _data.Fonts, ocr: null, frames) { MinReportSeconds = 1, RecordsFolder = folder, AppVersion = "v9.9" };

    // Spec 2026-11-02: cada partida con reloj deja su registro, y al terminar se avisa con la ruta.
    [Fact]
    public async Task Una_partida_deja_su_registro_y_avisa_al_terminar()
    {
        var session = RecordedSession.Load(Fixture);
        var folder = Path.Combine(_library.FullName, "registros");
        using var live = Recording(new GapFrames(session, before: 13, gap: 70, gapInFront: true), folder);
        var finished = new List<GameRecordInfo>();
        live.RecordFinished += finished.Add;

        for (var i = 0; i < 83; i++)
            await live.StepAsync();

        var info = Assert.Single(finished);
        Assert.Equal("fin", info.Reason);
        Assert.True(info.Length > 60, $"{info.Length} s");
        var record = GameRecordReader.Load(info.Path);
        Assert.False(record.Truncated);
        Assert.Equal(info.GameId, record.Meta!["game_id"]!.GetValue<string>());
        Assert.Equal("v9.9", record.Meta["app"]!.GetValue<string>());
        Assert.Equal(_data.GameData.DatVersion, record.Meta["game_data"]!.GetValue<string>());
        Assert.Equal(Scouts.Civ, record.Meta["civ"]!.GetValue<string>());
        Assert.True(record.Captures.Count() >= 13);
        // La BO va adentro: sin ella la partida de otro jugador no se reproduce. Sin el autor, que es un nombre.
        Assert.Equal(Scouts.Steps.Count, record.BuildOrder!.Steps.Count);
        Assert.Null(record.BuildOrder.Author);
        Assert.Contains(record.Lines, l => l["t"]!.GetValue<string>() == GameRecordKeys.Show);
        Assert.Equal("fin", record.Lines[^1]["reason"]!.GetValue<string>());

        live.Stop();
        Assert.Single(finished); // detener después no vuelve a cerrar nada
    }

    // AP-14: el nombre del archivo es la fecha del calendario común, sea cual sea el de Windows.
    [Fact]
    public async Task El_nombre_del_registro_no_depende_del_calendario_del_sistema()
    {
        var folder = Path.Combine(_library.FullName, "registros");
        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("fa-IR");
            using var live = Recording(new RecordedFrames(RecordedSession.Load(Fixture)), folder);
            var finished = new List<GameRecordInfo>();
            live.RecordFinished += finished.Add;
            for (var i = 0; i < 13; i++)
                await live.StepAsync();
            live.Stop();

            Assert.StartsWith(DateTime.Now.Year.ToString(CultureInfo.InvariantCulture), Path.GetFileName(Assert.Single(finished).Path));
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
    }

    // Reproducir una grabación más rápido no es mirar la pantalla más seguido: el registro dice lo segundo, y con eso se
    // reproduce después (cuántas capturas son "log vacío").
    [Fact]
    public async Task El_registro_dice_cada_cuanto_se_miro_la_pantalla_y_no_a_que_velocidad_se_reprodujo()
    {
        var folder = Path.Combine(_library.FullName, "registros");
        using var live = Recording(new RecordedFrames(RecordedSession.Load(Fixture), speed: 50), folder);
        var finished = new List<GameRecordInfo>();
        live.RecordFinished += finished.Add;
        while (live.Report().Length < 5 && await live.StepAsync())
        {
        }

        live.Stop();

        Assert.Equal(500, GameRecordReader.Load(Assert.Single(finished).Path).IntervalMs);
    }

    // "Algo anduvo mal": el jugador marca el momento y el registro dice cuándo fue.
    [Fact]
    public async Task Una_marca_queda_en_el_registro_y_se_cuenta()
    {
        var session = RecordedSession.Load(Fixture);
        var folder = Path.Combine(_library.FullName, "registros");
        using var live = Recording(new RecordedFrames(session), folder);
        var finished = new List<GameRecordInfo>();
        live.RecordFinished += finished.Add;
        for (var i = 0; i < 13; i++)
            await live.StepAsync();

        Assert.True(live.Flag());
        Assert.True(live.Flag());
        live.Stop();

        var info = Assert.Single(finished);
        Assert.Equal(2, info.Flags);
        var flags = GameRecordReader.Load(info.Path).Lines.Where(l => l["t"]?.GetValue<string>() == GameRecordKeys.Flag).ToList();
        Assert.Equal(2, flags.Count);
        Assert.True(flags[0]["time"]!.GetValue<double>() > 0);
    }

    [Fact]
    public void Sin_partida_no_hay_nada_que_marcar()
    {
        using var live = Recording(new RecordedFrames(RecordedSession.Load(Fixture)), Path.Combine(_library.FullName, "registros"));
        Assert.False(live.Flag());
    }

    // Se aprieta jugando, sin mirar la otra ventana: el overlay tiene que confirmar que llegó, y después callarse.
    [Fact]
    public async Task La_marca_se_confirma_unos_segundos_en_el_overlay()
    {
        var session = RecordedSession.Load(Fixture);
        using var live = Recording(new GapFrames(session, before: 13, gap: 70, gapInFront: true), Path.Combine(_library.FullName, "registros"));
        CoachFrame? last = null;
        live.Updated += frame => last = frame;
        for (var i = 0; i < 13; i++)
            await live.StepAsync();

        Assert.True(live.Flag());
        Assert.Equal(Texts.FlagNoticed, last!.Notice);

        await live.StepAsync();
        Assert.Equal(Texts.FlagNoticed, last!.Notice);

        for (var i = 0; i < 20; i++)
            await live.StepAsync();
        Assert.Null(last!.Notice);
    }

    [Fact]
    public async Task Una_partida_nueva_cierra_el_registro_anterior()
    {
        var session = RecordedSession.Load(Fixture);
        var folder = Path.Combine(_library.FullName, "registros");
        using var live = Recording(new GapFrames(session, before: 13, gap: 0, gapInFront: true, restart: true), folder);
        var finished = new List<GameRecordInfo>();
        live.RecordFinished += finished.Add;

        for (var i = 0; i < 17; i++)
            await live.StepAsync();

        Assert.Equal("nueva", Assert.Single(finished).Reason);
        Assert.Equal(2, Directory.GetFiles(folder, "*.jsonl.gz").Length); // la nueva ya tiene el suyo
    }

    [Fact]
    public async Task Detener_cierra_el_registro_y_una_partida_muy_corta_no_deja_nada()
    {
        var session = RecordedSession.Load(Fixture);
        var folder = Path.Combine(_library.FullName, "registros");
        using (var longer = Recording(new RecordedFrames(session), folder))
        {
            var finished = new List<GameRecordInfo>();
            longer.RecordFinished += finished.Add;
            for (var i = 0; i < 13; i++)
                await longer.StepAsync();
            longer.Stop();
            Assert.Equal("detenido", Assert.Single(finished).Reason);
            File.Delete(finished[0].Path);
        }

        using var shorter = new LiveCoach(Scouts, _data.GameData, _data.Events, _data.Fonts, null, new RecordedFrames(session)) { RecordsFolder = folder };
        var none = new List<GameRecordInfo>();
        shorter.RecordFinished += none.Add;
        await shorter.StepAsync();
        shorter.Stop();
        Assert.Empty(none);
        Assert.Empty(Directory.GetFiles(folder, "*.jsonl.gz"));
    }

    // Disco lleno, carpeta sin permiso: el coach sigue y lo dice; el registro no le puede cortar la partida a nadie.
    [Fact]
    public async Task Si_no_se_puede_escribir_el_registro_el_coach_sigue_y_lo_dice()
    {
        var session = RecordedSession.Load(Fixture);
        var blocked = Path.Combine(_library.FullName, "un-archivo");
        File.WriteAllText(blocked, "no es una carpeta");
        using var live = Recording(new RecordedFrames(session), blocked);
        CoachFrame? last = null;
        live.Updated += frame => last = frame;

        for (var i = 0; i < 5; i++)
            await live.StepAsync();

        Assert.Contains("registro", last!.Notice);
        Assert.Null(last.Error);
        Assert.True(last.InGame);
    }

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

    // AP-15: al empezar otra partida sin pantalla de carga, la captura se volvía a pasar por el armado nuevo con el reloj
    // de la partida anterior: el armado la tomaba como la hora de empezar a jugar y el tiempo quedaba en 0 toda la partida.
    [Fact]
    public async Task Despues_de_un_reloj_que_vuelve_a_empezar_el_tiempo_de_la_partida_nueva_corre()
    {
        var session = RecordedSession.Load(Fixture);
        var folder = Path.Combine(_library.FullName, "registros");
        using var live = Recording(new GapFrames(session, before: 13, gap: 0, gapInFront: true, restart: true), folder);
        var finished = new List<GameRecordInfo>();
        live.RecordFinished += finished.Add;
        CoachFrame? last = null;
        live.Updated += frame => last = frame;

        for (var i = 0; i < 13 + 13; i++)
            await live.StepAsync();

        Assert.Equal(0, last!.StartOffset);
        Assert.True(last.View.Time > 60, $"tiempo {last.View.Time}");

        // La partida nueva deja su informe y su registro, con su duración.
        Assert.True(live.Report().Length > 60, $"largo {live.Report().Length}");
        Assert.NotNull(live.TakePendingReport());
        live.Stop();
        Assert.Equal(["nueva", "detenido"], finished.Select(f => f.Reason));
        Assert.True(finished[1].Length > 60, $"largo {finished[1].Length}");
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
