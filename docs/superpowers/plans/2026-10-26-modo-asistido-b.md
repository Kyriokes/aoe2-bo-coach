# D12 (b): la política dentro del Planner, plan de implementación

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** que el Planner, mientras simula, le pregunte a una política qué pasos agregar al final de la BO (en cada nacimiento y cuando la BO se queda sin pasos por arrancar), de modo que la BO resultante, simulada de nuevo sin la política, dé exactamente los mismos tiempos.

**Architecture:** `IStepPolicy` recibe una foto de solo lectura (`PlanView`) en cada punto de decisión y devuelve pasos. El Planner los agrega, los avanza en el mismo segundo y, si en una simulación nueva el paso quedaría alcanzado antes de cuando se agregó, lo fija: con `at_pop` si en ese segundo nació alguien (la población llegó recién ahí a ese número) y si no con `at_time`. El generador del sub-paso (c) será una política más; acá se prueba con políticas de prueba.

**Tech Stack:** C# .NET 10 (`BoCoach.Core`, xunit en `BoCoach.Core.Tests`).

**Spec:** `docs/superpowers/specs/2026-10-26-modo-asistido-design.md` (sección 3, "Cuándo decide"; sección 5, `IStepPolicy` y `PlanView`; sección 10, sub-paso b).

## Global Constraints

- Sin política (`policy` null) el Planner hace exactamente lo de hoy: todos los tests y la web siguen en verde sin cambios.
- Puntos de decisión: un nacimiento (una unidad con población que termina, aldeano o militar) y el segundo en que la BO se queda sin pasos por arrancar; además, el segundo en que todos los pasos terminaron (si la política no agrega nada, la simulación termina).
- Equivalencia: la BO generada (los pasos del arranque más los agregados, pasados a totales con `LivePlan.CountsToTotals`) simulada con `Planner.Simulate(bo)` da los mismos `Steps` (Id, Start, End), `Blocks`, `Timeline` e `Idle`, con la comida infinita y con la finita.
- Lo que espera lleva `at_pop`, como en las guías; `at_time` solo cuando en ese segundo no nació nadie. Si el paso ya trae una condición, se queda con la más exigente (el máximo).
- Textos y comentarios en castellano rioplatense, sin guiones largos. TDD: el test falla primero. Un commit por tarea, terminado con `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`. Rama `fase-9-refinamiento`.

## Review Focus

1. **Una política que agrega pasos sin parar en el mismo segundo** (un "mover" que se cumple al instante y vuelve a dejar la BO sin pasos): tiene que cortar con un error que diga que la política no para, no colgarse. Test en la Tarea 1.
2. **Una política que repite el id de un paso** (de la BO o de uno que ya agregó): error claro, no dos pasos con el mismo id. Test en la Tarea 1.
3. **La BO generada tiene que validar:** `BuildOrderValidator` no da errores con los `at_pop` y `at_time` que puso el Planner. Test en la Tarea 1.
4. **La política guarda y reusa un `Step` que ya devolvió** y lo cambia después: no tiene que alterar la simulación (el Planner guarda una copia). Test en la Tarea 1.
5. **Un granjero que llegó a la granja sin un paso de granjas, con un paso de granjas desde la granja más adelante en la BO todavía sin alcanzar:** en la simulación con la política y en la de la BO generada tiene que juntar lo mismo (hoy `WaitingFarmers` cuenta también los pasos sin alcanzar y las dos simulaciones se separan). Test en la Tarea 3.

---

### Task 1: `IStepPolicy` y los puntos de decisión

**Files:**
- Create: `src/BoCoach.Core/Engine/PlanView.cs` (`IStepPolicy`, `PlanView`, `PlanViewStep`)
- Modify: `src/BoCoach.Core/Engine/Planner.cs` (`Simulate`, `Simulation.Run`, `CompleteRunning`, el armado de `ItemSim` para los pasos agregados)
- Test: `tests/BoCoach.Core.Tests/Engine/PlannerPolicyTests.cs` (nuevo)

