# AoE2 BO Coach

Overlay liviano para Age of Empires II: Definitive Edition que lee la pantalla con OCR, sigue una build order (BO), se adapta a los desvíos del jugador y sugiere cómo redistribuir aldeanos. El plan completo está en `PLAN.md`: leelo antes de empezar cualquier fase.

## Cómo trabajar conmigo
- Hablame en español rioplatense informal.
- No uses guiones largos (—) en textos, usá comas.
- Guiame paso a paso y no asumas lo que veo en una interfaz.
- Antes de borrar código o archivos, preferí reordenar o mover.
- Trabajamos por fases (ver `PLAN.md`, sección 7). No avances a la siguiente fase sin que se cumpla el criterio de salida de la actual y sin confirmarlo conmigo.

## Decisiones tomadas
- **Runtime 100% C# .NET 10 (LTS) + WPF.** Elegido por rendimiento: menos RAM, ejecutable más chico, OCR nativo de Windows (`Windows.Media.Ocr`), overlay y hotkeys con APIs Win32. No mezclar Python en runtime. Se pasó de .NET 8 a .NET 10 porque .NET 8 pierde soporte en noviembre de 2026.
- **Targets:** `Core` y `Core.Tests` en `net10.0`; `Vision`, `App` y `Vision.Tests` en `net10.0-windows10.0.19041.0` (el número de versión de Windows es necesario para usar las APIs WinRT como `Windows.Media.Ocr`). Solución en formato `BoCoach.slnx`.
- **Python solo como herramienta offline** en `tools/extractor/`, para convertir el `.dat` del juego a `data/game_data/<version>.json` una vez por parche.
- **Separación estricta:** `BoCoach.Core` (net10.0) es lógica pura, sin dependencias de Windows ni de UI. Motor y economía se testean con snapshots simulados. `ArquitecturaTests` en `Core.Tests` falla si `Core` referencia ensamblados de Windows o de la app.
- **Solo lectura de pantalla.** Nunca leer memoria del proceso, inyectar código ni enviar inputs al juego.
- **Calibración manual:** el usuario ubica rectángulos semitransparentes sobre el HUD; las coordenadas se guardan relativas a la resolución.
- **Log de eventos del juego** ("House Built", "Villager Created", al borde de la pantalla, dura unos 5 s y se desvanece) se usa para confirmar pasos. Leer a 2 capturas por segundo.
- **Editor de BO dentro de la app** (Fase 4) con vista previa simulada. **Web con biblioteca para compartir** en la última fase (Fase 10). App y web usan el mismo formato JSON con `schema_version`.
- **Git:** una rama por fase (`fase-N-<nombre>`), commits chicos y PR a `main`. El merge lo hace el usuario desde GitHub cuando se cumple el criterio de salida. No hay `gh` instalado: guiar el PR paso a paso por la web.
- **Repo público con licencia MIT.** CI con GitHub Actions en `windows-latest` (build + test de toda la solución).
- **Editor del usuario:** Cursor / VS Code (no hay Visual Studio instalado).

## Entorno de desarrollo (verificado)
- SDK .NET 10.0.203 fijado en `global.json` (también hay 5, 6, 8 y 9 instalados).
- Windows 10 Enterprise LTSC 2021 (build 19044). OCR de Windows disponible en `en-US` y `es-MX`; el juego está en inglés, así que se usa `en-US`.
- Python 3.12 instalado, pero `genieutils-py` todavía no está instalado en ese Python (se resuelve en la Fase 1 con un venv en `tools/extractor/`).

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
- Fase actual: 0 (setup del repo), en la rama `fase-0-setup`. Solución, 5 proyectos, tests de humo y CI armados; `dotnet test` en verde local. Falta: PR y CI en verde en GitHub.
