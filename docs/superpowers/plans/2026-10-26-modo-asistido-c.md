# D12 (c): el generador base, plan de implementación

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** que una BO con el campo `assisted` (apertura, aldeanos hasta N y edades) se genere sola con una política dentro del Planner, por tramos de aldeanos, con casas, campamentos, la comida que se agota y los clics de edad, y que la BO que sale simulada de nuevo dé lo mismo.

**Architecture:** `AssistedPlan` es el modelo del campo `assisted`. `AssistedGenerator.Generate(bo)` corre `Planner.Simulate(bo vacía, GoalPolicy)` con la comida finita del mapa y devuelve la BO generada (pasos en totales, postura), el porqué de cada paso, la hora de cada objetivo y los problemas. `GoalPolicy` arma lo que agrega con reglas chicas en `Core/Assisted/`, cada una probada con fotos (`PlanView`) armadas a mano.

**Tech Stack:** C# .NET 10 (`BoCoach.Core`, xunit en `BoCoach.Core.Tests`); TypeScript solo para copiar en la web el requisito de Imperial (`web/src/lib/editor/constraints.ts`, vitest).

**Spec:** `docs/superpowers/specs/2026-10-26-modo-asistido-design.md` (secciones 2, 3, 5, 8, 9 y 10, sub-paso c).

## Global Constraints

- Formato: campo opcional `assisted` en la BO, sin subir `schema_version`; propiedades `set` (con `init`, los serializadores generados dejan en null o 0 lo que falta). Rangos: `until` de 1 a 200; `count` de 1 a 200; `buildings` de 1 a 10; `town_centers` de 2 a 10; cada objetivo con exactamente uno de `age`, `unit`, `town_centers`, `tech`; `berries` es `"auto"` o `"no"`.
- Este sub-paso genera la apertura, aldeanos hasta N por tramos, casas, campamentos, molino, la comida (ovejas, jabalí, bayas, granjas) y las edades. Los objetivos de unidades, centros urbanos y tecnologías, y las mejoras eco, son del (d): acá dan el problema "Este objetivo todavía no se puede generar." y el resto sigue.
- Tramos (spec 3): se arma uno cuando no quedan aldeanos por encolar de los pasos de aldeanos anteriores; largo = los aldeanos hasta lo próximo que hay que hacer, con tope de 6; un paso por recurso; el campamento de un recurso nuevo lo hace el primer aldeano que va ahí, como paso mixto [1 aldeano → destino, construir el campamento desde el destino a N de población] (N = la población cuando sale ese aldeano), igual que el editor (D8 (7)).
- Casas: margen de 3 lugares (un centro urbano). Comida: ovejas, jabalí (si `boar`), bayas con molino (si `berries` es `"auto"`), granjas (una por granjero, cada una 60 de madera con el costo de la civ). Edades: los 2 edificios de la edad (los más baratos que falten) y el clic apenas alcanza, o a `at_pop`.
- La BO generada valida sin errores, y simulada de nuevo (`Planner.Simulate` con la misma comida finita) da los mismos pasos, bloqueos, línea de tiempo y parados.
- Textos y comentarios en castellano rioplatense, sin guiones largos. TDD: el test falla primero. Un commit por tarea, terminado con `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`. Rama `fase-9-refinamiento`.

## Review Focus

1. **Civs con edificios propios:** hunos (sin casas), incas (asentamiento por campamentos, molino y casa), armenios (carro de mulas por los campamentos). El generador tiene que usar lo que la civ tiene y la BO tiene que validar. Test en la Tarea 7.
2. **Mapa sin jabalí o sin bayas** (`maps.json` sin `hunt` o `berries`): la comida salta a la fuente siguiente sin aldeanos parados. Test en la Tarea 5.
3. **`until` más chico que la apertura** (por ejemplo 5): no entrena más de lo que pide, termina y no tira excepción. Test en la Tarea 4.
4. **Objetivos que este sub-paso no genera** (unidades, tecnologías, centros urbanos) mezclados con edades: problema en ese objetivo y las edades se generan igual. Test en la Tarea 6.
5. **Una edad con `at_pop` que nunca se alcanza** (más que `until` más los iniciales): problema "No llega antes de 1:00:00", sin colgarse. Test en la Tarea 6.

---

### Task 1: arreglos previos del Planner (revisión del b)

