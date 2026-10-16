# AoE2 BO Coach: Plan del proyecto

Overlay liviano para Age of Empires II: Definitive Edition que lee la pantalla en vivo (OCR), sigue una build order (BO) armada por el jugador, se adapta cuando el jugador se desvía y sugiere cómo redistribuir aldeanos para no acumular recursos que no necesita. Incluye un editor de BO dentro de la app y, en la fase final, una web para compartirlas.

---

## 1. Objetivo y alcance

**Qué hace**
- Lee en vivo: reloj, comida, madera, oro, piedra, población y el log de eventos ("House Built", "Villager Created").
- Permite armar, editar y guardar BO desde la propia app, con vista previa de tiempos estimados.
- Sigue la BO elegida y muestra en un overlay el paso actual y el siguiente.
- Detecta desvíos (adelantos, atrasos, acciones fuera del plan) y replanifica.
- Sugiere redistribución de aldeanos entre recursos para cumplir el plan.
- (Fase final) Web con biblioteca pública de BO para compartir e importar.

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
| App de escritorio | .NET 10 (LTS, soporte hasta noviembre de 2028), WPF |
| Captura | GDI `BitBlt` por región; `Windows.Graphics.Capture` como alternativa |
| OCR | `Windows.Media.Ocr`; plan B: template matching de dígitos propio |
| Hotkeys | `RegisterHotKey` (Win32) |
| Optimización (refinamiento) | Greedy propio; Google OR-Tools (.NET) si hace falta LP |
| Tests | xUnit |
| Extractor de datos | Python 3.12 + `genieutils-py` (herramienta offline) |
| Distribución | Publicación self-contained single-file de .NET |
| Web (fase final) | Next.js en Vercel + Supabase (Postgres, cuentas con Discord y Google); el `Core` compilado a WebAssembly para la línea de tiempo |

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

Regla clave: **el motor y la economía (`BoCoach.Core`) no saben nada de pantallas, OCR ni WPF.** Reciben un `GameSnapshot` y devuelven recomendaciones. Así se testean con datos simulados sin abrir el juego, y el editor de BO reutiliza el mismo planner para la vista previa.

---

## 4. Estructura del repo

