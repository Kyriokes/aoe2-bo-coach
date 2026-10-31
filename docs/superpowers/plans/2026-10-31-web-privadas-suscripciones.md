# La web: BO privadas, suscripciones e importadores (parte 3 de la app para el público) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** que en la web cada BO sea pública o privada, que cada uno se suscriba a BO públicas de otros, que la app tenga endpoints para pedir su lista y bajar privadas propias, y que el editor importe guías de buildorderguide.com y RTS Overlay con el Core.

**Architecture:** una migración nueva (`20261031000000_privadas_suscripciones.sql`) agrega `visibility`, `map` y `style` a `build_orders`, cambia el permiso de lectura a "pública o tuya", crea `subscriptions` y cuatro funciones `app_*` con el token (como `app_publish`), así la web sigue sin la clave de administrador. `map` y `style` son columnas generadas por la base desde el JSON. El Core suma `BuildOrderStyle` (el estilo, sacado de `BuildOrderSuggester`) y `BuildOrderImport` (detecta y convierte guías), que el Wasm expone. La web lee lo público con caché (el cliente anónimo ya no ve las privadas) y lo propio con la sesión, sin caché.

**Tech Stack:** C# .NET 10 (Core, Wasm), Next.js 16 (App Router, Cache Components), Supabase (Postgres, RLS), PGlite y vitest.

**Spec:** `docs/superpowers/specs/2026-10-30-app-publica-design.md` (parte 3).

## Global Constraints

