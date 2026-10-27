# Modo asistido (d): unidades, centros urbanos, tecnologías y mejoras eco Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** que el generador del modo asistido arme también los objetivos de unidades (con N edificios), centros urbanos extra, tecnologías a pedido y las mejoras eco automáticas (`eco_upgrades`).

**Architecture:** reglas nuevas en `src/BoCoach.Core/Assisted/` (una por tema, como las del (c)) dentro de `GoalPolicy`, más un cursor de objetivos en `GoalState` ("un objetivo empieza a trabajarse cuando el anterior ya arrancó todos sus pasos"). El Planner no cambia salvo lo que haga falta para varios centros urbanos (ya produce en paralelo con varios edificios de la misma clave). El extractor suma la rotación de cultivos y la sierra de dos manos.

**Tech Stack:** C# .NET 10 (`net10.0`, xunit), Python 3.12 + genieutils-py (extractor), TypeScript (solo `web/src/lib/stepText.ts`).

**Spec:** `docs/superpowers/specs/2026-10-26-modo-asistido-design.md` (secciones 2, 3, 8, 9 y 10 (d)).

## Global Constraints

- Textos, comentarios y commits en castellano rioplatense, sin rayas largas (—): comas.
- Un commit por cambio, con `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>` al final.
- `BoCoach.Core` sigue `net10.0` puro (`ArquitecturaTests`).
- Garantía de equivalencia: la BO generada, pasada a totales con `LivePlan.CountsToTotals` y simulada de nuevo sin la política con `AssistedGenerator.Settings`, da los mismos pasos, bloqueos, línea de tiempo y parados (`AssertConsistent` de `AssistedGeneratorTests`).
- Regla aprendida en el (c): el generador nunca agrega un paso que no puede arrancar delante de otros (un edificio sin la madera, una investigación con su edificio ocupado o sin el costo, unidades sin su edificio hecho). Lo que todavía no puede arrancar se espera (su costo va a `GoalState.Upcoming` o `Planned`) y se agrega cuando puede.
- Toda entidad nueva de game_data lleva su nombre en `StepText.cs` y en `web/src/lib/stepText.ts` (tests lo exigen).
- Rangos de la spec: `count` 1 a 200, `buildings` 1 a 10, `town_centers` 2 a 10 (ya validados en el (c)).
- Centro urbano: 275 de madera y 100 de piedra, desde Castillos (game_data). Mejoras eco con los umbrales de `TechReminders` (`GatherVillagers` = 3, `FarmVillagers` = 6); carretilla y carreta con 15 aldeanos o más.

## Review Focus

1. **Varios objetivos de unidades seguidos** (20 exploradores con 2 establos y después 10 caballeros con los mismos establos): el segundo no construye establos de más (cuenta los que hay) y no queda trabado detrás del primero. Test en la Tarea 3.
2. **Una unidad o tecnología de una edad que ningún objetivo de antes pide** (caballeros sin Castillos en la lista): problema claro en ese objetivo, los demás siguen, sin colgarse. Test en la Tarea 2.
3. **Varios centros urbanos y el clic de edad**: con 3 centros urbanos produciendo, el clic tiene que salir (un centro urbano libre alcanza) y el tramo no tiene que pensar que hay uno solo. Test en la Tarea 4.
4. **Mejoras eco con `until` chico** (13 aldeanos con `eco_upgrades`): no compra carretilla (pide 15 o más) ni deja la BO trabada. Test en la Tarea 6.
5. **Una tecnología de un edificio que produce sin parar** (la línea de sangre en un establo con 20 exploradores en cola): espera a que el establo quede libre sin frenar lo que va detrás. Test en la Tarea 5.

---

### Task 1: rotación de cultivos y sierra de dos manos en game_data

**Files:**
- Modify: `tools/extractor/extract.py` (lista de entidades, junto a `"heavy_plow": ("tech", 13)`)
- Modify: `data/game_data/v8.9.json` (regenerado)
- Modify: `src/BoCoach.Core/BuildOrders/StepText.cs`, `web/src/lib/stepText.ts`
- Test: `tests/BoCoach.Core.Tests/Data/` (el archivo de valores clave de game_data)

