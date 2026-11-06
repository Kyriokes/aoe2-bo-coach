# Lo que queda de tu parte

Lista viva: cada sesión agrega lo nuevo y saca lo hecho. Va ordenada por lo que más destraba. La lista vieja, con las instrucciones de cada prueba, está en [`historial/pendientes-hasta-2026-11-05.md`](historial/pendientes-hasta-2026-11-05.md).

## 1. Después de publicar la revisión del 5/11

**Hecho el 6/11:** `safe.directory`, la sesión de GitHub en esta PC, la migración `20261105000000_revision.sql` (comprobado desde afuera: la base ya tiene las funciones nuevas) y el push a `main`. El CI y la release dieron verde, la v0.9.94 está en Releases con su `.zip` y su `.sha256`, y la web nueva está en producción. Si la consulta de control de la migración dio alguna fila, pasame cuál.

1. **Volvé a publicar las BO de chinos y mayas.** La simulación cambió para las civs que arrancan con aldeanos de más, y la biblioteca muestra los horarios de Feudal y Castillos que se calcularon al publicarlas. Con la app cerrada, en una terminal en F:\aoe2-bo-coach, y con un token creado en **Mi cuenta** → **Crear token** (el de "Conectar la app" no publica) pegado en Ajustes → Avanzado → **Dirección y token…**:

   ```bash
   src\BoCoach.App\bin\Release\net10.0-windows10.0.19041.0\BoCoach.exe --publish build_orders\civs --dry-run
   ```

   Si la lista se ve bien, corrélo de nuevo sin `--dry-run`. Reemplaza las publicadas con los mismos códigos y, como cambia el cuerpo de pocas, las apps solo vuelven a bajar esas.

2. **Probá lo que PGlite no puede probar** (hace falta Supabase de verdad). Si algo no da como dice acá, contame:
   - **Subir una partida:** jugá una partida de más de un minuto con "Compartir mis partidas" prendido. En Ajustes, pestaña Partida, tiene que decir "Registro de la partida enviado. ¡Gracias!". Si no, los registros quedan en cola en tu PC y no se pierden.
   - **Borrar:** tocá "Borrar lo compartido…" y aceptá. En Supabase, **Storage** → `registros`, ese archivo tiene que desaparecer.
   - **Votar:** votá una BO. El contador sube y la fecha "actualizada" no cambia.
   - **Autores:** en la biblioteca cada BO sigue diciendo de quién es.
## 2. Después de reinstalar Windows

En esta PC se perdieron `%APPDATA%\BoCoach` y `%LOCALAPPDATA%\BoCoach`: tus ajustes, tu cuenta en la app, las grabaciones y los informes.

1. Abrí la app y seguí **Primeros pasos**: cuenta, idioma, mod (con el juego cerrado) y una partida rápida contra 7 IA.
2. **Compilar:** los accesos directos viejos apuntan a `G:`. Para probar una versión compilada: `F:\aoe2-bo-coach\src\BoCoach.App\bin\Release\net10.0-windows10.0.19041.0\BoCoach.exe`.
3. Si querés volver a usar 400 ms para comparar (punto 3.1), está en Ajustes, pestaña Desarrollador (5 clics sobre la versión).
4. **`web/.env` tiene la contraseña de la base en texto plano.** No está en git ni estuvo nunca, y la web no la usa. Mové esa contraseña a un gestor de contraseñas y borrá el archivo.

## 3. Para arrancar la beta

Antes de invitar a nadie. El mensaje y qué hacer con lo que llegue están en [`beta-invitacion.md`](beta-invitacion.md).

1. **Comparar 400 y 500 ms.** (1) Jugá una partida con 400 ms y fijate si se siente más fluido. (2) Poné 500 y jugá otra parecida. (3) Avisame y comparo los consumos de `%LOCALAPPDATA%\BoCoach\uso`. Decidí cuál queda para todos: hoy, los demás tienen 500.
2. **Sin Villager Highlighter, 3 minutos.** Apagá ese mod en Mods instalados, jugá 3 minutos sacando aldeanos con un telar en el medio, en un color que no sea azul, violeta ni naranja, y volvé a prenderlo. Sin esto no sé si el coach ve bien el aldeano de un jugador sin el mod, que es lo que tiene casi todo el mundo.
3. **Mirar la app en inglés.** Arriba a la derecha está el selector de idioma: elegí English, aceptá que se reabra, mirá la ventana y los Ajustes, y volvé a Español. Sin elegir, la app sigue el idioma de Windows, y el tuyo está en inglés.
4. **Leer lo que habla en tu nombre.** `web/src/content/roadmap.es.json` y `roadmap.en.json` (al principio del archivo dice cómo cambiarlos) y la página `/privacidad`, que cambió con esta revisión. Antes de publicar en otros países, conviene que la revise alguien del rubro.
5. **Decime a quiénes invitás**, y mandales el mensaje recién con todo lo de arriba en `main`: la descarga de la web baja lo que está ahí.

