using System.Windows.Input;
using BoCoach.App.Coaching;
using BoCoach.App.Hotkeys;

namespace BoCoach.App.Tests;

/// <summary>Atajos editables (spec 2026-10-30, app para el público, parte 1).</summary>
public sealed class HotkeysTests
{
    [Theory]
    [InlineData("Ctrl+Alt+Right", "Ctrl+Alt+Right", "Ctrl+Alt+→")]
    [InlineData("alt+ctrl+s", "Ctrl+Alt+S", "Ctrl+Alt+S")]
    [InlineData("Win+Shift+F5", "Shift+Win+F5", "Shift+Win+F5")]
    public void Lee_y_escribe_la_combinacion(string text, string storage, string display)
    {
        var binding = HotkeyBinding.Parse(text)!.Value;
        Assert.Equal(storage, binding.Storage);
        Assert.Equal(display, binding.Display);
        Assert.Equal(binding, HotkeyBinding.Parse(binding.Storage));
    }

    [Theory]
    [InlineData("S")]            // sin modificador: le robaría la tecla al juego
    [InlineData("Shift+S")]      // Shift solo tampoco alcanza
    [InlineData("Ctrl+Alt")]     // sin tecla
    [InlineData("Ctrl+Alt+Nada")]
    [InlineData("")]
    [InlineData(null)]
    public void Rechaza_lo_que_no_sirve(string? text) => Assert.Null(HotkeyBinding.Parse(text));

    [Fact]
    public void La_tecla_sola_de_un_modificador_no_vale() =>
        Assert.False(new HotkeyBinding(ModifierKeys.Control, Key.LeftAlt).IsValid);

    [Fact]
    public void Sin_cambios_son_las_de_fabrica()
    {
        var map = new HotkeyMap();
        Assert.Equal("Ctrl+Alt+→", map.Text("advance"));
        Assert.Empty(map.Overrides);
        Assert.Equal(12, HotkeyActions.All.Count);
        Assert.Equal(["calibrate", "record", "debug", "diagnostic"], HotkeyActions.All.Where(a => a.Developer).Select(a => a.Id));
    }

    [Fact]
    public void Lo_cambiado_se_guarda_y_restaurar_vuelve_a_fabrica()
    {
        var map = new HotkeyMap();
        var changes = 0;
        map.Changed += () => changes++;
        map.Set("advance", HotkeyBinding.Parse("Ctrl+Alt+F1")!.Value);
        Assert.Equal(new Dictionary<string, string> { ["advance"] = "Ctrl+Alt+F1" }, map.Overrides);
        map.Reset("advance");
        Assert.Empty(map.Overrides);
        Assert.Equal(2, changes);
    }

    [Fact]
    public void Avisa_quien_ya_usa_una_combinacion()
    {
        var map = new HotkeyMap();
        var pause = HotkeyBinding.Parse("Ctrl+Alt+P")!.Value;
        Assert.Equal("pause", map.UsedBy(pause, exceptId: "advance")?.Id);
        Assert.Null(map.UsedBy(pause, exceptId: "pause")); // la propia no es choque
    }

    [Fact]
    public void Lo_que_no_sirve_en_settings_queda_de_fabrica()
    {
        var map = new HotkeyMap(new Dictionary<string, string>
        {
            ["advance"] = "S",              // sin modificador
            ["nada"] = "Ctrl+Alt+F2",       // id desconocido
            ["back"] = "Ctrl+Alt+P",        // choca con la de fábrica de pausa
        });
        Assert.Equal("Ctrl+Alt+→", map.Text("advance"));
        Assert.Equal("Ctrl+Alt+←", map.Text("back"));
        Assert.Equal("Ctrl+Alt+P", map.Text("pause"));
    }

    // Editado a mano: dos acciones con la misma combinación. La segunda vuelve a fábrica; si su fábrica también está
    // tomada, queda sin atajo (nunca dos registros iguales).
    [Fact]
    public void Dos_iguales_en_settings_no_quedan_las_dos()
    {
        var map = new HotkeyMap(new Dictionary<string, string> { ["advance"] = "Ctrl+Alt+F3", ["back"] = "Ctrl+Alt+F3" });
        Assert.Equal("Ctrl+Alt+F3", map.Text("advance"));
        Assert.Equal("Ctrl+Alt+←", map.Text("back"));

        // back repite la de advance y su fábrica (←) se la quedó pausa: queda sin atajo.
        var taken = new HotkeyMap(new Dictionary<string, string>
        {
            ["advance"] = "Ctrl+Alt+F3", ["back"] = "Ctrl+Alt+F3", ["pause"] = "Ctrl+Alt+Left",
        });
        Assert.Equal("Ctrl+Alt+←", taken.Text("pause"));
        Assert.Null(taken.Get("back"));
        Assert.Equal("sin atajo", taken.Text("back"));
        // Las dos cambiadas a la fábrica de la otra: vale, se intercambian.
        var swapped = new HotkeyMap(new Dictionary<string, string> { ["advance"] = "Ctrl+Alt+Left", ["back"] = "Ctrl+Alt+Right" });
        Assert.Equal("Ctrl+Alt+←", swapped.Text("advance"));
        Assert.Equal("Ctrl+Alt+→", swapped.Text("back"));
    }

