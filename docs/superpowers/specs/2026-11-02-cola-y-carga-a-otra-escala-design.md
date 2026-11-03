# La cola de producción y la pantalla de carga a cualquier escala

Estado: hecho el 2026-11-02, parte del camino al público (`PLAN.md`, sección 11, ítem 15, punto 3: "la cola de producción y la pantalla de carga solo a 1920×1080"). Verificado con capturas reescaladas y con una captura real a escala 1,09; ningún jugador lo probó todavía en vivo a otra resolución.

## 1. Qué se mide y con qué

Hay dos escalas distintas en la pantalla del juego, y cada lectura sigue una:

| Lectura | Unidades | Escala | Cómo se mide |
|---|---|---|---|
| Números del HUD (stock, aldeanos, "N/M", inactivos, reloj) | del HUD | `hud_scale` | la calibración sola: casilleros del diseño (`AutoCalibration.DetectLayout`, pendiente de las posiciones) o franjas del mod viejo (ancho medio / 36) |
| **Cola de producción** | del HUD | `hud_scale` | la misma: `technologyprogresspanel.json` ubica sus íconos en unidades del HUD (fondo en y 115 = 50 px a 1080p) y el ícono mide 33 px × escala (en la captura del amigo, escala 1,09, mide 36 px) |
| Log de eventos | del HUD (tamaño), sobre un origen en unidades de pantalla | `hud_scale` | la misma; el OCR de Windows tolera el tamaño (la máscara se agranda ×3) |
| Panel de puntajes | del HUD | `hud_scale` | la misma; verificado en la captura del amigo, se lee reescalado |
| **Pantalla de carga** | de pantalla | alto de la pantalla / 1080 | el área de cliente del juego: el panel de jugadores va en (170, 190, 335×740) a 1080p, escalado por el alto y centrado a lo ancho si la pantalla no es 16:9 (`LoadingScreen.Place`) |

`hud_scale` es cuánto más grande es el HUD que el del usuario a 1920×1080, sobre el que se armaron las letras y los íconos. Depende de la resolución **y** del tamaño de interfaz que elige el jugador, por eso se mide y no se calcula. La pantalla de carga no tiene tamaño de interfaz: solo resolución.

## 2. Cómo se normaliza

**Se reescala el recorte, no las plantillas.** Es lo que ya hacía el HUD (`CapturedImage.AtHudScale`: promedio de área al achicar, bilineal al agrandar) y lo que deja igual todo lo de hoy: a escala 1 no se toca nada.

- **Pantalla de carga:** ya funcionaba. `LoadingScreenReader.ReadPanelAsync` lleva el panel al alto de 1080p antes del OCR y `SquareColor` escala las columnas del cuadrado del jugador. Se capturó aparte (`Frame.Screen`, sin `AtHudScale`), que es lo correcto. Solo faltaban los tests a 900p y 2160p.
- **Cola:** la región ya pasaba por `AtHudScale`, y aun así fallaba por dos cosas:
  1. **La escala medida estaba corrida.** El diseño decía `pixels_per_unit_at_1080p` 0,4286 y el real es 0,4348 (178 px cada 410 unidades). El usuario a 1080p medía 1,0146, sin consecuencias porque cae dentro del 2 % que no se reescala, pero a 1440p medía 1,353 en vez de 1,333 y los íconos quedaban de 32,5 px. Ahora el diseño dice 0,4348 y la escala medida es la real. Las grabaciones guardan la suya en `session.json` y siguen igual.
  2. **Los íconos se ubican por píxel exacto**, cada 33 px, y la escala medida nunca es exacta (con las franjas del mod viejo, ±1,4 %). Una imagen que pasó por `AtHudScale` queda marcada (`CapturedImage.Rescaled`) y `IconLibrary.Locate` busca cada ícono ±2 px alrededor de donde cayó el anterior (el primero, de 0 a 2 px a la derecha). **Sin reescalar el código es el de siempre:** la búsqueda sobre las capturas a escala 1 cambiaba lecturas en la 20261101-020049 (un vecino parecido a "-" cortaba la cola), así que no corre ahí.

## 3. Cajas por defecto y overlay

- `AutoCalibration.Finish` arma las cajas de la cola, del log (diseño mínimo) y de los puntajes escalando las de `RegionLayout.Default` por `hud_scale`, con los puntajes anclados abajo a la derecha. Un test de punta a punta lo verifica a 900p, 1440p y 2160p sobre la captura real del usuario.
- Con el mod que corre la cola (`HudLayout.Queue`), la caja sale del diseño por el factor medido.
- El overlay dibuja la GD con las cajas de la captura actual (`CoachFrame.Regions`) y las franjas medidas en la captura a 1080p multiplicadas por `HudScale`. `--overlay-screenshot` simula una pantalla de 1920×1080 a escala 1 y no supone nada más. No hay otra lectura fija a 1080p.

## 4. La captura del amigo

Escala 1,09, panel negro, comprimida por WhatsApp. La calibración encontraba el HUD y la caja, y reescalado el ícono medía 33 px y caía en x 1, y 7: la cola no se leía por la biblioteca, no por la escala. Es un aldeano casi terminado con **"10" en cola**, y un número de dos cifras tapa más celdas que la esquina que no se compara (`ProductionQueue.Ignored`, 4×5). Con la búsqueda nueva salía "carretilla" (a 0,088, justo debajo del umbral), peor que no leer. Se le enseñó ese ícono a la biblioteca (160 íconos): la captura lee "villager" y las 7 grabaciones comparadas dan lo mismo.

## 5. Lo que sigue fijo o sin verificar

- **Nadie jugó todavía a otra resolución.** Los tests agrandan capturas de 1080p; un 1440p real tiene los íconos dibujados más nítidos, no interpolados.
- **Calibración manual:** deja la escala en 1, así que a otra resolución solo sirve la calibración sola. Sin el mod no hay calibración sola, y sin ella nada se lee a otra escala (el HUD tampoco).
- **Un HUD real dentro del 2 % de la escala 1** no se reescala y sus íconos no miden 33 px justos. No se baja el margen: las grabaciones viejas tienen `hud_scale` 1,0146 y se pondrían a reescalar. No se sabe si existe tal HUD: el tamaño de interfaz parece ir por saltos, sin verificar.
- **Un número de dos cifras en la cola** solo se conoce para ese aldeano, y sacado de una captura comprimida. Los demás íconos con 10 o más en cola se leerán mal o nada.
- **Log a escala 0,9:** el texto mide unos 9 px antes de agrandarlo; sin probar.
- **Pantalla que no es 16:9:** el panel de carga se centra a lo ancho; sin verificar en el juego. El mapa de la pantalla de carga sigue sin verificarse a ninguna escala.