## 4. Decisiones que necesito

1. **Datos de terceros en la historia de git.** La captura de un amigo tenía el overlay de Discord con nombres y avatares, su nick y la marca "Activar Windows". Ya está tapada (IN-01), pero sigue en commits viejos de un repo público. Además, 392 de los 1076 commits tienen tu Gmail como autor, el último del 23/10 (IN-18). Sacarlos pide reescribir la historia y forzar el push: cambia todos los hashes y rompe los enlaces a commits. **Mi recomendación:** preguntale a tu amigo; si le molesta, lo hago; si no, se deja. Y en GitHub (Settings → Emails) prendé "Block command line pushes that expose my email".
2. **Registros viejos.** Los subidos antes de esta revisión pueden traer chat o nombres leídos del log (VI-16), y la identidad con que se subieron se perdió al reinstalar, así que "Borrar lo compartido…" no los alcanza. Son solo partidas tuyas. **Mi recomendación:** bajalos si te sirven y borralos a mano: Supabase, **Storage** → `registros`.
3. **Permiso de buildorderguide.com** (IN-05). La biblioteca publica 124 BO importadas de ahí, con autor y enlace. No encontré un permiso anotado ni pude leer sus términos. **Mi recomendación:** escribiles antes de abrir la beta.
4. **Que la app avise si hay versión nueva** (IN-20). Hoy no avisa, así que un arreglo de seguridad no le llega a quien no la vuelva a bajar. Es un pedido a GitHub al abrir, sin datos tuyos, pero hay que decirlo en la privacidad. **Mi recomendación:** sí.
5. **Etiquetas de la biblioteca** (BD-15). Para que no entre spam al sitemap, solo las de una lista fija (`KNOWN_TAGS` en `web/src/lib/filters.ts`) van al desplegable y se indexan. Una etiqueta nueva se puede usar, pero no aparece ahí hasta sumarla. ¿Te cierra?

## 5. Cosas de cuentas y paneles

1. **Que nada roto llegue a producción** (IN-13). Hoy Vercel corre solo `npm run build` y la release solo los tests de .NET, así que `main` con los tests de la web en rojo igual se publica. (1) GitHub → Settings → Branches → agregá una regla para `main` que pida en verde los checks `build-test`, `web` y `stats`. (2) Vercel → proyecto → Settings → Deployment Checks, con esos mismos. Si algún nombre no coincide con lo que ves, contame.
2. **Google Search Console.** Faltan los dos últimos pasos: **Verificar** y, en **Sitemaps**, mandar `sitemap.xml`.
3. **Revisar `data/maps.json`:** por mapa, si tiene agua para pescar y qué fuentes hay. Lo armé de memoria; los que dicen "Revisar" en la nota son los que menos seguro estoy.
4. **Donaciones (`data/donaciones.json`).** Completá `links` con tu usuario de Cafecito, PayPal y Ko-fi y, si querés, el enlace de Mercado Pago; revisá las metas. Cada aporte se anota a mano en `donations`, porque Cafecito no avisa por API.
5. **Reemplazar el Fast Castle publicado** (código `4sangt`, el viejo "Fast Castle (27 + 2)") por "Fast Castle para principiantes (27 + 2)".
6. **Capturas y video** para la portada y el portfolio (C2 de `PLAN.md`).
7. **Probar la release en otra PC** (C1 de `PLAN.md`): bajar el zip, instalar el mod, empezar el coach y abrir una BO desde la web. Anotá lo que no se entienda sin ayuda.
8. **Revisar `data/civ_styles.json`:** las aperturas típicas de las civs nuevas son una aproximación, y hay civs con exploradores que no tienen establo.

## 6. Pruebas a mano de la revisión

Lo que no se pudo probar sin jugar. Si algo no da como dice, apretá Ctrl+Alt+F ahí mismo y contame.

**En una partida:**

