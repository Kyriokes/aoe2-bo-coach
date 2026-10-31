using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using BoCoach.App.Coaching;
using BoCoach.App.Web;
using BoCoach.Core.BuildOrders;
using BoCoach.Core.Web;

namespace BoCoach.App.Tests;

/// <summary>La sección "Build order" de la ventana: tu cuenta, tu lista y sumar BO (parte 4).</summary>
public sealed class BuildOrdersViewModelTests : IDisposable
{
    private const string Token = "bocoach_0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("bocoach-bo-");
    private readonly FakeWeb _web = new();
    private readonly FakeDialogs _dialogs = new();
    private readonly WebAccount _account;
    private readonly BuildOrderCache _cache;

    private static readonly Core.Data.GameData Data = Core.Data.GameDataLoader.LoadLatest(Path.Combine(AppContext.BaseDirectory, "data", "game_data"));

    private static readonly BuildOrder Scouts = BuildOrderSerializer.Load(Path.Combine(AppContext.BaseDirectory, "fixtures", "build_orders", "scouts_feudal.json"));

    public BuildOrdersViewModelTests()
    {
        var http = new HttpClient(_web);
        _account = new WebAccount(http, Path.Combine(_dir.FullName, "web.json"));
        _cache = new BuildOrderCache(Path.Combine(_dir.FullName, "cache"), () => _account.Client, () => _account.Settings.Token);
    }

    public void Dispose() => _dir.Delete(recursive: true);

    private sealed class FakeWeb : HttpMessageHandler
    {
        public Dictionary<string, (HttpStatusCode Status, string Body)> Routes { get; } = [];
        public List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel)
        {
            var route = $"{request.Method} {request.RequestUri!.AbsolutePath}";
            Requests.Add(route);
            var (status, body) = Routes.TryGetValue(route, out var found) ? found : (HttpStatusCode.NotFound, """{"error":"No existe."}""");
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }

    private sealed class FakeDialogs : IBuildOrderDialogs
    {
        public string? Answer { get; set; }
        public bool ConfirmAnswer { get; set; } = true;
        public List<string> Confirmations { get; } = [];
        public List<string> Opened { get; } = [];

        public bool Confirm(string message)
        {
            Confirmations.Add(message);
            return ConfirmAnswer;
        }

        public string? AskText(string title, string message, string initial = "") => Answer;
        public WebSettings? EditWebAccount(WebSettings current) => null;
        public void OpenUrl(string url) => Opened.Add(url);
    }

    private BuildOrdersViewModel ViewModel() => new(_cache, _account, _dialogs, Data);

    private static JsonObject Entry(string code, string name, bool own = false) => new()
    {
        ["code"] = code, ["name"] = name, ["civ"] = "franks", ["map"] = null, ["style"] = "scouts", ["tags"] = new JsonArray(),
        ["author"] = "Ana", ["guide_author"] = "Cicero", ["own"] = own, ["visibility"] = own ? "private" : "public",
        ["updated_at"] = "2026-10-31T10:00:00Z", ["feudal_click"] = null, ["castle_click"] = null,
    };

    private void ServeList(params JsonObject[] items) =>
        _web.Routes["GET /api/app/mis-bo"] = (HttpStatusCode.OK, new JsonObject { ["account"] = "Ana", ["items"] = new JsonArray(items) }.ToJsonString());

    private void ServeBuildOrder(string code, string name) =>
        _web.Routes[$"GET /api/bo/{code}"] = (HttpStatusCode.OK, new JsonObject
        {
            ["code"] = code, ["name"] = name, ["author"] = "Ana", ["guide_author"] = "Cicero", ["updated_at"] = "2026-10-31T10:00:00Z",
            ["build_order"] = JsonNode.Parse(BuildOrderSerializer.Serialize(Scouts with { Name = name })),
        }.ToJsonString());

    [Fact]
    public async Task Sin_cuenta_pide_conectarla_y_no_pide_nada()
    {
        var vm = ViewModel();
        await vm.RefreshAsync();

        Assert.False(vm.Connected);
        Assert.Equal("Conectá tu cuenta para elegir BO", vm.AccountText);
        Assert.Empty(_web.Requests);
    }

    [Fact]
    public async Task Conectar_con_el_token_lo_guarda_y_muestra_la_cuenta()
    {
        ServeList(Entry("k7m2xq", "Mía", own: true));
        var vm = ViewModel();

        await vm.HandleLinkAsync($"bocoach://conectar/{Token}");

        Assert.Contains("Ana", Assert.Single(_dialogs.Confirmations));
        Assert.Equal(Token, _account.Settings.Token);
        Assert.True(vm.Connected);
        Assert.Equal("Conectada como Ana", vm.AccountText);
        var entry = Assert.Single(vm.Entries);
        Assert.Equal("Mía", entry.Name);
        Assert.Equal("se baja al usarla", entry.State);
        Assert.Contains("tuya", entry.Detail);
    }

