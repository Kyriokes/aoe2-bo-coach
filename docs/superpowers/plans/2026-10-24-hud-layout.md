# Plan de implementación: HUD propio descripto en un archivo

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** El mod `BoCoach HUD` dibuja un panel de recursos propio (oscuro y liso, íconos del juego, casillero del coach, campos negros) descripto en `data/hud_layout.json`, y de ese mismo archivo salen la calibración sola, el lector y el lugar de la GD.

**Architecture:** `HudLayout` (Vision) carga y valida el JSON y convierte unidades del panel a píxeles dentro de una caja. El generador (`App/Capture/HudMod`) arma el `resourcepanel.json` y el `worldtimerpanel.json` del mod a partir de los originales del juego más el diseño. `AutoCalibration` ancla en los cinco casilleros de color y arma las cajas desde el diseño; `LayoutPanel` recorta cada campo por proporción dentro de su caja y `HudReader` lo lee con la letra que dice el diseño. `regions.json` y `session.json` guardan el diseño entero, así cada grabación se lee con el suyo. El camino viejo (`ModPanel`, seis franjas) queda intacto para el mod viejo y sin mod.

**Tech Stack:** C# .NET 10, WPF (App), `System.Text.Json`, xunit. Sin librerías nuevas.

**Spec:** `docs/superpowers/specs/2026-10-24-hud-layout-design.md` (leer antes de cada tarea: la tabla de medidas de la sección 2 y el JSON de la 3 son la fuente de los números).

## Global Constraints

- `Core` no referencia `Vision` ni nada de Windows (`ArquitecturaTests`): el diseño vive en `BoCoach.Vision/Hud/HudLayout.cs`.
- Unidades del panel: 84 = el ícono original; `pixels_per_unit_at_1080p` = 0,4286 (84 unidades = 36 px). Escala respecto de 1080p = factor medido / 0,4286.
- Nombres de celda, en orden: `wood`, `food`, `gold`, `stone`, `population`, `idle`, `age`. Campos por celda: recursos `villagers` y `stock`; `population` `total` y `cap`; `idle` `count`; `age` `text`. Fuentes válidas: `hud_numbers`, `mod_clock`, `ocr`.
- Colores de los casilleros (JSON, r g b de 0 a 1): madera 0,53 0,36 0,16; comida 0,88 0,41 0,42; oro 0,94 0,75 0,10; piedra 0,62 0,62 0,65; población 0,16 0,39 0,90. Tolerancia al buscarlos: 45 por canal.
- Tolerancias: calibración, cada casillero a 3 px (a 1080p, escalados) de donde el diseño lo pone y ancho a menos del 10 %; lector, bordes del negro a 3 px a 1080p.
- Textos en castellano rioplatense, sin guiones largos. Commits chicos, uno por cambio, en `fase-9-refinamiento`, terminados en `Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>`.
- Tests: `dotnet test BoCoach.slnx` tiene que seguir en verde (las grabaciones viejas y sus golden no cambian). Si `BoCoach.exe` de Debug está bloqueado por la app abierta, agregar `-c Release`.

## Review Focus

1. **La GD del overlay tapa el medio del casillero** en las capturas en vivo: la calibración tiene que encontrar el casillero igual, por los bordes. Test en la Tarea 6 (`Con_la_GD_dibujada_adentro_de_los_casilleros_encuentra_lo_mismo`).
2. **Stock de 5 cifras ("10000") y población "200/200"** tienen que entrar en sus campos y leerse enteros. Test en la Tarea 7 (`Lee_cinco_cifras_de_stock_y_la_poblacion_al_tope`).
3. **Partida en pausa** (todo oscurecido ×0,2): cada campo se estira de brillo antes de leerse. Test en la Tarea 7 (`En_pausa_lee_igual`).
4. **Calibración manual después de una sola:** `regions.json` conserva el diseño y el lector verifica el casillero antes de usarlo. Tests en la Tarea 2 (`La_calibracion_manual_conserva_el_diseno`) y la Tarea 7 (`Sin_el_casillero_en_su_lugar_cae_al_camino_viejo`).
5. **Grabación del mod viejo leída con el diseño nuevo a mano:** tiene que dar lo mismo que sin él. Test en la Tarea 7 (`Una_grabacion_del_mod_viejo_se_lee_igual_con_el_diseno`).

---

### Tarea 1: El archivo de diseño y su modelo (`HudLayout`)

**Files:**
- Create: `data/hud_layout.json`
- Create: `src/BoCoach.Vision/Hud/HudLayout.cs`
- Modify: `src/BoCoach.App/BoCoach.App.csproj:50` (una línea `None Include` más, como `maps.json`, con `Link="data\hud_layout.json"`)
- Modify: `tests/BoCoach.Vision.Tests/BoCoach.Vision.Tests.csproj:27` (`Link="hud_layout.json"`) y `tests/BoCoach.App.Tests/BoCoach.App.Tests.csproj:27` (`Link="data\hud_layout.json"`)
- Test: `tests/BoCoach.Vision.Tests/HudLayoutTests.cs`

**Interfaces:**
- Produces (namespace `BoCoach.Vision.Hud`):
  - `public sealed record HudColor(double R, double G, double B)` con `public (int R, int G, int B) Captured` (cada canal redondeado ×255).
  - `public sealed record UnitRect(int X, int Y, int Width, int Height)`.
  - `public sealed record HudIcon(int X, int Y, int Size)`, `HudChip(int X, int Y, int Width, int Height, HudColor Color)`, `HudField(int X, int Y, int Width, int Height, string Font, int Points)`, `HudSeparator(int X, int Y, int Width, int Height, string? Text, int Points, HudColor Color)`.
  - `public sealed record HudCell(string Name, int X, int Width, HudIcon Icon, HudChip? Chip, IReadOnlyDictionary<string, HudField> Fields, IReadOnlyList<UnitRect> Backgrounds, HudSeparator? Separator)`.
  - `public sealed record HudBar(int Width, int Height, HudColor Background, int BorderWidth, HudColor BorderColor)`.
  - `public sealed record HudClock(int Gap, int Y, int Width, int Height, string Font, int Points)`.
  - `public sealed class HudLayout` con `string Name`, `double PixelsPerUnitAt1080p`, `HudBar Bar`, `IReadOnlyList<HudCell> Cells` (en el orden de `CellOrder`), `HudClock Clock`;
    `public const string FileName = "hud_layout.json"`; `public static readonly string[] CellOrder = ["wood", "food", "gold", "stone", "population", "idle", "age"]`;
    `public static HudLayout Load(string path)`, `public static HudLayout Parse(string json, string source = "hud_layout.json")`, `public string ToJson()` (mismo formato del spec, snake_case, colores como `[r, g, b]`);
    `public HudCell Cell(string name)`; `public IEnumerable<HudCell> Anchors` (las celdas con casillero, en orden);
    `public static string RegionOf(string cell)` (`wood`→`RegionNames.Wood` … `idle`→`RegionNames.IdleVillagers`, `age`→`RegionNames.Age`) y `public static string? CellOf(string region)`;
    `public UnitRect CellRect(HudCell cell)` = (cell.X, 0, cell.Width, Bar.Height); `public UnitRect AgeTextRect()` = el campo `text` de `age` en coordenadas de la barra; `public UnitRect ClockRect()` = (Bar.Width + Clock.Gap, Clock.Y, Clock.Width, Clock.Height);
    `public PixelRect FieldRect(HudCell cell, string field, PixelRect box)` y `public PixelRect ChipRect(HudCell cell, PixelRect box)`: proporción dentro de la caja de la celda (x = box.X + round(f.X × box.Width / cell.Width); y = box.Y + round(f.Y × box.Height / Bar.Height); ancho y alto igual).
