# Modo desarrollador (parte 2 de la app para el público) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** que lo de diagnóstico (calibrar a mano, grabar, grabaciones, captura de diagnóstico, lecturas del coach, atajos de desarrollador) quede escondido detrás de un modo desarrollador que se prende con `settings.json` o 5 clics sobre la versión, y sumar "Grabar cada partida sola".

**Architecture:** un estado único `DevMode.Instance` (ObservableObject en `src/BoCoach.App/DevMode.cs`) con `IsOn` y `AutoRecord`, enlazado desde el XAML con `x:Static` (como `HotkeyTips`). `CoachSessionViewModel` lo carga de `settings.json` y lo guarda cuando cambia (es el dueño de los ajustes). `VersionClicks` cuenta los clics; `AutoRecorder` decide con `CoachFrame.InGame` cuándo grabar, contra una interfaz `IRecordingControl` que implementa `CaptureToolsViewModel`. `HotkeyBinder` suma registrar o soltar las de desarrollador en vivo.

**Tech Stack:** C# .NET 10, WPF, CommunityToolkit.Mvvm, xUnit.

**Spec:** `docs/superpowers/specs/2026-10-30-app-publica-design.md` (parte 2).

## Global Constraints

- Textos en castellano rioplatense, sin guiones largos (—). Warnings son errores.
- Un commit por cambio, en castellano, con `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- Tests: `dotnet test BoCoach.slnx -c Release`.
- `settings.json`: `dev_mode` (bool, por defecto false) y `auto_record` (bool, por defecto false). Se guardan siempre.
- 5 clics sobre la versión en menos de 3 s prenden o apagan; la barra de estado dice "Modo desarrollador prendido" o "Modo desarrollador apagado". Sin reiniciar.
- Con el modo apagado no se ven: Calibrar pantalla…, Grabar, Grabaciones, Captura de diagnóstico (y su estado), "Grabar cada partida sola", y del panel en vivo todo menos la línea elegida, Atrás, Ya está, Saltear y el paso actual. Los atajos `calibrate`, `record`, `debug` y `diagnostic` no se registran ni se listan.
- El encabezado del panel en vivo dice "Lo que ve el coach" con el modo prendido y "En la partida" apagado.
- Apagar el modo no corta una grabación en curso; su estado se sigue viendo hasta que termina.
- "Grabar cada partida sola" solo actúa con el modo prendido: arranca cuando `InGame` pasa a true; corta cuando `InGame` vuelve a false (fin de partida o pantalla de carga de otra), al detener el coach; no toca una grabación que empezó a mano; si el jugador la corta a mano no la vuelve a arrancar en la misma partida.

## Review Focus

- `settings.json` con `dev_mode` de otro tipo ("sí", 1): queda apagado, sin excepción.
- Prender el modo con la ventana "Atajos" abierta: la ventana sigue mostrando lo de antes hasta reabrirla, pero los atajos de desarrollador quedan registrados (y al apagar, soltados) sin duplicar registros.
- Apagar el modo mientras graba sola: la grabación termina igual al fin de la partida (no queda grabando para siempre).
- Una partida sin fin limpio (se cierra el juego): el coach automático se detiene y la grabación corta.
- Clics lentos sobre la versión (uno cada 1 s, 10 veces): no prende nada.

---

### Task 1: estado del modo y settings.json

**Files:**
- Create: `src/BoCoach.App/DevMode.cs`, `src/BoCoach.App/VersionClicks.cs`
- Modify: `src/BoCoach.App/Coaching/CoachSettings.cs`, `src/BoCoach.App/Coaching/CoachSessionViewModel.cs` (carga y guardado)
- Test: `tests/BoCoach.App.Tests/DevModeTests.cs`

**Interfaces:**
- Produces:
  - `public sealed partial class DevMode : ObservableObject` con `static DevMode Instance`, `[ObservableProperty] bool IsOn`, `[ObservableProperty] bool AutoRecord`. Al cambiar cualquiera dispara `PropertyChanged` (lo usa `CoachSessionViewModel` para guardar).
  - `public sealed class VersionClicks(int count = 5, TimeSpan? window = null)` (ventana por defecto 3 s) con `bool Click(DateTime now)`: true cuando el último clic completa `count` dentro de la ventana; después vuelve a contar de cero.
  - `CoachSettings.DevMode` y `CoachSettings.AutoRecord` (`dev_mode`, `auto_record`; un valor que no es bool da false).
  - `CoachSessionViewModel(..., DevMode? devMode = null)`: carga los dos valores en el `DevMode` que recibe (por defecto `DevMode.Instance`) y guarda los ajustes cuando cambian.

- [ ] **Step 1: Tests**

```csharp
[Fact]
public void Cinco_clics_rapidos_prenden_y_cuatro_no()
{
    var clicks = new VersionClicks();
    var t = new DateTime(2026, 10, 30, 12, 0, 0);
    Assert.False(clicks.Click(t));
    Assert.False(clicks.Click(t.AddSeconds(0.5)));
    Assert.False(clicks.Click(t.AddSeconds(1)));
    Assert.False(clicks.Click(t.AddSeconds(1.5)));
    Assert.True(clicks.Click(t.AddSeconds(2)));
    Assert.False(clicks.Click(t.AddSeconds(2.2))); // vuelve a contar
}

