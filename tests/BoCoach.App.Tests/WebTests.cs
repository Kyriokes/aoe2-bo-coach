using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using BoCoach.App.Editor;
using BoCoach.App.Web;
using BoCoach.Core.BuildOrders;
using Microsoft.Win32;

namespace BoCoach.App.Tests;

public sealed class WebTests : IDisposable
{
    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("bocoach-web-");
    private readonly CoachData _data;

    public WebTests()
    {
        _data = CoachData.Load(Path.Combine(_dir.FullName, "biblioteca"));
    }

    public void Dispose() => _dir.Delete(recursive: true);

    private string SettingsPath => Path.Combine(_dir.FullName, "web.json");

    private const string Token = "bocoach_0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    /// <summary>Web falsa: responde con lo que diga <see cref="Respond"/> y guarda los pedidos.</summary>
    private sealed class FakeWeb : HttpMessageHandler
    {
        public Func<HttpRequestMessage, (HttpStatusCode, string)> Respond { get; set; } = _ => (HttpStatusCode.OK, "{}");
        public List<(HttpMethod Method, string Path, string? Auth)> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel)
        {
            Requests.Add((request.Method, request.RequestUri!.AbsolutePath, request.Headers.Authorization?.Parameter));
            var (status, body) = Respond(request);
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }

    private sealed class FakeDialogs : IDialogs
    {
        public string? Text { get; set; }
        public WebSettings? Account { get; set; }
        public List<string> Errors { get; } = [];
        public List<string> Opened { get; } = [];
        public int AccountDialogs { get; private set; }

        public string? PickFileToOpen() => null;
        public string? PickFileToSave(string suggestedName) => null;
        public bool AskSaveChanges(string name) => true;
        public bool Confirm(string message) => true;
        public void ShowError(string message) => Errors.Add(message);
        public string? AskText(string title, string message, string initial = "") => Text;
        public WebSettings? EditWebAccount(WebSettings current) { AccountDialogs++; return Account; }
        public void OpenUrl(string url) => Opened.Add(url);
    }

    private (MainViewModel Vm, FakeWeb Web, FakeDialogs Dialogs, WebAccount Account) Editor()
    {
        var web = new FakeWeb();
        var dialogs = new FakeDialogs();
        var account = new WebAccount(new HttpClient(web), SettingsPath);
        return (new MainViewModel(_data, dialogs, account), web, dialogs, account);
    }

    [Fact]
    public void El_token_se_guarda_cifrado_y_se_lee_igual()
    {
        var account = new WebAccount(new HttpClient(), SettingsPath);
        account.Save(new WebSettings { WebUrl = "https://bo.example", Token = Token });
        account.RememberPublished("scouts", "k7m2xq");

        var file = File.ReadAllText(SettingsPath);
        Assert.DoesNotContain(Token, file);
        var again = new WebAccount(new HttpClient(), SettingsPath).Settings;
        Assert.Equal("https://bo.example", again.WebUrl);
        Assert.Equal(Token, again.Token);
        Assert.Equal("k7m2xq", again.Published["scouts"]);
    }

    [Fact]
    public void Un_archivo_roto_vuelve_a_lo_de_fabrica()
    {
        File.WriteAllText(SettingsPath, "{ esto no es json");

        var settings = new WebAccount(new HttpClient(), SettingsPath).Settings;

        Assert.Equal(WebSettings.DefaultUrl, settings.WebUrl);
        Assert.False(settings.HasToken);
    }

    [Fact]
    public async Task Importa_una_BO_de_la_web_con_el_enlace_de_la_pagina()
    {
        var (vm, web, dialogs, _) = Editor();
        var scouts = BuildOrderSerializer.Parse(File.ReadAllText(Path.Combine(CoachData.DataDir, "build_orders", "scouts_feudal.json")));
        web.Respond = _ => (HttpStatusCode.OK, new JsonObject
        {
            ["code"] = "k7m2xq",
            ["author"] = "Ana",
            ["build_order"] = JsonNode.Parse(BuildOrderSerializer.Serialize(scouts with { Name = "Scouts de Ana" })),
        }.ToJsonString());
        dialogs.Text = "bocoach://import/k7m2xq";

        await vm.ImportFromWebCommand.ExecuteAsync(null);

        Assert.Equal("/api/bo/k7m2xq", web.Requests.Single().Path);
        Assert.Equal("Scouts de Ana", vm.Editor?.Name);
        Assert.Contains(vm.Entries, e => e.Name == "Scouts de Ana");
        Assert.Contains("de Ana", vm.Status);
        Assert.Empty(dialogs.Errors);
    }

