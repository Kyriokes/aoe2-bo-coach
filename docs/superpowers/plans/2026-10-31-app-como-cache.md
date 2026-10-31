# La app como caché de las BO de la web (parte 4 de la app para el público) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** que la app deje de editar y de tener biblioteca propia: conecta la cuenta de la web, pide la lista de tus BO y tus suscriptas, baja cada BO recién al usarla, guarda las usadas y elige en automático (la sugerida) o a mano.

**Architecture:** en el Core, `BuildOrderSummary` (lo que hace falta para puntuar sin los pasos) y la sugerencia sobre resúmenes; `WebLibraryClient` suma la lista, suscribirse y bajar con token; `BuildOrderCache` (Core/Web) guarda `mis-bo.json` y `bo/<código>.json` en `%APPDATA%\BoCoach\cache`. En la app, `BuildOrderPicker` decide qué BO usa el coach (automática o a mano, con lo guardado si no hay internet y sin BO si no hay nada), `BuildOrdersViewModel` es la sección "Build order" de la ventana, y los enlaces `bocoach://conectar/<token>` e `import/<código>` llegan por el mismo camino de hoy. Se va el editor WPF y `BuildOrderLibrary` (con la etiqueta `editor-wpf` antes).

**Tech Stack:** C# .NET 10 (Core, App WPF con CommunityToolkit.Mvvm), xUnit; la web (Next.js, Supabase) solo en la Task 1.

**Spec:** `docs/superpowers/specs/2026-10-30-app-publica-design.md` (parte 4). Maqueta: `docs/superpowers/specs/2026-10-30-app-publica-maqueta.png`.

## Global Constraints

- Textos en castellano rioplatense, sin guiones largos (—). Warnings son errores.
- Un commit por cambio, en castellano, con `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- Tests: `dotnet test BoCoach.slnx -c Release`; la web, `npm test`, `npm run lint`, `npx tsc --noEmit` en `web/`.
- Caché en `%APPDATA%\BoCoach\cache`: `mis-bo.json` (la última lista) y `bo/<código>.json` (cada BO usada con su `updated_at`).
- La lista se pide al abrir la app, al empezar el coach y con "Actualizar lista"; sin internet, la guardada.
- Una BO se baja de nuevo solo si la lista trae un `updated_at` más nuevo que el guardado; sin internet, la guardada.
- La lista muestra por BO "guardada" o "se baja al usarla".
- `settings.json`: `bo_mode` (`auto` por defecto, o `manual`) y `last_build_order` pasa a ser el **código** de la web.
- Automática: en la pantalla de carga, la de más puntaje de la lista (el puntaje de hoy, sobre `BuildOrderSummary`); si no está guardada se baja ahí; si falla, la mejor de las guardadas; si no hay ninguna, el coach sigue sin BO (casa, centro urbano parado, inactivos, tecnologías y GD por lo que hay).
- Manual: se elige de la lista y se baja al elegirla. En la carga aparece la sugerida con su atajo, como hoy.
- Sin cuenta: la sección dice "Conectá tu cuenta para elegir BO" con un botón que abre la web (Mi cuenta). Pegar el token a mano sigue (ventana "Cuenta de la web").
- `bocoach://conectar/<token>` guarda el token (cifrado con DPAPI en `web.json`, como hoy) y la app dice "Conectada como <nombre>".
- "Pegar código" suscribe (`POST /api/app/suscripciones/<código>`) y la baja; `bocoach://import/<código>` hace lo mismo con la confirmación de nombre y autor; "Buscar más BO en la web" abre la biblioteca.
- Se van: `BuildOrderLibrary` (biblioteca, carpeta y papelera), la carpeta `Editor`, Nueva, Importar, Pegar BO, Exportar, Publicar y la copia de `build_orders` a `data/`. Antes, la etiqueta de git `editor-wpf`.
- Se quedan: `build_orders/civs` en el repo, `--publish` como comando, ReplaySession leyendo BO de un archivo. La carpeta vieja `%APPDATA%\BoCoach\build_orders` queda en su lugar y se ignora.

