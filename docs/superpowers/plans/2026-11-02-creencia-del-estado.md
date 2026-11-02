# La creencia del estado (escalón 2) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** una sola capa `StateBelief` en el Core que fusiona HUD, log, cola y puntaje con una fuerza por fuente y reemplaza a `ScoreLedger`, `SnapshotBuilder.BuildingsFromQueue`/`BuildingsFromProduction`, `Coach.FinishedInQueue` y a `Tracker.FreeAt`/`MarkSeenDone`, sin empeorar los minutos de cierre de las grabaciones.

**Architecture:** `Engine/Belief/` con `StateBelief` (la fusión y la deduplicación de lo que el log avisa tarde) y una clase por fuente que no sea el log (`QueueSource`, `CapSource`, `UnitSource`, `ScoreSource` = el `ScoreLedger` movido). `SnapshotBuilder` la crea y le pasa cada `ScreenReading`; pone las completaciones nuevas como eventos en `GameSnapshot.Events` y la `BeliefView` en `GameSnapshot.Belief`. `Coach` y `Tracker` consumen eventos como hasta hoy y el `Coach` lee `Belief.Research` para marcar en curso.

**Tech Stack:** C# .NET 10, xUnit, `ReplaySession` (herramienta) para comparar grabaciones.

**Spec:** `docs/superpowers/specs/2026-11-02-creencia-del-estado-design.md`

## Global Constraints

- Textos para el usuario y comentarios en castellano rioplatense, sin guiones largos.
- `Directory.Build.props` trata los warnings como errores.
- Un commit por cambio, mensaje en castellano, con `Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>`.
- Tests: `dotnet test BoCoach.slnx -c Release` (1557 pasan en la v0.9.82). Nunca dos `dotnet build/test` a la vez.
- Rama `fase-9-refinamiento`; no pushear a `main`.
- `BoCoach.Core` sin dependencias de Windows (`ArquitecturaTests`).
- Fuerzas (exactas del spec): `Inferred = 1`, `Observed = 2`, `Announced = 3`, `Direct = 4`.
- Ventanas: `CreditSeconds` 20 s (casas, unidades, tecnologías por la cola); 2 s para los edificios por la cola y el puntaje; la cola en dos lecturas seguidas; una tecnología terminada por la cola pasado el 90 % de su tiempo con la civ; más de 200 aldeanos y más de 6 unidades de golpe se descartan.
- Orden dentro de una captura: log, tope, cola, puntaje, población menos aldeanos, edad y tecnologías gratis.
- Línea base de la v0.9.82 en el scratchpad (`base/<grabación>.txt`); la nueva se guarda en `nuevo/` y se comparan los minutos de cierre (`DoneAt` de cada paso) y los renglones "Centro urbano parado".

## Review Focus

- Un `GameSnapshot` sin `Belief` (ramas, tests a mano): el coach no marca nada en curso por la cola y no tira. Test en la Task 5.
- Un ícono de la cola de una unidad que game_data no tiene (`"?"`, `housed`): no suma edificios ni investigaciones. Test en la Task 2.
- El aviso del log de una tecnología que la creencia ya dio por hecha por la cola: un solo evento. Test en la Task 2.
- Una tecnología gratis de la civ que el log sí avisa después: un solo evento. Test en la Task 4.
- Las casas que el tope delata y el "House Built" tardío (`CoachTests` ya lo fija): sigue pasando con la creencia (Task 3).

---

### Task 1: Evidencia, vista y creencia con el log

**Files:**
- Create: `src/BoCoach.Core/Engine/Belief/Evidence.cs`
- Create: `src/BoCoach.Core/Engine/Belief/BeliefView.cs`
- Create: `src/BoCoach.Core/Engine/Belief/StateBelief.cs`
- Test: `tests/BoCoach.Core.Tests/Engine/StateBeliefTests.cs`

**Interfaces:**
- Produces: `public enum Strength { Inferred = 1, Observed = 2, Announced = 3, Direct = 4 }`; `public enum EvidenceSource { Log, Hud, Civ, Queue, PopulationCap, Production, Population, Score }`; `public sealed record Completion(string EventId, EvidenceSource Source, Strength Strength) { public bool Synthetic => Source != EvidenceSource.Log; }`.
- `public enum ResearchStatus { InProgress, Done }`; `public sealed record BeliefView(IReadOnlyDictionary<string,int> Buildings, IReadOnlyDictionary<string,ResearchStatus> Research, IReadOnlyDictionary<string,int> Units, int? Villagers, ResourceAmounts? VillagersOn, int? Population, int? PopulationCap, Age Age, int BuiltCap)` con `int Count(string building)`, `bool InProgress(string tech)`, `bool Done(string tech)`, `int Military` (suma de `Units`) y `static BeliefView Empty`.
- `public sealed class StateBelief(GameData data, CivBonuses? civ = null)` con `int? PlayerSlot { get; set; }`, `void Observe(ScreenReading reading, double now)` (`now` = reloj crudo, el último si esta captura no lo trae), `IReadOnlyList<Completion> TakeNew()` (lo creído desde la última llamada, en orden), `BeliefView View { get; }` y `static bool CanObserve(string eventId)` (`!EventConventions.Unannounced.Contains(eventId)`).

