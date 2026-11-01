# Estado del HUD y del OCR (1 de noviembre de 2026, v0.9.76)

Resumen de cómo el coach lee la pantalla hoy, qué está verificado en partidas reales y qué problemas siguen abiertos sin una solución definitiva. Lo de fondo (decisiones, grabaciones de referencia, versiones) está en `CLAUDE.md`; acá va la foto de hoy.

## 1. Qué se lee y cómo

Todo sale de capturas de pantalla (2 por segundo), nunca de la memoria del juego. Cada región calibrada se lee con un método distinto:

| Región | Método | Estado |
|---|---|---|
| Stock de madera, comida, oro y piedra | Plantillas de dígitos (`hud_numbers`, 48 pt, blanco sobre negro del mod) | 100 % en el golden de la 20261025-024356 |
| Aldeanos por recurso y total | Plantillas (`hud_numbers`) sobre el casillero de color del mod | 100 % en el golden |
| Población "N/M" | Plantillas (`hud_numbers`, con la "/") | Bien; cerca del tope el juego la hace parpadear y el suavizado conserva el último valor |
| Inactivos | Plantillas sobre el campo negro del mod | Bien |
| Reloj | Plantillas `mod_clock` y, si no da una hora, `clock` (la de siempre) | Se lee en todas las capturas de las últimas partidas |
| Edad | OCR de Windows cada 2 s, confirmado con 3 lecturas iguales | Solo informativo: el coach saca la edad de los pasos y del log |
| Log de eventos | OCR de Windows sobre máscara de texto blanco, ×3, solo si cambió; Levenshtein contra `events.json` | 8,5 % de palabras mal sobre el fondo del mod (7,6 % sobre negro) |
| Cola de producción | Biblioteca de íconos de 33×33 (colores resumidos en 11×11), con el estado (avanza, espera, sin población) | Sin "?" en las dos últimas partidas; cada ícono nuevo hay que enseñarlo |
| Panel de puntajes | OCR de Windows sobre la banda del panel, jugador por el color del texto | 93 % de las capturas (antes 60 %) |
| Pantalla de carga | OCR del panel de jugadores, civ por texto, número por el color del cuadrado | Verificada contra la IA en inglés |

Qué se deduce de todo eso además de leerlo:

- **Edificios que el log no avisa** (dos iguales que terminan juntos): por la cola (N íconos de la misma unidad avanzando = N edificios), por las unidades que salen (más unidades iguales en media producción que edificios conocidos) y, desde la v0.9.76, por el puntaje (sube el 20 % del costo al terminar cada edificio).
- **Casas** que el log no avisa: por el tope de población.
- **Unidades militares** que salen a la vez de dos edificios: por "N/M" menos el total de aldeanos.
- **Investigaciones**: el clic por la baja del stock o por la cola; el fin por el log, o pasado el 90 % del tiempo desde que se la vio en la cola.
- **Edificios ocupados o parados**: por la cola (un ícono por edificio trabajando).

## 2. El mod del HUD

El panel se describe en `data/hud_layout.json` (unidades del juego) y `HudMod` arma los `widgetui/*.json` desde los originales del juego. Diseño actual (variante A, v0.9.75): log en el rincón de arriba a la izquierda, barra a su derecha con una celda por recurso (ícono del juego, casillero de color donde la app dibuja la guía dinámica, aldeanos y stock blancos sobre negro), cola debajo de la barra, reloj a 56 pt al final de la barra, menú del juego achicado al 70 %, todo un solo panel opaco del color de relleno de los paneles del juego (24, 21, 18) con el marco fino del juego (`BlackPanel`) solo en los bordes que dan al mapa.

Lo que el juego nos enseñó a los golpes (cada regla costó una prueba):