    [Fact]
    public async Task Un_enlace_para_conectar_pregunta_antes_de_cambiar_la_cuenta()
    {
        // Revisión de la parte 4: cualquier página puede abrir bocoach://conectar/<token> con el token de otra cuenta.
        ServeList(Entry("k7m2xq", "Mía", own: true));
        _dialogs.ConfirmAnswer = false;
        var vm = ViewModel();

        await vm.HandleLinkAsync($"bocoach://conectar/{Token}");

        Assert.Contains("Ana", Assert.Single(_dialogs.Confirmations));
        Assert.False(_account.Settings.HasToken);
        Assert.False(vm.Connected);
    }

    [Fact]
    public async Task Actualizar_la_lista_no_pierde_la_elegida_ni_la_baja_de_nuevo()
    {
        _account.Save(_account.Settings with { Token = Token });
        ServeList(Entry("k7m2xq", "Mía", own: true));
        ServeBuildOrder("k7m2xq", "Mía");
        var vm = ViewModel();
        await vm.RefreshAsync();
        await vm.ChooseAsync(vm.Entries[0]);

        await vm.RefreshAsync();

        Assert.Equal("Mía", vm.SelectedBuildOrder?.Name);
        Assert.Equal("k7m2xq", vm.SelectedEntry?.Code);
        Assert.Equal(1, _web.Requests.Count(r => r == "GET /api/bo/k7m2xq"));
    }

    [Fact]
    public async Task Pegar_un_codigo_lo_suma_y_queda_guardada()
    {
        _account.Save(_account.Settings with { Token = Token });
        ServeList(Entry("abcdef", "Suscripta"));
        ServeBuildOrder("abcdef", "Suscripta");
        _web.Routes["POST /api/app/suscripciones/abcdef"] = (HttpStatusCode.OK, """{"code":"abcdef","own":false}""");
        _dialogs.Answer = "https://aoe2-bo-coach.vercel.app/bo/abcdef";
        var vm = ViewModel();

        await vm.PasteCodeAsync();

        Assert.Contains("POST /api/app/suscripciones/abcdef", _web.Requests);
        Assert.Equal("guardada", Assert.Single(vm.Entries).State);
        Assert.Empty(_dialogs.Confirmations);
    }

    [Fact]
    public async Task Un_enlace_de_la_web_pide_confirmacion_con_nombre_y_autor()
    {
        _account.Save(_account.Settings with { Token = Token });
        ServeList(Entry("abcdef", "Suscripta"));
        ServeBuildOrder("abcdef", "Suscripta");
        _web.Routes["POST /api/app/suscripciones/abcdef"] = (HttpStatusCode.OK, """{"code":"abcdef","own":false}""");
        _dialogs.ConfirmAnswer = false;
        var vm = ViewModel();

        await vm.HandleLinkAsync("bocoach://import/abcdef");

        var question = Assert.Single(_dialogs.Confirmations);
        Assert.Contains("Suscripta", question);
        Assert.Contains("Ana", question);
        Assert.DoesNotContain("POST /api/app/suscripciones/abcdef", _web.Requests);
    }

    [Fact]
    public async Task Con_el_token_vencido_pide_conectar_de_nuevo_y_sigue_la_lista()
    {
        _account.Save(_account.Settings with { Token = Token });
        ServeList(Entry("k7m2xq", "Mía", own: true));
        var vm = ViewModel();
        await vm.RefreshAsync();
        _web.Routes["GET /api/app/mis-bo"] = (HttpStatusCode.Unauthorized, """{"error":"El token de la app no es válido."}""");

        await vm.RefreshAsync();

        Assert.StartsWith("Conectá tu cuenta de nuevo", vm.Status);
        Assert.Single(vm.Entries);
    }

    [Fact]
    public async Task Elegir_una_que_no_esta_guardada_la_baja()
    {
        _account.Save(_account.Settings with { Token = Token });
        ServeList(Entry("k7m2xq", "Mía", own: true));
        ServeBuildOrder("k7m2xq", "Mía");
        var vm = ViewModel();
        await vm.RefreshAsync();

        await vm.ChooseAsync(vm.Entries[0]);

        Assert.Equal("Mía", vm.SelectedBuildOrder?.Name);
        Assert.Equal("k7m2xq", vm.SelectedBuildOrder?.Id);
        Assert.Equal("guardada", vm.Entries[0].State);
    }
}
