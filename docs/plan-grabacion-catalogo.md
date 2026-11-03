# Plan: la partida de catálogo

Una partida contra la IA, sin apuro, para que el coach vea en pantalla todo lo que puede pedir una BO. Se juega cuando vuelvas a casa, antes de la beta.

## Qué hace falta de verdad

Medido el 3/11 contra el repo:

| | Hay | Qué aporta la grabación |
|---|---|---|
| **Frases del log** | 594 (273 unidades, 270 tecnologías, 41 edificios, 10 avisos), sacadas de los archivos de texto del juego | No hace falta para tenerlas. Sirve para comprobar que el OCR las lee bien en pantalla. |
| **Íconos de la cola** | 20 cosas reconocidas de las 57 unidades y tecnologías de `game_data` | Es lo que realmente falta: 37 sin una sola muestra. |

Las unidades únicas de cada civ no entran: ninguna BO las pide todavía, y un ícono desconocido en la cola sale como "?" sin romper nada.

**Lo que más urge**, porque ya lo piden las 62 guías y hoy la cola no lo ve (entre paréntesis, cuántas veces aparece): minería de oro (17), sierra de arco (17), armadura acolchada para arqueros (12), hombre de armas (9), armadura de escamas (4), caballero (4), forja (2), arado pesado (2), monje (2) y hostigador (1).

## Una prueba que puede achicar la partida

Comparé los íconos de los archivos del juego con las muestras reales de la cola, usando la misma medida que usa el coach. **18 de 20 dan como más parecida su propia unidad o tecnología.** Las dos que fallan son el aldeano, que cambia por el mod Villager Highlighter, y la caballería ligera, que todavía no sé por qué falla.

Eso solo dice qué tan bien se reconoce cada ícono, no si ya alcanza. Muchas distancias quedan arriba del límite que usa el coach (0,09), porque en la cola los íconos se tiñen según su estado: verde al avanzar, amarillo al esperar, rojo sin población. Si esos tintes se pueden imitar, la biblioteca se arma entera desde los archivos, y esta partida pasa de enseñar a comprobar.

La partida se juega igual, pero rinde mucho más si el coach ya conoce los íconos: alcanza con mirar dónde se equivoca.

## Antes de empezar

1. Abrí la app y fijate que arriba diga la última versión.
2. Dejá prendidos "Mod mínimo" y "Grabar cada partida sola", como los tenés ahora (Ajustes, pestañas Juego y HUD y Desarrollador).
3. No cambies de mods entre esta partida y las otras. La biblioteca se armó con Villager Highlighter prendido.
4. En el juego: un jugador contra una IA fácil, **persas** (tienen 34 de las 37), juego en inglés, resolución 1920×1080 en "Full screen", velocidad normal.
5. Elegí un **mapa con agua**: hace falta el muelle.
6. Trucos en el chat: `cheese steak jimmy's` (comida), `lumberjack` (madera), `robin hood` (oro), `rock on` (piedra), +1000 cada uno. Solo andan contra la IA. Probá uno al arrancar y mirá que suba el recurso.

**No uses** `aegis` ni nada que haga instantánea la producción: la cola tiene que verse avanzar.

## Cómo jugarla

En cada edificio, tocá **todo lo de su renglón de una vez** y dejá que termine. Así salen las tres cosas que necesito: el primero avanza (verde), los otros esperan (amarillo) y al terminar cada uno aparece su renglón en el log.

No hace falta jugar bien ni defenderse. Dejá la parte de arriba a la izquierda sin tapar, y no abras ni la calibración ni otras ventanas encima del juego.

### Edad Oscura
Construí molino y muelle.
- **Muelle:** barco pesquero, barco de transporte.

Subí a Feudal.

### Edad Feudal
Construí cuartel, galería de tiro, establo, herrería y mercado.
- **Campamento minero:** minería de oro, minería de piedra.
- **Cuartel:** lancero, hombre de armas.
- **Galería de tiro:** hostigador.
- **Herrería:** forja, armadura de escamas, barda de escamas, armadura acolchada para arqueros.
- **Muelle:** galera, galera de fuego, balsa de demolición.
- **Mercado:** carro mercante.
- **Centro urbano:** vigilancia.

Subí a Castillos.

### Edad de los Castillos
Construí monasterio, universidad y taller de asedio.
- **Campamento maderero:** sierra de arco.
- **Molino:** arado pesado.
- **Campamento minero:** minería de piedra en pozo.
- **Establo:** caballero, camello.
- **Monasterio:** monje.
- **Taller de asedio:** mangonel, ariete, escorpión.
- **Galería de tiro:** anillo del pulgar.
- **Herrería:** armadura de cuero para arqueros.
- **Mercado:** acuñación.
- **Universidad:** balística.

Subí a Imperial.

### Edad Imperial
- **Campamento maderero:** sierra de dos hombres.
- **Molino:** rotación de cultivos.
- **Galería de tiro:** artillero manual, tácticas partas, arquero a caballo pesado.

### Para terminar: sin población
Llená el tope hasta quedarte sin lugar y poné un lote de cualquier unidad en dos edificios distintos. Dejalos unos segundos en rojo.

Después salí de la partida. La grabación queda sola en `%LOCALAPPDATA%\BoCoach\recordings`. Son unos 30 minutos.

## Lo que queda afuera

Los persas no tienen explorador águila, elefante de combate ni lancero estepario. Se hacen en partidas cortas aparte, si alguna vez hace falta: aztecas para el águila, una civ del sudeste asiático para el elefante, cumanos o tártaros para el lancero. Ninguna de las 62 guías los pide hoy, así que pueden esperar.

## Lo que hago yo con la grabación

Me avisás y no tenés que mirar nada:

1. **Cola:** agrupo los íconos con `ReplaySession icons` y etiqueto cada grupo, con la ayuda de los archivos del juego y de los renglones del log, que dicen qué era cada uno.
2. **Log:** comparo lo que leyó el OCR contra esta lista, que dice en qué orden tenía que aparecer cada mensaje. Lo que no se reconozca va a los tests.
3. **Fixture:** dejo capturas de cada ícono en cada estado en `tests/fixtures`, para que un cambio futuro no rompa nada sin que se note.
4. **Contra las partidas viejas:** vuelvo a correr las grabaciones que ya tenemos, para comprobar que los íconos nuevos no confundan a los conocidos.
