# App para el público: atajos editables, modo desarrollador y BO desde la web

Fecha: 2026-10-30. Pedido del usuario: "agregaría un botón para ver los atajos de teclado, y poder editarlos. Y me
gustaría que lo que se pueda manejar desde la web quede allá, repartir responsabilidades, y en todo caso que haya un
conjunto de información u opciones que queden para desarrolladores, la versión actual no la veo viable para un
release al público".

Maqueta de la ventana: `2026-10-30-app-publica-maqueta.png` (izquierda, el público; derecha, con el modo
desarrollador, lo naranja). Muestra la ventana al terminar las cuatro partes.

## Problema

- Los 12 atajos (Ctrl+Alt + letra) están fijos en `MainWindow.RegisterHotkeys`; si otro programa usa uno, solo hay un
  aviso en la barra de estado. Los textos de ayuda los nombran escritos a mano.
- La ventana mezcla lo que necesita un jugador con herramientas de diagnóstico (grabar, captura de diagnóstico,
  calibrar a mano, "Lo que ve el coach") y con un editor de BO completo que repite lo que ya hace la web.
- La web tiene biblioteca, editor y cuentas, pero la app no depende de ella: se puede usar sin pasar nunca por la
  web.

## Decisiones (del usuario, 2026-10-30)

1. Reparto A: la app deja de editar y de tener biblioteca propia. Crear, importar, pegar y editar BO se hace en la
   web. "Es lo que nos permite volvernos más indispensables para el usuario".
2. En la web cada uno elige al crear si la BO es pública o privada, y se suscribe a BO de otros desde la biblioteca.
3. La app es una caché: pide la lista de tus BO y tus suscriptas, baja cada BO recién cuando la va a usar y guarda
   las usadas. Las que no usó no las tiene. No vienen BO dentro de la app. Las de `%APPDATA%\BoCoach\build_orders` no
   se migran. La BO se elige en automático (la sugerida) o a mano entre las de la lista.
4. Modo desarrollador escondido: `"dev_mode": true` en `settings.json` o 5 clics sobre la versión. El usuario graba
   siempre sus partidas para que se revise el coach: se suma "Grabar cada partida sola".
5. Atajos: una ventana con la lista, editables, aprobada tal cual se propuso (sección 1).
6. Orden: de lo chico a lo grande (atajos, modo desarrollador, web, app como caché). Un spec y un plan por parte;
   cada parte sale como versión jugable y la app sigue andando como hoy hasta la parte 4.

## Parte 1: atajos editables

**Ventana "Atajos"** (botón en la barra de arriba): un renglón por acción con su nombre, su combinación y
"Restaurar"; abajo "Restaurar todos". Las acciones de desarrollador solo se listan con el modo prendido.

Acciones y combinación de fábrica:

| Id | Acción | Fábrica | Desarrollador |
|---|---|---|---|
| `start_stop` | Empezar o detener el coach | Ctrl+Alt+S | no |
| `advance` | Ya está | Ctrl+Alt+→ | no |
| `back` | Atrás | Ctrl+Alt+← | no |
| `skip` | Saltear | Ctrl+Alt+↓ | no |
| `pause` | Pausa | Ctrl+Alt+P | no |
| `overlay` | Ocultar el overlay | Ctrl+Alt+H | no |
| `cursor_reminders` | Recordatorios en el mouse | Ctrl+Alt+M | no |
| `accept_suggestion` | Usar la BO sugerida | Ctrl+Alt+B | no |
| `calibrate` | Calibrar a mano | Ctrl+Alt+C | sí |
| `record` | Grabar | Ctrl+Alt+R | sí |
| `debug` | Debug del overlay | Ctrl+Alt+D | sí |
| `diagnostic` | Captura de diagnóstico | Ctrl+Alt+X | sí |

**Cambiar una:** clic en la combinación, "Apretá la nueva…", se toma la primera tecla que no es modificador con los
modificadores apretados. Reglas:

- Tiene que llevar Ctrl, Alt o Win (una tecla sola se la robaría al juego). Escape cancela.
- Si la usa otra acción: el renglón queda en rojo con "Ya la usa: <acción>" y no se guarda.
- Se registra en el momento; si Windows no la deja (otro programa, por ejemplo las placas Intel con Ctrl+Alt+flecha):
  "Otro programa ya usa <combinación>" y vuelve a la anterior.

**Guardado:** `settings.json`, objeto `hotkeys` con id → combinación en texto (`"advance": "Ctrl+Alt+Right"`). Solo
se guardan las cambiadas; lo que falta o no se entiende toma la de fábrica.

