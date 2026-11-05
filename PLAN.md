# AoE2 BO Coach: Plan del proyecto

Overlay liviano para Age of Empires II: Definitive Edition que lee la pantalla en vivo (OCR), sigue una build order (BO) armada por el jugador, se adapta cuando el jugador se desvía y sugiere cómo redistribuir aldeanos para no acumular recursos que no necesita. Las BO se arman y se comparten en una web, y la app baja las que usa.

---

## 1. Objetivo y alcance

**Qué hace**
- Lee en vivo: reloj, comida, madera, oro, piedra, población y el log de eventos ("House Built", "Villager Created").
- Permite armar, editar y compartir BO en la web, con vista previa de tiempos estimados (el editor de la app, de la Fase 4, se fue en la v0.9.52).
- Sigue la BO elegida y muestra en un overlay el paso actual y el siguiente.
- Detecta desvíos (adelantos, atrasos, acciones fuera del plan) y replanifica.
- Sugiere redistribución de aldeanos entre recursos para cumplir el plan.
- Web con biblioteca pública de BO para compartir e importar.

**Qué NO hace (por diseño)**
- No lee memoria del proceso, no inyecta código, no envía inputs al juego. Solo captura pantalla y muestra información. Esto lo mantiene como herramienta de entrenamiento y no como bot.
- No soporta otros AoE en el MVP.

**Requisitos no funcionales**
- Liviano: objetivo menor al 3% de CPU y menos de 100 MB de RAM durante la partida.
- Sin instalaciones extra para el usuario final (nada de Python ni Tesseract).
- Funciona en modo ventana sin marco o pantalla completa no exclusiva.
- Sin dependencia de internet durante la partida.

---

## 2. Stack: C# en runtime, Python solo como herramienta offline

### Por qué C# para la app

| Aspecto | Python (PySide6 + OpenCV + Tesseract) | C# .NET 10 (WPF + APIs de Windows) |
|---|---|---|
| RAM en partida | 150 a 250 MB | 60 a 100 MB |
| Tamaño del ejecutable | 150 a 300 MB (PyInstaller) | 30 a 70 MB |
| Arranque | Lento (descomprime el bundle) | Rápido |
| OCR | Tesseract, instalación aparte | `Windows.Media.Ocr`, viene con Windows 10/11 |
| Captura | `mss` (GDI) | GDI para regiones chicas, `Windows.Graphics.Capture` si hace falta |
| Overlay transparente y click-through | Funciona con Qt | Nativo en WPF + Win32 |
| Hotkeys globales | `pynput` (hook de teclado) | `RegisterHotKey` nativo |
| Experiencia previa del dev | Media | Alta (.NET, WinForms) |

El trabajo de CPU es chico en ambos casos (leer unas pocas regiones dos veces por segundo), así que la diferencia real está en memoria, tamaño, arranque y dependencias. En todos esos puntos gana C#.

### Por qué Python sigue en el proyecto
- La librería que lee el `.dat` del juego (`genieutils-py`) es de Python y ya está probada con la instalación local.
- Se usa **una sola vez por parche** para exportar `game_data.json`. No corre durante la partida y el usuario final no lo necesita.

### Enfoque descartado
Mezclar Python y C# en runtime (por ejemplo, OCR en Python y overlay en C# comunicados por IPC) suma dos procesos y lo peor de cada stack. No conviene.

### Stack final

| Área | Elección |
|---|---|
| App de escritorio | .NET 10 (LTS, soporte hasta noviembre de 2028), WPF. Se pasó de .NET 8 porque pierde soporte en noviembre de 2026 |
| Captura | GDI `BitBlt` por región; `Windows.Graphics.Capture` como alternativa |
| OCR | `Windows.Media.Ocr`; plan B: template matching de dígitos propio |
| Hotkeys | `RegisterHotKey` (Win32) |
| Optimización (refinamiento) | Greedy propio; Google OR-Tools (.NET) si hace falta LP |
| Tests | xUnit |
| Extractor de datos | Python 3.12 + `genieutils-py` (herramienta offline) |
| Distribución | Publicación self-contained de .NET, un solo `.exe` con `data/` al lado (zip en las releases de GitHub) |
| Web | Next.js en Vercel + Supabase (Postgres, cuentas con Discord y Google); el `Core` compilado a WebAssembly para la línea de tiempo |

---

## 3. Arquitectura

