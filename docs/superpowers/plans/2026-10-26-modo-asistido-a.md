# D12 (a): comida finita en el Planner, plan de implementación

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** que el Planner pueda simular con la comida del mapa contada (ovejas, jabalíes, bayas y granjas que se agotan y se resiembran), como opción apagada por defecto, para que el generador del modo asistido no mande todo a las ovejas.

**Architecture:** el extractor suma a game_data la comida de cada objeto y lo que suman las mejoras de granja; `maps.json` dice cuántos objetos hay por mapa; `FoodSupply` junta las dos cosas; el Planner, si `PlannerSettings.Food` no es null, lleva lo que queda en una clase interna `FoodStocks` y registra los aldeanos parados en `PlanResult.Idle`. Con `Food` en null el código hace exactamente lo de hoy.

**Tech Stack:** C# .NET 10 (`BoCoach.Core`, xunit en `BoCoach.Core.Tests`), Python 3.12 con genieutils-py (`tools/extractor`, venv en `tools/extractor/.venv`).

**Spec:** `docs/superpowers/specs/2026-10-26-modo-asistido-design.md` (secciones 4, 5, 9 y 10, sub-paso a).

## Global Constraints

- `BoCoach.Core` es `net10.0` puro, sin Windows ni UI (`ArquitecturaTests` lo controla).
- Con `PlannerSettings.Food` en null, el Planner da exactamente los mismos resultados que hoy: el editor manual, el optimizador, las BO por civ, el coach y el replanificador en vivo no cambian.
- Valores verificados en el `.dat` el 2026-10-26: oveja (unidad 594) 100, jabalí (48) 340, arbusto de bayas (59) 125, granja 175 (recurso 36 de las civs reales; Gaia trae 0), collera (tech 14) +75, arado pesado (tech 13) +125. Teutones: granja a 36 de madera.
- Comida por mapa por defecto (Arabia): 8 ovejas, 2 jabalíes, 6 arbustos. Los ciervos y la pesca no cuentan.
- Los aldeanos de una fuente agotada quedan parados en esa fuente (no pasan a "sin tarea"). Cada granjero tiene su granja. La resiembra cuesta la madera de una granja con el costo de la civ y paga antes que los pasos.
- `data/maps.json` lo edita el usuario a mano: un valor de otro tipo se ignora (como hoy en `Maps`).
- Textos y comentarios en castellano rioplatense, sin guiones largos. TDD: el test falla primero. Un commit por tarea, terminado con `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- Se trabaja en la rama actual, `fase-9-refinamiento`.

## Review Focus

1. **game_data viejo sin `food_per_object` con la comida finita prendida:** tiene que dar un error que diga que falta correr el extractor, no simular con comida cero. Test en la Tarea 2.
2. **Mapa cuyo `sources` no tiene una fuente** (sin `hunt`): esa fuente vale 0 y los aldeanos que van ahí quedan parados desde el primer segundo, sin excepción. Tests en las Tareas 2 y 3.
3. **`food` mal escrito en `maps.json`** (texto, negativo, una fuente que no se cuenta): se ignora ese valor y queda el estándar. Test en la Tarea 2.
4. **Granjeros que se van de la granja y vuelven:** la comida nunca queda negativa ni hay excepción; el que vuelve trae una granja llena. Test en la Tarea 4.
5. **Una granja vacía mientras un paso espera madera:** la resiembra paga primero y la madera nunca queda negativa. Test en la Tarea 4.

---

### Task 1: game_data trae la comida de cada fuente y lo que suman las mejoras de granja

**Files:**
- Modify: `tools/extractor/extract.py` (constantes cerca de `VARIANTES_RECOLECCION`, `Extractor.entidad` para techs, `Extractor.exportar`)
- Modify: `data/game_data/v8.9.json` (se regenera)
- Modify: `src/BoCoach.Core/Data/GameData.cs`, `src/BoCoach.Core/Data/GameDataLoader.cs`
- Test: `tests/BoCoach.Core.Tests/Data/GameDataTests.cs`, `tests/BoCoach.Core.Tests/Data/GameDataLoaderTests.cs`

**Interfaces:**
- Produces: `GameData.FoodPerObject : IReadOnlyDictionary<GatherSource, double>` (vacío si el archivo no lo trae; claves Sheep, Hunt, Berries, Farm); constructor de `GameData` con un parámetro opcional nuevo al final, `IReadOnlyDictionary<GatherSource, double>? foodPerObject = null`. `Entity.FarmFood : double { get; init; }` (0 si no suma).
- JSON (snake_case, ya lo hace `CoreJsonContext`): en la raíz `"food_per_object": { "sheep": 100.0, "hunt": 340.0, "berries": 125.0, "farm": 175.0 }`; en `horse_collar` y `heavy_plow`, `"farm_food": 75.0` y `"farm_food": 125.0`.

- [ ] **Step 1: Write the failing tests**

En `GameDataTests`:

```csharp
[Fact]
public void Trae_la_comida_de_cada_fuente_y_lo_que_suman_las_mejoras_de_granja()
{
    var data = TestData.GameData;
    Assert.Equal(100, data.FoodPerObject[GatherSource.Sheep]);
    Assert.Equal(340, data.FoodPerObject[GatherSource.Hunt]);
    Assert.Equal(125, data.FoodPerObject[GatherSource.Berries]);
    Assert.Equal(175, data.FoodPerObject[GatherSource.Farm]);
    Assert.Equal(75, data.Get("horse_collar").FarmFood);
    Assert.Equal(125, data.Get("heavy_plow").FarmFood);
    Assert.Equal(0, data.Get("wheelbarrow").FarmFood);
}
```

En `GameDataLoaderTests`, con el mismo armado que `Parsea_un_archivo_valido`: `Una_fuente_desconocida_en_food_per_object_da_error` (con `"food_per_object": { "cows": 150 }`, `GameDataException` cuyo mensaje contiene `food_per_object`) y `Sin_food_per_object_queda_vacio` (el JSON válido de siempre: `data.FoodPerObject` vacío).

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/BoCoach.Core.Tests --filter "FullyQualifiedName~GameData"`
Expected: no compila (`FoodPerObject` y `FarmFood` no existen).

