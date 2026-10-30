# Coach por líneas paralelas: plan de implementación

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** El coach deja de seguir la BO como una sola fila y muestra cuatro renglones (Aldeanos, Construir, Investigar, Producir) que avanzan cada uno por su lado, sin mostrar nunca algo de otra edad ni sin su edificio.

**Architecture:** Las líneas se deducen de la acción de cada paso (`Lanes.Of`). El `Tracker` calcula la elegibilidad de cada paso (edad, edificio, dependencias) y el primer paso elegible de cada línea; el `Coach` arma un `LaneView` por línea con lo que falta o con el fallback (GD, casa, tecnologías, postura); `CoachText` y el overlay los dibujan en renglones fijos. La lectura de la partida y el `Planner` no cambian.

**Tech Stack:** C# .NET 10, xUnit, WPF (overlay y panel de la app).

**Spec:** `docs/superpowers/specs/2026-10-30-lineas-paralelas-design.md`

## Global Constraints

- `BoCoach.Core` sigue sin dependencias de Windows (`ArquitecturaTests`).
- El formato de la BO, el `Planner`, la web y el editor no cambian.
- Textos en castellano rioplatense, sin guiones largos (—), con comas.
- `Directory.Build.props` trata los warnings como errores.
- Un commit por cambio, mensaje en castellano, terminado en `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- Tests: `dotnet test BoCoach.slnx -c Release` (si la app está abierta, el `.exe` de Release queda bloqueado: cerrarla).

## Review Focus

- BO que arranca con un paso de una edad posterior en una línea (por ejemplo, Construir empieza con la galería): el renglón no puede quedar vacío para siempre ni mostrar la galería en la Edad Oscura; muestra el fallback hasta llegar a Feudal. Test en la Tarea 1.
- Clic de edad hecho y edad todavía en curso: lo de la edad siguiente sigue oculto hasta que llega el "<edad> Researched" (o el paso queda hecho). Test en la Tarea 1.
- Edificio que el log no avisó pero la BO ya dio por hecho (paso de Construir hecho por un paso posterior o a mano): cuenta como construido para la elegibilidad. Test en la Tarea 1.
- Civ sin el edificio que pide un paso (incas sin campamentos, con asentamiento): el `counts_as` del asentamiento cuenta como el edificio (los eventos ya llegan por `SnapshotBuilder`). Test en la Tarea 1.
- Todas las líneas sin nada (BO terminada, sin casa, sin tecnologías ni postura): el panel no queda con renglones vacíos con etiqueta. Test en la Tarea 3.

---

### Task 1: Líneas y elegibilidad en el Tracker

**Files:**
- Create: `src/BoCoach.Core/Engine/Lanes.cs`
- Modify: `src/BoCoach.Core/Engine/Tracker.cs`
- Test: `tests/BoCoach.Core.Tests/Engine/TrackerLanesTests.cs`

**Interfaces:**
- Produces:
  - `public enum Lane { Villagers, Build, Research, Produce }` (en ese orden: es el orden de los renglones).
  - `public static class Lanes { public static Lane Of(Step step); public static IReadOnlyList<Lane> All { get; } }`
    - `Train` de `villager` y `Move` → `Villagers`; `Build` → `Build`; `Research` → `Research`; `Train` de otra unidad → `Produce`.
  - `Tracker.Age` (`Age`): la edad a la que llegó el jugador: la mayor entre `Age.Dark`, `BuildOrderValidator.AgeOf` de cada paso de investigación hecho (`StepState.Done`) y de cada evento `<tech>_researched` del estado cuyo `AgeOf(tech)` no es null. Un paso en curso (`InProgress`) no cuenta.
  - `Tracker.Built(string building)` (`bool`): `building == BuildOrderValidator.StartingBuilding`, o `State.Count($"{building}_built") > 0`, o algún paso `Build` de ese edificio hecho.
  - `Tracker.IsEligible(int index)` (`bool`), con las reglas de la sección 2 del spec:
    1. edad requerida ≤ `Age`. La requerida es `entity.Age` (game_data) para todo paso con entidad, salvo un clic de edad: la anterior a `AgeOf(what)`. `Move` y pasos sin entidad: `Age.Dark`.
    2. `Train`/`Research`: `Built(entity.MadeIn)` si tiene `MadeIn`. `Build`: `Built(civ.Requires(entity))` si no es null (`civ = _data.Civ(_bo.Civ)`).
    3. `After` hechos, `AtPop` y `AtTime` cumplidos (lo que hoy mira `IsReady`).
    4. Clic de edad: los edificios de `BuildOrderValidator.AgeUpRequirement(what)` (2 de la lista, o uno de `AgeUpAloneBuilding`, como en `Posture`).
  - `Tracker.LaneStep(Lane lane)` (`StepProgress?`): el primer paso abierto (`Pending`/`Ready`) de esa línea, en orden de la BO, que es elegible y no es producción en marcha ni granja suelta (las mismas exclusiones que hoy `Current`).
  - `Tracker.Current` pasa a ser el primer paso abierto elegible (mismas exclusiones): arregla los dos casos de la 20261029-220928 mientras el resto de la app sigue usando `Current`.
  - `StepState.Ready` además pide que el paso sea elegible.

- [ ] **Step 1: Escribir los tests que fallan** en `TrackerLanesTests.cs`, con una BO armada en el test (pasos con `Id`, `Do`, `What`, `Count`, `Assign`) y `Partida`:
  - `Lanes_Of_reparte_cada_accion`: `Assert.Equal(Lane.Villagers, Lanes.Of(train villager))`, `Move` → `Villagers`, `Build house` → `Build`, `Research loom` → `Research`, `Train archer` → `Produce`.
  - `Con_la_Feudal_en_curso_no_muestra_el_hacha`: BO = aldeanos a madera ×3, `Research feudal_age`, `Research double_bit_axe`, aldeanos a madera ×3. Tras jugar los aldeanos y `tracker.MarkStarted(<feudal>, t)`: `LaneStep(Research)` es null (no el hacha), `IsEligible(<hacha>)` false, `LaneStep(Villagers)` es el segundo paso de aldeanos. Tras `partida.Event("feudal_age_researched")`: `Age == Age.Feudal` y `LaneStep(Research)` es el hacha.
  - `Sin_herreria_no_muestra_el_emplumado`: BO en Feudal (eventos `feudal_age_researched`) con `Research fletching` y después `Build blacksmith`: `LaneStep(Research)` null y `LaneStep(Build)` la herrería; tras `blacksmith_built`, `LaneStep(Research)` es el emplumado.
  - `Una_linea_que_arranca_con_algo_de_otra_edad_queda_vacia_hasta_llegar`: BO con `Build archery_range` como único paso de Construir: en la Edad Oscura `LaneStep(Build)` null; con `feudal_age_researched`, la galería.
  - `Un_edificio_que_la_BO_dio_por_hecho_cuenta_como_construido`: `Build blacksmith` marcado con `tracker.Advance()` (sin evento) → `Built("blacksmith")` true y el emplumado elegible.
  - `El_asentamiento_cuenta_como_campamento`: con `PlayerState` que tiene `lumber_camp_built` emitido por el asentamiento (como lo emite `SnapshotBuilder`), `Built("lumber_camp")` true.
  - `Current_saltea_lo_que_no_es_elegible`: en el caso del hacha, `Current` no es el hacha.

- [ ] **Step 2: Correr y ver que fallan**

Run: `dotnet test tests/BoCoach.Core.Tests -c Release --filter "FullyQualifiedName~TrackerLanesTests"`
Expected: no compila (`Lane`, `Lanes`, `LaneStep` no existen).

- [ ] **Step 3: Implementar** `Lanes.cs` y, en `Tracker`, `Age`, `Built`, `IsEligible`, `LaneStep`, y el cambio de `Current` y de `IsReady`. La elegibilidad usa el estado de los pasos ya calculado en `Recompute` (se evalúa después del punto 3 o con `doneAt`/`_manualDone`), así que `Age` y `Built` se calculan dentro de `Recompute` antes de asignar `Ready`.

- [ ] **Step 4: Correr los tests nuevos y la suite completa**

Run: `dotnet test BoCoach.slnx -c Release`
Expected: todo en verde. Si un test viejo de `TrackerTests`/`CoachTests` esperaba como `Current` un paso que ahora no es elegible, revisar que el nuevo comportamiento sea el del spec y corregir la expectativa con un comentario que lo diga.

- [ ] **Step 5: Commit**: `Tracker: líneas y elegibilidad por edad y edificio`.

---

### Task 2: Cada línea avanza sola (asignación y fin de "Atrasado")

**Files:**
- Modify: `src/BoCoach.Core/Engine/Tracker.cs`
- Test: `tests/BoCoach.Core.Tests/Engine/TrackerLanesTests.cs`

**Interfaces:**
- Consumes: `Lanes.Of`, `Tracker.IsEligible` (Tarea 1).
- Produces:
  - `StepState.Late` deja de asignarse (el valor del enum queda hasta la Tarea 6 para no romper consumidores; `Tracker.Late` devuelve vacío).
  - Paso 2 de `Recompute` (pasos que el log no confirma): solo cuentan los pasos posteriores **de la misma línea** (`Lanes.Of`). Las granjas de un paso mixto siguen con sus compañeras.
  - Los eventos se siguen asignando al primer paso abierto que los espera en orden de la BO (eso ya es "primer paso abierto de esa línea con ese evento").

- [ ] **Step 1: Tests que fallan**
  - `El_hacha_esperando_madera_no_frena_a_los_aldeanos`: BO = aldeanos a madera ×2, `Research double_bit_axe`, aldeanos a oro ×2 (en Feudal, con `lumber_camp_built`). Con stock sin madera suficiente y 4 `villager_created`: el paso del hacha está `Pending` (no `Late`), los dos pasos de aldeanos `Done`, y `LaneStep(Villagers)` null (no quedan) mientras `LaneStep(Research)` es el hacha.
  - `Un_edificio_hecho_antes_marca_su_paso_y_la_linea_sigue_en_el_primero`: BO con `Build house`, `Build lumber_camp`, `Build mill`; evento `mill_built` solo: el molino `Done`, la casa y el campamento abiertos (no `Late`), `LaneStep(Build)` es la casa.
  - `Una_granja_suelta_queda_hecha_por_un_paso_posterior_de_Construir`: `Build farm` seguido de `Research loom` y `Build house`: con `loom_researched` la granja sigue abierta; con `house_built`, hecha.
  - `Paso_mixto_partido`: grupo con aldeanos a bayas + `Build mill`: el aldeano en `LaneStep(Villagers)` y el molino en `LaneStep(Build)`, cada uno se cierra con su evento.

- [ ] **Step 2: Correr y ver que fallan** (`--filter "FullyQualifiedName~TrackerLanesTests"`).

- [ ] **Step 3: Implementar** el cambio del paso 2 y sacar la asignación de `Late` en el paso 3 de `Recompute` (y `Skips`, que queda sin uso).

- [ ] **Step 4: Suite completa** en verde. Los tests que esperaban `Late` (`TrackerTests`, `CoachTests`, `TrackerGroupTests`) pasan a esperar el paso abierto en su línea; cada cambio con un comentario que cite el spec.

- [ ] **Step 5: Commit**: `Tracker: cada línea avanza sola, sin pasos atrasados`.

---

### Task 3: Los cuatro renglones en la vista del coach

**Files:**
- Modify: `src/BoCoach.Core/Engine/Coach.cs` (`CoachView`, `Update`, `StepTarget`, `NowTarget`)
- Test: `tests/BoCoach.Core.Tests/Engine/CoachLanesTests.cs`

**Interfaces:**
- Consumes: `Tracker.LaneStep`, `Lanes.All` (Tarea 1).
- Produces:
  - `public sealed record LaneView(Lane Lane, StepProgress? Step, ResourceAmounts Missing, double? Eta)`: `Step` null = fallback. `Missing` y `Eta` como hoy `CoachView.Missing`/`CurrentEta` pero para ese paso.
  - `CoachView.Lanes` (`IReadOnlyList<LaneView>`, una por línea en el orden de `Lanes.All`).
  - `CoachView.VillagerGuide` (`Resource?`): con `Lanes[Villagers].Step` null y GD leída, el recurso con mayor `Distribution - Actual` (positivo); null si no falta ninguno.
  - `StepTarget` y `NowTarget` salen del paso de `LaneStep(Villagers)` (antes `Tracker.Current`); con el fallback, null.
  - Abandonada (`CoachView.Abandoned` no null): todas las `LaneView` con `Step` null.

- [ ] **Step 1: Tests que fallan** (con `Coach` y `Partida`, como `CoachTests`):
  - `Hay_un_renglon_por_linea_en_orden`: `view.Lanes.Select(l => l.Lane)` igual a `[Villagers, Build, Research, Produce]`.
  - `El_hacha_sin_madera_muestra_lo_que_falta_y_los_aldeanos_siguen`: `Lanes[Research].Step` el hacha con `Missing.Wood > 0`, `Lanes[Villagers].Step` el próximo paso de aldeanos.
  - `Sin_pasos_de_aldeanos_en_la_edad_la_linea_va_a_la_GD`: con la GD leída (`Hud`) y el paso de aldeanos siguiente de Castillos: `Lanes[Villagers].Step` null, `VillagerGuide` el recurso que falta, `StepTarget` null.
  - `Abandonada_todas_las_lineas_van_al_fallback`.
  - `Al_terminar_sale_del_paso_de_aldeanos`: `StepTarget` igual a lo que pide la BO al terminar `LaneStep(Villagers)`, aunque haya un paso de construir antes sin hacer.

- [ ] **Step 2: Correr y ver que fallan.**

- [ ] **Step 3: Implementar** en `Coach`. `Missing` por paso: la misma cuenta que hoy hace `Update` para el paso actual (costo de la civ en `Tracker.Age` menos stock, sin negativos); `Eta` del plan como hoy `CurrentEta` para ese índice. Las propiedades viejas (`Current`, `Next`, `Late`, `Goals`, `Missing`, `CurrentEta`) siguen llenándose hasta la Tarea 6.

- [ ] **Step 4: Suite completa** en verde.

- [ ] **Step 5: Commit**: `Coach: un renglón por línea en la vista`.

---

### Task 4: Textos y overlay con renglones fijos

**Files:**
- Modify: `src/BoCoach.Core/Engine/CoachText.cs`
- Modify: `src/BoCoach.App/Overlay/OverlayViewModel.cs` y su XAML (`src/BoCoach.App/Overlay/CoachOverlay.xaml`)
- Create: `tests/BoCoach.Core.Tests/Engine/CoachTextLanesTests.cs`
- Test: `tests/BoCoach.App.Tests` (el de `OverlayViewModel`)

**Interfaces:**
- Consumes: `CoachView.Lanes`, `CoachView.VillagerGuide`, `NeedsHouse`, `Techs`, `Posture` (Tarea 3).
- Produces:
  - `public enum LaneTone { Now, Waiting, Fallback }` (blanco, amarillo, gris).
  - `public sealed record LaneLine(Lane Lane, string Label, string Text, LaneTone Tone)`.
  - `CoachText.LaneLines(CoachView view, GameData data)` (`IReadOnlyList<LaneLine>`): solo las líneas con algo que decir. Etiquetas: "Aldeanos", "Construir", "Investigar", "Producir". Texto del paso con `StepText` como hoy; `Waiting` si `Missing` no es cero, con "faltan N de <recurso> (m:ss)" como el texto actual de faltantes. Fallbacks: Aldeanos "Aldeano → <recurso>, según lo que falta" (con `VillagerGuide`); Construir "Casa: quedan N lugares" si `NeedsHouse`; Investigar la primera de `Techs`; Producir el primer edificio parado de `Posture`. Sin nada, la línea no va.
  - El overlay reemplaza "paso actual / después / atrasado / hacé también / en curso" por los renglones de `LaneLines`; arriba siguen avisos, rama y señales; abajo los repartos ("Al terminar", "Ahora").
  - Con `CoachView.Compact`, `CoachLines.Card` usa las mismas `LaneLines` en vez de armar su lista aparte.

- [ ] **Step 1: Tests que fallan**:
  - `LaneLines_con_el_hacha_esperando`: un renglón "Investigar" con `Tone == Waiting` y texto que contiene "faltan" y "madera".
  - `LaneLines_fallback_de_aldeanos`: "Aldeanos" con `Tone == Fallback` y el recurso de `VillagerGuide`.
  - `LaneLines_sin_nada_no_devuelve_renglones`: BO terminada, sin casa, sin techs ni postura → vacío.
  - En App.Tests: el `OverlayViewModel` expone los renglones en orden y oculta los vacíos.
- [ ] **Step 2: Correr y ver que fallan.**
- [ ] **Step 3: Implementar** `LaneLines`, el `OverlayViewModel` y el XAML (una fila por renglón: etiqueta gris a la izquierda, texto con el color del tono).
- [ ] **Step 4: Suite completa** en verde, y `BoCoach.exe --overlay-screenshot <scratchpad>/lineas.png --readings tests/fixtures/rec-20261015-214512/lecturas.csv --bo scouts --at 8:20` para mirar la captura.
- [ ] **Step 5: Commit**: `Overlay: un renglón fijo por línea`.

---

### Task 5: Panel "Lo que ve el coach", botones y atajos por línea

**Files:**
- Modify: `src/BoCoach.Core/Engine/Tracker.cs` (`Advance`, `Skip`)
- Modify: `src/BoCoach.Core/Engine/Coach.cs` (`Advance`, `Skip`)
- Modify: `src/BoCoach.App/Coaching/LiveReadingViewModel.cs`, `CoachSessionViewModel` y la vista del panel
- Test: `tests/BoCoach.Core.Tests/Engine/TrackerLanesTests.cs`, `tests/BoCoach.App.Tests/CoachSessionTests.cs`

**Interfaces:**
- Produces:
  - `Tracker.Advance(Lane lane = Lane.Villagers)` y `Tracker.Skip(Lane lane = Lane.Villagers)`: actúan sobre `LaneStep(lane)`; si es null, no hacen nada. `Back()` no cambia (deshace lo último de cualquier línea).
  - `Coach.Advance(Lane lane = Lane.Villagers)`, `Coach.Skip(Lane lane = Lane.Villagers)`; con rama activa siguen cerrando la rama.
  - Panel: un selector de línea (por defecto Aldeanos) junto a "Ya está ▶" y "Saltear"; los atajos Ctrl+Alt+→/↓ usan Aldeanos. El panel muestra los cuatro renglones como el overlay.

- [ ] **Step 1: Tests que fallan**: `Advance_de_Investigar_da_por_hecho_el_paso_de_esa_linea` (y no toca Aldeanos); en App.Tests, el botón con la línea Construir elegida marca el paso de Construir.
- [ ] **Step 2: Correr y ver que fallan.**
- [ ] **Step 3: Implementar.**
- [ ] **Step 4: Suite completa** en verde.
- [ ] **Step 5: Commit**: `Panel: Ya está y Saltear por línea`.

---

### Task 6: Sacar lo viejo y verificar contra las grabaciones

**Files:**
- Modify: `src/BoCoach.Core/Engine/Tracker.cs`, `Coach.cs`, `CoachText.cs`, `Branches.cs`, `src/BoCoach.App/Overlay/*`, `tools/ReplaySession/Program.cs`
- Modify: tests que usen lo que se saca
- Modify: `CLAUDE.md` (estado de la fase 9), `PLAN.md` sección 11 (anotar el Planner en paralelo como pendiente)

**Interfaces:**
- Se van: `StepState.Late`, `Tracker.Late`, `Tracker.Next`, `Tracker.Silent`, `Tracker.CurrentGoals`, `CoachView.Next`, `CoachView.Late`, `CoachView.Goals`, `CoachView.Missing`, `CoachView.CurrentEta`.
- `Tracker.Current` queda solo como "algún renglón con paso" para `Coach.Finished`, `Branches` y `PostureView` (renombrado a `Tracker.HasOpenSteps` si no se usa el paso). Los consumidores se buscan con `grep -rn "\.Current\b\|\.Next\b\|\.Late\b\|CurrentGoals\|\.Silent\b" src tools`.
- `ReplaySession coach --guide` imprime los cuatro renglones (`CoachText.LaneLines`) en vez de paso actual / después / atrasado.

- [ ] **Step 1: Sacar lo listado** y ajustar consumidores y tests.
- [ ] **Step 2: Suite completa** en verde.
- [ ] **Step 3: Verificar contra grabaciones** con la versión anterior (`git stash` o el commit previo a la Tarea 1) y la nueva:

```bash
dotnet run -c Release --project tools/ReplaySession -- coach "$LOCALAPPDATA/BoCoach/recordings/20261029-220928" "$APPDATA/BoCoach/build_orders/bog-britons-mejorada.json" --guide --civ britons
dotnet run -c Release --project tools/ReplaySession -- coach tests/fixtures/rec-20261015-214512/lecturas.csv build_orders/scouts_22p.json --guide
dotnet run -c Release --project tools/ReplaySession -- coach "$LOCALAPPDATA/BoCoach/recordings/20261018-132830" build_orders/archers_feudal.json --guide
```

Expected: en la 20261029-220928, entre 9:38 y 10:31 Investigar no muestra el hacha y entre 12:56 y 16:42 no muestra el emplumado; ningún renglón muestra algo de una edad que no llegó. Los pasos se cierran en los mismos minutos que antes (salvo los que antes quedaban "atrasados"). Anotar el antes y el después con los minutos.
- [ ] **Step 4: Commits** separados: el código (`Coach: se van paso actual, siguiente y atrasados`) y las notas (`Docs: coach por líneas paralelas`).
- [ ] **Step 5:** seguir con la skill `nueva-version`.