```
          ┌──────────────┐
          │ Captura      │  cada 0,5 s, solo las regiones calibradas
          └──────┬───────┘
                 ▼
          ┌──────────────┐
          │  OCR + log   │  números y eventos crudos
          └──────┬───────┘
                 ▼
          ┌──────────────┐
          │  Suavizado   │  mediana, descarte de saltos imposibles
          └──────┬───────┘
                 ▼
          ┌──────────────┐      ┌────────────────┐
          │ Estado del   │◄─────│ game_data.json │ generado offline desde el .dat
          │ jugador      │      └────────────────┘
          └──────┬───────┘
                 ▼
          ┌──────────────┐      ┌────────────────┐
          │ Motor de BO  │◄─────│  build order   │ armada en el editor
          │ + replanif.  │      └────────────────┘
          └──────┬───────┘
                 ▼
          ┌──────────────┐
          │ Economía     │  tasas, déficits, asignación de aldeanos
          └──────┬───────┘
                 ▼
          ┌──────────────┐
          │ Overlay      │  paso actual, siguiente, sugerencia de aldeanos
          └──────────────┘
```

Regla clave: **el motor y la economía (`BoCoach.Core`) no saben nada de pantallas, OCR ni WPF.** Reciben un `GameSnapshot` y devuelven recomendaciones. Así se testean con datos simulados sin abrir el juego, y el editor de la web reutiliza el mismo planner, compilado a WebAssembly, para la vista previa.

---

## 4. Estructura del repo

```
aoe2-bo-coach/
├─ README.md                  # para el jugador
├─ CONTRIBUTING.md            # compilar, probar, herramientas
├─ PLAN.md                    # este archivo
├─ CLAUDE.md                  # reglas de trabajo (corto); las notas por tema están en docs/
├─ LICENSE                    # MIT para el código; ver la excepción de los datos del juego en el README
├─ BoCoach.slnx
├─ global.json                # fija el SDK de .NET 10
├─ .github/                   # CI (build, tests, web, stats), release, Dependabot
├─ data/                      # lo que la app usa en partida; va al lado del .exe
│  ├─ game_data/              # generado por el extractor, versionado por parche
│  ├─ events.json, events.es.json, events.mx.json   # frases del log → id de evento
│  ├─ fonts/, icons/          # letras del HUD e íconos de la cola
│  ├─ hud_layout.json, hud_layout_minimo.json       # los dos diseños del mod
│  └─ maps.json, civ_styles.json, matchup_stats.json, donaciones.json
├─ build_orders/              # 3 ejemplos y civs/ (una BO por civ, importada y mejorada)
├─ src/
│  ├─ BoCoach.Core/           # net10.0, lógica pura, sin dependencias de Windows
│  │  ├─ Data/                # game_data y bonificaciones de civ
│  │  ├─ BuildOrders/         # modelo, validación, serialización, importadores, sugerencia
│  │  ├─ Engine/              # Coach, Tracker, Planner, LivePlan, creencia (Belief/), postura, señales, informe
│  │  ├─ Economy/             # tasas, demanda, reparto, guía dinámica
│  │  ├─ Assisted/            # modo asistido: objetivos que arman la BO
│  │  ├─ Record/              # registro de partida y su envío
│  │  └─ Web/                 # cliente de la web y caché de BO
│  ├─ BoCoach.Vision/         # net10.0-windows10.0.19041.0: captura, letras, OCR, HUD, log, cola, carga, calibración, grabación
│  ├─ BoCoach.App/            # WPF, net10.0-windows10.0.19041.0
│  │  ├─ Coaching/            # el tablero, el loop en vivo, los ajustes, el informe
│  │  ├─ Overlay/             # guía, GD, recordatorios
│  │  ├─ Capture/, Calibration/, FirstRun/, Hotkeys/, Web/, Themes/
│  └─ BoCoach.Wasm/           # el Core en browser-wasm para la web (JSExport: Init, Analyze, Import)
├─ tests/
│  ├─ BoCoach.Core.Tests/, BoCoach.Vision.Tests/, BoCoach.App.Tests/
│  └─ fixtures/               # golden, capturas y guías de terceros
├─ tools/
│  ├─ extractor/              # Python: .dat → game_data, eventos e íconos
│  ├─ stats/                  # Python: estadísticas de cruces
│  ├─ mod/                    # respaldo del instalador del mod (la app lo instala sola)
│  └─ ReplaySession/          # corre el pipeline sobre grabaciones y registros
├─ web/                       # biblioteca y editor de BO: Next.js + Supabase (ver web/README.md)
└─ docs/                      # índice en docs/README.md
```

---

## 5. Formatos de datos

### 5.1 `game_data/<version>.json` (generado)