- [ ] **Step 3: Extractor**

En `extract.py`:
- `COMIDA_POR_OBJETO = {"sheep": 594, "hunt": 48, "berries": 59}` (oveja, jabalí, arbusto), `RECURSO_COMIDA_GRANJA = 36` y `CIV_CON_RECURSOS = 1` (Gaia trae 0 en el recurso 36).
- `Extractor.comida_por_objeto() -> dict[str, float]`: la cantidad del primer `resource_storages` de tipo 0 de cada unidad, más `"farm": self.dat.civs[CIV_CON_RECURSOS].resources[RECURSO_COMIDA_GRANJA]`.
- En `entidad()` para techs: `farm_food` = suma de `c.d` de los comandos con `c.type == 1`, `c.a == RECURSO_COMIDA_GRANJA` y `c.b == 1` (suma al recurso); se escribe solo si es mayor que 0.
- En `exportar()`: `"food_per_object": self.comida_por_objeto()`.

- [ ] **Step 4: Regenerar game_data y controlar que no cambió nada más**

Run: `tools/extractor/.venv/Scripts/python.exe tools/extractor/extract.py` (tarda unos 30 s)
Después: `git diff --stat data/` tiene que mostrar solo `data/game_data/v8.9.json`, y `git diff data/game_data/v8.9.json` solo líneas agregadas (`food_per_object` y los dos `farm_food`). Si cambia cualquier otra cosa (un parche del juego), parar y avisarle al usuario: eso es otra tarea.

- [ ] **Step 5: Core**

