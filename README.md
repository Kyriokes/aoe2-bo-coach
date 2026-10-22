# aoe2-bo-coach

Overlay liviano para Age of Empires II: Definitive Edition que lee la pantalla con OCR, sigue una build order, se adapta cuando el jugador se desvía y sugiere cómo redistribuir aldeanos.

Solo lee la pantalla: no lee memoria del proceso, no inyecta código y no envía inputs al juego.

El diseño completo y las fases están en [PLAN.md](PLAN.md).

## Descargar

Cada versión publicada está en [Releases](https://github.com/Kyriokes/aoe2-bo-coach/releases): un `.zip` con `BoCoach.exe` (trae .NET adentro, no hay que instalar nada) y la carpeta `data`. Cada corrida del CI también deja la versión portable como artefacto (14 días). Para publicar una versión se sube una etiqueta `v0.9.0`: el workflow `release.yml` arma el `.zip` y la release.

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

La biblioteca se guarda en `%APPDATA%\BoCoach\build_orders`. Trae una BO por civ importada de [buildorderguide.com](https://www.buildorderguide.com) y su versión mejorada con el simulador del coach (misma estrategia, otro reparto u orden de pasos), además de los ejemplos; las que agrega una versión nueva se suman solas, y el filtro "Civ" ayuda a encontrarlas. "Importar…" también lee las guías de esa página. Cada BO puede decir para qué mapa es (lista "Mapa", `data/maps.json`): el validador avisa si usa pesca o el muelle donde no hay agua. Atajos: Ctrl+1 a Ctrl+4 agregan aldeanos, construcción, investigación o movimiento; Ctrl+D duplica el paso; Alt+↑/↓ lo mueve; Ctrl+Supr lo quita; Ctrl+S guarda. Los pasos también se reordenan arrastrando el ícono ⋮⋮.

Si te sirve, podés **invitarme un café** desde la web (sección "Apoyar el proyecto": Cafecito, PayPal, Ko-fi o Mercado Pago) o con el botón "Apoyar ☕" de la app. Cada aporte se anota en `data/donaciones.json`, a la vista, y la web muestra cuánto falta para cada meta.

En la web también se puede **armar una BO** ([/subir](https://aoe2-bo-coach.vercel.app/subir)): una paleta de pasos que se arrastran a una línea de tiempo, con el stock de recursos arriba y cada paso en su lugar simulado por el mismo motor de la app (en WebAssembly). Una caja no se puede mover a donde el juego no la deja (una unidad antes del clic de su edad, el clic sin sus dos edificios, lo que se entrena antes de su edificio) y, dentro de lo posible, se pega al momento en que hay recursos; un cartel dice por qué. Lo armado se publica en la biblioteca o se baja como `.json` para la app.

Para verificar la interfaz sin abrirla: `BoCoach.exe --screenshot salida.png [--bo nombre | --open archivo.json] [--select número]` la dibuja en un PNG con una biblioteca temporal.

## Mod del HUD, calibración y grabar partidas

La primera vez se abre **Primeros pasos** (se vuelve a abrir con el botón de la barra): idioma del juego, instalar el mod, una partida rápida contra 7 IA (mientras tanto la app mira la pantalla: en la pantalla de carga lee la lista de jugadores y, ya en la partida, se calibra sola con el HUD) y elegir tu nombre de esa lista.

En la barra de arriba de la app:

- **Instalar mod del HUD** agrega al juego el mod local "BoCoach HUD": los aldeanos de cada recurso en cuadrados negros con una franja de color y el reloj más grande, que el coach lee mucho mejor. Se arma en la PC del jugador a partir de los paneles del juego (no se redistribuye nada) y se puede volver a tocar sin problema. Después hay que reabrir el juego y verlo activo en Mods.
- Con el mod, la pantalla **se calibra sola** al empezar el coach, a cualquier resolución y tamaño de HUD: busca las franjas de color, mide la escala del HUD y lleva cada captura a 1080p antes de leerla.
- **Calibrar pantalla…** (o **Ctrl+Alt+C** desde el juego) es la calibración a mano, para jugar sin el mod: una capa transparente sobre la ventana del juego con un rectángulo por región (madera, comida, oro, piedra, población, inactivos, edad, reloj, log de eventos, cola de producción y puntajes). Se arrastran y se estiran; debajo de cada uno se ve en vivo lo que lee el OCR. Se guarda en `%APPDATA%\BoCoach\regions.json`, relativo a la ventana del juego, así anda en cualquier monitor y en modo ventana.
- **Grabar partida** (o **Ctrl+Alt+R** desde el juego; arriba aparece "● Grabando") guarda las regiones 2 veces por segundo en `%LOCALAPPDATA%\BoCoach\recordings\<fecha>`: `session.json`, `frames.csv` y un PNG por región y captura. Sirve para probar el OCR sin volver a jugar.

## Usar el coach durante una partida

1. En la segunda fila de arriba, los ajustes de la partida: **Tu nombre** (como sale en la lista de jugadores de la pantalla de carga: ahí el coach lee tu civ y contra quién jugás, y empieza la partida de cero), **Civ** (la que jugás si no se vio la pantalla de carga; el coach usa sus bonificaciones), **Idioma del juego** (inglés, castellano de España o de Latinoamérica: el log y la edad se leen con sus frases y su OCR), **Máx. aldeanos** y **Recordatorios en el mouse**. En la biblioteca de la izquierda, elegí la build order.
2. **▶ Empezar coach** (o **Ctrl+Alt+S** desde el juego). Arriba a la derecha del juego aparece el overlay: el paso actual en grande, qué falta para hacerlo, lo que está en curso (por ejemplo la Feudal, desde que se paga), el paso siguiente, los atrasados, el objetivo de la BO ("Feudal estimada en 9:12, objetivo 9:00") y la sugerencia de aldeanos ("Pasá 2 de madera a comida").
   **Coach automático** (casilla en la segunda fila, prendida por defecto): no hace falta tocar nada. Al abrir el juego, el coach arranca solo con la última BO que usaste; el overlay aparece recién en la pantalla de carga (si otra BO de tu biblioteca encaja mejor con tu civ, el mapa y los rivales, cambia sola y avisa; Ctrl+Alt+B vuelve a la tuya) o con el reloj de la partida; si hacés alt-tab se esconde y no lee nada hasta que volvés; a los 30 s sin reloj (pantalla de resultados) da la partida por terminada y muestra el informe; y al cerrar el juego se detiene. Si lo detenés a mano, no vuelve a arrancar hasta que abras el juego de nuevo.
   Cuando terminan los pasos, el coach sigue con la **postura de cierre** de la BO: qué unidades van siempre en cola (avisa si un edificio queda parado) y a qué edad apuntar con lo que sobra ("Edad de los Castillos: faltan 430 de comida", "Hacé clic en Edad de los Castillos"). Se arma en el editor (sección "POSTURA DE CIERRE"); si la BO no la trae, se deduce de sus pasos.
   **Informe de la partida:** al detener el coach (o al empezar otra partida), si se jugó al menos un minuto, se abre un resumen y se guarda el informe completo en `%LOCALAPPDATA%\BoCoach\informes`: cada paso contra el plan de la BO, los clics de edad, el centro urbano parado, los inactivos, las trabas por población y los recursos acumulados.
   **BO sugerida:** en la pantalla de carga, con tu civ, el mapa y los rivales, el overlay (que sin reloj baja a la esquina de abajo a la derecha) sugiere la BO de tu biblioteca que mejor encaja (el rival de enfrente pesa más que los demás, y cuenta si sos flanco o bolsillo) y da un consejo contra el rival de enfrente; Ctrl+Alt+B la usa. Las aperturas típicas de cada civ, qué le gana a qué y los consejos están en `data/civ_styles.json`.
   **Señales de la partida:** el coach avisa cuando el rival hace clic en una edad (lo ve en el panel de puntajes: su puntaje cae la décima parte de lo que cuesta), cuando te atacan y cuando perdés aldeanos. Si la BO trae una **rama** para esa señal (se arma en el editor con "Pasos de"), sus pasos van primero en el overlay; Ctrl+Alt+→ la cierra.
   Si dejás la BO (más de 5 unidades militares que la BO no tiene mientras la de la BO no sale hace un minuto, o 3 minutos sin avanzar ningún paso), el overlay dice "BO abandonada" y por qué, deja de pedir los pasos y se queda con lo básico: centro urbano produciendo, casas, inactivos y la guía de aldeanos contra lo que tenés.
3. El overlay no recibe el mouse ni el teclado. Los atajos (todos con Ctrl+Alt, que el juego no usa) lo corrigen sin salir del juego:

| Atajo | Qué hace |
|---|---|
| Ctrl+Alt+S | Empieza o detiene el coach |
| Ctrl+Alt+→ | Da por hecho el paso actual |
| Ctrl+Alt+← | Deshace lo último (un paso hecho, salteado o un clic mal detectado); con la BO abandonada, la retoma |
| Ctrl+Alt+↓ | Saltea el paso actual |
| Ctrl+Alt+P | Pausa o sigue la lectura |
| Ctrl+Alt+H | Oculta o muestra el overlay |
| Ctrl+Alt+D | Muestra CPU, RAM y milisegundos por captura |
| Ctrl+Alt+M | Prende o apaga los recordatorios junto al mouse (casa, centro urbano parado, inactivos) |
| Ctrl+Alt+B | Usa la BO sugerida en la pantalla de carga |
| Ctrl+Alt+X | Captura de diagnóstico: a los 2 s guarda la pantalla sin pérdida y un `.txt` con lo que encontró la app en `%LOCALAPPDATA%\BoCoach\diagnostico` (para mandar si algo no se lee bien) |

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

Escribe `lecturas.csv` (reloj, recursos, aldeanos, edad y eventos por captura) y avisa si alguna caja corta los números. Otros comandos (ver el comienzo de `tools/ReplaySession/Program.cs`): `coach` corre el loop completo (visión, motor y economía) con una BO y muestra lo que diría el overlay cada vez que cambia (también desde un `lecturas.csv`, sin OCR); `bench` mide cuánto tarda cada parte de la lectura; `glyphs` agrupa los caracteres de una región para etiquetarlos, `font` arma una letra con esas etiquetas, `read` lee una región y `ocr` pasa el OCR de Windows; `autocalibrate <pantalla.png> --read` calibra sobre una captura entera (por ejemplo la de diagnóstico) y muestra lo que lee; `loading <pantalla.png> --nombre "tu nombre"` lee la pantalla de carga (jugadores, equipos y civs). Con el juego en castellano, `--idioma es` o `--idioma mx`.

## Regenerar los datos del juego (una vez por parche)

`data/game_data/v<versión>.json` sale del `.dat` del juego con un script de Python que no se usa durante la partida. El mismo script arma las frases del log de eventos con los textos del juego: `data/events.json` (inglés), `data/events.es.json` (castellano de España) y `data/events.mx.json` (de Latinoamérica), con los mismos ids.

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
