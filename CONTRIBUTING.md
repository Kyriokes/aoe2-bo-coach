# Colaborar con BO Coach

Cómo compilar, probar y entender el proyecto. Lo del jugador está en el [README](README.md); todo lo demás, en el [índice de la documentación](docs/README.md).

## Qué hace falta

- **Windows 10 (2004 o posterior) u 11.** La app y sus tests usan WPF y `Windows.Media.Ocr`.
- **SDK de .NET 10** (la versión está en `global.json`).
- **Node 24 o más nuevo**, para la web y sus tests. Los tests de la web compilan el Core a WebAssembly, así que también necesitan el SDK de .NET.
- **Python 3.12**, solo para las herramientas offline (`tools/extractor`, `tools/stats`). Los tests corren con el venv de `tools/stats`: `python -m pytest -q` ahí y `python -m pytest -q ../extractor` para el extractor.
- El **paquete de OCR en inglés** de Windows: sin él, los tests de lectura del log y de la pantalla de carga no prueban nada (en el CI fallan a propósito).
- Con el instalador de Windows:

```bash
winget install Microsoft.DotNet.SDK.10 Git.Git OpenJS.NodeJS.LTS Python.Python.3.12
```

## Compilar y probar

```bash
dotnet build BoCoach.slnx -c Release
dotnet test BoCoach.slnx -c Release
dotnet run --project src/BoCoach.App
```

En `web/`:

```bash
npm ci
npm test
npm run lint
npm run typecheck
npm run build
```

Warnings = errores en .NET (`Directory.Build.props`). Cada cambio lleva un test que falle primero y su propio commit, en castellano, como una frase que dice qué quedó mejor. Los textos que ve el usuario van en dos idiomas: en la app, `src/BoCoach.App/Texts.cs`; en el Core, `Lang.L`; en la web, `web/src/i18n/es.ts` y `en.ts`.

Cada versión nueva que llega a `main` (la `<Version>` de `BoCoach.App.csproj` sin su etiqueta) se publica sola: `release.yml` arma el `.zip`, la release y la etiqueta, y el botón "Descargar para Windows" de la web baja esa. Cada corrida del CI también deja la versión portable como artefacto (14 días). Después de `dotnet publish`, `.github/scripts/verificar-portable.ps1` exige cada `data/**/*.json` al lado del `.exe` (un archivo que no vaya con la app se agrega a `$soloWeb` de ese script) y la release publica el `.zip` con su `.sha256`.

## Modo desarrollador

Cinco clics sobre el número de versión. Suma **Calibrar pantalla** (Ctrl+Alt+C: una capa con un rectángulo por región, para jugar sin el mod), **Grabar partida** (Ctrl+Alt+R: 2 capturas por segundo en `%LOCALAPPDATA%\BoCoach\recordings`) o "Grabar cada partida sola", **Captura de diagnóstico** (Ctrl+Alt+X: a los 2 s guarda la pantalla sin pérdida y un `.txt` con lo que encontró la app en `%LOCALAPPDATA%\BoCoach\diagnostico`), lo que ve el coach en el panel de abajo y CPU, RAM y milisegundos por captura en el overlay (Ctrl+Alt+D).

## Probar sin el juego

- Sobre una grabación: `BoCoach.exe --replay "<carpeta>" --bo <archivo.json> [--speed 4] [--stats uso.csv]`.
- El overlay en un momento de una partida: `BoCoach.exe --overlay-screenshot salida.png --readings lecturas.csv --bo <archivo.json> --at 8:20`.
- La ventana en un PNG: `BoCoach.exe --screenshot salida.png [--state sin-cuenta|esperando|en-partida|terminada|elegir] [--settings N] [--report] [--first-run] [--hotkeys] [--dev] [--lang en]`.
- La ventana de verdad, con ajustes temporales y sin instancia única: `BoCoach.exe --preview [--no-activate]`.
- Publicar BO con tu cuenta: `BoCoach.exe --publish <carpeta> [--dry-run]`.

### tools/ReplaySession

Los números del HUD se leen por plantillas (`data/fonts`), sacadas de una grabación real; el log de eventos, con el OCR de Windows. Todo se puede correr sobre una grabación, sin el juego:

```bash
dotnet run --project tools/ReplaySession -c Release -- replay "%LOCALAPPDATA%\BoCoach\recordings\<carpeta>"
```

