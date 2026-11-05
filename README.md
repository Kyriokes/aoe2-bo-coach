# aoe2-bo-coach

Un coach para Age of Empires II: Definitive Edition: mira la pantalla, sigue tu build order (BO) mientras jugás, se acomoda cuando te desviás y te dice dónde poner a cada aldeano.

Solo lee la pantalla: no lee la memoria del juego, no le inyecta código y no le manda teclas ni clics.

- **Web:** <https://aoe2-bo-coach.vercel.app>: la biblioteca de BO, el editor, las estadísticas de cruces y [qué viene](https://aoe2-bo-coach.vercel.app/roadmap).
- **Diseño y hoja de ruta:** [PLAN.md](PLAN.md).

Todavía no es la versión definitiva y casi nadie más la probó: es muy probable que encuentres algo roto. Cómo avisar está en [Si algo anda mal](#si-algo-anda-mal).

## Empezar a usarlo

Hace falta Windows 10 (2004 o posterior) u 11, y el juego en inglés o en castellano: en otro idioma el coach no entiende los mensajes del juego y confirma mucho menos.

1. Bajá el último `.zip` de [Releases](https://github.com/Kyriokes/aoe2-bo-coach/releases), descomprimilo donde quieras y abrí `BoCoach.exe`. Trae .NET adentro: no hay que instalar nada. Windows puede avisar "Windows protegió su PC" porque el programa no está firmado: tocá "Más información" y "Ejecutar de todas formas".
2. La primera vez se abre **Primeros pasos**, que te lleva por lo que hace falta: la cuenta de la web (ahí se eligen las BO), el idioma del juego, el mod del HUD, una partida rápida contra 7 IA para leer tu nombre y calibrar la pantalla, y si querés compartir tus partidas.
3. Jugá. Con **Automática** el coach arranca solo con el juego y, en la pantalla de carga, elige la BO para tu civ, el mapa y tu rival. Si querés una en particular, elegila en la lista. Al terminar la partida te queda el informe.

La ventana es un tablero: arriba, el cuadro grande dice en qué momento estás y qué tocar; abajo, tu cuenta, la BO, la partida y el informe. Lo demás está en **Ajustes** (nombre, Elo, civ, idioma, mod, cuenta).

## Qué hace durante la partida

Arriba a la derecha del juego aparece el overlay, que no recibe el mouse ni el teclado:

- **Las líneas:** un renglón por cada cosa que va en paralelo (aldeanos, construir, investigar, producir), con lo que falta para cada una. Lo crítico va arriba y en grande: "Hacé clic en Edad Feudal" en verde cuando ya se puede, "Casa: quedan N lugares" en rojo, con más margen cuantos más edificios estén produciendo.
- **Dónde poner a los aldeanos:** cuántos tendría que haber en cada recurso, comparado con los que hay. "Pasá N de X a Y" sale solo si en 60 s algo va a faltar para lo próximo de la BO.
- **Recordatorios junto al mouse:** casa, centro urbano parado e inactivos.
- **Lo que hace el rival:** por el panel de puntajes se da cuenta de cuándo hace clic en una edad, y avisa si te atacan o si perdés aldeanos. Si la BO trae una rama para esa señal, el coach la agrega.
- **Cuando dejás la BO:** el overlay dice "BO abandonada" y por qué, deja de pedir sus pasos y se queda con lo que siempre vale. Si volvés a seguirla, la retoma sola.
- **Cuando la BO termina:** queda una tarjeta chica con lo que falta: la próxima edad, los edificios de la postura parados, mover aldeanos y las tecnologías que te faltan.

En la pantalla de carga sugiere una BO con tu civ, el mapa y los rivales, y da un consejo contra el de enfrente. Al terminar, el **informe** compara cada paso con la BO y dice cuánto estuvo parado el centro urbano, cuánto estuviste sin casas y cómo viene ese cruce. Queda en `%LOCALAPPDATA%\BoCoach\informes`.

### Atajos

Todos con Ctrl+Alt, que el juego no usa. Se cambian en **Atajos**, arriba en la ventana.

| Atajo | Qué hace |
|---|---|
| Ctrl+Alt+S | Empieza o detiene el coach |
| Ctrl+Alt+→ | Da por hecho el paso de la línea elegida |
| Ctrl+Alt+← | Deshace lo último (un paso hecho, salteado o un clic mal detectado); con la BO abandonada, la retoma |
| Ctrl+Alt+↓ | Saltea el paso de la línea elegida |
| Ctrl+Alt+P | Pausa o sigue la lectura |
| Ctrl+Alt+H | Oculta o muestra el overlay |
| Ctrl+Alt+M | Prende o apaga los recordatorios junto al mouse |
| Ctrl+Alt+B | Usa la BO sugerida en la pantalla de carga |
| Ctrl+Alt+F | Algo anduvo mal: marca el momento en el registro de la partida |

## El mod del HUD

**Instalar mod del HUD** (Ajustes, pestaña "Juego y HUD", con el juego cerrado) agrega al juego el mod local "BoCoach HUD", que se arma en tu PC a partir de los archivos del juego. Por defecto es el **mod mínimo**: la barra del juego donde está, opaca, con los números blancos y una franja fina de color bajo cada ícono. Sin marcar "Mod mínimo" se instala el completo, que también corre el log y la cola de producción.

Con el mod, la pantalla se calibra sola al empezar el coach: busca las marcas de color, mide la escala del HUD y lleva cada captura a 1080p antes de leerla. Todavía nadie la probó jugando en otra resolución. Sin el mod, los aldeanos por recurso se estiman y hay que calibrar a mano.

Si venís de una versión anterior, volvé a instalarlo: el diseño cambia entre versiones.

## Si algo anda mal

1. En ese momento, **Ctrl+Alt+F** (o "Algo anduvo mal" en la ventana): queda marcado en el registro de la partida y el overlay lo confirma.
2. Al terminar, el informe pregunta qué pasó: con una frase alcanza ("me pidió una casa que ya tenía").
3. Si preferís, abrí un [aviso en GitHub](https://github.com/Kyriokes/aoe2-bo-coach/issues) con lo que viste y, si podés, una captura.

## Compartir tus partidas

"Compartir mis partidas para mejorar el coach" viene prendido y se apaga en Ajustes o en Primeros pasos. Al terminar cada partida de un minuto o más se sube un registro con números y eventos: lo que el coach leyó del HUD, lo que creyó y lo que te mostró, con tu civ, el mapa y la BO. No viaja tu nombre, ni los de los otros jugadores, ni el chat, ni capturas de pantalla. La nota del informe se sube también, si la escribís.

Con eso cada error se reproduce y se arregla sin que nadie tenga que jugar de nuevo. "Borrar lo compartido…" pide que se borre lo que ya subiste. Tu copia queda en `%LOCALAPPDATA%\BoCoach\registros`. Los detalles están en la [página de privacidad](https://aoe2-bo-coach.vercel.app/privacidad).

## La web

La app no tiene editor: las BO se arman y se eligen en [la web](https://aoe2-bo-coach.vercel.app), y la app guarda las que usás para que anden sin internet (`%APPDATA%\BoCoach\cache`).

- **Conectar cuenta** abre "Mi cuenta" en la web; ahí "Conectar la app" la conecta con un token.
- En la web, **Usar en el coach** suma una BO a tu lista. **Pegar código** hace lo mismo desde la app, con el código de 6 letras.
- En [/subir](https://aoe2-bo-coach.vercel.app/subir) se arma una BO con una paleta y una línea de tiempo, o se la genera con objetivos en la pestaña Asistido; cada cambio se simula con el mismo motor que la app, compilado a WebAssembly.

Hay una BO por civ importada de [buildorderguide.com](https://www.buildorderguide.com), más su versión mejorada con el simulador. Si te sirve, podés **invitarme un café** desde la web o con "Apoyar" en la app: cada aporte se anota en `data/donaciones.json`, a la vista.

El código de la web está en `web/` (ver [web/README.md](web/README.md)).

## Desarrollar

Hace falta el SDK de .NET 10 (la versión exacta está en `global.json`).

```bash
dotnet build BoCoach.slnx
dotnet test BoCoach.slnx
dotnet run --project src/BoCoach.App
```

Cada versión nueva que llega a `main` (la `<Version>` de `BoCoach.App.csproj` sin su etiqueta) se publica sola: `release.yml` arma el `.zip`, la release y la etiqueta, y el botón "Descargar para Windows" de la web baja esa. Cada corrida del CI también deja la versión portable como artefacto (14 días).

### Modo desarrollador

Cinco clics sobre el número de versión. Suma **Calibrar pantalla** (Ctrl+Alt+C: una capa con un rectángulo por región, para jugar sin el mod), **Grabar partida** (Ctrl+Alt+R: 2 capturas por segundo en `%LOCALAPPDATA%\BoCoach\recordings`) o "Grabar cada partida sola", **Captura de diagnóstico** (Ctrl+Alt+X: a los 2 s guarda la pantalla sin pérdida y un `.txt` con lo que encontró la app en `%LOCALAPPDATA%\BoCoach\diagnostico`), lo que ve el coach en el panel de abajo y CPU, RAM y milisegundos por captura en el overlay (Ctrl+Alt+D).

### Probar sin el juego

- Sobre una grabación: `BoCoach.exe --replay "<carpeta>" --bo <archivo.json> [--speed 4] [--stats uso.csv]`.
- El overlay en un momento de una partida: `BoCoach.exe --overlay-screenshot salida.png --readings lecturas.csv --bo <archivo.json> --at 8:20`.
- La ventana en un PNG: `BoCoach.exe --screenshot salida.png [--state sin-cuenta|esperando|en-partida|terminada|elegir] [--settings N] [--report] [--first-run] [--hotkeys] [--dev] [--lang en]`.
- Publicar BO con tu cuenta: `BoCoach.exe --publish <carpeta> [--dry-run]`.

### Probar la visión sobre una grabación (tools/ReplaySession)

Los números del HUD se leen por plantillas (`data/fonts`), sacadas de una grabación real; el log de eventos, con el OCR de Windows. Todo se puede correr sobre una grabación, sin el juego:

```bash
dotnet run --project tools/ReplaySession -c Release -- replay "%LOCALAPPDATA%\BoCoach\recordings\<carpeta>"
```

Escribe `lecturas.csv` (reloj, recursos, aldeanos, edad y eventos por captura) y avisa si alguna caja corta los números. Para lo que manda la gente, `registros <carpeta>` resume cada registro de partida (qué se leyó, las marcas y las notas) y `coach <registro.jsonl.gz> --guide` la reproduce sin PNG ni juego. Otros comandos (ver el comienzo de `tools/ReplaySession/Program.cs`): `coach` corre el loop completo (visión, motor y economía) con una BO y muestra lo que diría el overlay cada vez que cambia (también desde un `lecturas.csv`, sin OCR); `bench` mide cuánto tarda cada parte de la lectura; `glyphs` agrupa los caracteres de una región para etiquetarlos, `font` arma una letra con esas etiquetas, `read` lee una región y `ocr` pasa el OCR de Windows; `autocalibrate <pantalla.png> --read` calibra sobre una captura entera (por ejemplo la de diagnóstico) y muestra lo que lee; `loading <pantalla.png> --nombre "tu nombre"` lee la pantalla de carga (jugadores, equipos y civs). Con el juego en castellano, `--idioma es` o `--idioma mx`.

### Regenerar los datos del juego (una vez por parche)

`data/game_data/v<versión>.json` sale del `.dat` del juego con un script de Python que no se usa durante la partida. El mismo script arma las frases del log de eventos con los textos del juego: `data/events.json` (inglés), `data/events.es.json` (castellano de España) y `data/events.mx.json` (de Latinoamérica), con los mismos ids.

```bash
py -3.12 -m venv tools/extractor/.venv
tools/extractor/.venv/Scripts/python -m pip install -r tools/extractor/requirements.txt
tools/extractor/.venv/Scripts/python tools/extractor/extract.py
```

Por defecto lee `H:\Steam\steamapps\common\AoE2DE\resources\_common\dat\empires2_x2_p1.dat`; se cambia con `--dat`. Para agregar entidades, sumalas a `ENTIDADES` en `extract.py` por su id del `.dat`.

### Estructura

| Proyecto | Target | Qué tiene |
|---|---|---|
| `src/BoCoach.Core` | `net10.0` | Lógica pura: datos del juego, build orders, motor y economía. Sin dependencias de Windows. |
| `src/BoCoach.Vision` | `net10.0-windows10.0.19041.0` | Captura de pantalla, OCR y suavizado. |
| `src/BoCoach.App` | `net10.0-windows10.0.19041.0` | App WPF: el tablero, el overlay, la calibración, los atajos y la conexión con la web. |
| `src/BoCoach.Wasm` | `net10.0` (browser-wasm) | El `Core` en WebAssembly para la web. |
| `web` | Next.js | Biblioteca web (Supabase + Vercel). |
| `tests/BoCoach.Core.Tests` | `net10.0` | Tests de `Core`, incluido uno que verifica que no dependa de Windows. |
| `tests/BoCoach.Vision.Tests` | `net10.0-windows10.0.19041.0` | Tests de `Vision`. |
| `tests/BoCoach.App.Tests` | `net10.0-windows10.0.19041.0` | Tests de los ViewModels, el overlay y la sesión de coach. |

## Licencia

El código es [MIT](LICENSE). Lo que sigue no es parte de esa licencia:

- **Lo que sale del juego:** los íconos de `web/public/icons` y `data/icons`, las letras de `data/fonts`, las frases de `data/events*.json`, los datos de `data/game_data` y las capturas de `tests/fixtures`. Age of Empires II © Microsoft Corporation. AoE2 BO Coach se hizo bajo las [Game Content Usage Rules](https://www.xbox.com/en-US/developers/rules) de Microsoft con contenido de Age of Empires II, y no está respaldado por Microsoft ni afiliado a Microsoft.
- **Las guías de otros:** las de `tests/fixtures/buildorderguide` y `tests/fixtures/rts-overlay`, y las BO que salen de ellas en `build_orders`, son de sus autores (cada archivo dice de quién es).
- **Lo que viene adentro de la versión portable:** .NET, WPF y los demás componentes, cada uno con su licencia, en [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt).
