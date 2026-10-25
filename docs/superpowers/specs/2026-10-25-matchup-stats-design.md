# Diseño de las estadísticas reales de cruces: civ, rival, mapa, Elo y duración (2026-10-25)

## 1. Objetivo

Saber, con partidas clasificatorias reales, cómo le va a cada civ contra cada rival en los mapas principales
(Arabia, Arena, Bosque Negro y Nómada), según el Elo y según cuánto dura la partida. Es la "capa 1" de la evaluación
del 2026-10-25 sobre las condiciones de victoria por cruce: la parte barata y verdadera, que además sirve después para
validar los modelos matemáticos (economía por mapa, combate por costo y ventanas de tiempo). Responde preguntas como
"francos contra britanos en Arabia, con mi Elo: ¿quién gana más y en qué duración de partida?".

### Decisiones del usuario (2026-10-25)

1. Se muestra en los **cuatro lugares**: la pantalla de carga (overlay), la web (una página por cruce), la sugerencia
   de BO (puntaje) y el informe de la partida.
2. **1v1 y equipos aparte:** los cruces civ contra civ salen solo del 1v1; las partidas por equipos dan otra tabla con
   el rendimiento de cada civ por mapa, Elo y duración (sirve para Nómada, que casi solo se juega por equipos, y para
   las partidas 4 contra 4 del usuario).
3. **Tu Elo a mano y con un botón "Buscar"** que lo trae una vez de la API oficial con "Tu nombre".
4. **Fuente mixta (enfoque C):** la herramienta lee fuentes intercambiables que terminan en la misma tabla de
   partidas. Arranca con aoe2-guide; el recolector propio de la API oficial se suma después.
5. Aprobadas las cuatro partes del diseño (piezas, modelo, qué se ve, errores y pruebas) tal como quedan abajo.

Lo que se supone y el usuario no objetó: solo partidas clasificatorias de mapa aleatorio; la herramienta es Python
offline (como el extractor) y su resultado es un JSON que se commitea; se corre una vez por parche.

## 2. La fuente de datos (medido el 2026-10-25)

- **aoestats.io** (la fuente de referencia de la comunidad) no sirve hoy: el sitio da error 522 y un reporte abierto
  dice que sus volcados semanales están vacíos desde el 2026-02-08, sin respuesta del dueño.
- **aoe2-guide.vercel.app/data** publica `matches.parquet` (8 MB), `players.parquet` (22 MB) y `ranked_data.json`
  (224 KB, totales ya calculados). No dice quién lo mantiene: por eso la fuente tiene que poder cambiarse. Los datos
  salen de la API oficial (`aoe-api.worldsedgelink.com`).
  - Los `.parquet` bajados cubren **solo 5 semanas** (partidas del 2026-06-01 al 2026-07-03): 362.052 partidas 1v1 y
    170.868 por equipos, todas del mismo parche. Columnas de la partida: `match_id`, `ladder` (`1v1` o `team`),
    `matchtype_id`, `map`, `num_players`, `start_time`, `completion_time`, `duration_min`, `avg_elo`, `week_end`,
    `patch`. Del jugador: `match_id`, `profile_id`, `civ_id`, `civ`, `team`, `won`, `old_rating`, `new_rating`,
    `rating_change`, `career_wins`, `career_losses`, `streak`. Ningún jugador sin Elo ni sin civ.
  - El JSON de totales abarca el parche entero ("Update 185872", desde el 2026-06-02, 18 semanas, 1,53 millones de
    partidas 1v1), pero no cruza todo: civ contra civ solo en dos franjas de Elo y sin mapa; por mapa, solo la civ.
  - `duration_min` es **tiempo real** (`completion_time - start_time` en minutos, verificado), no el del reloj del
    juego. Mediana 23,8 minutos reales.
  - 53 civs. `mayans` es `mayan` en game_data; las 9 civs de game_data que no aparecen (las de Crónicas y las más
    nuevas: achaemenids, athenians, spartans, macedonians, thracians, puru, danes, saxons, varangians) quedan "sin
    datos".