1. Un `FlatColorWithAlpha` en los estados por civ del fondo no se dibuja; en `StateNormal` sí.
2. Los planos (`ZPlane`) se suman por jerarquía y son globales entre paneles: el menú (plano 11) tapa lo que haya debajo.
3. El origen de una colección se mide en unidades de pantalla (3840 de ancho) y el `Anchor` y los tamaños en unidades del HUD.
4. La franja `blanktoppanel` y el marco de la civ del panel de recursos se pintan aunque sus estados estén en `None`: hay que taparlos o darles alto 0.
5. El `Box` de un `Surround` se dibuja desde el origen de su ventana y el juego recorta lo que se sale: sirve para esconder un lado del marco.
6. El `EventBackground` del log se achica a los renglones que hay: no sirve de fondo.
7. La translucidez del `BlackPanel` no se regula: son texturas de un atlas (75 % de alfa) y el material solo admite `Material` y `Wrap`.
8. Un parche del juego puede cambiar lo que se dibuja sin tocar los `widgetui` (el 27/10 desapareció el rectángulo negro del reloj).

Verificado en el juego hasta la v0.9.73 (grabación 20261101-020049) y la lectura de la v0.9.75 (grabación 20261101-125353). Sin verificar al píxel: el marco de la v0.9.75 (no hubo captura de diagnóstico), el recorte del `Box` hacia arriba y que el `None` apague el cuadro del log.

## 3. Problemas abiertos

Ordenados por cuánto afectan al coach. Cada uno dice qué se hizo, por qué no alcanza y qué haría falta.

### 3.1 Dos edificios iguales que terminan a la vez

El juego muestra un solo "X Built" si el mensaje ya es el último renglón del log, y no hay forma de verlo en el log: los mensajes no duran un tiempo fijo (uno solo dura unas 18 capturas; cuando llega otro, el de arriba se va unas 18 capturas después del nuevo), así que ni la duración delata la repetición.

Lo que hay: la cola (solo si el jugador pone a producir en los dos edificios), las unidades que salen (tarde, y solo con producción) y el puntaje (v0.9.76). El puntaje es la mejor señal, pero:

- vale solo para edificios de 150 o más (con los de 100, campamentos y molino, el ruido de lo que se paga y se junta en 3 s calza seguido);
- no sirve en los 10 s que siguen a un clic de edad (el puntaje baja el 10 % del costo y después rebota sin aviso), ni si el segundo edificio termina junto con la llegada a una edad;
- depende de que el panel de puntajes esté a la vista y se lea (93 % de las capturas), y de saber cuál es el renglón propio (de la pantalla de carga; si no, una votación que tarda unos minutos);
- la tolerancia es de ±25 % más 4 puntos, así que un edificio de 175 y uno de 150 se confunden si el último avisado fue el otro.

Haría falta una señal del juego que no existe (un aviso por edificio) o leer el panel del edificio seleccionado, que pide otra región y que el jugador lo seleccione.

### 3.2 La lectura del log

- Entre el 7 y el 9 % de las palabras salen mal; el matcher las corrige por parecido, pero un renglón con 3 letras mal puede caer en otro evento parecido ("Leather Archer Armor" contra "Padded Archer Armor").
- Los mensajes largos se parten en dos renglones y el primero puede quedar fuera de la caja ("Complete--" solo arriba de todo).
- Un mensaje que se desvanece a veces no se lee y al volver parecería nuevo: se tolera dando el log por vacío recién a las 5 capturas, con el riesgo de perder un evento repetido muy seguido.
- El aviso de ataque sale del color del jugador que ataca y la máscara de texto blanco no lo lee.
- Achicar las capturas empeora mucho la lectura (al 80 %, 26 % de palabras mal): no hay margen para un log más chico.
- En castellano, el corte en dos renglones del aviso de ataque es una suposición sin log real.

### 3.3 El reloj

- La letra `mod_clock` a veces lee el cero de la hora como 9 ("09:06:10"); la letra `clock` lo lee bien. Se prueban las dos y el suavizado no acepta saltos de más de 10 minutos, pero un reloj mal leído sostenido en pausa puede colarse.
- El texto del reloj es un solo `Label` ("hh:mm:ss (Normal - 1.7) (Estándar)"): no se puede partir en dos renglones sin cambiarle el tipo, y por eso el menú del juego se achicó.
- Con fondo translúcido la lectura se rompe (el "2" salía ":"): el fondo tiene que ser opaco, lo que decide el diseño del panel.