- [ ] **Step 1: Tests**

```csharp
public class StateBeliefTests
{
    private static ScreenReading Reading(double clock, params string[] events) =>
        new(clock, 200, 200, 100, 200, 3, null, null, events);

    [Fact]
    public void El_log_suma_un_edificio_por_aviso_y_el_centro_urbano_arranca_en_uno()
    {
        var belief = new StateBelief(TestData.GameData);
        belief.Observe(Reading(10, "archery_range_built"), 10);
        Assert.Equal(1, belief.View.Count("town_center"));
        Assert.Equal(1, belief.View.Count("archery_range"));
        Assert.Equal(Age.Feudal, belief.View.Age); // una galería es de Feudal
        var completion = Assert.Single(belief.TakeNew());
        Assert.Equal(("archery_range_built", EvidenceSource.Log, false), (completion.EventId, completion.Source, completion.Synthetic));
        Assert.Empty(belief.TakeNew());
    }

    [Fact]
    public void La_vista_trae_lo_leido_del_hud_y_lo_investigado()
    {
        var belief = new StateBelief(TestData.GameData);
        belief.Observe(new ScreenReading(20, 200, 200, 100, 200, 7, 9, 15, ["loom_researched", "scout_cavalry_created"]) { VillagersOn = new ResourceAmounts(4, 3, 0, 0) }, 20);
        Assert.Equal((7, 9, 15), (belief.View.Villagers, belief.View.Population, belief.View.PopulationCap));
        Assert.Equal(new ResourceAmounts(4, 3, 0, 0), belief.View.VillagersOn);
        Assert.True(belief.View.Done("loom"));
        Assert.Equal(1, belief.View.Units["scout_cavalry"]);
        Assert.Equal(1, belief.View.Military);
    }

    [Fact]
    public void Lo_que_el_juego_no_avisa_no_se_puede_observar()
    {
        Assert.False(StateBelief.CanObserve("farm_built"));
        Assert.True(StateBelief.CanObserve("house_built"));
    }
}
```

- [ ] **Step 2: Correr los tests y ver que fallan** (`dotnet test tests/BoCoach.Core.Tests -c Release --filter StateBeliefTests`: no compila).
- [ ] **Step 3: Implementar.** `StateBelief.Observe` solo con el log en esta tarea: `house_built` y edificios suman 1 (y lo que valen, `CountsAs`, con su `house_built`/`*_built` como completación propia, como hoy en `SnapshotBuilder`), `*_researched` → `Done`, `*_created` que no sea aldeano → `Units` +1, y el HUD a la vista. `BuiltCap` = población del centro urbano de la civ (`PlanStart.For(civ, data).PopulationCap`) más `civ.PopBonus(entity, Age.Dark)` de cada edificio avisado. La edad: `BuildOrderValidator.AgeOf(what)` para una edad investigada, `entity.Age` para lo demás (como `Tracker.Reached`).
- [ ] **Step 4: Tests en verde.**
- [ ] **Step 5: Commit** `Creencia del estado: evidencia, vista y lo que dice el log`.

### Task 2: La cola como fuente (edificios y tecnologías)

**Files:**
- Create: `src/BoCoach.Core/Engine/Belief/QueueSource.cs`
- Modify: `src/BoCoach.Core/Engine/Belief/StateBelief.cs`
- Test: `tests/BoCoach.Core.Tests/Engine/StateBeliefTests.cs`

