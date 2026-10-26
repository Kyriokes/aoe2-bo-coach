# Plan de implementación: estadísticas reales de cruces, sub-paso (c) (la app)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** La app muestra el cruce en la pantalla de carga, suma la ventana del cruce al puntaje de la BO sugerida, agrega el cruce al informe de la partida, y tiene "Elo 1v1" y "Elo equipos" con un botón "Buscar" que los trae de la API oficial.

**Architecture:** Lo puro va en el Core: el mapa normalizado y las ventanas de `CivStyles`, el término de la ventana en `BuildOrderSuggester`, los renglones del cruce en `GameReport` y `EloLookup`, que lee la respuesta de la API (Core/Web, junto a `WebLibraryClient`). Vision arma el `MatchupQuery` desde la pantalla de carga (`LoadingReading.Matchup`), que usan la app y ReplaySession. La app suma los ajustes, los campos y el botón, la ventana para elegir entre homónimos, el renglón del overlay y el informe.

**Tech Stack:** C# .NET 10, WPF, CommunityToolkit.Mvvm, `System.Text.Json` (sin reflexión en el Core), `HttpClient`, xunit.

**Spec:** `docs/superpowers/specs/2026-10-25-matchup-stats-design.md` (secciones 5.1 a 5.4 y 6), con lo que dejaron (a) y (b) en `PLAN.md` D10.

## Global Constraints

- API oficial, verificada el 2026-10-25: `GET https://aoe-api.worldsedgelink.com/community/leaderboard/GetPersonalStat?title=age2&aliases=<["alias"] en JSON, escapado para la URL>`. Respuesta: `result.code` (0 bien; 9 `UNKNOWN_ALIASES`), `statGroups[]` (hasta 10, cada uno con `id` y `members[]` con `alias`, `profile_id`, `country`) y `leaderboardStats[]` (`statgroup_id`, `leaderboard_id`: **3 = 1v1 mapa aleatorio, 4 = equipos mapa aleatorio**, `rating`, `lastmatchdate` en segundos unix). Solo https, espera máxima de **10 s**, User-Agent `aoe2-bo-coach (+https://github.com/Kyriokes/aoe2-bo-coach)`. El nombre viaja solo cuando el usuario toca "Buscar".
- Ajustes nuevos en `settings.json`: `elo_1v1`, `elo_team` (enteros o ausentes) y `profile_id` (entero o ausente).
- Ventanas de los estilos (spec 5.3), franjas 1..5 = menos de 20, 20 a 30, 30 a 40, 40 a 55 y 55 o más minutos de juego: arqueros, exploradores y hombres de armas `[1, 2]`; fast castle `[3, 4]`. En `data/civ_styles.json` como `"windows"`, para que el usuario las revise.
- Puntaje de la sugerencia: `clamp((p_ventana - p_total) · 20, -2, 2)`, con `p_ventana` = promedio de las franjas de la ventana pesado por sus N (decisión de este plan: el JSON no trae el casillero de dos franjas juntas). Solo si el cruce tiene tendencia (`OneVsOneTrend`, la regla de honestidad); el porqué (`MatchupText.DurationReason`) solo si los puntos son 0,5 o más y la tendencia apunta al lado de la ventana (`FavorsShort` == promedio de la ventana < 3). Usa la tabla de 1v1 con el rival de enfrente y el **Elo 1v1** aunque la partida sea por equipos (la tabla es de 1v1).
- Renglón de la carga (spec 5.2): con 2 jugadores, 1v1 con el rival y el Elo 1v1; con más, equipos con el Elo de equipos. Vive como el consejo de hoy: la carga y el primer minuto (`SuggestionSeconds`).
- Los datos se cargan con `MatchupStats.Load(ruta, onlyCiv: tu civ)` una vez por civ (caché en `CoachData`): tarda unos 170 ms y se hace en la pantalla de carga.
- `BoCoach.Core` sigue sin Windows ni reflexión en el JSON (`ArquitecturaTests`, WebAssembly). Textos en castellano rioplatense, sin guiones largos.
- Commits chicos en `fase-9-refinamiento`, terminados en `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`. Tests en Release (`dotnet test <proyecto> -c Release`): la app de Debug puede estar abierta.

## Review Focus

