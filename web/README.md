# Web de AoE2 BO Coach

Biblioteca pública de build orders: buscar, ver la línea de tiempo de cada BO, votar, publicar (desde la web o desde la app) y abrirlas en BO Coach con `bocoach://import/<código>`.

- **Next.js 16** (App Router, Cache Components) en Vercel.
- **Supabase**: Postgres, cuentas con Discord y Google, y permisos por fila (`supabase/migrations`).
- **El Core de C# en WebAssembly** (`../src/BoCoach.Wasm`): la web valida y simula cada BO con el mismo validador y el mismo Planner que la app. `scripts/build-coach.mjs` lo compila a `public/coach` antes de `dev`, `build` y `test` (hace falta el SDK de .NET de `../global.json`).

Para ponerla en línea (Supabase, Discord, Google y Vercel), seguí [DEPLOY.md](DEPLOY.md).

## Desarrollo

```bash
cp .env.example .env.local   # completar con los datos del proyecto de Supabase
npm install
npm run dev
```

Sin `.env.local` la web arranca igual: la biblioteca avisa que no hay base, y "Subir una BO" funciona para revisar y simular.

## Tests

```bash
npm test
```

- `supabase/tests`: la migración sobre PGlite (Postgres en WebAssembly, sin Docker): permisos por fila, votos, tokens de la app y publicación con token.
- `src/lib/coach`: el Core en WebAssembly corrido en Node.

## API para la app de escritorio

| Método | Ruta | Qué hace |
|---|---|---|
| GET | `/api/bo?q=&civ=&tag=&orden=votos\|recientes` | Biblioteca (sin el JSON de cada BO) |
| GET | `/api/bo/<código>` | Una BO con su JSON (`build_order`) |
| POST | `/api/bo` | Publica. `Authorization: Bearer <token>`; cuerpo `{ build_order, feudal_click?, castle_click? }` |
| PUT | `/api/bo/<código>` | Reemplaza una BO propia (mismo cuerpo) |

El token se crea en "Mi cuenta" y la base guarda solo su hash; la función `app_publish` lo valida, así la web no necesita la clave de administrador de Supabase.