- **Muestra en esas 5 semanas** (1v1 sin espejos, 349.626 partidas, 1.378 parejas de civs):
  - Partidas por pareja: en todos los mapas, mediana 186 (mínimo 18); en Arabia, mediana 36 (p10 13, p90 107); en
    Arena, 14; en Bosque Negro, 11. Con el parche entero serían unas 4 veces más.
  - Nómada **no aparece en 1v1** en esas semanas (10.496 partidas por equipos): depende de la rotación de mapas.
  - Casillero completo (pareja, mapa, 2 franjas de Elo, 5 de duración) en Arabia: mediana de **4 partidas**. Crudo no
    sirve: hace falta un modelo que sume efectos (sección 4).
  - Por civ y mapa sí alcanza: cada civ tiene como mínimo 711 partidas en Arabia, 255 en Arena y 205 en Bosque Negro.
  - Elo medio de la partida: p10 624, p25 843, mediana 1051, p75 1287, p90 1568. Diferencia de Elo entre los dos
    jugadores: mediana 24, p90 67 (el emparejamiento es parejo, pero igual se descuenta).
  - 12.426 espejos (misma civ) que no cuentan para los cruces.
- **Recolector propio (más adelante):** la API oficial tiene `getLeaderBoard2` (tabla de posiciones, de ahí salen los
  jugadores) y `getRecentMatchHistory` (últimas partidas de una lista de `profile_ids`, con civ, mapa, horarios,
  resultado y Elo previo y nuevo). No está documentada y puede cambiar: pedir despacio (una por segundo como mucho),
  esperar más ante 429 o 5xx y mandar un User-Agent que diga quiénes somos.

## 3. Piezas y recorrido de los datos

```
fuentes (aoe2-guide hoy, API oficial después)
   → tools/stats  (Python offline)
        tabla de partidas acumulada (caché local, fuera de git)
        → modelo → data/matchup_stats.json  (va al repo)
   → Core: Data/MatchupStats (lee el JSON y responde consultas)
        → app: pantalla de carga, sugerencia de BO, informe
        → web: páginas por cruce (build-coach lo copia a public/coach, como donaciones.json)
```

### 3.1 `tools/stats/`

Venv propio en `tools/stats/.venv` (ignorado por git) con `duckdb` (leer `.parquet` y cruzar tablas), `numpy` y
`scipy` (el modelo), en `requirements.txt`. Comandos de `stats.py`:

- `bajar --fuente aoe2guide`: baja los `.parquet` y los suma a la caché sin repetir partidas (`match_id`).
- `recolectar` (sub-paso e): el recolector propio de la API oficial, con la misma tabla de salida.
- `ajustar`: arma el modelo, lo valida y, si pasa, escribe `data/matchup_stats.json`. Si no pasa, no lo pisa y
  termina con error.
- `informe`: muestra la validación y cuántas partidas hay en cada casillero.

**Tabla de partidas** (igual para todas las fuentes): partida (`id`, `ladder`, `mapa` con la clave de `data/maps.json`
o el nombre de la fuente si no está, `fecha`, `duracion_real_s`, `parche`) y jugadores (`id_partida`, `civ` con la
clave de game_data, `equipo`, `elo_previo`, `gano`). Caché en `tools/stats/.cache/partidas.parquet` (ignorada por
git). Cada fuente tiene su lector, que traduce civs y mapas con una tabla (`fuentes/<fuente>.py`).

### 3.2 `data/matchup_stats.json`

Extracto (cada arreglo sigue con el resto de sus casilleros):

```json
{
  "schema_version": 1,
  "generated": "2026-10-25",
  "source": { "name": "aoe2-guide", "url": "https://aoe2-guide.vercel.app/data", "from": "2026-06-01", "to": "2026-07-03" },
  "patch": { "label": "Update 185872", "start": "2026-06-02", "previous_mixed": false },
  "matches": { "1v1": 349626, "team": 170868 },
  "bands": { "elo": [850, 1050, 1300, 1600], "game_minutes": [20, 30, 40, 55], "maps": ["arabia", "arena", "black_forest", "nomad"] },
  "interval": 0.9,
  "one_vs_one": { "britons|franks": [512, 480, 543, 1830, "..."] },
  "team": { "franks": [507, 497, 517, 9120, "..."] }
}
```

