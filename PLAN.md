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
│  └─ scouts_22p.json
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
- `group` (opcional) arma un **paso mixto**: los pasos seguidos con el mismo grupo son un solo paso con varias metas que se hacen a la vez (por ejemplo, el clic en Feudal y el cuartel). Cada meta avanza por su cuenta, el paso termina cuando terminan todas, y una meta no deja "atrasada" ni da por hecha a otra del mismo grupo. En el editor: "Junto con el anterior".
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
- Centro urbano parado: si el total de aldeanos no sube en lo que tarda uno más 8 s y el centro urbano no investiga, se avisa (sin leer la cola; ver la decisión abierta 6).

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

**Estado al 2026-10-19 (v0.9.15):** las fases 0 a 10 están hechas. Criterios de salida sin verificar: Fase 4 (armar una BO de 20 pasos en menos de 5 minutos, prueba del usuario) y Fase 10 (abrir una BO en otra PC con el enlace, con el zip de la release). La Fase 7 se cumplió con el mod del HUD (`BoCoach HUD`) y sus letras, no con los números de color del juego; la Fase 8 midió 0,4 a 2 % de CPU y 50 a 75 MB; la Fase 9 publicó la versión portable como release de GitHub. Lo que falta está ordenado en la sección 11.

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
3. ~~Importador de formatos de BO de la comunidad.~~ Resuelto en la Fase 9: `RtsOverlayImporter` lee el exportable de buildorderguide.com (formato de RTS Overlay) y agrega lo que esas guías dan por sabido (casas, campamentos, granjas); en la app, "Importar…" y "Pegar BO"; en ReplaySession, `import`. Los tres ejemplos de la app vienen de ahí.
6. ~~Leer la cola de producción de los edificios.~~ Resuelto en la Fase 9: la cola de arriba a la izquierda se ve siempre, sin seleccionar el edificio. `Vision/Hud/ProductionQueue` la lee con una biblioteca de íconos (`data/icons/production_queue.json`, armada con `ReplaySession icons`/`iconlib`/`teach`) y el coach saca de ahí si el centro urbano está parado (5 s de gracia), las investigaciones en curso aunque no se haya visto la baja del stock, y los edificios iguales que producen a la vez. Queda pendiente la escala a otras resoluciones (sección 11, B4).
4. ~~Stack de la web.~~ Resuelto: mismo repo (`web/`), Next.js + Supabase + Vercel, y el `Core` de C# compilado a WebAssembly para validar y simular en el navegador (una sola implementación del Planner). Login con Discord y Google.
5. ~~Licencia del repo.~~ Resuelto: repo público con licencia MIT.

---

## 11. Hoja de ruta (desde el 2026-10-19)

Orden elegido por el usuario: primero la robustez del coach en partidas reales, después la presentación y al final las ideas grandes. Cada ítem tiene su criterio de salida; cada cambio se hace con un test que falle primero y en su propio commit.

### Bloque B: robustez del coach en partidas reales

