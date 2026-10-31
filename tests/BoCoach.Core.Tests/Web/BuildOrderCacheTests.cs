using System.Net;
using System.Text.Json.Nodes;
using BoCoach.Core.Web;
using static BoCoach.Core.Tests.Web.WebLibraryClientAppTests;

namespace BoCoach.Core.Tests.Web;

/// <summary>La app guarda la última lista y las BO usadas, y sin internet sigue con eso (parte 4).</summary>
public sealed class BuildOrderCacheTests : IDisposable
{
    private const string Token = "bocoach_0123456789abcdef0123456789abcdef0123456789abcdef0123456789ab";

    private readonly string _dir = Directory.CreateTempSubdirectory("bocoach-cache-").FullName;
    private readonly FakeWeb _web = new();
    private string? _token = Token;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private BuildOrderCache Cache() => new(_dir, new WebLibraryClient(new HttpClient(_web), new Uri("https://bo.example/")), () => _token);

    private void ServeList(params JsonObject[] items) =>
        _web.Routes["GET /api/app/mis-bo"] = (HttpStatusCode.OK, new JsonObject { ["account"] = "Ana", ["items"] = new JsonArray(items) }.ToJsonString());

    private void ServeBuildOrder(string code, string updated = "2026-10-31T10:00:00Z", string name = "Scouts") =>
        _web.Routes[$"GET /api/bo/{code}"] = (HttpStatusCode.OK, BuildOrderJson(code, updated, name));

    private int Requests(string route) => _web.Requests.Count(r => $"{r.Method} {r.Path}" == route);

    [Fact]
    public async Task La_lista_se_guarda_y_sin_internet_se_usa_la_guardada()
    {
        ServeList(Entry("k7m2xq", "Mía", own: true));
        Assert.Null(await Cache().RefreshAsync());

        _web.Offline = true;
        var offline = Cache();
        var error = await offline.RefreshAsync();

        Assert.NotNull(error);
        Assert.Equal("Ana", offline.List.Account);
        Assert.Equal(["k7m2xq"], offline.List.Items.Select(i => i.Code));
    }

    [Fact]
    public async Task Una_BO_se_baja_una_vez_y_queda_con_su_codigo_como_id()
    {
        ServeList(Entry("k7m2xq", "Mía"));
        ServeBuildOrder("k7m2xq");
        var cache = Cache();
        await cache.RefreshAsync();

        var first = await cache.GetAsync("k7m2xq");
        var second = await Cache().GetAsync("k7m2xq");

        Assert.Equal(1, Requests("GET /api/bo/k7m2xq"));
        Assert.Equal("k7m2xq", first.Id);
        Assert.Equal(first.Name, second.Name);
        Assert.True(cache.IsSaved("k7m2xq"));
        Assert.Equal("k7m2xq", Assert.Single(cache.AllSaved()).Id);
    }

    [Fact]
    public async Task Se_baja_de_nuevo_si_la_lista_trae_una_fecha_mas_nueva()
    {
        ServeList(Entry("k7m2xq", "Mía", updated: "2026-10-31T10:00:00Z"));
        ServeBuildOrder("k7m2xq", "2026-10-31T10:00:00Z", "Vieja");
        var cache = Cache();
        await cache.RefreshAsync();
        await cache.GetAsync("k7m2xq");

        ServeList(Entry("k7m2xq", "Mía", updated: "2026-11-01T10:00:00Z"));
        ServeBuildOrder("k7m2xq", "2026-11-01T10:00:00Z", "Nueva");
        await cache.RefreshAsync();
        var updated = await cache.GetAsync("k7m2xq");

        Assert.Equal("Nueva", updated.Name);
        Assert.Equal(2, Requests("GET /api/bo/k7m2xq"));
    }

    [Fact]
    public async Task Sin_internet_usa_la_guardada_y_si_no_la_tiene_avisa()
    {
        ServeList(Entry("k7m2xq", "Mía", updated: "2026-11-01T10:00:00Z"), Entry("abcdef", "Otra"));
        ServeBuildOrder("k7m2xq", "2026-10-31T10:00:00Z");
        var cache = Cache();
        await cache.RefreshAsync();
        await cache.GetAsync("k7m2xq");

        _web.Offline = true;
        Assert.Equal("Scouts", (await cache.GetAsync("k7m2xq")).Name);
        await Assert.ThrowsAsync<WebLibraryException>(() => cache.GetAsync("abcdef"));
    }

    [Fact]
    public async Task Sumar_un_codigo_suscribe_refresca_y_baja()
    {
        ServeList(Entry("abcdef", "Suscripta"));
        ServeBuildOrder("abcdef");
        _web.Routes["POST /api/app/suscripciones/abcdef"] = (HttpStatusCode.OK, """{"code":"abcdef","own":false}""");
        var cache = Cache();

        var entry = await cache.AddAsync("abcdef");

        Assert.Equal("Suscripta", entry?.Name);
        Assert.Equal(1, Requests("POST /api/app/suscripciones/abcdef"));
        Assert.True(cache.IsSaved("abcdef"));
    }

    [Fact]
    public void Lo_guardado_roto_no_tira()
    {
        File.WriteAllText(Path.Combine(_dir, "mis-bo.json"), "{ esto no es json");
        Directory.CreateDirectory(Path.Combine(_dir, "bo"));
        File.WriteAllText(Path.Combine(_dir, "bo", "k7m2xq.json"), """{"updated_at":"x","build_order":{"steps":"no"}}""");

        var cache = Cache();

        Assert.Empty(cache.List.Items);
        Assert.Null(cache.Saved("k7m2xq"));
        Assert.False(cache.IsSaved("k7m2xq"));
        Assert.Empty(cache.AllSaved());
    }

    [Fact]
    public async Task Sin_token_no_pide_nada()
    {
        _token = null;
        var cache = Cache();

        Assert.Equal("Conectá tu cuenta para elegir BO.", await cache.RefreshAsync());
        Assert.Empty(_web.Requests);
    }
}