[Fact]
public void Clics_lentos_no_prenden()
{
    var clicks = new VersionClicks();
    var t = new DateTime(2026, 10, 30, 12, 0, 0);
    Assert.All(Enumerable.Range(0, 10), i => Assert.False(clicks.Click(t.AddSeconds(i))));
}

[Fact]
public void El_modo_y_la_grabacion_sola_se_guardan()
{
    // settings.json con dev_mode "sí" (texto) → apagado, sin excepción; true → prendido;
    // cambiar DevMode.IsOn en un CoachSessionViewModel con settingsPath → CoachSettings.Load(path).DevMode true.
    // Usa una instancia propia de DevMode (no la estática) pasada al constructor.
}
```

El tercero se escribe completo con `new DevMode()`, un `CoachSessionViewModel` como en `CoachSessionTests` (`_data`, `FakeHost`) y un `settings.json` temporal.

- [ ] **Step 2:** `dotnet test tests/BoCoach.App.Tests -c Release --filter DevModeTests` → Expected: no compila.
- [ ] **Step 3:** implementar.
- [ ] **Step 4:** mismo comando → Expected: PASS.
- [ ] **Step 5:** commit "Modo desarrollador: estado, clics sobre la versión y settings.json".

### Task 2: atajos de desarrollador según el modo

**Files:**
- Modify: `src/BoCoach.App/Hotkeys/HotkeyBinder.cs`, `src/BoCoach.App/MainWindow.xaml.cs` (`RegisterHotkeys`, `ShowHotkeys`)
- Test: `tests/BoCoach.App.Tests/HotkeysTests.cs`

**Interfaces:**
- Consumes: `DevMode.Instance.IsOn` (Task 1).
- Produces: `HotkeyBinder.IncludeDeveloper { get; set; }` (el parámetro del constructor pasa a ser el valor inicial). Al ponerlo en false suelta los atajos de desarrollador registrados; en true los registra (devuelve nada; los que no se pueden quedan sin registrar como al abrir). `MainWindow` crea el binder con `DevMode.Instance.IsOn`, lo actualiza cuando cambia `IsOn`, y la ventana "Atajos" se abre con `includeDeveloper: DevMode.Instance.IsOn`.

- [ ] **Step 1: Test**

```csharp
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
```

- [ ] **Step 2:** `dotnet test tests/BoCoach.App.Tests -c Release --filter Prender_y_apagar` → Expected: no compila.
- [ ] **Step 3:** implementar.
- [ ] **Step 4:** `dotnet test BoCoach.slnx -c Release` → Expected: PASS.
- [ ] **Step 5:** commit "Modo desarrollador: los atajos de desarrollador solo con el modo prendido".

### Task 3: grabar cada partida sola

**Files:**
- Create: `src/BoCoach.App/Capture/AutoRecorder.cs`
- Modify: `src/BoCoach.App/Capture/CaptureToolsViewModel.cs` (implementa `IRecordingControl`), `src/BoCoach.App/Coaching/CoachSessionViewModel.cs` (eventos `FrameApplied` en `Apply` y `Stopped` en `Stop`), `src/BoCoach.App/MainWindow.xaml.cs` (crea el `AutoRecorder` y lo conecta)
- Test: `tests/BoCoach.App.Tests/DevModeTests.cs`

**Interfaces:**
- Consumes: `DevMode` (Task 1).
- Produces:
  - `public interface IRecordingControl { bool IsRecording { get; } void StartRecording(); void StopRecording(); }`; `CaptureToolsViewModel` lo implementa con lo que hace hoy `ToggleRecording` (el comando sigue igual).
  - `public sealed class AutoRecorder(IRecordingControl recording, Func<bool> enabled)` con `void OnFrame(bool inGame)` y `void OnCoachStopped()`. `enabled` = `DevMode.IsOn && DevMode.AutoRecord`.
  - Reglas (Global Constraints): arranca al primer `inGame` true de una partida si está habilitado y no se graba; si ya se grababa a mano, esa partida no es suya; corta solo lo que arrancó, cuando `inGame` pasa de true a false o al detener el coach, aunque después se haya deshabilitado; si se cortó a mano, no vuelve a arrancar hasta la partida siguiente.
  - `CoachSessionViewModel.FrameApplied` (`event Action<CoachFrame>?`) y `Stopped` (`event Action?`).

- [ ] **Step 1: Tests** con un `FakeRecording : IRecordingControl` (cuenta arranques y cortes):

```csharp
[Fact]
public void Graba_cada_partida_y_corta_al_terminar()
{
    var rec = new FakeRecording();
    var auto = new AutoRecorder(rec, () => true);
    auto.OnFrame(false);
    Assert.False(rec.IsRecording);
    auto.OnFrame(true);
    auto.OnFrame(true);
    Assert.Equal(1, rec.Starts);
    auto.OnFrame(false);
    Assert.False(rec.IsRecording);
    auto.OnFrame(true);              // partida siguiente
    Assert.Equal(2, rec.Starts);
    auto.OnCoachStopped();
    Assert.False(rec.IsRecording);
}