1. **Nombre con homónimos** (la API devuelve hasta 10 con el mismo alias): se elige de una lista ordenada por la última partida, nunca el primero a ciegas. Test en la Tarea 6 (`Buscar_con_varios_deja_elegir`).
2. **Sin conexión, API caída o nombre que no existe:** un mensaje al lado del botón, sin excepción ni cartel. Tests en la Tarea 4 (`Busqueda_con_la_API_caida_da_un_error_legible`, `Nombre_que_no_existe_no_es_error`) y la Tarea 6 (`Buscar_sin_resultados_lo_dice`).
3. **El mapa que lee la pantalla de carga** viene como texto ("Black Forest"): tiene que caer en `black_forest`. Test en la Tarea 1 (`El_mapa_se_compara_sin_espacios_ni_mayusculas`).
4. **Sin el archivo de cruces o sin Elo:** la sugerencia queda como hoy y el renglón dice "sin tu Elo". Tests en la Tarea 2 (`Sin_datos_de_cruces_la_sugerencia_no_cambia`) y la Tarea 7 (`En_la_pantalla_de_carga_muestra_el_cruce`).
5. **Partida sin pantalla de carga** (el coach arrancó a mitad de partida): el informe no tiene sección del cruce. Test en la Tarea 3 (`Sin_cruce_el_informe_no_cambia`).

---

### Task 1: Mapa normalizado y ventanas de los estilos

**Files:**
- Modify: `src/BoCoach.Core/Data/MatchupStats.cs` (`MapIndex`), `src/BoCoach.Core/BuildOrders/BuildOrderSuggester.cs` (`CivStyles`), `data/civ_styles.json`
- Test: `tests/BoCoach.Core.Tests/Data/MatchupStatsTests.cs`, `tests/BoCoach.Core.Tests/BuildOrders/` (el archivo de tests de `CivStyles` que exista; si no, `CivStylesTests.cs`)

**Interfaces:**
- Produces: `MatchupStats.MapIndex` compara solo letras y dígitos en minúsculas ("Black Forest", "black_forest" y "BLACK-FOREST" son el mismo). `CivStyles(..., IReadOnlyDictionary<string, List<int>>? windows = null)` y `IReadOnlyList<int> CivStyles.Window(string style)` (lo del archivo; si no está, los valores de las restricciones; un estilo desconocido da vacío). `CivStyles.Load` lee `"windows"` (números de 1 a 5; lo demás se ignora).

- [ ] **Step 1: Tests**: `El_mapa_se_compara_sin_espacios_ni_mayusculas` (`MapIndex("Black Forest")` = 3, `("ARENA")` = 2); `Ventanas_por_defecto_y_del_archivo` (`CivStyles.Empty.Window("archers")` = [1, 2], `("fast_castle")` = [3, 4], `("dragones")` vacío; con `"windows": {"archers": [2, 9, "x"]}` → [2]); `El_archivo_del_repo_tiene_las_ventanas` (`data/civ_styles.json`: scouts [1, 2], fast_castle [3, 4]).
- [ ] **Step 2: Ver que fallan.**
- [ ] **Step 3: Implementar** y agregar a `data/civ_styles.json` el bloque `"windows"` con su explicación en los comentarios del principio.
- [ ] **Step 4: Ver que pasan.**
- [ ] **Step 5: Commit** `Core: ventanas de duración de cada estilo y mapa normalizado en los cruces`.

### Task 2: La ventana del cruce en el puntaje de la sugerencia

**Files:**
- Modify: `src/BoCoach.Core/BuildOrders/BuildOrderSuggester.cs`
- Test: `tests/BoCoach.Core.Tests/BuildOrders/BuildOrderSuggesterTests.cs` (el que exista)

**Interfaces:**
- Consumes: `MatchupStats`, `MatchupText.DurationReason`, `CivStyles.Window`.
- Produces: `MatchSetup(..., int? Elo = null)` (Elo 1v1 del jugador); `BuildOrderSuggester.Suggest(IEnumerable<BuildOrder> library, MatchSetup match, CivStyles styles, MatchupStats? matchups = null)` y `Score(BuildOrder bo, MatchSetup match, CivStyles styles, MatchupStats? matchups = null)`.