`GameDataDto` suma `Dictionary<string, double>? FoodPerObject`; `EntityDto` suma `double? FarmFood`. `GameDataLoader.Parse` convierte las claves con `Enum.TryParse<GatherSource>` (una desconocida da `GameDataException($"food_per_object: fuente desconocida '{name}'.")`) y pasa el diccionario al constructor. `ToEntity` pone `FarmFood = dto.FarmFood ?? 0`.

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/BoCoach.Core.Tests`
Expected: todo en verde.

- [ ] **Step 7: Commit**

```bash
git add tools/extractor/extract.py data/game_data/v8.9.json src/BoCoach.Core/Data/GameData.cs src/BoCoach.Core/Data/GameDataLoader.cs tests/BoCoach.Core.Tests/Data/GameDataTests.cs tests/BoCoach.Core.Tests/Data/GameDataLoaderTests.cs
git commit -m "game_data: la comida de cada fuente y lo que suman las mejoras de granja"
```

---

### Task 2: cuánta comida hay en cada mapa (`maps.json` y `FoodSupply`)

**Files:**
- Modify: `data/maps.json` (comentario del encabezado y `food` en `arabia`)
- Modify: `src/BoCoach.Core/Data/Maps.cs`
- Create: `src/BoCoach.Core/Engine/FoodSupply.cs`
- Test: `tests/BoCoach.Core.Tests/Data/MapsTests.cs`, `tests/BoCoach.Core.Tests/Engine/FoodSupplyTests.cs`

**Interfaces:**
- Consumes: `GameData.FoodPerObject` (Tarea 1).
- Produces:
  - `GameMap.Food : IReadOnlyDictionary<GatherSource, int> { get; init; }`, por defecto `GameMap.StandardFood` = `{ Sheep: 8, Hunt: 2, Berries: 6 }` (propiedad estática).
  - `public sealed record FoodSupply(IReadOnlyDictionary<GatherSource, double> Pools)` en `BoCoach.Core.Engine`, con `public double? FarmFood { get; init; }` (comida de una granja sin mejoras; null = la de game_data) y `public static FoodSupply For(GameData data, GameMap? map)`. `Pools` tiene siempre Sheep, Hunt y Berries; lo que no está en `Pools` es infinito.

- [ ] **Step 1: Write the failing tests**

En `MapsTests`:
- `Arabia_trae_la_comida_estandar_y_un_mapa_sin_food_usa_la_de_Arabia`: `Real.Get("arabia")!.Food` es 8, 2 y 6; `Maps.Parse("""{"maps":{"x":{"name":"X","sources":["sheep","wood"]}}}""").Get("x")!.Food` es igual a `GameMap.StandardFood`.
- `Un_valor_de_food_mal_escrito_se_ignora`: con `"food": { "sheep": "ocho", "hunt": -1, "berries": 4, "farm": 3 }` queda Sheep 8, Hunt 2, Berries 4 y no hay clave Farm.

En `FoodSupplyTests` (nuevo):
- `En_Arabia_hay_800_de_ovejas_680_de_jabali_y_750_de_bayas`: `FoodSupply.For(TestData.GameData, Maps.Load(Path.Combine(AppContext.BaseDirectory, "maps.json")).Get("arabia"))` da `Pools` 800, 680 y 750, y `FarmFood` null.
- `Sin_mapa_es_la_de_Arabia`: `For(data, null)` da lo mismo.
- `Una_fuente_que_el_mapa_no_tiene_vale_cero`: con `new GameMap("x", "X", false, new HashSet<GatherSource> { GatherSource.Sheep, GatherSource.Wood }, null)`, Hunt 0, Berries 0 y Sheep 800.
- `Sin_la_comida_en_game_data_avisa_que_falta_correr_el_extractor`: con `new GameData("t", TestData.GameData.Entities, TestData.GameData.GatherRates)`, `For` tira `InvalidOperationException` cuyo mensaje contiene `food_per_object`.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/BoCoach.Core.Tests --filter "FullyQualifiedName~Maps|FullyQualifiedName~FoodSupply"`
Expected: no compila (`GameMap.Food` y `FoodSupply` no existen).

