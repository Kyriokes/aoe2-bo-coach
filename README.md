# aoe2-bo-coach

Overlay liviano para Age of Empires II: Definitive Edition que lee la pantalla con OCR, sigue una build order, se adapta cuando el jugador se desvía y sugiere cómo redistribuir aldeanos.

Solo lee la pantalla: no lee memoria del proceso, no inyecta código y no envía inputs al juego.

El diseño completo y las fases están en [PLAN.md](PLAN.md).

## Requisitos para desarrollar

- Windows 10 (2004 o posterior) u 11
- SDK de .NET 10 (la versión exacta está en `global.json`)

## Compilar y correr los tests

```bash
dotnet build BoCoach.slnx
dotnet test BoCoach.slnx
```

## Abrir el editor de build orders

```bash
dotnet run --project src/BoCoach.App
```

La biblioteca se guarda en `%APPDATA%\BoCoach\build_orders` (la primera vez se copian las BO de ejemplo). Atajos: Ctrl+1 a Ctrl+4 agregan aldeanos, construcción, investigación o movimiento; Ctrl+D duplica el paso; Alt+↑/↓ lo mueve; Ctrl+Supr lo quita; Ctrl+S guarda. Los pasos también se reordenan arrastrando el ícono ⋮⋮.

Para verificar la interfaz sin abrirla: `BoCoach.exe --screenshot salida.png [--bo nombre | --open archivo.json] [--select número]` la dibuja en un PNG con una biblioteca temporal.

## Calibrar la pantalla y grabar partidas

En la barra de arriba de la app:

- **Calibrar pantalla…** (o **Ctrl+Alt+C** desde el juego) abre una capa transparente sobre la ventana del juego con un rectángulo por región (madera, comida, oro, piedra, población, inactivos, edad, reloj y log de eventos). Se arrastran y se estiran; debajo de cada uno se ve en vivo lo que lee el OCR. Se guarda en `%APPDATA%\BoCoach\regions.json`, relativo a la ventana del juego, así anda en cualquier monitor y en modo ventana.
- **Grabar partida** (o **Ctrl+Alt+R** desde el juego; arriba aparece "● Grabando") guarda las regiones 2 veces por segundo en `%LOCALAPPDATA%\BoCoach\recordings\<fecha>`: `session.json`, `frames.csv` y un PNG por región y captura. Sirve para probar el OCR sin volver a jugar.

## Usar el coach durante una partida

1. En la biblioteca de la izquierda, elegí la build order.
2. **▶ Empezar coach** (o **Ctrl+Alt+S** desde el juego). Arriba a la derecha del juego aparece el overlay: el paso actual en grande, qué falta para hacerlo, lo que está en curso (por ejemplo la Feudal, desde que se paga), el paso siguiente, los atrasados, el objetivo de la BO ("Feudal estimada en 9:12, objetivo 9:00") y la sugerencia de aldeanos ("Pasá 2 de madera a comida").
3. El overlay no recibe el mouse ni el teclado. Los atajos (todos con Ctrl+Alt, que el juego no usa) lo corrigen sin salir del juego:

| Atajo | Qué hace |
|---|---|
| Ctrl+Alt+S | Empieza o detiene el coach |
| Ctrl+Alt+→ | Da por hecho el paso actual |
| Ctrl+Alt+← | Deshace lo último (un paso hecho, salteado o un clic mal detectado) |
| Ctrl+Alt+↓ | Saltea el paso actual |
| Ctrl+Alt+P | Pausa o sigue la lectura |
| Ctrl+Alt+H | Oculta o muestra el overlay |
| Ctrl+Alt+D | Muestra CPU, RAM y milisegundos por captura |

Para probarlo sin el juego, sobre una grabación: `BoCoach.exe --replay "<carpeta de la grabación>" --bo scouts [--speed 4] [--stats uso.csv]`. Para ver cómo queda el overlay en un momento de una partida: `BoCoach.exe --overlay-screenshot salida.png --readings lecturas.csv --bo scouts --at 8:20`.

## Biblioteca web

La web (`web/`, ver [web/README.md](web/README.md)) es una biblioteca pública de BO: cada una muestra la línea de tiempo calculada por el mismo motor que la app (compilado a WebAssembly). Desde la app:

- **Desde la web…** (biblioteca) importa una BO con su código de 6 letras, el enlace «Abrir en BO Coach» o la dirección de su página.
- **Publicar en la web** (editor) la publica con tu cuenta, o la reemplaza si ya la habías publicado desde esta PC. La primera vez pide el token, que se crea en "Mi cuenta" de la web.
- **Cuenta de la web…** cambia la dirección de la web o el token, y registra los enlaces `bocoach://` para que el botón «Abrir en BO Coach» de la web abra la app directo.

## Probar la visión sobre una grabación (tools/ReplaySession)

Los números del HUD se leen por plantillas (`data/fonts`), sacadas de una grabación real; el log de eventos, con el OCR de Windows. Todo se puede correr sobre una grabación, sin el juego:

```bash
dotnet run --project tools/ReplaySession -c Release -- replay "%LOCALAPPDATA%\BoCoach\recordings\<carpeta>"
```

Escribe `lecturas.csv` (reloj, recursos, aldeanos, edad y eventos por captura) y avisa si alguna caja corta los números. Otros comandos (ver el comienzo de `tools/ReplaySession/Program.cs`): `coach` corre el loop completo (visión, motor y economía) con una BO y muestra lo que diría el overlay cada vez que cambia (también desde un `lecturas.csv`, sin OCR); `bench` mide cuánto tarda cada parte de la lectura; `glyphs` agrupa los caracteres de una región para etiquetarlos, `font` arma una letra con esas etiquetas, `read` lee una región y `ocr` pasa el OCR de Windows.

## Regenerar los datos del juego (una vez por parche)

`data/game_data/v<versión>.json` sale del `.dat` del juego con un script de Python que no se usa durante la partida.

```bash
py -3.12 -m venv tools/extractor/.venv
tools/extractor/.venv/Scripts/python -m pip install -r tools/extractor/requirements.txt
tools/extractor/.venv/Scripts/python tools/extractor/extract.py
```

Por defecto lee `H:\Steam\steamapps\common\AoE2DE\resources\_common\dat\empires2_x2_p1.dat`; se cambia con `--dat`. Para agregar entidades, sumalas a `ENTIDADES` en `extract.py` por su id del `.dat`.

## Estructura

| Proyecto | Target | Qué tiene |
|---|---|---|
| `src/BoCoach.Core` | `net10.0` | Lógica pura: datos del juego, build orders, motor y economía. Sin dependencias de Windows. |
| `src/BoCoach.Vision` | `net10.0-windows10.0.19041.0` | Captura de pantalla, OCR y suavizado. |
| `src/BoCoach.App` | `net10.0-windows10.0.19041.0` | App WPF: overlay, calibración, editor de BO, hotkeys y conexión con la web. |
| `src/BoCoach.Wasm` | `net10.0` (browser-wasm) | El `Core` en WebAssembly para la web. |
| `web` | Next.js | Biblioteca web (Supabase + Vercel). |
| `tests/BoCoach.Core.Tests` | `net10.0` | Tests de `Core`, incluido uno que verifica que no dependa de Windows. |
| `tests/BoCoach.Vision.Tests` | `net10.0-windows10.0.19041.0` | Tests de `Vision`. |
| `tests/BoCoach.App.Tests` | `net10.0-windows10.0.19041.0` | Tests de los ViewModels del editor, el overlay y la sesión de coach. |

## Licencia

[MIT](LICENSE)