- **B1. Tiempo relativo.** Hecho el 2026-10-19 (commits dd0c87b y el aviso del panel; sobre la 20261017-163738 la BO arranca a las 36:01 del reloj). Los objetivos de la BO se cuentan desde que el jugador empieza a jugar, no desde 00:00. En la partida 20261017-163738 el reloj marcaba 36:26 con 6 aldeanos (36 minutos en idle antes de arrancar) y todos los `at_time` y los objetivos de Feudal quedaban atrasados. El coach fija un inicio de juego (primer snapshot con el reloj andando y los 3 aldeanos iniciales, o el primer aldeano nuevo; si ya hay más aldeanos, una estimación hacia atrás) y el Tracker, el Planner y el desvío contra `targets` usan el tiempo desde ese inicio. Tests con la fixture `lecturas.csv` de scouts corrida +2000 s: mismos pasos y tiempos que sin correr. Criterio: sobre la 20261017-163738 (desde la captura 1000) el desvío se mide desde el 36:26.
- **B2. Civ de la partida, no de la BO.** Primera parte hecha el 2026-10-19 (desplegable "Civ" en la ventana, `ReplaySession coach --civ`; sobre la 20261017-163738 el tope sube de 10 en 10). Falta la segunda: leerla sola de la pantalla. Hoy `Coach` usa `BuildOrder.Civ` ("any" en las BO importadas). Primero un desplegable "Civ de esta partida" en la ventana principal (por defecto la de la BO, guardado en `settings.json`) que pase a `PlanStart.For`, al Planner y a la economía; después leerla de la pantalla de carga o del panel de puntajes (región `scores`, ya se graba) con el OCR de Windows, con el desplegable como confirmación. Criterio: en la partida de incas las casas dan 10 de población y los scouts cuestan 68. Diseño acordado con el usuario el 2026-10-20: la civ sale de la **pantalla de carga**, no de las dos letras del panel de puntajes (no están en los textos del juego, parecen el comienzo del nombre y se pisan: "Ma" son magyares, malayos, malienses y mayas). (1) Reconocer que se está en la pantalla de carga: de ahí salen el inicio real de la partida (lo de antes no cuenta, mejora B1) y las civs de todos los jugadores. (2) El nombre del jugador se captura en el tutorial de primer uso (C4): después de calibrar, una partida rápida contra 7 IA, y en su pantalla de carga se lee el nombre. Si el jugador lo cambia, lo pone en la app (más adelante se ve cómo se actualiza solo). (3) Equipos fijos según el usuario: 1, 3, 5 y 7 contra 2, 4, 6 y 8, así se sabe contra quién se juega; falta verificar si el 1 queda enfrente del 2 o del 8 (espejado). Hecho el mismo día: `Vision/Loading`, "Tu nombre" en la ventana, el coach reinicia la partida y toma la civ en la pantalla de carga, y la captura de diagnóstico la lista. La primera versión salió de un JSON del juego que no era esta pantalla; se rehízo con la captura de diagnóstico del usuario (4 contra 4 contra la IA): lee los 8 jugadores, civs, equipos y número por color. Posiciones en multijugador (usuario): 1, 3, 5 y 7 contra 2, 4, 6 y 8 en círculo, el 1 enfrente del 2 y el 7 y el 8 en el otro extremo. Falta: el mapa (lo tapa el overlay del coach), la pantalla en castellano y una en multijugador.
- **B3. Incas: el "Settlement".** Hecho el 2026-10-19 (entidad `settlement` con `counts_as`; sobre la 20261017-163738 campamentos, molino y casa quedan hechos a las 3:38). El extractor lo saca del `.dat` y `game_data` dice que cuenta como campamento maderero, campamento minero, molino y casa a la vez; "Settlement Built" confirma los pasos de esos cuatro edificios y suma su población. Depende de B2. Criterio: en la 20261017-163738 los pasos de campamentos y molino quedan hechos con el Settlement.
- **B4. Autocalibración por resolución** (pedido del 2026-10-18; el ítem más grande). Partes (a) y (c) hechas el 2026-10-19 (`Vision/Regions/AutoCalibration`, al empezar el coach; verificado sobre una pantalla armada con las cajas grabadas a 1080p y al doble, no todavía sobre una captura real: `ReplaySession autocalibrate <pantalla.png>` sirve para eso). (b) hecha el 2026-10-20 normalizando las capturas (cada región se reescala por 1/escala antes de leerla; `regions.json` y `session.json` guardan `hud_scale`), probada con la captura real de un amigo a escala 1,09 (`tests/fixtures/screens/amigo-20261020-1080p-panel-negro.png`, el único fixture a otra escala); falta el golden sobre una grabación a otra resolución. Ese día el detector pasó a anclar en las franjas de color (el panel negro de otras civs se confundía con los cuadrados). (a) Con el mod, buscar las anclas en toda la pantalla: los cuadrados negros con franja de color y el rectángulo negro del reloj (`ModPanel` ya los reconoce dentro de una caja); de ahí salen las 9 regiones y la escala respecto de 1080p; verificar contra la calibración manual sobre las grabaciones de 1080p (±2 px). (b) Escalar las letras `mod_*` por el alto del cuadrado (o normalizar el recorte) y `ProductionQueue.IconSize`; hace falta una grabación corta del usuario a otra resolución (2560×1440 o 1600×900 en ventana) para el golden. (c) La calibración manual queda como respaldo cuando no hay anclas (sin mod). Criterio: sobre la grabación a otra resolución, reloj, stocks y aldeanos al 98 % sin tocar las cajas.
- **B5. Prueba de usabilidad del editor** (criterio de la Fase 4, tarea del usuario): una BO de 20 pasos desde cero, cronometrada. Si pasa de 5 minutos, anotar qué trabó y arreglarlo.
- **B6. Mantenimiento:** cada partida nueva se revisa con la skill `revisar-grabacion`; cada parche del juego, extractor y `game_data` nuevo. De la revisión de seguridad del 2026-10-19 quedan las acciones del CI fijadas por SHA (o Dependabot) y el `dotnet-install.sh` de Vercel sin hash (a propósito: Microsoft lo actualiza).

