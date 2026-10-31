using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using BoCoach.Core.BuildOrders;
using BoCoach.Core.Web;

namespace BoCoach.Core.Tests.Web;

/// <summary>Lo que la app le pide a la web con su token: su lista, suscribirse y bajar privadas (parte 4).</summary>
public class WebLibraryClientAppTests
{
    private const string Token = "bocoach_0123456789abcdef0123456789abcdef0123456789abcdef0123456789ab";

    private static readonly BuildOrder Scouts = BuildOrderSerializer.Load(Path.Combine(TestData.BuildOrdersDir, "scouts_feudal.json"));

    /// <summary>Servidor falso: responde lo que se le indique para cada ruta y guarda los pedidos.</summary>
    internal sealed class FakeWeb : HttpMessageHandler
    {
        public Dictionary<string, (HttpStatusCode Status, string Body)> Routes { get; } = [];
        public List<(HttpMethod Method, string Path, string? Auth)> Requests { get; } = [];
        public bool Offline { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel)
        {
            if (Offline)
                throw new HttpRequestException("sin conexión");
            var path = request.RequestUri!.AbsolutePath;
            Requests.Add((request.Method, path, request.Headers.Authorization?.ToString()));
            var (status, body) = Routes.TryGetValue($"{request.Method} {path}", out var route) ? route : (HttpStatusCode.NotFound, """{"error":"No existe."}""");
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }

    internal static JsonObject Entry(string code, string name, bool own = false, string updated = "2026-10-31T10:00:00Z") => new()
    {
        ["code"] = code,
        ["name"] = name,
        ["civ"] = "franks",
        ["map"] = "arabia",
        ["style"] = "scouts",
        ["tags"] = new JsonArray("feudal"),
        ["author"] = "Ana",
        ["guide_author"] = "Cicero",
        ["own"] = own,
        ["visibility"] = own ? "private" : "public",
        ["updated_at"] = updated,
        ["feudal_click"] = 540,
        ["castle_click"] = null,
    };

    internal static string BuildOrderJson(string code, string updated = "2026-10-31T10:00:00Z", string name = "Scouts") => new JsonObject
    {
        ["code"] = code,
        ["name"] = name,
        ["author"] = "Ana",
        ["guide_author"] = "Cicero",
        ["updated_at"] = updated,
        ["build_order"] = JsonNode.Parse(BuildOrderSerializer.Serialize(Scouts with { Name = name })),
    }.ToJsonString();

    private static (WebLibraryClient Client, FakeWeb Web) Client(string baseUrl = "https://bo.example/")
    {
        var web = new FakeWeb();
        return (new WebLibraryClient(new HttpClient(web), new Uri(baseUrl)), web);
    }

    [Fact]
    public async Task Lee_tu_lista_con_la_cuenta()
    {
        var (client, web) = Client();
        var broken = new JsonObject { ["code"] = "no sirve", ["name"] = "Rota" };
        web.Routes["GET /api/app/mis-bo"] = (HttpStatusCode.OK, new JsonObject
        {
            ["account"] = "Ana",
            ["items"] = new JsonArray(Entry("k7m2xq", "Mía", own: true), broken, Entry("abcdef", "Suscripta")),
        }.ToJsonString());

        var list = await client.MyBuildOrdersAsync(Token);

        Assert.Equal("Ana", list.Account);
        Assert.Equal(["k7m2xq", "abcdef"], list.Items.Select(i => i.Code));
        var mine = list.Items[0];
        Assert.True(mine.Own);
        Assert.Equal("private", mine.Visibility);
        Assert.Equal(new DateTimeOffset(2026, 10, 31, 10, 0, 0, TimeSpan.Zero), mine.UpdatedAt);
        Assert.Equal(540, mine.FeudalClick);
        Assert.Equal(new[] { "k7m2xq", "Mía", "franks", "arabia", "scouts" },
            new[] { mine.Summary.Key, mine.Summary.Name, mine.Summary.Civ, mine.Summary.Map, mine.Summary.Style });
        Assert.Equal(["feudal"], mine.Summary.Tags);
        Assert.Equal($"Bearer {Token}", web.Requests.Single().Auth);
    }

    [Fact]
    public async Task Un_token_invalido_da_401_con_el_mensaje_de_la_web()
    {
        var (client, web) = Client();
        web.Routes["GET /api/app/mis-bo"] = (HttpStatusCode.Unauthorized, """{"error":"El token de la app no es válido."}""");

        var error = await Assert.ThrowsAsync<WebLibraryException>(() => client.MyBuildOrdersAsync(Token));

        Assert.Equal(HttpStatusCode.Unauthorized, error.Status);
        Assert.Contains("token", error.Message);
    }

    [Fact]
    public async Task Suscribirse_manda_el_token_y_dice_si_es_propia()
    {
        var (client, web) = Client();
        web.Routes["POST /api/app/suscripciones/k7m2xq"] = (HttpStatusCode.OK, """{"code":"k7m2xq","own":true}""");
        web.Routes["POST /api/app/suscripciones/abcdef"] = (HttpStatusCode.OK, """{"code":"abcdef","own":false}""");

        Assert.True(await client.SubscribeAsync("k7m2xq", Token));
        Assert.False(await client.SubscribeAsync("abcdef", Token));
        Assert.All(web.Requests, r => Assert.Equal($"Bearer {Token}", r.Auth));
    }

    [Fact]
    public async Task Bajar_con_token_manda_la_cabecera_y_trae_la_fecha()
    {
        var (client, web) = Client();
        web.Routes["GET /api/bo/k7m2xq"] = (HttpStatusCode.OK, BuildOrderJson("k7m2xq", "2026-10-30T08:00:00Z"));

        var withToken = await client.DownloadAsync("k7m2xq", Token);
        var withoutToken = await client.DownloadAsync("k7m2xq");

        Assert.Equal($"Bearer {Token}", web.Requests[0].Auth);
        Assert.Null(web.Requests[1].Auth);
        Assert.Equal(new DateTimeOffset(2026, 10, 30, 8, 0, 0, TimeSpan.Zero), withToken.UpdatedAt);
        Assert.Equal("Cicero", withToken.GuideAuthor);

        // El autor del JSON lo escribe cualquiera: se corta a 60 (la web lo limita desde la migración de columnas; lo viejo, no).
        var longAuthor = JsonNode.Parse(BuildOrderJson("k7m2xq"))!.AsObject();
        longAuthor["guide_author"] = new string('x', 100);
        web.Routes["GET /api/bo/k7m2xq"] = (HttpStatusCode.OK, longAuthor.ToJsonString());
        Assert.Equal(new string('x', WebLibraryClient.MaxAuthorLength), (await client.DownloadAsync("k7m2xq", Token)).GuideAuthor);
        Assert.Equal("Ana", withoutToken.Author);
    }

    [Fact]
    public async Task Por_http_a_otra_PC_no_manda_el_token()
    {
        var (client, web) = Client("http://bo.example/");

        await Assert.ThrowsAsync<WebLibraryException>(() => client.MyBuildOrdersAsync(Token));
        await Assert.ThrowsAsync<WebLibraryException>(() => client.SubscribeAsync("k7m2xq", Token));
        await Assert.ThrowsAsync<WebLibraryException>(() => client.DownloadAsync("k7m2xq", Token));
        Assert.Empty(web.Requests);
    }
}