```json
{
  "dat_version": "VER 8.9",
  "entities": {
    "villager":   { "kind": "unit",     "dat_id": 83,  "cost": { "food": 50 }, "time": 25, "made_in": "town_center" },
    "house":      { "kind": "building", "dat_id": 70,  "cost": { "wood": 25 }, "time": 25, "pop_bonus": 5 },
    "barracks":   { "kind": "building", "dat_id": 12,  "cost": { "wood": 175 }, "time": 50 },
    "loom":       { "kind": "tech",     "dat_id": 22,  "cost": { "gold": 50 }, "time": 25, "made_in": "town_center" },
    "feudal_age": { "kind": "tech",     "dat_id": 101, "cost": { "food": 500 }, "time": 130, "made_in": "town_center" }
  },
  "civ_overrides": {
    "britons": { "...": "solo lo que difiere del base" }
  },
  "gather_rates": { "sheep": 0.33, "berries": 0.31, "wood": 0.39, "gold": 0.38, "stone": 0.36, "farm": 0.32 }
}
```

- Se mapea por **ID**, nunca por nombre interno. Los nombres del `.dat` están desfasados (el tech 101 se llama "Middle Age" y es el paso a Feudal).
- Las `gather_rates` del `.dat` son el valor inicial; durante la partida se ajustan con lo observado.

### 5.2 Build order (`*.json`)

Es el mismo formato en la app, en los archivos y en la web.

```json
{
  "schema_version": 2,
  "id": "a1b2c3",
  "name": "Scouts Feudal (22 pop)",
  "civ": "any",
  "author": "...",
  "description": "...",
  "tags": ["feudal", "scouts", "arabia"],
  "steps": [
    { "id": "s1", "do": "train",    "what": "villager",    "count": 6, "assign": "sheep" },
    { "id": "s2", "do": "build",    "what": "house",       "count": 2, "assign": "wood", "confirm_by": "house_built" },
    { "id": "s3", "do": "train",    "what": "villager",    "count": 4, "assign": "wood" },
    { "id": "s4", "do": "build",    "what": "lumber_camp", "confirm_by": "lumber_camp_built" },
    { "id": "s5", "do": "research", "what": "loom",        "after": ["s4"] },
    { "id": "s6", "do": "research", "what": "feudal_age",  "at_pop": 22, "note": "Mientras sube: 2 casas y cuartel" }
  ],
  "targets": {
    "feudal_click_time": 540,
    "villagers_at_feudal": { "food": 14, "wood": 6, "gold": 0, "stone": 0 }
  }
}
```

- El costo y el tiempo **no se escriben**: salen de `game_data`.
- `assign` indica a qué recurso va el aldeano producido. En un paso de aldeanos, `count` es el **total** que queda en esa fuente al terminar el paso, contando a los 3 iniciales ("6 en ovejas" son 6, nunca 9). El `schema_version` 1 guardaba aldeanos nuevos y se migra al leer (`LivePlan.CountsToTotals`).
- `after`, `at_pop`, `at_time` son condiciones opcionales. `confirm_by` usa ids de `events.json`.
- `group` (opcional) arma un **paso mixto**: los pasos seguidos con el mismo grupo son un solo paso con varias metas que se hacen a la vez (por ejemplo, el clic en Feudal y el cuartel). Cada meta avanza por su cuenta, el paso termina cuando terminan todas, y una meta no deja "atrasada" ni da por hecha a otra del mismo grupo. En el editor: "Junto con el anterior".
- `map` (opcional): la clave de `data/maps.json` para la que está pensada; sin él, cualquier mapa.
- `posture` (opcional, D2): `queue` (unidades siempre en cola) y `next_age`; sin ella se deduce. `branches` (opcional, D3): pasos que se agregan con una señal de la partida. `assisted` (opcional, D12): los objetivos de los que salen los pasos. Ninguno sube `schema_version`: las versiones viejas ignoran lo que no conocen.
- Topes: 300 pasos, `count` y `builders` hasta 200, `after` hasta 50, `assisted.goals` hasta 40, `map` hasta 40 caracteres y 128 KB la BO entera. Están en el validador, en zod (`web/src/lib/buildOrder.ts`) y en la base, porque cualquiera le puede hablar directo a la base.
- `schema_version` permite migrar BO viejas cuando el formato cambie.

### 5.3 `regions.json` (generado por la calibración, en la carpeta de usuario)

```json
{
  "reference_resolution": [1920, 1080],
  "hud_scale": 1.0,
  "hud_layout": { "name": "minimo" },
  "regions": {
    "clock":      [0.46, 0.02, 0.06, 0.025],
    "food":       [0.01, 0.005, 0.04, 0.02],
    "population": [0.30, 0.005, 0.05, 0.02],
    "event_log":  [0.00, 0.10, 0.18, 0.08]
  }
}
```