### Bloque C: presentación y portfolio (después de B1 a B3; antes de B4 si B4 se demora)

1. **Probar la release en otra PC** (criterio de la Fase 10, tarea del usuario): bajar el zip de la release, instalar el mod desde la app, calibrar, registrar los enlaces en "Cuenta de la web" y abrir una BO con "Abrir en BO Coach". Lo que no se entienda sin ayuda va al LEEME y a la portada.
2. **Capturas y video** (tarea del usuario): overlay en partida, editor con vista previa, calibración y web, para el README, la portada (`web/src/app/page.tsx`) y el portfolio.
3. **README y `docs/LEEME-portable.txt`**: enlace a la release y guía de primeros pasos (mod, calibrar, grabar, empezar el coach), con lo que salió de 1.
4. **Tutorial de primer uso** (idea del usuario, 2026-10-20): instalar el mod, calibrar y una partida rápida contra 7 IA, en cuya pantalla de carga se lee el nombre del jugador (ver B2).
5. **Ejecutable de un solo archivo** (`data/` adentro del `.exe`): el último paso del bloque, no antes.

### Bloque D: ideas grandes (diseño con el usuario antes de escribir código)

Cada una arranca con una charla de diseño y se escribe acá antes de codearla.

1. **"Si no sabe, que se calle" / economía sana como piso.** Cuando el jugador abandona la BO (arquerías y herrería donde la BO pedía establo y molino, partida 20261017-163738), el coach deja de pedir los pasos viejos y se queda con lo que siempre vale: centro urbano produciendo, casas, inactivos y la GD contra lo que hay. Es la base de lo que sigue. Criterios del usuario (2026-10-20): la BO se da por abandonada si se producen muchas unidades ajenas a ella o si se atrasa un tiempo considerable. Sumar 3 a 5 unidades de reacción (piqueros en una BO de arqueros) no es abandonarla; más que eso con la producción de la unidad de la BO totalmente parada es un cambio de build; si produce las dos a la par es un gris difícil. Con la BO abandonada, los avisos de casas y de aldeanos inactivos siguen. Valores aprobados por el usuario el 2026-10-20: abandonada con más de 5 unidades que la BO no tiene y la unidad de la BO sin producirse hace 60 s o más, o con 3 minutos de atraso respecto del paso que la BO esperaba para ese momento; en el gris (las dos unidades a la par) sigue con la BO pero no marca atrasado lo militar. Hecho el mismo día (`Engine/BuildOrderAbandonment`, v0.9.20), con un cambio: el atraso se mide como 3 minutos sin avanzar ningún paso de la BO, porque contra el plan del simulador (optimista: Feudal de scouts a las 7:30 con objetivo 9:00) la partida de scouts del 2026-10-15, siguiendo la BO a ritmo humano, quedaba abandonada a las 13:05. Abandonada, el overlay dice por qué y "Atrás" la retoma. En las grabaciones: incas 20261017-163738 a las 17:58 (sexto arquero con BO de scouts); arqueros 20261018-132830 y 001030 y scouts, nunca. Falta probarlo en vivo.
2. **BO como postura de cierre con prioridades:** 1 economía sana, 2 postura, 3 el excedente a sostener producción y después a la próxima edad, 4 la estrategia queda en la BO. Ejemplo del usuario: "Scouts Feudal (22 pop)" cierra con 1 aldeano y 1 scout siempre en cola y clic en Castillos. Cambia el formato (`schema_version` 3) y el validador; la web lo toma del Core. Diseño acordado el 2026-10-20: (1) campo opcional `posture` en la BO: `queue` (unidades siempre en cola, cada una en todos los edificios que la producen) y `next_age` (la edad a la que se apunta). Sin subir `schema_version`: el lector ignora los campos que no conoce, así que las versiones viejas abren las BO nuevas sin la postura. (2) Entra al terminar la BO; con la BO abandonada (D1) queda solo lo básico. (3) Las BO sin postura (las importadas) la deducen: aldeanos más la última unidad militar que entrena la BO, y la edad siguiente a la última que clica; se cambia en el editor. (4) En el overlay: "Postura: aldeanos y exploradores siempre en cola", el edificio parado ("Establo parado: poné exploradores en cola", con la cola de producción, un ícono por edificio trabajando) o que falta, y la próxima edad: lo que falta guardando para un lote de cada unidad de la postura por edificio, "te falta 1 edificio de Feudal", "Hacé clic en Castillos" o "Castillos en curso". Hecho el mismo día (v0.9.21): `Engine/Posture` (postura efectiva y `PostureCoach`), `CoachView.Posture`, sección en el editor y renglón en la página de la web. La postura aparece cuando no queda paso actual (las granjas y la producción militar a medias nunca se cierran solas). Falta: que la GD y la economía usen la demanda de la postura (hoy la GD sigue con lo básico) y que el editor ofrezca más unidades (game_data tiene 11).
3. **Ramas disparadas por señales:** clic del rival (puntaje que cae de golpe, región `scores`), ataques (aviso del log), aldeanos perdidos. Necesita la letra del panel de puntajes y leer el color del aviso de ataque. Diseño acordado el 2026-10-20: (1) cada señal da siempre un aviso con un consejo general, y si la BO trae una rama para esa señal (pasos alternativos, armados en el editor), el coach la agrega; (2) las tres señales: rival que sube de edad, te atacan, perdiste aldeanos; (3) el rival se avisa en el clic (el puntaje cae de golpe al pagar la edad), no al llegar. El panel de puntajes (verificado en la grabación 20261019-202635): un renglón por jugador con el número en un cuadrado de su color, "Nombre: puntaje/puntaje", la civ abreviada y un ícono con la edad en romanos; se reordena por puntaje, así que cada jugador se sigue por número y nombre. Hecho el mismo día (v0.9.22), con lo que mostraron los datos: el puntaje sí cae en el clic (la décima parte del costo: el clic de Feudal del usuario bajó 49) y el ícono cambia al llegar, no en el clic; el número del cuadrado no se lee, así que cada jugador se sigue por la continuidad de su puntaje con el color del texto como desempate (asignando todos los renglones juntos). En las tres grabaciones con panel avisa el clic de Feudal del rival sin falsos. Ramas: campo `branches` (on, before, note, steps), editor con "Pasos de" y página de la web. Falta: leer el aviso de ataque, que sale del color del atacante (hace falta una grabación con un ataque), y el panel con más de 2 jugadores (la región por defecto tiene 2 renglones).
4. **Elegir la BO por civ, rival y mapa:** pantalla de carga (texto grande, OCR de Windows) y sugerencia desde la biblioteca. A futuro (usuario, 2026-10-20): que el coach elija la mejor estrategia estándar para la combinación de civs aliadas y enemigas.
5. **Informe y estadísticas de después de la partida:** con `lecturas.csv` ya está todo; primero `ReplaySession stats` sobre una grabación, después en la app al terminar el coach.
