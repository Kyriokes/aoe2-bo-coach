# AoE2 BO Coach

Overlay liviano para Age of Empires II: Definitive Edition que lee la pantalla con OCR, sigue una build order (BO), se adapta a los desvíos del jugador y sugiere cómo redistribuir aldeanos. El plan completo está en `PLAN.md`: leelo antes de empezar cualquier fase.

## Cómo trabajar conmigo
- Hablame en español rioplatense informal.
- No uses guiones largos (—) en textos, usá comas.
- Guiame paso a paso y no asumas lo que veo en una interfaz.
- Antes de borrar código o archivos, preferí reordenar o mover.
- Trabajamos por fases (ver `PLAN.md`, sección 7). No avances a la siguiente fase sin que se cumpla el criterio de salida de la actual.
- **Autonomía:** tenés permiso para avanzar fases, commitear, pushear y mergear a `main` sin preguntarme. Frená solo cuando un paso necesite algo que únicamente yo puedo hacer (jugar una partida, calibrar en pantalla, probar usabilidad, crear cuentas); en ese caso dejame instrucciones paso a paso.

## Decisiones tomadas
- **Runtime 100% C# .NET 10 (LTS) + WPF.** Elegido por rendimiento: menos RAM, ejecutable más chico, OCR nativo de Windows (`Windows.Media.Ocr`), overlay y hotkeys con APIs Win32. No mezclar Python en runtime. Se pasó de .NET 8 a .NET 10 porque .NET 8 pierde soporte en noviembre de 2026.
- **Targets:** `Core` y `Core.Tests` en `net10.0`; `Vision`, `App` y `Vision.Tests` en `net10.0-windows10.0.19041.0` (el número de versión de Windows es necesario para usar las APIs WinRT como `Windows.Media.Ocr`). Solución en formato `BoCoach.slnx`.
- **Python solo como herramienta offline** en `tools/extractor/`, para convertir el `.dat` del juego a `data/game_data/<version>.json` una vez por parche.
- **Separación estricta:** `BoCoach.Core` (net10.0) es lógica pura, sin dependencias de Windows ni de UI. Motor y economía se testean con snapshots simulados. `ArquitecturaTests` en `Core.Tests` falla si `Core` referencia ensamblados de Windows o de la app.
- **Solo lectura de pantalla.** Nunca leer memoria del proceso, inyectar código ni enviar inputs al juego.
- **Calibración manual:** el usuario ubica rectángulos semitransparentes sobre el HUD; las coordenadas se guardan relativas al área de cliente de la ventana del juego (proceso `AoE2DE_s` de Steam o `AoE2DE`), que se busca con GetClientRect/ClientToScreen (posición de la ventana, nunca memoria). Si el juego no está abierto, se usa la pantalla principal.
- **Atajos globales** (RegisterHotKey): Ctrl+Alt+C calibrar, Ctrl+Alt+R grabar. Indicador "● Grabando" click-through arriba del juego.
- **Log de eventos del juego** ("House Built", "Villager Created", al borde de la pantalla, dura unos 5 s y se desvanece) se usa para confirmar pasos. Leer a 2 capturas por segundo.
- **Editor de BO dentro de la app** (Fase 4) con vista previa simulada. **Web con biblioteca para compartir** en la última fase (Fase 10). App y web usan el mismo formato JSON con `schema_version`.
- **Git:** una rama por fase (`fase-N-<nombre>`) con commits chicos. Cada fase sale de la anterior (ramas apiladas). El push a `main` (fast-forward) funciona cuando el usuario lo pide explícitamente en el chat; por iniciativa propia lo bloquea el clasificador de permisos. No hay `gh` ni conector de GitHub autenticado.
- **Repo público con licencia MIT.** CI con GitHub Actions en `windows-latest` (build + test de toda la solución) en cada push a cualquier rama. Como el repo es público, el estado del CI se consulta sin autenticación: `curl -s "https://api.github.com/repos/Kyriokes/aoe2-bo-coach/actions/runs?per_page=3"`.
- **Editor del usuario:** Cursor / VS Code (no hay Visual Studio instalado).

