# Web de AoE2 BO Coach

Biblioteca pública de build orders: buscar, ver la línea de tiempo de cada BO, votar, armarlas en un editor (`/subir`, con arrastre o con objetivos en la pestaña Asistido), publicarlas como públicas o privadas, y usarlas en BO Coach: "Usar en el coach" las suma a la lista de la cuenta y `bocoach://import/<código>` las abre directo. Estadísticas de cruces en `/cruces`, roadmap en `/roadmap`, en castellano e inglés.

- **Next.js 16** (App Router, Cache Components) en Vercel.
- **Supabase**: Postgres, cuentas con Discord y Google, permisos por fila y un bucket privado para los registros de partida (`supabase/migrations`). La clave publishable es pública, así que la base se defiende sola: la web no tiene la clave de administrador.
- **El Core de C# en WebAssembly** (`../src/BoCoach.Wasm`): la web valida y simula cada BO con el mismo validador y el mismo Planner que la app. `scripts/build-coach.mjs` lo compila a `public/coach` antes de `dev`, `build` y `test` (hace falta el SDK de .NET de `../global.json`).

Para ponerla en línea (Supabase, Discord, Google y Vercel), seguí [DEPLOY.md](DEPLOY.md).

## Desarrollo

```bash
cp .env.example .env.local   # completar con los datos del proyecto de Supabase
npm ci
npm run dev
```

Sin `.env.local` la web arranca igual: la biblioteca avisa que no hay base, y el editor funciona para armar y simular. Con `.env.local`, ojo: `npm run dev` y `npm run build` hablan con el Supabase de producción.

Los textos van en `src/i18n/es.ts` y `en.ts` (un test exige las mismas claves); una página nueva se agrega a `PAGES` de `src/i18n/routing.ts`, y un archivo o carpeta nueva de `public/`, a `SITE_FILES`. Lo aprendido, en `../docs/dev/web.md`.

## Tests

```bash
npm test
npm run lint
npm run typecheck
npm run build
```

- `supabase/tests`: las migraciones sobre PGlite (Postgres en WebAssembly, sin Docker), con un Storage falso: permisos por fila, votos, topes, tokens de la app, publicación con token y registros de partida. PGlite tiene una sola conexión y no arma la API de PostgREST: la concurrencia real y los embeds ambiguos solo se ven contra Supabase.
- `src/lib/coach`: el Core en WebAssembly corrido en Node.

## API para la app de escritorio

| Método | Ruta | Qué hace |
|---|---|---|
| GET | `/api/bo?q=&civ=&tag=&orden=votos\|recientes` | Biblioteca (sin el JSON de cada BO) |
| GET | `/api/bo/<código>` | Una BO con su JSON (`build_order`). Con token, también las privadas propias |
| POST | `/api/bo` | Publica. `Authorization: Bearer <token>`; cuerpo `{ build_order, feudal_click?, castle_click? }` |
| PUT | `/api/bo/<código>` | Reemplaza una BO propia (mismo cuerpo) |
| GET | `/api/app/mis-bo` | Con token: la cuenta y su lista (`{ account, items }`), propias y suscriptas |
| POST | `/api/app/suscripciones/<código>` | Con token: suma una BO pública a la lista |
| DELETE | `/api/app/suscripciones/<código>` | Con token: la saca de la lista |
| POST | `/api/app/registros` | El registro de una partida, en gzip. Sin cuenta: cabeceras `X-BoCoach-Install`, `-Secret`, `-Game` y `-Version`. 201, o 200 con `{ duplicate: true }` si ya estaba |
| DELETE | `/api/app/registros` | El pedido de borrado de lo que subió esa instalación. `{ forgotten }` |

El token se crea en "Mi cuenta" y la base guarda solo su hash; la función `app_publish` lo valida, así la web no necesita la clave de administrador de Supabase. El de "Conectar la app" sirve para usar la app, pero no para publicar: para `BoCoach.exe --publish` hace falta uno creado con "Crear token".