**Textos:** tooltips, Primeros pasos, barra de estado y mensajes del overlay ("<combinación> para usarla") piden el
texto al mapa de atajos y se actualizan al cambiarlo.

**Código:**

- `App/Hotkeys/HotkeyAction`: la tabla de arriba (id, nombre, combinación de fábrica, si es de desarrollador).
- `HotkeyBinding`: combinación (modificadores + tecla) a texto y de texto a combinación.
- `HotkeyMap`: fábrica + `settings.json`, repetidas, texto de cada acción. Lógica pura.
- `GlobalHotkeys`: suma `Unregister(id)` y `Replace(id, combinación)` (si la nueva falla, vuelve a la vieja).
- `MainWindow.RegisterHotkeys` arma todo desde `HotkeyMap`.

**Tests:** texto ida y vuelta de cada combinación; repetidas; sin modificador se rechaza; `settings.json` roto da las
de fábrica; restaurar deja la de fábrica.

## Parte 2: modo desarrollador

**Cómo se prende:** `"dev_mode": true` en `settings.json`, o 5 clics sobre el número de versión en menos de 3 s (la
barra de estado dice "Modo desarrollador prendido" y queda guardado; otros 5 lo apagan). Sin reiniciar.

**Lo que ve el público:**

- Barra de arriba: Empezar coach, Atajos, Primeros pasos, Mod del HUD, Cuenta y Apoyar.
- Ajustes: Tu nombre, Civ, Idioma del juego, Máx. aldeanos, Elo, Recordatorios en el mouse y Coach automático.
- "En la partida": la línea elegida, Atrás, Ya está, Saltear y el paso actual en un renglón (con dos monitores es la
  forma de corregir al coach sin atajos).
- El informe de fin de partida.

**Lo que suma el modo desarrollador:** Calibrar a mano, Grabar, Grabaciones, Captura de diagnóstico, "Grabar cada
partida sola", el panel "Lo que ve el coach" (lecturas, eventos, aldeanos y GD contra la BO) y los atajos de
desarrollador. Con el modo apagado esos atajos no se registran.

**No cambia:** las grabaciones y diagnósticos que ya existen, la calibración sola (para todos) y el informe.

**"Grabar cada partida sola"** (`auto_record` en `settings.json`, solo con el modo prendido): arranca cuando aparece el
reloj de una partida nueva (las mismas señales que el coach automático: pantalla de carga o reloj que vuelve atrás);
corta al terminar la partida (30 s sin reloj con el juego al frente), al cerrarse el juego o al detener el coach. Si
ya se grababa a mano no hace nada. Respeta la pausa con menos de 1 GB libre.

**Código:** `CoachSettings.DevMode` y `AutoRecord`; `MainViewModel.DevMode` para las visibilidades del XAML;
`VersionClicks` (cuenta clics en una ventana de tiempo); `AutoRecorder` (decide arrancar o cortar con lo que avisa
`LiveCoach`).

**Tests:** 5 clics rápidos prenden y 4 no (ni 5 lentos); `AutoRecorder` arranca con el reloj de una partida nueva,
corta al fin de partida y al detener, no toca una grabación manual; con el modo apagado no se registran los atajos de
desarrollador.

## Parte 3: la web

**Visibilidad:** columna `visibility` (`public` o `private`, por defecto `public` para lo ya publicado) en
`build_orders`. El permiso por fila de lectura pasa a "pública o tuya". Una privada no aparece para nadie más en la
biblioteca, en `/bo/<código>` (404), en el sitemap, en la API ni en los votos (no se puede votar una privada ajena).
Las consultas con caché (`"use cache"`) solo traen públicas; lo propio se lee sin caché. En `/subir`, al guardar, se
elige Pública o Privada (marcada Privada al empezar); se puede cambiar desde "Mi cuenta". No hay "privada con
enlace".

**Suscripciones:** tabla `subscriptions` (`user_id`, `build_order_id`, `created_at`, clave las dos primeras, borrado
en cascada con la BO). Cada uno ve, crea y borra solo las suyas, y solo a BO públicas. Las propias no se suscriben:
están siempre en la lista de la app. Botón "Usar en el coach" / "En tu coach ✓" en el detalle y en las tarjetas de la
biblioteca; "Mi cuenta" lista las suscripciones. Una suscripta que se borra o pasa a privada sale de la lista.

**Datos para elegir sin bajar la BO:** columnas `map` (del JSON) y `style` (`BuildOrderStyle` del `Core`, calculado
en el navegador al publicar, como los clics de edad; también la app con `--publish`). Las ya publicadas se completan
una vez con un script.

**Endpoints para la app** (token personal, como `POST /api/bo`):

