# Plan de implementación: estadísticas reales de cruces, sub-paso (b) (Core: `MatchupStats` y `MatchupText`)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** El Core lee `data/matchup_stats.json` y responde, para una civ, un rival, un mapa, una franja de Elo y una de duración, la chance con su rango y las partidas; y arma los textos del renglón de la pantalla de carga, de la sección del informe y del porqué de la sugerencia, con la regla de honestidad de la spec.

**Architecture:** `Data/MatchupStats` (lectura con `JsonDocument`, tolerante; guarda los casilleros como `int[]` por clave y, si se le pasa una civ, solo lo de esa civ) y `Data/MatchupCell`/`DurationTrend`. `Engine/MatchupText` arma los textos con los nombres que le pasa quien llama (la app ya sabe nombrar las civs según el idioma del juego y los mapas salen de `data/maps.json`). Nada de la app ni de la web cambia en este sub-paso (eso es (c) y (d)).

**Tech Stack:** C# .NET 10, `System.Text.Json` (`JsonDocument`, sin reflexión: el Core se compila a WebAssembly recortado), xunit.

**Spec:** `docs/superpowers/specs/2026-10-25-matchup-stats-design.md` (secciones 3.2, 3.3, 4.5, 5.2, 5.3, 5.4 y 6).

## Global Constraints

- `BoCoach.Core` sigue sin referencias a Windows (`ArquitecturaTests`).
- Formato del JSON (spec 3.2 y lo que escribió el sub-paso (a)): `bands.elo` = [850, 1050, 1300, 1600], `bands.game_minutes` = [20, 30, 40, 55], `bands.maps` = ["arabia", "arena", "black_forest", "nomad"]; cada entrada, 180 casilleros `[p, desde, hasta, n]` (milésimas, partidas) en el orden mapa (todos + los de `bands.maps`) × Elo (todas + 5) × duración (todas + 5); claves de 1v1 `"a|b"` en orden alfabético con `p` de la primera; `team` por civ. En equipos `n` son apariciones de la civ (cada jugador cuenta), no partidas: decirlo en la documentación de `Team`.
- Índices de las consultas: 0 = todos o todas; 1..5 = cada franja; mapas 1..4 en el orden de `bands.maps`. Las franjas se leen del JSON; si faltan, las de arriba.
- Un archivo que falta, vacío o roto da `MatchupStats.Empty`, sin excepción (spec 6: la app no muestra error); una entrada con otro largo u otro tipo se ignora.
- Regla de honestidad (spec 4.5): parejo si el rango incluye el 50 %; tendencia por duración solo entre la franja más corta y la más larga con `n` > 0, y solo si sus rangos no se tocan.
- Textos en castellano rioplatense, sin guiones largos; porcentajes redondeados al entero, con espacio antes de `%` ("47 %").
- Commits chicos en `fase-9-refinamiento`, terminados en `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`. Tests: `dotnet test tests/BoCoach.Core.Tests -c Release` (Release, por si la app de Debug está abierta).

## Review Focus

1. **El mismo cruce pedido al revés** (rival primero): la chance es 1 - p y el rango se invierte. Test en la Tarea 2 (`Al_reves_invierte_la_chance_y_el_rango`).
2. **Espejo o civ sin datos** (las de Crónicas): null, nunca una excepción. Test en la Tarea 2 (`Espejo_y_civ_sin_datos_dan_null`).
3. **JSON roto o de otra versión:** vacío, sin excepción. Test en la Tarea 1 (`Un_archivo_roto_o_que_falta_da_vacio`).
4. **Nómada sin partidas 1v1:** el texto no afirma nada por duración y dice que es estimado. Test en la Tarea 4 (`Sin_partidas_en_el_mapa_dice_que_es_estimado`).
5. **Sin tu Elo:** usa todas las franjas y lo aclara. Test en la Tarea 4 (`Sin_tu_Elo_lo_aclara`).

---

### Task 1: `MatchupStats`: lectura, cabecera y franjas

**Files:**
- Create: `src/BoCoach.Core/Data/MatchupStats.cs`
- Create: `tests/BoCoach.Core.Tests/Data/MatchupJson.cs` (arma JSON de prueba)
- Test: `tests/BoCoach.Core.Tests/Data/MatchupStatsTests.cs`

