using System.Net;
using System.Text;
using BoCoach.App.Coaching;
using BoCoach.App.Overlay;
using BoCoach.Core.BuildOrders;
using BoCoach.Core.Record;
using BoCoach.Core.Web;
using BoCoach.Vision.Capture;
using BoCoach.Vision.Recording;

namespace BoCoach.App.Tests;

/// <summary>"Compartir mis partidas": prendido por defecto, solo partidas reales, y con pedido de borrado.</summary>
public sealed class RecordSharingTests : IDisposable
{
    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("bocoach-compartir-");
    private readonly CoachData _data = CoachData.Load();
    private readonly FakeWeb _web = new();
    private readonly Host _host = new();

    public void Dispose() => _dir.Delete(recursive: true);

    private static readonly BuildOrder Scouts = BuildOrderSerializer.Load(Path.Combine(AppContext.BaseDirectory, "fixtures", "build_orders", "scouts_feudal.json"));

    private string SettingsPath => Path.Combine(_dir.FullName, "settings.json");

    private sealed class FakeWeb : HttpMessageHandler
    {
        public HttpStatusCode Status { get; set; } = HttpStatusCode.Created;
        public string Body { get; set; } = """{"duplicate":false}""";
        public List<string> Requests { get; } = [];
        public bool Offline { get; set; }

        /// <summary>Lo que pasa mientras la web atiende un pedido.</summary>
        public Func<Task>? WhileAnswering { get; set; }

        /// <summary>Si está, los POST no contestan hasta que se complete.</summary>
        public TaskCompletionSource? Hold { get; set; }

