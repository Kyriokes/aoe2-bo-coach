# AoE2 BO Coach

Overlay liviano para Age of Empires II: Definitive Edition que lee la pantalla (OCR y plantillas), sigue una build order (BO), se adapta a los desvíos del jugador y sugiere cómo repartir aldeanos. Hay una web con biblioteca y editor de BO: https://aoe2-bo-coach.vercel.app

Este archivo se carga en cada sesión, así que tiene que quedar corto: lo demás está en `docs/` (índice en `docs/README.md`). **Antes de tocar un tema, leé su nota:** casi todas las reglas raras salieron de una partida real, y una "mejora" que las ignora vuelve a romper lo que ya se arregló.

## Cómo trabajar conmigo

- Hablame en español rioplatense informal.
- No uses guiones largos (—) en textos, usá comas.
- Guiame paso a paso y no asumas lo que veo en una interfaz.
- Antes de borrar código o archivos, preferí reordenar o mover.
- Seguimos la hoja de ruta (`PLAN.md`, sección 11). No des por terminado un ítem sin que se cumpla su criterio de salida.
- **Autonomía:** tenés permiso para avanzar, commitear, pushear y mergear a `main` sin preguntarme. Frená solo cuando un paso necesite algo que únicamente yo puedo hacer (jugar una partida, calibrar en pantalla, probar usabilidad, crear cuentas, correr una migración en Supabase); en ese caso dejame instrucciones paso a paso en `docs/pendientes-usuario.md`.

## Reglas del proyecto

- **Solo lectura de pantalla.** Nunca leer memoria del proceso, inyectar código ni enviar inputs al juego. El juego es un solo proceso sin API hacia afuera: "suscribirse a sus eventos" haría falta inyectar o leer memoria, que es lo que se considera cheat.
- **Runtime 100 % C# .NET 10 (LTS) + WPF**, elegido por rendimiento: menos RAM, ejecutable más chico, OCR nativo de Windows (`Windows.Media.Ocr`), overlay y atajos con APIs Win32. No mezclar Python en runtime. **Python solo como herramienta offline** (`tools/extractor`, `tools/stats`).
- **Separación estricta:** `BoCoach.Core` (`net10.0`) es lógica pura, sin Windows ni UI, y también corre en WebAssembly con globalización invariante: nada que dependa de la cultura. `ArquitecturaTests` falla si referencia ensamblados de Windows o de la app. `Vision`, `App` y sus tests son `net10.0-windows10.0.19041.0`.
- **Un solo Planner:** la web valida y simula con el `Core` compilado a WebAssembly (`src/BoCoach.Wasm`). Lo que se copia a mano (por ejemplo `StepText` en `web/src/lib/stepText.ts`, los topes del validador en zod y en la base) tiene un test que compara los dos lados: si cambiás uno, cambiá el otro.
- **Lo que viene de la web es entrada no confiable:** BO, códigos, listas y registros. Tiene que validarse y tener tope antes de llegar al Planner, a una ruta de archivo o al HTML del informe.
- **Mapear por ID, no por nombre:** los nombres internos del `.dat` están desfasados (la tech 101 "Middle Age" es el paso a Feudal).
- **Textos:** los de la app en `src/BoCoach.App/Texts.cs`, los del Core con `Lang.L(castellano, inglés)`, los de la web en `web/src/i18n/{es,en}.ts`. Dos idiomas siempre.
- **Test primero** (que falle por el motivo correcto) y **un commit por cambio**, en castellano, como una frase que dice qué quedó mejor. Warnings = errores.
- La web no usa la clave de administrador de Supabase: la base se defiende sola con permisos por fila, porque cualquiera le puede hablar directo con la clave pública.

## Comandos

```bash
dotnet build BoCoach.slnx -c Release
dotnet test BoCoach.slnx -c Release
dotnet run --project tools/ReplaySession -c Release -- <comando>
```

