---
name: revisar-grabacion
description: Cómo analizar una grabación de partida de AoE2 BO Coach (%LOCALAPPDATA%\BoCoach\recordings) para encontrar y arreglar lo que el usuario vio mal en el coach, el overlay, la GD o la lectura del HUD. Usala siempre que el usuario diga que jugó o grabó, pida "revisá la última grabación", cuente un problema que vio en una partida (se trabó un paso, la GD pide mover aldeanos de más, no contó unidades, avisó algo falso, no leyó un número) o pegue una captura del overlay, aunque no nombre la grabación.
---

# Revisar una grabación

El usuario juega con Ctrl+Alt+R y después cuenta lo que vio. El trabajo es reproducir cada problema con la grabación, encontrar la causa, arreglarla con un test que la fije y comprobar el arreglo contra la misma grabación. Nunca hay que pedirle que juegue de nuevo para algo que la grabación ya muestra.

## 1. Ubicar la grabación

- Están en `%LOCALAPPDATA%\BoCoach\recordings\<aaaammdd-hhmmss>` (en Bash: `"$LOCALAPPDATA/BoCoach/recordings"`). La última es la de nombre más nuevo: `ls -t | head -1`.
- Cada captura es `NNNNNN_<región>.png` (una por región calibrada) a 2 por segundo. `frames.csv` da `frame,time_ms` y `session.json` tiene el área del juego y las regiones (`pixels` = x, y, ancho, alto).
- Si una región no está en `session.json` (por ejemplo `production_queue` en grabaciones viejas), esa lectura no se puede revisar: decilo en vez de suponer.
- Anotá en una lista los problemas que contó el usuario, uno por renglón, y resolvelos de a uno.

## 2. Correr el coach sobre la grabación

Todo con `dotnet run -c Release --project tools/ReplaySession -- <comando>` desde la raíz del repo (Release, porque el Debug de la app suele estar bloqueado si el editor está abierto). La lista completa de comandos está en el comentario al principio de `tools/ReplaySession/Program.cs`.

- `coach <grabación> <bo.json> --guide`: lo que mostraría el overlay cada vez que cambia, con los aldeanos que hay, la GD y la BO, y al final el estado de cada paso. Es el punto de partida casi siempre.
- La visión completa sobre 15 000 capturas tarda. Si vas a correr el coach varias veces, primero `replay <grabación> --out <scratchpad>/lecturas.csv` y después `coach <scratchpad>/lecturas.csv <bo.json> --guide`, que es casi instantáneo. Ojo: el CSV solo guarda reloj, stocks, total de aldeanos, inactivos, edad y eventos; pierde los aldeanos por recurso del mod, el "N/M" y la cola de producción. Con el CSV la GD sale "(estimado)", la población se estima y los edificios por la cola no se cuentan (en la 20261019-202635 las galerías se cerraban a las 17:06 en vez de 12:16). Para lo que dependa de eso, corré `coach` sobre la grabación (unos 45 s para 12 minutos).
- `queue <grabación> data/icons/production_queue.json`: cada cambio de la cola de producción.
- `read <grabación> <región> data/fonts/<letra>.json --frames ...` y `ocr <grabación> event_log --frames ...` para una lectura puntual.
- La BO: la del repo está en `build_orders/`, pero desde la v0.9.52 el usuario juega con la que bajó de la web, guardada en `%APPDATA%\BoCoach\cache\bo\<código>.json` (dentro, `build_order`), que puede ser distinta. Si el problema depende de la BO, compará las dos y avisá si difieren.

Filtrá la salida con `grep` alrededor del minuto que contó el usuario; no la vuelques entera.

## 3. Mirar las capturas

Para confirmar qué pasaba en pantalla, abrí con Read los PNG de las capturas cercanas (el reloj de cada captura está en `NNNNNN_clock.png` y el tiempo real en `frames.csv`). Para comparar muchas, armá una hoja de contactos con un script `.ps1` en el scratchpad (System.Drawing) en vez de leer decenas de imágenes sueltas. No generes video ni captures el escritorio del usuario: gasta muchísimo y no hace falta.

Lo que se vio en el pasado y conviene descartar primero:
- Lecturas que parpadean (el "N/M" cerca del tope titila una captura sí y otra no; el suavizado conserva el último valor).
- Eventos que el log no muestra: dos edificios iguales que terminan juntos dan un solo aviso, un mensaje repetido no agrega renglón, "Farm Built" no existe.
- El overlay propio sale en las capturas (la GD dentro de la franja de color, el aviso de inactivos): no es parte del HUD del juego.

## 4. Arreglar con un test

- Encontrá la causa en el código antes de tocar nada (el camino es ScreenReader → HudTracker → SnapshotBuilder → GameSnapshot → Coach/Tracker/Planner → CoachText/Overlay).
- Escribí primero el test que reproduce lo que vio el usuario y fallá con él:
  - lógica del coach o del motor: `tests/BoCoach.Core.Tests/Engine/Partida.cs` simula una partida (`Event`, `Events`, `PopulationGrows`, `Hud`, `Wait`, `Play`);
  - lectura de pantalla: copiá a `tests/fixtures/<tema>-<grabación>/` solo las capturas que hacen falta (pocas, con el número de captura en el nombre) y agregalas al `.csproj` de tests si no entran por un comodín existente.
- Corré la suite completa: `dotnet test BoCoach.slnx -c Release`.

## 5. Verificar contra la grabación

Volvé a correr el mismo comando de ReplaySession y mostrá el antes y el después con el minuto exacto (por ejemplo "el paso de las galerías se cerraba a las 18:11, ahora a las 12:45"). Si un arreglo cambia otras partidas, corré también las grabaciones anteriores que tocan lo mismo.

## 6. Cerrar

- Un commit por cada arreglo (regla del usuario), con un mensaje en castellano que diga qué se arregló y en qué partida se vio.
- Anotá en `CLAUDE.md` (sección de la fase actual) lo que se aprendió del juego o de la lectura, con la grabación de referencia, en un commit aparte.
- Si hay cambios para que el usuario pruebe, seguí con la skill `nueva-version`.
- Al usuario: por cada problema que contó, qué pasaba, qué cambió y cómo quedó en su partida, en castellano rioplatense, sin guiones largos. Terminá con qué tiene que hacer él (abrir la versión nueva, calibrar algo, grabar otra partida).
