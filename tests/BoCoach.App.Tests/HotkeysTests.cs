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
        public List<string> Taken { get; } = [.. taken];
        public Dictionary<int, string> Registered { get; } = [];

        public int? Register(HotkeyBinding binding, Action action)
        {
            if (Taken.Contains(binding.Storage) || Registered.ContainsValue(binding.Storage))
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

    private static (HotkeysViewModel Vm, HotkeyMap Map) Window(params string[] taken)
    {
        var map = new HotkeyMap();
        var binder = new HotkeyBinder(new FakeRegistrar(taken), map, Actions(), includeDeveloper: true);
        binder.RegisterAll();
        return (new HotkeysViewModel(binder, map, includeDeveloper: true), map);
    }

    [Fact]
    public void La_ventana_cambia_y_avisa_choques()
    {
        var (vm, map) = Window();
        var advance = vm.Rows.Single(r => r.Id == "advance");
        advance.StartCaptureCommand.Execute(null);
        Assert.Equal("Apretá la nueva…", advance.Text);
        Assert.False(vm.Capture("advance", ModifierKeys.None, Key.F1));
        Assert.Equal("Tiene que llevar Ctrl, Alt o Win", advance.Message);
        Assert.False(vm.Capture("advance", ModifierKeys.Control, Key.LeftCtrl)); // solo el modificador: sigue esperando
        Assert.True(vm.Capture("advance", ModifierKeys.Control | ModifierKeys.Alt, Key.P));
        Assert.Equal("Ya la usa: Pausa", advance.Message);
        Assert.True(advance.IsError);
        Assert.Equal("Ctrl+Alt+→", advance.Text);
        advance.StartCaptureCommand.Execute(null);
        Assert.True(vm.Capture("advance", ModifierKeys.Control | ModifierKeys.Alt, Key.F1));
        Assert.Equal("Ctrl+Alt+F1", advance.Text);
        Assert.Null(advance.Message);
        Assert.Equal("Ctrl+Alt+F1", map.Overrides["advance"]);
        vm.ResetAllCommand.Execute(null);
        Assert.Equal("Ctrl+Alt+→", advance.Text);
    }

    [Fact]
    public void Escape_cancela()
    {
        var (vm, _) = Window();
        var advance = vm.Rows.Single(r => r.Id == "advance");
        advance.StartCaptureCommand.Execute(null);
        Assert.True(vm.Capture("advance", ModifierKeys.None, Key.Escape));
        Assert.Equal("Ctrl+Alt+→", advance.Text);
        Assert.Null(advance.Message);
        Assert.False(advance.Capturing);
    }

    [Fact]
    public void La_ventana_avisa_si_otro_programa_la_tiene_y_restaura_una()
    {
        var (vm, _) = Window("Ctrl+Alt+F1");
        var advance = vm.Rows.Single(r => r.Id == "advance");
        advance.StartCaptureCommand.Execute(null);
        Assert.True(vm.Capture("advance", ModifierKeys.Control | ModifierKeys.Alt, Key.F1));
        Assert.Equal("Otro programa ya usa Ctrl+Alt+F1", advance.Message);
        Assert.Equal("Ctrl+Alt+→", advance.Text);
        advance.StartCaptureCommand.Execute(null);
        vm.Capture("advance", ModifierKeys.Control | ModifierKeys.Alt, Key.F2);
        advance.ResetCommand.Execute(null);
        Assert.Equal("Ctrl+Alt+→", advance.Text);
        Assert.Equal(12, vm.Rows.Count);
        Assert.Equal(8, new HotkeysViewModel(new HotkeyBinder(new FakeRegistrar(), new HotkeyMap(), Actions(), false), new HotkeyMap(), includeDeveloper: false).Rows.Count);
    }

    // Mientras se espera la combinación nueva, los atajos se sueltan: si no, apretar una que ya está registrada (la de
    // Pausa) dispararía la pausa en vez de llegar a la ventana.
    [Fact]
    public void Mientras_espera_la_combinacion_los_atajos_se_sueltan()
    {
        var registrar = new FakeRegistrar();
        var map = new HotkeyMap();
        var binder = new HotkeyBinder(registrar, map, Actions(), includeDeveloper: true);
        binder.RegisterAll();
        var vm = new HotkeysViewModel(binder, map, includeDeveloper: true);
        var advance = vm.Rows.Single(r => r.Id == "advance");

        advance.StartCaptureCommand.Execute(null);
        Assert.Empty(registrar.Registered);
        Assert.True(vm.Capture("advance", ModifierKeys.Control | ModifierKeys.Alt, Key.F1));
        Assert.Equal(12, registrar.Registered.Count);
        Assert.Contains("Ctrl+Alt+F1", registrar.Registered.Values);

        advance.StartCaptureCommand.Execute(null);
        vm.Capture("advance", ModifierKeys.None, Key.Escape);
        Assert.Equal(12, registrar.Registered.Count);

        // Cerrar la ventana esperando una combinación también los vuelve a tomar, sin registrar nada dos veces.
        advance.StartCaptureCommand.Execute(null);
        vm.CancelCapture();
        vm.CancelCapture();
        Assert.Equal(12, registrar.Registered.Count);
        Assert.Equal(12, registrar.Registered.Values.Distinct().Count());
    }

    // Revisión: "Restaurar" en el renglón que esperaba la combinación dejaba todos los atajos sueltos.
    [Fact]
    public void Restaurar_mientras_espera_vuelve_a_tomar_los_atajos()
    {
        var registrar = new FakeRegistrar();
        var map = new HotkeyMap();
        var binder = new HotkeyBinder(registrar, map, Actions(), includeDeveloper: true);
        binder.RegisterAll();
        var vm = new HotkeysViewModel(binder, map, includeDeveloper: true);
        var advance = vm.Rows.Single(r => r.Id == "advance");
        advance.StartCaptureCommand.Execute(null);
        advance.ResetCommand.Execute(null);
        Assert.Equal(12, registrar.Registered.Count);
        Assert.False(advance.Capturing);
    }

    // Revisión: una de fábrica que tenía otro programa al abrir se ve en la ventana y "Restaurar" la vuelve a intentar.
    [Fact]
    public void La_que_no_se_registro_al_abrir_se_avisa_y_restaurar_la_reintenta()
    {
        var registrar = new FakeRegistrar("Ctrl+Alt+Right");
        var map = new HotkeyMap();
        var binder = new HotkeyBinder(registrar, map, Actions(), includeDeveloper: true);
        binder.RegisterAll();
        var vm = new HotkeysViewModel(binder, map, includeDeveloper: true);
        var advance = vm.Rows.Single(r => r.Id == "advance");
        Assert.Equal("Otro programa ya usa Ctrl+Alt+→", advance.Message);

        advance.ResetCommand.Execute(null);
        Assert.Equal("Otro programa ya usa Ctrl+Alt+→", advance.Message);
        registrar.Taken.Clear(); // el otro programa la soltó
        advance.ResetCommand.Execute(null);
        Assert.Null(advance.Message);
        Assert.Contains("Ctrl+Alt+Right", registrar.Registered.Values);
    }

    // Revisión: una clave repetida en settings.json (editado a mano) tiraba ArgumentException y la app no abría.
    [Fact]
    public void Una_clave_repetida_en_settings_no_rompe_la_app()
    {
        var path = Path.Combine(Path.GetTempPath(), $"bocoach-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, """{ "hotkeys": { "advance": "Ctrl+Alt+F1", "advance": "Ctrl+Alt+F2" } }""");
        var settings = CoachSettings.Load(path);
        Assert.Empty(settings.Hotkeys);
        File.Delete(path);
    }

    // Modo desarrollador: prenderlo registra los atajos de desarrollador y apagarlo los suelta, sin duplicar.
    [Fact]
    public void Prender_y_apagar_el_modo_registra_y_suelta_los_de_desarrollador()
    {
        var registrar = new FakeRegistrar();
        var binder = new HotkeyBinder(registrar, new HotkeyMap(), Actions(), includeDeveloper: false);
        binder.RegisterAll();
        Assert.Equal(8, registrar.Registered.Count);
        binder.IncludeDeveloper = true;
        Assert.Equal(12, registrar.Registered.Count);
        binder.IncludeDeveloper = true;
        Assert.Equal(12, registrar.Registered.Count);
        binder.IncludeDeveloper = false;
        Assert.Equal(8, registrar.Registered.Count);
        Assert.DoesNotContain("Ctrl+Alt+C", registrar.Registered.Values);
    }
}