**Interfaces:**
- Produces (namespace `BoCoach.Core.Data`):
  - `public sealed record MatchupCell(double P, double From, double To, int N)` con `public bool Even => From <= 0.5 && 0.5 <= To;` y `public MatchupCell Inverted => new(1 - P, 1 - To, 1 - From, N);`.
  - `public sealed class MatchupStats`: `static MatchupStats Empty`, `bool IsEmpty`, `string? SourceName`, `string? PatchLabel`, `DateOnly? From`, `DateOnly? To`, `bool PreviousMixed`, `IReadOnlyList<int> EloLimits`, `IReadOnlyList<int> GameMinuteLimits`, `IReadOnlyList<string> MapKeys`; `static MatchupStats Load(string path, string? onlyCiv = null)`, `static MatchupStats Parse(string json, string? onlyCiv = null)`; `int EloBand(int? elo)` (null o ≤ 0 → 0); `int DurationBand(TimeSpan gameTime)`; `int MapIndex(string? map)` (desconocido o null → 0); `string DurationLabel(int band)` ("antes de los 20 min", "de 20 a 30 min", ..., "después de los 55 min").
- Produces (`MatchupJson`, tests): `static string Build(IReadOnlyDictionary<string, Func<int, int, int, int[]>> oneVsOne, IReadOnlyDictionary<string, Func<int, int, int, int[]>> team, bool previousMixed = false)` (cada función da `[p, desde, hasta, n]` para mapa, Elo y duración) y `static int[] Cell(int p, int from, int to, int n)`.

- [ ] **Step 1: Tests**: `Lee_la_cabecera_y_las_franjas` (fuente, parche, fechas, `PreviousMixed`, límites y mapas tal como los escribe `MatchupJson.Build`), `Franjas_con_los_bordes` (`EloBand`: null → 0, 849 → 1, 850 → 2, 1600 → 5; `DurationBand`: 19:59 → 1, 20:00 → 2, 55:00 → 5; `MapIndex`: "arabia" → 1, "nomad" → 4, "megarandom" → 0, null → 0; `DurationLabel(1)` = "antes de los 20 min", `(3)` = "de 30 a 40 min", `(5)` = "después de los 55 min"), `Un_archivo_roto_o_que_falta_da_vacio` (ruta inexistente, "", "{", `{"one_vs_one": 3}` → `IsEmpty`, sin excepción), `Ignora_una_entrada_con_otro_largo` (una de 719 enteros no está; la vecina sí).
- [ ] **Step 2: Correrlos y ver que fallan** (no compila: falta `MatchupStats`).
- [ ] **Step 3: Implementar** con `JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip })` dentro de un `try` que atrapa `JsonException` (y cualquier dato de otro tipo da vacío o se ignora). Guardar cada entrada como `int[]` de 720.
- [ ] **Step 4: Correr y ver que pasan.**
- [ ] **Step 5: Commit** `Core: MatchupStats lee data/matchup_stats.json (cabecera y franjas)`.

### Task 2: Consultas de 1v1 y de equipos

**Files:**
- Modify: `src/BoCoach.Core/Data/MatchupStats.cs`, `tests/BoCoach.Core.Tests/BoCoach.Core.Tests.csproj` (copiar `data/matchup_stats.json` a la salida, como `maps.json`)
- Test: `tests/BoCoach.Core.Tests/Data/MatchupStatsTests.cs`

**Interfaces:**
- Produces: `MatchupCell? OneVsOne(string civ, string rival, int map, int eloBand, int durationBand)` (claves sin mayúsculas ni espacios; espejo o pareja que no está → null; si `civ` va segunda en la clave, `Inverted`); `MatchupCell? Team(string civ, int map, int eloBand, int durationBand)`; `bool HasCiv(string civ)`.

