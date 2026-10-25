using System.Text.Json.Nodes;
using BoCoach.App.Capture;
using BoCoach.Vision.Hud;

namespace BoCoach.App.Tests;

/// <summary>
/// El mod "BoCoach HUD" se arma desde los paneles originales del juego y el diseño (data/hud_layout.json): cada widget
/// del juego se conserva con su nombre y su tooltip y se reubica según el diseño.
/// </summary>
public sealed class HudModTests : IDisposable
{
    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("bocoach-mod-");

    public void Dispose() => _dir.Delete(recursive: true);

    private string Profiles => Path.Combine(_dir.FullName, "perfiles");

    public static HudLayout Layout() => HudLayout.Load(Path.Combine(AppContext.BaseDirectory, "data", HudLayout.FileName));

    private static JsonObject View(int x, int y, int w, int h) => new() { ["xorigin"] = x, ["yorigin"] = y, ["width"] = w, ["height"] = h, ["alignment"] = "CentreCentre" };

    private static (int X, int Y, int W, int H) View(JsonObject widget)
    {
        var v = widget["ViewPort"]!;
        return ((int)v["xorigin"]!, (int)v["yorigin"]!, (int)v["width"]!, (int)v["height"]!);
    }

    private static JsonObject Wrap(JsonObject widget) => new() { ["Widget"] = widget };

    private static JsonObject Label(string name, int x, int y, int w, int h, int? help = null)
    {
        var label = new JsonObject
        {
            ["Type"] = "Label",
            ["Name"] = name,
            ["ViewPort"] = View(x, y, w, h),
            ["StateMaterials"] = new JsonObject { ["StateNormal"] = new JsonObject { ["Font"] = new JsonObject { ["FontIndex"] = 0, ["PointSize"] = h } } },
        };
        if (help is { } h2)
            label["Help"] = h2;
        return label;
    }