- [ ] **Step 3: `maps.json`**

Al comentario del encabezado, una línea sobre `food`: cuántas ovejas (`sheep`), jabalíes (`hunt`) y arbustos de bayas (`berries`) tiene cada jugador; sin `food`, los de Arabia (8, 2 y 6); los ciervos no cuentan; lo usa la comida finita del modo asistido. En `arabia`: `"food": { "sheep": 8, "hunt": 2, "berries": 6 }`.

- [ ] **Step 4: `Maps.Read` y `FoodSupply`**

`Maps.Read` lee `food` si es un objeto: cada clave que sea Sheep, Hunt o Berries con un entero mayor o igual a 0 pisa el estándar; lo demás se ignora. `FoodSupply.For`: si `data.FoodPerObject` no tiene Sheep, Hunt, Berries y Farm, `InvalidOperationException("game_data no trae 'food_per_object': hay que correr el extractor.")`; si no, cada fuente contada vale cantidad × comida por objeto, con cantidad = `StandardFood` sin mapa, 0 si el mapa no tiene la fuente en `Sources`, y si no `map.Food` (o el estándar para la clave que falte).

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/BoCoach.Core.Tests`
Expected: todo en verde (también el `MapsTests` de siempre).

- [ ] **Step 6: Commit**

```bash
git add data/maps.json src/BoCoach.Core/Data/Maps.cs src/BoCoach.Core/Engine/FoodSupply.cs tests/BoCoach.Core.Tests/Data/MapsTests.cs tests/BoCoach.Core.Tests/Engine/FoodSupplyTests.cs
git commit -m "Mapas: cuánta comida tiene cada uno (FoodSupply)"
```

---

### Task 3: el Planner con fuentes que se agotan y aldeanos parados

**Files:**
- Modify: `src/BoCoach.Core/Engine/Planner.cs` (`PlannerSettings`, records de resultado, `Simulation`)
- Create: `src/BoCoach.Core/Engine/FoodStocks.cs`
- Test: `tests/BoCoach.Core.Tests/Engine/PlannerFoodTests.cs` (nuevo)

**Interfaces:**
- Consumes: `FoodSupply` (Tarea 2).
- Produces:
  - `PlannerSettings.Food : FoodSupply? { get; init; }` (null = infinita).
  - `public enum IdleReason { Exhausted, Reseed }` y `public sealed record PlanIdle(GatherSource Source, IdleReason Reason, double From, double To, int Villagers)` con `Duration => To - From`, al lado de `PlanBlock`.
  - `PlanResult.Idle : IReadOnlyList<PlanIdle> { get; init; } = []`.
  - `internal sealed class FoodStocks(FoodSupply supply)` con `static bool Counts(GatherSource source)` (en esta tarea Sheep, Hunt y Berries), `double Remaining(GatherSource source)`, `int Idle(GatherSource source, int villagers)` (cuántos de esos aldeanos no juntan nada este segundo) y `double Gather(GatherSource source, int villagers, double wanted)` (lo que juntan, a lo sumo lo que queda; devuelve exactamente `wanted` si alcanza, para que con comida de sobra los números sean los mismos que hoy).

- [ ] **Step 1: Write the failing tests**

En `PlannerFoodTests` (la tasa esperada sale de `new GatherModel(TestData.GameData, PlannerSettings.DefaultEfficiency).Rate(source, CivBonuses.None, Age.Dark, [])`; un paso `loom` con `AtTime` alto mantiene viva la simulación; arranque `PlanStart.Standard with { IdleVillagers = 0, Villagers = ..., Stock = new ResourceAmounts(200, 0, 0, 100) }`; `Arabia = new FoodSupply(new Dictionary<GatherSource, double> { [Sheep] = 800, [Hunt] = 680, [Berries] = 750 })`):

- `Con_comida_de_sobra_todas_las_BO_dan_lo_mismo_que_con_la_infinita` (Theory sobre todos los `.json` de `TestData.BuildOrdersDir` y de la carpeta `civs` de la salida): `Simulate` con `new PlannerSettings()` y con `Food = new FoodSupply(<1e9 en las tres>) { FarmFood = 1e9 }` dan los mismos `Steps` (Id, Start, End), los mismos `Blocks` y el mismo `Timeline`; los dos con `Idle` vacío.
- `Las_ovejas_se_acaban_y_los_pastores_quedan_parados`: 6 en ovejas, `loom` con `AtTime = 600`. Comida al final (`Timeline[^1].Stock.Food`) = 1000; un solo `PlanIdle` (Sheep, Exhausted, 6) con `From` entre `Math.Ceiling(800 / (6 * rate)) - 1` y `+ 1`, y `To >= 600`.
- `Mover_a_los_parados_al_jabali_los_pone_a_juntar`: 6 en ovejas, `Move` de 6 de Sheep a Hunt con `AtTime = 500`, `loom` con `AtTime = 1000`. Comida al final 1680; dos `PlanIdle`: el de Sheep con `To` entre 499 y 501, y uno de Hunt (Exhausted, 6).
- `Un_mapa_sin_jabali_deja_parados_a_los_que_van_al_jabali`: `Pools` con Hunt 0, 3 en Hunt, `loom` con `AtTime = 60`. Comida al final 200; un `PlanIdle` (Hunt, Exhausted, 3) con `From = 0`.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/BoCoach.Core.Tests --filter "FullyQualifiedName~PlannerFoodTests"`
Expected: no compila (`PlannerSettings.Food`, `PlanIdle` y `PlanResult.Idle` no existen).