- `one_vs_one`: una entrada por pareja con las claves de game_data en orden alfabético, separadas por `|`. Cada
  entrada es un arreglo plano de casilleros `[p, desde, hasta, n]`: `p` es la chance de que gane la **primera** civ
  de la clave con el mismo Elo que su rival, `desde` y `hasta` el rango probable (sección 4.4), los tres en
  milésimas, y `n` las partidas reales de ese casillero. Orden: mapa (todos, arabia, arena, black_forest, nomad) ×
  franja de Elo (todas y las 5) × franja de duración (todas y las 5) = 180 casilleros por pareja. La otra dirección
  es `1000 - p` con el rango invertido.
- `team`: una entrada por civ con los mismos 180 casilleros; `p` es la chance de que gane un equipo con esa civ
  contra el resto, a Elo parejo.
- Tamaño estimado: unos 4 MB. La app lo lee recién en la pantalla de carga y se queda solo con lo de la civ del
  jugador, para no sumar RAM todo el tiempo.
- Un campo que no se conoce se ignora (como en los demás archivos de `data/`); `schema_version` sube solo si cambia
  el significado de algo que ya está.

### 3.3 Core

- `Data/MatchupStats`: lee el JSON (tolerante como `Maps` y `CivStyles`: lo que no se entiende se ignora y un error
  de sintaxis dice el archivo), `EloBand(int elo)`, `DurationBand(TimeSpan juego)`, `OneVsOne(civ, rival, mapa,
  franjaElo, franjaDuracion)` y `Team(civ, mapa, franjaElo, franjaDuracion)`, que devuelven `MatchupCell(P, Desde,
  Hasta, N)` o null. Un mapa que no está en las bandas usa "todos"; una civ sin datos da null.
- `Engine/MatchupText`: los textos (renglón de la carga, sección del informe, porqué de la sugerencia), con la regla
  de honestidad (sección 4.5). Lo usan la app y ReplaySession, como `CoachText`.

## 4. El modelo

### 4.1 Qué entra

Partidas clasificatorias del parche actual. Para los cruces, 1v1 sin espejos; para equipos, las de `team`. Se
descartan las de menos de 5 minutos de juego (abandonos y desconexiones). Si el parche lleva menos de 4 semanas, se
suma el anterior y el JSON lo dice (`previous_mixed`); la app lo aclara.

### 4.2 Franjas

- **Duración, en tiempo de juego:** minutos reales × 1,7 (velocidad normal de DE). La conversión se verifica con una
  partida grabada del usuario contra su historial de la API (último reloj de la grabación contra
  `completion_time - start_time`); si la pantalla de carga entra en la duración, se le resta. Franjas: menos de 20,
  de 20 a 30, de 30 a 40, de 40 a 55 y más de 55 minutos del reloj del juego.
- **Elo:** menos de 850, de 850 a 1050, de 1050 a 1300, de 1300 a 1600 y más de 1600 (salen de los cuartiles de la
  muestra, con una franja alta aparte). En equipos se usa el Elo de equipos del jugador.

### 4.3 Cómo se calcula

Regresión logística con penalización (equivale a un modelo bayesiano con previas normales, ajustado al máximo):

- Cada partida 1v1 se orienta con la civ A primera en orden alfabético; la salida es si gana A.
- **Elo primero:** término fijo `(EloA - EloB) · ln(10) / 400`, la fórmula del Elo. Lo que queda es de la civ.
- **Piezas de cada civ** (entran como +A y -B): fuerza general, ajuste por mapa (los 4 y "otros"), por franja de Elo
  y por franja de duración.
- **Piezas de cada cruce:** el cruce en sí, ajuste por mapa, por duración y por Elo.
- **Un freno por grupo** (la desviación de su previa): un ajuste con pocas partidas queda cerca de cero, así Bosque
  Negro o Nómada se apoyan en lo que se sabe de todos los mapas y un casillero de 4 partidas no inventa nada. El
  tamaño de cada freno se elige prediciendo las últimas 2 semanas con el resto (menor pérdida logarítmica).
- Unos 16 mil parámetros sobre una matriz rala; `scipy.optimize` (L-BFGS) ajusta en minutos.
- **Equipos:** el mismo modelo con cada civ contra el resto (el promedio de las civs del otro equipo), sin piezas de
  cruce.

