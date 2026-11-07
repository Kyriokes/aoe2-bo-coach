using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using BoCoach.App.Web;
using BoCoach.Core.BuildOrders;
using BoCoach.Core.Web;
using Microsoft.Win32;

namespace BoCoach.App.Tests;

public sealed class WebTests : IDisposable
{
    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("bocoach-web-");
    private readonly CoachData _data;

    public WebTests()
    {
        _data = CoachData.Load();
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
        Assert.Equal("{ esto no es json", File.ReadAllText(SettingsPath + ".roto"));
    }

    // AP-04: un solo campo de otro tipo perdía toda la cuenta, y un archivo que no se podía leer cortaba el arranque.
    [Fact]
    public void Un_valor_raro_en_un_campo_de_la_cuenta_no_pierde_los_demas()
    {
        new WebAccount(new HttpClient(), SettingsPath).Save(new WebSettings { WebUrl = "https://bo.example", Token = Token });
        var json = JsonNode.Parse(File.ReadAllText(SettingsPath))!.AsObject();
        json["published"] = new JsonObject { ["scouts"] = "k7m2xq", ["rara"] = 5 };
        File.WriteAllText(SettingsPath, json.ToJsonString());

        var settings = new WebAccount(new HttpClient(), SettingsPath).Settings;

        Assert.Equal("https://bo.example", settings.WebUrl);
        Assert.Equal(Token, settings.Token);
        Assert.Equal("k7m2xq", Assert.Single(settings.Published).Value);
    }

    [Fact]
    public void Un_token_de_otro_usuario_de_Windows_no_pierde_la_direccion_ni_los_codigos()
    {
        File.WriteAllText(SettingsPath, """{ "web_url": "https://bo.example", "token": "bm8gZXMgdW4gdG9rZW4=", "published": { "scouts": "k7m2xq" } }""");

        var settings = new WebAccount(new HttpClient(), SettingsPath).Settings;

        Assert.False(settings.HasToken);
        Assert.Equal("https://bo.example", settings.WebUrl);
        Assert.Equal("k7m2xq", settings.Published["scouts"]);
    }

    [Fact]
    public void Un_archivo_de_la_cuenta_que_no_se_puede_leer_no_corta_el_arranque()
    {
        File.WriteAllText(SettingsPath, "{}");

        using var locked = new FileStream(SettingsPath, FileMode.Open, FileAccess.Read, FileShare.None);

        Assert.False(new WebAccount(new HttpClient(), SettingsPath).Settings.HasToken);
    }

    [Fact]
    public void Guardar_la_cuenta_escribe_por_un_temporal_y_si_falla_queda_lo_anterior()
    {
        var account = new WebAccount(new HttpClient(), SettingsPath);
        account.Save(new WebSettings { WebUrl = "https://bo.example", Token = Token });
        Directory.CreateDirectory(SettingsPath + ".tmp"); // no se puede escribir el temporal

        Assert.ThrowsAny<Exception>(() => account.Save(new WebSettings { WebUrl = "https://otra.example", Token = Token }));

        Assert.Equal("https://bo.example", new WebAccount(new HttpClient(), SettingsPath).Settings.WebUrl);
    }

    [Fact]
    public void Una_direccion_que_no_sirve_vuelve_a_la_de_fabrica()
    {
        File.WriteAllText(SettingsPath, """{ "web_url": "esto no es una dirección" }""");

        var settings = new WebAccount(new HttpClient(), SettingsPath).Settings;

        Assert.Equal(WebSettings.DefaultUrl, settings.WebUrl);
        Assert.Equal(new Uri(WebSettings.DefaultUrl + "/"), settings.BaseUri);
    }

    [Fact]
    public void Lo_que_guarda_otra_instancia_no_se_pisa()
    {
        var app = new WebAccount(new HttpClient(), SettingsPath);
        app.Save(new WebSettings { WebUrl = "https://bo.example", Token = Token });
        _ = app.Settings;

        new WebAccount(new HttpClient(), SettingsPath).RememberPublished("bog-aztecs", "k7m2xq");
        app.RememberPublished("scouts", "p3n4rd");

        var saved = new WebAccount(new HttpClient(), SettingsPath).Settings.Published;
        Assert.Equal("k7m2xq", saved["bog-aztecs"]);
        Assert.Equal("p3n4rd", saved["scouts"]);
        Assert.Equal("k7m2xq", app.Settings.Published["bog-aztecs"]);
    }