**Interfaces:**
- Produces: entidades `crop_rotation` (tech 12 del `.dat`, Imperial, molino, con `farm_food`) y `two_man_saw` (tech 221, Imperial, campamento maderero, con `gather`).

- [ ] **Step 1: test** `Rotacion_de_cultivos_y_sierra_de_dos_manos`: `crop_rotation` es tech, edad Imperial, `made_in` = `mill`, `FarmFood` = 175; `two_man_saw` es tech, edad Imperial, `made_in` = `lumber_camp`, con efecto de recolección de madera.
- [ ] **Step 2: correr** `dotnet test tests/BoCoach.Core.Tests --filter "FullyQualifiedName~Rotacion_de_cultivos"`. Expected: FAIL (no existe la entidad).
- [ ] **Step 3:** agregar `"crop_rotation": ("tech", 12)` y `"two_man_saw": ("tech", 221)` al extractor, correrlo con `tools/extractor/.venv/Scripts/python.exe` como en el (a) y revisar el diff de `v8.9.json` (solo deben aparecer las dos entidades y lo que dependa de ellas). Nombres: "Rotación de cultivos" y "Sierra de dos manos" en los dos `stepText`.
- [ ] **Step 4: correr** `dotnet test tests/BoCoach.Core.Tests` y `cd web && npm test`. Expected: todo en verde.
- [ ] **Step 5: commit** "game_data: rotación de cultivos y sierra de dos manos".

### Task 2: cursor de objetivos, objetivos que no se pueden y la postura

**Files:**
- Modify: `src/BoCoach.Core/Assisted/GoalState.cs`, `AgeRule.cs`, `AssistedGenerator.cs`
- Test: `tests/BoCoach.Core.Tests/Assisted/AssistedGeneratorTests.cs`, `RuleTests.cs`

**Interfaces:**
- Produces en `GoalState`:
  - `int? ActiveGoal(PlanView view)`: el índice del primer objetivo que no arrancó todos sus pasos (null si todos arrancaron). Un objetivo arrancó si: edad, su clic está en la foto o en `Added`; unidad, su paso de entrenar está; centros urbanos, todos sus pasos de construir están; tecnología, su paso de investigar está. Un objetivo salteado (ver abajo) cuenta como arrancado.
  - `IReadOnlySet<int> Skipped` y `string? SkipReason(int index)`: los objetivos que no se pueden, calculados una vez al crear el estado (`GoalState.Plan` y la civ ya están).
  - `IReadOnlySet<string> WantedBuildings`: los edificios que piden los objetivos (el `made_in` de cada unidad y tecnología pedida), para que `AgeRule.Cheapest` los prefiera.
- `AssistedGenerator`: `GoalResult` para todos los tipos (inicio y fin: edad, el clic; unidad, su paso de entrenar; centros urbanos, el primero y el último; tecnología, su investigación). Ya no existe "Este objetivo todavía no se puede generar." Postura: aldeanos si no se llegó a `until`, más la unidad del último objetivo de unidades, y la edad siguiente a la **más alta** de los objetivos de edad.

- [ ] **Step 1: tests**:
  - `Un_objetivo_que_la_civ_no_tiene_se_saltea`: incas, `Unit = "scout_cavalry", Count = 5, Buildings = 1` después de Feudal: el problema dice "Los incas no tienen" (nombre de la civ en castellano, de `CivBonuses.Names["mx"]`) y el nombre de la unidad o del edificio; Castillos, que va después, llega igual.
  - `Una_unidad_sin_su_edad_antes_es_un_problema` (Review Focus 2): francos, objetivos `[Feudal, 5 caballeros en 1 establo]`: el problema de los caballeros dice "Pedí antes la Edad de los Castillos"; Feudal llega; la BO valida.
  - `La_postura_sale_de_la_edad_mas_alta_y_de_la_ultima_unidad`: objetivos `[Imperial, Feudal]` dan `NextAge` null (no hay edad después de Imperial); `[Feudal, 10 exploradores, Castillos]` dan cola `villager` y `scout_cavalry` y `NextAge` = `imperial_age`.
  - En `RuleTests`: `Para_Castillos_prefiere_el_establo_que_piden_los_objetivos`: foto en Feudal con herrería hecha y un objetivo de exploradores más abajo: el edificio que pide `AgeRule` es `stable`.