- [ ] **Step 3: Implementar**

`Simulation` crea `FoodStocks` solo si `settings.Food` no es null. En `Gather()`, para cada fuente que `FoodStocks.Counts`: primero `Idle` (si es mayor que 0, `RecordIdle(source, IdleReason.Exhausted, idle)`) y después `_stock` suma `Gather(source, count, _rates[source] * count * Tick)`; las demás fuentes, como hoy. `RecordIdle` copia la forma de `Block`: si el último `PlanIdle` es de la misma fuente, motivo y cantidad y su `To >= _time`, lo estira hasta `_time + Tick`; si no, agrega uno. `Run` devuelve `Idle` en el `PlanResult`.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/BoCoach.Core.Tests`
Expected: todo en verde, incluido el resto de `PlannerTests`.

- [ ] **Step 5: Commit**

```bash
git add src/BoCoach.Core/Engine/Planner.cs src/BoCoach.Core/Engine/FoodStocks.cs tests/BoCoach.Core.Tests/Engine/PlannerFoodTests.cs
git commit -m "Planner: comida finita como opción, con los aldeanos parados"
```

---

### Task 4: granjas que se agotan y se resiembran, y docs

**Files:**
- Modify: `src/BoCoach.Core/Engine/FoodStocks.cs`, `src/BoCoach.Core/Engine/Planner.cs`
- Test: `tests/BoCoach.Core.Tests/Engine/PlannerFoodTests.cs`
- Modify: `PLAN.md` (D12), `CLAUDE.md` ("Estado actual")

**Interfaces:**
- Consumes: `FoodStocks`, `PlanIdle`, `IdleReason.Reseed` (Tarea 3); `Entity.FarmFood` y `GameData.FoodPerObject` (Tarea 1).
- Produces: `FoodStocks.Counts(Farm) == true`; `void SyncFarms(int farmers, double farmFood)` (una granja por granjero: el que llega trae una con `farmFood`, el que se va se lleva la última de la lista); `int Reseed(double wood, double cost, double farmFood)` (resiembra las vacías de a una mientras alcance la madera y devuelve cuántas); `Idle(Farm, n)` = granjeros con la granja vacía; `Gather(Farm, n, wanted)`: cada granja con comida da a lo sumo `wanted / n` (lo de un granjero) y lo que le queda, las vacías no dan nada, y si todas dan su parte devuelve exactamente `wanted`.

- [ ] **Step 1: Write the failing tests**

En `PlannerFoodTests`, con `Arabia with { FarmFood = 50 }` salvo que se diga otra cosa, `loom` con `AtTime = 600`, comida 0 y oro 100 al arrancar (`new ResourceAmounts(0, <madera>, 0, 100)`):

- `La_granja_se_resiembra_con_madera_y_sin_madera_el_granjero_espera`: 1 granjero, madera 60, comida 0. Al final comida 100 y madera 0; un `PlanIdle` (Farm, Reseed, 1) con `To >= 600`.
- `La_collera_agranda_las_granjas_nuevas`: 1 granjero, madera 0, `Researched = ["horse_collar"]`. Comida al final 125.
- `Los_teutones_resiembran_mas_barato`: 1 granjero, madera 72, `Simulate(items, start, TestData.GameData.Civ("teutons"))`. Comida al final 150, madera 0.
- `La_resiembra_paga_antes_que_los_pasos` (Review Focus 5): 1 granjero, 1 sin tarea, madera 60, `FarmFood = 0.0001`, un solo paso: casa (`From` null, `Builders = 1`) con `AtTime = 1`. Hay un `PlanBlock` de la casa por `Resources` con `Missing == Resource.Wood`, y en todo el `Timeline` la madera es mayor o igual a 0.
- `Los_granjeros_que_se_van_y_vuelven_traen_granja_llena` (Review Focus 4): 2 granjeros, madera 0, `FarmFood = 40`, `Move` de 1 de Farm a Wood con `AtTime = 5`, `Move` de 1 de Wood a Farm con `AtTime = 10`, `loom` con `AtTime = 400`. Comida al final entre `80 + 4 * rate` y `80 + 6 * rate` (rate de Farm), sin excepción.
- La Theory de equivalencia de la Tarea 3 sigue en verde (ahora con las granjas contadas y `FarmFood = 1e9`).

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/BoCoach.Core.Tests --filter "FullyQualifiedName~PlannerFoodTests"`
Expected: fallan los cinco nuevos (las granjas todavía no se cuentan: la comida no se corta en 100, 125 ni 150).