- Validación en `Parse` (lanza `InvalidDataException` con el nombre del archivo y qué falta): las siete celdas de `CellOrder` presentes; celdas ordenadas por x, sin solaparse, dentro de la barra; ícono, casillero, campos, fondos y separador dentro de la celda (0..ancho, 0..alto de la barra); los campos exactos de cada celda; fuentes válidas; casillero obligatorio en las cinco primeras celdas.

- [ ] **Paso 1: Escribir `data/hud_layout.json` completo** con los valores de la sección 2 y 3 del spec (las celdas de comida, oro y piedra copian la de madera con `x` 420, 830, 1240 y su color; `separator` con `"text": "/"`, 44 pt, color 0,55 gris).

- [ ] **Paso 2: Escribir los tests que fallan** en `tests/BoCoach.Vision.Tests/HudLayoutTests.cs` (el archivo del repo llega a la salida como `hud_layout.json` por el csproj):

```csharp
private static HudLayout Repo() => HudLayout.Load(Path.Combine(AppContext.BaseDirectory, "hud_layout.json"));

[Fact] public void Carga_el_diseno_del_repo_con_sus_siete_celdas_en_orden()
{   var layout = Repo();
    Assert.Equal("bocoach-2026-10", layout.Name);
    Assert.Equal(0.4286, layout.PixelsPerUnitAt1080p, 4);
    Assert.Equal(HudLayout.CellOrder, layout.Cells.Select(c => c.Name));
    Assert.Equal((2720, 108), (layout.Bar.Width, layout.Bar.Height));
    Assert.Equal(10, layout.Cell("wood").X);
    Assert.Equal(80, layout.Cell("population").Chip!.Width);
    Assert.Equal(5, layout.Anchors.Count());
    Assert.Equal(new UnitRect(2734, 12, 230, 84), layout.ClockRect()); }

[Fact] public void Las_celdas_no_se_solapan_y_todo_entra_en_su_celda()  // recorre Cells: X ≥ anterior.X + anterior.Width; ícono, chip, fields, backgrounds y separator dentro de (0, 0, Width, Bar.Height)

[Fact] public void El_rectangulo_de_un_campo_dentro_de_su_caja_va_en_proporcion()
{   var box = new PixelRect(100, 20, 176, 46); // 410 × 108 unidades a 0,4286
    Assert.Equal(new PixelRect(218, 25, 57, 36), Repo().FieldRect(Repo().Cell("wood"), "stock", box));
    Assert.Equal(new PixelRect(145, 25, 30, 36), Repo().ChipRect(Repo().Cell("wood"), box)); }

[Fact] public void Un_diseno_sin_un_campo_dice_cual_falta()  // ToJson() del repo con "stock" renombrado a "stok" → Assert.Throws<InvalidDataException>(...).Message contiene "stock"

[Fact] public void Ida_y_vuelta_por_JSON()  // Assert.Equal(Repo().ToJson(), HudLayout.Parse(Repo().ToJson()).ToJson())

[Fact] public void Regiones_y_celdas_se_corresponden()  // RegionOf("idle") == RegionNames.IdleVillagers; CellOf(RegionNames.Population) == "population"; CellOf(RegionNames.Clock) == null
```

- [ ] **Paso 3: Correr y ver que fallan**: `dotnet test tests/BoCoach.Vision.Tests --filter "FullyQualifiedName~HudLayoutTests"` → no compila (`HudLayout` no existe).

- [ ] **Paso 4: Implementar `HudLayout.cs`** con DTOs privados (`System.Text.Json`, `JsonNamingPolicy.SnakeCaseLower`, `ReadCommentHandling.Skip`, como `RegionLayout`) y las validaciones listadas; `Load` envuelve `JsonException` en `InvalidDataException` con el nombre del archivo.

- [ ] **Paso 5: Correr los tests**: el mismo filtro → PASS. Después `dotnet build BoCoach.slnx` para verificar que `data/hud_layout.json` llega a `bin/.../data/` de la App.

- [ ] **Paso 6: Commit**: `git add data/hud_layout.json src/BoCoach.Vision/Hud/HudLayout.cs src/BoCoach.App/BoCoach.App.csproj tests/BoCoach.Vision.Tests/BoCoach.Vision.Tests.csproj tests/BoCoach.App.Tests/BoCoach.App.Tests.csproj tests/BoCoach.Vision.Tests/HudLayoutTests.cs` y `git commit -m "Diseño del HUD propio: data/hud_layout.json y HudLayout"`.

---

### Tarea 2: El diseño viaja con la calibración y con las grabaciones

**Files:**
- Modify: `src/BoCoach.Vision/Regions/RegionLayout.cs` (clase `RegionLayout`, `Dto`, `Save`, `LoadOrDefault`, `With*`)
- Modify: `src/BoCoach.Vision/Recording/RecordedSession.cs` (`Load`) y `src/BoCoach.Vision/Recording/SessionRecorder.cs:66-99` (`Open`)
- Test: `tests/BoCoach.Vision.Tests/RegionLayoutTests.cs`, `tests/BoCoach.Vision.Tests/SessionRecorderTests.cs`

**Interfaces:**
- Consumes: `HudLayout.Parse`, `HudLayout.ToJson` (Tarea 1).
- Produces: `RegionLayout(int referenceWidth, int referenceHeight, IReadOnlyDictionary<string, RegionRect> regions, double hudScale = 1, HudLayout? hud = null)`; `public HudLayout? Hud { get; }`; `public RegionLayout WithHud(HudLayout? hud)`. `With`, `WithReference` y `WithHudScale` conservan `Hud`. En `regions.json`, `hud_layout` es el objeto del diseño (`JsonElement` en el `Dto`; se escribe con `JsonSerializer.Deserialize<JsonElement>(Hud.ToJson())` y se lee con `HudLayout.Parse(element.GetRawText(), "regions.json")`); ausente o null → `Hud` null. `RecordedSession.Hud` (`HudLayout?`) sale de `hud_layout` de `session.json` igual; `SessionRecorder.Open` lo escribe cuando `_layout.Hud` no es null.

- [ ] **Paso 1: Tests que fallan** (en los archivos existentes, mismo estilo):