- [ ] **Step 1: Tests** (con `MatchupJson` de `Core.Tests/Data`, la BO de exploradores y la de fast castle de los ejemplos, civ francos, enfrente britanos; `britons|franks` con, para los britanos, franja 1 = 440 (400 a 480, n 60), 2 = 450 (410 a 490, n 60), 3 y 4 = 500 (460 a 540, n 60), 5 = 560 (520 a 600, n 60) y general 500 (480 a 520): a los francos les va mejor corto): `La_ventana_del_cruce_suma_a_la_BO_que_la_aprovecha` (la de exploradores suma 1,1 ± 0,05 contra sin datos y su porqué incluye "el cruce se gana más en partidas cortas"; la de fast castle suma 0 ± 0,05), `Sin_tendencia_no_suma` (franjas 1 y 5 que se tocan → mismo puntaje que sin datos), `Sin_datos_de_cruces_la_sugerencia_no_cambia` (`matchups` null o `MatchupStats.Empty` → mismo puntaje y porqué que hoy).
- [ ] **Step 2: Ver que fallan.**
- [ ] **Step 3: Implementar** el término en `Evaluate` (después del estilo típico, antes de la mejorada), con los datos del cruce calculados una vez en `Context`.
- [ ] **Step 4: Ver que pasan** (y los tests de sugerencia que ya había).
- [ ] **Step 5: Commit** `Core: la sugerencia de BO suma la ventana del cruce`.

### Task 3: El cruce en el informe de la partida

**Files:**
- Modify: `src/BoCoach.Core/Engine/GameReport.cs`
- Test: `tests/BoCoach.Core.Tests/Engine/` (el archivo de tests del informe que exista)

**Interfaces:**
- Produces: `GameReport.Matchup` (`IReadOnlyList<string>`, `init`, vacía por defecto). `GameReportText.Summary` agrega sus renglones al final; `Html` los muestra en el resumen (ya sale de `Summary`).

- [ ] **Step 1: Tests**: `El_informe_suma_el_cruce` (un informe `with { Matchup = ["Britanos contra ...", "Duró 34 min: ..."] }` → `Summary` termina con esos dos y `Html` los contiene, codificados), `Sin_cruce_el_informe_no_cambia` (el mismo informe sin `Matchup` da el mismo `Summary` que antes).
- [ ] **Step 2: Ver que fallan.**
- [ ] **Step 3: Implementar.**
- [ ] **Step 4: Ver que pasan.**
- [ ] **Step 5: Commit** `Core: el informe de la partida suma el cruce`.

### Task 4: `EloLookup`: buscar el Elo en la API oficial

**Files:**
- Create: `src/BoCoach.Core/Web/EloLookup.cs`, `tests/fixtures/api/personal-stat-theviper.json` (la respuesta real de "TheViper" del 2026-10-25, recortada a 3 grupos y sus estadísticas)
- Test: `tests/BoCoach.Core.Tests/Web/EloLookupTests.cs` (y el `.csproj` copia el fixture)

**Interfaces:**
- Produces (namespace `BoCoach.Core.Web`): `public sealed record EloCandidate(string Alias, long ProfileId, string? Country, int? Elo1v1, int? EloTeam, DateTimeOffset? LastMatch)`; `public sealed record EloSearch(IReadOnlyList<EloCandidate> Candidates, string? Error)`; `public static class EloLookup` con `const string BaseUrl = "https://aoe-api.worldsedgelink.com"`, `static Uri Address(string alias)`, `static IReadOnlyList<EloCandidate> Parse(string json)` (ordenados por `LastMatch`, la más nueva primero; `result.code` 9 o JSON roto → vacío) y `static Task<EloSearch> SearchAsync(HttpClient http, string alias, CancellationToken cancel = default)` (10 s; un error de red, de tiempo o una respuesta que no es 200 → `Error` con un texto para el usuario y sin candidatos).

- [ ] **Step 1: Tests**: `Lee_los_candidatos_y_su_Elo` (con el fixture: alias, perfil, país, Elo 1v1 de la tabla 3 y de equipos de la 4, orden por última partida), `Nombre_que_no_existe_no_es_error` (`{"result":{"code":9,"message":"UNKNOWN_ALIASES"}}` → vacío; por `SearchAsync` con un `HttpMessageHandler` falso → sin `Error`), `Busqueda_con_la_API_caida_da_un_error_legible` (handler que responde 503 → `Error` que empieza con "La API del juego"; handler que tira `HttpRequestException` → `Error` que empieza con "No se pudo consultar"), `La_direccion_escapa_el_nombre_y_es_https` (`Address("Juan Pérez \"x\"")` es https y el parámetro `aliases`, decodificado, es `["Juan Pérez \"x\""]`).
- [ ] **Step 2: Ver que fallan.**
- [ ] **Step 3: Implementar** con `JsonDocument` y `JsonEncodedText` para armar el arreglo (sin reflexión).
- [ ] **Step 4: Ver que pasan.**
- [ ] **Step 5: Commit** `Core: EloLookup busca el Elo de un jugador en la API oficial del juego`.

