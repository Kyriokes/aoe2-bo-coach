# Atajos editables (parte 1 de la app para el público) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** que los 12 atajos globales se vean en una ventana "Atajos", se puedan cambiar y restaurar, queden en `settings.json` y todos los textos que los nombran muestren la combinación actual.

**Architecture:** lógica pura en `src/BoCoach.App/Hotkeys` (`HotkeyBinding`, `HotkeyAction`, `HotkeyMap`, `HotkeyBinder` contra una interfaz `IHotkeyRegistrar`), testeada sin Windows en `BoCoach.App.Tests`. `GlobalHotkeys` implementa `IHotkeyRegistrar` con Win32. `MainWindow` arma todo desde el mapa; la ventana `HotkeysWindow` usa un `HotkeysViewModel` testeable. `HotkeyMap.Current` (estático, por defecto las de fábrica) da el texto de cada atajo a los ViewModels y al XAML.

**Tech Stack:** C# .NET 10, WPF, CommunityToolkit.Mvvm (ya en el proyecto), xUnit.

**Spec:** `docs/superpowers/specs/2026-10-30-app-publica-design.md` (parte 1).

## Global Constraints

- Textos para el usuario en castellano rioplatense, sin guiones largos (—).
- `Directory.Build.props` trata los warnings como errores.
- Un commit por cambio, mensaje en castellano, con `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- Tests: `dotnet test BoCoach.slnx -c Release` (Release: el Debug de la app puede estar bloqueado).
- Rama `fase-9-refinamiento`; no pushear a `main`.
- Ids y fábrica (exactos del spec): `start_stop` S, `advance` Right (→), `back` Left (←), `skip` Down (↓), `pause` P, `overlay` H, `cursor_reminders` M, `accept_suggestion` B, y de desarrollador `calibrate` C, `record` R, `debug` D, `diagnostic` X; todas con Ctrl+Alt.
- Toda combinación lleva Ctrl, Alt o Win. Escape cancela la captura.
- `settings.json`: objeto `hotkeys` id → texto (`"advance": "Ctrl+Alt+Right"`), solo las distintas de fábrica.
- Hasta la parte 2 (modo desarrollador) no existe `DevMode`: la ventana lista también las de desarrollador.

## Review Focus

- `settings.json` con `hotkeys` de otro tipo (número, lista) o con un id desconocido: la app abre con las de fábrica para lo que no sirve, sin excepción.
- Dos acciones con la misma combinación en `settings.json` (editado a mano): la segunda vuelve a fábrica; si también choca, queda sin atajo y se avisa en la barra de estado, nunca dos registros iguales.
- Cambiar a una combinación que Windows rechaza: la vieja sigue registrada y andando (no queda la acción sin atajo).
- Cambiar una acción a su misma combinación: no es "ya la usa otra acción".
- Una combinación de fábrica que otro programa tiene al abrir: el resto se registra igual y el aviso nombra la que falta con su texto actual.

---

### Task 1: HotkeyBinding (combinación ↔ texto)

**Files:**
- Create: `src/BoCoach.App/Hotkeys/HotkeyBinding.cs`
- Test: `tests/BoCoach.App.Tests/HotkeysTests.cs`

**Interfaces:**
- Produces: `public readonly record struct HotkeyBinding(ModifierKeys Modifiers, Key Key)` con `static HotkeyBinding? Parse(string? text)` (formato de guardado, nombres de `Key`: `"Ctrl+Alt+Right"`, orden libre, mayúsculas indistintas; null si no se entiende, si la tecla es un modificador o si no lleva Ctrl/Alt/Win), `string Storage` (`"Ctrl+Alt+Right"`, orden Ctrl, Alt, Shift, Win), `string Display` (igual pero flechas como →, ←, ↑, ↓) y `bool IsValid` (lleva Ctrl, Alt o Win y la tecla no es modificador).

- [ ] **Step 1: Tests**

```csharp
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
```

- [ ] **Step 2:** `dotnet test tests/BoCoach.App.Tests -c Release --filter HotkeysTests` → Expected: no compila (`HotkeyBinding` no existe).
- [ ] **Step 3:** implementar `HotkeyBinding` en `src/BoCoach.App/Hotkeys/HotkeyBinding.cs` (`Enum.TryParse<Key>(..., ignoreCase: true)`; "Ctrl"/"Control", "Alt", "Shift", "Win"/"Windows").
- [ ] **Step 4:** mismo comando → Expected: PASS.
- [ ] **Step 5:** commit "Atajos: combinación a texto y de texto a combinación".

### Task 2: HotkeyAction, HotkeyMap y settings.json

**Files:**
- Create: `src/BoCoach.App/Hotkeys/HotkeyAction.cs`, `src/BoCoach.App/Hotkeys/HotkeyMap.cs`
- Modify: `src/BoCoach.App/Coaching/CoachSettings.cs` (propiedad, `Load`, `Save`)
- Test: `tests/BoCoach.App.Tests/HotkeysTests.cs`

**Interfaces:**
- Consumes: `HotkeyBinding` (Task 1).
- Produces:
  - `public sealed record HotkeyAction(string Id, string Name, HotkeyBinding Default, bool Developer)` y `public static class HotkeyActions { public static IReadOnlyList<HotkeyAction> All { get; } }` con la tabla de Global Constraints en ese orden. Nombres: "Empezar o detener el coach", "Ya está", "Atrás", "Saltear", "Pausa", "Ocultar el overlay", "Recordatorios en el mouse", "Usar la BO sugerida", "Calibrar a mano", "Grabar", "Debug del overlay", "Captura de diagnóstico".
  - `public sealed class HotkeyMap`: `static HotkeyMap Current { get; set; }` (inicial: `new HotkeyMap()`), `HotkeyMap(IReadOnlyDictionary<string, string>? overrides = null)`, `HotkeyBinding? Get(string id)`, `string Text(string id)` (`Display`, o "sin atajo"), `HotkeyAction? UsedBy(HotkeyBinding binding, string exceptId)`, `void Set(string id, HotkeyBinding binding)` (sin validar choques: eso lo hace quien llama), `void Reset(string id)`, `void ResetAll()`, `IReadOnlyDictionary<string, string> Overrides` (solo las distintas de fábrica, en `Storage`), `event Action? Changed` (en cada `Set`/`Reset`/`ResetAll`).
  - Al construir: un override desconocido, que no se entiende o que repite la combinación de una acción anterior en la tabla se ignora (queda la de fábrica); si la de fábrica también la tiene otra acción ya puesta, la acción queda sin atajo (`Get` null).
  - `CoachSettings.Hotkeys`: `Dictionary<string, string>` (vacío por defecto). `Load` lee `json["hotkeys"]` si es objeto, solo valores string; `Save` escribe `hotkeys` solo si no está vacío.

- [ ] **Step 1: Tests**

```csharp
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
    map.Set("advance", HotkeyBinding.Parse("Ctrl+Alt+F1")!.Value);
    Assert.Equal(new Dictionary<string, string> { ["advance"] = "Ctrl+Alt+F1" }, map.Overrides);
    map.Reset("advance");
    Assert.Empty(map.Overrides);
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
        ["back"] = "Ctrl+Alt+P",        // choca con pausa (pausa está después en la tabla, pero su fábrica ya es P)
    });
    Assert.Equal("Ctrl+Alt+→", map.Text("advance"));
    Assert.Equal("Ctrl+Alt+←", map.Text("back"));
    Assert.Equal("Ctrl+Alt+P", map.Text("pause"));
}

