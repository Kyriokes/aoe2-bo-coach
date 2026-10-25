# Plan de implementación: estadísticas reales de cruces, sub-paso (d) (la web `/cruces`)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** La web tiene `/cruces` (elegir dos civs), `/cruces/<civ>` (sus rivales ordenados y cómo le va por equipos) y `/cruces/<a>-vs-<b>` (el cruce: general, por mapa y por duración, con fuente y "cómo se calcula"), en el HTML desde el servidor, en el sitemap y enlazadas desde cada BO.

**Architecture:** `build-coach.mjs` copia `data/matchup_stats.json` a `public/coach/`, como los demás datos. `src/lib/matchups.ts` es puro: lee el JSON (la misma tolerancia que el `MatchupStats` del Core: `schema_version` 1, 720 enteros por entrada), consulta casilleros (con la inversión), aplica la regla de honestidad y arma los textos cortos. `src/lib/matchupsServer.ts` (solo servidor) lo lee una vez del disco. Las páginas se prerenderizan todas al compilar (`generateStaticParams`, 53 civs + 1378 parejas) y los selectores de mapa, Elo y duración son componentes de cliente que reciben solo los casilleros de esa página.

**Tech Stack:** Next.js 16 (App Router, Cache Components), React 19, TypeScript, Tailwind 4, vitest.

**Spec:** `docs/superpowers/specs/2026-10-25-matchup-stats-design.md` (sección 5.5, y 3.2 y 4.5 para el formato y la regla), con lo que dejaron (a) a (c) en `PLAN.md` D10.

## Global Constraints

