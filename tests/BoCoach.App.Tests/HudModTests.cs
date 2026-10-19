using System.Text.Json.Nodes;
using BoCoach.App.Capture;

namespace BoCoach.App.Tests;

public sealed class HudModTests : IDisposable
{
    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("bocoach-mod-");

    public void Dispose() => _dir.Delete(recursive: true);

    private string Profiles => Path.Combine(_dir.FullName, "perfiles");

    private static JsonObject View(int x, int y, int w, int h) => new() { ["xorigin"] = x, ["yorigin"] = y, ["width"] = w, ["height"] = h, ["alignment"] = "CentreCentre" };

    private static JsonObject Wrap(JsonObject widget) => new() { ["Widget"] = widget };

    private static JsonObject Label(string name, int x, int y, int w, int h) => new()
    {
        ["Type"] = "Label",
        ["Name"] = name,
        ["ViewPort"] = View(x, y, w, h),
        ["StateMaterials"] = new JsonObject { ["StateNormal"] = new JsonObject { ["Font"] = new JsonObject { ["FontIndex"] = 0, ["PointSize"] = h } } },
    };

    private static JsonObject Icon(string name, int x) => new()
    {
        ["Type"] = "Base",
        ["Name"] = name,
        ["ViewPort"] = View(x, 57, 84, 84),
        ["ChildWidgets"] = new JsonArray(Wrap(Label("Workers", 16, 55, 60, 32))),
    };

    /// <summary>
    /// Un panel de recursos con la forma del original del juego (mismas posiciones, sin texturas): el del juego no se
    /// copia al repo.
    /// </summary>
    public static JsonObject ResourcePanel()
    {
        var children = new JsonArray();
        var x = 57;
        foreach (var resource in new[] { "Wood", "Food", "Gold", "Stone" })
        {
            children.Add(Wrap(Icon(resource, x)));
            children.Add(Wrap(Label($"{resource}Storage", x + 51, 60, 100, 34)));
            x += 200;
        }
        children.Add(Wrap(Icon("Population", 856)));
        children.Add(Wrap(Label("PopulationCount", 910, 60, 130, 34)));
        children.Add(Wrap(new JsonObject { ["Type"] = "Button", ["Name"] = "Idle", ["ViewPort"] = View(1080, 57, 70, 70) }));
        children.Add(Wrap(Label("IdleWorkers", 1060, 70, 40, 30)));
        return new JsonObject
        {
            ["Collection"] = new JsonObject
            {
                ["Widgets"] = new JsonArray(Wrap(new JsonObject { ["Type"] = "Base", ["Name"] = "ResourcePanel", ["ChildWidgets"] = children })),
            },
        };
    }

    public static JsonObject ClockPanel() => new()
    {
        ["Collection"] = new JsonObject
        {
            ["Widgets"] = new JsonArray(Wrap(new JsonObject
            {
                ["Type"] = "Base",
                ["Name"] = "WorldTimer",
                ["ChildWidgets"] = new JsonArray(
                    Wrap(Label("WorldTimerLabel", 0, 10, 160, 40)),
                    Wrap(Label("GameIDLabel", 0, 60, 160, 20))),
            })),
        },
    };

    /// <summary>Una carpeta de juego con los dos paneles originales.</summary>
    public static string FakeGame(string root)
    {
        var game = Path.Combine(root, "AoE2DE");
        Directory.CreateDirectory(Path.Combine(game, "widgetui"));
        File.WriteAllText(Path.Combine(game, "widgetui", "resourcepanel.json"), ResourcePanel().ToJsonString());
        File.WriteAllText(Path.Combine(game, "widgetui", "worldtimerpanel.json"), ClockPanel().ToJsonString());
        return game;
    }

    private static IEnumerable<JsonObject> All(JsonObject widget) =>
        [widget, .. (widget["ChildWidgets"] as JsonArray ?? []).OfType<JsonObject>().Select(c => c["Widget"]!.AsObject()).SelectMany(All)];

    private static JsonObject Widget(JsonObject panel, string name) =>
        panel["Collection"]!["Widgets"]!.AsArray().SelectMany(w => All(w!["Widget"]!.AsObject())).First(w => (string?)w["Name"] == name);