[Fact]
public void Settings_guarda_y_lee_los_atajos()
{
    var path = Path.Combine(Path.GetTempPath(), $"bocoach-{Guid.NewGuid():N}.json");
    new CoachSettings { Hotkeys = new() { ["advance"] = "Ctrl+Alt+F1" } }.Save(path);
    Assert.Equal("Ctrl+Alt+F1", CoachSettings.Load(path).Hotkeys["advance"]);
    File.WriteAllText(path, """{ "hotkeys": [1, 2] }""");
    Assert.Empty(CoachSettings.Load(path).Hotkeys);
    File.Delete(path);
}
```

Nota para el choque de `back`: al construir, se recorren las acciones en el orden de la tabla y se reservan primero todas las combinaciones de fábrica de las acciones sin override válido; un override que cae en una combinación ya reservada se ignora.

- [ ] **Step 2:** `dotnet test tests/BoCoach.App.Tests -c Release --filter HotkeysTests` → Expected: no compila.
- [ ] **Step 3:** implementar `HotkeyAction`, `HotkeyActions`, `HotkeyMap` y `CoachSettings.Hotkeys`.
- [ ] **Step 4:** mismo comando → Expected: PASS.
- [ ] **Step 5:** commit "Atajos: tabla de acciones, mapa con lo cambiado y settings.json".

### Task 3: registrar desde el mapa (HotkeyBinder y GlobalHotkeys)

**Files:**
- Create: `src/BoCoach.App/Hotkeys/HotkeyBinder.cs`
- Modify: `src/BoCoach.App/Overlay/GlobalHotkeys.cs`, `src/BoCoach.App/MainWindow.xaml.cs` (`RegisterHotkeys`)
- Test: `tests/BoCoach.App.Tests/HotkeysTests.cs`

**Interfaces:**
- Consumes: `HotkeyMap`, `HotkeyActions` (Task 2).
- Produces:
  - `public interface IHotkeyRegistrar { int? Register(HotkeyBinding binding, Action action); void Unregister(int id); }` (`Register` devuelve el id o null si Windows no la deja).
  - `GlobalHotkeys : IHotkeyRegistrar` (cambia la firma actual de `Register`; `Unregister` llama a `UnregisterHotKey` y saca la acción).
  - `public sealed class HotkeyBinder(IHotkeyRegistrar registrar, HotkeyMap map, IReadOnlyDictionary<string, Action> actions, bool includeDeveloper)`: `IReadOnlyList<string> RegisterAll()` (devuelve los ids que no se pudieron registrar) y `ChangeResult Change(string id, HotkeyBinding? binding)` (`binding` null = restaurar). `public enum ChangeResult { Done, UsedByOther, Rejected }`: `UsedByOther` sin tocar nada; `Rejected` cuando Windows no deja la nueva, y entonces la vieja vuelve a quedar registrada y el mapa no cambia. `Done` actualiza el mapa (`Set` o `Reset`). Expone `HotkeyAction? LastConflict` para el mensaje. `void ResetAll()` restaura una por una con `Change`.
  - `MainWindow.RegisterHotkeys`: arma `actions` (id → la misma acción de hoy), crea `HotkeyMap.Current = new HotkeyMap(settings.Hotkeys)`, `HotkeyBinder(..., includeDeveloper: true)`, y en el estado usa `HotkeyMap.Current.Text(id)` para los que faltan. Al cambiar el mapa (`Changed`) guarda `settings.Hotkeys = map.Overrides`.

- [ ] **Step 1: Tests** con un `FakeRegistrar` en el archivo de tests (lista de combinaciones "ocupadas por otro programa", diccionario id → combinación registrada):

```csharp
[Fact]
public void Registra_todas_y_dice_cuales_tiene_otro_programa()
{
    var registrar = new FakeRegistrar(taken: ["Ctrl+Alt+Right"]);
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
    var registrar = new FakeRegistrar(taken: ["Ctrl+Alt+F1"]);
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
    Assert.Equal(ChangeResult.Done, binder.Change("advance", null));
    Assert.Equal("Ctrl+Alt+→", map.Text("advance"));
    Assert.Equal(12, registrar.Registered.Count);
}
```

- [ ] **Step 2:** `dotnet test tests/BoCoach.App.Tests -c Release --filter HotkeysTests` → Expected: no compila.
- [ ] **Step 3:** implementar `IHotkeyRegistrar`, `HotkeyBinder`, cambiar `GlobalHotkeys` y `MainWindow.RegisterHotkeys`.
- [ ] **Step 4:** `dotnet test BoCoach.slnx -c Release` → Expected: PASS todo.
- [ ] **Step 5:** commit "Atajos: se registran desde el mapa y se pueden cambiar sin perder el anterior".

### Task 4: ventana "Atajos"

**Files:**
- Create: `src/BoCoach.App/Hotkeys/HotkeysViewModel.cs`, `src/BoCoach.App/Hotkeys/HotkeysWindow.xaml`, `HotkeysWindow.xaml.cs`
- Modify: `src/BoCoach.App/MainWindow.xaml` (botón "Atajos" en la barra de arriba, después de "Empezar coach"), `MainWindow.xaml.cs` (abre la ventana, no modal, una sola a la vez), `src/BoCoach.App/App.xaml.cs` (`--screenshot salida.png --hotkeys` dibuja la ventana sin el juego, como `--first-run`)
- Test: `tests/BoCoach.App.Tests/HotkeysTests.cs`

**Interfaces:**
- Consumes: `HotkeyBinder`, `HotkeyMap`, `HotkeyActions` (Tasks 2 y 3).
- Produces: `HotkeysViewModel(HotkeyBinder binder, HotkeyMap map, bool includeDeveloper)` con `Rows` (`HotkeyRowViewModel`: `Id`, `Name`, `Text`, `Capturing`, `Message`, `IsError`, `StartCaptureCommand`, `ResetCommand`), `ResetAllCommand` y `bool Capture(string id, ModifierKeys modifiers, Key key)` (la ventana lo llama desde `PreviewKeyDown` mientras una fila captura; devuelve true si terminó la captura).

Textos exactos: mientras captura, `Text` = "Apretá la nueva…"; Escape → vuelve sin mensaje; sin Ctrl/Alt/Win → sigue capturando con `Message` "Tiene que llevar Ctrl, Alt o Win"; tecla modificador sola → sigue capturando sin mensaje; `UsedByOther` → `IsError`, "Ya la usa: <nombre>"; `Rejected` → `IsError`, "Otro programa ya usa <combinación>"; `Done` → sin mensaje.

- [ ] **Step 1: Tests**

```csharp
[Fact]
public void La_ventana_cambia_y_avisa_choques()
{
    var (vm, map) = Window();
    var advance = vm.Rows.Single(r => r.Id == "advance");
    advance.StartCaptureCommand.Execute(null);
    Assert.Equal("Apretá la nueva…", advance.Text);
    Assert.False(vm.Capture("advance", ModifierKeys.None, Key.F1));
    Assert.Equal("Tiene que llevar Ctrl, Alt o Win", advance.Message);
    Assert.True(vm.Capture("advance", ModifierKeys.Control | ModifierKeys.Alt, Key.P));
    Assert.Equal("Ya la usa: Pausa", advance.Message);
    Assert.True(advance.IsError);
    advance.StartCaptureCommand.Execute(null);
    Assert.True(vm.Capture("advance", ModifierKeys.Control | ModifierKeys.Alt, Key.F1));
    Assert.Equal("Ctrl+Alt+F1", advance.Text);
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
}
```

- [ ] **Step 2:** `dotnet test tests/BoCoach.App.Tests -c Release --filter HotkeysTests` → Expected: no compila.
- [ ] **Step 3:** implementar el ViewModel, la ventana (mismo estilo oscuro que `FirstRunWindow`: `#2B2724` de fondo, columna nombre, botón con la combinación, "Restaurar" por fila, "Restaurar todos" abajo; mensaje en rojo `#E06C5A` debajo del renglón) y el botón.
- [ ] **Step 4:** `dotnet test BoCoach.slnx -c Release` → Expected: PASS. Después `dotnet build src/BoCoach.App -c Release` y `src/BoCoach.App/bin/Release/net10.0-windows10.0.19041.0/BoCoach.exe --screenshot <scratchpad>/atajos.png --hotkeys` → Expected: la imagen muestra los 12 renglones; mirarla con Read.
- [ ] **Step 5:** commit "Atajos: ventana para verlos y cambiarlos".