    [Fact]
    public async Task Publicar_una_carpeta_publica_cada_BO_y_la_segunda_vez_las_reemplaza()
    {
        var folder = Directory.CreateDirectory(Path.Combine(_dir.FullName, "civs")).FullName;
        var scouts = BuildOrderSerializer.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "build_orders", "scouts_22p.json")));
        File.WriteAllText(Path.Combine(folder, "a.json"), BuildOrderSerializer.Serialize(scouts with { Id = "bog-a", Name = "A" }));
        File.WriteAllText(Path.Combine(folder, "b.json"), BuildOrderSerializer.Serialize(scouts with { Id = "bog-b", Name = "B" }));
        File.WriteAllText(Path.Combine(folder, "rota.json"), BuildOrderSerializer.Serialize(scouts with { Id = "bog-rota", Name = "Rota", Steps = [] }));
        var web = new FakeWeb();
        var codes = new Queue<string>(["aaaaaa", "bbbbbb"]);
        web.Respond = r => r.Method == HttpMethod.Post
            ? (HttpStatusCode.Created, $$"""{ "code": "{{codes.Peek()}}", "url": "https://bo.example/bo/{{codes.Dequeue()}}" }""")
            : (HttpStatusCode.OK, $$"""{ "code": "{{r.RequestUri!.Segments[^1]}}", "url": "https://bo.example/bo/{{r.RequestUri!.Segments[^1]}}" }""");
        var account = new WebAccount(new HttpClient(web), SettingsPath);
        account.Save(new WebSettings { WebUrl = "https://bo.example", Token = Token });

        var first = new StringWriter();
        Assert.Equal(1, await BulkPublish.RunAsync(_data, account, folder, dryRun: false, first));
        var second = new StringWriter();
        Assert.Equal(1, await BulkPublish.RunAsync(_data, new WebAccount(new HttpClient(web), SettingsPath), folder, dryRun: false, second));

        Assert.Equal([(HttpMethod.Post, "/api/bo"), (HttpMethod.Post, "/api/bo"), (HttpMethod.Put, "/api/bo/aaaaaa"), (HttpMethod.Put, "/api/bo/bbbbbb")],
            web.Requests.Where(r => r.Method != HttpMethod.Get).Select(r => (r.Method, r.Path)));
        Assert.Equal("aaaaaa", account.Settings.Published["bog-a"]);
        Assert.Contains("✗ rota.json: tiene errores", first.ToString());
        Assert.Contains("2 nuevas, 0 reemplazadas, 1 con problemas.", first.ToString());
        Assert.Contains("0 nuevas, 2 reemplazadas, 1 con problemas.", second.ToString());
    }

    // Después de reinstalar Windows se pierde web.json con los códigos: sin esto, volver a publicar duplicaba las 124.
    [Fact]
    public async Task Publicar_sin_los_codigos_guardados_reconoce_las_propias_por_civ_y_nombre()
    {
        var folder = Directory.CreateDirectory(Path.Combine(_dir.FullName, "civs")).FullName;
        var scouts = BuildOrderSerializer.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "build_orders", "scouts_22p.json")));
        File.WriteAllText(Path.Combine(folder, "a.json"), BuildOrderSerializer.Serialize(scouts with { Id = "bog-a", Name = "A" }));
        File.WriteAllText(Path.Combine(folder, "b.json"), BuildOrderSerializer.Serialize(scouts with { Id = "bog-b", Name = "B" }));
        File.WriteAllText(Path.Combine(folder, "c.json"), BuildOrderSerializer.Serialize(scouts with { Id = "bog-c", Name = "C" }));
        static string Item(string code, string name, string civ, bool own) =>
            $$"""{ "code": "{{code}}", "name": "{{name}}", "civ": "{{civ}}", "tags": [], "author": "Yo", "guide_author": "Yo", "own": {{(own ? "true" : "false")}}, "visibility": "public", "updated_at": "2026-11-01T00:00:00Z" }""";
        var list = $$"""
            { "account": "Yo", "items": [
              {{Item("aaaaaa", "A", scouts.Civ, own: true)}},
              {{Item("zzzzzz", "A", "otra-civ", own: true)}},
              {{Item("bbbbbc", "B", scouts.Civ, own: true)}},
              {{Item("bbbbbd", "B", scouts.Civ, own: true)}},
              {{Item("cccccc", "C", scouts.Civ, own: false)}}
            ] }
            """;
        var web = new FakeWeb();
        web.Respond = r => r.Method == HttpMethod.Get
            ? (HttpStatusCode.OK, list)
            : r.Method == HttpMethod.Post
                ? (HttpStatusCode.Created, """{ "code": "nuevac", "url": "https://bo.example/bo/nuevac" }""")
                : (HttpStatusCode.OK, $$"""{ "code": "{{r.RequestUri!.Segments[^1]}}", "url": "https://bo.example/bo/{{r.RequestUri!.Segments[^1]}}" }""");
        var account = new WebAccount(new HttpClient(web), SettingsPath);
        account.Save(new WebSettings { WebUrl = "https://bo.example", Token = Token });

        var dry = new StringWriter();
        await BulkPublish.RunAsync(_data, account, folder, dryRun: true, dry);
        Assert.Contains("a.json: \"A\" reemplazaría aaaaaa", dry.ToString());
        Assert.Contains("✗ b.json: hay 2 publicadas tuyas", dry.ToString());
        // Una suscripta de otro no es tuya: se publica nueva.
        Assert.Contains("c.json: \"C\" se publicaría nueva", dry.ToString());
        Assert.DoesNotContain(web.Requests, r => r.Method != HttpMethod.Get);

        var output = new StringWriter();
        Assert.Equal(1, await BulkPublish.RunAsync(_data, account, folder, dryRun: false, output));

        Assert.Equal([(HttpMethod.Put, "/api/bo/aaaaaa"), (HttpMethod.Post, "/api/bo")],
            web.Requests.Where(r => r.Method != HttpMethod.Get).Select(r => (r.Method, r.Path)));
        Assert.Equal("aaaaaa", account.Settings.Published["bog-a"]);
        Assert.Contains("1 nuevas, 1 reemplazadas, 1 con problemas.", output.ToString());
    }

    [Fact]
    public async Task Publicar_una_carpeta_sin_cuenta_no_manda_nada()
    {
        var web = new FakeWeb();
        var output = new StringWriter();

        var result = await BulkPublish.RunAsync(_data, new WebAccount(new HttpClient(web), SettingsPath),
            Path.Combine(AppContext.BaseDirectory, "build_orders"), dryRun: false, output);

        Assert.Equal(2, result);
        Assert.Empty(web.Requests);
        // Manda a donde se pega el token, con los nombres que tiene en la app: «Cuenta de la web…» ya no existía.
        Assert.Contains($"{Texts.SettingsTitle} > {Texts.TabAdvanced} > \"{Texts.WebAccountButton}\"", output.ToString());
    }

    // AP-31: HttpClient borra Authorization al redirigir a otro host, pero no X-BoCoach-Secret. El cliente de la web no sigue
    // redirecciones, y una redirección es un error que se entiende, no una excepción sin manejar.
    [Fact]
    public async Task El_cliente_de_la_web_no_sigue_redirecciones_y_lo_dice()
    {
        var other = new TcpListener(IPAddress.Loopback, 0);
        var web = new TcpListener(IPAddress.Loopback, 0);
        other.Start();
        web.Start();
        try
        {
            var otherPort = ((IPEndPoint)other.LocalEndpoint).Port;
            var server = Task.Run(async () =>
            {
                using var connection = await web.AcceptTcpClientAsync();
                var stream = connection.GetStream();
                Assert.True(await stream.ReadAsync(new byte[8192]) > 0);
                await stream.WriteAsync(Encoding.ASCII.GetBytes(
                    $"HTTP/1.1 308 Permanent Redirect\r\nLocation: http://127.0.0.1:{otherPort}/robar\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"));
            });
            using var http = WebAccount.NewHttpClient();
            var client = new WebLibraryClient(http, new Uri($"http://127.0.0.1:{((IPEndPoint)web.LocalEndpoint).Port}/"));

            var error = await Assert.ThrowsAsync<WebLibraryException>(() =>
                client.ForgetRecordsAsync(new Core.Record.InstallIdentity(new string('a', 32), new string('b', 64))));

            await server;
            Assert.Equal(HttpStatusCode.PermanentRedirect, error.Status);
            Assert.Contains("redirección", error.Message);
            Assert.False(other.Pending());
        }
        finally
        {
            other.Stop();
            web.Stop();
        }
    }

    [Fact]
    public void Un_enlace_con_comillas_no_cuela_otros_argumentos()
    {
        // Lo que llega si el enlace es bocoach://import/k7m2xq" --stats "C:\Users\x\archivo
        string[] args = ["--import", "bocoach://import/k7m2xq", "--stats", @"C:\Users\x\archivo"];

        Assert.Equal(["--import", "bocoach://import/k7m2xq"], LinkProtocol.StartupArguments(args));
        Assert.Equal(["--replay", "carpeta", "--stats", "uso.csv"], LinkProtocol.StartupArguments(["--replay", "carpeta", "--stats", "uso.csv"]));
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

    // AP-06: con una versión nueva en otra carpeta, los enlaces seguían abriendo la vieja.
    [Fact]
    public void Los_enlaces_registrados_pasan_a_esta_copia_y_los_que_no_estaban_no_se_registran()
    {
        var key = $@"Software\BoCoachTests-{Guid.NewGuid():N}\Classes";
        var links = new LinkProtocol(key);
        try
        {
            links.RefreshIfRegistered(@"D:\nueva\BoCoach.exe");
            Assert.False(links.IsRegisteredTo(@"D:\nueva\BoCoach.exe"));
            using (var none = Registry.CurrentUser.OpenSubKey($@"{key}\bocoach"))
                Assert.Null(none);

            links.Register(@"C:\vieja\BoCoach.exe");
            links.RefreshIfRegistered(@"D:\nueva\BoCoach.exe");

            Assert.True(links.IsRegisteredTo(@"D:\nueva\BoCoach.exe"));
            Assert.False(links.IsRegisteredTo(@"C:\vieja\BoCoach.exe"));
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
        Assert.Equal("bocoach://import/k7m2xq", await received.Task.WaitAsync(TimeSpan.FromSeconds(15)));
    }

    [Fact]
    public async Task Si_otro_proceso_tiene_el_pipe_el_escucha_reintenta_hasta_tomarlo()
    {
        var name = $"BoCoachTest-{Guid.NewGuid():N}";
        // Otro proceso con el mismo nombre de pipe (una sola instancia): abrir el nuestro falla hasta que lo suelte.
        var squatter = new System.IO.Pipes.NamedPipeServerStream(SingleInstance.PipeNameFor(name), System.IO.Pipes.PipeDirection.In, 1);
        var previous = SingleInstance.RetryDelay;
        SingleInstance.RetryDelay = TimeSpan.FromMilliseconds(100);
        try
        {
            using var first = SingleInstance.Claim(null, name);
            var received = new TaskCompletionSource<string>();
            first!.Listen(link => received.TrySetResult(link));
            await Task.Delay(400);
            Assert.False(received.Task.IsCompleted);

            squatter.Dispose();
            Assert.Null(SingleInstance.Claim("bocoach://import/k7m2xq", name));

            Assert.Equal("bocoach://import/k7m2xq", await received.Task.WaitAsync(TimeSpan.FromSeconds(15)));
        }
        finally
        {
            SingleInstance.RetryDelay = previous;
            squatter.Dispose();
        }
    }
}