```csharp
// RegionLayoutTests
[Fact] public void Guarda_y_carga_el_diseno_del_HUD()
{   var hud = HudLayout.Load(Path.Combine(AppContext.BaseDirectory, "hud_layout.json"));
    RegionLayout.Default.WithHud(hud).WithHudScale(1.5).Save(_path);
    var loaded = RegionLayout.LoadOrDefault(_path);
    Assert.Equal("bocoach-2026-10", loaded.Hud!.Name);
    Assert.Equal(hud.ToJson(), loaded.Hud.ToJson());
    Assert.Equal(1.5, loaded.HudScale); }

[Fact] public void La_calibracion_manual_conserva_el_diseno()  // WithHud(hud).WithReference(2560, 1440).With(RegionNames.Food, rect).Hud no es null

[Fact] public void Sin_diseno_el_archivo_no_lo_trae()  // Default.Save(_path): el texto no contiene "hud_layout" con objeto (puede ser null) y LoadOrDefault(...).Hud == null

// SessionRecorderTests (usar el fake IScreenSource que ya usan)
[Fact] public void session_json_lleva_el_diseno_del_HUD_y_la_grabacion_lo_devuelve()  // recorder con layout.WithHud(hud); Open(); Stop(); RecordedSession.Load(folder).Hud!.Name == "bocoach-2026-10"; y sin Hud, RecordedSession.Hud == null
```

- [ ] **Paso 2: Correr y ver que fallan**: `dotnet test tests/BoCoach.Vision.Tests --filter "FullyQualifiedName~RegionLayoutTests|FullyQualifiedName~SessionRecorderTests"`.

- [ ] **Paso 3: Implementar** los cambios de la interfaz. En `RecordedSession.Load`, `hud_layout` ausente, null o inválido → `Hud` null (una grabación vieja nunca falla por esto).

- [ ] **Paso 4: Correr**: mismo filtro → PASS; `dotnet test tests/BoCoach.Vision.Tests` entero → PASS (los fixtures viejos no tienen `hud_layout`).

- [ ] **Paso 5: Commit**: `git commit -m "regions.json y session.json guardan el diseño del HUD"`.

---

### Tarea 3: El generador del mod arma el panel desde el original y el diseño

**Files:**
- Modify: `src/BoCoach.App/Capture/HudMod.cs` (reescritura de `ResourcePanel`, `ClockPanel`, `Install`; se quita `regenerate`)
- Modify: `src/BoCoach.App/Capture/CaptureToolsViewModel.cs:119-135` (`InstallMod` carga el diseño) y `src/BoCoach.App/CoachData.cs` (propiedad `HudLayout Layout` perezosa desde `Path.Combine(DataDir, HudLayout.FileName)`)
- Test: `tests/BoCoach.App.Tests/HudModTests.cs` (reescrito), `tests/BoCoach.App.Tests/CaptureToolsTests.cs:197-215` (ajuste del texto esperado)

**Interfaces:**
- Consumes: `HudLayout` (Tarea 1).
- Produces: `public static HudModResult Install(string game, HudLayout layout, string? profilesRoot = null)`; `public static JsonObject ResourcePanel(JsonObject original, HudLayout layout)`; `public static JsonObject ClockPanel(JsonObject original, HudLayout layout)`. `Installed` dice `"Perfil {nombre}: diseño {layout.Name}."`. `info.json` → `Description` = `"Panel de recursos propio ({layout.Name}): fondo liso, íconos del juego, casillero del coach y campos negros para que BoCoach los lea."`. `CaptureToolsViewModel` recibe `Func<HudLayout> layout` en el constructor (como `fonts`), por defecto `() => HudLayout.Load(Path.Combine(CoachData.DataDir, HudLayout.FileName))`.

Reglas del panel de recursos (en unidades del diseño, todo con `alignment: TopLeft`; `Flat(r,g,b)` = `FlatColorWithAlpha`):
- Antes de nada, se descartan los widgets cuyo `Name` empieza con `BoCoach` (si el "original" fuera un panel ya modificado).
- `Collection.ViewPort` = (0, 0, bar.Width, bar.Height). `Background.ViewPort` igual; `StateMaterials` = `UserStates` 9 con `UserState0..8` = `Flat(bar.Background)`. Hijos nuevos al principio: `BoCoachBorderTop` (0,0,W,b), `BoCoachBorderBottom` (0,H−b,W,b), `BoCoachBorderLeft` (0,0,b,H), `BoCoachBorderRight` (W−b,0,b,H), `Flat(bar.BorderColor)`, `ZPlaneLocalOffset` 1.
- Celdas contenedor (`Wood`, `Food`, `Gold`, `Stone`, `Population`): `ViewPort` = (cell.X, 0, cell.Width, bar.Height); `StateMaterials` = `{ StateNormal: { Material: "None" } }` (en `Population`, `UserStates` 2 con los dos en `None`); `ZPlaneLocalOffset` 1 se conserva; `Help` se conserva. Hijos, en este orden y relativos a la celda: `BoCoachIcon` (`Base`, material = el que tenía el contenedor en `StateNormal.Material` o `UserState0.Material`, o `"Resource{Nombre}"`/`"Population"` si no tenía; ViewPort (icon.X, icon.Y, size, size)); `BoCoachChip` (`Flat(chip.Color)`) si hay casillero; un `BoCoachBack{i}` por cada `backgrounds` (`Flat(0,0,0)`); `BoCoachSeparator` si hay: con `text`, un `Label` con `Text`, `TextAnchor: CentreCentre`, letra del `FontIndex` original a `points` y `TextColor` = color, `ZPlaneLocalOffset` 2; sin `text`, un `Base` `Flat(color)` de 4 unidades de ancho centrado en el rectángulo; por último el `Workers` original con `ViewPort` = el campo `villagers` (o `total`), letra del `FontIndex` original a `points`, blanco, `TextAnchor: CentreCentre`, `ZPlaneLocalOffset` 2.
- Etiquetas hermanas: `WoodStorage`… = campo `stock` en coordenadas de la barra (cell.X + f.X, f.Y, w, h); `PopulationCount` = campo `cap`; `IdleWorkers` = campo `count`; todas con la letra a `points`, blanco, `TextAnchor: CentreCentre`, `ZPlaneLocalOffset` 2 (y `ZPlane` original si lo tenía). `PopulationFlash.ViewPort` = campo `cap` en coordenadas de la barra. Los fondos de `idle` van como hermanos `BoCoachBack_idle_{i}` insertados justo antes de `Idle`.
- `Idle` (botón): `ViewPort` = (idle.X + icon.X, icon.Y, size, size), materiales originales. `AgeUp`: (age.X + icon.X, icon.Y, size, size). `AgeBar`: campo `text` en coordenadas de la barra; su hijo `AgeTextLabel` → (0, 0, w, h), `PointSize` = points, `TextAnchor: CentreCentre`.

Reglas del reloj: `Collection.ViewPort.xorigin` = bar.Width + clock.Gap, `yorigin` 0; `BoCoachClockBackground` `Flat(0,0,0)` en (0, clock.Y, clock.Width, clock.Height) como primer hijo de `WorldTimer`; `WorldTimerLabel.ViewPort` = (8, clock.Y + (clock.Height − points) / 2, 1000, points), `PointSize` = points, `ZPlaneLocalOffset` 1; `SpectatorsIcon` y `GameIDLabel` con `yorigin` = clock.Y + clock.Height + 12.

- [ ] **Paso 1: Reescribir `HudModTests.cs`** (conservar `ResourcePanel()`, `ClockPanel()` y `FakeGame()`; agregar `["Help"] = 42202` a `WoodStorage` y `["Help"] = 42011` al ícono `Wood` del fake; un helper `Layout()` que carga `data/hud_layout.json` de la salida):