[Fact]
public void No_toca_una_grabacion_manual_ni_vuelve_si_se_corto_a_mano()
{
    var rec = new FakeRecording();
    var auto = new AutoRecorder(rec, () => true);
    rec.StartRecording();            // a mano, antes de la partida
    auto.OnFrame(true);
    auto.OnFrame(false);
    Assert.True(rec.IsRecording);    // la manual sigue
    rec.StopRecording();
    auto.OnFrame(true);
    Assert.True(rec.IsRecording);
    rec.StopRecording();             // el jugador la corta a mano
    auto.OnFrame(true);
    Assert.False(rec.IsRecording);   // no vuelve en la misma partida
}

[Fact]
public void Deshabilitada_no_arranca_pero_termina_la_suya()
{
    var enabled = true;
    var rec = new FakeRecording();
    var auto = new AutoRecorder(rec, () => enabled);
    auto.OnFrame(true);
    enabled = false;
    auto.OnFrame(false);
    Assert.False(rec.IsRecording);
    auto.OnFrame(true);
    Assert.Equal(1, rec.Starts);
}
```

- [ ] **Step 2:** `dotnet test tests/BoCoach.App.Tests -c Release --filter DevModeTests` → Expected: no compila.
- [ ] **Step 3:** implementar y conectar.
- [ ] **Step 4:** `dotnet test BoCoach.slnx -c Release` → Expected: PASS.
- [ ] **Step 5:** commit "Modo desarrollador: grabar cada partida sola".

### Task 4: la ventana según el modo

**Files:**
- Modify: `src/BoCoach.App/MainWindow.xaml` (visibilidades con `{Binding IsOn, Source={x:Static app:DevMode.Instance}, Converter={StaticResource BoolToVisibility}}`; la versión con `MouseLeftButtonUp`; checkbox "Grabar cada partida sola" enlazada a `AutoRecord`, junto a Captura de diagnóstico; el panel en vivo partido en lo público y lo de desarrollador; encabezado), `src/BoCoach.App/MainWindow.xaml.cs` (clic en la versión → `VersionClicks` → `DevMode.Instance.IsOn = !IsOn` y el estado), `src/BoCoach.App/App.xaml.cs` (`--screenshot salida.png [--dev]` prende el modo para la captura)
- Test: verificación con capturas.

**Interfaces:**
- Consumes: `DevMode`, `VersionClicks` (Task 1).
- Produces: `DevMode.Header` (string: "Lo que ve el coach" o "En la partida", cambia con `IsOn`), con su test en `DevModeTests` (`new DevMode { IsOn = true }.Header`).

- [ ] **Step 1: Test** de `Header` en `DevModeTests`.
- [ ] **Step 2:** `dotnet test tests/BoCoach.App.Tests -c Release --filter DevModeTests` → Expected: no compila.
- [ ] **Step 3:** implementar la ventana.
- [ ] **Step 4:** `dotnet test BoCoach.slnx -c Release` → Expected: PASS. Compilar en Release y sacar `BoCoach.exe --screenshot <scratchpad>/publico.png` y `--screenshot <scratchpad>/dev.png --dev` → Expected: en la primera no están Calibrar, Grabar, Grabaciones ni Captura de diagnóstico; en la segunda sí, con "Grabar cada partida sola". Mirarlas con Read.
- [ ] **Step 5:** commit "Modo desarrollador: la ventana esconde lo de diagnóstico".

### Task 5: versión para probar

- [ ] Skill `nueva-version`: nota en `CLAUDE.md` (commit aparte), punto en `docs/pendientes-usuario.md` (prender el modo con 5 clics, ver que aparezca lo de diagnóstico, probar "Grabar cada partida sola" en una partida y que corte al terminar), subir `<Version>`, compilar en Release, verificar `data/`, pushear `fase-9-refinamiento`.
