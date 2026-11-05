using System.Net;
using BoCoach.Core.Record;
using BoCoach.Core.Web;
using static BoCoach.Core.Tests.Web.WebLibraryClientAppTests;

namespace BoCoach.Core.Tests.Record;

/// <summary>El envío de los registros (spec 2026-11-02): cola local, espera creciente, sin duplicar y con topes.</summary>
public sealed class RecordUploadTests : IDisposable
{
    private const string Route = "POST /api/app/registros";
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 22, 0, 0, TimeSpan.Zero);

    private readonly string _dir = Directory.CreateTempSubdirectory("bocoach-envio-").FullName;
    private readonly FakeWeb _web = new();
    private readonly InstallIdentity _identity;

    public RecordUploadTests()
    {
        _identity = InstallIdentity.LoadOrCreate(Path.Combine(_dir, "instalacion.json"));
        Accept();
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private void Accept() => _web.Routes[Route] = (HttpStatusCode.Created, """{"duplicate":false}""");

    private WebLibraryClient Client(string url = "https://bo.example/") => new(new HttpClient(_web), new Uri(url));

    private Task<RecordSendResult> Send(RecordQueue queue, DateTimeOffset now) =>
        RecordUploader.SendPendingAsync(queue, Client(), _identity, "v0.9.85 · abc1234 · 02/11 22:00", now);

    private string File_(string name, int bytes = 100)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllBytes(path, new byte[bytes]);
        return path;
    }

    private static string GameId(char c) => new(c, 32);

    private RecordQueue Queue(params string[] names)
    {
        var queue = new RecordQueue(_dir);
        for (var i = 0; i < names.Length; i++)
            queue.Enqueue(File_(names[i]), GameId((char)('a' + i)), 600, Now);
        return queue;
    }

    [Fact]
    public async Task Se_sube_con_la_identidad_de_la_instalacion_y_sale_de_la_cola()
    {
        var queue = Queue("a.jsonl.gz");

        var result = await Send(queue, Now);

        Assert.Equal(new RecordSendResult(1, 0, 0), result);
        Assert.Empty(queue.Pending);
        var headers = _web.RequestHeaders[^1];
        Assert.Equal(_identity.Id, headers["X-BoCoach-Install"]);
        Assert.Equal(_identity.Secret, headers["X-BoCoach-Secret"]);
        Assert.Equal(GameId('a'), headers["X-BoCoach-Game"]);
        Assert.True(headers["X-BoCoach-Version"].All(char.IsAscii), headers["X-BoCoach-Version"]);
        Assert.Equal(100, _web.Bodies[^1].Length);
        Assert.Equal("application/gzip", headers["Content-Type"]);
        Assert.True(File.Exists(Path.Combine(_dir, "a.jsonl.gz"))); // el registro local queda
    }

    [Fact]
    public async Task Sin_internet_quedan_todos_y_esperan_15_minutos()
    {
        var queue = Queue("a.jsonl.gz", "b.jsonl.gz");
        _web.Offline = true;

        var result = await Send(queue, Now.AddHours(1));

        Assert.Equal(new RecordSendResult(0, 2, 0), result);
        Assert.Equal(2, queue.Pending.Count);
        Assert.All(queue.Pending, r => Assert.Equal(1, r.Attempts));
        Assert.Empty(queue.Due(Now.AddHours(1).AddMinutes(14)));
        Assert.Equal(2, queue.Due(Now.AddHours(1).AddMinutes(15)).Count);
    }

    [Fact]
    public async Task La_cola_sobrevive_a_cerrar_la_app()
    {
        var queue = Queue("a.jsonl.gz");
        _web.Offline = true;
        await Send(queue, Now);

        var reopened = new RecordQueue(_dir);

        var item = Assert.Single(reopened.Pending);
        Assert.Equal(GameId('a'), item.GameId);
        Assert.Equal(1, item.Attempts);
        Assert.Equal(Now.AddMinutes(15), item.NextTry);
    }

    [Fact]
    public async Task Vencida_se_reintenta_y_la_espera_crece_hasta_el_tope()
    {
        var queue = Queue("a.jsonl.gz");
        _web.Offline = true;
        var now = Now;
        var delays = new List<TimeSpan>();
        for (var i = 0; i < 12; i++)
        {
            await Send(queue, now);
            var next = Assert.Single(queue.Pending).NextTry;
            delays.Add(next - now);
            now = next;
        }

        Assert.Equal(TimeSpan.FromMinutes(15), delays[0]);
        Assert.Equal(TimeSpan.FromMinutes(30), delays[1]);
        Assert.Equal(TimeSpan.FromHours(1), delays[2]);
        Assert.Equal(RecordQueue.MaxDelay, delays[^1]);

        _web.Offline = false;
        Assert.Equal(new RecordSendResult(1, 0, 0), await Send(queue, now));
        Assert.Empty(queue.Pending);
    }

    [Fact]
    public async Task No_se_reintenta_antes_de_tiempo()
    {
        var queue = Queue("a.jsonl.gz");
        _web.Offline = true;
        await Send(queue, Now);
        _web.Offline = false;

        Assert.Equal(new RecordSendResult(0, 0, 0), await Send(queue, Now.AddMinutes(1)));
        Assert.Empty(_web.Requests);
    }

    // La app se cerró antes de anotar que se envió: la web lo reconoce por el id de la partida.
    [Fact]
    public async Task Un_duplicado_cuenta_como_enviado()
    {
        var queue = Queue("a.jsonl.gz");
        _web.Routes[Route] = (HttpStatusCode.OK, """{"duplicate":true}""");

        Assert.Equal(new RecordSendResult(1, 0, 0), await Send(queue, Now));
        Assert.Empty(queue.Pending);
    }

    [Fact]
    public void Encolar_la_misma_partida_dos_veces_no_la_duplica()
    {
        var queue = Queue("a.jsonl.gz");
        queue.Enqueue(Path.Combine(_dir, "a.jsonl.gz"), GameId('a'), 600, Now);
        Assert.Single(queue.Pending);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.RequestEntityTooLarge)]
    [InlineData(HttpStatusCode.UnprocessableEntity)]
    public async Task Lo_que_la_web_rechaza_para_siempre_se_descarta(HttpStatusCode status)
    {
        var queue = Queue("a.jsonl.gz", "b.jsonl.gz");
        _web.Routes[Route] = (status, """{"error":"no"}""");

        Assert.Equal(new RecordSendResult(0, 0, 2), await Send(queue, Now));
        Assert.Empty(queue.Pending);
    }

    // Un 401 o un 403 pueden venir del firewall de Vercel o de un proxy, y no dicen nada del registro: descartarlos vaciaba
    // toda la cola en una sola pasada.
    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task Un_401_o_403_corta_la_tanda_y_espera(HttpStatusCode status)
    {
        var queue = Queue("a.jsonl.gz", "b.jsonl.gz");
        _web.Routes[Route] = (status, """{"error":"no"}""");

        Assert.Equal(new RecordSendResult(0, 2, 0), await Send(queue, Now));

        Assert.Single(_web.Requests);
        Assert.Equal(2, queue.Pending.Count);
        Assert.Empty(queue.Due(Now.AddMinutes(14)));
        Assert.Equal(2, queue.Due(Now.AddMinutes(15)).Count);
    }

    [Fact]
    public async Task Cuando_la_web_vuelve_a_atender_sale_todo()
    {
        var queue = Queue("a.jsonl.gz", "b.jsonl.gz");
        _web.Routes[Route] = (HttpStatusCode.Forbidden, """{"error":"no"}""");
        var now = Now;
        for (var i = 0; i < RecordUploader.MaxDenials - 1; i++)
        {
            await Send(queue, now);
            now = queue.Pending.Max(r => r.NextTry);
        }

        Accept();

        Assert.Equal(new RecordSendResult(2, 0, 0), await Send(queue, now));
        Assert.Empty(queue.Pending);
    }

    // Si insiste de verdad (el secreto no es el de la instalación) no va a salir nunca: se saca, pero de a uno y solo el
    // que se intentó: los demás siguen esperando.
    [Fact]
    public async Task Un_401_que_insiste_saca_solo_el_que_se_intento()
    {
        var queue = Queue("a.jsonl.gz", "b.jsonl.gz");
        _web.Routes[Route] = (HttpStatusCode.Unauthorized, """{"error":"no"}""");
        var now = Now;
        for (var i = 0; i < RecordUploader.MaxDenials - 1; i++)
        {
            Assert.Equal(new RecordSendResult(0, 2, 0), await Send(queue, now));
            now = queue.Pending.Max(r => r.NextTry);
        }

        Assert.Equal(new RecordSendResult(0, 1, 1), await Send(queue, now));

        Assert.Equal(GameId('b'), Assert.Single(queue.Pending).GameId);
        Assert.Equal(0, queue.Pending[0].Denials);
        Assert.Equal(RecordUploader.MaxDenials, _web.Requests.Count);
    }

    [Fact]
    public async Task Las_negativas_sobreviven_a_cerrar_la_app()
    {
        var queue = Queue("a.jsonl.gz");
        _web.Routes[Route] = (HttpStatusCode.Forbidden, """{"error":"no"}""");
        await Send(queue, Now);

        Assert.Equal(1, Assert.Single(new RecordQueue(_dir).Pending).Denials);
    }

    // Una semana sin internet no acerca a nadie a salir de la cola.
    [Fact]
    public async Task Los_intentos_sin_conexion_no_cuentan_como_negativas()
    {
        var queue = Queue("a.jsonl.gz");
        _web.Offline = true;
        var now = Now;
        for (var i = 0; i < RecordUploader.MaxDenials + 2; i++)
        {
            await Send(queue, now);
            now = Assert.Single(queue.Pending).NextTry;
        }
        _web.Offline = false;
        _web.Routes[Route] = (HttpStatusCode.Forbidden, """{"error":"no"}""");

        Assert.Equal(new RecordSendResult(0, 1, 0), await Send(queue, now));
        Assert.Equal(1, Assert.Single(queue.Pending).Denials);
    }

    [Fact]
    public void Una_cola_de_antes_de_las_negativas_se_lee()
    {
        File.WriteAllText(Path.Combine(_dir, RecordQueue.FileName),
            """[{"file":"a.jsonl.gz","game_id":"x","length":600,"added":"2026-11-02T22:00:00+00:00","attempts":2,"next_try":"2026-11-02T22:00:00+00:00"}]""");

        var item = Assert.Single(new RecordQueue(_dir).Pending);

        Assert.Equal((2, 0), (item.Attempts, item.Denials));
    }

    // Sin la migración corrida, o con el límite diario: se reintenta más tarde, no se pierde nada.
    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task Lo_que_puede_andar_despues_se_reintenta(HttpStatusCode status)
    {
        var queue = Queue("a.jsonl.gz", "b.jsonl.gz");
        _web.Routes[Route] = (status, """{"error":"después"}""");

        var result = await Send(queue, Now);

        Assert.Equal(new RecordSendResult(0, 2, 0), result);
        Assert.Single(_web.Requests); // no insiste con los demás en la misma tanda
        Assert.Equal(2, queue.Pending.Count);
    }

    // Una respuesta rara del servidor es un fallo más: se escapaba como InvalidOperationException, sin anotar el intento.
    [Fact]
    public async Task Un_error_con_otra_forma_cuenta_como_un_intento()
    {
        var queue = Queue("a.jsonl.gz");
        _web.Routes[Route] = (HttpStatusCode.InternalServerError, """{"error":{"code":1}}""");

        var result = await Send(queue, Now);

        Assert.Equal(new RecordSendResult(0, 1, 0), result);
        Assert.Equal(1, Assert.Single(queue.Pending).Attempts);
    }

    [Fact]
    public async Task Un_registro_de_mas_de_1_MB_no_se_manda()
    {
        var queue = new RecordQueue(_dir);
        queue.Enqueue(File_("grande.jsonl.gz", RecordQueue.MaxUploadBytes + 1), GameId('a'), 600, Now);

        Assert.Equal(new RecordSendResult(0, 0, 1), await Send(queue, Now));
        Assert.Empty(_web.Requests);
    }

    [Fact]
    public async Task Un_archivo_que_ya_no_esta_se_saca_de_la_cola()
    {
        var queue = Queue("a.jsonl.gz");
        File.Delete(Path.Combine(_dir, "a.jsonl.gz"));

        Assert.Equal(new RecordSendResult(0, 0, 1), await Send(queue, Now));
        Assert.Empty(queue.Pending);
    }

    [Fact]
    public void La_cola_saca_lo_mas_viejo_al_pasar_el_tope_de_espacio()
    {
        var queue = new RecordQueue(_dir, maxBytes: 250);
        queue.Enqueue(File_("a.jsonl.gz"), GameId('a'), 600, Now);
        queue.Enqueue(File_("b.jsonl.gz"), GameId('b'), 600, Now.AddMinutes(1));
        queue.Enqueue(File_("c.jsonl.gz"), GameId('c'), 600, Now.AddMinutes(2));

        Assert.Equal([GameId('b'), GameId('c')], queue.Pending.Select(r => r.GameId));
    }

    [Fact]
    public void La_cola_saca_lo_de_mas_de_30_dias()
    {
        var queue = new RecordQueue(_dir);
        queue.Enqueue(File_("vieja.jsonl.gz"), GameId('a'), 600, Now);
        queue.Enqueue(File_("nueva.jsonl.gz"), GameId('b'), 600, Now.AddDays(31));

        Assert.Equal([GameId('b')], queue.Pending.Select(r => r.GameId));
    }

    [Theory]
    [InlineData("{ esto no es")]
    [InlineData("""[{"file":"a","file":"b"}]""")]
    public void Una_cola_rota_arranca_vacia(string contents)
    {
        File.WriteAllText(Path.Combine(_dir, RecordQueue.FileName), contents);
        Assert.Empty(new RecordQueue(_dir).Pending);
    }

    [Fact]
    public void La_identidad_se_crea_una_vez_y_es_aleatoria()
    {
        var path = Path.Combine(_dir, "otra.json");
        var first = InstallIdentity.LoadOrCreate(path);

        Assert.Equal(first, InstallIdentity.LoadOrCreate(path));
        Assert.NotEqual(first.Id, _identity.Id);
        Assert.Matches("^[0-9a-f]{32}$", first.Id);
        Assert.Matches("^[0-9a-f]{64}$", first.Secret);
    }

    [Theory]
    [InlineData("""{"id":"no sirve"}""")]
    [InlineData("")]
    [InlineData("{ esto no es")]
    [InlineData("[]")]
    [InlineData("""{"id":5,"secret":[]}""")]
    [InlineData("""{"id":"\ud800","secret":"\ud800"}""")]
    [InlineData("""{"id":"a","id":"b"}""")]
    public void Una_identidad_rota_se_vuelve_a_crear(string contents)
    {
        var path = Path.Combine(_dir, "rota.json");
        File.WriteAllText(path, contents);

        var created = InstallIdentity.LoadOrCreate(path);

        Assert.Matches("^[0-9a-f]{32}$", created.Id);
        Assert.Equal(created, InstallIdentity.LoadOrCreate(path));
    }

    // Un antivirus que tiene tomado el archivo: se puede escribir pero no leer. Otra identidad escrita encima dejaba
    // afuera de "Borrar lo compartido" todo lo que subió la anterior.
    [Fact]
    public void Un_bloqueo_al_leer_no_cambia_la_identidad()
    {
        var path = Path.Combine(_dir, "tomada.json");
        var first = InstallIdentity.LoadOrCreate(path);

        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Write))
            Assert.ThrowsAny<IOException>(() => InstallIdentity.LoadOrCreate(path));

        Assert.Equal(first, InstallIdentity.LoadOrCreate(path));
    }

    [Fact]
    public void Crear_la_identidad_no_deja_temporales()
    {
        var folder = Directory.CreateDirectory(Path.Combine(_dir, "sola")).FullName;

        InstallIdentity.LoadOrCreate(Path.Combine(folder, "instalacion.json"));

        Assert.Equal(["instalacion.json"], Directory.EnumerateFiles(folder).Select(Path.GetFileName));
    }

    [Fact]
    public async Task El_secreto_no_viaja_sin_cifrar()
    {
        var queue = Queue("a.jsonl.gz");

        var result = await RecordUploader.SendPendingAsync(queue, Client("http://bo.example/"), _identity, "v1", Now);

        Assert.Equal(new RecordSendResult(0, 1, 0), result);
        Assert.Empty(_web.Requests);
    }

    // "Borrar lo compartido" con un envío en curso: lo que faltaba de la tanda seguía saliendo y quedaba en la web.
    [Fact]
    public async Task Si_se_vacia_la_cola_durante_un_envio_no_sale_lo_que_faltaba()
    {
        var queue = Queue("a.jsonl.gz", "b.jsonl.gz", "c.jsonl.gz");
        _web.WhileAnswering = queue.Clear;

        var result = await Send(queue, Now);

        Assert.Equal(new RecordSendResult(1, 0, 0), result);
        Assert.Single(_web.Requests);
        Assert.Empty(queue.Pending);
    }

    // Lo que la cola saca mientras se manda otra cosa (por el tope de espacio) no sale, y lo que sigue sí.
    [Fact]
    public async Task Lo_que_salio_de_la_cola_durante_un_envio_no_sale_y_lo_demas_si()
    {
        var queue = Queue("a.jsonl.gz", "b.jsonl.gz", "c.jsonl.gz");
        var second = queue.Pending[1];
        _web.WhileAnswering = () => queue.Remove(second);

        var result = await Send(queue, Now);

        Assert.Equal(new RecordSendResult(2, 0, 0), result);
        Assert.Equal([GameId('a'), GameId('c')], _web.RequestHeaders.Select(h => h["X-BoCoach-Game"]));
    }

    [Fact]
    public async Task Si_se_apaga_durante_un_envio_lo_que_faltaba_queda_en_la_cola_sin_contar_como_intento()
    {
        var queue = Queue("a.jsonl.gz", "b.jsonl.gz", "c.jsonl.gz");
        var sharing = true;
        _web.WhileAnswering = () => sharing = false;

        var result = await RecordUploader.SendPendingAsync(queue, Client(), _identity, "v1", Now, keepSending: () => sharing);

        Assert.Equal(new RecordSendResult(1, 0, 0), result);
        Assert.Single(_web.Requests);
        Assert.Equal([GameId('b'), GameId('c')], queue.Pending.Select(r => r.GameId));
        Assert.All(queue.Pending, r => Assert.Equal(0, r.Attempts));
        Assert.Equal(2, queue.Due(Now).Count);
    }

    [Fact]
    public async Task El_pedido_de_borrado_manda_la_identidad_y_vacia_la_cola()
    {
        var queue = Queue("a.jsonl.gz");
        _web.Routes["DELETE /api/app/registros"] = (HttpStatusCode.OK, """{"forgotten":3}""");

        var forgotten = await RecordUploader.ForgetAsync(queue, Client(), _identity);

        Assert.Equal(3, forgotten);
        Assert.Equal(_identity.Id, _web.RequestHeaders[^1]["X-BoCoach-Install"]);
        Assert.Equal(_identity.Secret, _web.RequestHeaders[^1]["X-BoCoach-Secret"]);
        Assert.Empty(queue.Pending);
    }
}