### 4.4 Rango probable

Se repite el ajuste 20 veces con partidas sorteadas con reposición (con los frenos ya elegidos); el rango es donde cae
el 90 % de los resultados de cada casillero.

### 4.5 Regla de honestidad

- Un cruce es **parejo** si su rango incluye el 50 %.
- "Te conviene que dure poco / mucho" se dice solo si la diferencia entre la franja más corta y la más larga con
  datos supera el ruido: los rangos de las dos no se tocan. Si no, no se dice nada sobre la duración.
- Lo que se dice de la duración es lo que pasó, no una receta: "en las partidas que terminan antes de los 20 minutos
  gana el X %". La duración es un resultado de la partida, no una decisión.

### 4.6 Validación (criterio de salida del modelo)

- Sobre las 2 semanas apartadas predice mejor (pérdida logarítmica) que "solo el Elo" y que el % crudo de cada
  casillero.
- Calibrado: por deciles de lo predicho, lo observado queda a 2 puntos o menos (cuando dice 55 %, pasa entre 53 y 57).
- Con datos inventados de fuerzas conocidas, las recupera dentro de su rango en el 85 % de los casos o más.

## 5. Qué se ve en cada lugar

### 5.1 Ventana de la app

En la segunda fila, junto a "Tu nombre": "Elo 1v1" y "Elo equipos" (números, vacíos por defecto) y el botón
"Buscar". Buscar usa "Tu nombre" contra la API oficial (una consulta, solo por https, 10 s de espera máxima); si hay
varios jugadores con ese nombre, una lista con nombre, país y Elo para elegir. Queda en `settings.json`
(`elo_1v1`, `elo_team`, `profile_id`). Sin Elo, el coach usa "todas las franjas" y lo aclara ("sin tu Elo").

### 5.2 Pantalla de carga (overlay)

Renglón nuevo, junto al consejo de hoy ("Contra <civ> (enfrente): ..."), con la misma vida que él (la carga y el
primer minuto). Con 2 jugadores es 1v1 (Elo 1v1); con más, equipos (Elo de equipos).

- 1v1: "Francos contra britanos en Arabia (Elo 1050): ganás 47 % (44 a 50). Mejor si dura poco: 53 % antes de los
  20 min, 42 % después de los 40."
- Sin diferencia clara por duración: "... ganás 47 % (44 a 50), parejo en cualquier duración."
- Mapa fuera de los cuatro o sin leer: "... en todos los mapas ...".
- Civ sin datos: "Sin datos de partidas clasificatorias de <civ>."
- Equipos: "Francos en Nómada por equipos (Elo 1200): 52 % (50 a 54), mejor en partidas largas."
- Parche anterior sumado: se agrega "(con datos del parche anterior)".

### 5.3 Sugerencia de BO

`data/civ_styles.json` suma `windows`, para que lo revise el usuario: la franja de duración típica de cada estilo.
Arqueros, exploradores y hombres de armas: menos de 20 y de 20 a 30; fast castle: de 30 a 40 y de 40 a 55.
`BuildOrderSuggester` suma entre -2 y +2 según cuánto mejor se gana el cruce (con el rival de enfrente) en la ventana
del estilo de la BO que en general: `(p_ventana - p_total) · 20`, recortado, y solo si pasa la regla de honestidad
(si no, 0). El porqué de la sugerencia lo dice ("el cruce se gana más en partidas cortas"). Sin el JSON, la
sugerencia queda como hoy.

### 5.4 Informe de la partida

Sección "El cruce": el % general del cruce en ese mapa con tu Elo y el de la franja de duración que tuvo la partida
("Duró 34 min: a esa duración, en este cruce ganás el 45 %"). El informe no sabe si ganaste: es contexto, no un
veredicto. En equipos, el rendimiento de tu civ en ese mapa.

### 5.5 Web

- `/cruces`: elegís dos civs (y un enlace a cada civ).
- `/cruces/<civ>`: sus rivales ordenados por %, con el mapa y la franja de Elo para elegir, y cómo le va por equipos
  en cada mapa.