```csharp
[Fact] public void Arma_cada_celda_desde_el_original_segun_el_diseno()
{   var panel = HudMod.ResourcePanel(ResourcePanel(), Layout());
    var wood = Widget(panel, "Wood");
    Assert.Equal((10, 0, 410, 108), View(wood));              // helper que devuelve (x, y, w, h) del ViewPort
    Assert.Equal("None", (string?)wood["StateMaterials"]!["StateNormal"]!["Material"]);
    Assert.Equal(42011, (int)wood["Help"]!);
    var names = Children(wood).Select(c => (string?)c["Name"]).ToList();
    Assert.Equal(["BoCoachIcon", "BoCoachChip", "BoCoachBack0", "BoCoachSeparator", "Workers"], names);
    Assert.Equal("ResourceWood", (string?)Children(wood)[0]["StateMaterials"]!["StateNormal"]!["Material"]);
    Assert.Equal((104, 12, 70, 84), View(Children(wood)[1]));
    Assert.Equal(0.53, (double)Children(wood)[1]["StateMaterials"]!["StateNormal"]!["Color"]!["r"]!, 2);
    Assert.Equal("/", (string?)Children(wood)[3]["Text"]);
    Assert.Equal((184, 12, 70, 84), View(Children(wood)[4]));
    Assert.Equal(48, (int)Children(wood)[4]["StateMaterials"]!["StateNormal"]!["Font"]!["PointSize"]!);
    var storage = Widget(panel, "WoodStorage");
    Assert.Equal((284, 12, 132, 84), View(storage));
    Assert.Equal(42202, (int)storage["Help"]!);
    Assert.Equal("CentreCentre", (string?)storage["TextAnchor"]);
    Assert.Equal((1840, 12, 180, 84), View(Widget(panel, "PopulationCount")));
    Assert.Equal((1840, 12, 180, 84), View(Widget(panel, "PopulationFlash")));
    Assert.Equal((2040, 12, 84, 84), View(Widget(panel, "Idle")));
    Assert.Equal((2130, 12, 80, 84), View(Widget(panel, "IdleWorkers")));
    Assert.Equal((2324, 28, 378, 52), View(Widget(panel, "AgeBar"))); }

[Fact] public void El_fondo_es_liso_en_los_nueve_estados_y_la_barra_mide_lo_del_diseno()
{   // Background: UserStates == 9, UserState0 y UserState8 con Material FlatColorWithAlpha y Color.r 0.1; ViewPort (0,0,2720,108); los cuatro BoCoachBorder* existen; Collection.ViewPort width 2720 height 108 }

[Fact] public void Un_panel_ya_modificado_vuelve_a_salir_igual()  // ResourcePanel(ResourcePanel(ResourcePanel(), L), L) DeepEquals ResourcePanel(ResourcePanel(), L)

[Fact] public void El_reloj_toma_el_diseno_y_la_coleccion_se_corre_al_final_de_la_barra()
{   var panel = HudMod.ClockPanel(ClockPanel(), Layout());
    Assert.Equal(2734, (int)panel["Collection"]!["ViewPort"]!["xorigin"]!);
    Assert.Equal((0, 12, 230, 84), View(Widget(panel, "BoCoachClockBackground")));
    Assert.Equal(56, (int)Widget(panel, "WorldTimerLabel")["StateMaterials"]!["StateNormal"]!["Font"]!["PointSize"]!);
    Assert.Equal(12 + 84 + 12, (int)Widget(panel, "GameIDLabel")["ViewPort"]!["yorigin"]!); }

[Fact] public void Instala_en_cada_perfil_con_mods_aunque_no_tenga_lista()  // Installed == ["Perfil 111: diseño bocoach-2026-10."]; existen los dos json; info.json contiene "bocoach-2026-10"

[Fact] public void Instalar_dos_veces_deja_los_mismos_archivos()  // bytes iguales de resourcepanel.json y worldtimerpanel.json

[Fact] public void Avisa_si_otro_mod_activo_le_gana()  // como hoy Parte_del_panel_del_mod_activo_y_avisa_si_le_gana_al_nuestro, pero Installed == ["Perfil 111: diseño bocoach-2026-10."]

[Fact] public void Sin_perfiles_avisa_que_abra_el_juego()  // igual que hoy
```

- [ ] **Paso 2: Correr y ver que fallan**: `dotnet test tests/BoCoach.App.Tests --filter "FullyQualifiedName~HudModTests"`.

- [ ] **Paso 3: Implementar `HudMod`** según las reglas de arriba (conservar los helpers `All`, `Children`, `Detach`, `Flat`, `View`, `Wrap`, `NumberFont` → ahora `Font(JsonNode fontIndex, int points, HudColor color)`), `CoachData.Layout` y el `Func<HudLayout>` en `CaptureToolsViewModel`. Ajustar en `CaptureToolsTests` lo que mire el texto "recursos de original".

- [ ] **Paso 4: Correr**: `dotnet test tests/BoCoach.App.Tests` → PASS.

- [ ] **Paso 5: Commit**: `git commit -m "El mod del HUD se arma desde el original del juego y data/hud_layout.json"`.

---

### Tarea 4: `ReplaySession hud-preview` dibuja el diseño

**Files:**
- Create: `tools/ReplaySession/HudPreview.cs`
- Modify: `tools/ReplaySession/Program.cs:85-105` (comando `"hud-preview" => HudPreview.Render(args[1], Layout(args), Text(args, "--fonts", Path.Combine("data", "fonts")), Option(args, "--scale", 1.0))`) y el comentario de uso al principio.

**Interfaces:**
- Consumes: `HudLayout`, `GlyphFont.Load` y sus plantillas (`GlyphFont` ya expone las plantillas que usa `GlyphReader`; si no hay acceso público a las grillas, agregar `public IReadOnlyList<GlyphTemplate> Templates { get; }`), `Png.Write`, `CapturedImage`.
- Produces: `public static int Render(string output, HudLayout layout, string fontsDir, double scale)`; `static HudLayout Layout(string[] args)` en `Program.cs` = `HudLayout.Load(Text(args, "--layout", Path.Combine("data", HudLayout.FileName)))`.

Qué dibuja (píxeles por unidad = `layout.PixelsPerUnitAt1080p × scale`, imagen de (bar.Width + gap + clock.Width + 40) × (bar.Height + 40) unidades, fondo verde 79,107,58): la barra con su fondo y borde; por celda, el ícono como cuadrado gris 70,70,70; el casillero con su color; los fondos negros; el separador como barra gris de 4 unidades; en cada campo, el texto de muestra (`wood` 12 y 1250, `food` 9 y 386, `gold` 4 y 10000, `stone` 0 y 200, `population` 19 y 21/30, `idle` 3, reloj 00:12:34) con las plantillas de `mod_numbers.json` (dígitos), `mod_population.json` ("/") y `mod_clock.json` (reloj), cada glifo pintado blanco (tinta × 255) escalado al 0,73 × points × píxeles por unidad de alto, centrado en el campo; si falta una letra, el campo queda vacío (no es error).

- [ ] **Paso 1: Implementar `HudPreview.cs` y el comando.**