    [Fact]
    public void Cada_recurso_es_un_cuadrado_negro_con_franja_y_los_aldeanos_grandes()
    {
        var panel = HudMod.ResourcePanel(ResourcePanel());

        var wood = Widget(panel, "Wood");
        Assert.Equal(0, (double)wood["StateMaterials"]!["StateNormal"]!["Color"]!["r"]!);
        var band = wood["ChildWidgets"]![0]!["Widget"]!;
        Assert.Equal("BoCoachBand", (string?)band["Name"]);
        Assert.Equal(36, (int)band["ViewPort"]!["height"]!); // 84 × 0,43
        var workers = All(wood).First(w => (string?)w["Name"] == "Workers");
        Assert.Equal(48, (int)workers["StateMaterials"]!["StateNormal"]!["Font"]!["PointSize"]!);
        Assert.Equal("CentreCentre", (string?)workers["TextAnchor"]);
        // Detrás del stock, un rectángulo negro que empieza después del cuadrado.
        var back = Widget(panel, "BoCoachBackWoodStorage");
        Assert.Equal(57 + 42 + 5, (int)back["ViewPort"]!["xorigin"]!);
        Assert.Equal(48, (int)Widget(panel, "WoodStorage")["StateMaterials"]!["StateNormal"]!["Font"]!["PointSize"]!);
        // Inactivos: el mismo cuadrado, con la franja roja.
        var idle = Widget(panel, "Idle");
        Assert.Equal(84, (int)idle["ViewPort"]!["width"]!);
        Assert.Equal(0.9, (double)idle["ChildWidgets"]![0]!["Widget"]!["StateMaterials"]!["StateNormal"]!["Color"]!["r"]!);
    }

    [Fact]
    public void Aplicarlo_dos_veces_da_lo_mismo()
    {
        // El mod parte de sus propios paneles si ya existe: no tiene que agregar otra franja ni otro fondo.
        var once = HudMod.ResourcePanel(ResourcePanel());
        var twice = HudMod.ResourcePanel(once.DeepClone().AsObject());
        Assert.True(JsonNode.DeepEquals(once, twice));

        var clock = HudMod.ClockPanel(ClockPanel());
        Assert.True(JsonNode.DeepEquals(clock, HudMod.ClockPanel(clock.DeepClone().AsObject())));
    }

    [Fact]
    public void El_reloj_crece_con_un_fondo_negro_y_el_id_de_partida_baja()
    {
        var panel = HudMod.ClockPanel(ClockPanel());

        Assert.Equal(56, (int)Widget(panel, "WorldTimerLabel")["StateMaterials"]!["StateNormal"]!["Font"]!["PointSize"]!);
        Assert.Equal("BoCoachClockBackground", (string?)Widget(panel, "WorldTimer")["ChildWidgets"]![0]!["Widget"]!["Name"]);
        Assert.Equal(10 + 56 + 12, (int)Widget(panel, "GameIDLabel")["ViewPort"]!["yorigin"]!);
    }

    [Fact]
    public void Instala_en_cada_perfil_con_mods_aunque_no_tenga_lista()
    {
        var game = FakeGame(_dir.FullName);
        Directory.CreateDirectory(Path.Combine(Profiles, "111", "mods"));
        Directory.CreateDirectory(Path.Combine(Profiles, "logs"));

        var result = HudMod.Install(game, Profiles);

        Assert.Equal(["Perfil 111: recursos de original, reloj de original."], result.Installed);
        Assert.Empty(result.Warnings);
        var mod = Path.Combine(Profiles, "111", "mods", "local", HudMod.Name);
        Assert.True(File.Exists(Path.Combine(mod, "widgetui", "resourcepanel.json")));
        Assert.True(File.Exists(Path.Combine(mod, "widgetui", "worldtimerpanel.json")));
        Assert.Contains("detrás", File.ReadAllText(Path.Combine(mod, "info.json")));
    }

    [Fact]
    public void Parte_del_panel_del_mod_activo_y_avisa_si_le_gana_al_nuestro()
    {
        var game = FakeGame(_dir.FullName);
        var mods = Path.Combine(Profiles, "111", "mods");
        var other = Path.Combine(mods, "subscribed", "17306_Otro panel", "widgetui");
        Directory.CreateDirectory(other);
        File.WriteAllText(Path.Combine(other, "resourcepanel.json"), ResourcePanel().ToJsonString());
        File.WriteAllText(Path.Combine(mods, "mod-status.json"), """
            { "Mods": [
              { "Enabled": true, "Path": "subscribed//17306_Otro panel", "Priority": 1, "Title": "Otro panel" },
              { "Enabled": true, "Path": "local//BoCoach HUD", "Priority": 2, "Title": "BoCoach HUD" }
            ] }
            """);

        var result = HudMod.Install(game, Profiles);

        Assert.Equal(["Perfil 111: recursos de 17306_Otro panel, reloj de original."], result.Installed);
        var warning = Assert.Single(result.Warnings);
        Assert.Contains("\"Otro panel\" también cambia el panel de recursos", warning);
        Assert.Contains("(1 contra 2)", warning);
    }

    [Fact]
    public void Sin_perfiles_avisa_que_abra_el_juego()
    {
        var result = HudMod.Install(FakeGame(_dir.FullName), Profiles);

        Assert.False(result.Ok);
        Assert.Contains("abrí el juego", Assert.Single(result.Warnings));
    }
}
