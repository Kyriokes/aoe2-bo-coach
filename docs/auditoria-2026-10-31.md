# Auditoría del proyecto (2026-10-31)

Revisión de seguridad y de errores sobre `fase-9-refinamiento` en la v0.9.56: web (Next.js 16 + Supabase), base y
migraciones, app WPF, Core, CI y herramientas. Se leyó el código, se corrieron los tests de las tres partes y se
verificó en vivo (solo lecturas, con la clave publishable) qué expone el proyecto de Supabase y qué cabeceras manda la
web publicada.

## Resumen

No hay nada crítico ni alto. Las decisiones de base están bien tomadas y verificadas: permisos por fila, el token de
la app guardado como hash y validado en funciones `security definer` con las internas revocadas, la clave de
administrador de Supabase nunca se usa, no hay secretos en el repo, las acciones del CI fijadas por SHA con el token en
solo lectura, el token de la app cifrado con DPAPI y solo por https, el pipe de instancia única solo para el mismo
usuario, y cada BO validada (web, base y app) antes de simularla. Las dos migraciones pendientes (topes y privadas)
están aplicadas en producción.

Lo que vale la pena arreglar está en orden de importancia; después viene lo verificado como correcto y lo que no se
revisó.

## Hallazgos

### Medio

**M1. El proveedor de email de Supabase está prendido y la web no lo usa.** `auth/v1/settings` dice `email: true`.
Cualquiera con la clave publishable (está en el HTML de la web) puede crear cuentas por `POST /auth/v1/signup` con
correos descartables, sin pasar por Discord ni Google, y cada cuenta puede publicar 150 BO por día: es la vía más
fácil para llenar la biblioteca de basura o de nombres falsos. Arreglo (solo vos, en el panel de Supabase):
Authentication → Providers → Email → apagarlo. Si alguna vez hace falta email, prender el CAPTCHA de Auth. Está en
`docs/pendientes-usuario.md`.

**M2. El JSON de la BO no está atado a sus columnas y `author` no tiene tope en la base.** La web guarda nombre, civ,
etiquetas y descripción en columnas (con topes) y además el JSON entero en `body`. Con una sesión o un token se puede
hablar directo con PostgREST y mandar un `body` cuyo `name`, `civ`, `tags` o `description` no coincidan con las
columnas, y `body->>'author'` solo lo limita zod (60) en la web: por la base entra cualquier largo hasta los 512 KB
del cuerpo. Efectos: la biblioteca lista una BO como de una civ y la simula como de otra; `guide_author` sale sin
recortar en las tarjetas, la página, `/api/bo` y la lista de la app (`byline` no lo corta). Arreglo: en una migración,
`check (char_length(body->>'author') <= 60)` y, mejor, que las columnas salgan del JSON como ya pasa con `map` y
`style` (generadas), o un `check` de igualdad (`body->>'name' = name`, `body->>'civ' = civ`). Mientras tanto, recortar
`author` al mostrarlo.

### Bajo

**B1. Un nombre vacío en el registro rompe el alta.** `handle_new_user` usa `coalesce(full_name, name, user_name,
'Jugador')`: una cadena vacía no es null, así que un proveedor que mande `full_name: ""` deja `display_name = ''`, el
`check (1..60)` falla y Supabase responde "Database error saving new user". Arreglo: `coalesce(nullif(trim(...), ''),
...)` en cada opción.

**B2. La API pública y las páginas exponen los UUID de las cuentas.** `GET /api/bo` manda `id` y `owner_id` de cada BO,
y `profiles` se puede listar con la clave publishable (id, nombre, avatar, fecha de alta: hoy una sola fila). Los
votos (`votes`) también se listan: quién votó qué. Nada de eso da acceso a nada, pero permite enumerar cuentas y
cruzarlas. Arreglo barato: no mandar `id` ni `owner_id` en `/api/bo` (la app no los usa) y, si se quiere, una vista
pública de `profiles` sin `created_at`.

**B3. El escucha de enlaces se puede morir en silencio.** `SingleInstance.Listen` atrapa solo `IOException` y
`OperationCanceledException`. Si crear el pipe falla con otra cosa (por ejemplo `UnauthorizedAccessException` porque
otro proceso ya tiene ese nombre), la tarea termina sin aviso: la app sigue abierta pero los `bocoach://` y el
"activate" de una segunda instancia se pierden, y esa segunda instancia se cierra sin hacer nada. Además, una
`IOException` repetida vuelve a intentar sin espera. Arreglo: atrapar `Exception` y avisar en la barra de estado, y una
pausa corta entre reintentos.

**B4. Dos tests flojos bajo carga.** Con los tests de .NET y los de la web corriendo a la vez fallaron
`WebTests.Un_enlace_abierto_con_la_app_ya_abierta_le_llega_a_esa_ventana` (el cliente del pipe conecta con 3 s de
tope) y `supabase/tests/biblioteca.test.ts` (el `beforeEach` que levanta PGlite pasa los 10 s del `hookTimeout`). Los
dos pasan solos. Es casi seguro lo que falló una vez en el CI el 2026-10-20. Arreglo: `hookTimeout: 60_000` en
`vitest.config.ts` y un tope más holgado en el test del pipe.

**B5. `--publish` publica como pública; el editor de la web, como privada.** `app_publish` no recibe visibilidad y la
columna arranca en `public`, mientras que una BO nueva en `/subir` nace privada. Es una decisión, no un error, pero
conviene dejarla escrita o aceptar `p_visibility` en `app_publish`.