    private static JsonObject Icon(string name, int x, int? help = null)
    {
        var icon = new JsonObject
        {
            ["Type"] = "Base",
            ["Name"] = name,
            ["ViewPort"] = View(x, 57, 84, 84),
            ["StateMaterials"] = new JsonObject { ["StateNormal"] = new JsonObject { ["Material"] = $"Resource{name}" } },
            ["ChildWidgets"] = new JsonArray(Wrap(Label("Workers", 16, 55, 60, 32))),
        };
        if (help is { } h)
            icon["Help"] = h;
        return icon;
    }

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
            children.Add(Wrap(Icon(resource, x, resource == "Wood" ? 42011 : null)));
            children.Add(Wrap(Label($"{resource}Storage", x + 51, 60, 100, 34, resource == "Wood" ? 42202 : null)));
            x += 200;
        }
        var population = Icon("Population", 856);
        population["StateMaterials"] = new JsonObject { ["UserStates"] = 2, ["UserState0"] = new JsonObject { ["Material"] = "Population" }, ["UserState1"] = new JsonObject { ["Material"] = "None" } };
        children.Add(Wrap(population));
        children.Add(Wrap(Label("PopulationCount", 910, 60, 130, 34)));
        children.Add(Wrap(new JsonObject { ["Type"] = "Base", ["Name"] = "PopulationFlash", ["ViewPort"] = View(900, 60, 140, 72) }));
        children.Add(Wrap(new JsonObject { ["Type"] = "Button", ["Name"] = "Idle", ["ViewPort"] = View(1080, 57, 70, 70), ["StateMaterials"] = new JsonObject { ["StateNormal"] = new JsonObject { ["Material"] = "IdleVillagerNormal" } } }));
        var idleWorkers = Label("IdleWorkers", 1060, 70, 40, 30);
        idleWorkers["ZPlane"] = 10;
        children.Add(Wrap(idleWorkers));
        children.Add(Wrap(new JsonObject { ["Type"] = "Button", ["Name"] = "AgeUp", ["ViewPort"] = View(1200, 62, 141, 122) }));
        children.Add(Wrap(new JsonObject
        {
            ["Type"] = "ProgressBar",
            ["Name"] = "AgeBar",
            ["ViewPort"] = View(1250, 57, 378, 52),
            ["ChildWidgets"] = new JsonArray(Wrap(Label("AgeTextLabel", 0, 0, 380, 50))),
        }));
        var background = new JsonObject
        {
            ["Type"] = "Base",
            ["Name"] = "Background",
            ["ViewPort"] = new JsonObject { ["xorigin"] = 0, ["yorigin"] = 0, ["width"] = 1750, ["height"] = 276, ["alignment"] = "TopLeft" },
            ["ZPlane"] = 1,
            ["StateMaterials"] = new JsonObject { ["UserStates"] = 9, ["UserState0"] = new JsonObject { ["Material"] = "CivAfriResourcePanel" } },
            ["ChildWidgets"] = children,
        };
        return new JsonObject
        {
            ["Collection"] = new JsonObject
            {
                ["Name"] = "ResourcePanel",
                ["ViewPort"] = new JsonObject { ["xorigin"] = 0, ["yorigin"] = 0, ["width"] = 1935, ["height"] = 228, ["alignment"] = "TopLeft" },
                ["Widgets"] = new JsonArray(Wrap(background)),
            },
        };
    }

    public static JsonObject ClockPanel() => new()
    {
        ["Collection"] = new JsonObject
        {
            ["Name"] = "WorldTimerPanel",
            ["ViewPort"] = new JsonObject { ["xorigin"] = 0, ["yorigin"] = 0, ["width"] = 3840, ["height"] = 2160, ["alignment"] = "TopLeft" },
            ["Widgets"] = new JsonArray(Wrap(new JsonObject
            {
                ["Type"] = "Anchor",
                ["Name"] = "WorldTimer",
                ["ChildWidgets"] = new JsonArray(
                    Wrap(Label("WorldTimerLabel", 0, 12, 1000, 40)),
                    Wrap(Label("GameIDLabel", 0, 64, 512, 49))),
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
        [widget, .. Children(widget).SelectMany(All)];

    private static List<JsonObject> Children(JsonObject widget) =>
        (widget["ChildWidgets"] as JsonArray ?? []).OfType<JsonObject>().Select(c => c["Widget"]!.AsObject()).ToList();

    private static JsonObject Widget(JsonObject panel, string name) =>
        panel["Collection"]!["Widgets"]!.AsArray().SelectMany(w => All(w!["Widget"]!.AsObject())).First(w => (string?)w["Name"] == name);

    private static string? Material(JsonObject widget, string state = "StateNormal") => (string?)widget["StateMaterials"]?[state]?["Material"];

    private static int Points(JsonObject label) => (int)label["StateMaterials"]!["StateNormal"]!["Font"]!["PointSize"]!;

    [Fact]
    public void Arma_cada_celda_desde_el_original_segun_el_diseno()
    {
        var panel = HudMod.ResourcePanel(ResourcePanel(), Layout());

        var wood = Widget(panel, "Wood");
        Assert.Equal((10, 0, 410, 108), View(wood));
        Assert.Equal("TopLeft", (string?)wood["ViewPort"]!["alignment"]);
        Assert.Equal("None", Material(wood));
        Assert.Equal(42011, (int)wood["Help"]!);
        var children = Children(wood);
        Assert.Equal(["BoCoachIcon", "BoCoachChip", "BoCoachBack0", "BoCoachSeparator", "Workers"], children.Select(c => (string?)c["Name"]));
        Assert.Equal("ResourceWood", Material(children[0]));
        Assert.Equal((10, 12, 84, 84), View(children[0]));
        Assert.Equal((104, 12, 70, 84), View(children[1]));
        Assert.Equal(0.53, (double)children[1]["StateMaterials"]!["StateNormal"]!["Color"]!["r"]!, 2);
        Assert.Equal((180, 12, 230, 84), View(children[2]));
        Assert.Equal("/", (string?)children[3]["Text"]);
        Assert.Equal(44, Points(children[3]));
        Assert.Equal((184, 12, 70, 84), View(children[4]));
        Assert.Equal(48, Points(children[4]));
        Assert.Equal("CentreCentre", (string?)children[4]["TextAnchor"]);

        var storage = Widget(panel, "WoodStorage");
        Assert.Equal((284, 12, 132, 84), View(storage));
        Assert.Equal(48, Points(storage));
        Assert.Equal(42202, (int)storage["Help"]!);
        Assert.Equal("CentreCentre", (string?)storage["TextAnchor"]);

        var population = Widget(panel, "Population");
        Assert.Equal("None", Material(population, "UserState0"));
        Assert.Equal("Population", Material(Children(population)[0]));
        Assert.Equal((104, 12, 80, 84), View(Children(population).First(c => (string?)c["Name"] == "Workers")));
        Assert.Equal((1840, 12, 180, 84), View(Widget(panel, "PopulationCount")));
        Assert.Equal((1840, 12, 180, 84), View(Widget(panel, "PopulationFlash")));

        Assert.Equal((2040, 12, 84, 84), View(Widget(panel, "Idle")));
        Assert.Equal("IdleVillagerNormal", Material(Widget(panel, "Idle")));
        Assert.Equal((2130, 12, 80, 84), View(Widget(panel, "BoCoachBack_idle_0")));
        Assert.Equal((2130, 12, 80, 84), View(Widget(panel, "IdleWorkers")));
        Assert.Equal(10, (int)Widget(panel, "IdleWorkers")["ZPlane"]!);

        Assert.Equal((2230, 12, 84, 84), View(Widget(panel, "AgeUp")));
        Assert.Equal((2324, 28, 378, 52), View(Widget(panel, "AgeBar")));
        var ageText = Widget(panel, "AgeTextLabel");
        Assert.Equal((0, 0, 378, 52), View(ageText));
        Assert.Equal(44, Points(ageText));
    }

    /// <summary>
    /// El fondo del juego (la textura de la civ) se apaga en sus nueve estados y la barra lisa es un hijo propio con
    /// material normal: en el juego, un color liso puesto en los estados por civ no se dibujó y se vio la textura
    /// (20261024-224921). Los colores lisos con StateNormal (casilleros, bordes) sí se dibujan.
    /// </summary>
    [Fact]
    public void El_fondo_del_juego_se_apaga_y_la_barra_lisa_es_el_primer_hijo()
    {
        var panel = HudMod.ResourcePanel(ResourcePanel(), Layout());

        var collection = panel["Collection"]!["ViewPort"]!;
        Assert.Equal((2720, 108), ((int)collection["width"]!, (int)collection["height"]!));
        var background = Widget(panel, "Background");
        Assert.Equal((0, 0, 2720, 108), View(background));
        Assert.Equal(9, (int)background["StateMaterials"]!["UserStates"]!);
        foreach (var state in new[] { "UserState0", "UserState8" })
            Assert.Equal("None", Material(background, state));
        var bar = Children(background)[0];
        Assert.Equal("BoCoachBar", (string?)bar["Name"]);
        Assert.Equal((0, 0, 2720, 108), View(bar));
        Assert.Equal("FlatColorWithAlpha", Material(bar));
        Assert.Equal(0.1, (double)bar["StateMaterials"]!["StateNormal"]!["Color"]!["r"]!, 2);
        Assert.Null(bar["ZPlaneLocalOffset"]);
        Assert.Equal((0, 0, 2720, 2), View(Widget(panel, "BoCoachBorderTop")));
        Assert.Equal((0, 106, 2720, 2), View(Widget(panel, "BoCoachBorderBottom")));
        Assert.Equal((0, 0, 2, 108), View(Widget(panel, "BoCoachBorderLeft")));
        Assert.Equal((2718, 0, 2, 108), View(Widget(panel, "BoCoachBorderRight")));
    }

    [Fact]
    public void Un_panel_ya_modificado_vuelve_a_salir_igual()
    {
        var layout = Layout();
        var once = HudMod.ResourcePanel(ResourcePanel(), layout);

        var twice = HudMod.ResourcePanel(once.DeepClone().AsObject(), layout);

        Assert.True(JsonNode.DeepEquals(once, twice));
    }

    [Fact]
    public void El_reloj_toma_el_diseno_y_la_coleccion_se_corre_al_final_de_la_barra()
    {
        var panel = HudMod.ClockPanel(ClockPanel(), Layout());

        Assert.Equal(2734, (int)panel["Collection"]!["ViewPort"]!["xorigin"]!);
        Assert.Equal("BoCoachClockBackground", (string?)Children(Widget(panel, "WorldTimer"))[0]["Name"]);
        Assert.Equal((0, 12, 230, 84), View(Widget(panel, "BoCoachClockBackground")));
        var label = Widget(panel, "WorldTimerLabel");
        Assert.Equal(56, Points(label));
        Assert.Equal((8, 12 + (84 - 56) / 2, 1000, 56), View(label));
        Assert.Equal(12 + 84 + 12, (int)Widget(panel, "GameIDLabel")["ViewPort"]!["yorigin"]!);
    }

    [Fact]
    public void Instala_en_cada_perfil_con_mods_aunque_no_tenga_lista()
    {
        var game = FakeGame(_dir.FullName);
        Directory.CreateDirectory(Path.Combine(Profiles, "111", "mods"));
        Directory.CreateDirectory(Path.Combine(Profiles, "logs"));

        var result = HudMod.Install(game, Layout(), Profiles);

        Assert.Equal(["Perfil 111: diseño bocoach-2026-10."], result.Installed);
        Assert.Empty(result.Warnings);
        var mod = Path.Combine(Profiles, "111", "mods", "local", HudMod.Name);
        Assert.True(File.Exists(Path.Combine(mod, "widgetui", "resourcepanel.json")));
        Assert.True(File.Exists(Path.Combine(mod, "widgetui", "worldtimerpanel.json")));
        Assert.Contains("bocoach-2026-10", File.ReadAllText(Path.Combine(mod, "info.json")));
    }

    [Fact]
    public void Instalar_dos_veces_deja_los_mismos_archivos()
    {
        var game = FakeGame(_dir.FullName);
        Directory.CreateDirectory(Path.Combine(Profiles, "111", "mods"));
        var mod = Path.Combine(Profiles, "111", "mods", "local", HudMod.Name, "widgetui");

        HudMod.Install(game, Layout(), Profiles);
        var first = (File.ReadAllBytes(Path.Combine(mod, "resourcepanel.json")), File.ReadAllBytes(Path.Combine(mod, "worldtimerpanel.json")));
        HudMod.Install(game, Layout(), Profiles);

        Assert.Equal(first.Item1, File.ReadAllBytes(Path.Combine(mod, "resourcepanel.json")));
        Assert.Equal(first.Item2, File.ReadAllBytes(Path.Combine(mod, "worldtimerpanel.json")));
    }

    [Fact]
    public void Avisa_si_otro_mod_activo_le_gana()
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

        var result = HudMod.Install(game, Layout(), Profiles);

        Assert.Equal(["Perfil 111: diseño bocoach-2026-10."], result.Installed);
        var warning = Assert.Single(result.Warnings);
        Assert.Contains("\"Otro panel\" también cambia el panel de recursos", warning);
        Assert.Contains("(1 contra 2)", warning);
    }

    [Fact]
    public void Sin_perfiles_avisa_que_abra_el_juego()
    {
        var result = HudMod.Install(FakeGame(_dir.FullName), Layout(), Profiles);

        Assert.False(result.Ok);
        Assert.Contains("abrí el juego", Assert.Single(result.Warnings));
    }
}