- [ ] **Step 1: Tests**: `Lee_cada_casillero_en_su_lugar` (con `MatchupJson` que pone en cada casillero p = 100·mapa + 10·elo + duración: `OneVsOne("britons", "franks", 2, 3, 4)` da P = 0,234), `Al_reves_invierte_la_chance_y_el_rango` (p 600, desde 550, hasta 650 → al revés 0,4, 0,35 y 0,45, mismo N), `Espejo_y_civ_sin_datos_dan_null` (`("franks", "franks")`, `("franks", "spartans")`, `Team("spartans")`), `Con_una_civ_guarda_solo_lo_suyo` (`Parse(json, onlyCiv: "franks")`: `britons|franks` sí, `britons|mayan` null, `Team("britons")` null), `Lee_el_archivo_real` (`data/matchup_stats.json`: `OneVsOne("britons", "franks", 0, 0, 0)` = P 0,497, desde 0,469, hasta 0,525, N 93; al revés P 0,503; `Team("georgians", 0, 0, 0)` P 0,523; ninguna pareja de 1v1 en Nómada tiene N > 0).
- [ ] **Step 2: Correrlos y ver que fallan.**
- [ ] **Step 3: Implementar.**
- [ ] **Step 4: Correr y ver que pasan.**
- [ ] **Step 5: Commit** `Core: consultas de cruces 1v1 y de civs por equipos`.

### Task 3: Tendencia por duración

**Files:**
- Modify: `src/BoCoach.Core/Data/MatchupStats.cs`
- Test: `tests/BoCoach.Core.Tests/Data/MatchupStatsTests.cs`

**Interfaces:**
- Produces: `public sealed record DurationTrend(int ShortBand, MatchupCell Short, int LongBand, MatchupCell Long)` con `public bool FavorsShort => Short.P > Long.P;`; `DurationTrend? OneVsOneTrend(string civ, string rival, int map, int eloBand)` y `DurationTrend? TeamTrend(string civ, int map, int eloBand)`: entre las franjas 1..5 con N > 0, la primera y la última; null si hay menos de dos o si sus rangos se tocan (`Short.From <= Long.To && Long.From <= Short.To`).

- [ ] **Step 1: Tests**: `Tendencia_a_partidas_cortas` (franja 1: 540 (491 a 587) con n 50; franja 5: 420 (380 a 460) con n 40 → `FavorsShort`, bandas 1 y 5), `Sin_tendencia_si_los_rangos_se_tocan` (franja 5 hasta 495 → null), `Usa_las_franjas_con_partidas` (franja 5 con n 0 y franja 4 con n 30 → `LongBand` 4), `Sin_partidas_no_hay_tendencia` (todas con n 0 → null), `Al_reves_la_tendencia_se_da_vuelta` (pedida al revés → `FavorsShort` false).
- [ ] **Step 2: Correrlos y ver que fallan.**
- [ ] **Step 3: Implementar.**
- [ ] **Step 4: Correr y ver que pasan.**
- [ ] **Step 5: Commit** `Core: tendencia de un cruce por duración con la regla de honestidad`.

### Task 4: `MatchupText`: el renglón de la pantalla de carga

**Files:**
- Create: `src/BoCoach.Core/Engine/MatchupText.cs`
- Test: `tests/BoCoach.Core.Tests/Engine/MatchupTextTests.cs`

**Interfaces:**
- Consumes: `MatchupStats`, `MatchupCell`, `DurationTrend`.
- Produces (namespace `BoCoach.Core.Engine`): `public sealed record MatchupQuery(string Civ, string? Rival, string? Map, int? Elo, bool Team)`; `public static class MatchupText` con `static string? Loading(MatchupStats stats, MatchupQuery query, Func<string, string> civName, Func<string, string?> mapName)`. `civName` da el nombre en minúsculas como la app ("francos"); `mapName` el nombre del mapa ("Arabia", "Nómada") o null.