    [Fact]
    public async Task Un_codigo_que_no_existe_se_explica()
    {
        var (vm, web, dialogs, _) = Editor();
        web.Respond = _ => (HttpStatusCode.NotFound, """{ "error": "No hay ninguna build order con el código k7m2xq." }""");

        Assert.False(await vm.ImportFromLinkAsync("k7m2xq"));

        Assert.Contains("No hay ninguna build order", Assert.Single(dialogs.Errors));
    }

    [Fact]
    public async Task Publicar_pide_la_cuenta_la_primera_vez_y_despues_reemplaza_la_misma_BO()
    {
        var (vm, web, dialogs, account) = Editor();
        web.Respond = r => (r.Method == HttpMethod.Post ? HttpStatusCode.Created : HttpStatusCode.OK,
            """{ "code": "k7m2xq", "url": "https://bo.example/bo/k7m2xq" }""");
        dialogs.Account = new WebSettings { WebUrl = "https://bo.example", Token = Token };
        vm.SelectedEntry = vm.Entries.First(e => e.Name.Contains("Scouts"));

        await vm.PublishToWebCommand.ExecuteAsync(null);
        await vm.PublishToWebCommand.ExecuteAsync(null);

        Assert.Equal(1, dialogs.AccountDialogs);
        Assert.Equal([(HttpMethod.Post, "/api/bo", Token), (HttpMethod.Put, "/api/bo/k7m2xq", Token)], web.Requests);
        Assert.Equal("k7m2xq", account.Settings.Published[vm.Editor!.ToBuildOrder().Id]);
        Assert.Equal("https://bo.example/bo/k7m2xq", dialogs.Opened.Last());
        Assert.Empty(dialogs.Errors);
    }

    [Fact]
    public async Task Si_la_BO_se_borro_de_la_web_se_publica_de_nuevo()
    {
        var (vm, web, dialogs, account) = Editor();
        account.Save(new WebSettings { WebUrl = "https://bo.example", Token = Token });
        vm.SelectedEntry = vm.Entries.First(e => e.Name.Contains("Scouts"));
        account.RememberPublished(vm.Editor!.ToBuildOrder().Id, "zzzzzz");
        web.Respond = r => r.Method == HttpMethod.Put
            ? (HttpStatusCode.Forbidden, """{ "error": "No hay una BO tuya con el código zzzzzz." }""")
            : (HttpStatusCode.Created, """{ "code": "k7m2xq", "url": "https://bo.example/bo/k7m2xq" }""");

        await vm.PublishToWebCommand.ExecuteAsync(null);

        Assert.Equal([HttpMethod.Put, HttpMethod.Post], web.Requests.Select(r => r.Method));
        Assert.Equal("k7m2xq", account.Settings.Published[vm.Editor!.ToBuildOrder().Id]);
        Assert.Empty(dialogs.Errors);
    }

    [Fact]
    public async Task Una_BO_con_errores_no_se_publica()
    {
        var (vm, web, dialogs, account) = Editor();
        account.Save(new WebSettings { WebUrl = "https://bo.example", Token = Token });
        vm.NewCommand.Execute(null); // BO nueva sin pasos: tiene errores

        await vm.PublishToWebCommand.ExecuteAsync(null);

        Assert.Empty(web.Requests);
        Assert.Contains("errores", Assert.Single(dialogs.Errors));
    }

    [Fact]
    public void Registra_los_enlaces_bocoach_para_este_ejecutable()
    {
        // En una clave de prueba propia, no en la de verdad (HKCU\Software\Classes).
        var key = $@"Software\BoCoachTests-{Guid.NewGuid():N}\Classes";
        var links = new LinkProtocol(key);
        try
        {
            Assert.False(links.IsRegisteredTo(@"C:\BoCoach\BoCoach.exe"));
            links.Register(@"C:\BoCoach\BoCoach.exe");

            Assert.True(links.IsRegisteredTo(@"C:\BoCoach\BoCoach.exe"));
            Assert.False(links.IsRegisteredTo(@"D:\otra\BoCoach.exe"));
            using var root = Registry.CurrentUser.OpenSubKey($@"{key}\bocoach");
            Assert.Equal("", root!.GetValue("URL Protocol"));
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(key.Split('\\')[0] + "\\" + key.Split('\\')[1], throwOnMissingSubKey: false);
        }
    }

    [Fact]
    public async Task Un_enlace_abierto_con_la_app_ya_abierta_le_llega_a_esa_ventana()
    {
        var name = $"BoCoachTest-{Guid.NewGuid():N}";
        using var first = SingleInstance.Claim(null, name);
        Assert.NotNull(first);
        var received = new TaskCompletionSource<string>();
        first.Listen(link => received.TrySetResult(link));

        var second = SingleInstance.Claim("bocoach://import/k7m2xq", name);

        Assert.Null(second);
        Assert.Equal("bocoach://import/k7m2xq", await received.Task.WaitAsync(TimeSpan.FromSeconds(10)));
    }
}