        /// <summary>Se completa cuando llega un POST.</summary>
        public TaskCompletionSource Posted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel)
        {
            if (Offline)
                throw new HttpRequestException("sin conexión");
            Requests.Add($"{request.Method} {request.RequestUri!.AbsolutePath}");
            if (WhileAnswering is { } whileAnswering)
                await whileAnswering();
            if (request.Method == HttpMethod.Post)
            {
                Posted.TrySetResult();
                if (Hold is { } hold)
                    await hold.Task;
            }
            return new HttpResponseMessage(Status) { Content = new StringContent(Body, Encoding.UTF8, "application/json") };
        }
    }

    private sealed class Host : ICoachHost
    {
        public bool Answer { get; set; } = true;
        public string? Asked { get; private set; }

        public void ShowOverlay(OverlayViewModel overlay, PixelRect area) { }
        public void PlaceOverlay(PixelRect area) { }
        public void SetOverlayVisible(bool visible) { }
        public void CloseOverlay() { }
        public void RunOnUi(Action action) => action();
        public void ShowReport(IReadOnlyList<string> summary, string path) { }
        public EloCandidate? ChooseElo(IReadOnlyList<EloCandidate> candidates) => null;

        public bool Confirm(string message)
        {
            Asked = message;
            return Answer;
        }
    }

    /// <summary>Capturas en vivo, sin nada que leer: lo que hace falta para que la sesión cuente como real.</summary>
    private sealed class LiveFrames : IFrameSource
    {
        private int _served;
        public TimeSpan Interval => TimeSpan.FromHours(1);
        public PixelRect Area => new(0, 0, 1920, 1080);
        public Frame? Next() => new(_ => null, ++_served, Area);
    }

    /// <summary>Una grabación servida como si fuera la pantalla: para la sesión es una partida real.</summary>
    private sealed class LiveRecording(RecordedSession session) : IFrameSource
    {
        private int _served;
        public TimeSpan Interval => TimeSpan.FromHours(1);
        public PixelRect Area => new(0, 0, 1920, 1080);

        public Frame? Next()
        {
            if (_served >= session.Frames.Count)
                return null;
            var frame = session.Frames[_served++];
            return new Frame(region => session.Has(frame.Index, region) ? session.Image(frame.Index, region) : null, frame.TimeMs / 1000.0, Area);
        }
    }

    /// <summary>
    /// Como la ventana de verdad: lo que va por RunOnUi se encola (Dispatcher.BeginInvoke) y corre después de lo que está
    /// corriendo. Anota lo que dice la sesión en el momento de mostrar el informe.
    /// </summary>
    private sealed class QueuedHost : ICoachHost
    {
        private readonly Queue<Action> _pending = new();
        public CoachSessionViewModel? Session { get; set; }
        public bool CouldSendNote { get; private set; }
        public GameRecordInfo? Game { get; private set; }

        public void RunPending()
        {
            while (_pending.TryDequeue(out var action))
                action();
        }

        public void RunOnUi(Action action) => _pending.Enqueue(action);

        public void ShowReport(IReadOnlyList<string> summary, string path) =>
            (CouldSendNote, Game) = (Session!.CanSendNote, Session.LastRecord);

        public void ShowOverlay(OverlayViewModel overlay, PixelRect area) { }
        public void PlaceOverlay(PixelRect area) { }
        public void SetOverlayVisible(bool visible) { }
        public void CloseOverlay() { }
        public EloCandidate? ChooseElo(IReadOnlyList<EloCandidate> candidates) => null;
        public bool Confirm(string message) => true;
    }

    private static string Fixture => Path.Combine(AppContext.BaseDirectory, "fixtures", "rec-20261015-214512");

    private static async Task PlayAMinute(CoachSessionViewModel session, QueuedHost host)
    {
        session.Start(Scouts, new LiveRecording(RecordedSession.Load(Fixture)));
        while (session.LiveCoach!.Report().Length < 70 && await session.LiveCoach.StepAsync())
            host.RunPending();
    }

    // Partida 20261104-114422: 3 marcas con Ctrl+Alt+F y la caja de la nota nunca apareció. Al detener el coach, el informe
    // se mostraba antes de cerrar el registro: para el informe todavía no había ninguna partida a la que anotarle algo.
    [Fact]
    public async Task Al_detener_el_coach_el_informe_ya_sabe_de_que_partida_habla_y_cuantas_marcas_tiene()
    {
        var folder = Path.Combine(_dir.FullName, "registros");
        var host = new QueuedHost();
        var sharing = new RecordSharing(folder, Path.Combine(_dir.FullName, "instalacion.json"),
            () => new WebLibraryClient(new HttpClient(_web), new Uri("https://bo.example/")), "v9.9");
        using var session = new CoachSessionViewModel(_data, host, () => null, () => throw new InvalidOperationException(), SettingsPath)
        {
            ManualSteps = true,
            RecordsFolder = folder,
            ReportsFolder = Path.Combine(_dir.FullName, "informes"),
            Sharing = sharing,
        };
        host.Session = session;

        await PlayAMinute(session, host);
        Assert.True(session.LiveCoach!.Flag());
        session.Stop();

        Assert.True(host.CouldSendNote);
        Assert.Equal(1, host.Game?.Flags);
        var first = host.Game!.GameId;
        await Sent(sharing);

        // La segunda partida de la sesión no le anota nada a la primera.
        await PlayAMinute(session, host);
        session.Stop();

        Assert.NotEqual(first, host.Game?.GameId);
        Assert.Equal(0, host.Game?.Flags);
        await Sent(sharing);
    }

    /// <summary>El registro sale solo después de detener el coach: se espera a que salga de la cola.</summary>
    private static async Task Sent(RecordSharing sharing)
    {
        for (var i = 0; i < 500 && sharing.Pending > 0; i++)
            await Task.Delay(10);
        Assert.Equal(0, sharing.Pending);
    }

    private (CoachSessionViewModel Session, RecordSharing Sharing) Session(bool live = true)
    {
        var folder = Path.Combine(_dir.FullName, "registros");
        var sharing = new RecordSharing(folder, Path.Combine(_dir.FullName, "instalacion.json"),
            () => new WebLibraryClient(new HttpClient(_web), new Uri("https://bo.example/")), "v9.9");
        var session = new CoachSessionViewModel(_data, _host, () => null, () => throw new InvalidOperationException(), SettingsPath) { RecordsFolder = folder, Sharing = sharing };
        if (live)
            session.Start(Scouts, new LiveFrames());
        return (session, sharing);
    }

    private GameRecordInfo Record(double length = 600)
    {
        var folder = Directory.CreateDirectory(Path.Combine(_dir.FullName, "registros")).FullName;
        var path = Path.Combine(folder, "20261102-220000.jsonl.gz");
        File.WriteAllBytes(path, new byte[50]);
        return new GameRecordInfo(path, new string('a', 32), length, "fin");
    }

    [Fact]
    public void Viene_prendido_y_se_guarda_al_cambiarlo()
    {
        Assert.True(new CoachSettings().ShareGames);
        Assert.True(CoachSettings.Load(SettingsPath).ShareGames);

        var (session, _) = Session(live: false);
        Assert.True(session.ShareGames);
        session.ShareGames = false;

        Assert.False(CoachSettings.Load(SettingsPath).ShareGames);
        File.WriteAllText(SettingsPath, """{ "share_games": "no sé" }""");
        Assert.True(CoachSettings.Load(SettingsPath).ShareGames); // un valor raro no apaga ni rompe nada
    }

    [Fact]
    public void Otros_ajustes_no_lo_vuelven_a_prender()
    {
        var (session, _) = Session(live: false);
        session.ShareGames = false;
        session.AutoStart = !session.AutoStart;
        Assert.False(CoachSettings.Load(SettingsPath).ShareGames);
    }

    [Fact]
    public async Task Al_terminar_una_partida_real_se_sube()
    {
        var (session, sharing) = Session();
        using (session)
        {
            await session.RecordDoneAsync(Record());
            Assert.Equal(["POST /api/app/registros"], _web.Requests);
            Assert.Equal(0, sharing.Pending);
            Assert.Contains("enviado", session.ShareStatus);
        }
    }

    // La nota del informe: el registro de la partida ya salió, así que va como un registro chico que apunta a ella.
    [Fact]
    public async Task La_nota_del_informe_se_sube_aparte_y_apunta_a_su_partida()
    {
        var (session, sharing) = Session();
        using (session)
        {
            var game = Record();
            await session.RecordDoneAsync(game);
            _web.Requests.Clear();
            Assert.True(session.CanSendNote);

            Assert.True(await session.SendNoteAsync("el coach pidió una casa de más"));

            Assert.Equal(["POST /api/app/registros"], _web.Requests);
            Assert.Equal(0, sharing.Pending);
            var note = GameRecordReader.Load(Path.Combine(_dir.FullName, "registros", "20261102-220000-nota.jsonl.gz"));
            Assert.Equal(game.GameId, note.Meta!["note_for"]!.GetValue<string>());
            Assert.NotEqual(game.GameId, note.Meta["game_id"]!.GetValue<string>());
        }
    }

    [Fact]
    public async Task Sin_internet_la_nota_queda_en_la_cola()
    {
        var (session, sharing) = Session();
        using (session)
        {
            _web.Offline = true;
            await session.RecordDoneAsync(Record());

            Assert.True(await session.SendNoteAsync("algo"));

            Assert.Equal(2, sharing.Pending);
        }
    }

    [Fact]
    public async Task Sin_compartir_o_sin_partida_no_hay_nota()
    {
        var (session, _) = Session();
        using (session)
        {
            Assert.False(session.CanSendNote); // todavía no terminó ninguna partida
            Assert.False(await session.SendNoteAsync("algo"));

            await session.RecordDoneAsync(Record());
            session.ShareGames = false;

            Assert.False(session.CanSendNote);
            Assert.False(await session.SendNoteAsync("algo"));
        }
    }

    [Fact]
    public async Task Una_nota_vacia_no_se_manda()
    {
        var (session, _) = Session();
        using (session)
        {
            await session.RecordDoneAsync(Record());
            _web.Requests.Clear();

            Assert.False(await session.SendNoteAsync("   "));

            Assert.Empty(_web.Requests);
        }
    }

    [Fact]
    public async Task Sin_internet_queda_para_la_proxima()
    {
        var (session, sharing) = Session();
        using (session)
        {
            _web.Offline = true;
            await session.RecordDoneAsync(Record());
            Assert.Equal(1, sharing.Pending);
            Assert.Contains("después", session.ShareStatus);
        }
    }

    [Fact]
    public async Task Apagado_no_se_sube_ni_se_encola()
    {
        var (session, sharing) = Session();
        using (session)
        {
            session.ShareGames = false;
            await session.RecordDoneAsync(Record());
            Assert.Empty(_web.Requests);
            Assert.Equal(0, sharing.Pending);
        }
    }

    [Fact]
    public async Task Apagado_no_manda_lo_que_quedo_pendiente()
    {
        var (session, sharing) = Session();
        using (session)
        {
            sharing.Enqueue(Record(), DateTimeOffset.Now.AddHours(-1));
            session.ShareGames = false;
            await session.SendPendingRecordsAsync();
            Assert.Empty(_web.Requests);
            Assert.Equal(1, sharing.Pending);

            session.ShareGames = true;
            await session.SendPendingRecordsAsync();
            Assert.Single(_web.Requests);
        }
    }

    [Fact]
    public async Task Una_partida_muy_corta_no_se_sube()
    {
        var (session, _) = Session();
        using (session)
        {
            await session.RecordDoneAsync(Record(length: 30));
            Assert.Empty(_web.Requests);
        }
    }

    // Una grabación reproducida con --replay no es una partida nueva.
    [Fact]
    public async Task Una_grabacion_reproducida_no_se_sube()
    {
        var (session, _) = Session(live: false);
        await session.RecordDoneAsync(Record());
        Assert.Empty(_web.Requests);
    }

    [Fact]
    public async Task El_pedido_de_borrado_pregunta_antes_y_vacia_la_cola()
    {
        var (session, sharing) = Session(live: false);
        sharing.Enqueue(Record(), DateTimeOffset.Now);
        _web.Status = HttpStatusCode.OK;
        _web.Body = """{"forgotten":4}""";

        _host.Answer = false;
        await session.ForgetSharedCommand.ExecuteAsync(null);
        Assert.NotNull(_host.Asked);
        Assert.Empty(_web.Requests);
        Assert.Equal(1, sharing.Pending);

        _host.Answer = true;
        await session.ForgetSharedCommand.ExecuteAsync(null);
        Assert.Equal(["DELETE /api/app/registros"], _web.Requests);
        Assert.Equal(0, sharing.Pending);
        Assert.Contains("4", session.ShareStatus);
    }

    // "Borrar lo compartido" con un envío en curso: el POST que ya salió podía llegar después del DELETE y lo que faltaba de
    // la tanda seguía saliendo; todo eso quedaba en la web después de decirle al jugador que estaba borrado.
    [Fact]
    public async Task El_pedido_de_borrado_espera_el_envio_en_curso_y_corta_lo_que_faltaba()
    {
        var (_, sharing) = Session(live: false);
        sharing.Enqueue(Record(), DateTimeOffset.Now);
        sharing.Enqueue(Record() with { GameId = new string('b', 32) }, DateTimeOffset.Now);
        _web.Hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var sending = sharing.SendPendingAsync(DateTimeOffset.Now);
        await _web.Posted.Task;
        var forgetting = sharing.ForgetAsync();
        await Task.Delay(100);

        Assert.False(forgetting.IsCompleted);
        Assert.Equal(["POST /api/app/registros"], _web.Requests);
        Assert.Equal(0, sharing.Pending);

        _web.Body = """{"forgotten":1,"duplicate":false}""";
        _web.Hold.SetResult();
        await sending;
        Assert.Equal(1, await forgetting);

        Assert.Equal(["POST /api/app/registros", "DELETE /api/app/registros"], _web.Requests);
        Assert.Equal(0, sharing.Pending);
    }

    // Mientras dura el borrado no empieza otro envío: lo nuevo sale en el siguiente.
    [Fact]
    public async Task Durante_el_pedido_de_borrado_no_sale_nada()
    {
        var (_, sharing) = Session(live: false);
        var game = Record();
        RecordSendResult? during = null;
        _web.Status = HttpStatusCode.OK;
        _web.Body = """{"forgotten":0}""";
        _web.WhileAnswering = async () =>
        {
            sharing.Enqueue(game, DateTimeOffset.Now);
            during = await sharing.SendPendingAsync(DateTimeOffset.Now);
        };

        await sharing.ForgetAsync();

        Assert.Equal(new RecordSendResult(0, 0, 0), during);
        Assert.Equal(["DELETE /api/app/registros"], _web.Requests);
    }

    [Fact]
    public async Task Apagar_el_interruptor_corta_el_envio_en_curso()
    {
        var (session, sharing) = Session();
        using (session)
        {
            sharing.Enqueue(Record(), DateTimeOffset.Now.AddHours(-1));
            sharing.Enqueue(Record() with { GameId = new string('b', 32) }, DateTimeOffset.Now.AddHours(-1));
            _web.WhileAnswering = () =>
            {
                session.ShareGames = false;
                return Task.CompletedTask;
            };

            await session.SendPendingRecordsAsync();

            Assert.Single(_web.Requests);
            Assert.Equal(1, sharing.Pending); // queda para cuando lo vuelva a prender
        }
    }

    [Fact]
    public async Task Si_el_pedido_de_borrado_falla_lo_dice()
    {
        var (session, _) = Session(live: false);
        _web.Offline = true;
        await session.ForgetSharedCommand.ExecuteAsync(null);
        Assert.Contains("conectar", session.ShareStatus);
    }

    // Lo que se le promete al jugador.
    [Fact]
    public void El_texto_dice_que_viaja_y_que_no()
    {
        Assert.Contains("No viaja tu nombre", RecordSharing.Explanation);
        Assert.Contains("capturas de pantalla", RecordSharing.Explanation);
        Assert.Contains("chat", RecordSharing.Explanation);
        Assert.Contains("borre", RecordSharing.Explanation);
    }
}