- [ ] **Paso 2: Correr**: `dotnet run --project tools/ReplaySession -- hud-preview <scratchpad>\hud-preview.png` → "Guardado … 1287×63 px" (a escala 1: 3004 × 148 unidades a 0,4286). Abrir el PNG con `Read` y comparar a ojo con `docs/superpowers/specs/2026-10-24-hud-layout-maqueta.png`: mismas proporciones, dígitos centrados, nada cortado.

- [ ] **Paso 3: Commit**: `git commit -m "ReplaySession hud-preview: el diseño del HUD como imagen"`.

---

### Tarea 5: `LayoutPanel`: el casillero y los campos dentro de una caja

**Files:**
- Create: `src/BoCoach.Vision/Hud/LayoutPanel.cs`
- Test: `tests/BoCoach.Vision.Tests/LayoutPanelTests.cs`

**Interfaces:**
- Consumes: `HudLayout.ChipRect`, `HudLayout.FieldRect`, `HudColor.Captured`, `ModPanel.Black` (internal, mismo ensamblado).
- Produces (`public static class LayoutPanel`):
  - `public static PixelRect? Chip(HudLayout layout, HudCell cell, CapturedImage box)`: el rectángulo del casillero si en `ChipRect` achicado 2 px por lado todos los píxeles de las columnas y filas del borde están a 45 o menos por canal del color del casillero (el medio puede estar tapado por la GD); si no, null. Para una celda sin casillero (`idle`) devuelve la caja entera.
  - `public static PixelRect Field(HudLayout layout, HudCell cell, string field, CapturedImage box, bool refine = true)`: `FieldRect` y, con `refine`, cada borde corrido hasta 3 px hacia adentro mientras la línea del borde no sea negra en el 90 % (`ModPanel.Black`), y hasta 3 px hacia afuera mientras la línea siguiente sí lo sea. El campo `total` de población se pide con `refine: false`.
  - `public static CapturedImage Crop(HudLayout layout, HudCell cell, string field, CapturedImage box)` = `box.Crop(Field(...))` (con `refine` false para `total`).

- [ ] **Paso 1: Tests que fallan** (caja sintética de 176×46 px = la celda de madera a 0,4286, dibujada con un helper `WoodBox(int shiftBlack = 0, bool chip = true, bool gdInside = false)`: fondo gris 26, casillero del color 135,92,41 en su `ChipRect`, fondo negro en el rectángulo de `backgrounds[0]` corrido `shiftBlack` px, y con `gdInside` un rectángulo negro en el medio del casillero dejando 2 px de borde):

```csharp
[Fact] public void Encuentra_el_casillero_por_su_color_aunque_la_GD_tape_el_medio()  // Chip(Layout, Wood, WoodBox(gdInside: true)) == ChipRect; Chip(Layout, Wood, WoodBox(chip: false)) == null
[Fact] public void La_celda_de_inactivos_devuelve_la_caja_entera()  // Chip(Layout, Layout.Cell("idle"), box) == (0,0,w,h)
[Fact] public void El_campo_se_afina_a_los_bordes_del_negro()  // Field("stock", WoodBox(shiftBlack: 2)) tiene X == FieldRect.X + 2; con shiftBlack 0, igual a FieldRect; con shiftBlack 6, se queda en ±3
```

- [ ] **Paso 2: Correr y ver que fallan**: `dotnet test tests/BoCoach.Vision.Tests --filter "FullyQualifiedName~LayoutPanelTests"`.

- [ ] **Paso 3: Implementar `LayoutPanel.cs`.**

- [ ] **Paso 4: Correr** → PASS. **Paso 5: Commit**: `git commit -m "LayoutPanel: casillero y campos de una caja según el diseño"`.

---

### Tarea 6: Calibración sola con el diseño

**Files:**
- Modify: `src/BoCoach.Vision/Regions/AutoCalibration.cs` (`Detect` con `HudLayout?`, `LeftPart` 0,8, nuevo camino `DetectLayout`, `AutoCalibrationResult` con `HudLayout? Hud`)
- Modify: `src/BoCoach.App/Capture/CaptureToolsViewModel.cs:197-224` (`TryAutoCalibrate` pasa `_layout()` y el estado dice el diseño), `src/BoCoach.App/Capture/DiagnosticCapture.cs:64-78` (idem), `tools/ReplaySession/Program.cs:549-560` (`autocalibrate` pasa `Layout(args)` si el archivo existe e imprime "diseño …")
- Test: `tests/BoCoach.Vision.Tests/AutoCalibrationTests.cs` (tests nuevos) y `tests/BoCoach.Vision.Tests/HudLayoutScreens.cs` (helper)

**Interfaces:**
- Consumes: `HudLayout` (Tarea 1), `RegionLayout.WithHud` (Tarea 2), `AutoCalibration.EvenRects`, `Extend`, `TextClusters` existentes.
- Produces: `public static AutoCalibrationResult? Detect(CapturedImage screen, RegionLayout reference, HudLayout? layout = null)` (con `layout`, primero el camino del diseño; si da null, el camino de hoy); `AutoCalibrationResult(RegionLayout Layout, double Scale, IReadOnlyList<PixelRect> Squares, PixelRect Clock, HudLayout? Hud = null)` (`Squares` = los cinco casilleros con el diseño; `Layout.Hud` = el diseño). Helper de tests `internal static CapturedImage HudLayoutScreens.Render(HudLayout layout, double pixelsPerUnit, int width = 1920, int height = 1080, int originX = 0, int originY = 0, bool gdInside = false)`: fondo verde 79,107,58, barra con fondo y borde, casilleros con su color (con `gdInside`, negro adentro dejando 2 px), fondos negros, cuadrados grises 70 por ícono, tres rayas blancas de 2 px dentro de cada campo (números cualquiera), el rectángulo negro del reloj en `ClockRect()`.

Camino del diseño: franja = 10 % de arriba × 80 % de la izquierda. Para cada `layout.Anchors`: los `EvenRects` cuyo color está a 45 o menos por canal de `chip.Color.Captured`; el más ancho; se le suman los del mismo ancho (±2) encima y debajo y se estira por las columnas de los bordes (`Extend`, como hoy); si falta uno → null. `factor` = mediana de (ancho medido / chip.Width); `originX` = mediana de (x medido − (cell.X + chip.X) × factor); `originY` = mediana de (y medido − chip.Y × factor); `scale` = factor / `PixelsPerUnitAt1080p`. Se rechaza (null) si algún casillero queda a más de round(3 × scale) px de donde el diseño lo pone o su ancho difiere más del 10 %. Cajas: para cada celda, `(originX + cell.X × factor, originY, cell.Width × factor, bar.Height × factor)` redondeados, salvo `age`, que usa `AgeTextRect()`; el reloj = `ClockRect()` por el factor, y después se busca desde x − 40 × scale hasta x + 40 × scale la primera columna con el 80 % de negro puro en las filas del reloj: si hay, la caja empieza ahí (mismo ancho); si no, queda la predicha. Cola, log y puntajes como hoy (sacar ese pedazo a un método privado compartido por los dos caminos). El `RegionLayout` sale con `WithHudScale(scale).WithHud(layout)`.

- [ ] **Paso 1: Tests que fallan** (en `AutoCalibrationTests`, `Layout` = el del repo):