1. Pausá el juego más de 30 s con el mod mínimo: no tiene que salir informe ni partirse el registro. Lo mismo pausando el coach con Ctrl+Alt+P.
2. Abrí "Primeros pasos" en plena partida: el coach tiene que seguir andando.
3. Dejá la BO a propósito hasta que diga "BO abandonada": los aldeanos no la retoman, y un edificio de la BO sí.
4. Terminá una BO que acabe en granjas: quedan la postura y la tarjeta chica, y no sale "BO abandonada" a los 3 minutos.
5. Cancelá una investigación y usá "◀ Atrás": el coach la vuelve a pedir y no la marca en curso sola.
6. Empezá otra partida sin pasar por la pantalla de carga: el tiempo tiene que correr desde cero.
7. Sin el mod, mirá la GD y las sugerencias: cambió cómo se miden las tasas.
8. Con una BO de chinos o mayas: cambió la simulación (cuenta a los aldeanos de más), así que los horarios y "a N de población" salen antes.

**En la app, sin jugar:**

9. Con el coach esperando la partida, elegí otra BO de la lista: tiene que arrancar con esa.
10. Conectá otra cuenta con el enlace de la web: el cartel nombra las dos y "No" es el botón por defecto. Probá pegar un token en "Pegar código": conecta y no lo muestra.
11. Cerrá la app, dejá `%APPDATA%\BoCoach\settings.json` vacío y abrila: tiene que abrir, avisar en Ajustes y dejar apagado "Compartir mis partidas" hasta que lo prendas. Lo mismo con `regions.json` vacío: abre igual.
12. Ajustes → Avanzado → **Sacar…**, con el juego cerrado: el mod se va de Mods instalados.
13. Si tenés dos monitores con distinta escala de Windows, mirá que el overlay caiga en su lugar.
14. A 1366×768 o 1280×720: la app tiene que calibrar sola y avisar, en Primeros pasos y en Ajustes, que a esa resolución los números salen muy chicos y se lee peor.

**En la web, con sesión:**

15. Armá una BO sin sesión, tocá "iniciar sesión", entrá con Discord o Google: tiene que volver con el aviso "Recuperamos…".
16. Editá una BO tuya, cambiale algo y tocá Nueva: va a `/subir`, y con Atrás vuelve lo editado. Guardá: reemplaza la BO y al volver no queda borrador.
17. Cerrá o recargá la pestaña con cambios: pregunta el navegador, y al recargar se recupera.
18. En una caja del centro urbano, apretá Alt+← dos veces con el teclado: la caja se mueve y no salís de la página.
19. Empezá un arrastre, hacé Alt+Tab, soltá en otra ventana y volvé: la caja no sigue al mouse y Publicar anda.
20. "Conectar la app" da un token que no publica: `BoCoach.exe --publish` con ese token tiene que explicar el motivo. Para publicar, creá uno con "Crear token".

## 7. Lo que sigue sin confirmar de antes

Si ya lo viste andar jugando, avisame y lo saco. El paso a paso de cada uno está en la lista vieja, con el mismo número.

- **Otra resolución (3):** 2 o 3 minutos grabando con el juego a 2560×1440 o 1600×900 en ventana. Es lo que falta para cerrar B4.
- **Captura de diagnóstico de un amigo con el HUD a 1,58 (2):** Ctrl+Alt+X en partida, y que mande `%LOCALAPPDATA%\BoCoach\diagnostico`.
- **Un ataque grabado (13):** una grabación en la que te ataquen. El aviso sale del color del atacante y hoy solo se lee el texto blanco.
- **Pantalla de carga (4b):** una captura con el juego en castellano y otra en multijugador; si el de enfrente que elige el coach es el que te queda enfrente en el mapa.
- **Hunos, "Scouts → CA (mejorada)" (0n):** si los pasos de Castillos se pueden seguir y si sobra o falta algo.
- **La casa en rojo (0r):** si te molesta durante toda la investigación de la Feudal, con el centro urbano sin producir.
- **El cruce (0b, 0c):** "Buscar" tu Elo, el renglón violeta en la carga, y las páginas `/cruces`. La conversión de duración (×1,7) está sin verificar con una partida.
- **Modo asistido (0e, 0f) y editor de la web (8d):** si se entiende, si la espera molesta y si los pasos generados se pueden jugar.
- **La GD que salta de lugar (4c):** no la pude reproducir. Si la ves, Ctrl+Alt+X ahí mismo.
- **Ventanas:** "Atajos" (0g), modo desarrollador (0h), el tablero (0u) y "Coach automático" con alt-tab (16b).