- Textos en castellano rioplatense, sin guiones largos (—). En C#, warnings son errores.
- Un commit por cambio, en castellano, con `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- Tests: `dotnet test BoCoach.slnx -c Release` y, en `web/`, `npm test`, `npm run lint` y `npx tsc --noEmit`.
- `visibility`: `public` o `private`. Lo ya publicado queda `public`. En `/subir` una BO nueva arranca marcada Privada; al editar, la que tenía. Se cambia también desde "Mi cuenta". No hay "privada con enlace".
- Una privada ajena no aparece en la biblioteca, en `/bo/<código>` (404), en el sitemap, en `/api/bo`, ni se puede votar ni suscribir.
- `subscriptions(user_id, build_order_id, created_at)`, clave las dos primeras, borrado en cascada con la BO y con el perfil. Cada uno ve, crea y borra solo las suyas, solo a BO públicas ajenas. Una suscripta que pasa a privada sale de la lista de la app.
- Las propias no se suscriben: están siempre en la lista de la app.
- `GET /api/app/mis-bo` con token: propias y suscriptas, sin pasos: `code`, `name`, `civ`, `map`, `style`, `tags`, `author`, `uploader`, `own`, `visibility`, `updated_at`, `feudal_click`, `castle_click`. Sin token o con uno inválido, 401.
- `POST` y `DELETE /api/app/suscripciones/<código>`, con token.
- `GET /api/bo/<código>` acepta además el token para bajar las privadas propias.
- Estilos: `archers`, `scouts`, `maa`, `fast_castle` o null (las reglas de hoy de `BuildOrderSuggester.StyleOf`).
- La migración la corre el usuario en el SQL Editor de Supabase: hasta entonces la web publicada sigue como hoy.

## Decisiones del plan (sobre la spec)

- `map` y `style` no los manda el navegador: son columnas generadas (`generated always as ... stored`) desde el JSON. La spec decía "calculado en el navegador al publicar, y la app con `--publish`" y "las ya publicadas se completan con un script". Generadas, no hay que confiar en lo que manda quien publica, las ya publicadas quedan completas al correr la migración y la app no cambia. El costo es una copia de la regla del estilo en SQL (`build_order_style`), atada al Core por un test que compara las dos con todas las BO del repo.
- Importar: "Abrir .json" y "Pegar" del menú "Cargar" aceptan los tres formatos (BO de la app, buildorderguide.com, RTS Overlay) en vez de sumar dos entradas nuevas. El botón pasa a decir "Pegar JSON o guía". Se reconoce solo el formato, como "Importar…" en la app.

## Review Focus

- Una BO pública que pasa a privada estaba en el caché de la biblioteca y de su página: al cambiarla se invalidan `LIBRARY_TAG` y `buildOrderTag(código)` y desaparece para los demás.
- El dueño edita una privada: `/subir?editar=<código>` la lee con la sesión (con el cliente anónimo daría "no existe").
- `app_publish` al reemplazar una BO no le cambia la visibilidad (`--publish` sobre una que el usuario puso privada).
- `GET /api/bo/<código>` de una pública con un token inválido sigue devolviendo la BO (el token solo hace falta para las privadas).
- Suscribirse por la base directa (con la sesión, sin la web) a una privada ajena o a una propia: lo rechaza la política.

---

### Task 1: Core, estilo e importación

**Files:**
- Create: `src/BoCoach.Core/BuildOrders/BuildOrderStyle.cs`, `src/BoCoach.Core/BuildOrders/BuildOrderImport.cs`
- Modify: `src/BoCoach.Core/BuildOrders/BuildOrderSuggester.cs` (sacar `StyleOf`, usar `BuildOrderStyle.Of`), `tools/ReplaySession/CivGuides.cs`, `tests/BoCoach.Core.Tests/BuildOrders/BuildOrderSuggesterTests.cs` (llamadas a `StyleOf`), `src/BoCoach.App/Editor/MainViewModel.cs` (la importación usa `BuildOrderImport.Read`)
- Test: `tests/BoCoach.Core.Tests/BuildOrders/BuildOrderImportTests.cs`

**Interfaces:**
- Produces: `public static class BuildOrderStyle { public static string? Of(BuildOrder bo); }` (mismas reglas que `StyleOf`).
- Produces: `public sealed record ImportedBuildOrder(BuildOrder BuildOrder, IReadOnlyList<string> Warnings, string Source);` con `Source` = `"bocoach"`, `"rts_overlay"` o `"buildorderguide"`.
- Produces: `public static class BuildOrderImport { public static ImportedBuildOrder Read(string json, GameData data); public static string ReadJson(string json, GameData data); }`. `Read` prueba `RtsOverlayImporter.IsRtsOverlay`, después `BuildOrderGuideImporter.IsBuildOrderGuide`, si no `BuildOrderSerializer.Parse` (sin avisos); lo que no se entiende tira la excepción de cada uno. `ReadJson` devuelve `{"ok":true,"source":"...","warnings":[...],"build_order":<BuildOrderSerializer.Serialize>}` o `{"ok":false,"error":"<mensaje>"}`, sin tirar.

- [ ] **Step 1: Escribir los tests**

En `BuildOrderImportTests` (con los datos del juego como los otros tests del importador):
- `Read_GuiaDeRtsOverlay_LaConvierte`: `tests/fixtures/rts-overlay/22p-scouts.json` da `Source == "rts_overlay"` y pasos.
- `Read_GuiaDeBuildOrderGuide_LaConvierte`: `tests/fixtures/buildorderguide/armenians.json` da `Source == "buildorderguide"`.
- `Read_BoDeLaApp_LaLeeSinAvisos`: `build_orders/scouts_22p.json` da `Source == "bocoach"` y `Warnings` vacío.
- `ReadJson_TextoQueNoEsJson_DevuelveError`: `ReadJson("{no", data)` tiene `ok` false y `error` no vacío.
- `ReadJson_Guia_TraeLaBoYLosAvisos`: con la de RTS Overlay, `source`, `warnings` (lista) y `build_order.steps` con elementos.

En `BuildOrderSuggesterTests`, cambiar `BuildOrderSuggester.StyleOf` por `BuildOrderStyle.Of` (los asserts quedan igual).

- [ ] **Step 2: Correr y ver que fallan**

Run: `dotnet test tests/BoCoach.Core.Tests -c Release --filter "BuildOrderImport|BuildOrderSuggester"`
Expected: no compila (`BuildOrderStyle` y `BuildOrderImport` no existen).

- [ ] **Step 3: Implementar** `BuildOrderStyle.Of` (mover el cuerpo de `StyleOf` con su comentario), `BuildOrderImport` y cambiar los llamadores (`BuildOrderSuggester`, `CivGuides.cs` en las líneas 186 y 275, `MainViewModel.Import`, que pasa a `var (bo, warnings, _) = BuildOrderImport.Read(json, _app.GameData);`).

- [ ] **Step 4: Correr la suite entera**

Run: `dotnet test BoCoach.slnx -c Release`
Expected: todo pasa.

- [ ] **Step 5: Commit** (`BuildOrderStyle: el estilo de una BO sale del sugeridor` y `BuildOrderImport: reconocer y convertir guías en el Core`, dos commits).

### Task 2: Wasm, estilo en el análisis e importar desde la web

**Files:**
- Modify: `src/BoCoach.Core/BuildOrders/BuildOrderAnalysis.cs` (campo `style` arriba, junto a `valid`), `src/BoCoach.Wasm/Exports.cs` (`Import`), `web/src/lib/coach/runtime.ts`, `web/src/lib/coach/types.ts`
- Test: `web/src/lib/coach/coach.test.ts`, `tests/BoCoach.Core.Tests` (el test de `BuildOrderAnalysis` que ya existe: sumar el `style`)

**Interfaces:**
- Consumes: `BuildOrderStyle.Of`, `BuildOrderImport.ReadJson` (Task 1).
- Produces: `[JSExport] public static string Import(string json)` en `Exports` (llama a `BuildOrderImport.ReadJson` con `_data`).
- Produces (TS): `Analysis.style: string | null`; `type ImportResult = { ok: true; source: "bocoach" | "rts_overlay" | "buildorderguide"; warnings: string[]; build_order: Record<string, unknown> } | { ok: false; error: string }`; `CoachRuntime.importBuildOrder(json: string): ImportResult`.

- [ ] **Step 1: Escribir los tests.** En el test de `BuildOrderAnalysis` del Core: la de scouts da `"style": "scouts"`. En `coach.test.ts`, `describe("importar guías")`: `22p-scouts.json` de `tests/fixtures/rts-overlay` da `source: "rts_overlay"` y un `build_order` que `analyze` acepta (`ok` true); una de `tests/fixtures/buildorderguide` da `"buildorderguide"`; `"{no"` da `ok: false`; y `analyze` de `build_orders/scouts_22p.json` trae `style: "scouts"`.
- [ ] **Step 2: Correr y ver que fallan.** Run: `dotnet test tests/BoCoach.Core.Tests -c Release --filter BuildOrderAnalysis` y en `web/`, `npm test -- src/lib/coach/coach.test.ts`. Expected: falta `style` e `importBuildOrder`.
- [ ] **Step 3: Implementar** el campo, la exportación y `importBuildOrder` en `startCoach` (como `analyze`, con `JSON.parse`).
- [ ] **Step 4: Correr.** Mismos comandos; Expected: pasan (`npm test` recompila el Wasm en `pretest`).
- [ ] **Step 5: Commit** (`Wasm: estilo en el análisis e importar guías`).

### Task 3: la migración

**Files:**
- Create: `web/supabase/migrations/20261031000000_privadas_suscripciones.sql`
- Test: `web/supabase/tests/biblioteca.test.ts` (casos nuevos), `web/supabase/tests/estilo.test.ts` (SQL contra el Core)

**Interfaces:**
- Produces (SQL):
  - `public.build_order_style(body jsonb) returns text`, `language sql immutable`: el `what` del último paso `do = 'train'` con `what` distinto de `villager` (en el orden de `steps`), mapeado como `BuildOrderStyle.Of`; si no hay ninguno y algún paso es `do = 'research'`, `what = 'castle_age'`, `fast_castle`; si no, null.
  - En `build_orders`: `visibility text not null default 'public' check (visibility in ('public','private'))`, `map text generated always as (body ->> 'map') stored`, `style text generated always as (public.build_order_style(body)) stored`. `visibility` se suma a los `grant insert (...)` y `grant update (...)` de `authenticated`.
  - Política de lectura: se reemplaza "Las BO son públicas" por "Las públicas y las tuyas" (`visibility = 'public' or owner_id = auth.uid()`).
  - Votos: la de insertar exige además que la BO sea visible (`exists (select 1 from public.build_orders b where b.id = build_order_id)`, que ya pasa por la política de lectura); la de ver votos, lo mismo.
  - `public.subscriptions (user_id uuid not null default auth.uid() references public.profiles (id) on delete cascade, build_order_id uuid not null references public.build_orders (id) on delete cascade, created_at timestamptz not null default now(), primary key (user_id, build_order_id))` con índice por `build_order_id`. RLS: ver y borrar las tuyas; insertar con `user_id = auth.uid()` y la BO `public` y ajena. `grant select, delete` y `insert (build_order_id)` a `authenticated`; nada a `anon`.
  - `public.app_my_build_orders(p_token text) returns jsonb`: lista de objetos con las claves de Global Constraints (`author` = el `author` del JSON o, si no hay, el nombre de la cuenta; `uploader` = la cuenta; `own` true para las propias). Propias (cualquier visibilidad) y suscriptas públicas, propias primero y después por nombre.
  - `public.app_subscribe(p_token text, p_code text) returns text`: `'own'` si es propia (no guarda nada), `'subscribed'` si es pública ajena (`on conflict do nothing`), y si no existe o es privada ajena `raise exception 'No hay ninguna build order pública con el código %.' using errcode = 'P0002'`.
  - `public.app_unsubscribe(p_token text, p_code text) returns void` (sin error si no estaba).
  - `public.app_build_order(p_token text, p_code text) returns jsonb`: la BO si es pública o propia, con las claves de hoy de `GET /api/bo/<código>` (`code`, `name`, `civ`, `tags`, `author` = cuenta, `guide_author`, `votes`, `feudal_click`, `castle_click`, `updated_at`, `build_order`); null si no.
  - Las cuatro `app_*` son `security definer set search_path = ''`, validan con `app_token_owner` y se dan con `grant execute` a `anon, authenticated`.

- [ ] **Step 1: Escribir los tests** en `biblioteca.test.ts`, `describe("privadas y suscripciones")`:
  - una privada de Ana no la ve un anónimo ni Beto, y Ana sí;
  - lo ya publicado queda `public` (insertar sin `visibility`);
  - Beto no vota una privada de Ana; Ana cambia la visibilidad de la suya y Beto no la de Ana;
  - Beto se suscribe a una pública de Ana, no a una privada de Ana, no a una propia, y no ve ni borra las suscripciones de Ana;
  - al borrar la BO se van sus suscripciones;
  - `map` y `style` salen del JSON (`scouts` con `map: "arabia"` da `arabia` y `scouts`) y cambian al editar el JSON;
  - `app_my_build_orders` trae propias (también privadas, `own` true) y suscriptas (`own` false), no trae una suscripta que pasó a privada, y con un token inválido tira `28000`;
  - `app_subscribe` da `own` para una propia, `subscribed` para una pública ajena (dos veces sin error), y `P0002` para una privada ajena o un código que no existe; `app_unsubscribe` la saca;
  - `app_build_order` devuelve una privada propia, null para una privada ajena;
  - `app_publish` al reemplazar una privada la deja privada.

  En `estilo.test.ts`: arranca la base (exportar `database()` de `biblioteca.test.ts` a `supabase/tests/database.ts` para compartirla) y el Core (`startTestCoach`), y para cada `.json` de `build_orders/` y `build_orders/civs/` del repo, `select public.build_order_style($1)` es igual al `style` de `analyze`.
- [ ] **Step 2: Correr y ver que fallan.** Run (en `web/`): `npx vitest run supabase/tests`. Expected: fallan por columnas y funciones que no existen.
- [ ] **Step 3: Escribir la migración**, con un comentario al principio como las otras (qué cambia y que se corre en el SQL Editor).
- [ ] **Step 4: Correr.** Run: `npx vitest run supabase/tests`. Expected: pasan los viejos y los nuevos.
- [ ] **Step 5: Commit** (`Base: BO privadas, suscripciones y funciones de la app`).

### Task 4: lo propio y las acciones en la web

**Files:**
- Create: `web/src/lib/ownData.ts`
- Modify: `web/src/lib/buildOrder.ts` (`Visibility`, `columnsFor`), `web/src/lib/data.ts` (`visibility` en `BuildOrderDetail`), `web/src/app/actions.ts`, `web/src/lib/dbErrors.ts`
- Test: `web/src/lib/buildOrder.test.ts`, `web/src/lib/dbErrors.test.ts`

**Interfaces:**
- Produces: `export type Visibility = "public" | "private"` y `columnsFor(body, feudalClick?, castleClick?, visibility?: Visibility)` (agrega `visibility` solo si viene).
- Produces (`ownData.ts`, `server-only`, con `sessionClient`, sin caché): `getOwnBuildOrder(code: string): Promise<BuildOrderDetail | null>` (solo si es del usuario; código mal formado, null), `mySubscriptionIds(): Promise<Set<string> | null>` (ids de BO; null sin sesión), `mySubscriptions(): Promise<BuildOrderSummary[]>`.
- Produces (`actions.ts`): `publishBuildOrder(json, feudalClick, castleClick, visibility: Visibility)`, `updateBuildOrder(code, json, feudalClick, castleClick, visibility: Visibility)`, `setVisibility(code: string, visibility: Visibility): Promise<ActionResult>`, `setSubscription(buildOrderId: string, code: string, subscribe: boolean): Promise<ActionResult>`. Todas las que tocan una BO invalidan `LIBRARY_TAG` y `buildOrderTag(code)`.
- Produces (`dbErrors.ts`): `P0002` se muestra tal cual y da 404.

- [ ] **Step 1: Tests.** `columnsFor(body, 1, 2, "private").visibility === "private"` y sin el argumento no trae la clave; `dbErrorMessage({ code: "P0002", message: "No hay…" })` devuelve el mensaje y `dbErrorStatus` 404.
- [ ] **Step 2: Correr y ver que fallan.** Run: `npm test -- src/lib/buildOrder.test.ts src/lib/dbErrors.test.ts`. Expected: fallan.
- [ ] **Step 3: Implementar.** `BuildOrderDetail.visibility` se lee en `cachedBuildOrder` (será siempre `public`) y en `getOwnBuildOrder`.
- [ ] **Step 4: Correr** `npm test`, `npm run lint` y `npx tsc --noEmit`. Expected: limpios (los llamadores de `publishBuildOrder` y `updateBuildOrder` fallan en `tsc` hasta la Task 6: pasarles `"private"` y la visibilidad de la BO para que compile en este commit).
- [ ] **Step 5: Commit** (`Web: visibilidad y suscripciones en las acciones`).

### Task 5: endpoints de la app

**Files:**
- Create: `web/src/app/api/app/mis-bo/route.ts`, `web/src/app/api/app/suscripciones/[code]/route.ts`
- Modify: `web/src/app/api/bo/[code]/route.ts`, `web/src/app/api/bo/appApi.ts` (`appRpc`)

**Interfaces:**
- Consumes: `app_my_build_orders`, `app_subscribe`, `app_unsubscribe`, `app_build_order` (Task 3); `bearerToken`, `error`, `dbErrorStatus`, `dbErrorMessage`, `CODE_PATTERN`.
- Produces: `appRpc(request, fn, args)` en `appApi.ts`: sin token 401 con el mensaje de hoy, sin base 503, error de la base con `dbErrorStatus`.
- `GET /api/app/mis-bo` → 200 `{ items: [...] }`.
- `POST /api/app/suscripciones/<código>` → 200 `{ code, own: boolean }`; `DELETE` → 204. Código que no cumple `CODE_PATTERN` → 404 sin llamar a la base.
- `GET /api/bo/<código>`: primero la pública con caché (como hoy); si no está y hay token, `app_build_order` (error de token → 401; null → 404). Una pública con un token inválido sale igual.

- [ ] **Step 1: Implementar** los tres archivos (las funciones las prueba la Task 3; las rutas importan `server-only` y vitest no las puede cargar).
- [ ] **Step 2: Verificar.** Run (en `web/`): `npx tsc --noEmit`, `npm run lint`, `npm run build`. Expected: limpios, y el build lista `/api/app/mis-bo` y `/api/app/suscripciones/[code]` como dinámicas.
- [ ] **Step 3: Verificar sin token** con el servidor de desarrollo (`preview_start` con `web`): `curl -s -o /dev/null -w "%{http_code}" http://localhost:3000/api/app/mis-bo` da 401 y `curl -s -X POST -w "%{http_code}" http://localhost:3000/api/app/suscripciones/abcdef` da 401.
- [ ] **Step 4: Commit** (`API de la app: mis BO, suscripciones y privadas con token`).