```csharp
[Fact] public void Con_el_diseno_encuentra_los_cinco_casilleros_y_arma_las_cajas_desde_el_diseno()
{   var screen = HudLayoutScreens.Render(Layout, 0.4286, originX: 6, originY: 4);
    var found = AutoCalibration.Detect(screen, RegionLayout.Default, Layout);
    Assert.NotNull(found);
    Assert.Equal("bocoach-2026-10", found.Hud!.Name);
    Assert.Equal(5, found.Squares.Count);
    Assert.Equal(1.0, found.Scale, 0.03);
    AssertClose(new PixelRect(6 + 4, 4, 176, 46), found.Layout.PixelsOf(RegionNames.Wood, 1920, 1080), 2, "madera");
    AssertClose(new PixelRect(6 + 707, 4, 163, 46), found.Layout.PixelsOf(RegionNames.Population, 1920, 1080), 2, "población");
    AssertClose(new PixelRect(6 + 870, 4, 81, 46), found.Layout.PixelsOf(RegionNames.IdleVillagers, 1920, 1080), 2, "inactivos");
    AssertClose(new PixelRect(6 + 996, 4 + 12, 162, 22), found.Layout.PixelsOf(RegionNames.Age, 1920, 1080), 2, "edad");
    AssertClose(new PixelRect(6 + 1172, 4 + 5, 99, 36), found.Layout.PixelsOf(RegionNames.Clock, 1920, 1080), 3, "reloj");
    Assert.Equal("bocoach-2026-10", found.Layout.Hud!.Name); }

[Fact] public void A_escala_1_5_lo_mismo_al_1_5()  // Render(Layout, 0.6429, 2880, 1620): Scale ≈ 1.5 (±0.05), cajas = las de arriba × 1,5 a ±3 px

[Fact] public void Con_la_GD_dibujada_adentro_de_los_casilleros_encuentra_lo_mismo()  // gdInside: true → mismas cajas a ±2

[Fact] public void Sin_los_casilleros_del_diseno_cae_a_la_deteccion_del_mod_viejo()  // Composite(RecordedBoxes()) + Layout → found.Hud == null y 6 Squares, mismas cajas que el test de hoy

[Fact] public void Un_casillero_corrido_no_calibra()  // Render normal y después pintar el casillero de piedra 12 px más a la derecha (tapar el original con gris) → Detect == null (ni el camino viejo lo toma: no hay seis franjas)
```

- [ ] **Paso 2: Correr y ver que fallan**: `dotnet test tests/BoCoach.Vision.Tests --filter "FullyQualifiedName~AutoCalibrationTests"`.

- [ ] **Paso 3: Implementar** `DetectLayout`, el helper de tests y el cableado de `CaptureToolsViewModel` (estado: `$"Calibrada sola con el diseño {hud.Name} para {w}×{h}: 5 casilleros y el reloj"` + la escala si difiere), `DiagnosticCapture` (línea `Calibración sola: diseño …, 5 casilleros, HUD a escala …`) y `ReplaySession autocalibrate` (`--layout`, por defecto `data/hud_layout.json` si existe).

- [ ] **Paso 4: Correr**: el filtro → PASS; `dotnet test tests/BoCoach.Vision.Tests` y `tests/BoCoach.App.Tests` → PASS (los tests viejos de calibración siguen igual con `layout` null).

- [ ] **Paso 5: Commit**: `git commit -m "Calibración sola con el diseño: anclas en los cinco casilleros"`.

---

### Tarea 7: El lector lee cada campo por el diseño

**Files:**
- Modify: `src/BoCoach.Vision/Hud/HudReader.cs` (`HudFonts.HudNumbers`, `HudReading.LayoutName`, `Read(..., HudLayout?)`), `src/BoCoach.Vision/Hud/HudTracker.cs` (`HudState.LayoutName`), `src/BoCoach.Vision/Hud/ScreenReader.cs` (constructor con `HudLayout? layout = null`)
- Modify: `src/BoCoach.App/Coaching/FrameSources.cs` (`IFrameSource.Hud`), `src/BoCoach.App/Coaching/LiveCoach.cs:410-415` (`NewGame` pasa `Frames.Hud`; `CoachFrame.Notice`), `src/BoCoach.App/Overlay/OverlayViewModel.cs` (`WarningFor` devuelve `frame.Notice` si no hay otro aviso), `src/BoCoach.App/Capture/DiagnosticCapture.cs:84-86` (`Read(..., layout.Hud)` y una línea `Letra hud_numbers: presente|falta` después de la del mod), `tools/ReplaySession/Program.cs` (`Part` con `celda:campo`; `read`, `coach`, `replay` y `autocalibrate --read` pasan el diseño)
- Test: `tests/BoCoach.Vision.Tests/HudLayoutReaderTests.cs` (nuevo), `tests/BoCoach.Vision.Tests/HudTests.cs` (un test más)

**Interfaces:**
- Consumes: `LayoutPanel` (Tarea 5), `RecordedSession.Hud` (Tarea 2), `GlyphFont` y sus plantillas (para el helper de tests), `HudLayoutScreens` (Tarea 6).
- Produces: `HudFonts(..., GlyphFont? HudNumbers = null)` cargada de `hud_numbers.json` (opcional) en `HudFonts.Load`; `HudReading(..., string? LayoutName = null)`; `HudState(..., string? LayoutName = null)` (lo pasa `HudTracker.Update`); `HudReader.Read(Func<string, CapturedImage?> image, HudLayout? layout = null)` y `Read(RecordedSession session, int frame)` que pasa `session.Hud`; `ScreenReader(HudFonts fonts, OcrReader? ocr, EventCatalog catalog, HudLayout? layout = null)`; `IFrameSource.Hud` (`HudLayout?`, por defecto null; `ScreenFrames` → `layout.Hud`, `RecordedFrames` → `session.Hud`); `CoachFrame.Notice` (`string?`, por defecto null): en `LiveCoach`, `Frames.Hud is not null && _fonts.HudNumbers is null ? "Falta data/fonts/hud_numbers.json: con el diseño nuevo no se leen los números del HUD" : null`. Helper de tests `internal static CapturedImage HudLayoutScreens.Box(HudLayout layout, HudCell cell, double pixelsPerUnit, IReadOnlyDictionary<string, string> texts, GlyphFont font, double darken = 1)`: la celda sola como la grabaría el recorder (lo mismo que `Render` pero una celda, con los textos pintados con las plantillas de `font` a 0,73 × points × píxeles por unidad de alto, centrados; `darken` multiplica todos los canales, 0,2 = pausa).

Camino del diseño en `HudReader.Read`: solo si `layout` y `_hudNumbers` no son null. Por celda de recurso (`wood`…): `img = image(RegionOf)`; si `LayoutPanel.Chip(layout, cell, img)` es null → esa celda va por el camino de hoy; si no, `VillagersOn[r]` = `Parse(_hudNumbers.Read(Crop(villagers).Stretched(PauseGain)))`, el stock igual con `stock`, y `bands[region]` = el casillero. Población: `Villagers` = `total` (sin afinar), `Population`/`PopulationCap` = `cap` con `PopulationText` (como hoy con `_modPopulation`). Inactivos: `count`; `bands[idle]` = la caja entera. El reloj no cambia. `LayoutName` = `layout.Name` si al menos una celda se leyó por el diseño. `Clipped` como hoy (`GlyphReading.Clipped`).