**Interfaces:**
- Produces (en `BoCoach.Core.Engine`):
  ```csharp
  public interface IStepPolicy
  {
      /// Lo que agrega la política en este punto de decisión, al final de la BO (vacío = nada por ahora).
      IReadOnlyList<Step> Next(PlanView view);
  }

  public sealed record PlanViewStep(Step Step, int Remaining, bool Reached, bool Started, double? Start, double? End);

  public sealed record PlanView(
      double Time, ResourceAmounts Stock, int Population, int InProduction, int PopulationCap, Age Age,
      IReadOnlyDictionary<GatherSource, int> Villagers, int IdleVillagers, int Builders,
      IReadOnlyDictionary<string, int> Buildings,          // hechos, contando counts_as
      IReadOnlyDictionary<string, int> UnderConstruction,  // en obra, por clave
      IReadOnlyList<string> Researched,
      IReadOnlyDictionary<GatherSource, double> Rates,     // por aldeano y por segundo, con la civ, la edad y las tecnologías
      IReadOnlyDictionary<string, int> Queued,             // unidades y tecnologías esperando en la cola de cada edificio
      IReadOnlyDictionary<string, int> Busy,               // edificios de cada clave trabajando ahora
      IReadOnlyList<PlanViewStep> Steps,                   // todos los pasos hasta ahora, en orden
      bool Birth,                                          // en este segundo nació alguien
      bool RanOut);                                        // la BO no tiene pasos por arrancar
  ```
- Produces en `Planner`: `PlanResult Simulate(IReadOnlyList<PlanItem> items, PlanStart start, CivBonuses? civ = null, IStepPolicy? policy = null)` y `PlanResult Simulate(BuildOrder bo, IStepPolicy policy)` (los pasos de la BO más los de la política, desde `PlanStart.For(civ)` como `Simulate(bo)`). `PlanResult.Steps` trae también los agregados, en orden, con las condiciones que les puso el Planner.
- Los pasos agregados entran en forma de unidades (como `schema_version` 1): `Remaining` = `Count` si es `Train` o `Build`, 1 si no (igual que `LivePlan.Units` sobre la BO ya pasada a totales).

- [ ] **Step 1: Write the failing tests**

En `PlannerPolicyTests` (nuevo), con una política de prueba `Scripted` (en el mismo archivo de tests) que recibe una función `Func<PlanView, IEnumerable<Step>>` y anota cada `PlanView` que recibe; arranque `PlanStart.Standard with { PopulationCap = 200 }` salvo que se diga otra cosa. Ayuda `Generated(PlanResult plan, string civ)`: `new BuildOrder { Name = "g", Civ = civ, Steps = plan.Steps.Select(s => s.Step with { }).ToList() }` pasado por `LivePlan.CountsToTotals`. Ayuda `AssertSame(PlanResult a, PlanResult b)`: mismos `Steps` (Id, Start, End), `Blocks`, `Timeline` e `Idle`.

