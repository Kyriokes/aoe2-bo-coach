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

        var sinRed = await EloLookup.SearchAsync(new HttpClient(new FakeHandler(_ => throw new HttpRequestException("No such host is known"))), "TheViper");
        Assert.Empty(sinRed.Candidates);
        // Sin el mensaje de .NET, que viene en inglés.
        Assert.Equal("No se pudo consultar la API del juego: revisá la conexión.", sinRed.Error);

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

    [Fact]
    public async Task Otra_respuesta_de_error_no_es_un_nombre_que_no_existe()
    {
        // Con 200 pero otro código (límite de pedidos, mantenimiento) o algo que no es JSON: no es "no encontré".
        var otroCodigo = await EloLookup.SearchAsync(new HttpClient(new FakeHandler(_ => Json("""{"result":{"code":5,"message":"BUSY"}}"""))), "x");
        Assert.Empty(otroCodigo.Candidates);
        Assert.StartsWith("La API del juego", otroCodigo.Error);

        var html = await EloLookup.SearchAsync(new HttpClient(new FakeHandler(_ => Json("<html>mantenimiento</html>"))), "x");
        Assert.Empty(html.Candidates);
        Assert.StartsWith("La API del juego", html.Error);
    }

    [Fact]
    public async Task Un_error_inesperado_o_una_fecha_rara_no_tiran_excepcion()
    {
        var raro = await EloLookup.SearchAsync(new HttpClient(new FakeHandler(_ => throw new InvalidOperationException("charset"))), "x");
        Assert.Empty(raro.Candidates);
        Assert.Equal("No se pudo consultar la API del juego.", raro.Error);

        // En milisegundos (por si la API cambia) se entiende; un número imposible queda sin fecha.
        var json = Fixture.Replace("1735099389", "1735099389000").Replace("1587690245", "9000000000000000000");
        var candidatos = EloLookup.Parse(json);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1735099389), candidatos.Single(c => c.ProfileId == 3003).LastMatch);
        Assert.Null(candidatos.Single(c => c.ProfileId == 3002).LastMatch);
    }
}