**Interfaces:**
- Produces: `internal sealed class QueueSource(GameData data)` con `QueueFacts Update(IReadOnlyList<QueueItem> queue, double now, Func<string, double> researchTime)` y `internal sealed record QueueFacts(IReadOnlyDictionary<string,int> Producers, IReadOnlySet<string> InProgress, IReadOnlyList<string> Finished)`: `Producers` = edificio → N íconos iguales que no esperan, confirmado en dos lecturas seguidas (máximo por clave, sin sumar claves, como `BuildingsFromQueue`); `InProgress` = tecnologías en la cola en esta y la anterior lectura; `Finished` = las que estuvieron (desde `firstSeen`) y faltan en dos lecturas seguidas pasado `0.9 * researchTime(tech)` (como `Coach.FinishedInQueue`).
- `StateBelief` filtra `Producers`: no cuenta un productor cuya edad (`entity.Age` del edificio) o la de la unidad sea mayor que `View.Age`; agrega completaciones Observadas hasta llegar a N; `Finished` → `Done` con una completación `<tech>_researched` Observada que espera 20 s el aviso del log.

- [ ] **Step 1: Tests**

```csharp
private static ScreenReading Queue(double clock, params (string Key, bool Waiting)[] items) =>
    Reading(clock) with { Queue = items.Select(i => new QueueItem(i.Key, i.Waiting)).ToList() };

[Fact]
public void El_log_aviso_una_galeria_y_la_cola_muestra_dos_arqueros_a_la_vez_entonces_hay_dos()
{
    var belief = new StateBelief(TestData.GameData);
    belief.Observe(Reading(717, "archery_range_built"), 717);
    belief.Observe(Queue(734, ("villager", false), ("archer", false), ("archer", false), ("archer", true)), 734);
    Assert.Equal(1, belief.View.Count("archery_range")); // una sola lectura no alcanza
    belief.Observe(Queue(734.5, ("villager", false), ("archer", false), ("archer", false), ("archer", true)), 734.5);
    Assert.Equal(2, belief.View.Count("archery_range"));
    Assert.Equal(1, belief.View.Count("town_center"));
    var added = belief.TakeNew().Where(c => c.EventId == "archery_range_built").ToList();
    Assert.Equal(2, added.Count);
    Assert.Equal([EvidenceSource.Log, EvidenceSource.Queue], added.Select(c => c.Source));
}

[Fact]
public void Un_icono_de_una_unidad_de_una_edad_que_no_se_alcanzo_no_cuenta()
{
    // Partida 20261101-230201: una milicia leída como arquero daba una galería a las 9:37, en la Edad Oscura.
    var belief = new StateBelief(TestData.GameData);
    belief.Observe(Queue(577, ("archer", false)), 577);
    belief.Observe(Queue(577.5, ("archer", false)), 577.5);
    Assert.Equal(0, belief.View.Count("archery_range"));
    belief.Observe(Reading(600, "feudal_age_researched"), 600);
    belief.Observe(Queue(601, ("archer", false)), 601);
    belief.Observe(Queue(601.5, ("archer", false)), 601.5);
    Assert.Equal(1, belief.View.Count("archery_range"));
}

[Fact]
public void Un_icono_desconocido_o_sin_poblacion_no_dice_nada()
{
    var belief = new StateBelief(TestData.GameData);
    for (var t = 0; t < 2; t++)
        belief.Observe(Queue(100 + t, ("?", false), ("housed", false)), 100 + t);
    Assert.Equal(1, belief.View.Buildings.Count);
    Assert.Empty(belief.View.Research);
}

[Fact]
public void Una_tecnologia_en_la_cola_esta_en_curso_y_al_irse_pasado_su_tiempo_queda_hecha()
{
    var belief = new StateBelief(TestData.GameData);
    belief.Observe(Queue(200, ("loom", false)), 200);
    Assert.False(belief.View.InProgress("loom"));
    belief.Observe(Queue(201, ("loom", false)), 201);
    Assert.True(belief.View.InProgress("loom"));
    // El telar tarda 25 s: a los 10 s que falte es una lectura mala.
    belief.Observe(Queue(210, ("villager", false)), 210);
    belief.Observe(Queue(211, ("villager", false)), 211);
    Assert.True(belief.View.InProgress("loom"));
    belief.Observe(Queue(226, ("villager", false)), 226);
    belief.Observe(Queue(227, ("villager", false)), 227);
    Assert.True(belief.View.Done("loom"));
    var done = Assert.Single(belief.TakeNew(), c => c.EventId == "loom_researched");
    Assert.True(done.Synthetic);
}

[Fact]
public void El_aviso_tardio_del_log_de_una_tecnologia_hecha_por_la_cola_no_cuenta_dos_veces()
{
    var belief = new StateBelief(TestData.GameData);
    belief.Observe(Queue(200, ("loom", false)), 200);
    belief.Observe(Queue(201, ("loom", false)), 201);
    belief.Observe(Queue(226, ("villager", false)), 226);
    belief.Observe(Queue(227, ("villager", false)), 227);
    belief.Observe(Reading(230, "loom_researched"), 230);
    Assert.Equal(1, belief.TakeNew().Count(c => c.EventId == "loom_researched"));
}
```