- Formato del JSON: el de la spec 3.2 tal como lo escribe `tools/stats` (cabecera `source`, `patch`, `matches`, `bands`, `interval`, `validation`; `one_vs_one` con claves `"a|b"` en orden alfabético y `p` de la primera; `team` por civ; 180 casilleros `[p, desde, hasta, n]` en milésimas y partidas, en el orden mapa (todos + 4) × Elo (todas + 5) × duración (todas + 5)). En equipos `n` son apariciones de la civ, no partidas: decirlo en la página.
- Regla de honestidad (spec 4.5): parejo si el rango incluye el 50 %; tendencia solo entre la franja más corta y la más larga con `n` > 0 si sus rangos no se tocan; si no, "Sin diferencia clara por duración" (el texto que eligió la revisión de b). Es lo que ya hace `MatchupStats` en C#: copiar con cuidado y probar con los mismos casos.
- Nombres en castellano: civs con `CIV_NAMES` de `civNames.ts`; mapas Arabia, Arena, Bosque Negro y Nómada (los `name` de `data/maps.json`); franjas de Elo "menos de 850", "de 850 a 1050", "de 1050 a 1300", "de 1300 a 1600", "1600 o más" y de duración "menos de 20 min", "de 20 a 30 min", "de 30 a 40 min", "de 40 a 55 min", "55 min o más" (minutos de juego), armadas desde `bands`.
- URLs: `/cruces`, `/cruces/<civ>` y `/cruces/<a>-vs-<b>` con `a < b` (orden alfabético de las claves, como las del JSON); la otra dirección redirige con `permanentRedirect`; lo que no existe da `notFound`. Validación de claves sin leer los datos (con `CIV_NAMES`), así la redirección y el 404 no tocan el disco.
- Porcentajes redondeados al entero con espacio antes de `%` ("49 %"). Textos en castellano rioplatense, sin guiones largos. Estilo: las clases de la web (`text-faint`, `text-muted`, `bg-panel`, `border-line`, `text-amber`, `button`) y los colores de `globals.css`.
- Sin dependencias nuevas. Los tests corren con `npm test` (vitest); además `npm run lint`, `npm run typecheck` y `npm run build` tienen que pasar (el CI corre los cuatro).
- Commits chicos en `fase-9-refinamiento`, terminados en `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

## Review Focus

1. **El cruce pedido al revés** (`/cruces/franks-vs-britons`): redirige a `/cruces/britons-vs-franks` y la página muestra la chance desde el punto de vista que corresponda en cada renglón. Test en la Tarea 1 (`al revés invierte la chance y el rango`) y verificación con `curl` en la Tarea 4.
2. **Civ de Crónicas o clave inventada** (`/cruces/spartans`, `/cruces/zzz-vs-franks`): 404, nunca un error 500. Verificación con `curl` en la Tarea 4.
3. **Nómada sin partidas 1v1:** la página lo dice ("estimado: no hay partidas de este cruce en Nómada") y no afirma nada por duración. Test en la Tarea 1 (`sin partidas no hay tendencia`) y en la Tarea 2 (`textos de un mapa sin partidas`).
4. **Sin el archivo de cruces** (por ejemplo, un build sin `build-coach`): las páginas dicen que no hay datos en vez de romper el build. Test en la Tarea 1 (`un JSON roto o de otra versión da null`) y en la Tarea 2 (el lector del servidor devuelve null si falta el archivo).
5. **Celular:** las tablas y el gráfico se desplazan de costado sin romper la página (como la línea de tiempo). Verificación en la Tarea 4 con el navegador a 375 px.

---

### Task 1: El lector de cruces en TypeScript

**Files:**
- Modify: `web/scripts/build-coach.mjs` (copia `data/matchup_stats.json` a `public/coach/matchup_stats.json`; comentario de la cabecera)
- Create: `web/src/lib/matchups.ts`, `web/src/lib/matchups.test.ts`

**Interfaces:**
- Produces (`matchups.ts`): `type Cell = { p: number; from: number; to: number; n: number }` (p, from y to de 0 a 1); `type Trend = { shortBand: number; short: Cell; longBand: number; long: Cell; favorsShort: boolean }`; `interface MatchupData { source: { name?: string; url?: string; from?: string; to?: string }; patch: { label?: string; start?: string; previousMixed: boolean }; matches: { oneVsOne: number; team: number }; eloLimits: number[]; minuteLimits: number[]; mapKeys: string[]; oneVsOne: Map<string, Int32Array>; team: Map<string, Int32Array> }`; `parseMatchups(json: string): MatchupData | null`; `oneVsOne(data, civ, rival, map, elo, duration): Cell | null` (null en un espejo o sin la pareja; invertido si `civ` va segunda); `team(data, civ, map, elo, duration): Cell | null`; `trend(cellOf: (band: number) => Cell | null): Trend | null`; `isEven(cell)`; `pairSlug(a, b): string` (`"britons-vs-franks"`, ordenado); `parseSlug(slug: string): { civ: string } | { a: string; b: string; canonical: boolean } | null` (claves de `CIV_NAMES`; `canonical` false si viene al revés); `percent(x: number): number`; `chanceText(cell): string` (`"49 % (46 a 52)"`); `MAP_NAMES` (`{ arabia: "Arabia", arena: "Arena", black_forest: "Bosque Negro", nomad: "Nómada" }`); `eloLabel(data, band)` y `durationLabel(data, band)` (los textos de las restricciones; `band` 0 = "todas" / "cualquier duración").

- [ ] **Step 1: Tests** (con un JSON armado en el test, del mismo formato, y uno sobre `../data/matchup_stats.json`): `lee la cabecera y las franjas`, `cada casillero en su lugar` (p = 100·mapa + 10·elo + duración), `al revés invierte la chance y el rango` (600 de 550 a 650 → 0,4 de 0,35 a 0,45, mismo n), `espejo y civ sin datos dan null`, `tendencia a partidas cortas` (franja 1 = 540 de 491 a 587 con n 50, franja 5 = 420 de 380 a 460 con n 40), `sin tendencia si los rangos se tocan`, `sin partidas no hay tendencia`, `un JSON roto o de otra versión da null` ("", "{", `schema_version` 2, entradas de otro largo se ignoran), `el slug ordena y reconoce las civs` (`pairSlug("franks","britons")` = `"britons-vs-franks"`; `parseSlug("franks-vs-britons")` → canónico false; `"zzz-vs-franks"` y `"franks-vs-franks"` → null; `"spartans"` → `{ civ: "spartans" }` porque está en `CIV_NAMES`: el 404 lo da la página, que no le encuentra datos), `los nombres de mapas son los de maps.json`, `el archivo real` (1378 parejas, las 53 civs con nombre en `CIV_NAMES`, `p(a,b) + p(b,a)` = 1 con 0,001 de tolerancia en 5 parejas).
- [ ] **Step 2: Ver que fallan** (`npx vitest run src/lib/matchups.test.ts`).
- [ ] **Step 3: Implementar** y sumar la copia a `build-coach.mjs`.
- [ ] **Step 4: Ver que pasan.**
- [ ] **Step 5: Commit** `web: lector de las estadísticas de cruces`.

### Task 2: Las páginas `/cruces`, `/cruces/<civ>` y `/cruces/<a>-vs-<b>`

**Files:**
- Create: `web/src/lib/matchupsServer.ts` (`import "server-only"`; `getMatchups(): MatchupData | null`, leído una vez de `public/coach/matchup_stats.json`; null si falta), `web/src/lib/matchupView.ts` (puro: lo que necesita cada página) y `web/src/lib/matchupView.test.ts`
- Create: `web/src/app/cruces/page.tsx`, `web/src/app/cruces/[slug]/page.tsx`, `web/src/components/MatchupRivals.tsx` y `web/src/components/MatchupDuration.tsx` (`"use client"`)

**Interfaces:**
- Consumes: todo `matchups.ts`.
- Produces (`matchupView.ts`): `rivalsOf(data, civ): RivalRow[]` (`{ rival, cells: Cell[] }` con los 30 casilleros mapa × Elo de duración "todas", para que el cliente ordene sin recibir todo el archivo); `teamOf(data, civ): Cell[]` (los 30 de equipos); `pairCells(data, a, b): Int32Array` (los 720 enteros desde `a`); `pairSummary(data, a, b): { general: Cell; maps: { key: string; name: string; cell: Cell }[]; trendText: string | null; estimated: boolean }` (la frase de duración con la regla de honestidad: `"Se gana más en partidas cortas: 54 % menos de 20 min, 42 % 55 min o más."`, `"Se gana más en partidas largas: ..."` o `"Sin diferencia clara por duración."`; null si no hay dos franjas con partidas); `mapLine(cell, mapName)`: con `n` 0, `"Estimado: no hay partidas de este cruce en <Mapa>."`.

Páginas:
- `/cruces`: título "Cruces de civs en Age of Empires II", qué es (partidas clasificatorias reales, fuente y fechas de la cabecera), un formulario GET con dos listas de civs (`?a=&b=`; con las dos, redirige al cruce), y la lista de civs con enlace a `/cruces/<civ>`. Sin datos: "Todavía no hay estadísticas de cruces."
- `/cruces/<civ>`: "Francos: cómo le va contra cada civ"; `MatchupRivals` (listas de mapa y franja de Elo; tabla de rivales ordenada por chance, con rango y partidas, cada una enlazada a su cruce; "parejo" marcado); "Por equipos" (por mapa: chance contra el resto, rango y apariciones, con la aclaración de que en equipos se cuentan apariciones).
- `/cruces/<a>-vs-<b>`: "Britanos contra francos"; el general desde los dos lados ("Britanos ganan 49 % (46 a 52); francos 51 %"), una tabla por mapa (todos y los 4) con chance, rango y partidas, `MatchupDuration` (listas de mapa y Elo; un gráfico SVG con una columna por franja de duración: el punto en la chance, la barra en el rango, la línea del 50 % y las partidas debajo; "parejo" si toca el 50 %) y la frase de duración; al final "Cómo se calcula" (modelo con el Elo descontado, frenos, rango del 90 %, regla de honestidad, en 4 o 5 renglones) y la fuente, las fechas y el parche.
- `generateStaticParams` de `[slug]`: las 53 civs y las 1378 parejas canónicas. `generateMetadata`: título y descripción por página ("Britanos contra francos: estadísticas del cruce · AoE2"), canónica, y `robots` noindex en `/cruces` con `?a=`.

- [ ] **Step 1: Tests** de `matchupView.ts`: `rivales con sus 30 casilleros`, `el resumen del cruce desde a`, `frase de duración con la regla de honestidad` (los tres casos), `textos de un mapa sin partidas`.
- [ ] **Step 2: Ver que fallan.**
- [ ] **Step 3: Implementar** `matchupView.ts`, `matchupsServer.ts`, las páginas y los dos componentes.
- [ ] **Step 4: Ver que pasan,** y `npm run lint` y `npm run typecheck`.
- [ ] **Step 5: Commit** `web: páginas de cruces de civs`.

### Task 3: Sitemap, menú, enlace desde cada BO y el archivo en el servidor

**Files:**
- Modify: `web/src/app/sitemap.ts` (las 53 + 1378 páginas, prioridad 0,5, `changeFrequency` "monthly"), `web/src/components/Header.tsx` (enlace "Cruces" después de "Biblioteca"), la página de la BO (`web/src/app/bo/[code]/page.tsx`: "Cómo le va a esta civ" → `/cruces/<civ>` si la civ tiene datos), `web/next.config.ts` (`outputFileTracingIncludes` de `/cruces/[slug]` y `/cruces` con `./public/coach/matchup_stats.json`, por si algo se renderiza en el servidor después del build)
- Test: `web/src/lib/matchupView.test.ts` (`las entradas del sitemap`: `matchupSitemapPaths(data)` da 53 + 1378 rutas, todas canónicas)

- [ ] **Step 1: Test** de `matchupSitemapPaths`.
- [ ] **Step 2: Ver que falla.**
- [ ] **Step 3: Implementar.**
- [ ] **Step 4: `npm test`, `npm run lint`, `npm run typecheck` y `npm run build`** pasan (el build prerenderiza las páginas).
- [ ] **Step 5: Commit** `web: los cruces en el sitemap, el menú y cada BO`.

### Task 4: Verificar en el navegador y documentos

- [ ] **Step 1:** `preview_start` con la configuración de la web en producción (`web-prod`, `next start` después del build): abrir `/cruces`, `/cruces/franks`, `/cruces/britons-vs-franks`, cambiar mapa y Elo, y sacar capturas; `/cruces/franks-vs-britons` redirige; `/cruces/spartans` y `/cruces/zzz-vs-franks` dan 404 (con `curl -s -o /dev/null -w "%{http_code}"`); el HTML de un cruce trae el título, la canónica y los números sin JavaScript (`curl` y buscar "Britanos"); a 375 px no hay desplazamiento horizontal de la página.
- [ ] **Step 2:** `PLAN.md` (D10: "(d) hecho"), `CLAUDE.md` y, si queda algo del usuario (mirar las páginas publicadas), `docs/pendientes-usuario.md`, en commits separados, y push de `fase-9-refinamiento` (Vercel arma la vista previa de la rama; producción sale de `main`, que pide el usuario).