- [ ] **Paso 1: Tests que fallan** (`HudLayoutReaderTests`, con `Fonts` = `HudFonts.Load(fonts)` con `HudNumbers` = `mod_numbers.json` y `ModPopulation` para el "/": `fonts with { HudNumbers = GlyphFont.Load("mod_numbers.json") }`; las plantillas son la misma letra del juego a 48 pt, así que lo que se pinta con ellas se tiene que leer):

```csharp
[Fact] public void Lee_los_campos_de_cada_celda_con_la_letra_del_mod()
{   var boxes = Boxes(wood: ("12", "1250"), food: ("9", "386"), gold: ("4", "9999"), stone: ("0", "200"), population: ("19", "21/30"), idle: "3");
    var reading = new HudReader(Fonts).Read(boxes.GetValueOrDefault, Layout);
    Assert.Equal("bocoach-2026-10", reading.LayoutName);
    Assert.Equal(1250, reading.Wood); Assert.Equal(12, reading.VillagersOn![Resource.Wood]);
    Assert.Equal(386, reading.Food); Assert.Equal(9999, reading.Gold); Assert.Equal(200, reading.Stone);
    Assert.Equal(19, reading.Villagers); Assert.Equal((21, 30), (reading.Population, reading.PopulationCap));
    Assert.Equal(3, reading.IdleVillagers);
    Assert.Equal(Layout.ChipRect(Layout.Cell("wood"), new PixelRect(0, 0, 176, 46)), reading.ModBands![RegionNames.Wood]);
    Assert.Equal(new PixelRect(0, 0, 81, 46), reading.ModBands[RegionNames.IdleVillagers]);
    Assert.Empty(reading.Clipped); }

[Fact] public void Lee_cinco_cifras_de_stock_y_la_poblacion_al_tope()  // gold ("60", "10000"), population ("150", "200/200") → 10000, 60, 150, (200, 200)
[Fact] public void En_pausa_lee_igual()  // Boxes(..., darken: 0.2) → mismos valores
[Fact] public void Sin_el_casillero_en_su_lugar_cae_al_camino_viejo()  // la caja de madera con gris liso donde iba el casillero: Wood == null y VillagersOn sin Wood (el camino viejo no encuentra un cuadrado negro con franja), Food == 386 y LayoutName == "bocoach-2026-10" (las demás celdas sí fueron por el diseño)
[Fact] public void Sin_la_letra_hud_numbers_no_usa_el_diseno()  // Fonts sin HudNumbers → LayoutName null
// HudTests (fixture rec-20261015-205040, mod viejo)
[Fact] public void Una_grabacion_del_mod_viejo_se_lee_igual_con_el_diseno()  // Reader.Read(image, Layout) da el mismo HudReading que Reader.Read(image) en las capturas del golden, y LayoutName == null
```

- [ ] **Paso 2: Correr y ver que fallan**: `dotnet test tests/BoCoach.Vision.Tests --filter "FullyQualifiedName~HudLayoutReaderTests|FullyQualifiedName~HudTests"`.

- [ ] **Paso 3: Implementar** el camino del diseño en `HudReader`, `HudFonts`, `HudReading`, `HudState`, `ScreenReader`, y el cableado en App y ReplaySession (`Part`: `"wood:stock"` con `session.Hud` → `LayoutPanel.Crop`; sin diseño, `square`/`stock` como hoy; otra parte → `ArgumentException` que nombra los campos válidos).

- [ ] **Paso 4: Correr**: `dotnet test BoCoach.slnx` → PASS (todo lo viejo igual).

- [ ] **Paso 5: Commit**: `git commit -m "El lector lee cada campo del HUD por el diseño"`.

---

### Tarea 8: El overlay dibuja en el casillero y enmarca los inactivos

**Files:**
- Modify: `src/BoCoach.App/Overlay/ResourceTargetViewModel.cs` (`[ObservableProperty] public partial bool Chip { get; set; }`: "la franja es un casillero del diseño nuevo"), `src/BoCoach.App/Overlay/OverlayViewModel.cs:102-110` (`target.Chip = frame.Hud?.LayoutName is not null`, `IdleAlert.Chip` igual), `src/BoCoach.App/Overlay/ResourceOverlay.cs` (`Position`, `PositionIdle`, `RefreshIdle`, `OnTargetChanged`/`OnIdleChanged` reaccionan a `Chip`), `src/BoCoach.App/App.xaml.cs:226-236` (`--bands` arma el caso nuevo)

**Interfaces:**
- Consumes: `HudState.LayoutName` (Tarea 7).
- Produces: con `Chip`, la GD va dentro de `Band` dejando un marco `max(2, band.Height / 18)` px (en píxeles físicos, antes de dividir por la escala de WPF), fondo negro opaco, `CornerRadius` 2, texto de alto `0,7 ×` el interior, color `BrushFor(match)`; el aviso de inactivos con `Chip` es un `Border` sin fondo, `BorderThickness` 3 (físicos), `BorderBrush` que alterna `ShortBrush`/`OverBrush`, sobre `Band` entera. Sin `Chip`, todo como hoy. En `--overlay-screenshot --bands`: `target.Band = new PixelRect(45, 5, 30, 36)` y `Chip = true` para los recursos; `IdleAlert.Band = new PixelRect(0, 0, 81, 46)`, `Chip = true`.

- [ ] **Paso 1: Implementar** los cambios (no hay tests de WPF: la verificación es por captura).

- [ ] **Paso 2: Verificar**: `dotnet build src/BoCoach.App -c Release` y `src\BoCoach.App\bin\Release\net10.0-windows10.0.19041.0\BoCoach.exe --overlay-screenshot <scratchpad>\overlay.png --readings tests/fixtures/rec-20261015-214512/lecturas.csv --bo scouts --at 8:20 --bands` → abrir `<scratchpad>\overlay-recursos.png` con `Read`: cuatro números dentro de un casillero con marco de color y fondo negro, y un marco rojo alrededor de la celda de inactivos. (Cuidado: si la app está abierta, Debug queda bloqueado; por eso Release.)

- [ ] **Paso 3: Commit**: `git commit -m "Overlay: la GD dentro del casillero del diseño y marco en inactivos"`.

---

### Tarea 9: `ReplaySession glyphs` por campo, docs y versión para que el usuario pruebe

**Files:**
- Modify: `tools/ReplaySession/Program.cs:60-64` (comentario de uso: `celda:campo`), `tools/mod/instalar-mod.ps1:1-5` (nota: "Desde la v0.9.34 el mod lo arma la app desde data/hud_layout.json; este script arma el diseño viejo (cuadrados con franja) y queda de respaldo")
- Modify: `docs/pendientes-usuario.md` (ítem nuevo al principio, ver abajo), `src/BoCoach.App/BoCoach.App.csproj:12` (`<Version>0.9.34</Version>`), `docs/LEEME-portable.txt` si describe el mod viejo (franjas de color)

**Interfaces:**
- Consumes: todo lo anterior.