- [ ] **Step 2: Ver que fallan.**
- [ ] **Step 3: Implementar `QueueSource` y el filtro por edad en `StateBelief`.** `researchTime` = `civ.Time(entity, View.Age)`.
- [ ] **Step 4: Verde.**
- [ ] **Step 5: Commit** `Creencia del estado: la cola de producción como fuente`.

### Task 3: Tope, población, puntaje y el `SnapshotBuilder` sobre la creencia

**Files:**
- Create: `src/BoCoach.Core/Engine/Belief/CapSource.cs` (de `SnapshotBuilder.InferHousesFromCap`)
- Create: `src/BoCoach.Core/Engine/Belief/UnitSource.cs` (de `LoggedUnit`, `InferUnitsFromPopulation`, `BuildingsFromProduction`)
- Rename: `src/BoCoach.Core/Engine/ScoreLedger.cs` → `src/BoCoach.Core/Engine/Belief/ScoreSource.cs` (`git mv`, clase `ScoreSource`, mismo algoritmo)
- Modify: `src/BoCoach.Core/Engine/Belief/StateBelief.cs`
- Modify: `src/BoCoach.Core/Engine/SnapshotBuilder.cs`
- Modify: `src/BoCoach.Core/Engine/GameSnapshot.cs` (`public BeliefView? Belief { get; init; }`)
- Test: los que ya hay (`ScoreLedgerTests`, `CoachTests` de cola, producción, casas y unidades, `SettlementTests`, `GameStartTests`); `ScoreLedgerTests` cambia solo su comentario de cabecera.

**Interfaces:**
- `internal sealed class CapSource(GameData data, CivBonuses civ)`: `void Credit(int pop, double now)` (un edificio avisado que da población), `int HousesFrom(int cap, double now)` (casas que el tope delata), `bool AbsorbHouse(double now)` (un "House Built" que ya se había deducido).
- `internal sealed class UnitSource(GameData data)`: `bool Logged(string unitEvent, double now, out IReadOnlyList<string> alsoCreated)` (false si el aviso ya se había deducido; `alsoCreated` = las unidades sin tipo que este aviso explica), `IReadOnlyList<string> FromPopulation(int military, double now)` (las unidades de más), `int ProducersOf(string unitEvent, double now)` (edificios iguales que delata el ritmo).
- `ScoreSource`: la API de `ScoreLedger` (`Logged`, `Update`, `PlayerSlot`, `ScoreShare`, `RepeatWindow`).
- `StateBelief.Observe` en el orden del spec; `TakeNew` devuelve todo lo creído en la captura (también lo que el log avisó).
- `SnapshotBuilder` queda con `ScreenReading`, `StartOffset`, `PopulationEstimated`, `PlayerSlot` (pasa a la creencia), el stock, la estimación de "N/M" (`Villagers + Belief.Military + 1`, tope `Belief.BuiltCap`) y `Build`, que pone `Events` desde `TakeNew()` (con el tiempo de juego) y `Belief = belief.View`.

- [ ] **Step 1: Mover `ScoreLedger` con `git mv` y renombrar; mover el código del tope, las unidades y el ritmo a sus clases; reescribir `SnapshotBuilder` sobre `StateBelief`.**
- [ ] **Step 2: Correr `dotnet test tests/BoCoach.Core.Tests -c Release`: todos en verde** (los tests de casos reales no cambian).
- [ ] **Step 3: Commit** `SnapshotBuilder sobre la creencia del estado: tope, población, ritmo y puntaje como fuentes`.

### Task 4: Tecnologías gratis de la civ y el `Tracker` sin reglas sueltas

**Files:**
- Modify: `src/BoCoach.Core/Engine/Belief/StateBelief.cs`
- Modify: `src/BoCoach.Core/Engine/Tracker.cs` (sacar `FreeAt`, `_freeAt`, `MarkSeenDone`; `EventConventions.Unannounced.Contains(id)` → `!StateBelief.CanObserve(id)`)
- Modify: `src/BoCoach.Core/Engine/GameReport.cs:147` (idem)
- Test: `tests/BoCoach.Core.Tests/Engine/StateBeliefTests.cs`; migrar `TrackerLanesTests.Lo_que_la_civ_tiene_gratis_queda_hecho_al_llegar_a_su_edad` a `SnapshotBuilder` + `Coach` con el mismo caso (francos sí, britanos no).

- [ ] **Step 1: Tests**

