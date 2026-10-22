using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using BoCoach.Core.BuildOrders;
using BoCoach.Core.Web;

namespace BoCoach.Core.Tests.Web;

public class WebCodeTests
{
    [Theory]
    [InlineData("k7m2xq", "k7m2xq")]
    [InlineData("  K7M2XQ ", "k7m2xq")]
    [InlineData("bocoach://import/k7m2xq", "k7m2xq")]
    [InlineData("bocoach://import/k7m2xq/", "k7m2xq")]
    [InlineData("https://aoe2-bo-coach.vercel.app/bo/k7m2xq", "k7m2xq")]
    [InlineData("https://aoe2-bo-coach.vercel.app/bo/k7m2xq?x=1", "k7m2xq")]
    [InlineData("http://localhost:3000/api/bo/k7m2xq", "k7m2xq")]
    public void Reconoce_el_codigo_en_cualquier_forma(string text, string code)
    {
        Assert.Equal(code, WebCode.Parse(text));
    }

    [Theory]
    [InlineData("")]
    [InlineData("k7m2x")]
    [InlineData("k7m2xq9")]
    [InlineData("k7m0xq")] // el 0 no está en los códigos
    [InlineData("https://otro.sitio/k7m2xq/algo")]
    public void Rechaza_lo_que_no_es_un_codigo(string text)
    {
        Assert.Null(WebCode.Parse(text));
    }
}

public class WebLibraryClientTests
{
    private static readonly BuildOrder Scouts = BuildOrderSerializer.Load(Path.Combine(TestData.BuildOrdersDir, "scouts_feudal.json"));

    /// <summary>Servidor falso: guarda los pedidos y responde lo que se le indica.</summary>
    private sealed class FakeWeb(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public List<(HttpMethod Method, string Url, string? Auth, string? Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel)
        {
            Requests.Add((request.Method, request.RequestUri!.ToString(), request.Headers.Authorization?.ToString(),
                request.Content is null ? null : await request.Content.ReadAsStringAsync(cancel)));
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }

    private static (WebLibraryClient Client, FakeWeb Web) Client(HttpStatusCode status, string body)
    {
        var web = new FakeWeb(status, body);
        return (new WebLibraryClient(new HttpClient(web), new Uri("https://bo.example/")), web);
    }

    [Fact]
    public async Task Baja_una_BO_por_su_codigo()
    {
        var json = new JsonObject
        {
            ["code"] = "k7m2xq",
            ["author"] = "Ana",
            ["build_order"] = JsonNode.Parse(BuildOrderSerializer.Serialize(Scouts)),
        };
        var (client, web) = Client(HttpStatusCode.OK, json.ToJsonString());

        var result = await client.DownloadAsync("k7m2xq");

        Assert.Equal("https://bo.example/api/bo/k7m2xq", web.Requests.Single().Url);
        Assert.Equal("Ana", result.Author);
        Assert.Equal(Scouts.Name, result.BuildOrder.Name);
        Assert.Equal(Scouts.Steps.Count, result.BuildOrder.Steps.Count);
    }

    [Fact]
    public async Task Publica_con_el_token_y_la_Feudal_estimada()
    {
        var (client, web) = Client(HttpStatusCode.Created, """{ "code": "k7m2xq", "url": "https://bo.example/bo/k7m2xq" }""");

        var published = await client.PublishAsync(Scouts, "bocoach_" + new string('a', 64), 475.4, null);

        Assert.Equal(new PublishedBuildOrder("k7m2xq", "https://bo.example/bo/k7m2xq"), published);
        var request = web.Requests.Single();
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://bo.example/api/bo", request.Url);
        Assert.Equal($"Bearer bocoach_{new string('a', 64)}", request.Auth);
        var body = JsonNode.Parse(request.Body!)!;
        Assert.Equal(475, body["feudal_click"]!.GetValue<double>());
        Assert.Null(body["castle_click"]);
        Assert.Equal(Scouts.Name, body["build_order"]!["name"]!.GetValue<string>());
    }

    [Fact]
    public async Task Con_codigo_reemplaza_la_BO()
    {
        var (client, web) = Client(HttpStatusCode.OK, """{ "code": "k7m2xq", "url": "https://bo.example/bo/k7m2xq" }""");

        await client.PublishAsync(Scouts, "bocoach_x", null, null, code: "k7m2xq");

        Assert.Equal((HttpMethod.Put, "https://bo.example/api/bo/k7m2xq"), (web.Requests[0].Method, web.Requests[0].Url));
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, """{ "error": "No hay ninguna build order con el código k7m2xq." }""", "No hay ninguna build order")]
    [InlineData(HttpStatusCode.Unauthorized, "", "token")]
    [InlineData(HttpStatusCode.InternalServerError, "<html>", "500")]
    public async Task Los_errores_llegan_explicados(HttpStatusCode status, string body, string expected)
    {
        var (client, _) = Client(status, body);

        var error = await Assert.ThrowsAsync<WebLibraryException>(() => client.DownloadAsync("k7m2xq"));

        Assert.Contains(expected, error.Message);
        Assert.Equal(status, error.Status);
    }

    [Theory]
    [InlineData("http://bo.example/", false)]
    [InlineData("http://localhost:3000/", true)]
    [InlineData("http://127.0.0.1:3000/", true)]
    [InlineData("https://bo.example/", true)]
    public void El_token_solo_viaja_por_https_o_a_esta_PC(string url, bool safe) =>
        Assert.Equal(safe, WebLibraryClient.SendsTokenSafely(new Uri(url)));

    [Fact]
    public async Task Por_http_no_manda_el_token()
    {
        var web = new FakeWeb(HttpStatusCode.Created, """{ "code": "k7m2xq", "url": "http://bo.example/bo/k7m2xq" }""");
        var client = new WebLibraryClient(new HttpClient(web), new Uri("http://bo.example/"));

        var error = await Assert.ThrowsAsync<WebLibraryException>(() => client.PublishAsync(Scouts, "bocoach_x", null, null));

        Assert.Contains("https", error.Message);
        Assert.Empty(web.Requests);
    }

    [Fact]
    public async Task Un_codigo_mal_formado_no_arma_otra_direccion()
    {
        var (client, web) = Client(HttpStatusCode.OK, "{}");

        await Assert.ThrowsAsync<WebLibraryException>(() => client.PublishAsync(Scouts, "bocoach_x", null, null, code: "../cuenta"));

        Assert.Empty(web.Requests);
    }

    [Fact]
    public async Task Un_codigo_invalido_no_llega_a_la_web()
    {
        var (client, web) = Client(HttpStatusCode.OK, "{}");

        await Assert.ThrowsAsync<WebLibraryException>(() => client.DownloadAsync("../etc"));

        Assert.Empty(web.Requests);
    }
}