Coordenadas relativas (x, y, ancho, alto como fracción de la pantalla). Con el mod, este archivo lo escribe la calibración sola al empezar el coach, con la escala medida del HUD (`hud_scale`) y el diseño (`hud_layout`); cada región se lleva a 1080p antes de leerla. La calibración a mano queda para el modo desarrollador.

### 5.4 `data/events.json`

```json
{
  "house_built":       ["House Built"],
  "villager_created":  ["Villager Created"],
  "lumber_camp_built": ["Lumber Camp Built"]
}
```

Lo genera el extractor con los textos del juego (594 eventos, también `events.es.json` y `events.mx.json`): ver `docs/dev/datos-del-juego.md`.

---

## 6. Módulos en detalle

### 6.1 Captura y calibración
- Se capturan solo los rectángulos configurados, no la pantalla completa.
- UI de calibración: ventana WPF transparente sobre el juego con un rectángulo semitransparente por región, con etiqueta. Se arrastran y estiran con el mouse. Debajo de cada uno se muestra en vivo lo que lee el OCR. Botón Guardar.

### 6.2 Visión
- **Números:** por plantillas (`data/fonts`, armadas de grabaciones reales): el OCR de Windows no lee ni el reloj ni los números chicos del HUD. Con el mod, cada campo se recorta en proporción desde el diseño.
- **Cola de producción y panel de puntajes:** íconos contra una biblioteca (`data/icons`) y OCR de texto; ver `docs/dev/vision-y-hud.md`.
- **Log:** máscara de texto blanco, OCR de texto libre, limpieza de guiones y coincidencia aproximada (distancia de Levenshtein) contra `events.json`. Un mensaje repetido no agrega renglón sino que mueve el viejo al final, así que dos edificios iguales seguidos pueden verse como uno: lo completan la cola, el tope de población y el puntaje (la creencia del estado, ítem 14).
- **Frecuencia:** 2 lecturas por segundo por defecto (`capture_interval_ms`, de 200 a 1000).
- **Suavizado:** mediana de las últimas 5 lecturas por valor; se descartan cambios imposibles (por ejemplo, más de +60 de un recurso en 0,5 s sin evento que lo explique).

### 6.3 Estado del jugador
`GameSnapshot` (lo leído en un instante) → `PlayerState` (lo acumulado):
- stock por recurso, población actual y máxima, tiempo de partida
- eventos confirmados (contadores por tipo)
- aldeanos por recurso: **los muestra el HUD** (el número chico debajo de cada ícono de recurso) y se leen directo; la estimación por tasas queda como respaldo si la lectura falla
- aldeanos inactivos (ícono rojo del HUD) y edad actual ("Dark Age", "Feudal Age"...), que confirma las subidas de edad que el log no muestra
- edad actual

### 6.4 Motor de build order
**Tracker** (dónde estoy):
- Estados por paso: `pending`, `ready` (hay recursos y se cumplen condiciones), `in_progress` (una investigación pagada), `done`, `skipped`. Desde el ítem 13 el overlay muestra una línea por cada cosa que va en paralelo (aldeanos, construir, investigar, producir) en vez de un solo paso actual.
- Un paso pasa a `done` con su `confirm_by` o cuando el conteo lo prueba.
- Si se confirma un paso posterior de la misma línea y uno anterior sigue pendiente, el anterior queda como "Quedó sin hacer" y la línea sigue; no se pierde.
- Acciones fuera del plan se registran como `extra` y afectan el presupuesto, no el puntero.

**Planner** (qué falta y cuándo):
- Simulación hacia adelante en pasos de 1 s con stock, tasas estimadas y cola del centro urbano. Devuelve el tiempo estimado de cada paso pendiente.
- Muestra el desvío contra los `targets` ("Feudal estimada en 9:40, objetivo 9:00").
- El jugador puede forzar avanzar o retroceder con hotkeys; el motor respeta la corrección manual.

### 6.5 Economía y balanceo

**Tasas observadas** en una ventana deslizante de 20 s:

```
tasa_r = (Δstock_r + gasto_r) / Δt
```

`gasto_r` sale de los eventos confirmados por los costos de `game_data`.

**Aldeanos estimados por recurso:**

```
aldeanos_r ≈ tasa_r / (rate_r × eficiencia)
```

La eficiencia del jugador se aprende durante la partida. Se ajusta para que la suma coincida con los aldeanos totales.

