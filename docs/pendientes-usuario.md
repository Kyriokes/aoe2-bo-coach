# Lo que queda de tu parte

Lista viva: cada sesión agrega lo nuevo y tacha lo hecho. Ordenado por lo que más destraba.

## Para los amigos (escala y castellano)

1. **Grabación en castellano.** Que un amigo con el juego en castellano grabe 2 minutos con Ctrl+Alt+R (desde el arranque de la partida, con la v0.9.17 o posterior) y te mande la carpeta que queda en `%LOCALAPPDATA%\BoCoach\recordings`. Sin esto no se puede comprobar que el log en castellano confirme los pasos (los textos salen del juego, pero falta verlos leídos de verdad).
2. **Captura de diagnóstico del amigo con el HUD a 1,58.** Cuando tenga la v0.9.17: Ctrl+Alt+X en partida y que mande la carpeta `%LOCALAPPDATA%\BoCoach\diagnostico`. Es una captura sin pérdida (las de WhatsApp vienen comprimidas: el "13/15" y la cola de producción no se leen por eso).
3. **Grabación tuya a otra resolución.** 2 o 3 minutos con Ctrl+Alt+R con el juego a 2560×1440 o a 1600×900 en ventana, con el mod. Sirve para medir que la lectura a otra escala llega al 98 % (criterio de B4).
4. **Probar la captura de diagnóstico en tu PC** (Ctrl+Alt+X y el botón): que deje el `.png` y el `.txt` en `%LOCALAPPDATA%\BoCoach\diagnostico`.

## Versiones y publicación

5. **Pasar la v0.9.17 a los amigos.** El zip lo arma el CI: en GitHub, pestaña Actions, la corrida "CI" del commit `v0.9.17` (rama `fase-9-refinamiento`), abajo en "Artifacts" está `BoCoach-portable-win-x64` (dura 14 días; hay que estar logueado). Instrucciones para ellos, también en el `LEEME.txt` del zip: abrir la app, "Instalar mod del HUD", reabrir el juego, elegir "Idioma del juego" (España o Latinoamérica, según cómo lo tengan en el juego) y la "Civ", empezar el coach (calibra solo). Si algo falla, Ctrl+Alt+X en partida y mandar la carpeta `%LOCALAPPDATA%\BoCoach\diagnostico`.
6. **Push a `main`.** Lo pedís vos en el chat ("pusheá a main"): la rama `fase-9-refinamiento` va adelante de `main`.
7. **Release pública** (etiqueta `v*`): cuando quieras. La portada de la web apunta a la última release y hoy no hay ninguna.

## Web y portfolio

8. **Reemplazar el Fast Castle publicado** (código `4sangt`, el viejo "Fast Castle (27 + 2)") por el nuevo "Fast Castle para principiantes (27 + 2)": borrarlo desde "Mi cuenta" y publicar el nuevo desde el editor.
9. **Capturas y video** del overlay en partida, el editor con la vista previa, la calibración y la web, para la portada y el portfolio (C2 de `PLAN.md`).
10. **Probar la release en otra PC** (C1 de `PLAN.md`): bajar el zip, instalar el mod, empezar el coach, registrar los enlaces y abrir una BO desde la web. Anotar lo que no se entienda sin ayuda.

## Otros

11. **Prueba de usabilidad del editor** (B5): una BO de 20 pasos desde cero, cronometrada. Si pasa de 5 minutos, contame qué te trabó.
12. **Tu biblioteca local:** las copias de scouts y fast castle en `%APPDATA%\BoCoach\build_orders` son las viejas (9 en ovejas). Para tener las nuevas: "Importar…" con `build_orders/scouts_22p.json` y `build_orders/fast_castle_principiante.json` del repo.
13. **Bloque D de `PLAN.md`** (ideas grandes): charlarlas antes de que yo escriba código.

## Decisiones que necesito

14. **Leer la civ sola (B2, segunda parte).** En tus grabaciones el panel de puntajes muestra la civ con dos letras al lado de cada jugador ("Ja", "Bo"), no el nombre. Necesito que me digas: (a) cómo sé cuál renglón es el tuyo: ¿pongo en la app un campo "Tu nombre en el juego" o siempre sos el jugador 1? (b) si dos civs empiezan igual (burgundios, búlgaros y birmanos), ¿alcanza con que el coach no elija y deje el desplegable "Civ" como está? La pantalla de carga muestra el nombre entero, pero tus grabaciones suelen empezar después.
15. **Dependabot.** Dejé las acciones del CI fijadas por SHA. Si querés que se actualicen solas, agrego Dependabot, pero va a abrir PRs por su cuenta en el repo público. ¿Lo activo?