- [ ] **Step 2: correr** `dotnet test tests/BoCoach.Core.Tests --filter "FullyQualifiedName~Assisted"`. Expected: FAIL en los cuatro.
- [ ] **Step 3: implementar.** Un objetivo se saltea si la civ no lo puede hacer (`GameData.CanMake` de la unidad, de su edificio o de la tecnología), o si su edad (la de la unidad, la de su edificio o la de la tecnología; Castillos para centros urbanos) es mayor que la más alta que piden los objetivos de edad de antes (la Edad Oscura siempre está). `AgeRule` trabaja la edad solo si el objetivo activo es esa edad; las edades intermedias que hagan falta para ella las sigue clicando como hoy. `AgeRule.Cheapest`: entre los edificios que faltan para la edad, primero los de `WantedBuildings`, después el más barato.
- [ ] **Step 4: correr** `dotnet test tests/BoCoach.Core.Tests`. Expected: PASS (las pruebas del (c) siguen igual: sin objetivos nuevos, el cursor es el de antes).
- [ ] **Step 5: commit** "Asistido: el cursor de objetivos, los que no se pueden y la postura".

### Task 3: objetivos de unidades

**Files:**
- Create: `src/BoCoach.Core/Assisted/UnitRule.cs`
- Modify: `GoalPolicy.cs` (orden: apertura, casas, campamentos, comida, edad, unidades, centros urbanos, tecnología, mejoras eco, aldeanos), `VillagerRule.cs` (demanda de la producción continua), `HouseRule.cs` (margen)
- Test: `RuleTests.cs`, `AssistedGeneratorTests.cs`

**Interfaces:**
- Consumes: `GoalState.ActiveGoal`, `Skipped` (Tarea 2), `GoalState.Builder`, `Affordable`, `Upcoming`, `Planned`.
- Produces: `UnitRule : IGoalRule`; en `GoalState`, `int Producers(PlanView view)`: los edificios que producen sin parar (centros urbanos más los edificios de los pasos de entrenar unidades sin terminar).

Comportamiento (spec 3.3, "Unidades"):
- Activo el objetivo de unidades: el edificio es el `made_in` de la unidad. Cuenta los hechos, en obra y pedidos de esa clave; mientras haya menos que `Buildings`, agrega uno (desde `Builder`, de vuelta a donde estaba) si la edad de la foto lo permite y alcanza; si no, su costo va a `Upcoming`. Uno por consulta.
- El paso de entrenar (`Count` = `goal.Count`) va cuando hay al menos uno de esos edificios **hecho** y los demás ya pedidos: así arranca enseguida y los otros se suman al terminar (el Planner produce en paralelo con varios edificios de la misma clave).
- `Planned` suma lo que falta pagar de las unidades (costo × `Count`) mientras el objetivo está activo; el siguiente objetivo también suma a `Planned` (spec 3.5: "el objetivo activo y el siguiente").
- `VillagerRule.Missing`: los pasos de entrenar unidades sin terminar no suman su costo entero sino lo que producen en el horizonte: `min(Remaining, edificios × H / tiempo de la unidad) × costo`.
- `HouseRule.Margin`: 3 con un solo edificio que produce, uno más por cada otro (`2 + Producers`).

