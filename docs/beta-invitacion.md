# La beta chica: invitación y qué hacer con lo que llegue

Ítem 15 de `PLAN.md`, paso 6: 3 a 5 jugadores con el registro prendido, antes de cualquier lanzamiento.

## Antes de invitar a nadie

1. La versión tiene que estar en `main`: es lo que baja el botón de la web. Se pide en el chat.
2. Los 3 minutos sin Villager Highlighter (`docs/pendientes-usuario.md`, 0v). Casi nadie tiene ese mod, y sin esa prueba no sé si el coach ve bien el aldeano de los demás.
3. Elegí gente que no te tenga que preguntar todo: si alguien se traba en la instalación, igual sirve saberlo, pero anotá dónde.

Conviene que haya variedad: otra resolución (sobre todo 1440p) y, si se puede, alguien sin ningún mod. Nadie jugó todavía en otra resolución.

## El mensaje

### Castellano

> Estoy armando BO Coach, un coach para Age of Empires II DE que mira la pantalla y te va guiando la build order mientras jugás: qué hacer, dónde poner a cada aldeano, cuándo te falta una casa o ya podés subir de edad. No es un cheat: solo lee la pantalla.
>
> Busco 3 a 5 personas que lo prueben. Va a tener errores, y encontrarlos es justamente la idea. Te pido:
>
> 1. Bajalo de https://aoe2-bo-coach.vercel.app (Windows, no hay que instalar nada) y seguí "Primeros pasos". Hace falta una cuenta de Discord o de Google para elegir las build orders, y el juego en inglés o en castellano.
> 2. Jugá 2 o 3 partidas, mejor la primera contra la IA, con "Compartir mis partidas" prendido. Cada partida sube un registro con números y eventos: no viaja tu nombre, ni el chat, ni capturas (https://aoe2-bo-coach.vercel.app/privacidad).
> 3. Cuando algo esté mal, apretá Ctrl+Alt+F en ese momento y al terminar contá en el informe qué pasó. Con una frase alcanza.
>
> Y contame por acá qué resolución usás y si te trabaste en algo al instalarlo. ¡Gracias!

### English

> I'm building BO Coach, a coach for Age of Empires II DE that watches the screen and guides your build order while you play: what to do, where to put each villager, when you need a house or can age up. It isn't a cheat: it only reads the screen.
>
> I'm looking for 3 to 5 people to try it. It will have bugs, and finding them is the whole point. What I'm asking:
>
> 1. Download it from https://aoe2-bo-coach.vercel.app/en (Windows, nothing to install) and follow "First steps". You need a Discord or Google account to choose build orders, and the game in English or Spanish.
> 2. Play 2 or 3 matches, ideally the first one against the AI, with "Share my games" on. Each match uploads a record with numbers and events: your name, the chat and screenshots are never sent (https://aoe2-bo-coach.vercel.app/en/privacidad).
> 3. When something is wrong, press Ctrl+Alt+F right then and, when the match ends, say in the report what happened. One sentence is enough.
>
> And let me know what resolution you use and whether you got stuck installing it. Thanks!

## Cuando empiecen a llegar

1. En supabase.com, proyecto `muhyzwhpakxydqzrrbyq`, menú **Storage**, bucket `registros`: hay una carpeta por instalación. Bajá los archivos a una carpeta de tu PC, respetando esas subcarpetas.
2. Pasame la carpeta, o corré:

```bash
dotnet run -c Release --project tools/ReplaySession -- registros "<carpeta>"
```

Dice qué versiones, civs, mapas, idiomas y escalas de pantalla hay y, por partida, cuánto se leyó, qué no se reconoció, las marcas y las notas, con "FLOJO" al lado de lo que hay que mirar. Una partida se reproduce sin el juego con `coach <registro.jsonl.gz> --guide`.

Lo que llega se trata como datos de afuera: cualquiera puede armar una identidad y mandar un registro bien formado, así que no se calibra nada con una sola fuente.