Escribe `lecturas.csv` (reloj, recursos, aldeanos, edad y eventos por captura) y avisa si alguna caja corta los números. La lista entera de comandos está al comienzo de `tools/ReplaySession/Program.cs`. Los que más se usan:

- `coach <grabación o lecturas.csv> <bo.json>`: el loop completo (visión, motor y economía), mostrando lo que diría el overlay cada vez que cambia. `--guide` suma los que hay y la guía; `--until m:ss` corta en ese minuto; `--civ` y `--slot` fijan la civ y el número de jugador.
- `coach <registro.jsonl.gz> [--guide]`: reproduce una partida ajena sin PNG ni juego (el registro trae la BO, la civ y el idioma).
- `registros <carpeta>`: resume cada registro de partida que llega (qué se leyó, las marcas, las notas y qué hay que mirar).
- `simulate <bo.json> [--food]`: simula una BO; con `--food`, la comida se agota.
- `autocalibrate <pantalla.png> --read`: calibra sobre una captura entera y muestra lo que lee.
- `loading <pantalla.png> --nombre "tu nombre"`: lee la pantalla de carga.
- `bench`: cuánto tarda cada parte de la lectura.
- `glyphs`, `font`, `read`, `ocr`, `icons`, `iconlib`, `queue`, `teach`: armar y probar letras e íconos (ver `.claude/skills/armar-letras-e-iconos/SKILL.md`).

Con el juego en castellano, `--idioma es` o `--idioma mx`.

## Regenerar los datos del juego (una vez por parche)

`data/game_data/v<versión>.json` sale del `.dat` del juego con un script de Python que no se usa durante la partida. El mismo script arma las frases del log de eventos con los textos del juego: `data/events.json` (inglés), `data/events.es.json` (castellano de España) y `data/events.mx.json` (de Latinoamérica), con los mismos ids.

```bash
py -3.12 -m venv tools/extractor/.venv
tools/extractor/.venv/Scripts/python -m pip install -r tools/extractor/requirements.txt
tools/extractor/.venv/Scripts/python tools/extractor/extract.py
```

La ruta del juego sale de `--dat`, de la variable `AOE2DE_PATH` o de las bibliotecas de Steam. Para agregar entidades, sumalas a `ENTIDADES` en `extract.py` por su id del `.dat`. Más detalles en [docs/dev/datos-del-juego.md](docs/dev/datos-del-juego.md).

## Estructura

| Carpeta | Target | Qué tiene |
|---|---|---|
| `src/BoCoach.Core` | `net10.0` | Lógica pura: datos del juego, build orders, motor y economía. Sin dependencias de Windows. |
| `src/BoCoach.Vision` | `net10.0-windows10.0.19041.0` | Captura de pantalla, OCR y suavizado. |
| `src/BoCoach.App` | `net10.0-windows10.0.19041.0` | App WPF: el tablero, el overlay, la calibración, los atajos y la conexión con la web. |
| `src/BoCoach.Wasm` | `net10.0` (browser-wasm) | El `Core` en WebAssembly para la web. |
| `web` | Next.js | Biblioteca y editor web (Supabase + Vercel). Ver [web/README.md](web/README.md). |
| `tests/BoCoach.Core.Tests` | `net10.0` | Tests de `Core`, incluido uno que verifica que no dependa de Windows. |
| `tests/BoCoach.Vision.Tests` | `net10.0-windows10.0.19041.0` | Tests de `Vision`, contra capturas reales. |
| `tests/BoCoach.App.Tests` | `net10.0-windows10.0.19041.0` | Tests de los ViewModels, el overlay y la sesión de coach. |
| `tests/fixtures` | | Golden, capturas y guías de terceros para los tests. |
| `tools/ReplaySession` | `net10.0-windows10.0.19041.0` | Corre todo sobre grabaciones y registros. |
| `tools/extractor`, `tools/stats` | Python | Datos del juego y estadísticas de cruces, offline. |
| `tools/mod` | PowerShell | Respaldo del instalador del mod. |
| `data` | | Datos que usa la app: `game_data`, eventos, letras, íconos, diseños del HUD, mapas, estilos. |
| `build_orders` | | Las BO de ejemplo y las de cada civ. |
| `docs` | | Ver el [índice](docs/README.md). |