- [ ] **Step 1: tests**:
  - `RuleTests.Las_unidades_esperan_su_primer_edificio`: foto en Feudal sin establo y con 175 de madera: sale un `Build stable` y ningún `Train`; con un establo hecho y otro en obra (objetivo de 2): sale el `Train scout_cavalry` con `Count` 20 y ningún establo más.
  - `AssistedGeneratorTests.Veinte_exploradores_con_dos_establos`: francos, `until` 60, `[Feudal, 20 exploradores en 2 establos]`: sin problemas, `AssertConsistent`, sin parados (`IdleReason.Exhausted`), y los 20 exploradores salen (el paso termina).
  - `Mas_establos_nunca_terminan_mas_tarde` (spec 9): con 1, 2 y 3 establos, el fin del paso de exploradores no sube.
  - `Dos_objetivos_de_unidades_comparten_los_edificios` (Review Focus 1): `[Feudal, 10 exploradores en 2 establos, Castillos, 10 caballeros en 2 establos]`: la BO tiene 2 pasos de construir establo (no 4) y los caballeros terminan.
- [ ] **Step 2: correr** `dotnet test tests/BoCoach.Core.Tests --filter "FullyQualifiedName~Assisted"`. Expected: FAIL.
- [ ] **Step 3: implementar** `UnitRule`, `Producers`, la demanda en el horizonte y el margen de casas.
- [ ] **Step 4: correr** `dotnet test tests/BoCoach.Core.Tests`. Expected: PASS, con el barrido `En_todas_las_civs_llega_sin_parados_y_da_lo_mismo` en verde.
- [ ] **Step 5: commit** "Asistido: objetivos de unidades con sus edificios".

### Task 4: centros urbanos extra

**Files:**
- Create: `src/BoCoach.Core/Assisted/TownCenterRule.cs`
- Modify: `GoalState.cs` (centros urbanos y tiempo efectivo de un aldeano), `VillagerRule.cs`, `AgeRule.cs`
- Test: `RuleTests.cs`, `AssistedGeneratorTests.cs`

**Interfaces:**
- Produces en `GoalState`: `int TownCenters(PlanView view)` (los hechos de `town_center`); `double VillagerInterval(PlanView view)` = `VillagerTime(view.Age) / TownCenters(view)`; `bool TownCenterFree(PlanView view)`: algún centro urbano sin trabajo (`view.Busy["town_center"]` menor que los hechos) y sin aldeanos por encolar (`QueuedVillagers` = 0).

Comportamiento (spec 3.3, "Centros urbanos"):
- Activo el objetivo: `Planned` suma el costo de los que faltan (madera y piedra: la piedra va primero en el reparto, como el oro; el primer aldeano a la piedra hace el campamento minero si no hay). Desde Castillos, agrega uno por consulta cuando alcanza, con 3 constructores desde `Builder`, hasta llegar al total (`town_centers` cuenta el inicial).
- Varios centros urbanos: `VillagerRule` usa `VillagerInterval` donde hoy usa `VillagerTime` (horizonte, lo que junta cada aldeano del tramo, la espera del campamento y de la granja) y el tramo llega a `MaxTramo × TownCenters`; `TownCenterResearching` frena los tramos solo si investigan todos los centros urbanos. `AgeRule` agrega el clic con `TownCenterFree` en vez de "sin nada en producción" (Review Focus 3: hoy `InProduction` cuenta también las unidades militares y el clic no saldría nunca con un establo produciendo).

- [ ] **Step 1: tests**:
  - `RuleTests.El_centro_urbano_espera_Castillos_y_su_costo`: en Feudal no agrega nada y `Planned` tiene 275 de madera y 100 de piedra por cada uno que falta; en Castillos con 275 y 100 en el stock, sale `Build town_center` con `Builders` 3.
  - `RuleTests.El_clic_sale_con_un_establo_produciendo`: foto con un centro urbano libre, sin aldeanos en cola, el costo de Castillos juntado y un explorador en producción: sale el clic.
  - `AssistedGeneratorTests.Tres_centros_urbanos_llegan_antes_a_los_aldeanos`: francos, `until` 100, `[Feudal, Castillos, 3 centros urbanos]` contra `[Feudal, Castillos]`: con los 3, el aldeano 100 nace antes (última muestra de la línea de tiempo con 100 aldeanos); `AssertConsistent` y sin parados.