### Task 5: El cruce desde la pantalla de carga

**Files:**
- Modify: `src/BoCoach.Vision/Loading/LoadingScreen.cs` (`LoadingReading`)
- Test: `tests/BoCoach.Vision.Tests/LoadingScreenTests.cs`

**Interfaces:**
- Produces: `MatchupQuery? LoadingReading.Matchup(LoadingPlayer me, int? elo1v1, int? eloTeam)`: null si `me.Civ` es null; con 2 jugadores, 1v1 con `Facing(me)?.Civ` y `elo1v1`; con más, equipos con `eloTeam`; el mapa es `Map`.

- [ ] **Step 1: Tests**: `El_cruce_de_un_uno_contra_uno` (2 jugadores → `Team` false, rival la civ del otro, Elo 1v1), `El_cruce_por_equipos` (8 jugadores → `Team` true, sin rival, Elo de equipos), `Sin_tu_civ_no_hay_cruce`.
- [ ] **Step 2: Ver que fallan.**
- [ ] **Step 3: Implementar.**
- [ ] **Step 4: Ver que pasan.**
- [ ] **Step 5: Commit** `Vision: el cruce de la partida desde la pantalla de carga`.

### Task 6: Elo en la ventana con "Buscar"

**Files:**
- Modify: `src/BoCoach.App/Coaching/CoachSettings.cs`, `src/BoCoach.App/Coaching/CoachSessionViewModel.cs`, `src/BoCoach.App/MainWindow.xaml`, `src/BoCoach.App/MainWindow.xaml.cs` (el host)
- Create: `src/BoCoach.App/Coaching/EloChoiceWindow.cs` (ventana sin XAML, como `ReportWindow`)
- Test: `tests/BoCoach.App.Tests/CoachSessionTests.cs`

**Interfaces:**
- Consumes: `EloLookup`, `EloCandidate`, `EloSearch`.
- Produces: `CoachSettings.Elo1v1`, `EloTeam` (`int?`) y `ProfileId` (`long?`), en `settings.json` como `elo_1v1`, `elo_team` y `profile_id`. En `CoachSessionViewModel`: `string Elo1v1Text`, `string EloTeamText` (lo que se escribe; vacío o un número de 1 a 4000, si no se descarta), `int? Elo1v1`, `int? EloTeam`, `string EloStatus`, `IAsyncRelayCommand SearchEloCommand` y la propiedad `Func<string, CancellationToken, Task<EloSearch>> SearchElo` (por defecto `EloLookup.SearchAsync` con un `HttpClient` propio; los tests la cambian). `ICoachHost.ChooseElo(IReadOnlyList<EloCandidate>)` devuelve el elegido o null.

Comportamiento de "Buscar": sin "Tu nombre" → `EloStatus` = "Poné tu nombre primero."; con un candidato → completa los dos Elo (los que tenga) y el perfil, `EloStatus` = "Elo de <alias>: 1v1 <n>, equipos <n>." (con "-" si falta uno); con varios → `ChooseElo` (la lista ya viene ordenada por la última partida) y lo mismo con el elegido, o nada si cancela; sin candidatos → "No encontré a <nombre> en el juego."; con `Error` → el error. La ventana de elección muestra por renglón alias, país, Elo 1v1 y de equipos y la fecha de la última partida.