En `web/`: `npm ci`, `npm test` (corre las migraciones sobre PGlite y el Core en WebAssembly), `npm run lint`, `npm run typecheck` (`next typegen` + `tsc`), `npm run build` (unos 2:30). Detalles y modos de prueba sin el juego en [CONTRIBUTING.md](CONTRIBUTING.md).

## Git y publicación

- Se trabaja en `fase-9-refinamiento`. **`main` es producción:** un push despliega la web y, si la `<Version>` de `BoCoach.App.csproj` no tiene etiqueta, publica la release que baja todo el mundo. Por eso el push a `main` se hace cuando el usuario lo pide en el chat (por iniciativa propia lo bloquea el clasificador de permisos), y las migraciones las corre él en el SQL Editor **antes** de ese push.
- Commiteá antes de compilar lo que el usuario va a probar: la app muestra el commit en su versión. Usá la skill `nueva-version`.
- Toda BO que use entidades nuevas de `game_data` se publica después de que esa versión esté en `main`: con una app vieja da error y el coach no arranca.

## Dónde está cada cosa

| Tema | Dónde |
|---|---|
| Índice de toda la documentación | `docs/README.md` |
| Lo que le toca hacer al usuario, en orden | `docs/pendientes-usuario.md` |
| Plan, formatos y hoja de ruta | `PLAN.md` (la sección 11 es lo vigente) |
| PC, herramientas, rutas, antivirus | `docs/dev/entorno.md` |
| CI, releases, cómo ver el estado sin iniciar sesión | `docs/dev/ci-y-releases.md` |
| El `.dat`, `game_data`, eventos, bonificaciones de civ | `docs/dev/datos-del-juego.md` |
| Leer la pantalla: HUD, mod, letras, log, cola, calibración, pantalla de carga | `docs/dev/vision-y-hud.md` |
| El coach: Tracker, Planner, creencia, GD, postura, señales, informe | `docs/dev/coach.md` |
| BO: formato, importadores, BO por civ, simulación, modo asistido | `docs/dev/build-orders.md` |
| La app: tablero, overlay, atajos, modo desarrollador, caché, idiomas | `docs/dev/app.md` |
| La web: Next, Supabase, editor, idiomas, despliegue | `docs/dev/web.md`, `web/README.md`, `web/DEPLOY.md` |
| Registro de partida y privacidad | `docs/dev/registro-de-partida.md` |
| Estadísticas de cruces | `docs/dev/estadisticas-de-cruces.md` |
| Auditorías de seguridad y errores | `docs/auditorias/` |
| Partidas revisadas una por una y estado por fases | `docs/historial/` |
| Diseños aprobados y planes ya ejecutados | `docs/superpowers/specs`, `docs/superpowers/plans` |

Skills del proyecto: `revisar-grabacion` (el usuario jugó o vio algo mal), `armar-letras-e-iconos` (un número o un ícono se lee mal), `nueva-version` (antes de decirle "probalo").

## Trampas que ya costaron tiempo

- Si la app está abierta, su `BoCoach.exe` queda bloqueado: compilá en Release o con `-o` a otra carpeta. Es de instancia única: otra instancia le avisa a la abierta y se cierra (los modos `--screenshot`, `--overlay-screenshot` y `--preview` no cuentan).
- Los archivos de `web/` son CRLF: un reemplazo por script con `\n` no encuentra nada.
- Con el panel del navegador oculto, `requestAnimationFrame` no corre: React no hidrata y el .NET de WebAssembly no termina de arrancar. No es un error de la web: mostrá el panel o probá con `curl`.
- Después de cambiar rutas de la web, `npx next typegen` antes de `tsc`. Una página nueva va en `PAGES` de `web/src/i18n/routing.ts`, si no el proxy da 404.
- PGlite no arma la API de PostgREST: un embed ambiguo solo falla con Supabase real.
- Los tests del pipe de instancia única y de PGlite son sensibles a la carga: si falla uno solo con la PC ocupada, repetilo antes de concluir nada.