**Demanda:** costo acumulado de los pasos pendientes hasta el próximo hito, dividido por el tiempo que falta hasta su objetivo.

**Déficit proyectado** en un horizonte H (60 s por defecto):

```
déficit_r = demanda_r × H − (stock_r + tasa_r × H)
```

Positivo: va a faltar. Muy negativo: sobra, que es el problema típico del jugador que aprende.

**Asignación:**
- **v1 (greedy):** mover de a 1 aldeano desde el recurso con mayor excedente al de mayor déficit, con mínimos por recurso.
- **v2 (LP):** minimizar `Σ w_r · |déficit_r|` sujeto a `Σ aldeanos_r = N`, `aldeanos_r ≥ min_r` y un costo por reasignación (caminata, campamentos nuevos).
  - Hecho en la Fase 9 (`VillagerAllocator.Exact`, el de por defecto; el greedy sigue como `AllocatorKind.Greedy`). Costo: lo que falta por encima del umbral pesa 1 y lo que sobra por encima de su umbral pesa 0,2; mover cuesta 15 s de trabajo por aldeano y 40 más si van a un recurso sin nadie. El que cede tiene que seguir sobrando. Como el overlay muestra un solo par a la vez, se prueban las 12 combinaciones por hasta 5 aldeanos: es el óptimo exacto entre lo que se puede sugerir, sin solver.
  - Sobre la partida de scouts sugiere en los mismos momentos que el greedy, con la cantidad que alcanza para cubrir lo que falta (15 cambios contra 17).
- **Histéresis:** solo sugerir si el déficit se sostiene más de 10 s y no hubo sugerencia en los últimos 20 s.
- **Salida:** una frase simple, por ejemplo "Pasá 2 de madera a comida". Una sugerencia a la vez.

**Limitación honesta:** la tasa real depende del mapa y la distancia a los recursos, por eso se aprende de la partida. Las primeras versiones van a ser aproximadas.

### 6.6 Overlay
- Ventana WPF sin bordes, `Topmost`, con `WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE` para que sea click-through y no robe foco.
- Contenido: el renglón crítico arriba (la casa que falta, el clic de edad cuando ya se puede), una línea por cada cosa que va en paralelo, lo que está en curso, la sugerencia económica y el desvío contra el objetivo.
- Atajos (todos con Ctrl+Alt, que el juego no usa, y editables): la lista está en el README y en `src/BoCoach.App/Hotkeys/HotkeyAction.cs`.
- Posición: siempre arriba a la derecha de la ventana del juego, también en la pantalla de carga (desde la v0.9.43), y la sigue si se mueve. Con la BO terminada queda una tarjeta chica con lo que hay que hacer (D11).
- En curso: el log avisa cuando una investigación termina, no cuando se hace clic. El clic se reconoce porque el stock baja de golpe justo el costo.
- CPU: el OCR del log se pasa solo si hay texto y si cambió desde la captura anterior.
- Centro urbano parado: sale de la cola de producción (5 s sin nada suyo); si la cola no se lee, se deduce porque el total de aldeanos no sube en lo que tarda uno más 8 s.

### 6.7 Editor de build orders

Está en la web (`/subir`): paleta, línea de tiempo con arrastre y vista previa simulada con el Core en WebAssembly, más un modo asistido que arma la BO a partir de objetivos (ítems D8 y D12 de la hoja de ruta). El editor WPF de la Fase 4 y la biblioteca local se fueron en la v0.9.52 (recuperables con la etiqueta de git `editor-wpf`): la app guarda una caché de las BO que usa y las elige sola en la pantalla de carga.

### 6.8 Web y biblioteca
- Página pública con la biblioteca de BO: buscar por civ, mapa, estrategia y tags; ordenar por popularidad.
- Detalle de cada BO con la misma línea de tiempo del editor (el planner es el mismo, por WebAssembly).
- Cuentas de usuario para publicar, editar, votar y suscribirse. Las BO pueden ser públicas o privadas.
- Importar a la app: botón "Abrir en BO Coach" con un enlace `bocoach://import/<id>` (protocolo registrado por la app) y, como alternativa, código corto para pegar.
- La app no publica (salvo `BoCoach.exe --publish`, para el dueño): baja la lista de la cuenta y las BO que usa.
- API para la app: ver `web/README.md`.
- La web valida con el mismo `schema_version` que la app.
- Editor de BO en la web (`/subir`, D8): paleta de pasos y línea de tiempo arrastrable, simulada con el Core en WebAssembly; una caja no puede ir a donde el juego no la deja.

---

## 7. Fases