## Decisiones del plan (sobre la spec)

- `GET /api/app/mis-bo` devuelve `{ account, items }`: para "Conectada como <nombre>" hace falta el nombre de la cuenta y la lista no lo traía. La migración no se corrió todavía, así que la función se cambia en el mismo archivo.
- Sin BO, el coach corre con una BO vacía (`BuildOrder` sin pasos, nombre "Sin BO"): el `Coach` la toma como terminada y muestra la tarjeta compacta (avisos, casa, centro urbano parado, tecnologías). No se valida (el validador exige pasos).
- `--replay`, `--screenshot` y `--overlay-screenshot` toman `--bo <ruta.json>` (sin biblioteca no hay nombres para buscar).

## Review Focus

- Una BO de la lista que la web ya no devuelve (borrada o pasada a privada) pero está guardada: sigue en `bo/` y no aparece en la lista; la automática no la elige, y si era la última usada se arranca sin ella.
- `mis-bo.json` o un `bo/<código>.json` roto o de otra versión: se ignora (lista vacía o "se baja al usarla"), sin excepción.
- La descarga en la pantalla de carga termina después de que empezó la partida (más de 60 s): no cambia la BO en plena partida.
- Un token inválido (borrado en la web): la lista responde 401, la app dice "Conectá tu cuenta de nuevo" y sigue con lo guardado.
- Dos `bocoach://import/<código>` seguidos con la app abierta: cada uno pide su confirmación; el segundo no pisa al primero a medio bajar.

---

### Task 1: la web devuelve el nombre de la cuenta

**Files:**
- Modify: `web/supabase/migrations/20261031000000_privadas_suscripciones.sql` (`app_my_build_orders`), `web/supabase/tests/biblioteca.test.ts`, `web/src/app/api/app/mis-bo/route.ts`

**Interfaces:**
- Produces: `app_my_build_orders(p_token) returns jsonb` = `{ "account": <display_name>, "items": [...] }`; `GET /api/app/mis-bo` → `{ account, items }`.

- [x] **Step 1: Test.** En "con el token de la app", `list` lee `.items` y un caso nuevo: `account` es "Beto" con el token de Beto.
- [x] **Step 2: Correr** `npx vitest run supabase/tests/biblioteca.test.ts`: falla.
- [x] **Step 3: Implementar** (objeto con `account` del perfil del dueño del token e `items`, `[]` si no hay) y la ruta pasa el objeto tal cual.
- [x] **Step 4: Correr** el mismo comando, `npx tsc --noEmit` y `npm run lint`: pasan.
- [x] **Step 5: Commit** (`mis-bo: el nombre de la cuenta junto a la lista`).

### Task 2: Core, resumen de una BO y sugerencia sobre resúmenes

**Files:**
- Create: `src/BoCoach.Core/BuildOrders/BuildOrderSummary.cs`
- Modify: `src/BoCoach.Core/BuildOrders/BuildOrderSuggester.cs`
- Test: `tests/BoCoach.Core.Tests/BuildOrders/BuildOrderSuggesterTests.cs`

**Interfaces:**
- Produces: `public sealed record BuildOrderSummary(string Key, string Name, string Civ, string? Map, IReadOnlyList<string> Tags, string? Style) { public static BuildOrderSummary Of(BuildOrder bo); }` (`Key` = el código de la web, o el `Id` de la BO en `Of`; `Style` = `BuildOrderStyle.Of`).
- Produces: `public sealed record SummarySuggestion(BuildOrderSummary Summary, string Reason, double Score);` y en `BuildOrderSuggester`: `Suggest(IEnumerable<BuildOrderSummary>, MatchSetup, CivStyles, MatchupStats? = null) : SummarySuggestion?` y `Score(BuildOrderSummary, MatchSetup, CivStyles, MatchupStats? = null) : double`. `Evaluate` pasa a trabajar sobre el resumen; las sobrecargas de hoy con `BuildOrder` siguen (las usan tests y ReplaySession) y llaman a las nuevas con `BuildOrderSummary.Of`.

