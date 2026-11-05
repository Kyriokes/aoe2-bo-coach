# La documentación de BO Coach

Dónde está cada cosa, según quién la busca. Nada se borró al ordenarla (2026-11-05): lo que dejó de valer se movió a `historial/`.

## Si jugás

- [README](../README.md): qué hace, cómo empezar, atajos, el mod, cómo avisar si algo anda mal.
- [LEEME-portable.txt](LEEME-portable.txt): va adentro del zip, como `LEEME.txt`.
- [Privacidad](https://aoe2-bo-coach.vercel.app/privacidad) y [qué viene](https://aoe2-bo-coach.vercel.app/roadmap), en la web.

## Si querés colaborar con el código

- [CONTRIBUTING](../CONTRIBUTING.md): qué instalar, compilar, correr los tests y probar sin el juego.
- [PLAN](../PLAN.md): objetivo, arquitectura, formatos y hoja de ruta.
- [web/README](../web/README.md) y [web/DEPLOY](../web/DEPLOY.md): la web y cómo ponerla en línea.
- Las notas de `dev/`, abajo.

## Notas técnicas por tema (`dev/`)

Cómo funciona cada parte y, sobre todo, **por qué**: casi todas las reglas salieron de una partida real. Están en el orden en que se escribieron; si dos se contradicen, manda la más nueva y, siempre, el código. Conviene buscarlas por el nombre de la clase.

| Nota | De qué trata |
|---|---|
| [entorno](dev/entorno.md) | La PC de desarrollo, herramientas, rutas, antivirus |
| [ci-y-releases](dev/ci-y-releases.md) | GitHub Actions, releases, versión portable, ramas |
| [datos-del-juego](dev/datos-del-juego.md) | El `.dat`, `game_data`, eventos, bonificaciones de civ |
| [vision-y-hud](dev/vision-y-hud.md) | HUD, mod propio, letras, log, cola de producción, calibración, escala, pantalla de carga |
| [coach](dev/coach.md) | Tracker, Planner en vivo, creencia del estado, economía y GD, postura, señales, informe |
| [build-orders](dev/build-orders.md) | Formato, conteo de aldeanos, importadores, una BO por civ, simulación, modo asistido |
| [app](dev/app.md) | Tablero, overlay, recordatorios, atajos, modo desarrollador, caché de BO, idiomas |
| [web](dev/web.md) | Next.js, Supabase, biblioteca, editor, idiomas, BO privadas y suscripciones |
| [registro-de-partida](dev/registro-de-partida.md) | Qué se graba, cómo se envía y cómo se reproduce una partida ajena |
| [estadisticas-de-cruces](dev/estadisticas-de-cruces.md) | De dónde salen, cómo se ajustan, dónde se muestran |

## Si sos el dueño del proyecto

- [pendientes-usuario](pendientes-usuario.md): lo que solo vos podés hacer, en orden.
- [beta-invitacion](beta-invitacion.md): el mensaje de la beta y qué hacer con lo que llegue.

## Auditorías (`auditorias/`)

- [2026-11-05](auditorias/2026-11-05.md): de punta a punta, con el estado de cada hallazgo.
- [2026-10-31](auditorias/2026-10-31.md): seguridad y errores.
- [revisiones-anteriores](auditorias/revisiones-anteriores.md): las del 19, 21 y 22 de octubre.

## Lo que ya pasó (`historial/`)

Fotos de un momento: sirven para entender cómo se llegó a algo, no como verdad de hoy.

- [partidas](historial/partidas.md): las partidas revisadas una por una y lo que se arregló con cada una.
- [estado-por-fases](historial/estado-por-fases.md): cómo se fueron cerrando las fases 0 a 10.
- [hoja-de-ruta-hecha](historial/hoja-de-ruta-hecha.md): el detalle completo de cada ítem terminado de la hoja de ruta.
- [pendientes-hasta-2026-11-05](historial/pendientes-hasta-2026-11-05.md): la lista vieja de pendientes, con las instrucciones de cada prueba.
- [estado-hud-ocr-v0.9.76](historial/estado-hud-ocr-v0.9.76.md): cómo se leía la pantalla en esa versión.
- [traspaso-2026-10-20](historial/traspaso-2026-10-20.md): traspaso entre sesiones de esa fecha.
- [plan-grabacion-catalogo](historial/plan-grabacion-catalogo.md): el guion de la partida de catálogo, ya jugada.

## Diseños y planes (`superpowers/`)

[`specs/`](superpowers/specs): los diseños aprobados, con sus decisiones. [`plans/`](superpowers/plans): los planes de implementación, **ya ejecutados**: las rutas que citan pueden haber cambiado (por ejemplo, el editor WPF se fue en la v0.9.52; recuperable con la etiqueta de git `editor-wpf`).