- `La_politica_se_consulta_al_quedarse_sin_pasos_y_en_cada_nacimiento`: la política devuelve en la primera consulta un paso de 3 aldeanos a ovejas (`Count = 3`) y después nada. Las consultas son a los 0 (`RanOut` true, `Birth` false), 25, 50 y 75 (`Birth` true), y nada más.
- `Lo_que_agrega_arranca_en_el_mismo_segundo`: en la consulta de los 0 s agrega una casa con 3 constructores y los 3 iniciales: `plan.For(<id>)!.Start == 0`.
- `La_BO_generada_da_lo_mismo_que_la_simulacion_con_la_politica` (Theory con `"any"`, `"franks"` y `"huns"`; los hunos no tienen casas y la política no las pide): política `Simple` (en el archivo de tests, con un parámetro `houses` = `!civ.Start.NoHouses`) que en cada consulta, si hay aldeanos sin tarea, los mueve a las ovejas; si hay menos de 15 aldeanos y ningún paso de aldeanos con unidades pendientes, agrega 1 aldeano (a ovejas hasta tener 6 ahí, después a madera), y si `PopulationCap - Population - InProduction <= 2`, `houses` y no hay una casa pendiente ni en obra, una casa desde la madera que vuelve a la madera. Con `Simulate(bo vacía, política)` contra `Simulate(Generated(...))`: `AssertSame`, y `BuildOrderValidator` sin errores sobre la BO generada.
- `Un_paso_agregado_despues_queda_fijado_para_arrancar_en_el_mismo_segundo`: política de 3 consultas: a los 0 s agrega [casa x2 con 3 constructores a ovejas, 3 aldeanos a ovejas]; en el primer nacimiento (25 s) agrega un campamento maderero desde las ovejas a madera; en el segundo (50 s), un cuartel desde la madera a madera (espera madera); cuando el cuartel arranca y la BO vuelve a quedarse sin pasos, 1 aldeano a madera. El campamento tiene `AtPop == 5`, el cuartel `AtPop == 6`, el último paso de aldeanos tiene `AtTime` o `AtPop` y `AssertSame` contra la BO generada.
- `Una_politica_que_no_para_de_agregar_da_error` (Review Focus 1): en cada consulta agrega un "mover 0 aldeanos sin tarea (`From` null) a madera" con id nuevo (se cumple al instante: `Move` con 0 no espera a nadie). `Simulate` tira `InvalidOperationException` cuyo mensaje contiene "política".
- `Un_id_repetido_da_error` (Review Focus 2): la política devuelve dos veces un paso con id `"x"`. `InvalidOperationException` cuyo mensaje contiene `x`.
- `Cambiar_un_paso_ya_devuelto_no_cambia_la_simulacion` (Review Focus 4): la política devuelve un paso de 3 aldeanos y en la consulta siguiente le pone `Count = 30` al mismo objeto. La simulación da lo mismo que con una política que nunca lo cambia.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/BoCoach.Core.Tests --filter "FullyQualifiedName~PlannerPolicyTests"`
Expected: no compila (`IStepPolicy`, `PlanView` y la sobrecarga de `Simulate` no existen).

- [ ] **Step 3: Implementar**

En `Simulation.Run`, el recorrido de los pasos pasa a ser por índice, para poder seguir con los que se agreguen en el mismo segundo. Después de avanzar los pasos que hay, si hay política y es un punto de decisión, se consulta; lo que devuelve se agrega (copiado con `with { }`) y se avanza en el mismo segundo con las mismas variables `inOrder` y `reached`; si eso vuelve a dejar la BO sin pasos por arrancar, se consulta otra vez. Más de 100 consultas en un segundo: `InvalidOperationException("La política agregó pasos sin parar en <hora>: ...")`. Un id que ya existe: `InvalidOperationException` que lo nombra.

Puntos de decisión en un segundo: hubo un nacimiento (`CompleteRunning` cuenta las unidades con `Pop > 0` que terminaron), o `inOrder` pasó a ser verdadero en este segundo (`RanOut`), o todos los pasos terminaron. Si todos terminaron y la política no agrega nada, la simulación termina.

Fijación (el algoritmo que decide la equivalencia): el Planner lleva desde cuándo `inOrder` está en verdadero sin cortes (`_inOrderSince`) y desde cuándo `reached` lo está (`_reachedSince`), medidos al final de la lista. Un paso agregado en el segundo `t` quedaría alcanzado en una simulación nueva desde `_reachedSince` si es de aldeanos y desde `_inOrderSince` si no. Si ese segundo es anterior a `t`: con un nacimiento en `t`, `AtPop = Math.Max(AtPop ?? 0, _population)`; sin nacimiento, `AtTime = Math.Max(AtTime ?? 0, (int)t)`.

`PlanView` se arma con lo que ya lleva la simulación (`_stock`, `_population`, `_reserved`, `_cap`, `_age`, `_villagers`, `_idle`, `_builders`, `_buildings`, los pasos de construcción con `Unfinished > 0`, `_researched`, `_rates`, `_queues`, `_busyProducers` y los `ItemSim`).

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/BoCoach.Core.Tests`
Expected: todo en verde (los tests del Planner de siempre también).

