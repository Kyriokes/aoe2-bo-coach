# La web en varios idiomas y el roadmap público

Fecha: 2026-11-02. Ítem 15 (5) de la hoja de ruta (`PLAN.md`, sección 11): el usuario pidió "poner la página en
varios idiomas y dar un roadmap a los usuarios para que vean que esta no es una versión definitiva y que con su ayuda
el potencial es enorme". La comunidad de AoE2 es mayormente de habla inglesa.

## Qué hay hoy

- La web (`web/`, Next.js 16 con App Router y Cache Components) está solo en castellano: `lang="es"` fijo, los textos
  escritos en cada componente (unos 60 archivos), los nombres de civ en `lib/civNames.ts` (copia de game_data
  `names.mx`), los textos de los pasos en `lib/stepText.ts` (copia en TypeScript de `StepText.Describe` del Core,
  atada por un test que los compara contra el Core en WebAssembly).
- Las URL en castellano (`/`, `/biblioteca`, `/bo/<código>`, `/subir`, `/cruces/...`, `/cuenta`, `/login`,
  `/privacidad`) están indexadas y las abre la app (`/cuenta` desde "Conectar cuenta", `/#apoyar` desde "Apoyar").
- Lo que viene del Core y la web muestra tal cual, en castellano: los avisos del validador (unos 45 mensajes), los del
  importador y el serializador (unos 145 literales), los porqués del modo asistido (unos 90), el texto de la postura,
  el nombre de cada señal de rama y la descripción de cada paso. Los bloqueos del Planner son códigos (`reason`,
  `missing`) y los traduce la web.

## Decisiones

### 1. Idiomas por segmento de URL, el castellano sin prefijo

Mecanismo: un segmento `[locale]` arriba de toda la app (`app/[locale]/layout.tsx` es el root layout, con
`generateStaticParams` = `es`, `en`). El castellano queda en las URL de hoy (`/biblioteca`) y el inglés en `/en/...`
(`/en/biblioteca`). El proxy (`src/proxy.ts`) hace el mapeo:

- `/en` y `/en/...`: pasan tal cual.
- `/es` y `/es/...`: redirección **308** a la misma ruta sin prefijo (la canónica del castellano es sin prefijo).
  Excepción: las imágenes generadas (`.../opengraph-image`, `.../twitter-image`) se sirven con prefijo, porque Next
  escribe `og:image` con la ruta del segmento (`/es/opengraph-image`).
- Lo demás: rewrite interno a `/es/...`. El navegador sigue viendo `/biblioteca`. Quedan afuera `api/`, `auth/`,
  `coach/`, `_next/`, `_vercel/`, `idioma/` y cualquier ruta con extensión (archivos de `public/`, `sitemap.xml`,
  `robots.txt`).

Por qué el segmento y no una cookie con `Accept-Language`:

- Con Cache Components, leer una cookie o una cabecera vuelve dinámico lo que la lee: el armazón de cada página (con
  la cabecera y el pie traducidos) dejaría de prerenderizarse. Con el segmento, cada idioma es una página estática.
- Los buscadores necesitan una URL por idioma para indexar las dos versiones y relacionarlas con `hreflang`; una
  misma URL que cambia de idioma según el visitante no se indexa en los dos.
- Google desaconseja redirigir por `Accept-Language` (su robot no manda preferencia y vería siempre una sola versión).

Lo que sí hay de preferencia del visitante:

- Cookie `idioma` (un año, `SameSite=Lax`), que escribe el selector de idioma a través de `GET /idioma/<locale>?volver=<ruta>`
  (ruta interna solamente) y redirige con 307. Con la cookie en `en`, una ruta sin prefijo redirige con **307**
  (nunca 308: depende de la cookie) a `/en/...`: así, si la app abre `/cuenta`, quien eligió inglés cae en
  `/en/cuenta`. Una ruta con `/en` o la cookie en `es` no redirigen nunca.
- En la portada en castellano, un aviso chico del lado del cliente ("This site is also in English") si el navegador
  prefiere inglés (`navigator.language`) y no hay cookie. No toca el HTML del servidor.