Cada fase termina con algo que se puede probar. Las fases 1 a 5 no necesitan el juego abierto.

| Fase | Entregable | Criterio de salida |
|---|---|---|
| **0. Setup** | Repo, solución .NET con los 3 proyectos, xUnit, CI con GitHub Actions | `dotnet test` en verde |
| **1. Datos del juego** | `tools/extractor` → `data/game_data/v8.9.json`; carga del JSON en `Core` | Valores clave coinciden con el juego (aldeano 50f/25s, casa 25w, Feudal 500f/130s); test automático sobre esos valores |
| **2. Modelo de BO** | Clases, serialización, validador, 2 o 3 BO de ejemplo | Una BO mal formada da un error claro; las de ejemplo validan |
| **3. Motor** | Tracker y Planner sobre snapshots simulados | Tests: partida perfecta, jugador atrasado, paso salteado, acción extra, corrección manual |
| **4. Editor de BO** | Editor WPF con biblioteca local y vista previa de tiempos. **Se fue en la v0.9.52: el editor pasó a la web** | Armar una BO de 20 pasos desde cero en menos de 5 minutos; la vista previa marca un bloqueo de población |
| **5. Economía v1** | Tasas, déficits y asignador greedy | Escenario simulado "sobra madera" sugiere mover aldeanos a comida; sin oscilaciones en 5 min simulados |
| **6. Captura + calibración** | UI de rectángulos, `regions.json`, `RecordSession` | Calibrar las 9 regiones en menos de 2 minutos; grabar una partida de prueba |
| **7. Visión** | OCR de números, OCR del log, suavizado | Sobre 2 partidas grabadas: más del 98% de lecturas correctas en comida, madera y población; más del 95% de eventos del log sin duplicados |
| **8. Overlay + integración** | Loop completo en vivo con overlay y hotkeys | Una partida completa siguiendo la BO, CPU menor al 3%, RAM menor a 100 MB, sin interferir con mouse ni teclado |
| **9. Refinamiento** | LP, bonificaciones por civ, oro y piedra, más eventos, instalador | Ejecutable que corre en otra PC sin instalar nada |
| **10. Web y biblioteca** | Sitio, API, cuentas, publicación e importación desde la app | Publicar una BO desde la app y abrirla en otra PC con el enlace |

Orden recomendado: 0 → 1 → 2 → 3, luego 4 y 5 en paralelo, después 6 → 7 → 8 → 9 → 10. El núcleo lógico y el editor quedan probados antes de pelear con el OCR, y con el editor ya se pueden armar las BO de prueba para todo lo que sigue.

**Estado:** las fases 0 a 10 están hechas desde el 2026-10-19 (v0.9.15). Criterio de salida sin verificar: Fase 10 (abrir una BO en otra PC con el enlace, con el zip de la release); el de la Fase 4 quedó sin objeto al irse ese editor. La Fase 7 se cumplió con el mod del HUD (`BoCoach HUD`) y sus letras, no con los números de color del juego; la Fase 8 midió 0,4 a 2 % de CPU y 50 a 75 MB; la Fase 9 publicó la versión portable como release de GitHub. Lo que falta está ordenado en la sección 11.

---

## 8. Estrategia de pruebas

- **Unit tests** de `Core` (motor, economía, validación de BO) con snapshots escritos a mano.
- **Grabación y replay:** `SessionRecorder` guarda las regiones capturadas con timestamp durante una partida real. `ReplaySession` corre todo el pipeline sobre la grabación, así se reproduce cualquier bug sin volver a jugar.
- **Golden files:** para cada grabación, un archivo con los valores correctos esperados; el test de visión compara contra eso.
- **Medición de recursos:** contador de CPU y RAM del proceso en el overlay de debug.

---

## 9. Riesgos y mitigaciones