- [ ] **Step 2: correr** `dotnet test tests/BoCoach.Core.Tests --filter "FullyQualifiedName~Assisted"`. Expected: FAIL.
- [ ] **Step 3: implementar.**
- [ ] **Step 4: correr** `dotnet test tests/BoCoach.Core.Tests`. Expected: PASS (barrido incluido).
- [ ] **Step 5: commit** "Asistido: centros urbanos extra".

### Task 5: tecnologías a pedido

**Files:**
- Create: `src/BoCoach.Core/Assisted/TechRule.cs`
- Modify: `GoalState.cs`, `AgeRule.cs` (la regla del clic pasa a un ayudante compartido)
- Test: `RuleTests.cs`, `AssistedGeneratorTests.cs`

**Interfaces:**
- Produces en `GoalState`: `bool CanResearchNow(PlanView view, string tech)`: su edificio hecho y con una instancia libre (`Busy` menor que los hechos), la edad alcanzada y el costo en el stock. Para las del centro urbano, `TownCenterFree` en vez de la instancia libre, y mientras no alcanza se corta el tramo como para el clic (el ayudante que sale de `AgeRule`: `VillagersBeforeClick` y despertar).

Comportamiento (spec 3.3, "Tecnología"):
- Activo el objetivo: si su edificio no está hecho, en obra ni pedido, lo agrega como cualquier edificio (edad, costo, `Builder`). La investigación va cuando `CanResearchNow`; mientras tanto su costo va a `Planned` y la regla pide que la despierten (15 s si el edificio está ocupado).

- [ ] **Step 1: tests**:
  - `AssistedGeneratorTests.Linea_de_sangre_despues_del_establo`: francos, `[Feudal, línea de sangre]`: la BO tiene el establo antes de `Research bloodlines`, la investigación termina, `AssertConsistent`.
  - `La_tecnologia_espera_el_establo_libre` (Review Focus 5): `[Feudal, 20 exploradores en 1 establo, línea de sangre]`: la investigación arranca después del último explorador o en un hueco del establo, sin parados ni bloqueos de pasos que no sean de unidades, `AssertConsistent`.
  - `El_telar_a_pedido_no_frena_al_centro_urbano`: `[telar, Feudal]`: el telar sale con el centro urbano libre y Feudal llega; sin parados.
- [ ] **Step 2: correr** `dotnet test tests/BoCoach.Core.Tests --filter "FullyQualifiedName~Assisted"`. Expected: FAIL.
- [ ] **Step 3: implementar.**
- [ ] **Step 4: correr** `dotnet test tests/BoCoach.Core.Tests`. Expected: PASS.
- [ ] **Step 5: commit** "Asistido: tecnologías a pedido".

### Task 6: mejoras eco automáticas

**Files:**
- Create: `src/BoCoach.Core/Assisted/EcoRule.cs`
- Modify: `GoalPolicy.cs`, `AgeRule.cs` (el telar antes de Feudal)
- Test: `RuleTests.cs`, `AssistedGeneratorTests.cs`

Comportamiento (spec 3.6), solo con `eco_upgrades`:
- Candidatas, en este orden: hachas de doble filo (3 en madera), collera (6 granjeros), minería de oro (3 en oro), minería de piedra (3 en piedra), carretilla (15 aldeanos o más), sierra de arco, arado pesado, minería de pozo de oro y de piedra, carreta (15 o más, Castillos), sierra de dos manos y rotación de cultivos (Imperial). Solo las que la civ tiene y no le salen gratis (costo cero en game_data: carretilla de los vikingos, collera de los francos), de la edad ya alcanzada, sin hacer ni pedidas, una por consulta, cuando `CanResearchNow`.
- Prioridad debajo del objetivo activo: no se compra si con el objetivo activo de edad listo (sus 2 edificios) le falta menos que el tiempo de la investigación para juntar el costo (`SecondsToAfford` de `AgeRule`), ni si el costo de la edad ya está juntado y el clic espera.
- Las del centro urbano (carretilla, carreta) además usan el corte de tramo de la Tarea 5 y nunca van entre que se junta el costo de una edad y su clic.
- El telar va antes del clic de Feudal: con `eco_upgrades`, cuando el clic de Feudal está por salir (centro urbano libre y costo juntado) y el telar no está hecho ni pedido, sale el telar y el clic en la consulta siguiente (el telar termina en 25 s; la regla pide que la despierten entonces).