    [Fact]
    public void Settings_guarda_y_lee_los_atajos()
    {
        var path = Path.Combine(Path.GetTempPath(), $"bocoach-{Guid.NewGuid():N}.json");
        new CoachSettings { Hotkeys = new() { ["advance"] = "Ctrl+Alt+F1" } }.Save(path);
        Assert.Equal("Ctrl+Alt+F1", CoachSettings.Load(path).Hotkeys["advance"]);
        File.WriteAllText(path, """{ "hotkeys": [1, 2] }""");
        Assert.Empty(CoachSettings.Load(path).Hotkeys);
        File.WriteAllText(path, """{ "hotkeys": { "advance": 5, "back": "Ctrl+Alt+F2" } }""");
        Assert.Equal(new Dictionary<string, string> { ["back"] = "Ctrl+Alt+F2" }, CoachSettings.Load(path).Hotkeys);
        File.Delete(path);
    }

    private static Dictionary<string, Action> Actions() => HotkeyActions.All.ToDictionary(a => a.Id, _ => (Action)(() => { }));

    /// <summary>Hace de Windows: las combinaciones de <c>taken</c> las tiene otro programa.</summary>
    private sealed class FakeRegistrar(params string[] taken) : IHotkeyRegistrar
    {
        private int _next = 1;
        public Dictionary<int, string> Registered { get; } = [];

        public int? Register(HotkeyBinding binding, Action action)
        {
            if (taken.Contains(binding.Storage) || Registered.ContainsValue(binding.Storage))
                return null;
            Registered[_next] = binding.Storage;
            return _next++;
        }

        public void Unregister(int id) => Registered.Remove(id);
    }

    [Fact]
    public void Registra_todas_y_dice_cuales_tiene_otro_programa()
    {
        var registrar = new FakeRegistrar("Ctrl+Alt+Right");
        var binder = new HotkeyBinder(registrar, new HotkeyMap(), Actions(), includeDeveloper: true);
        Assert.Equal(["advance"], binder.RegisterAll());
        Assert.Equal(11, registrar.Registered.Count);
    }

    [Fact]
    public void Sin_desarrollador_no_registra_las_de_desarrollador()
    {
        var registrar = new FakeRegistrar();
        new HotkeyBinder(registrar, new HotkeyMap(), Actions(), includeDeveloper: false).RegisterAll();
        Assert.Equal(8, registrar.Registered.Count);
    }

    [Fact]
    public void Si_Windows_no_deja_la_nueva_queda_la_vieja()
    {
        var registrar = new FakeRegistrar("Ctrl+Alt+F1");
        var map = new HotkeyMap();
        var binder = new HotkeyBinder(registrar, map, Actions(), includeDeveloper: true);
        binder.RegisterAll();
        Assert.Equal(ChangeResult.Rejected, binder.Change("advance", HotkeyBinding.Parse("Ctrl+Alt+F1")));
        Assert.Contains("Ctrl+Alt+Right", registrar.Registered.Values);
        Assert.Equal("Ctrl+Alt+→", map.Text("advance"));
    }

    [Fact]
    public void No_deja_repetir_y_restaurar_vuelve_a_fabrica()
    {
        var registrar = new FakeRegistrar();
        var map = new HotkeyMap();
        var binder = new HotkeyBinder(registrar, map, Actions(), includeDeveloper: true);
        binder.RegisterAll();
        Assert.Equal(ChangeResult.UsedByOther, binder.Change("advance", HotkeyBinding.Parse("Ctrl+Alt+P")));
        Assert.Equal("pause", binder.LastConflict?.Id);
        Assert.Equal(ChangeResult.Done, binder.Change("advance", HotkeyBinding.Parse("Ctrl+Alt+F1")));
        Assert.Contains("Ctrl+Alt+F1", registrar.Registered.Values);
        Assert.DoesNotContain("Ctrl+Alt+Right", registrar.Registered.Values);
        Assert.Equal(ChangeResult.Done, binder.Change("advance", null));
        Assert.Equal("Ctrl+Alt+→", map.Text("advance"));
        Assert.Equal(12, registrar.Registered.Count);
    }

    [Fact]
    public void Cambiar_a_la_misma_no_es_choque()
    {
        var registrar = new FakeRegistrar();
        var binder = new HotkeyBinder(registrar, new HotkeyMap(), Actions(), includeDeveloper: true);
        binder.RegisterAll();
        Assert.Equal(ChangeResult.Done, binder.Change("advance", HotkeyBinding.Parse("Ctrl+Alt+Right")));
        Assert.Equal(12, registrar.Registered.Count);
    }

    [Fact]
    public void Restaurar_todos_cuando_dos_se_intercambiaron()
    {
        var registrar = new FakeRegistrar();
        var map = new HotkeyMap(new Dictionary<string, string> { ["advance"] = "Ctrl+Alt+Left", ["back"] = "Ctrl+Alt+Right" });
        var binder = new HotkeyBinder(registrar, map, Actions(), includeDeveloper: true);
        binder.RegisterAll();
        binder.ResetAll();
        Assert.Equal("Ctrl+Alt+→", map.Text("advance"));
        Assert.Equal("Ctrl+Alt+←", map.Text("back"));
        Assert.Equal(12, registrar.Registered.Count);
        Assert.Empty(map.Overrides);
    }
}