- `GET /api/app/mis-bo`: tus BO y tus suscriptas, sin pasos: código, nombre, civ, mapa, estilo, etiquetas, autor,
  `own`, `visibility`, `updated_at`, clics de edad. Sin token o con uno inválido, 401.
- `POST` y `DELETE /api/app/suscripciones/<código>`: suscribirse y desuscribirse.
- `GET /api/bo/<código>` (ya existe): acepta además el token, para bajar las privadas propias.

**Importar en el editor web:** el menú "Cargar" de `/subir` suma "Importar de buildorderguide.com o RTS Overlay"
(pegar o archivo) y "Pegar JSON de BO". El Wasm expone los importadores del `Core` (`RtsOverlayImporter`,
`BuildOrderGuideImporter`) y la web muestra sus avisos.

**"Conectar la app":** botón en "Mi cuenta" que crea un token y abre `bocoach://conectar/<token>` (parte 4).

**Base de datos:** una migración nueva, que corre el usuario en el SQL Editor de Supabase (pasos en
`docs/pendientes-usuario.md`). Hasta entonces la web publicada sigue como hoy.

**Tests (PGlite y Wasm):** una privada no la ve un anónimo ni otra cuenta; no se suscribe a una privada ajena; las
suscripciones son de cada uno; al borrar una BO se van sus suscripciones; `mis-bo` con token trae propias y
suscriptas, sin token 401; los importadores por el Wasm con las guías de `tests/fixtures`.

## Parte 4: la app como caché

**Conectar la cuenta:** `bocoach://conectar/<token>` (desde la web) guarda el token cifrado en `web.json` (DPAPI, como
hoy) y la app dice "Conectada como <nombre>". Pegar el token a mano sigue. Primeros pasos suma el paso. Sin cuenta, la
sección "Build order" dice "Conectá tu cuenta para elegir BO" con un botón a la web.

**Caché** en `%APPDATA%\BoCoach\cache`:

- `mis-bo.json`: la última lista. Se pide al abrir la app, al empezar el coach y con "Actualizar lista"; sin
  internet, la guardada.
- `bo/<código>.json`: cada BO usada con su `updated_at`. Al usarla, si la lista trae una fecha más nueva se baja de
  nuevo; sin internet, la guardada.
- La lista muestra "guardada" o "se baja al usarla".

**Elegir la BO** (`bo_mode` en `settings.json`: `auto` por defecto o `manual`; la última usada por código):

- Automática: en la pantalla de carga, la de más puntaje de la lista (mismo puntaje que hoy, sobre
  `BuildOrderSummary`: civ, mapa, etiquetas, estilo). Si no está guardada se baja ahí; si falla, la mejor de las
  guardadas; si no hay ninguna, el coach sigue sin BO (casa, centro urbano parado, inactivos y GD por lo que hay, como
  con la BO abandonada).
- Manual: se elige de la lista y se baja al elegirla. En la carga aparece la sugerida con su atajo, como hoy.

**Sumar BO desde la app:** "Pegar código" suscribe (`POST /api/app/suscripciones/<código>`) y la baja; los enlaces
`bocoach://import/<código>` hacen lo mismo con la confirmación de nombre y autor; "Buscar más BO en la web" abre la
biblioteca.

**Se va de la app:** `BuildOrderLibrary` (biblioteca, carpeta y papelera), la carpeta `Editor` (editor WPF), Nueva,
Importar, Pegar BO, Exportar, Publicar y la copia de `build_orders` a `data/`. Antes de borrar el editor se pone la
etiqueta de git `editor-wpf` para poder recuperarlo.

**Se queda:** `build_orders/civs` en el repo (tests y web), `--publish` como comando, ReplaySession leyendo BO de un
archivo. La carpeta vieja de la biblioteca queda en su lugar y se ignora.

**Core:** `BuildOrderSummary` y `BuildOrderStyle`; `BuildOrderSuggester.Score` y `Suggest` trabajan con resúmenes
(`BuildOrderSummary.Of(bo)` para las BO enteras).

**Tests (servidor falso con `HttpMessageHandler`):** la lista se guarda y sin internet se usa la guardada; una BO se
baja una vez y se rebaja solo si cambió; la automática con y sin internet y sin ninguna guardada; pegar un código
suscribe y baja; el puntaje por resumen da lo mismo que por BO entera en las 124 por civ.

## Fuera de esta etapa

- Subir el informe de cada partida a la cuenta (historial en la web): anotar en `PLAN.md` como fase siguiente.
- BO privadas compartibles por enlace.
- Usar la app sin cuenta con suscripciones locales.