- [ ] **Step 1: Tests**: `El_Elo_se_guarda_con_los_ajustes` (vuelve a cargar con los mismos valores; "abc" y "99999" no se guardan), `Buscar_con_uno_completa_el_Elo`, `Buscar_con_varios_deja_elegir` (el host recibe la lista en el orden de `SearchElo` y se usa el elegido), `Buscar_sin_resultados_lo_dice`, `Buscar_sin_nombre_lo_pide`.
- [ ] **Step 2: Ver que fallan.**
- [ ] **Step 3: Implementar**, con los campos "Elo 1v1" y "Elo equipos" (ancho 50) y el botón "Buscar" en la segunda fila, después de "Tu nombre", y `EloStatus` al lado. Tooltips en castellano que digan para qué sirve y que el nombre se manda a la API del juego solo al tocar "Buscar".
- [ ] **Step 4: Ver que pasan.**
- [ ] **Step 5: Commit** `App: Elo 1v1 y de equipos en la ventana, con Buscar en la API del juego`.

### Task 7: El cruce en la pantalla de carga, la sugerencia y el informe

**Files:**
- Modify: `src/BoCoach.App/CoachData.cs` (ruta y caché por civ), `src/BoCoach.App/BoCoach.App.csproj` (copia `data/matchup_stats.json`, como `maps.json`), `src/BoCoach.App/Overlay/OverlayViewModel.cs` y `CoachOverlay.xaml` (renglón `MatchupLine`, debajo de `MatchAdvice`, color `#C9B8E8`), `src/BoCoach.App/Coaching/CoachSessionViewModel.cs` (`Suggest`, el corte del primer minuto y `Finished`)
- Test: `tests/BoCoach.App.Tests/CoachSessionTests.cs`

**Interfaces:**
- Consumes: `LoadingReading.Matchup`, `MatchupStats`, `MatchupText.Loading`/`Report`, `BuildOrderSuggester.Suggest(..., matchups)`, `GameReport.Matchup`.
- Produces: `CoachData.Matchups(string civ)` (`MatchupStats`, cargado una vez por civ); `OverlayViewModel.MatchupLine`; en la sesión, el `MatchupQuery` de la última pantalla de carga queda guardado y `Finished` agrega `MatchupText.Report(...)` al informe con la duración de la partida. Los nombres: los de `CivName` y `data/maps.json`.

- [ ] **Step 1: Tests**: `En_la_pantalla_de_carga_muestra_el_cruce` (la captura de la carga de 4 contra 4 con "Kyriokes" y sin Elo: `MatchupLine` empieza con "Japoneses en " y contiene "por equipos (sin tu Elo)"; con `EloTeam` 1200, contiene "(Elo 1200)"), y el test de la sugerencia que ya existe sigue pasando.
- [ ] **Step 2: Ver que fallan.**
- [ ] **Step 3: Implementar.** `MatchupLine` se borra junto con `MatchAdvice` pasado el primer minuto. La sugerencia pasa `MatchSetup(..., Elo: Elo1v1)` y `_data.Matchups(me.Civ)`.
- [ ] **Step 4: Correr toda la solución** (`dotnet test BoCoach.slnx -c Release`) y ver que pasa.
- [ ] **Step 5: Commit** `App: el cruce en la pantalla de carga, la sugerencia y el informe`.

### Task 8: ReplaySession muestra el cruce

**Files:**
- Modify: `tools/ReplaySession/Program.cs` (`Loading`: `--elo N` y `--elo-equipos N`; renglón `Cruce:` con `MatchupText.Loading` y la sugerida con los cruces)

- [ ] **Step 1: Implementar** y correr `dotnet run --project tools/ReplaySession -c Release -- loading tests/fixtures/screens/carga-ia-en-1080p.png --nombre Kyriokes --elo-equipos 1200`: tiene que mostrar `Cruce: Japoneses en ... por equipos (Elo 1200): ...`.
- [ ] **Step 2: Commit** `ReplaySession: loading muestra el cruce`.

### Task 9: Versión, documentos y push

- [ ] **Step 1:** Seguir la skill `nueva-version` (subir la versión, compilar en Release, verificar que `data/matchup_stats.json` llegó a la salida, pushear).
- [ ] **Step 2:** `docs/pendientes-usuario.md`: probar "Buscar" con su nombre, mirar el renglón del cruce en una pantalla de carga (1v1 y por equipos), revisar `windows` en `data/civ_styles.json`, y jugar una partida grabada después de usar "Buscar" para verificar la conversión ×1,7 de la duración (con el perfil guardado se compara su última partida de la API con el reloj final de la grabación).
- [ ] **Step 3:** `PLAN.md` (D10: "(c) hecho") y `CLAUDE.md`, en commits separados, y push.
