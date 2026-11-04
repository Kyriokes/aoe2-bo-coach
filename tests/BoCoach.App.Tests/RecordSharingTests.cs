using System.Net;
using System.Text;
using BoCoach.App.Coaching;
using BoCoach.App.Overlay;
using BoCoach.Core.BuildOrders;
using BoCoach.Core.Record;
using BoCoach.Core.Web;
using BoCoach.Vision.Capture;

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

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel)
        {
            if (Offline)
                throw new HttpRequestException("sin conexión");
            Requests.Add($"{request.Method} {request.RequestUri!.AbsolutePath}");
            return Task.FromResult(new HttpResponseMessage(Status) { Content = new StringContent(Body, Encoding.UTF8, "application/json") });
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