### Task 5: los textos muestran la combinación actual

**Files:**
- Modify: `src/BoCoach.Core/Engine/CoachText.cs` (`Lines(CoachView view, GameData data, string backHotkey = "Ctrl+Alt+←")`, pasado a `BaseLines`), `src/BoCoach.App/Overlay/OverlayViewModel.cs` (pasa `HotkeyMap.Current.Text("back")`; "En pausa: <pause> para seguir."), `src/BoCoach.App/Coaching/CoachSessionViewModel.cs` (estado al empezar y sugerida/cambio de BO con `accept_suggestion`, `advance`, `back`, `skip`, `overlay`, `cursor_reminders`), `src/BoCoach.App/MainWindow.xaml` (tooltips de Empezar coach, Calibrar, Grabar, Atrás, Ya está y Saltear como `<Button.ToolTip><TextBlock Text="{Binding ..., StringFormat=...}"/></Button.ToolTip>` contra una propiedad del ViewModel que se actualiza con `HotkeyMap.Changed`)
- Test: `tests/BoCoach.Core.Tests/Engine/CoachTests.cs` (o el archivo de tests de `CoachText` que ya exista), `tests/BoCoach.App.Tests/CoachSessionTests.cs`

**Interfaces:**
- Consumes: `HotkeyMap.Current`, `HotkeyMap.Changed` (Task 2).
- Produces: `MainViewModel.HotkeyTexts` (o una clase `HotkeyTexts : ObservableObject` con una propiedad string por acción usada en el XAML, que se refresca en `Changed`).