## Entorno de desarrollo (verificado)
- SDK .NET 10.0.203 fijado en `global.json` (también hay 5, 6, 8 y 9 instalados).
- Windows 10 Enterprise LTSC 2021 (build 19044). Dos monitores de 1920×1080: DISPLAY1 principal en (0,0) y DISPLAY2 a la derecha en (1920,0). En el juego, "Full desktop" se estira a los dos monitores y es injugable: usar "Full screen" o "Windowed". OCR de Windows disponible en `en-US` y `es-MX`; el juego está en inglés, así que se usa `en-US`.
- Python 3.12 con `genieutils-py` 0.1.2 en el venv `tools/extractor/.venv` (ignorado por git). Parsear el `.dat` tarda unos 30 s.
- En el `.dat`, el TC que se construye es la base 621, que se apila en el TC real 109 (el que da población y entrena aldeanos); al subir de edad pasa a 71/141/142. El extractor los resuelve todos como `town_center`.
- La tasa de granjero del `.dat` es 0,53 (sin caminatas); la efectiva ronda 0,32. El Planner tiene que usar eficiencias por fuente, no la tasa cruda.

## Datos del juego (verificado)
- Instalación: `H:\Steam\steamapps\common\AoE2DE`
- Archivo de datos: `resources\_common\dat\empires2_x2_p1.dat` (VER 8.9, 63 civs, 1510 techs)
- Se lee con `pip install genieutils-py`:
  ```python
  from genieutils.datfile import DatFile
  d = DatFile.parse(path)
  u = d.civs[0].units[83]                      # aldeano
  u.creatable.resource_costs                   # lista con .type, .amount, .flag
  u.creatable.train_locations                  # lista con .unit_id, .train_time
  t = d.techs[101]                             # paso a Feudal
  t.resource_costs, t.research_locations       # .location_id, .research_time
  ```
- Tipos de recurso: 0 comida, 1 madera, 2 piedra, 3 oro.
- **Mapear por ID, no por nombre:** los nombres internos están desfasados (tech 101 "Middle Age" es el paso a Feudal; tech 102 "Feudal Age" es el paso a Castillos).
- Valores verificados: aldeano (83) 50 comida/25 s; casa (70) 25 madera/25 s; cuartel (12) 175 madera/50 s; molino (68), campamento maderero (562), campamento minero (584) 100 madera/35 s; arquero (4) 25 madera + 45 oro/35 s; telar (22) 50 oro/25 s; Feudal (101) 500 comida/130 s; Castillos (102) 800 comida + 200 oro/160 s.

## Decisiones abiertas
Ver `PLAN.md`, sección 10.

## Estado actual
- Fase 0 (setup) completa: CI en verde en GitHub.
- Fase 1 (datos del juego) completa: extractor, v8.9.json y tests de valores clave.
- Fase 2 (modelo de BO) completa: serializador, validador, events.json y 3 BO de ejemplo.
- Fase 3 (motor) completa: PlayerState, Tracker, Planner y LivePlan con tests de los 5 escenarios.
- Fase 5 (economía v1) completa: RateObserver, EconomyAdvisor (estimación de aldeanos, demanda, déficit, greedy) y SuggestionGate.
- Fase 4 (editor WPF) hecha y verificada por código (tests de ViewModels y capturas con --screenshot). La prueba de usabilidad del usuario (BO de 20 pasos en menos de 5 minutos) quedó pendiente; el usuario pidió seguir con la Fase 6.
- main está al día hasta la Fase 6 (el usuario pidió explícitamente cada push a main, 2026-10-15).
- Fase 6 (captura y calibración): código hecho y testeado (ScreenCapture GDI, Png, RegionLayout, OcrReader, SessionRecorder, CalibrationWindow, barra con "Calibrar pantalla" y "Grabar partida", app.manifest PerMonitorV2). Falta del usuario: calibrar las 7 regiones en menos de 2 minutos con el juego abierto y grabar una partida de prueba.
- Si el editor está abierto, `BoCoach.exe` de Debug queda bloqueado: compilar en Release o con `-o` a otra carpeta.