```
aoe2-bo-coach/
├─ README.md
├─ PLAN.md
├─ CLAUDE.md
├─ LICENSE                    # MIT
├─ BoCoach.slnx
├─ global.json                # fija el SDK de .NET 10
├─ .github/workflows/ci.yml   # build + test en windows-latest
├─ data/
│  ├─ game_data/              # generado por el extractor, versionado por parche
│  │  └─ v8.9.json
│  └─ events.json             # frases del log conocidas → id de evento
├─ build_orders/              # BO de ejemplo
│  └─ scouts_feudal.json
├─ src/
│  ├─ BoCoach.Core/           # net10.0, lógica pura, sin dependencias de Windows
│  │  ├─ Data/                # modelos de game_data y carga
│  │  ├─ BuildOrders/         # modelo, validación, serialización
│  │  ├─ Engine/              # GameSnapshot, PlayerState, Tracker, Planner
│  │  └─ Economy/             # Rates, Deficit, Allocator
│  ├─ BoCoach.Vision/         # net10.0-windows10.0.19041.0: captura, preprocesado, OCR, suavizado y grabación (Recording/SessionRecorder = RecordSession)
│  └─ BoCoach.App/            # WPF, net10.0-windows10.0.19041.0
│     ├─ Overlay/
│     ├─ Calibration/         # UI de rectángulos
│     ├─ BuildOrderEditor/    # editor de BO
│     └─ Hotkeys/
├─ tests/
│  ├─ BoCoach.Core.Tests/
│  ├─ BoCoach.Vision.Tests/   # contra capturas grabadas
│  └─ fixtures/
├─ tools/
│  ├─ extractor/              # Python: .dat → game_data/*.json
│  │  ├─ extract.py
│  │  └─ requirements.txt
│  ├─ (RecordSession)         # quedó en BoCoach.Vision/Recording, con el botón "Grabar partida" de la app
│  └─ ReplaySession/          # corre el pipeline sobre una grabación
├─ src/BoCoach.Wasm/          # el Core en browser-wasm para la web (JSExport: Init y Analyze)
└─ web/                       # biblioteca de BO: Next.js + Supabase (ver web/README.md)
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

Es el mismo formato en la app, en los archivos locales y en la web.

```json
{
  "schema_version": 1,
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
- `assign` indica a qué recurso va el aldeano producido.
- `after`, `at_pop`, `at_time` son condiciones opcionales. `confirm_by` usa ids de `events.json`.
- `schema_version` permite migrar BO viejas cuando el formato cambie.

### 5.3 `regions.json` (generado por la calibración, en la carpeta de usuario)

```json
{
  "reference_resolution": [1920, 1080],
  "regions": {
    "clock":      [0.46, 0.02, 0.06, 0.025],
    "food":       [0.01, 0.005, 0.04, 0.02],
    "population": [0.30, 0.005, 0.05, 0.02],
    "event_log":  [0.00, 0.10, 0.18, 0.08]
  }
}
```

Coordenadas relativas (x, y, ancho, alto como fracción de la pantalla) para soportar cambios de resolución.

### 5.4 `data/events.json`

```json
{
  "house_built":       ["House Built"],
  "villager_created":  ["Villager Created"],
  "lumber_camp_built": ["Lumber Camp Built"]
}
```

A completar relevando el log en partidas reales con `RecordSession`.

---

## 6. Módulos en detalle

### 6.1 Captura y calibración
- Se capturan solo los rectángulos configurados, no la pantalla completa.
- UI de calibración: ventana WPF transparente sobre el juego con un rectángulo semitransparente por región, con etiqueta. Se arrastran y estiran con el mouse. Debajo de cada uno se muestra en vivo lo que lee el OCR. Botón Guardar.

### 6.2 Visión
- **Números:** escala de grises, umbral, escalado x3 y `Windows.Media.Ocr`. Se filtra el resultado a dígitos, `/` y `:`.
- **Plan B para números:** la fuente del HUD es fija, así que se pueden recortar los 10 dígitos una vez y comparar píxeles (template matching). Es más rápido y preciso que un OCR general.
- **Log:** OCR de texto libre, limpieza de guiones y coincidencia aproximada (distancia de Levenshtein) contra `events.json`. Dedupe: el mismo evento visible durante 5 s cuenta una sola vez, salvo que aparezca una línea nueva idéntica (dos casas seguidas); para eso se rastrea la posición de cada línea entre capturas.
- **Frecuencia:** 2 lecturas por segundo. El log dura unos 5 s, así que se ve en al menos 10 capturas.
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
- Estados por paso: `pending`, `ready` (hay recursos y se cumplen condiciones), `done`, `late`, `skipped`.
- Un paso pasa a `done` con su `confirm_by` o cuando el conteo lo prueba.
- Si se confirma un paso posterior y uno anterior sigue pendiente, el anterior queda `late` y se muestra en otro color; no se pierde.
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
- Contenido: paso actual (grande) con lo que falta para hacerlo, lo que está en curso (una edad desde que se paga), siguiente (chico), pasos `late` en otro color, sugerencia económica y línea de desvío contra el objetivo.
- Hotkeys (todas con Ctrl+Alt, que el juego no usa): S empezar o detener, → avanzar, ← retroceder, ↓ saltear, P pausar, H ocultar, D CPU y RAM.
- Posición: arriba a la derecha de la ventana del juego (lejos del log de eventos) y la sigue si se mueve. Configurable más adelante.
- En curso: el log avisa cuando una investigación termina, no cuando se hace clic. El clic se reconoce porque el stock baja de golpe justo el costo.
- CPU: el OCR del log se pasa solo si hay texto y si cambió desde la captura anterior.

### 6.7 Editor de build orders (en la app)
- Lista de pasos con alta, edición, duplicado y **reordenamiento por arrastre**.
- Selector de acción (`train`, `build`, `research`, `move`) y de entidad desde `game_data`, filtrado por edad y por civ.
- Campos por paso: cantidad, recurso asignado, condiciones (`after`, `at_pop`, `at_time`), evento de confirmación y nota libre.
- Metas de la BO: tiempo objetivo de Feudal/Castillos y distribución de aldeanos esperada.
- **Vista previa en vivo:** usa el Planner de `Core` para simular la BO y mostrar una línea de tiempo con el tiempo estimado de cada paso, recursos acumulados y los pasos donde el plan queda trabado (por ejemplo, falta de población por no hacer casas).
- Validación con mensajes claros (paso sin recurso asignado, dependencia circular, entidad que no existe en esa edad).
- Biblioteca local: lista de BO guardadas, buscar, duplicar, importar y exportar archivo `.json`.

### 6.8 Web y biblioteca (fase final)
- Página pública con la biblioteca de BO: buscar por civ, mapa, estrategia y tags; ordenar por popularidad.
- Detalle de cada BO con la misma línea de tiempo del editor (el planner se reimplementa o se comparte la lógica).
- Cuentas de usuario para publicar, editar y votar.
- Importar a la app: botón "Abrir en BO Coach" con un enlace `bocoach://import/<id>` (protocolo registrado por la app) y, como alternativa, código corto para pegar.
- La app puede publicar una BO directo desde el editor.
- API mínima: `GET /bo`, `GET /bo/{id}`, `POST /bo`, `PUT /bo/{id}`, `POST /bo/{id}/vote`.
- La web valida con el mismo `schema_version` que la app.

---

## 7. Fases

Cada fase termina con algo que se puede probar. Las fases 1 a 5 no necesitan el juego abierto.

| Fase | Entregable | Criterio de salida |
|---|---|---|
| **0. Setup** | Repo, solución .NET con los 3 proyectos, xUnit, CI con GitHub Actions | `dotnet test` en verde |
| **1. Datos del juego** | `tools/extractor` → `data/game_data/v8.9.json`; carga del JSON en `Core` | Valores clave coinciden con el juego (aldeano 50f/25s, casa 25w, Feudal 500f/130s); test automático sobre esos valores |
| **2. Modelo de BO** | Clases, serialización, validador, 2 o 3 BO de ejemplo | Una BO mal formada da un error claro; las de ejemplo validan |
| **3. Motor** | Tracker y Planner sobre snapshots simulados | Tests: partida perfecta, jugador atrasado, paso salteado, acción extra, corrección manual |
| **4. Editor de BO** | Editor WPF con biblioteca local y vista previa de tiempos | Armar una BO de 20 pasos desde cero en menos de 5 minutos; la vista previa marca un bloqueo de población |
| **5. Economía v1** | Tasas, déficits y asignador greedy | Escenario simulado "sobra madera" sugiere mover aldeanos a comida; sin oscilaciones en 5 min simulados |
| **6. Captura + calibración** | UI de rectángulos, `regions.json`, `RecordSession` | Calibrar las 9 regiones en menos de 2 minutos; grabar una partida de prueba |
| **7. Visión** | OCR de números, OCR del log, suavizado | Sobre 2 partidas grabadas: más del 98% de lecturas correctas en comida, madera y población; más del 95% de eventos del log sin duplicados |
| **8. Overlay + integración** | Loop completo en vivo con overlay y hotkeys | Una partida completa siguiendo la BO, CPU menor al 3%, RAM menor a 100 MB, sin interferir con mouse ni teclado |
| **9. Refinamiento** | LP, bonificaciones por civ, oro y piedra, más eventos, instalador | Ejecutable que corre en otra PC sin instalar nada |
| **10. Web y biblioteca** | Sitio, API, cuentas, publicación e importación desde la app | Publicar una BO desde la app y abrirla en otra PC con el enlace |

Orden recomendado: 0 → 1 → 2 → 3, luego 4 y 5 en paralelo, después 6 → 7 → 8 → 9 → 10. El núcleo lógico y el editor quedan probados antes de pelear con el OCR, y con el editor ya se pueden armar las BO de prueba para todo lo que sigue.

---

## 8. Estrategia de pruebas

- **Unit tests** de `Core` (motor, economía, validación de BO) con snapshots escritos a mano.
- **Grabación y replay:** `RecordSession` guarda las regiones capturadas con timestamp durante una partida real. `ReplaySession` corre todo el pipeline sobre la grabación, así se reproduce cualquier bug sin volver a jugar.
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
3. ¿Importador de formatos de BO de la comunidad? (Propuesta: después de la Fase 10, como extra.)
4. ~~Stack de la web.~~ Resuelto: mismo repo (`web/`), Next.js + Supabase + Vercel, y el `Core` de C# compilado a WebAssembly para validar y simular en el navegador (una sola implementación del Planner). Login con Discord y Google.
5. ~~Licencia del repo.~~ Resuelto: repo público con licencia MIT.