```csharp
[Fact]
public void Una_tecnologia_gratis_de_la_civ_queda_hecha_al_llegar_a_su_edad()
{
    // Partida 20261031-192108: a los francos el juego les da la collera al llegar a Feudal.
    var belief = new StateBelief(TestData.GameData, TestData.GameData.Civ("franks"));
    belief.Observe(Reading(10), 10);
    Assert.False(belief.View.Done("horse_collar"));
    belief.Observe(Reading(600, "feudal_age_researched"), 600);
    Assert.True(belief.View.Done("horse_collar"));
    Assert.Contains(belief.TakeNew(), c => c is { EventId: "horse_collar_researched", Source: EvidenceSource.Civ });
    belief.Observe(Reading(605, "horse_collar_researched"), 605);
    Assert.DoesNotContain(belief.TakeNew(), c => c.EventId == "horse_collar_researched");
}

[Fact]
public void A_quien_no_le_sale_gratis_no_se_le_da()
{
    var belief = new StateBelief(TestData.GameData, TestData.GameData.Civ("britons"));
    belief.Observe(Reading(600, "feudal_age_researched"), 600);
    Assert.False(belief.View.Done("horse_collar"));
}
```

- [ ] **Step 2: Fallan.**
- [ ] **Step 3: Implementar.** Al subir la edad (y en la primera captura), por cada `Entity` de `data.Entities` con `Kind` tecnología, `BuildOrderValidator.AgeOf(key) is null`, `entity.Age <= age`, `entity.Cost != Zero` y `civ.Cost(entity, age) == Zero`: `Done` con completación Avisada (fuente `Civ`), que absorbe el aviso del log 20 s. Sacar `FreeAt` y `MarkSeenDone` del `Tracker`.
- [ ] **Step 4: Verde** (`dotnet test tests/BoCoach.Core.Tests -c Release`).
- [ ] **Step 5: Commit** `Las tecnologías gratis de la civ salen de la creencia, no del Tracker`.

### Task 5: El `Coach` lee la creencia

**Files:**
- Modify: `src/BoCoach.Core/Engine/Coach.cs:438-485` (`ResearchInQueue` con `snapshot.Belief`; sacar `_lastQueueKeys`, `_seenInQueue`, `FinishedInQueue`)
- Test: `tests/BoCoach.Core.Tests/Engine/CoachTests.cs` (`Una_tecnologia_que_paso_por_la_cola_y_se_fue_despues_de_su_tiempo_queda_hecha` y `Una_tecnologia_que_nunca_se_vio_en_la_cola_no_se_da_por_hecha_por_la_cola` pasan por `SnapshotBuilder` con `ScreenReading.Queue`; nuevo `Un_snapshot_sin_creencia_no_marca_nada_por_la_cola`).

- [ ] **Step 1: Migrar los dos tests y escribir el nuevo** (`coach.Update(WithQueue(...))` sin `Belief`: el paso del telar sigue `Pending`/`Ready`, sin excepción).
- [ ] **Step 2: Fallan** (el nuevo no; los migrados sí hasta cambiar el `Coach`).
- [ ] **Step 3: Implementar.** `ResearchInQueue(snapshot)`: por cada paso de investigación `Pending`/`Ready` cuya tecnología `snapshot.Belief?.InProgress(tech) == true`, `Tracker.MarkStarted(step.Index, snapshot.Time)`. Lo terminado llega como evento `<tech>_researched`.
- [ ] **Step 4: Verde.**
- [ ] **Step 5: Commit** `El Coach marca en curso por la creencia y deja de cerrar investigaciones por su cuenta`.

### Task 6: Comparar las grabaciones, la suite y la versión

**Files:**
- Modify: `CLAUDE.md` (nota de la versión), `docs/pendientes-usuario.md`, `src/BoCoach.App/BoCoach.App.csproj` (`<Version>`), `PLAN.md` sección 11 ítem 14 (escalón 2 hecho).

- [ ] **Step 1: `dotnet build tools/ReplaySession -c Release` y correr `corridas.sh` del scratchpad con el bin nuevo a `nuevo/`.**
- [ ] **Step 2: Comparar** por grabación la tabla final de pasos (`DoneAt`) y los renglones con "Centro urbano parado" (`grep`): nada más tarde que en `base/`; lo distinto se explica en la nota. Si algo empeora, arreglar antes de seguir (volver a la Task que corresponda).
- [ ] **Step 3: `dotnet test BoCoach.slnx -c Release`: todo en verde.**
- [ ] **Step 4: Skill `nueva-version`:** notas en `CLAUDE.md`, `<Version>` nueva, compilar en Release, push de la rama, instrucciones en `docs/pendientes-usuario.md`.