Páginas 404: con el root layout en `[locale]`, una URL que no calza con ninguna ruta no pasa por `not-found.tsx` del
segmento (`global-not-found` sigue experimental en Next 16). Se agrega `app/[locale]/[...rest]/page.tsx`, que llama a
`notFound()`: así todo 404 sale con la cabecera, el pie y el idioma de la URL. Los 404 y 308 de `/cruces` que
decide el proxy pasan a `/<locale>/cruces/...`.

### 2. Diccionarios en un solo lugar

`web/src/i18n/`:

- `locales.ts`: `Locale` (`"es" | "en"`), `LOCALES`, `DEFAULT_LOCALE` (`es`), `isLocale`, `localePath(locale, ruta)`
  (`/biblioteca` o `/en/biblioteca`), `stripLocale(ruta)`, `GAME_LANGUAGE` (qué idioma del juego usa cada uno: `es`
  → `mx`, `en` → `en`), `LANG_TAG` (`es-AR`, `en-US`, para fechas y orden), `OG_LOCALE` (`es_AR`, `en_US`).
- `es.ts`: el diccionario en castellano, fuente de verdad; `Dictionary = typeof es`.
- `en.ts`: tipado como `Dictionary`: si falta una clave, no compila. Las hojas son cadenas con `{marcadores}`
  (`fill(texto, { n: 3 })`); nada de funciones, así el diccionario es datos y se puede pasar al cliente. Los plurales
  son claves explícitas (`one`, `other`).
- `index.ts`: `getDictionary(locale)` (sincrónica; los dos diccionarios se importan estáticamente, también en el
  cliente: unos 20 KB comprimidos los dos; si se suman más idiomas, se parte por importación dinámica).
- `server.ts`: `currentLocale()` con `next/root-params` (`locale`), para cualquier componente de servidor sin pasar
  props.
- `client.tsx`: `I18nProvider` (lo pone el layout) y `useI18n()` → `{ locale, dict, href }` para los componentes de
  cliente.

Reglas: ningún texto que vea el visitante va escrito en un componente; las funciones puras de `lib/` que arman texto
(`share.ts`, `matchups.ts`, `matchupView.ts`, `filters.ts`, `dbErrors.ts`, `editor/text.ts`, `editor/explain.ts`,
`editor/guard.ts`, `editor/palette.ts`, `editor/villagers.ts`, `editor/assisted.ts`, `coach/stalls.ts`) reciben
`locale` y leen el diccionario. Las fechas y los números usan `LANG_TAG`; el orden alfabético de civs también.

Para agregar un idioma: sumar el código a `LOCALES` (y a `GAME_LANGUAGE`, `LANG_TAG`, `OG_LOCALE`), un `xx.ts` tipado
como `Dictionary`, la columna en `civNames.ts` (generada de game_data), el idioma en `StepText` del Core y en
`lib/stepText.ts`, y `content/roadmap.xx.json`. Nada más: las rutas, el sitemap, los `hreflang` y el selector salen de
`LOCALES`.

### 3. Nombres de civ por idioma

`lib/civNames.ts` pasa a `CIV_NAMES: Record<Locale, Record<string, string>>` con `es` = game_data `names.mx` y `en`
= `names.en` (62 civs cada una), y `civName(locale, clave)`. El test contra game_data compara cada idioma con su
columna. `civLabel(civ, locale)` ("Cualquier civ" / "Any civ").

### 4. Los pasos en inglés

