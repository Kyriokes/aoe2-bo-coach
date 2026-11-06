using System.Net;
using BoCoach.Core.Web;

namespace BoCoach.Core.Tests.Web;

/// <summary>El aviso de versión nueva: al abrir, la app le pregunta a GitHub cuál es la última release.</summary>
public class UpdateCheckTests
{
    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public HttpRequestMessage? Last { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Last = request;
            return Task.FromResult(respond(request));
        }
    }

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body) };

    [Theory]
    [InlineData("""{"tag_name":"v0.9.95","name":"BO Coach v0.9.95"}""", "0.9.95")]
    [InlineData("""{"tag_name":"v1.0.0"}""", "1.0.0")]
    public void Lee_la_etiqueta_de_la_release(string json, string expected) =>
        Assert.Equal(Version.Parse(expected), UpdateCheck.ParseLatest(json));

    [Theory]
    [InlineData("""{"tag_name":"0.9.95"}""")]
    [InlineData("""{"tag_name":"v0.9"}""")]
    [InlineData("""{"tag_name":"v0.9.95-beta"}""")]
    [InlineData("""{"tag_name":"v0.9.95.1"}""")]
    [InlineData("""{"tag_name":12}""")]
    [InlineData("""{"message":"API rate limit exceeded"}""")]
    [InlineData("""[]""")]
    [InlineData("no es json")]
    public void Lo_que_no_es_una_etiqueta_vN_N_N_no_avisa_nada(string json) =>
        Assert.Null(UpdateCheck.ParseLatest(json));

    [Theory]
    [InlineData("0.9.95", "0.9.94", true)]
    [InlineData("0.10.0", "0.9.94", true)]
    [InlineData("0.9.94", "0.9.94", false)]
    [InlineData("0.9.93", "0.9.94", false)]
    public void Avisa_solo_si_la_de_GitHub_es_mas_nueva(string latest, string current, bool expected) =>
        Assert.Equal(expected, UpdateCheck.IsNewer(Version.Parse(latest), Version.Parse(current)));

    [Fact]
    public void La_version_de_la_app_ignora_la_cuarta_parte_del_ensamblado() =>
        Assert.False(UpdateCheck.IsNewer(Version.Parse("0.9.94"), new Version(0, 9, 94, 0)));

    [Fact]
    public async Task Pide_la_ultima_release_con_un_User_Agent_y_devuelve_la_nueva()
    {
        var handler = new FakeHandler(_ => Json("""{"tag_name":"v0.9.95"}"""));
        using var http = new HttpClient(handler);

        var newer = await UpdateCheck.NewerVersionAsync(http, new Version(0, 9, 94));

        Assert.Equal(new Version(0, 9, 95), newer);
        Assert.Equal(UpdateCheck.LatestReleaseApi, handler.Last!.RequestUri!.ToString());
        Assert.NotEmpty(handler.Last.Headers.UserAgent);
    }

    [Fact]
    public async Task Con_la_misma_version_no_avisa()
    {
        using var http = new HttpClient(new FakeHandler(_ => Json("""{"tag_name":"v0.9.94"}""")));
        Assert.Null(await UpdateCheck.NewerVersionAsync(http, new Version(0, 9, 94)));
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task Si_GitHub_responde_mal_no_avisa_ni_falla(HttpStatusCode status)
    {
        using var http = new HttpClient(new FakeHandler(_ => Json("""{"tag_name":"v9.9.9"}""", status)));
        Assert.Null(await UpdateCheck.NewerVersionAsync(http, new Version(0, 9, 94)));
    }

    [Fact]
    public async Task Sin_internet_no_avisa_ni_falla()
    {
        using var http = new HttpClient(new FakeHandler(_ => throw new HttpRequestException("sin red")));
        Assert.Null(await UpdateCheck.NewerVersionAsync(http, new Version(0, 9, 94)));
    }

    [Fact]
    public void El_enlace_de_descarga_es_el_zip_de_la_ultima_release_con_nombre_fijo() =>
        Assert.Equal("https://github.com/Kyriokes/aoe2-bo-coach/releases/latest/download/BoCoach-win-x64.zip", UpdateCheck.DownloadUrl);
}