- [ ] **Step 1: Tests.** `Of` copia nombre, civ, mapa, etiquetas y estilo de la de scouts; `PorResumen_DaLoMismoQuePorBo`: para las 124 de `civs` y cada `MatchSetup` de una lista chica (civ de la BO, otra civ, con mapa Arabia, con rival de enfrente y posición), `Score(Of(bo))` es igual a `Score(bo)` y `Suggest` sobre resúmenes elige la misma clave que sobre las BO.
- [ ] **Step 2: Correr** `dotnet test tests/BoCoach.Core.Tests -c Release --filter BuildOrderSuggester`: no compila.
- [ ] **Step 3: Implementar.**
- [ ] **Step 4: Correr** la suite del Core: pasa.
- [ ] **Step 5: Commit** (`BuildOrderSummary: la sugerencia puntúa sin los pasos`).

### Task 3: Core, la lista, suscribirse y bajar con token

**Files:**
- Modify: `src/BoCoach.Core/Web/WebLibraryClient.cs`
- Test: `tests/BoCoach.Core.Tests/Web/WebLibraryClientTests.cs`

**Interfaces:**
- Produces: `public sealed record WebBuildOrderEntry(string Code, string Name, string Civ, string? Map, string? Style, IReadOnlyList<string> Tags, string Author, string GuideAuthor, bool Own, string Visibility, DateTimeOffset UpdatedAt, double? FeudalClick, double? CastleClick) { public BuildOrderSummary Summary { get; } }` y `public sealed record WebBuildOrderList(string Account, IReadOnlyList<WebBuildOrderEntry> Items);`.
- Produces: `Task<WebBuildOrderList> MyBuildOrdersAsync(string token, CancellationToken = default)`, `Task<bool> SubscribeAsync(string code, string token, CancellationToken = default)` (true si es propia), `Task<WebBuildOrder> DownloadAsync(string code, string? token = null, CancellationToken = default)` (con token manda `Authorization: Bearer`). `WebBuildOrder` suma `DateTimeOffset? UpdatedAt` y `string GuideAuthor`.
- Las tres con token exigen `SendsTokenSafely(BaseUrl)` (como `PublishAsync`). Una entrada de la lista con código inválido o sin nombre se descarta; el resto de la lista sigue.

- [ ] **Step 1: Tests** con `FakeWeb`: la lista se lee entera (cuenta, una propia privada, una suscripta); una entrada rota no tira; 401 da `WebLibraryException` con `Status` 401 y el mensaje de la web; suscribirse manda `POST api/app/suscripciones/<código>` con el token y devuelve `own`; bajar con token manda la cabecera y sin token no; `updated_at` llega en `WebBuildOrder`; con `http://` a otro host no manda el token.
- [ ] **Step 2: Correr** `dotnet test tests/BoCoach.Core.Tests -c Release --filter WebLibraryClient`: no compila.
- [ ] **Step 3: Implementar.**
- [ ] **Step 4: Correr** la suite del Core: pasa.
- [ ] **Step 5: Commit** (`WebLibraryClient: tu lista, suscribirse y bajar con token`).

### Task 4: Core, la caché

**Files:**
- Create: `src/BoCoach.Core/Web/BuildOrderCache.cs`
- Test: `tests/BoCoach.Core.Tests/Web/BuildOrderCacheTests.cs`