Core (C#): `StepText` toma un idioma (`"es"` por defecto, `"en"`): `Describe(step, language)`, `Name(key, count,
language)`, `Source`, `Action`, `HasName(key, language)`. Un diccionario de nombres por idioma; lo que no tiene
nombre sale como la clave con espacios, en los dos idiomas. Formas en inglés: "6 villagers on sheep", "Build 2 houses
(3 builders) → sheep", "Research Feudal Age at 22 pop", "Move 3 from wood to gold", "Send 2 idle villagers to
berries". `BuildOrderAnalysis.Analyze(..., language)` describe los pasos, las ramas y la postura en ese idioma
(`Branch.SignalText(clave, language)`); el Wasm exporta `Analyze(json, language)`. La app y ReplaySession siguen en
castellano (el valor por defecto). El test del Core que exige nombre para toda entidad de game_data pasa a exigirlo en
los dos idiomas.

Web: `lib/stepText.ts` con `describeStep(step, locale)`, `entityName(key, count, locale)`, `sourceName(source,
locale)` y las tablas por idioma; `hasEntityName(key, locale)`. El test de `coach.test.ts` compara los dos idiomas
contra el Core (todas las entidades, todas las formas, las 129 BO) y además exige que cada entidad de game_data tenga
nombre en los dos idiomas: una entidad sin nombre en inglés hace fallar el test, no sale en castellano.

### 5. Los mensajes del Core quedan en castellano (anotado el camino)

Son muchos (unos 280 literales entre validador, importador, serializador y modo asistido) y cambian seguido: no se
traducen ahora. La web en inglés los muestra tal cual con un aviso una sola vez por lista ("The simulator's messages
are in Spanish for now"). El camino, cuando toque: cada `ValidationIssue` (y los avisos del importador y los porqués
del asistido) lleva un código y sus argumentos (`Code`, `Args`) además del `Message`; el Core arma el `Message` con
un diccionario por idioma como el de `StepText` y `Analyze`/`Import` reciben el idioma; la web no traduce nada por
su cuenta. Los bloqueos del Planner ya son códigos y la web los traduce desde el diccionario.

### 6. SEO

- `<html lang>` por idioma; `metadata.alternates.languages` (`es`, `en`, `x-default` = castellano) y `canonical` en
  cada página con `localeAlternates(ruta)`; `openGraph.locale` por idioma; `inLanguage` del JSON-LD por idioma.
- Sitemap: cada URL en los dos idiomas, cada una con `alternates.languages`. Las reglas de noindex (búsquedas por
  texto, login, cuenta, privadas) no cambian.
- Las imágenes de vista previa (`opengraph-image`) se dibujan con el texto del idioma.
- CSP: sin cambios (no entra ningún origen nuevo). `robots.txt`: sin cambios.

### 7. Roadmap público

Página `/roadmap` y `/en/roadmap`, enlazada desde la portada (una sección corta "Qué viene" con el enlace) y desde el
pie. Escrita para jugadores. Secciones: "Qué hace hoy" (una frase por cosa), "Qué no hace todavía", "Qué viene" (de
`PLAN.md` ítems 14 y 15 en lenguaje de usuario: registro de partida para mejorar con las partidas de todos, cualquier
resolución, la app más amigable, la BO que se replanifica sola desde lo que pasa en la partida, los idiomas del
juego, la beta chica) y "Cómo ayudar" (jugar con el registro prendido cuando esté, mientras tanto grabar y mandar la
grabación, publicar BO, reportar en GitHub, traducir). Tono: no es la versión definitiva; con tu ayuda crece.

El contenido vive en `web/src/content/roadmap.es.json` y `roadmap.en.json`, un archivo por idioma con la forma
`{ intro, updated, sections: [{ id, title, intro?, items: [{ title, text }] }], closing }`; la página solo lo
dibuja. La primera clave de cada archivo (`_como_editar`) explica cómo editarlo. Metadata, JSON-LD (`WebPage`) y
sitemap como cualquier página.

### 8. Pruebas

- `i18n/i18n.test.ts`: los dos diccionarios tienen las mismas claves (lo garantiza el tipo) y los mismos marcadores
  en cada hoja; `localePath`/`stripLocale`; la decisión del proxy como función pura (`localeRouting(ruta, cookie)`:
  pasa, rewrite, 308, 307) con sus casos.
- `civNames.test.ts`: cada idioma contra game_data.
- `coach.test.ts`: los pasos en los dos idiomas y el nombre obligatorio en los dos.
- Los tests de `lib/editor/*` y `matchups` pasan `"es"` y siguen comparando los textos de siempre.
- Criterio de salida: las dos versiones renderizan todas las páginas (con `next start` y `curl`: `/`, `/en`,
  `/biblioteca`, `/en/biblioteca`, `/es/biblioteca` → 308, `/roadmap`, `/en/roadmap`, `/bo/<código>`, un 404 en los
  dos idiomas, `hreflang` en el HTML), `npm test`, `npm run lint`, `npm run build` y la suite de .NET en verde.

## Fuera de alcance

- La app de escritorio en inglés (ítem 15 (3)).
- Los mensajes del Core en inglés (sección 5).
- Traducir los nombres de los mapas del juego más allá de lo que trae `maps.json` y los de `/cruces` (`MAP_NAMES`
  pasa al diccionario).