Textos exactos (la primera letra en mayúscula; `<pct>` = `round(P·100)`, `<desde>`/`<hasta>` igual):
- 1v1: `"<Civ> contra <rival> en <Mapa> (Elo 1050): ganás <pct> % (<desde> a <hasta>)."`; parejo: `"...: parejo, ganás <pct> % (<desde> a <hasta>)."`.
- Tendencia: se agrega `" Mejor si dura poco: <pct corta> % <etiqueta corta>, <pct larga> % <etiqueta larga>."` (o `"Mejor si dura mucho: ..."`); sin tendencia y con al menos dos franjas con partidas: `" Parejo en cualquier duración."`.
- Mapa fuera de `bands.maps` o null: `"en todos los mapas"`. Sin Elo: `"(sin tu Elo)"` en vez de `"(Elo 1050)"`. General con N 0: se agrega `" Estimado: no hay partidas de este cruce en <Mapa>."` (sin `" en <Mapa>"` si es todos los mapas) y no se dice nada por duración.
- Equipos: `"<Civ> en <Mapa> por equipos (Elo 1200): <pct> % (<desde> a <hasta>)."`, con `" Mejor en partidas cortas."` / `" Mejor en partidas largas."` si hay tendencia.
- Civ sin datos: `"Sin datos de partidas clasificatorias de <civ>."`; 1v1 sin rival leído, o `stats.IsEmpty`: null.
- `PreviousMixed`: se agrega `" (Con datos del parche anterior.)"` al final.

- [ ] **Step 1: Tests**: un caso por renglón de arriba (`Uno_contra_uno_con_tendencia`, `Parejo_y_sin_tendencia`, `En_todos_los_mapas`, `Sin_tu_Elo_lo_aclara`, `Sin_partidas_en_el_mapa_dice_que_es_estimado`, `Por_equipos`, `Civ_sin_datos`, `Sin_rival_o_sin_datos_no_hay_renglon`, `Con_el_parche_anterior`), con el texto entero esperado.
- [ ] **Step 2: Correrlos y ver que fallan.**
- [ ] **Step 3: Implementar.**
- [ ] **Step 4: Correr y ver que pasan.**
- [ ] **Step 5: Commit** `Core: MatchupText arma el renglón del cruce para la pantalla de carga`.

### Task 5: `MatchupText`: informe y porqué de la sugerencia

**Files:**
- Modify: `src/BoCoach.Core/Engine/MatchupText.cs`
- Test: `tests/BoCoach.Core.Tests/Engine/MatchupTextTests.cs`

**Interfaces:**
- Produces: `static IReadOnlyList<string> Report(MatchupStats stats, MatchupQuery query, TimeSpan gameLength, Func<string, string> civName, Func<string, string?> mapName)` y `static string DurationReason(DurationTrend trend)`.

Textos exactos:
- `Report`, 1v1: renglón 1 = el de `Loading` sin la tendencia ni el aviso del parche; renglón 2 = `"Duró <m> min: a esa duración, en este cruce ganás el <pct> % (<desde> a <hasta>)."` (la franja de `DurationBand(gameLength)`; si esa franja tiene N 0, se agrega `" Estimado: pocas partidas así."`). Equipos: renglón 2 = `"Duró <m> min: a esa duración, los equipos con <civ> ganan el <pct> % (<desde> a <hasta>)."`. Sin datos o sin rival: lista vacía.
- `DurationReason`: `"el cruce se gana más en partidas cortas"` o `"el cruce se gana más en partidas largas"`.

- [ ] **Step 1: Tests**: `Informe_uno_contra_uno`, `Informe_por_equipos`, `Informe_sin_datos_vacio`, `Porque_de_la_sugerencia`.
- [ ] **Step 2: Correrlos y ver que fallan.**
- [ ] **Step 3: Implementar.**
- [ ] **Step 4: Correr toda la solución** (`dotnet test BoCoach.slnx -c Release`) y ver que pasa.
- [ ] **Step 5: Commit** `Core: MatchupText arma el informe del cruce y el porqué de la sugerencia`.

### Task 6: Documentos y push

**Files:**
- Modify: `PLAN.md` (D10: "(b) hecho" con lo que quedó), `CLAUDE.md` (renglón de D10: `MatchupStats` y `MatchupText`)

- [ ] **Step 1: Documentos** y commits separados (`PLAN: D10 (b) hecho`, `CLAUDE.md: MatchupStats y MatchupText`).
- [ ] **Step 2: Push** de `fase-9-refinamiento` y CI en verde (`curl -s "https://api.github.com/repos/Kyriokes/aoe2-bo-coach/actions/runs?per_page=1"`).