**Interfaces:**
- Consumes: Task 3.
- Produces: `public sealed class BuildOrderCache(string directory, WebLibraryClient client, Func<string?> token)` con:
  - `WebBuildOrderList List { get; }` (la guardada al crearla; `Account` "" y lista vacía si no hay o está rota),
  - `Task<string?> RefreshAsync(CancellationToken = default)`: pide la lista; si sale, la guarda y la usa; si no, sigue la guardada y devuelve el mensaje del error (null si salió). Sin token: "Conectá tu cuenta para elegir BO" sin pedir nada.
  - `bool IsSaved(string code)`, `BuildOrder? Saved(string code)` (null si no está o no se lee),
  - `Task<BuildOrder> GetAsync(string code, CancellationToken = default)`: guardada y al día (su `updated_at` igual o más nuevo que el de la lista, o sin entrada en la lista) → la guardada; si no, la baja con el token, la guarda (`{ "updated_at": ..., "build_order": ... }`) y la devuelve; si bajarla falla y está guardada, la guardada; si no, tira `WebLibraryException`.
  - `Task<WebBuildOrderEntry?> AddAsync(string code, CancellationToken = default)`: suscribe, refresca la lista y baja la BO; devuelve la entrada.
  - `IEnumerable<BuildOrder> AllSaved()` (para la automática sin internet).
- Archivos: `mis-bo.json` = `{ "account", "items" }` como lo da la web; nombres de `bo/` solo para códigos válidos (`WebCode.IsValid`).

- [ ] **Step 1: Tests** (carpeta temporal y un `HttpMessageHandler` que cuenta los pedidos por ruta y se puede "desconectar"): la lista se guarda y sin internet se usa la guardada; `RefreshAsync` sin internet devuelve el error y la lista sigue; una BO se baja una vez y la segunda vez no pide nada; se rebaja si la lista trae un `updated_at` más nuevo; sin internet y guardada, la guardada; sin internet y sin guardar, `WebLibraryException`; `AddAsync` pide suscribirse, la lista y la BO; un `mis-bo.json` roto y un `bo/x.json` roto no tiran; sin token no pide nada.
- [ ] **Step 2: Correr** `dotnet test tests/BoCoach.Core.Tests -c Release --filter BuildOrderCache`: no compila.
- [ ] **Step 3: Implementar.**
- [ ] **Step 4: Correr** la suite del Core: pasa.
- [ ] **Step 5: Commit** (`BuildOrderCache: la lista y las BO usadas guardadas en la PC`).

### Task 5: el coach sin BO

**Files:**
- Modify: `src/BoCoach.Core/BuildOrders/BuildOrder.cs` (`BuildOrder.None`), `src/BoCoach.App/Coaching/CoachSessionViewModel.cs` (`Start` no valida `None`)
- Test: `tests/BoCoach.Core.Tests/Engine/CoachTests.cs` (o el archivo de pruebas del `Coach` que use `Partida`), `tests/BoCoach.App.Tests/CoachSessionTests.cs`

**Interfaces:**
- Produces: `public static BuildOrder None { get; }` = `new BuildOrder { Id = "", Name = "Sin BO", Civ = "any", Steps = [] }` y `bool IsNone` (sin pasos y nombre "Sin BO").

- [ ] **Step 1: Tests.** Core: un `Coach` con `BuildOrder.None` y una partida simulada (`Partida`) con el centro urbano parado y la población al tope da `Compact` con el aviso del centro urbano y la casa, sin pasos ni excepción. App: `Start(BuildOrder.None, frames)` arranca (`IsRunning`) sin el error de validación.
- [ ] **Step 2: Correr** los dos filtros: fallan.
- [ ] **Step 3: Implementar** lo mínimo (si el `Coach` ya lo soporta, solo `None` y saltear la validación).
- [ ] **Step 4: Correr** la suite: pasa.
- [ ] **Step 5: Commit** (`El coach anda sin BO`).

### Task 6: qué BO usa el coach

**Files:**
- Create: `src/BoCoach.App/Coaching/BuildOrderPicker.cs`
- Modify: `src/BoCoach.App/Coaching/CoachSettings.cs` (`bo_mode`), `src/BoCoach.App/Coaching/CoachSessionViewModel.cs` (`Suggest`, `Tick`, `AutoBuildOrder`, `AcceptSuggestion`, `StartStop` usan el picker en vez de `_data.Library` y `_selected`)
- Test: `tests/BoCoach.App.Tests/BuildOrderPickerTests.cs`, `tests/BoCoach.App.Tests/CoachSessionTests.cs`