**B6. Sin actualizaciones automáticas de dependencias.** No hay Dependabot ni Renovate. `npm audit` da 0 en producción
y 16 moderadas en herramientas de lint (`minimatch` por los plugins de ESLint, sin versión corregida todavía). Hay
versiones nuevas de Next (16.4.3), React (19.3.1) y PGlite (0.5.14). Arreglo: un `.github/dependabot.yml` para npm,
NuGet, pip y GitHub Actions.

**B7. El build de Vercel baja `dotnet-install.sh` sin fijarlo.** `scripts/vercel-build.sh` hace `curl
https://dot.net/v1/dotnet-install.sh` y lo corre. Es lo que recomienda Microsoft y va por https, pero no hay suma de
control: si ese script cambiara con mala intención, correría en el build. El CI de GitHub no tiene el problema
(`setup-dotnet` fijado por SHA). Se puede guardar el script en el repo o verificar su SHA-256.

### Informativo

- **CSP con `'unsafe-inline'` para scripts.** Next hidrata con scripts en línea y las páginas se cachean, así que no
  hay nonce por pedido. Hoy no hay ningún lugar donde se inyecte HTML (el único `dangerouslySetInnerHTML` es el JSON-LD,
  que escapa `<`), pero si algún día lo hubiera, la CSP no lo frenaría.
- **`display_name` lo cambia cada uno** a cualquier texto de 1 a 60: se puede imitar el nombre de otro. Las BO ajenas
  siempre muestran "subida por <cuenta>", así que el daño es solo de confusión.
- **`web.json` acepta cualquier dirección absoluta** (también http fuera de esta PC); el token igual no viaja porque
  `SendsTokenSafely` lo corta antes de mandar.
- **`app_token_owner` hace un `update` por cada pedido**, también con un token inválido (afecta 0 filas: barato). Sin
  límite de pedidos en `/api/*` aparte de los de la base; el token tiene 256 bits y no se adivina.
- **La captura de diagnóstico** guarda la pantalla entera y tu nombre de jugador en `%LOCALAPPDATA%`: solo importa si
  se comparte la carpeta.

## Verificado como correcto

- **Base.** RLS en las 5 tablas; "pública o tuya" en `build_orders` y los votos solo a lo que se ve; `insert`/`update`
  con columnas explícitas (código, dueño, votos y fechas no se tocan); `hash_token` y `app_token_owner` revocadas (en
  vivo responden 42501); `subscriptions` sin acceso anónimo (en vivo, "permission denied"); funciones con
  `search_path = ''`; las funciones de los checks de topes están en producción (`build_order_body_ok` rechaza
  `count: 500`); `visibility` existe. Los 31 caracteres del código corto dan 887 millones de combinaciones y la
  columna es `unique`.
- **Web.** `parseBuildOrder` con tope de 512 KB, 300 pasos, `count` y `builders` de 1 a 200; filtros de `civ` y `tag`
  con lista blanca; el buscador saca lo que rompe la sintaxis de PostgREST; página con tope 1000; `next` del login y de
  `/auth/callback` solo rutas internas (y sin `//`); el origen se antepone, así que `/\` tampoco sale del sitio;
  JSON-LD escapado; las cabeceras llegan en producción (CSP, HSTS con preload, nosniff, X-Frame-Options DENY,
  Referrer-Policy, Permissions-Policy, COOP, sin `X-Powered-By`); lo cacheado usa el cliente anónimo (nunca ve
  privadas) y lo propio la sesión; `/api/*` sin CORS (otro origen no puede leerlo) y la API de la app solo con `Bearer
  bocoach_<64 hex>`; los enlaces externos con `rel="noopener noreferrer"`; sin `.env` en git y `.env.example` sin
  claves; `npm run lint` y `typecheck` limpios.
- **App.** Token con DPAPI (`CurrentUser`) y nunca en texto plano; solo viaja por https o a localhost; `HttpClient` con
  tope de 8 MB y 20 a 30 s; `bocoach://conectar/<token>` pide confirmación con el nombre de la cuenta antes de guardar
  (una página cualquiera puede abrir ese enlace con el token de otro); `bocoach://import` confirma con nombre y
  cuenta; `--import` como primer argumento anula cualquier otro que venga pegado; el pipe con `CurrentUserOnly` de los
  dos lados y lectura con tope de 4 KB; `OpenUrl` solo http(s); `settings.json` y `web.json` rotos no impiden abrir
  la app; `Validator.Validate` antes de `Planner.Simulate` también para las BO bajadas; un total de aldeanos
  imposible se descarta; la búsqueda de Elo codifica el nombre como JSON y después como parte de la URL; la carpeta
  del juego sale de la ruta del proceso (sin leer memoria) o del registro de Steam; el instalador del mod escribe solo
  dentro de `mods/local/BoCoach HUD` de cada perfil. No hay `async void` ni `catch` vacíos en `src`.
- **CI.** Acciones fijadas por SHA, `permissions: contents: read` en el CI y `write` solo en la release, `ubuntu-24.04`
  y `windows-latest`, `npm ci` con lockfile.
- **Tests.** .NET: 1 fallo en la corrida completa (B4) que pasa solo; el resto en verde. Web: 184 de 185 en la corrida
  completa, el que faltaba (B4) pasa solo. Python de `tools/stats`: no se corrió acá (lo cubre el CI).

## No revisado a fondo

- Las herramientas offline en Python (`tools/extractor`, `tools/stats`): no tienen superficie de ataque hacia el
  usuario final; solo se miró que no bajen nada en los tests.
- `BoCoach.Vision` (OCR, letras, cola de producción): lee píxeles de la pantalla, sin entradas de terceros.
- La lógica del motor (Planner, Tracker, economía): la cubren los tests; no es tema de seguridad.