- [ ] **Step 5: Commit**

```bash
git add src/BoCoach.Core/Engine/PlanView.cs src/BoCoach.Core/Engine/Planner.cs tests/BoCoach.Core.Tests/Engine/PlannerPolicyTests.cs
git commit -m "Planner: una política que agrega pasos mientras simula (D12 b)"
```

---

### Task 2: la comida en la foto de la política

**Files:**
- Modify: `src/BoCoach.Core/Engine/PlanView.cs`, `src/BoCoach.Core/Engine/FoodStocks.cs`, `src/BoCoach.Core/Engine/Planner.cs`
- Test: `tests/BoCoach.Core.Tests/Engine/PlannerPolicyTests.cs`

**Interfaces:**
- Consumes: `PlanView` e `IStepPolicy` (Tarea 1); `FoodStocks`, `CivBonuses.FoodYield` (sub-paso a).
- Produces:
  ```csharp
  public sealed record PlanFoodView(
      IReadOnlyDictionary<GatherSource, double> Remaining, // ovejas, caza y bayas: la comida que todavía rinden para la civ en esta edad
      IReadOnlyList<double> Farms,                         // lo que le queda a la granja de cada granjero, en orden
      int FarmersWaitingReseed,
      int FarmersWaitingFarm);
  ```
  y `PlanView.Food : PlanFoodView?` (init, null con la comida infinita). Lo que no está en `Remaining` no se acaba (madera, oro, piedra, pesca).

- [ ] **Step 1: Write the failing tests**

En `PlannerPolicyTests`:
- `Sin_comida_finita_la_foto_no_trae_comida`: la primera `PlanView` tiene `Food == null`.
- `La_foto_trae_lo_que_queda_de_cada_fuente_con_el_rendimiento_de_la_civ`: tártaros, comida de Arabia (`new PlannerSettings { Food = FoodSupply.For(TestData.GameData, null) }`), arranque con 6 en ovejas; en la consulta de los 0 s `Food.Remaining[Sheep] == 1256` (800 × 1,57), `Food.Remaining[Hunt] == 680`, y no hay clave `Wood`.
- `La_foto_trae_las_granjas_y_los_granjeros_que_esperan`: 2 granjeros con granjas de 50 (`FarmFood = 50`), madera 0; en una consulta posterior al vaciado de las dos (un nacimiento a los 300 s con 1 aldeano en cola desde el arranque con `AtTime = 275`): `Food.Farms` tiene dos ceros y `FarmersWaitingReseed == 2`.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/BoCoach.Core.Tests --filter "FullyQualifiedName~PlannerPolicyTests"`
Expected: no compila (`PlanView.Food` no existe).

- [ ] **Step 3: Implementar**

`FoodStocks` suma `PlanFoodView Snapshot(Func<GatherSource, double> yield, int farmersWaitingFarm)` (la comida cruda de cada fuente contada por su rendimiento; las granjas en orden; los granjeros con la granja vacía entre los que tienen granja). El Planner la pone en `PlanView.Food` con `_civ.FoodYield(source, _age)` y `WaitingFarmers()`.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/BoCoach.Core.Tests`
Expected: todo en verde.

- [ ] **Step 5: Commit**

```bash
git add src/BoCoach.Core/Engine/PlanView.cs src/BoCoach.Core/Engine/FoodStocks.cs src/BoCoach.Core/Engine/Planner.cs tests/BoCoach.Core.Tests/Engine/PlannerPolicyTests.cs
git commit -m "Planner: la política ve la comida que queda y las granjas"
```

---