Ítem para `docs/pendientes-usuario.md` (numerarlo primero, "0" o el que siga a la numeración de arriba):
> **Probar el HUD nuevo y grabar (v0.9.34).** (1) Actualizá la app y tocá "Instalar mod del HUD" (reemplaza el mod viejo). (2) Abrí el juego, entrá a Mods y fijate que "BoCoach HUD" esté activo y primero. (3) Empezá una escaramuza contra la IA con el coach andando: la barra de estado tiene que decir "Calibrada sola con el diseño bocoach-2026-10". (4) Grabá 2 o 3 minutos con Ctrl+Alt+R moviendo aldeanos por madera, comida, oro y piedra, dejando alguno inactivo y haciendo alguna casa. (5) Ctrl+Alt+X una vez con el panel a la vista. (6) Mirá y contame: ¿se ve el "/" entre aldeanos y stock? ¿la barra tapa la cola de producción de arriba a la izquierda? ¿el reloj quedó pegado al final de la barra o lejos? ¿aparecen los tooltips al pasar el mouse por cada recurso? Mandame la carpeta de la grabación (`%LOCALAPPDATA%\BoCoach\recordings\<fecha>`) y la de diagnóstico. Con eso armo la letra `hud_numbers`, el golden y los tests de lectura. Hasta entonces el coach no lee los números con el mod nuevo (el panel "Lo que ve el coach" lo avisa).

- [ ] **Paso 1: Hacer los cambios de docs y la nota del script.** Commit: `git commit -m "Pendientes: probar el HUD nuevo y grabar; nota en instalar-mod.ps1"`.

- [ ] **Paso 2: Versión** siguiendo la skill `nueva-version` (commit "v0.9.34: HUD propio descripto en data/hud_layout.json", `dotnet build src/BoCoach.App -c Release`, verificar que `bin/Release/.../data/hud_layout.json` existe, `dotnet test BoCoach.slnx -c Release` en verde, push de la rama) y dejarle al usuario las instrucciones del ítem de pendientes en el chat.

---

### Tarea 10: Con la grabación del usuario: letra `hud_numbers`, golden y tests de lectura

Espera la grabación y la captura de diagnóstico del usuario (Tarea 9). Antes de empezar, leer la skill `armar-letras-e-iconos` y `docs/superpowers/specs/2026-10-24-hud-layout-design.md` sección 8.

**Files:**
- Create: `data/fonts/hud_numbers.json`
- Create: `tests/fixtures/hud-<fecha>/` (`session.json` de la grabación, `golden.csv` con columnas `frame,clock,wood,wood_villagers,food,food_villagers,gold,gold_villagers,stone,stone_villagers,villagers,population,idle`, y las 8 regiones de cada captura elegida: 20 capturas al azar, valores leídos a ojo de las imágenes) y `tests/fixtures/screens/hud-layout-<fecha>-1080p.png` (la captura de diagnóstico con todo en negro menos el panel, como `carga-ia-en-1080p.png`)
- Modify: `tests/BoCoach.Vision.Tests/BoCoach.Vision.Tests.csproj` (ya copia `fixtures/**`), `tests/BoCoach.Vision.Tests/HudLayoutReaderTests.cs` (tests golden), `tests/BoCoach.Vision.Tests/AutoCalibrationTests.cs` (test sobre la pantalla real)

- [ ] **Paso 1: Mirar la grabación**: `dotnet run --project tools/ReplaySession -- autocalibrate <diagnóstico>.png --read` → "diseño bocoach-2026-10", 5 casilleros, escala ≈ 1. Si no calibra, revisar los candidatos que imprime contra los colores del diseño antes de seguir (el juego puede atenuar los colores lisos; ajustar el JSON y volver a instalar el mod es parte de esta tarea).

- [ ] **Paso 2: Armar la letra**: `glyphs "<grabación>" wood:stock,food:stock,gold:stock,stone:stock,wood:villagers,food:villagers,gold:villagers,stone:villagers,population:total,population:cap,idle:count white-on-black <carpeta>` → etiquetar `clusters*.png` con el golden (dígitos 0 a 9 y "/") → `font <carpeta> "<etiquetas>" hud_numbers white-on-black data/fonts/hud_numbers.json`.

- [ ] **Paso 3: Tests golden** (misma forma que `HudTests.Lee_todo_el_HUD_de_una_captura_y_avisa_las_cajas_que_cortan_el_numero`, sobre `hud-<fecha>`):

```csharp
[Fact] public void Lee_el_100_por_ciento_del_golden_de_la_grabacion_con_el_diseno()  // por captura: reloj, 4 stocks, 4 aldeanos por recurso, total, N/M e inactivos iguales al golden; LayoutName == "bocoach-2026-10"
[Fact] public void Calibra_sola_la_pantalla_real_con_el_diseno()  // Detect(Png.Read(screens/hud-layout-<fecha>-1080p.png), Default, Layout): Hud no null, 5 Squares, Scale entre 0,95 y 1,05, y la lectura de las cajas (--read, como en DiagnosticCapture) da los valores de esa captura (anotarlos en el test)
```

- [ ] **Paso 4: Correr** `dotnet test BoCoach.slnx` → PASS; `ReplaySession coach <grabación> scouts --guide` y mirar que los pasos de aldeanos avancen con el HUD.

- [ ] **Paso 5: Commits** separados: la letra ("Letra hud_numbers del HUD nuevo"), los fixtures ("Fixture hud-<fecha> y pantalla del HUD nuevo"), los tests ("Tests golden del lector con el diseño").

- [ ] **Paso 6: Docs al cierre**: `CLAUDE.md` (estado actual: el mod nuevo, `data/hud_layout.json`, qué hace cada pieza, lo verificado y lo que no), `PLAN.md` D9 (hecho, con fecha y resultado), tachar el ítem en `docs/pendientes-usuario.md`, versión siguiente por la skill `nueva-version`. Commit por archivo.

---

## Self-review (hecho al escribir el plan)

- **Cobertura del spec:** sección 2 y 3 → Tarea 1; sección 4 → Tareas 3 y 4; sección 5 → Tarea 6; sección 6 → Tareas 5 y 7; sección 7 → Tarea 8; sección 8 → Tareas 7 (sintético), 9 (`glyphs`) y 10 (letra, fixtures, golden); sección 9 → Tareas 9 y 10; sección 10 (riesgos) → ítem de pendientes de la Tarea 9 y paso 1 de la Tarea 10; sección 11 → nada que hacer.
- **Nombres:** `HudLayout.Cell/Cells/Anchors/CellRect/AgeTextRect/ClockRect/FieldRect/ChipRect/RegionOf/CellOf`, `LayoutPanel.Chip/Field/Crop`, `RegionLayout.Hud/WithHud`, `RecordedSession.Hud`, `HudFonts.HudNumbers`, `HudReading.LayoutName`, `HudState.LayoutName`, `IFrameSource.Hud`, `CoachFrame.Notice`, `ResourceTargetViewModel.Chip`, `AutoCalibrationResult.Hud`, `HudLayoutScreens.Render/Box`: usados con el mismo nombre en todas las tareas.
- **Proporción:** el plan decide archivos, firmas, valores y tests; los cuerpos quedan para el implementador salvo las reglas del generador y del detector, que el spec fija.