**Interfaces:**
- Consumes: Tasks 2, 4, 5.
- Produces: `public interface IBuildOrderSource { WebBuildOrderList List { get; } BuildOrder? Saved(string code); IEnumerable<BuildOrder> AllSaved(); Task<BuildOrder> GetAsync(string code, CancellationToken cancel = default); }` (lo implementa `BuildOrderCache`; los tests usan uno falso).
- Produces: `public enum BuildOrderMode { Auto, Manual }` y `public sealed class BuildOrderPicker(IBuildOrderSource source, CivStyles styles)` con:
  - `BuildOrder? Current(string? lastCode)`: la guardada de `lastCode`, o null.
  - `SummarySuggestion? Best(MatchSetup match, MatchupStats? matchups)`: la de más puntaje de la lista.
  - `Task<BuildOrder> ForMatchAsync(MatchSetup match, MatchupStats? matchups, CancellationToken cancel)`: la mejor de la lista (guardada o bajada); si bajarla falla, la mejor de `AllSaved()` (puntuada con `BuildOrderSummary.Of`); si no hay ninguna, `BuildOrder.None`.
- En `CoachSessionViewModel`: `BoMode` (de `settings.json`, se guarda al cambiar). Automática y arrancado solo: en la carga, `ForMatchAsync`; cuando termina, si la partida sigue en la carga o en el primer minuto (`SuggestionSeconds`) y la BO es otra, `Start` con ella (como hoy, con el atajo para volver). Manual: en la carga, `Best` como sugerencia; el atajo la baja (`GetAsync`) y la usa. `last_build_order` guarda el código (`BuildOrder.Id` de una BO de la web es su código: la caché se lo pone al guardarla).

- [ ] **Step 1: Tests** del picker con una fuente falsa: elige la de más puntaje; la baja si no está guardada; si bajarla falla usa la mejor guardada; sin nada, `None`; `Current` con un código que no está da null. Y en `CoachSessionTests`: `bo_mode` se lee y se guarda; con la descarga demorada más allá de `SuggestionSeconds` no cambia la BO.
- [ ] **Step 2: Correr** `dotnet test tests/BoCoach.App.Tests -c Release --filter "BuildOrderPicker|CoachSession"`: no compila.
- [ ] **Step 3: Implementar.** `BuildOrderCache.GetAsync` le pone a la BO `Id = código` antes de devolverla.
- [ ] **Step 4: Correr** la suite: pasa.
- [ ] **Step 5: Commit** (`El coach elige la BO de tu lista: automática o a mano`).

### Task 7: la sección "Build order" de la ventana y los enlaces

**Files:**
- Create: `src/BoCoach.App/Coaching/BuildOrdersViewModel.cs`, `src/BoCoach.App/Web/AppLink.cs`
- Modify: `src/BoCoach.App/MainWindow.xaml` y `.cs` (la sección reemplaza biblioteca y editor), `src/BoCoach.App/App.xaml.cs` (enlaces, `--bo <ruta>`, capturas), `src/BoCoach.App/Web/WebAccount.cs` (guardar el token que llega por enlace), `src/BoCoach.App/FirstRun/*` (paso "Conectá tu cuenta"), `src/BoCoach.App/CoachData.cs` (la caché en lugar de la biblioteca)
- Test: `tests/BoCoach.App.Tests/BuildOrdersViewModelTests.cs`, `tests/BoCoach.App.Tests/AppLinkTests.cs`