**Files:**
- Modify: `src/BoCoach.Core/Engine/PlanView.cs`, `src/BoCoach.Core/Engine/Planner.cs` (`View`, `AskPolicy`, `Pin`)
- Test: `tests/BoCoach.Core.Tests/Engine/PlannerPolicyTests.cs`

**Interfaces:**
- Produces: `PlanView` pasa de record posicional a record con propiedades `init` y valores por defecto (`Time` 0, `Stock` cero, `Population` 0, `InProduction` 0, `PopulationCap` 5, `Age` Dark, diccionarios y listas vacíos, `Birth` y `RanOut` false, `Food` null); los nombres no cambian. `PlanViewStep` suma `int Unfinished` (lo que está en producción o en obra) como último parámetro.
- `AskPolicy`: un `Train` o `Build` con `Count < 1` da `InvalidOperationException` que nombra el id. `Pin` no fija un paso si sus propias condiciones (`After`, `AtPop`, `AtTime`) no se cumplen en este segundo.

- [ ] **Step 1: Write the failing tests**

- `Una_foto_se_arma_a_mano_con_lo_que_hace_falta`: `new PlanView { Time = 300, Population = 14, PopulationCap = 15 }` compila, y `Villagers` está vacío.
- `Una_cantidad_menor_a_uno_da_error`: la política devuelve en la primera consulta un `Train` de milicia con `Count = 0` e id `"cero"`; `InvalidOperationException` cuyo mensaje contiene `'cero'`.
- `Un_paso_que_todavia_no_puede_arrancar_no_se_fija`: arranque estándar con tope 200, la política devuelve a los 0 s 3 aldeanos a ovejas y en el primer nacimiento un telar con `AtTime = 400`; el telar queda con `AtPop == null` y `AtTime == 400`.
- `La_foto_dice_lo_que_esta_en_produccion`: con 3 aldeanos a ovejas, en la consulta de los 0 s encadenada (la segunda), el paso de aldeanos tiene `Unfinished == 1`.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/BoCoach.Core.Tests --filter "FullyQualifiedName~PlannerPolicyTests"`
Expected: no compila (`PlanView` sin constructor vacío, `Unfinished` no existe).

- [ ] **Step 3: Implementar** lo de Interfaces. `ConditionsMet(step)` ya existe y sirve para `Pin`.

- [ ] **Step 4: Run** `dotnet test tests/BoCoach.Core.Tests`. Expected: todo en verde.

- [ ] **Step 5: Commit** `git commit -m "Planner: la foto se arma a mano, guarda de cantidades y sin fijar lo que no puede arrancar"` (con `PlanView.cs`, `Planner.cs` y el test).

---

### Task 2: el validador, desde la civ y con el requisito de Imperial

**Files:**
- Modify: `src/BoCoach.Core/BuildOrders/BuildOrderValidator.cs`, `web/src/lib/editor/constraints.ts`
- Test: `tests/BoCoach.Core.Tests/BuildOrders/BuildOrderValidatorTests.cs` (el que exista con ese nombre; si no, nuevo), `web/src/lib/editor/constraints.test.ts`

**Interfaces:**
- Produces: `AgeUpRequirements["imperial_age"] = (Age.Castle, ["university", "monastery", "siege_workshop", "castle"])` y `public static int AgeUpProgress(string ageTech, IEnumerable<string> builtKeys)`: cuántos de los 2 edificios que pide la edad hay (cada clave distinta cuenta 1; `castle` cuenta 2 para Imperial; tope 2). El validador lo usa en vez de `req.Buildings.Count(built.Contains)`.
- El aviso "La población nunca llega a N" arranca de `PlanStart.For(gameData.Civ(bo.Civ), gameData).Population`.
- Web: `AGE_UP` suma `imperial_age: { requires: "castle", buildings: ["university", "monastery", "siege_workshop", "castle"] }` y la cuenta trata a `castle` como 2 para Imperial, igual que el Core.

- [ ] **Step 1: Write the failing tests**

- `Los_chinos_arrancan_con_mas_poblacion`: BO de chinos con un paso de 1 aldeano a ovejas con `AtPop = 8` como primer paso: ningún aviso que contenga "nunca llega".
- `Imperial_pide_dos_edificios_de_Castillos_o_un_castillo`: una BO que llega a Castillos y hace clic en Imperial con solo una universidad avisa "hacen falta 2 edificios"; con universidad y monasterio no; con un castillo solo no.
- En `constraints.test.ts`: `allowedRange` de un clic en Imperial no deja ponerlo antes del segundo edificio de Castillos, y con un castillo alcanza.

- [ ] **Step 2: Run** `dotnet test tests/BoCoach.Core.Tests --filter "FullyQualifiedName~Validator"` y `cd web && npx vitest run src/lib/editor/constraints.test.ts`. Expected: fallan los tests nuevos.

- [ ] **Step 3: Implementar** lo de Interfaces.

- [ ] **Step 4: Run** `dotnet test` y `cd web && npm test`. Expected: todo en verde (los 127 de `roundTrip.test.ts` también).

- [ ] **Step 5: Commit** `git commit -m "Validador: Imperial pide sus edificios y la población arranca de la civ"`.

---

### Task 3: el modelo `assisted` y su validación

**Files:**
- Create: `src/BoCoach.Core/BuildOrders/AssistedPlan.cs`
- Modify: `src/BoCoach.Core/BuildOrders/BuildOrder.cs` (`Assisted`), `src/BoCoach.Core/BuildOrders/BuildOrderValidator.cs` (`ValidateAssisted`), `src/BoCoach.Core/CoreJson.cs` si hace falta registrar tipos
- Test: `tests/BoCoach.Core.Tests/BuildOrders/AssistedPlanTests.cs` (nuevo)

**Interfaces:**
- Produces (en `BoCoach.Core.BuildOrders`, todas con `set` y estos valores por defecto):
  ```csharp
  public sealed record AssistedPlan { AssistedOpening Opening = new(); AssistedVillagers Villagers = new(); bool EcoUpgrades = false; IReadOnlyList<AssistedGoal> Goals = []; }
  public sealed record AssistedOpening { bool Houses = true; int Sheep = 6; bool Boar = true; string Berries = "auto"; }
  public sealed record AssistedVillagers { int Until = 120; }
  public sealed record AssistedGoal { string? Age; int? AtPop; string? Unit; int? Count; int? Buildings; int? TownCenters; string? Tech; }
  ```
  `BuildOrder.Assisted : AssistedPlan?` (null = BO manual). JSON en snake_case (`eco_upgrades`, `at_pop`, `town_centers`).
- Validación (errores, en `Validate`): cada objetivo con exactamente uno de `age`, `unit`, `town_centers`, `tech`; `age` es `feudal_age`, `castle_age` o `imperial_age`; `unit` y `tech` son entidades de game_data del tipo que corresponde; `count` y `until` de 1 a 200; `buildings` de 1 a 10; `town_centers` de 2 a 10; `at_pop` de 1 a 200; `opening.sheep` de 0 a 20; `berries` `"auto"` o `"no"`. Que la civ no pueda hacer algo no es error (es un problema del generador).

- [ ] **Step 1: Write the failing tests**

- `El_ejemplo_de_la_spec_va_y_vuelve_por_JSON`: el JSON de la sección 2 de la spec (el bloque `assisted`) dentro de una BO se lee con `BuildOrderSerializer` y se vuelve a escribir igual (mismos valores), y una BO sin `assisted` queda con `Assisted == null`.
- `Los_valores_por_defecto`: `{"assisted": {}}` da apertura con casas, 6 a ovejas, jabalí, bayas `"auto"`, `until` 120 y sin objetivos.
- `Objetivos_mal_armados_dan_error` (Theory): `{}` (ningún tipo), `{"age":"feudal_age","unit":"archer"}` (dos), `{"age":"dark_age"}`, `{"unit":"knigth","count":5}`, `{"unit":"archer","count":0}`, `{"unit":"archer","count":5,"buildings":11}`, `{"town_centers":1}`, `{"tech":"villager"}`: cada uno da al menos un error de validación.
- `Until_fuera_de_rango_da_error`: `until` 0 y 201.

- [ ] **Step 2: Run** `dotnet test tests/BoCoach.Core.Tests --filter "FullyQualifiedName~AssistedPlanTests"`. Expected: no compila.

- [ ] **Step 3: Implementar** lo de Interfaces.

- [ ] **Step 4: Run** `dotnet test tests/BoCoach.Core.Tests`. Expected: todo en verde.

- [ ] **Step 5: Commit** `git commit -m "BO: el campo assisted del modo asistido y su validación"`.

---

### Task 4: el generador con la apertura, los tramos de aldeanos, las casas y los campamentos

**Files:**
- Create: `src/BoCoach.Core/Assisted/AssistedGenerator.cs`, `GoalPolicy.cs`, `GoalState.cs`, `CivBuildings.cs`, `OpeningRule.cs`, `HouseRule.cs`, `VillagerRule.cs` (todos en `src/BoCoach.Core/Assisted/`)
- Test: `tests/BoCoach.Core.Tests/Assisted/AssistedGeneratorTests.cs`, `tests/BoCoach.Core.Tests/Assisted/RuleTests.cs` (nuevos)

**Interfaces:**
- Produces (en `BoCoach.Core.Assisted`):
  ```csharp
  public sealed record GoalResult(int Index, double? Start, double? End, string? Problem);
  public sealed record AssistedResult(BuildOrder BuildOrder, IReadOnlyDictionary<string, string> Reasons,
      IReadOnlyList<GoalResult> Goals, IReadOnlyList<string> Problems, PlanResult Plan);
  public sealed class AssistedGenerator(GameData data, Maps maps)
  {
      public const int Version = 1;
      public static PlannerSettings Settings(GameData data, Maps maps, BuildOrder bo); // Food = FoodSupply.For(data, maps.Get(bo.Map))
      public AssistedResult Generate(BuildOrder bo); // ArgumentException si bo.Assisted es null
  }
  ```
  `Generate` ignora los pasos que traiga la BO: simula `bo with { Steps = [] }` con `GoalPolicy` y `Settings`, y devuelve `bo` con los pasos del resultado pasados a totales (`LivePlan.CountsToTotals`) y la postura (Tarea 6; acá `null`).
- Internas: `GoalState` (contador de ids `s1`, `s2`... en el orden en que se agregan; `Reasons`; banderas de cada regla), `CivBuildings.For(GameData, CivBonuses, string key) -> string?` (la clave que la civ construye para cumplir `key`: ella misma si `CanMake`, si no una entidad que la civ hace y la tiene en `CountsAs`; null si nada), y reglas con `IEnumerable<Step> Next(PlanView view, GoalState state)`. `GoalPolicy.Next` las corre en este orden: apertura (solo en la primera consulta), casas, comida (Tarea 5), edad (Tarea 6), aldeanos.
- `OpeningRule`: con `Houses` y la civ con casas, [2 casas con todos los sin tarea como constructores, a las ovejas]; si no, [mover los sin tarea a las ovejas]. Razón: "Apertura: ...".
- `HouseRule`: si la civ hace casas, la población más lo que está en producción más los aldeanos que faltan encolar de los pasos pendientes llega al tope menos 3, el tope es menor a 200 y no hay casa pendiente ni en obra: una casa desde la madera a la madera (si no hay leñadores, desde la fuente con más aldeanos a esa fuente). Razón: "Casa: quedan N lugares".
- `VillagerRule` (tramos):
  - Arma un tramo solo si ningún paso de aldeanos tiene `Remaining > 0` y los aldeanos (en fuentes, sin tarea, construyendo y en producción) son menos que `until`.
  - Largo `k` = el mínimo entre 6, lo que falta para `until`, los aldeanos hasta la próxima casa si la regla de casas todavía no la pidió (`tope - 3 - población - en producción`, si es positivo) y, en la Tarea 6, los que entran antes del clic de edad. Como mínimo 1.
  - Reparto: las ovejas primero hasta tener `opening.Sheep` ahí (si queda comida en las ovejas); el resto, de a uno, al recurso con más segundos de trabajo faltantes en el horizonte `H = Math.Clamp(k × tiempo de un aldeano, 60, 180)`: faltante = demanda en H (comida de los aldeanos que se van a entrenar, madera de la próxima casa si va a hacer falta, lo que pidan las otras reglas en `GoalState.Planned`) menos stock menos lo que juntan los aldeanos actuales en H (`view.Rates`); cada aldeano asignado suma su tasa por el resto de H. Sin faltantes, madera. La comida va a la fuente de comida actual (`GoalState.FoodSource`, ovejas al principio).
  - Un paso por recurso, en el orden del primer aldeano asignado. Si el recurso no tiene su campamento (hecho, en obra o pedido) y lo necesita (madera: `lumber_camp`; oro y piedra: `mining_camp`, vía `CivBuildings.For`), el primer aldeano va en un paso mixto: [1 aldeano → recurso, construir el campamento desde el recurso al recurso con `AtPop = view.Population + view.InProduction + 1`], los dos con `Group = "g" + id del paso de aldeanos`; el resto del recurso, en otro paso.
  - Razones: "Aldeanos para la madera: faltan 120 en los próximos 2:00", "Aldeano N: campamento maderero".

- [ ] **Step 1: Write the failing tests**

En `RuleTests` (fotos armadas a mano con la Tarea 1):
- `La_casa_se_pide_con_tres_lugares`: población 12, en producción 1, tope 15, 4 leñadores, sin casas pendientes: `HouseRule` da una casa desde la madera.
- `Sin_casas_para_los_hunos`: lo mismo con la civ de los hunos: nada.
- `El_tramo_llena_las_ovejas_y_despues_reparte`: 3 en ovejas, apertura con 6, ningún paso de aldeanos pendiente, stock 0 de madera: el tramo empieza con 3 aldeanos a ovejas.
- `El_primer_aldeano_a_la_madera_hace_el_campamento`: sin campamento maderero y con faltante de madera: hay un paso mixto [1 aldeano a madera, campamento maderero desde la madera con `AtPop` = población + en producción + 1].

En `AssistedGeneratorTests`:
- `Genera_aldeanos_hasta_N_con_casas` (Theory con `"any"`, `"franks"`): `assisted` con `until = 30` y sin objetivos: la BO generada tiene 30 aldeanos al terminar (`Timeline[^1].Villagers == 30`), ningún bloqueo de población de más de 5 s en pasos de aldeanos, valida sin errores y da lo mismo simulada de nuevo con `AssistedGenerator.Settings`.
- `Los_pasos_de_aldeanos_salen_por_tramos`: con `until = 60`, menos de un paso de aldeanos cada 2 aldeanos (pasos de aldeanos ≤ 30) y ninguno de más de 6.
- `Until_chico_no_entrena_de_mas` (Review Focus 3): `until = 5` con la apertura de siempre: 5 aldeanos al final, sin excepción.
- `Sin_assisted_da_error`: `Generate` de una BO sin `assisted` tira `ArgumentException`.

- [ ] **Step 2: Run** `dotnet test tests/BoCoach.Core.Tests --filter "FullyQualifiedName~Assisted"`. Expected: no compila.

- [ ] **Step 3: Implementar** lo de Interfaces. El tiempo de un aldeano es `civ.Time(villager, age) / civ.WorkRate("town_center", age)`.

- [ ] **Step 4: Run** `dotnet test tests/BoCoach.Core.Tests`. Expected: todo en verde.

- [ ] **Step 5: Commit** `git commit -m "Asistido: el generador con la apertura, los tramos de aldeanos, las casas y los campamentos"`.

---

### Task 5: la comida que se acaba

**Files:**
- Create: `src/BoCoach.Core/Assisted/FoodRule.cs`
- Modify: `GoalPolicy.cs`, `GoalState.cs`, `VillagerRule.cs`
- Test: `tests/BoCoach.Core.Tests/Assisted/RuleTests.cs`, `AssistedGeneratorTests.cs`

**Interfaces:**
- Consumes: `PlanView.Food` (sub-paso b).
- Produces: `GoalState.FoodSource : GatherSource` (Sheep, después Hunt si `opening.Boar` y el mapa tiene caza, después Berries si `berries == "auto"` y hay bayas, después Farm). `FoodRule`:
  - Calcula cuándo se acaba la fuente actual: lo que queda (`Food.Remaining`) sobre lo que juntan sus aldeanos por segundo.
  - Con la siguiente fuente: si es Hunt o Berries, cuando falten 20 s o menos (Berries: más el tiempo del molino si no hay uno hecho, en obra o pedido) agrega [molino desde la fuente actual a las bayas, si hace falta] y [mover a todos los de la fuente actual a la siguiente]. Si es Farm, cuando falten 30 s o menos agrega [molino si no hay] y [una granja por cada aldeano de la fuente actual, desde esa fuente a la granja, todas a la vez (`Build farm`, `Count` = aldeanos, `From` = la fuente)].
  - Con `FoodSource == Farm`, los aldeanos nuevos que van a comida salen en el tramo como [k aldeanos a la granja, k granjas desde la granja a la granja] (las hace cada granjero sin granja). `GoalState.Planned` suma la madera de las granjas que se van a necesitar.
  - Razones: "Molino: las ovejas se acaban a las 6:10", "Granjas: las bayas se acaban a las 9:30".

- [ ] **Step 1: Write the failing tests**

En `RuleTests`:
- `Las_ovejas_que_se_acaban_pasan_al_jabali`: 6 en ovejas, `Food.Remaining[Sheep] = 30`, tasa 0,3: `FoodRule` da mover 6 de ovejas a caza y `FoodSource` pasa a Hunt.
- `Antes_de_las_bayas_el_molino`: 6 en caza con poca caza, sin molino: da [molino, mover 6 de caza a bayas].
- `Sin_bayas_en_el_mapa_va_a_las_granjas` (Review Focus 2): con `Food.Remaining[Berries] = 0` y la caza que se acaba: da [molino si falta, granjas desde la caza].

En `AssistedGeneratorTests`:
- `Con_la_comida_de_Arabia_nadie_queda_parado` (Theory con `"any"`, `"tatars"`, `"poles"`): `until = 60`, sin objetivos: ningún `PlanIdle` con `Exhausted`, hay pasos de granjas, valida sin errores y da lo mismo simulada de nuevo.
- `Un_mapa_sin_jabali_ni_bayas_va_directo_a_las_granjas`: con un `Maps` armado con un mapa sin `hunt` ni `berries` en `sources` y la BO con ese mapa: ningún paso a caza ni a bayas, ningún `Exhausted`.

- [ ] **Step 2: Run** `dotnet test tests/BoCoach.Core.Tests --filter "FullyQualifiedName~Assisted"`. Expected: fallan los nuevos.

- [ ] **Step 3: Implementar** lo de Interfaces.

- [ ] **Step 4: Run** `dotnet test tests/BoCoach.Core.Tests`. Expected: todo en verde.

- [ ] **Step 5: Commit** `git commit -m "Asistido: la comida que se acaba (jabalí, bayas con molino, granjas)"`.

---

### Task 6: las edades, los resultados y la postura

**Files:**
- Create: `src/BoCoach.Core/Assisted/AgeRule.cs`
- Modify: `GoalPolicy.cs`, `GoalState.cs`, `VillagerRule.cs`, `AssistedGenerator.cs`
- Test: `tests/BoCoach.Core.Tests/Assisted/RuleTests.cs`, `AssistedGeneratorTests.cs`

**Interfaces:**
- Consumes: `BuildOrderValidator.AgeUpRequirement` y `AgeUpProgress` (Tarea 2).
- Produces: `AgeRule` con el objetivo activo (el primer objetivo de edad sin clic agregado):
  - Si a los edificios de la edad (hechos, en obra o pedidos) les falta para llegar a 2 (`AgeUpProgress`), agrega el más barato que falte y que la civ pueda hacer (por madera más piedra), desde la madera a la madera. Razón: "Para Feudal hacen falta 2 edificios de la Edad Oscura".
  - Clic: con `AtPop` del objetivo, agrega el clic con ese `AtPop` en cuanto están pedidos los 2 edificios. Sin `AtPop` (lo antes posible), lo agrega cuando para cada recurso `stock + lo que juntan en 25 s >= costo` (25 s = lo que tarda el aldeano que está saliendo) y los 2 edificios están hechos o en obra. Razón: "Clic en Feudal: alcanza la comida".
  - Mientras haya un objetivo de edad activo, la demanda del reparto (`GoalState.Planned`) suma el costo de la edad, así van aldeanos al oro para Castillos (con su campamento minero).
  - `VillagerRule` acorta el tramo a los aldeanos que salen antes del clic estimado (`ceil(tiempo hasta alcanzar el costo / tiempo de un aldeano)`, entre 1 y 6), así el clic no queda en la cola detrás de 6 aldeanos.
  - Los objetivos de unidades, centros urbanos y tecnologías no se generan: `GoalResult.Problem = "Este objetivo todavía no se puede generar."`.
- `AssistedGenerator.Generate` completa: `Goals` (por cada objetivo: `Start` y `End` del paso del clic, o `Problem`; un clic que no terminó dentro de la simulación: "No llega antes de 1:00:00"), `Problems` (todos los de los objetivos), `Reasons` (de `GoalState`) y la postura: `Queue` con `"villager"` si no se llegó a `until`, y `NextAge` = la edad siguiente a la última de los objetivos (null después de Imperial).

- [ ] **Step 1: Write the failing tests**

En `RuleTests`:
- `Para_Feudal_faltan_dos_edificios`: con el objetivo Feudal activo, un campamento maderero hecho y nada más: `AgeRule` da un molino o un campamento minero (el que sea más barato; empate: el primero de la lista del validador que la civ pueda hacer).
- `El_clic_va_cuando_alcanza`: 480 de comida, 6 en ovejas a 0,3, los 2 edificios hechos: `AgeRule` da el clic en Feudal; con 300 de comida, nada.

En `AssistedGeneratorTests`:
- `Llega_a_Imperial` (Theory con `"any"`, `"franks"`): `until = 100` y objetivos Feudal, Castillos e Imperial: los tres con `End`, en orden, ningún problema, valida sin errores y da lo mismo simulada de nuevo.
- `Objetivos_que_todavia_no_se_generan` (Review Focus 4): objetivos [Feudal, 20 arqueros, Castillos]: el de los arqueros tiene el problema, Feudal y Castillos tienen `End`.
- `Una_edad_a_una_poblacion_que_nunca_llega` (Review Focus 5): `until = 20` y Feudal con `AtPop = 60`: el objetivo tiene el problema "No llega antes de 1:00:00".
- `La_postura_sale_de_los_objetivos`: con Feudal y Castillos y `until = 50`: `NextAge == "imperial_age"`.

- [ ] **Step 2: Run** `dotnet test tests/BoCoach.Core.Tests --filter "FullyQualifiedName~Assisted"`. Expected: fallan los nuevos.

- [ ] **Step 3: Implementar** lo de Interfaces.

- [ ] **Step 4: Run** `dotnet test tests/BoCoach.Core.Tests`. Expected: todo en verde.

- [ ] **Step 5: Commit** `git commit -m "Asistido: las edades, los resultados de cada objetivo y la postura"`.

---

### Task 7: las pruebas de la spec y las docs

**Files:**
- Test: `tests/BoCoach.Core.Tests/Assisted/AssistedGeneratorTests.cs`
- Modify (solo si una prueba muestra un error del generador): los archivos de `src/BoCoach.Core/Assisted/`
- Modify: `PLAN.md` (D12), `CLAUDE.md` ("Estado actual")

**Interfaces:**
- Consumes: todo lo anterior.

- [ ] **Step 1: Write the tests**

- `La_BO_generada_da_lo_mismo_en_cada_civ` (Theory con `"franks"`, `"incas"`, `"huns"`, `"chinese"`, `"armenians"`; Review Focus 1): `until = 100`, objetivos Feudal, Castillos e Imperial: valida sin errores, da lo mismo simulada de nuevo, y los hunos no tienen casas, los incas no tienen campamentos ni molino (usan su asentamiento) y los armenios no tienen campamentos.
- `El_centro_urbano_no_para`: franks, `until = 100`, Feudal y Castillos: cero segundos de bloqueos de población en pasos de aldeanos y, sumando los de recursos de pasos de aldeanos, 25 s o menos; ningún `PlanIdle` con `Exhausted`.
- `Contra_las_guias`: para cada guía de `tests/fixtures/buildorderguide` con el atributo `fastFeudal`, `uptime.feudalAge` y `pop.feudalAge`, generar con la civ de la guía, `until = 40` y Feudal con `AtPop = pop.feudalAge`; la diferencia entre la llegada a Feudal (`End`) y el `uptime` tiene mediana de 60 s o menos y ninguna de más de 180 s.
- `Genera_rapido`: franks, `until = 120`, Feudal, Castillos e Imperial: después de una corrida de calentamiento, `Generate` tarda menos de 250 ms (es el Core con JIT; la meta de 300 ms en la web se mide en el sub-paso e).

- [ ] **Step 2: Run** `dotnet test tests/BoCoach.Core.Tests --filter "FullyQualifiedName~Assisted"`. Si algo falla, es un error del generador: se arregla en su regla con un test que lo reproduzca primero y se anota en el registro.

- [ ] **Step 3: Run** `dotnet test` (toda la solución) y `cd web && npm test`. Expected: todo en verde.

- [ ] **Step 4: Docs.** `PLAN.md`, D12: "(c) hecho el <fecha>" con los tramos, las reglas, lo que queda para el (d) y lo que mostraron las pruebas contra las guías (la mediana real). `CLAUDE.md`, "Estado actual": un renglón de D12 (c).

- [ ] **Step 5: Commit** `git commit -m "Asistido: las pruebas contra las guías y por civ (D12 c)"` con los tests, lo arreglado y las docs.