### 3.4 La cola de producción

- Cada unidad, tecnología o estado nuevo hay que enseñarlo a mano con `ReplaySession teach`; hasta entonces se lee como otra cosa (la Imperial salía Castillos o vacío; el arquero a caballo salía ballestero). No hay forma de saber de antemano qué íconos faltan.
- El tamaño de los íconos es fijo a 1080p; a otra escala se reescala la captura, sin verificar con una partida a otra resolución.
- Un ícono casi lleno de verde (investigación por terminar) se parece al fondo o a otro ícono.

### 3.5 El panel de puntajes

- Es translúcido: el fondo cambia con el mapa de atrás, y el recorte de la banda se apoya en umbrales medidos en dos partidas.
- Sin pantalla de carga (coach arrancado a mitad de partida, grabación sola), el renglón propio se vota con los avisos del log y puede tardar minutos o no decidirse.
- En partidas por equipos el panel crece hacia arriba y muestra "puntaje/el más alto del equipo": se acepta solo si el primero no pasa al segundo; con la calibración sola la caja cubre 8 renglones, sin verificar con más de 4 jugadores.
- El OCR todavía pierde un 7 % de las capturas y una lectura mala aislada (un dígito perdido) puede parecer un salto.

### 3.6 Resoluciones, escalas y proporciones

- Todo el diseño va en unidades del juego escaladas por la altura de la pantalla y la app mide la escala al calibrar, pero solo se probó a 1920×1080 (escala 1,0146 en la PC del usuario) y con una captura comprimida de un amigo (escala 1,09, panel negro de otra civ).
- Otra proporción de pantalla (21:9, 16:10) y "ventana" a otra resolución no se verificaron.
- La cola y el panel de puntajes se reescalan por la misma escala que el HUD: no se sabe si el juego los escala igual.

### 3.7 El HUD del juego en sí

- No hay documentación de `widgetui`: cada regla de la sección 2 se descubrió probando en el juego, y nada garantiza que el próximo parche no las cambie (ya pasó con el reloj el 27/10).
- Las texturas del panel cambian por civ (marrón, gris claro, negro): el mod tapa casi todo, pero lo que el juego pinte encima del mod se ve recién al jugar con esa civ.
- Opción B del diseño (dejar solo el cuadro de mensajes del juego, con fondo opaco propio) y pisar las 9 texturas `npanel` con copias opacas quedaron como alternativas sin probar.
- Desde la 20261017-002121 aparece una marquita del color del recurso en la esquina de las cajas, sin identificar.

### 3.8 Lo que depende de la partida o del jugador

- La pantalla de carga en castellano ("¿EQUIPO 1?") y en multijugador real no se vieron; el mapa de la pantalla de carga queda tapado por el overlay.
- El fin de partida (pantalla de resultados), un alt-tab real en "Full screen" y que la ventana del juego sea la ventana activa al jugar no se verificaron en vivo.
- El overlay propio sale en las grabaciones (decisión del usuario) y la guía dinámica tapa el medio de los casilleros: el detector los mide por los bordes.
- Algunas placas Intel se comen Ctrl+Alt+flechas; hay botones en el panel como respaldo.

## 4. Qué cerraría cada uno

| Problema | Qué haría falta |
|---|---|
| Edificios repetidos | Leer el panel del edificio seleccionado, o aceptar el margen actual (cola, unidades y puntaje) |
| Log | Un modelo de texto propio para la fuente del juego en vez del OCR de Windows, o un log más alto con renglones enteros |
| Reloj | Rearmar `mod_clock` con más ceros, o un solo lector con la letra de siempre |
| Cola | Enseñar los íconos de todas las unidades y tecnologías desde la instalación del juego (DDS), como se hizo con los de la web |
| Puntajes | Más partidas (equipos, otras civs) para fijar los umbrales; guardar el renglón propio en `settings.json` |
| Resoluciones | Una captura de diagnóstico a otra resolución y otra proporción |
| HUD del juego | Rehacer la prueba de las 8 reglas después de cada parche; una captura de diagnóstico por versión del mod |