### Task 3: equivalencia con la comida finita y granjas, y docs

**Files:**
- Modify: `src/BoCoach.Core/Engine/Planner.cs` (`WaitingFarmers`)
- Test: `tests/BoCoach.Core.Tests/Engine/PlannerPolicyTests.cs`, `tests/BoCoach.Core.Tests/Engine/PlannerFoodTests.cs`
- Modify: `PLAN.md` (D12), `CLAUDE.md` ("Estado actual"), `docs/superpowers/specs/2026-10-26-modo-asistido-design.md` (sección 4, la regla de la granja)

**Interfaces:**
- Consumes: todo lo de las Tareas 1 y 2.
- Produces: `WaitingFarmers()` cuenta solo los pasos de granjas desde la granja ya alcanzados (`Reached` o `Started`) y sin terminar.

- [ ] **Step 1: Write the failing tests**

En `PlannerFoodTests`:
- `Un_paso_de_granjas_que_todavia_no_llego_no_hace_esperar_al_granjero` (Review Focus 5): comida de Arabia, arranque con 1 en ovejas, madera 200 y oro 100; pasos: mover 1 de ovejas a la granja, telar con `AtTime = 100`, granja desde la granja a la granja. Ningún `PlanIdle` con `WaitingFarm` empieza antes de los 100 s.

En `PlannerPolicyTests`:
- `Con_comida_finita_la_BO_generada_da_lo_mismo` (Theory con `"any"`, `"tatars"` y `"poles"`): política `Simple` de la Tarea 1 con dos cambios: cuando `Food.Remaining[Sheep]` baja de 200, mueve una vez a todos los de las ovejas a las bayas; y cuando las bayas bajan de 200, el aldeano nuevo va a la granja con su granja, como dos pasos en la misma consulta: [1 aldeano a la granja, 1 granja desde la granja a la granja] (en vez del aldeano a madera). Con `Food = FoodSupply.For(data, null)` en las dos simulaciones: `AssertSame` y `BuildOrderValidator` sin errores.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/BoCoach.Core.Tests --filter "FullyQualifiedName~PlannerPolicyTests|FullyQualifiedName~PlannerFoodTests"`
Expected: `Un_paso_de_granjas_que_todavia_no_llego...` falla (hay un `WaitingFarm` desde los 0 s). La Theory puede pasar o no según cuándo agregue la política; si pasa, igual queda como guarda.

- [ ] **Step 3: Implementar**

En `WaitingFarmers()`, el filtro de pasos suma `i.Reached || i.Started`.

- [ ] **Step 4: Run all the tests**

Run: `dotnet test` (toda la solución) y `cd web && npm test`.
Expected: todo en verde.

- [ ] **Step 5: Docs**

- Spec, sección 4 ("Granjas"): el que llega a la granja sin un paso de granjas **alcanzado** que la hace desde la granja trae la suya (así una simulación nueva de la BO generada da lo mismo).
- Spec, sección 3 ("Cuándo decide"): quien fija los pasos es el Planner, no la política; con `at_pop` si en ese segundo nació alguien y con `at_time` si no (un paso de aldeanos agregado cuando arranca un edificio que esperaba madera, por ejemplo).
- `PLAN.md`, D12: "(b) hecho el <fecha>": los puntos de decisión, la fijación con `at_pop` o `at_time`, `PlanView` con la comida y el arreglo de `WaitingFarmers`.
- `CLAUDE.md`, "Estado actual": un renglón de D12 (b).

- [ ] **Step 6: Commit**

```bash
git add src/BoCoach.Core/Engine/Planner.cs tests/BoCoach.Core.Tests/Engine/PlannerPolicyTests.cs tests/BoCoach.Core.Tests/Engine/PlannerFoodTests.cs PLAN.md CLAUDE.md docs/superpowers/specs/2026-10-26-modo-asistido-design.md
git commit -m "Planner: la BO generada con comida finita da lo mismo (D12 b)"
```