### Task 6: pantallas

**Files:**
- Create: `web/src/components/SubscribeButton.tsx`, `web/src/components/VisibilityToggle.tsx`, `web/src/components/ConnectAppButton.tsx`
- Modify: `web/src/app/subir/page.tsx` (editar con `getOwnBuildOrder`), `web/src/components/editor/BuildOrderEditor.tsx` (Pública/Privada al publicar; `load` pasa por `importBuildOrder`), `web/src/components/editor/LoadMenu.tsx` ("Pegar JSON o guía"), `web/src/app/bo/[code]/page.tsx` (privada propia y suscribirse), `web/src/components/BoCard.tsx` y `web/src/app/biblioteca/page.tsx` (suscribirse en las tarjetas), `web/src/app/cuenta/page.tsx` (visibilidad, suscripciones, conectar)

**Interfaces:**
- Consumes: Task 2 (`importBuildOrder`), Task 4 (acciones y `ownData`).
- `SubscribeButton({ buildOrderId, code, subscribed, loggedIn, own })`: propia → texto "Tuya: está en tu coach"; sin sesión → enlace "Usar en el coach" a `/login?volver=/bo/<código>`; si no, botón "Usar en el coach" / "En tu coach ✓" con `useOptimistic`, como `VoteButton`.
- `VisibilityToggle({ code, visibility })`: lista desplegable Pública / Privada que llama a `setVisibility`.
- `ConnectAppButton()`: llama a `createAppToken("BO Coach")`, abre `bocoach://conectar/<token>` y muestra el token una vez con "Si la app no se abrió, pegalo en Cuenta de la web".