**Interfaces:**
- Produces: `public abstract record AppLink { record Connect(string Token); record Import(string Code); public static AppLink? Parse(string text); }` (`bocoach://conectar/<token>` con token `bocoach_` + 64 hex; `bocoach://import/<código>` y lo que acepta `WebCode.Parse`).
- Produces: `BuildOrdersViewModel(BuildOrderCache cache, WebAccount web, IDialogs dialogs)` con `Account` ("" sin cuenta), `Connected`, `Entries` (nombre, civ en castellano, estilo, "Tuya"/"Suscripta", "guardada"/"se baja al usarla"), `Mode` (Automática/A mano), `Selected` (a mano: al elegirla se baja; mientras, "Bajando…"), `Status`, y comandos `Refresh`, `PasteCode` (pide el código, `AddAsync`), `SearchMore` (abre `/biblioteca`), `Connect` (registra el protocolo `bocoach://` y abre `/cuenta`), `AccountSettings` (la ventana de hoy), `ConnectWithToken(string token)` (guarda, refresca y dice "Conectada como <nombre>"), `ImportAsync(string code, bool confirm)` (pide confirmación con nombre y autor de la BO bajada antes de sumarla). `SupportUrl` se muda acá desde `MainViewModel`. Los pedidos de importar se hacen de a uno (el segundo espera al primero).
- La ventana sigue la maqueta: arriba la sección "Build order" (cuenta, modo, lista, botones), el resto como hoy.

- [ ] **Step 1: Tests**: `AppLink.Parse` (conectar con token válido, inválido, import con código y con la URL de la página, cualquier otra cosa null); el view model con una caché sobre un `HttpMessageHandler` falso: sin token dice "Conectá tu cuenta para elegir BO"; `ConnectWithToken` guarda el token y muestra la cuenta; `PasteCode` suscribe y la entrada queda "guardada"; con 401 dice "Conectá tu cuenta de nuevo" y la lista guardada sigue; elegir a mano una no guardada la baja.
- [ ] **Step 2: Correr** `dotnet test tests/BoCoach.App.Tests -c Release --filter "AppLink|BuildOrdersViewModel"`: no compila.
- [ ] **Step 3: Implementar** el view model, los enlaces (en `App.xaml.cs`, el argumento `--import` y el pipe pasan por `AppLink.Parse`) y la ventana.
- [ ] **Step 4: Verificar** con `--screenshot salida.png` (sin cuenta y con una caché de prueba con 3 BO: `--cache <carpeta>` para la captura) y mirar las dos imágenes. Correr la suite.
- [ ] **Step 5: Commits**: enlaces; view model; ventana; primeros pasos.

### Task 8: se va el editor y la biblioteca

**Files:**
- Delete: `src/BoCoach.App/Editor/` (entera), `src/BoCoach.Core/BuildOrders/BuildOrderLibrary.cs`, `tests/BoCoach.Core.Tests/BuildOrders/BuildOrderLibraryTests.cs`, `tests/BoCoach.App.Tests/EditorTests.cs`
- Modify: `src/BoCoach.App/BoCoach.App.csproj` (sin la copia de `build_orders`), `tests/BoCoach.Core.Tests/BuildOrders/CivBuildOrdersTests.cs` (sin `IsFileNameId`), lo que quede usando `IDialogs` o `MainViewModel` (la interfaz se muda a `Coaching` si `BuildOrdersViewModel` la usa)

- [ ] **Step 1:** `git tag editor-wpf` sobre el último commit con el editor y `git push origin editor-wpf`.
- [ ] **Step 2:** Borrar y arreglar lo que no compile. `grep -rn "BuildOrderLibrary\|MainViewModel\|BoCoach.App.Editor" src tests tools` no encuentra nada.
- [ ] **Step 3:** Correr la suite y `dotnet build src/BoCoach.App -c Release`: `data/` ya no tiene `build_orders`.
- [ ] **Step 4: Commit** (`Se va el editor de BO y la biblioteca de la app: todo eso está en la web`).

### Task 9: notas, pendientes y versión

- [ ] **Step 1:** `docs/pendientes-usuario.md` (CRLF, `newline=''`): 0j, probar la app nueva: conectar la cuenta desde la web, la lista, pegar un código, automática en una partida, a mano, y sin internet.
- [ ] **Step 2:** `CLAUDE.md`: la app como caché (sección de la Fase 10 o de la parte 3), el editor en la etiqueta `editor-wpf`.
- [ ] **Step 3:** `nueva-version` (0.9.52) y push de la rama. Recordar en el mensaje que la migración (pendiente 0i) tiene que estar corrida antes del push a `main`.