- [ ] **Step 1: tests**:
  - `RuleTests.Las_hachas_con_tres_en_la_madera`: Feudal, campamento maderero hecho, 3 en madera, 100 de comida y 50 de madera libres: sale `Research double_bit_axe`; con 2 en madera, no.
  - `AssistedGeneratorTests.Con_mejoras_eco_compra_las_de_siempre`: francos, `until` 100, `EcoUpgrades` true, `[Feudal, Castillos]`: la BO investiga hachas, minería de oro y carretilla (la collera de los francos es gratis: no aparece), el telar antes del clic de Feudal, ninguna carretilla entre que se junta el costo de una edad y su clic, `AssertConsistent` y sin parados.
  - `Con_pocos_aldeanos_no_compra_carretilla` (Review Focus 4): `until` 13, `EcoUpgrades` true, `[Feudal, Castillos, Imperial]`: sin carretilla, llegan las tres edades.
  - `Las_mejoras_no_atrasan_las_edades_de_mas`: francos, `until` 100, `[Feudal, Castillos]`: con `EcoUpgrades`, el clic de Castillos no sale más de 60 s después que sin mejoras.
- [ ] **Step 2: correr** `dotnet test tests/BoCoach.Core.Tests --filter "FullyQualifiedName~Assisted"`. Expected: FAIL.
- [ ] **Step 3: implementar.**
- [ ] **Step 4: correr** `dotnet test tests/BoCoach.Core.Tests`. Expected: PASS.
- [ ] **Step 5: commit** "Asistido: mejoras eco automáticas".

### Task 7: el criterio de la spec, el barrido y las docs

**Files:**
- Test: `AssistedGeneratorTests.cs`
- Modify: `PLAN.md` (D12), `CLAUDE.md` (renglón de D12)

- [ ] **Step 1: tests**:
  - `El_ejemplo_de_la_spec_entero`: francos, `until` 120, `EcoUpgrades` true, `[Feudal, 20 exploradores en 2 establos, Castillos a 32, 3 centros urbanos, línea de sangre, Imperial]`: sin problemas, `AssertConsistent`, sin parados, la BO valida.
  - `Agregar_un_objetivo_al_final_no_atrasa_a_los_de_arriba` (spec 9): la misma lista sin Imperial y con Imperial: cada objetivo de arriba termina como mucho 10 s más tarde.
  - El barrido `En_todas_las_civs_llega_sin_parados_y_da_lo_mismo` suma la fila `[Feudal, 10 de la unidad militar típica de la civ que pueda hacer (la primera de exploradores, arqueros o milicia), Castillos]` con `until` 60; las civs que no pueden ninguna se saltean.
  - `Genera_rapido`: el ejemplo de la spec entero tarda menos de 250 ms después del calentamiento.
- [ ] **Step 2: correr** `dotnet test tests/BoCoach.Core.Tests --filter "FullyQualifiedName~Assisted"`. Si algo falla es un error de una regla: se arregla con un test que lo reproduzca primero.
- [ ] **Step 3: correr** `dotnet test` (toda la solución) y `cd web && npm test`. Expected: todo en verde.
- [ ] **Step 4: docs.** `PLAN.md` D12: "(d) hecho el <fecha>" con las reglas nuevas, los tiempos del ejemplo de la spec y lo que queda para (e). `CLAUDE.md`: el renglón de D12.
- [ ] **Step 5: commit** "Asistido: el ejemplo entero de la spec y el barrido con unidades (D12 d)".