- [ ] **Step 1: Editor.** Junto a Publicar, un grupo de dos opciones "Pública" / "Privada" (marcada Privada si es nueva; al editar, la que tiene), y el texto corto "Privada: solo la ves vos y tu app". `load(json)`: `importBuildOrder`; si es `bocoach`, `analyze(json)` como hoy; si es una guía, `analyze(JSON.stringify(build_order))` y el aviso "Importada de buildorderguide.com" (o "de RTS Overlay") más los primeros 3 avisos, como en la app. `/subir?editar=` lee con `getOwnBuildOrder`.
- [ ] **Step 2: Detalle.** `Detail` busca primero `getBuildOrder` y, si no está, `getOwnBuildOrder` (la privada propia); si no, `notFound()`. Con privada, una marca "Privada" junto al nombre, sin votos ni JSON-LD, y `generateMetadata` no la indexa. Un componente `Subscribe` en `<Suspense>` lee la sesión y `mySubscriptionIds`.
- [ ] **Step 3: Biblioteca.** `Results` pide a la vez la página y `mySubscriptionIds()`; `BoCard` recibe `subscription?: { subscribed: boolean; own: boolean }` y con sesión muestra `SubscribeButton`. Sin sesión, la tarjeta queda como hoy.
- [ ] **Step 4: Mi cuenta.** En "Tus build orders", `VisibilityToggle` por renglón. Sección nueva "En tu coach" con `mySubscriptions()` y un botón "Quitar" por cada una (`setSubscription(..., false)`), o "Todavía no sumaste BO de otros: buscalas en la biblioteca". En "Conectar la app de escritorio", `ConnectAppButton` arriba del `TokenManager`.
- [ ] **Step 5: Verificar.** `npm test`, `npm run lint`, `npx tsc --noEmit`, `npm run build`. Después con `preview_start` `web` y el panel a la vista: `/subir` muestra Pública/Privada con Privada marcada, y pegar `tests/fixtures/rts-overlay/22p-scouts.json` en "Pegar JSON o guía" carga la BO con el aviso de importada (captura). La base de verdad no tiene la migración: la biblioteca y el detalle se ven como hoy y las acciones nuevas fallan con un error de la base; se anota.
- [ ] **Step 6: Commits**, uno por pantalla (editor, detalle, biblioteca, cuenta).

### Task 7: notas, pendientes y versión

**Files:**
- Modify: `docs/pendientes-usuario.md` (CRLF: escribir con `newline=''`), `CLAUDE.md`, `PLAN.md` (sección 11, la parte 3 hecha), `src/BoCoach.App/BoCoach.App.csproj` (`<Version>`)

- [ ] **Step 1: Pendiente** 0i en `docs/pendientes-usuario.md`, antes de "## El modo asistido de la web (D12)": correr `web/supabase/migrations/20261031000000_privadas_suscripciones.sql` en el SQL Editor de Supabase (proyecto `muhyzwhpakxydqzrrbyq`, pegar el archivo entero, Run), y después probar: publicar una privada desde `/subir`, verla en una ventana privada del navegador (sin sesión) y que dé 404, suscribirse a una BO de otra cuenta, y cambiar la visibilidad desde Mi cuenta.
- [ ] **Step 2: CLAUDE.md**, en la sección de la Fase 10: lo de esta parte (columnas generadas, `app_*`, `ownData`, importar en la web) y que la web publicada sigue como antes hasta correr la migración.
- [ ] **Step 3: Versión** con la skill `nueva-version` (sube a 0.9.51: cambió el Core y la importación de la app), tests de las dos partes y push de la rama.