- [ ] **Step 1: Tests**
  - Core: con la BO abandonada, `CoachText.Lines(view, data, backHotkey: "Ctrl+Alt+F9").Detail` contiene "Ctrl+Alt+F9 vuelve a la BO" (y sin el argumento, "Ctrl+Alt+← vuelve a la BO").
  - App: con `HotkeyMap.Current` cambiado (`accept_suggestion` → Ctrl+Alt+F8), el texto de la sugerida del overlay termina en "Ctrl+Alt+F8 para usarla". Restaurar `HotkeyMap.Current = new HotkeyMap()` al final del test (es estático).
- [ ] **Step 2:** `dotnet test BoCoach.slnx -c Release` → Expected: los tests nuevos fallan (el texto dice la combinación de fábrica).
- [ ] **Step 3:** cambiar los textos.
- [ ] **Step 4:** `dotnet test BoCoach.slnx -c Release` → Expected: PASS. `grep -rn "Ctrl+Alt" src --include=*.cs --include=*.xaml` → Expected: solo comentarios, la tabla de `HotkeyActions` y el valor por defecto de `backHotkey`.
- [ ] **Step 5:** commit "Atajos: los textos de la app y del overlay muestran la combinación elegida".

### Task 6: versión para probar

- [ ] Seguir la skill `nueva-version`: anotar en `CLAUDE.md` (sección de la fase 9) los atajos editables (commit aparte), subir `<Version>` (0.9.47 → 0.9.48), compilar en Release, verificar `data/`, pushear `fase-9-refinamiento`.
- [ ] Anotar en `docs/pendientes-usuario.md` la prueba: abrir "Atajos", cambiar "Ya está" a otra combinación, usarla en el juego, cerrar y abrir la app y ver que quedó; probar una que use otro programa.