| Riesgo | Impacto | Mitigación |
|---|---|---|
| `Windows.Media.Ocr` lee mal los dígitos chicos del HUD | Alto | Escalado x3 y umbral; si no alcanza, template matching de dígitos (fuente fija, 10 plantillas) |
| El log se superpone con chat u otros mensajes | Medio | Coincidencia aproximada contra frases conocidas; ignorar lo que no matchea |
| El log no tiene fondo propio: texto blanco sobre el mapa, poco contraste | Medio | Máscara de texto blanco antes del OCR (el mapa siempre tiene color) y brillo estirado para la pausa. "Suscribirse" a los eventos del juego no es opción: es un solo proceso y haría falta inyectar código o leer memoria |
| Eventos repetidos (dos casas seguidas) se cuentan como uno | Medio | Rastrear posición de cada línea entre capturas |
| Pantalla completa exclusiva impide la captura | Medio | Documentar el uso de ventana sin marco; probar `Windows.Graphics.Capture` |
| Estimación de aldeanos por recurso imprecisa | Bajo | El HUD muestra los aldeanos de cada recurso: se leen directo. Si la lectura falla, partir del plan y corregir con tasas observadas |
| El OCR general no lee la letra del reloj ni los números chicos | Medio | Template matching de dígitos con plantillas sacadas de una grabación real (plan B de la sección 6.2) |
| Un parche cambia el formato del `.dat` | Bajo | Versionar `game_data` por parche; actualizar `genieutils-py` |
| Sugerencias que oscilan | Medio | Histéresis y una sola sugerencia a la vez |
| La lógica del planner se duplica entre app y web | Medio | Resuelto: la web corre el mismo `Core` compilado a WebAssembly (`src/BoCoach.Wasm`) |

---

## 10. Decisiones abiertas

1. ~~Nombre del repo.~~ Resuelto: `aoe2-bo-coach`.
2. ~~Bonificaciones por civilización.~~ Resuelto en la Fase 9: el extractor las saca del `.dat` (por edad) y el Planner, la economía y el coach las aplican según la civ de la BO ("any" = valores base).
3. ~~Importador de formatos de BO de la comunidad.~~ Resuelto en la Fase 9: `RtsOverlayImporter` lee el exportable de buildorderguide.com (formato de RTS Overlay) y agrega lo que esas guías dan por sabido (casas, campamentos, granjas); en la web, "Abrir .json" y "Pegar JSON o guía"; en ReplaySession, `import`. Los tres ejemplos vienen de ahí.
6. ~~Leer la cola de producción de los edificios.~~ Resuelto en la Fase 9: la cola de arriba a la izquierda se ve siempre, sin seleccionar el edificio. `Vision/Hud/ProductionQueue` la lee con una biblioteca de íconos (`data/icons/production_queue.json`, armada con `ReplaySession icons`/`iconlib`/`teach`) y el coach saca de ahí si el centro urbano está parado (5 s de gracia), las investigaciones en curso aunque no se haya visto la baja del stock, y los edificios iguales que producen a la vez. A otras escalas se lee normalizando la captura (sección 11, B4).
4. ~~Stack de la web.~~ Resuelto: mismo repo (`web/`), Next.js + Supabase + Vercel, y el `Core` de C# compilado a WebAssembly para validar y simular en el navegador (una sola implementación del Planner). Login con Discord y Google.
5. ~~Licencia del repo.~~ Resuelto: repo público con licencia MIT.

---

## 11. Hoja de ruta

