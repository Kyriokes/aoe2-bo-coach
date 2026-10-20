# Lo que queda de tu parte

Lista viva: cada sesión agrega lo nuevo y tacha lo hecho. Ordenado por lo que más destraba.

## Para los amigos (escala y castellano)

1. **Grabación en castellano.** Que un amigo con el juego en castellano grabe 2 minutos con Ctrl+Alt+R (desde el arranque de la partida, con la v0.9.17 o posterior) y te mande la carpeta que queda en `%LOCALAPPDATA%\BoCoach\recordings`. Sin esto no se puede comprobar que el log en castellano confirme los pasos (los textos salen del juego, pero falta verlos leídos de verdad).
2. **Captura de diagnóstico del amigo con el HUD a 1,58.** Cuando tenga la v0.9.17: Ctrl+Alt+X en partida y que mande la carpeta `%LOCALAPPDATA%\BoCoach\diagnostico`. Es una captura sin pérdida (las de WhatsApp vienen comprimidas: el "13/15" y la cola de producción no se leen por eso).
3. **Grabación tuya a otra resolución.** 2 o 3 minutos con Ctrl+Alt+R con el juego a 2560×1440 o a 1600×900 en ventana, con el mod. Sirve para medir que la lectura a otra escala llega al 98 % (criterio de B4).
4. **Probar la captura de diagnóstico en tu PC** (Ctrl+Alt+X y el botón): que deje el `.png` y el `.txt` en `%LOCALAPPDATA%\BoCoach\diagnostico`.
4b. **Captura de la pantalla de carga** (para leer nombres y civs, B2; desde la v0.9.18 ya está armada la lectura, falta probarla con la pantalla real): en la app escribí "Tu nombre" (segunda fila de arriba, como sale en el juego), elegí una BO y tocá "Empezar coach". Empezá una partida rápida contra IA (mejor 4 contra 4, así salen los 8 jugadores) y, apenas aparece la pantalla de carga, Ctrl+Alt+X (guarda a los 2 s). Si la carga es muy rápida y no llega, probá con una partida más grande. En la barra de estado de la app tendría que decir "Pantalla de carga: jugás con ...". Mandame la carpeta `%LOCALAPPDATA%\BoCoach\diagnostico` y contame qué dijo la barra. Ya en la partida, fijate si el jugador 2 o el 8 quedó enfrente tuyo.

4c. **La GD que salta de lugar** (2026-10-20): no la pude reproducir. En tus 3 últimas grabaciones la franja se lee igual en todas las capturas y la GD queda siempre chica arriba. El código la pone grande abajo de la caja solo mientras todavía no vio la franja de ese recurso en esta sesión del coach. Cuando la veas: Ctrl+Alt+X en ese momento (la captura incluye el overlay) y contame tres cosas: (1) si eran dos números del mismo recurso o de recursos distintos, (2) en qué momento (al empezar la partida, después de una pausa, de un Alt+Tab, de calibrar o de reiniciar el coach), y (3) qué versión decía arriba en la ventana. Ojo: el número grande blanco de abajo del cuadrado es el del mod (los aldeanos que hay); el chico de color, arriba en la franja, es la GD (los que tendría que haber).

## Versiones y publicación

5. **Pasar la v0.9.17 a los amigos.** El zip lo arma el CI: en GitHub, pestaña Actions, la última corrida "CI" en verde de la rama `fase-9-refinamiento` (un push nuevo cancela la anterior), abajo en "Artifacts" está `BoCoach-portable-win-x64` (dura 14 días; hay que estar logueado). Instrucciones para ellos, también en el `LEEME.txt` del zip: abrir la app, "Instalar mod del HUD", reabrir el juego, elegir "Idioma del juego" (España o Latinoamérica, según cómo lo tengan en el juego) y la "Civ", empezar el coach (calibra solo). Si algo falla, Ctrl+Alt+X en partida y mandar la carpeta `%LOCALAPPDATA%\BoCoach\diagnostico`.
6. ~~**Push a `main`.**~~ Hecho el 2026-10-20 (v0.9.17). Para los próximos, pedilo en el chat ("pusheá a main").
7. **Release pública** (etiqueta `v*`): cuando quieras. La portada de la web apunta a la última release y hoy no hay ninguna.

## Web y portfolio

8. **Reemplazar el Fast Castle publicado** (código `4sangt`, el viejo "Fast Castle (27 + 2)") por el nuevo "Fast Castle para principiantes (27 + 2)": borrarlo desde "Mi cuenta" y publicar el nuevo desde el editor.
9. **Capturas y video** del overlay en partida, el editor con la vista previa, la calibración y la web, para la portada y el portfolio (C2 de `PLAN.md`).
10. **Probar la release en otra PC** (C1 de `PLAN.md`): bajar el zip, instalar el mod, empezar el coach, registrar los enlaces y abrir una BO desde la web. Anotar lo que no se entienda sin ayuda.

## Otros

11. **Prueba de usabilidad del editor** (B5): una BO de 20 pasos desde cero, cronometrada. Si pasa de 5 minutos, contame qué te trabó.
12. **Tu biblioteca local:** las copias de scouts y fast castle en `%APPDATA%\BoCoach\build_orders` son las viejas (9 en ovejas). Para tener las nuevas: "Importar…" con `build_orders/scouts_22p.json` y `build_orders/fast_castle_principiante.json` del repo.
13. **Bloque D de `PLAN.md`** (ideas grandes): charlarlas antes de que yo escriba código. De D1 ya están tus criterios (2026-10-20, anotados en `PLAN.md`); falta definir el "gris" (produce las dos unidades a la par) y cuánto atraso es "considerable".

## Decisiones que necesito

14. ~~**Leer la civ sola (B2, segunda parte).**~~ Respondida el 2026-10-20: sale de la pantalla de carga (nombre capturado en el tutorial de primer uso, equipos 1-3-5-7 contra 2-4-6-8); el diseño quedó en `PLAN.md`, B2. Hace falta la captura del punto 4b. Lo que preguntaba: en tus grabaciones el panel de puntajes muestra la civ con dos letras al lado de cada jugador ("Ja", "Bo"), no el nombre. Necesito que me digas: (a) cómo sé cuál renglón es el tuyo: ¿pongo en la app un campo "Tu nombre en el juego" o siempre sos el jugador 1? (b) si dos civs empiezan igual (burgundios, búlgaros y birmanos), ¿alcanza con que el coach no elija y deje el desplegable "Civ" como está? La pantalla de carga muestra el nombre entero, pero tus grabaciones suelen empezar después. Ojo: las dos letras no están en los textos del juego, parecen el comienzo del nombre, y así se pisan muchas ("Ma": magyares, malayos, malienses y mayas; "Vi": vietnamitas y vikingos; "Po": polacos y portugueses). Mi propuesta: leerlas solo como sugerencia (el coach cambia la civ si las letras son de una sola civ) y que manden el desplegable o la pantalla de carga si se grabó.
15. **Dependabot.** Es un robot de GitHub que revisa si salieron versiones nuevas de las piezas que usa el proyecto (acá, las "acciones" del CI: los pasos prearmados que bajan .NET y Node, arman el zip, etc.) y abre solo un pedido de cambio (PR) para actualizarlas; alguien tiene que aceptarlo en GitHub, y yo desde acá no puedo. Hoy esas acciones están fijadas a una versión exacta (por seguridad: nadie puede cambiarlas por debajo). Mi recomendación: no activarlo por ahora; las reviso a mano cada tanto. ¿Te parece?
