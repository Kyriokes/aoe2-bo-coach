# Plan: la partida de catálogo

Una partida contra la IA, sin apuro, para que el coach vea en pantalla todo lo que puede pedir una BO. Se juega cuando vuelvas a casa, antes de la beta.

## Qué cambió el 3/11

La biblioteca ya no necesita esta partida para conocer los íconos: desde la v0.9.87 los saca de los archivos del juego (543 unidades y tecnologías, teñidos como los tiñe el juego). En 4 partidas grabadas la cola vio todas las tecnologías que avisó el log, incluidas 8 que no tenían ninguna muestra real (minería de oro, sierra de arco, arado pesado, anillo del pulgar, las dos armaduras de arqueros, balística y hombre de armas). Ver `CLAUDE.md`.

Esta partida ahora sirve para **comprobar** lo que no pude medir con lo grabado:

| Qué | Por qué falta |
|---|---|
| **Otro color de jugador** | Todas las grabaciones que hay son en azul. Las celdas del color del jugador no se comparan, pero nunca se probó con otro. |
| **Los íconos en amarillo** | El tinte de espera borra casi todo el color: es donde más se confunde. |
| **El OCR del log** | Las 594 frases salen de los archivos de texto del juego, pero muchas nunca se leyeron en pantalla. |
| **Unidades de los edificios que nunca usaste** | Muelle, taller de asedio, monasterio y mercado no aparecen en ninguna grabación. |

Las unidades únicas de cada civ también están en los archivos y no hace falta producirlas.

## Antes de empezar

1. Abrí la app y fijate que arriba diga la última versión.
2. Dejá prendidos "Mod mínimo" y "Grabar cada partida sola", como los tenés ahora (Ajustes, pestañas Juego y HUD y Desarrollador).
3. No cambies de mods entre esta partida y las otras. La biblioteca se armó con Villager Highlighter prendido.
4. En el juego: un jugador contra una IA fácil, **persas** (tienen casi todo), **color rojo** (no azul), juego en inglés, resolución 1920×1080 en "Full screen", velocidad normal.
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

## Una prueba más, cortita: sin Villager Highlighter

Ese mod cambia el ícono del aldeano, y la mayoría de los jugadores no lo tiene. Apagalo en Mods instalados, jugá 3 minutos sacando aldeanos con un telar en el medio, y volvé a prenderlo. Sin esto no sé si el coach ve bien el aldeano de un jugador sin el mod, y de ese ícono depende el aviso de centro urbano parado.

## Lo que hago yo con la grabación

Me avisás y no tenés que mirar nada:

1. **Cola:** comparo lo que leyó con el orden de esta lista y con los renglones del log. Lo que confunda se corrige con una muestra real (`ReplaySession teach`) o afinando los tintes.
2. **Log:** comparo lo que leyó el OCR contra esta lista, que dice en qué orden tenía que aparecer cada mensaje. Lo que no se reconozca va a los tests.
3. **Fixture:** dejo capturas de cada ícono en cada estado en `tests/fixtures`, para que un cambio futuro no rompa nada sin que se note.
4. **Contra las partidas viejas:** vuelvo a correr las grabaciones que ya tenemos, para comprobar que los íconos nuevos no confundan a los conocidos.