- `/cruces/<a>-vs-<b>`: el general, por mapa, un gráfico por duración con selector de Elo y cuántas partidas hay
  detrás de cada número; fuente, fechas, parche y "cómo se calcula". La clave va en orden alfabético
  (`britons-vs-franks`); la otra dirección redirige a esa.
- Sale en el HTML desde el servidor (sin el Core en WebAssembly), con los nombres en castellano (`civNames.ts`);
  `/cruces/<civ>` y los cruces entran al sitemap (53 + 1.378 páginas). Cada página de BO enlaza a
  `/cruces/<su civ>` ("Cómo le va a esta civ").

## 6. Errores

- Fuente caída o con otro formato: `bajar` lo dice claro y la caché conserva lo que había.
- Validación que no pasa: `ajustar` no pisa el JSON y termina con error. Nunca se commitea un modelo peor.
- Una civ o un mapa de la fuente sin traducción: un test exige que cada civ de los datos tenga su clave de game_data o
  figure en la lista de ignoradas.
- App sin el JSON o con un JSON roto: no hay renglón, ni puntaje extra, ni sección en el informe, y nunca un cartel de
  error. "Buscar" sin conexión o sin resultados lo dice al lado del botón.
- Web sin datos de un cruce o de una civ: la página lo dice.

## 7. Pruebas

- **Python (pytest en `tools/stats/tests`):** datos inventados con fuerzas conocidas que el modelo recupera (4.6);
  conversión de duración y franjas; cada lector de fuente con `.parquet` chicos armados en el test; la tabla de
  traducción de civs. Un trabajo nuevo del CI (`ubuntu-24.04`, acciones fijadas por SHA) corre solo esto, sin bajar
  nada.
- **Core:** lectura del JSON (también roto o con campos nuevos), franjas, consultas en las dos direcciones, textos con
  la regla de honestidad, con un `matchup_stats.json` de prueba chico.
- **App:** campos de Elo y su guardado, renglón del overlay con `tests/fixtures/screens/carga-ia-en-1080p.png`
  (equipos) y un caso 1v1 armado, sección del informe, puntaje de la sugerencia con y sin el JSON.
- **Web:** páginas desde el JSON de prueba, la redirección de la otra dirección, el sitemap y los nombres de las civs
  contra game_data.

## 8. Orden de trabajo

- **(a)** `tools/stats` con la fuente aoe2-guide, la caché, el modelo, la validación y el primer
  `data/matchup_stats.json`.
- **(b)** Core: `MatchupStats` y `MatchupText`.
- **(c)** App: Elo con "Buscar", renglón de la carga, puntaje de la sugerencia e informe.
- **(d)** Web: `/cruces`.
- **(e)** Recolector propio de la API oficial, cuando haga falta (el parche entero, Nómada en 1v1, o si aoe2-guide
  deja de publicar). Dónde corre (la PC del usuario o GitHub Actions programado) se decide en ese momento.

Cada sub-paso tiene su propio plan de implementación y se hace de a uno, empezando por (a): sin el JSON validado, lo
demás no tiene qué mostrar.

## 9. Lo que hace el usuario

- Verificar la conversión de duración con una partida suya (alcanza con usar "Buscar" con su nombre después de una
  partida grabada).
- Revisar `windows` en `data/civ_styles.json`.
- Mirar el renglón en una pantalla de carga real (1v1 y por equipos).

## 10. Criterio de salida de D10

El modelo pasa la validación de 4.6 con los datos de aoe2-guide; el renglón sale en la captura de la pantalla de
carga; la sugerencia usa la ventana del estilo (con un test); la web muestra las páginas de los cruces.

## 11. Riesgos

- **La fuente:** aoe2-guide puede dejar de publicar o cambiar el formato; por eso las fuentes son intercambiables y
  existe el sub-paso (e).
- **Muestra chica por casillero:** el modelo con frenos lo maneja, pero en Arena, Bosque Negro y Nómada casi todo lo
  específico del cruce va a quedar cerca de lo general. Es lo honesto: la regla de 4.5 evita decir de más.
- **Duración en tiempo real:** si la conversión ×1,7 no cierra con la partida del usuario, se ajusta antes de mostrar
  nada por duración.
- **Balance:** cada parche cambia las civs; con menos de 4 semanas de parche se mezcla el anterior y se avisa.