- [ ] **Step 3: Implementar**

En `Run`, después de `CompleteRunning()` y antes de avanzar los pasos (así la resiembra paga primero): si hay comida finita, `SyncFarms(_villagers[Farm], FarmFood())` y `Reseed(_stock.Wood, <madera de _civ.Cost(_data.Get("farm"), _age)>, FarmFood())`, descontando del stock la madera de las resembradas. `FarmFood()` = `_settings.Food.FarmFood ?? _data.FoodPerObject[Farm]` más el `FarmFood` de cada tecnología de `_researched`. En `Gather()`, la granja usa `IdleReason.Reseed`.

- [ ] **Step 4: Run all the tests**

Run: `dotnet test` (toda la solución) y `cd web && npm test` (el Core en WebAssembly simula las 127 BO).
Expected: todo en verde.

- [ ] **Step 5: Docs**

`PLAN.md`, D12: "(a) hecho el <fecha>" con lo que salió (valores del `.dat`, comida por mapa, parados en la fuente, una granja por granjero, la resiembra paga primero). `CLAUDE.md`, "Estado actual": un renglón de D12 con lo mismo, corto, y dónde está la spec.

- [ ] **Step 6: Commit**

```bash
git add src/BoCoach.Core/Engine/FoodStocks.cs src/BoCoach.Core/Engine/Planner.cs tests/BoCoach.Core.Tests/Engine/PlannerFoodTests.cs PLAN.md CLAUDE.md
git commit -m "Planner: las granjas se agotan y se resiembran con madera"
```
