using System.Net;
using BoCoach.Core.Web;

namespace BoCoach.Core.Tests.Web;

/// <summary>D10: buscar el Elo de un jugador por su nombre en la API oficial del juego (botón "Buscar").</summary>
public class EloLookupTests
{
    private static string Fixture => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "api", "personal-stat-theviper.json"));

    private const string Unknown = """{"result":{"code":9, "message":"UNKNOWN_ALIASES"}}""";

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public HttpRequestMessage? Last { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Last = request;
            return Task.FromResult(respond(request));
        }
    }

    private static HttpResponseMessage Json(string body, HttpStatusCode code = HttpStatusCode.OK) =>
        new(code) { Content = new StringContent(body) };

    [Fact]
    public void Lee_los_candidatos_y_su_Elo()
    {
        var candidatos = EloLookup.Parse(Fixture);

        // Ordenados por la última partida (en 1v1 o por equipos), la más nueva primero.
        Assert.Equal([3003L, 3001L, 3002L], candidatos.Select(c => c.ProfileId)); // perfiles inventados en el fixture
        var primero = candidatos[0];
        Assert.Equal("TheViper", primero.Alias);
        Assert.Equal("vn", primero.Country);
        Assert.Equal(843, primero.Elo1v1);
        Assert.Equal(856, primero.EloTeam);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1735099389), primero.LastMatch);
        Assert.Null(candidatos[2].Elo1v1);
        Assert.Equal(915, candidatos[2].EloTeam);
    }

    [Fact]
    public async Task Nombre_que_no_existe_no_es_error()
    {
        Assert.Empty(EloLookup.Parse(Unknown));
        Assert.Empty(EloLookup.Parse("{ roto"));

        var handler = new FakeHandler(_ => Json(Unknown));
        var busqueda = await EloLookup.SearchAsync(new HttpClient(handler), "nadie");
        Assert.Empty(busqueda.Candidates);
        Assert.Null(busqueda.Error);
        Assert.Contains("aoe2-bo-coach", handler.Last!.Headers.UserAgent.ToString());
    }

    [Fact]
    public async Task Busqueda_con_la_API_caida_da_un_error_legible()
    {
        var caida = await EloLookup.SearchAsync(new HttpClient(new FakeHandler(_ => Json("", HttpStatusCode.ServiceUnavailable))), "TheViper");
        Assert.Empty(caida.Candidates);
        Assert.StartsWith("La API del juego", caida.Error);

        var sinRed = await EloLookup.SearchAsync(new HttpClient(new FakeHandler(_ => throw new HttpRequestException("sin red"))), "TheViper");
        Assert.Empty(sinRed.Candidates);
        Assert.StartsWith("No se pudo consultar", sinRed.Error);

        var bien = await EloLookup.SearchAsync(new HttpClient(new FakeHandler(_ => Json(Fixture))), "TheViper");
        Assert.Equal(3, bien.Candidates.Count);
        Assert.Null(bien.Error);
    }

    [Fact]
    public void La_direccion_escapa_el_nombre_y_es_https()
    {
        var direccion = EloLookup.Address("Juan Pérez \"x\"&y");

        Assert.Equal("https", direccion.Scheme);
        Assert.Equal("aoe-api.worldsedgelink.com", direccion.Host);
        var aliases = direccion.Query.TrimStart('?').Split('&')
            .Select(p => p.Split('=', 2)).Single(p => p[0] == "aliases")[1];
        Assert.Equal("[\"Juan Pérez \\\"x\\\"&y\"]", Uri.UnescapeDataString(aliases));
    }
}