Lo vigente. Cada ítem tiene su criterio de salida, y cada cambio se hace con un test que falle primero y en su propio commit. El detalle completo de cada ítem (pedidos del usuario, decisiones, lo medido y lo aprendido) está en [`docs/historial/hoja-de-ruta-hecha.md`](docs/historial/hoja-de-ruta-hecha.md), con los mismos nombres (B1, D8, 13...). La versión pública de esta lista es [`/roadmap`](https://aoe2-bo-coach.vercel.app/roadmap) (`web/src/content/roadmap.<idioma>.json`). Lo que le toca hacer al usuario, paso a paso: [`docs/pendientes-usuario.md`](docs/pendientes-usuario.md).

### Lo que sigue, en orden

1. **Camino al público (ítem 15).** Las partidas del propio usuario son hoy la única fuente de errores nuevos y no alcanzan. Hecho: cortar las "mejoradas" (no se producen más; la sugerencia prefiere la guía original), registro de partida con su envío, cola y pantalla de carga a cualquier escala (sin probar en vivo), la app remodelada, la app y la web en castellano e inglés, el roadmap público y la preparación de la beta (v0.9.90: el mod mínimo por defecto, "Algo anduvo mal", `ReplaySession registros`). **Falta la beta chica:** 3 a 5 jugadores con el registro prendido, antes de cualquier lanzamiento. Criterio de salida: partidas de otros, reproducidas con `coach <registro>`, y lo que rompieron, arreglado.
2. **La BO por objetivos (ítem 14), escalones 3 y 4.** Escalones 1 (casas por ritmo, renglón crítico, v0.9.82) y 2 (creencia del estado, v0.9.83) hechos. **(3)** Replanificar desde el estado vivo hacia el próximo hito de la BO (colisiones con tiempo, planes B simulados): es la prueba que decide el formato, contra las 129 guías. **(4)** Solo si gana: schema 3 (`goals` + `tactics`), base, web y migración. Espera a tener partidas de otros.
3. **Robustez que falta (bloque B).**
   - **B4:** jugar de verdad a otra resolución y sacar un golden de esa grabación (lo que hay se probó con capturas reescaladas).
   - **B2:** ver la pantalla de carga en castellano y en multijugador, y verificar el mapa, que el overlay puede tapar.
   - **D3:** leer el aviso de ataque, que sale del color del atacante (hace falta una grabación con un ataque).
   - **D4:** la "mejor estrategia estándar" contra la combinación de civs aliadas y enemigas (hoy cuenta sobre todo el rival de enfrente).
   - **D7:** el bono de los cumanos (centro urbano en Feudal), que el validador no conoce.
   - **D12:** el editor manual de la web no sabe que los jemeres suben de edad sin edificios.
   - **Ítem 13:** el Planner sigue la BO en orden (los aldeanos en paralelo, lo demás no); hacer que construir e investigar también vayan en paralelo cambia los horarios de todas las BO, la web y el modo asistido, y queda para cuando el usuario lo pida.
4. **Mantenimiento (B6).** Cada partida nueva se revisa con la skill `revisar-grabacion`; cada parche del juego, extractor y `game_data` nuevo (el parche del 2026-10-27 trajo una civ nueva, "danes", y `game_data` sigue en v8.9).
5. **Estadísticas de cruces (D10), paso (e).** El recolector propio, cuando la fuente de hoy deje de alcanzar (`matchup_stats.json` termina el 3 de julio).
6. **Presentación (bloque C).**
   - **C1:** probar la release en otra PC. Tarea del usuario.
   - **C2:** capturas y video para la portada y el portfolio. Tarea del usuario.
   - **C5:** ejecutable de un solo archivo con `data/` adentro. El `.exe` ya sale en un solo archivo; `data/` va al lado a propósito (`ExcludeFromSingleFile`), y es lo último del bloque.
7. **Lo que dejó abierto la auditoría del 2026-11-05** (`docs/auditorias/2026-11-05.md`): avisar en la app que hay una versión nueva (IN-20, es un pedido de red más al abrir: decisión del usuario), y probar si la portable anda sin extraer DLL a `%TEMP%` (AP-32).

### Terminado

| Ítem | Qué | Cuándo |
|---|---|---|
| Fases 0 a 10 | Ver la sección 7 | Hasta 2026-10-19 |
| B1 | Tiempo relativo: los objetivos se cuentan desde que el jugador empieza a jugar | 2026-10-19 |
| B2 | La civ de la partida: desplegable y lectura de la pantalla de carga | 2026-10-19 y 20 |
| B3 | El "Settlement" de los incas cuenta como campamentos, molino y casa | 2026-10-19 |
| B4 | Calibración sola y capturas normalizadas a 1080p (falta probarla en vivo, arriba) | 2026-10-19 y 20; 2026-11-02 |
| B5 | Prueba de usabilidad del editor WPF: **quedó sin objeto**, porque ese editor se fue en la v0.9.52. La del editor web es el 8d de los pendientes viejos | |
| C3 | README y LEEME con la guía de primeros pasos | 2026-11 |
| C4 | Tutorial "Primeros pasos" | v0.9.25 |
| C6 | Apoyar el proyecto: enlaces y metas (`data/donaciones.json`, falta que el usuario complete sus usuarios) | 2026-10-22 |
| D1 | "Si no sabe, que se calle": BO abandonada y economía sana como piso | v0.9.20 |
| D2 | BO como postura de cierre con prioridades | v0.9.21 y v0.9.26 |
| D3 | Ramas disparadas por señales: clic del rival, ataques, aldeanos perdidos | v0.9.22 |
| D4 | Elegir la BO por civ, rival, mapa y posición | v0.9.23 y v0.9.27 |
| D5 | Informe de después de la partida | v0.9.24 |
| D6 | El coach solo: arranca con el juego y reconoce principio y fin de cada partida | v0.9.29 |
| D7 | Una BO por civ, importada y mejorada | v0.9.30 |
| D8 | Editor de BO en la web: paleta y línea de tiempo con arrastre | 2026-10-22 y 23 |
| D9 | HUD propio descripto en un archivo | v0.9.34 |
| D10 | Estadísticas reales de cruces, pasos (a) a (d) | 2026-10-25 |
| D11 | El panel del coach no estorba; tarjeta chica con la BO terminada | v0.9.40 y v0.9.43 |
| D12 | Modo asistido del editor: objetivos que arman la BO | 2026-10-26 y 27 |
| 13 | Coach por líneas paralelas | 2026-10-30 |
