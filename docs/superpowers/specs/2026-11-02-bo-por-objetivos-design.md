# La BO por objetivos: evolución en escalones

Estado: diseño acordado con el usuario el 2026-11-02 (reformulado desde el borrador del mismo día). Se implementa en el orden de la sección 4; cada escalón tiene su plan y su versión. Fecha: 2026-11-02.

## 1. Por qué

Hoy una BO es una lista de pasos ("hacé X") y el coach confirma cada paso con fuentes ruidosas (OCR del log, cola, puntaje, tope de población). Para reconstruir un estado objetivo desde la lista se acumularon reglas: `group`, `after`, `at_pop`, `from`, `Lanes`, `StepTarget`, `NowTarget`, `PlannedVillagers`, `HudTargets`, `CapTargets`, `Posture`, `GuideDistribution`. Además conviven dos modelos de la economía (los conteos de aldeanos de la guía humana y las tasas del simulador), y cuando discrepan el coach pide mover aldeanos sin que se sepa quién tiene razón.

Lo que el diagnóstico original decía de más: que cada partida pide un parche nuevo por culpa del formato. En las últimas cuatro partidas (1/11) los arreglos fueron de percepción (un ícono que faltaba en la cola, el reloj tapado, el panel de puntajes que no se leía), no de la lógica de pasos. Una capa de creencia habría leído "arquero" donde había una milicia igual que hoy. Los objetivos no eliminan los parches de lectura; lo que cambian es el daño: hoy una lectura mala cierra un paso para siempre, y con replanificación desde el estado solo desvía un rato.

Meta: que un error de lectura o del jugador no rompa el consejo, y que el simulador sea la única fuente de verdad de la economía. Sin perder lo que ya anda (las 129 BO, el editor de la web, la base) antes de probar que lo nuevo es mejor.

## 2. Principios

1. **La BO dice qué lograr, no qué hacer paso a paso.** El camino lo calcula el motor desde el estado real.
2. **Una sola fuente de verdad para la economía:** el simulador. Las guías humanas se importan como objetivos y como referencia, no como segundo modelo.
3. **Un error de lectura y un error del jugador son lo mismo:** el estado real no es el esperado. Los dos se resuelven recalculando desde el estado actual.
4. **El coach nunca pide algo imposible según lo que cree.** Si falta un recurso o un edificio, pide eso.
5. **Al jugador, una sola cosa importante por vez**, y lo crítico nunca es un renglón chico.

## 3. Lo decidido: evolución, no reescritura

- **Los pasos siguen siendo el formato** (schema 1 y 2) y el camino por defecto. Los objetivos se derivan de los pasos; ya pasa en parte: `Posture.Effective` deduce la postura de cierre, `PopulationTargets` y `CapTargets` sacan hitos de la simulación.
- **Cada escalón vale solo.** Si el proyecto se frena en cualquiera, lo hecho queda andando y verificado contra las grabaciones.
- **El escalón 3 es la prueba que decide el formato.** El generador desde el estado vivo tiene que igualar o ganarle a las 129 guías, en simulación y en partidas reales. Si gana, se cambia el formato (escalón 4). Si no, igual quedan los escalones 1, 2 y 3. Hoy el generador no iguala a las guías ni para Feudal sola (D12), y el simulador sigue optimista (no camina, la comida natural no se agota salvo opción): cambiar el formato antes de esa prueba daría consejos peores que leer buildorderguide, dichos con más seguridad.
- **Verificación de cada escalón:** las grabaciones de `%LOCALAPPDATA%\BoCoach\recordings` son el golden. Los minutos de cierre de cada paso y los avisos no pueden empeorar (`ReplaySession coach`).

## 4. Escalones, en orden

### 4.1 Escalón 1: lo crítico se ve (casas por ritmo y clic de edad)

Sin tocar el formato ni la web. Es lo que faltó en la partida 20261101-230201: la Feudal estuvo 14 s "posible" en blanco y el jugador no lo notó.

**Casas por ritmo de producción.** Hoy avisa con 3 lugares libres, lo que sirve con un centro urbano y llega tarde con varias galerías.

- **ritmo de consumo** (población por segundo) = suma, por lo que está produciendo en la cola real (íconos que no esperan), de población de la unidad / tiempo de entrenamiento con la civ y la velocidad del edificio (`CivBonuses.Time`, `WorkRate`). Las tecnologías y las edades no consumen. Sin cola leída (o sin confiar en ella): solo el centro urbano con aldeanos.
- **tiempo de reacción** = tiempo de construcción de la casa con la civ (25 s) + caminata (unos 5 s) + tiempo del jugador (unos 10 s, a calibrar con el registro del escalón transversal).
- **margen** = el mayor entre 3 (el de siempre) y ritmo × reacción, redondeado para arriba. Aviso cuando los lugares libres son el margen o menos (y no se llegó a 200). Las casas en construcción no se ven en pantalla: no se descuentan.
- Ejemplos: un centro urbano solo consume 0,04 por segundo (1 / 25) y da margen 3 (como hoy). Centro urbano, 3 galerías y un establo consumen unos 0,16 por segundo: margen 7, que coincide con la intuición del usuario ("unos 6").

**Escalera de prioridades (primer tramo).** Del sistema, no de cada BO:

1. **Crítico**, un solo renglón grande, verde si es algo que hacer ya y rojo si es una falta: clic de edad cuando ya se puede (el objetivo lo exige y hay recursos y edificios), casa por ritmo, centro urbano parado mientras faltan aldeanos. En este escalón: el clic de edad y la casa; el centro urbano ya tiene su renglón.
2. **Ahora**: las líneas de siempre (una acción por línea).
3. **Después** y **de fondo**: quedan para el escalón 4 (presentación en capas).

El clic de edad posible pasa de la misma frase en blanco a "Hacé clic en Edad Feudal", grande y titilando como el clic del rival (verde). Con la BO terminada, la tarjeta ya dice "Hacé clic en ..." (`CardKind.Ready`): titila igual.

### 4.2 Escalón 2: creencia del estado

Una sola capa que fusiona las fuentes y no sabe nada de la BO: OCR del HUD, log, cola de producción, puntaje. Cada fuente aporta evidencia con una fuerza; ante contradicción gana la más fuerte y todo se replanifica desde la creencia corregida (ejemplo: el log avisa una galería y la cola muestra dos arqueros a la vez, entonces hay dos). Reemplaza a `ScoreLedger`, `BuildingsFromQueue`, `FinishedInQueue` y a las reglas sueltas de granjas y telar del `Tracker`, reaprovechando su conocimiento y no su forma. Entrega: los mismos minutos de cierre (o mejores) en todas las grabaciones con cola y puntaje. Una o dos semanas.

### 4.3 Escalón 3: replanificar desde el estado vivo hacia el próximo hito

- Los hitos salen de la BO actual (de pasos): el próximo clic de edad, el próximo lote de unidades, los edificios que piden. No hace falta cambiar el JSON.
- El Planner arranca desde el estado vivo (`PlayerState`: stock, aldeanos por recurso, edificios de la creencia, investigaciones en curso). Hay que verificar que pueda (hoy arranca de `PlanStart`).
- Se simula hacia adelante para detectar **colisiones**: "en 40 s vas a necesitar 300 de madera y con este reparto tenés 180". Se muestran como aviso con tiempo, no como una orden de mover aldeanos sin explicación.
- Los planes B salen de comparar 2 o 3 alternativas simuladas contra el hito: 458/500 de comida, ¿clic ya o un aldeano más? Un empate dentro de unos pocos segundos se muestra como empate; no se inventa una orden.
- La GD se calcula desde el simulador y el hito activo (la BO humana deja de ser el segundo modelo).
- **Criterio de éxito (la prueba que decide el escalón 4):** en simulación, el camino generado desde el 0:00 hacia los hitos de cada una de las 129 BO llega no más tarde que la BO (Feudal, Castillos, lotes); en grabaciones, los consejos no empeoran ningún cierre y el jugador los entiende.
- **Lo que la partida 20261102-204825 dejó para este escalón:** (a) el simulador por defecto es de comida infinita y así el optimizador "mejoraba" moviendo aldeanos de comida a madera (8 mejoradas regeneradas con `improve --food`); la comida finita (`FoodSupply`) es más honesta pero todavía no sirve de por defecto: 8 BO no terminan (civs sin molino, Arena, Hideout). Hacerla el modelo único es parte de este escalón. (b) La GD pide como mucho 5 movimientos (`EconomySettings.MaxMoves`) aunque falten 11 contra la BO. (c) La línea de aldeanos muestra el paso de la BO ("Mover 4 de madera a oro") aunque la economía esté pidiendo comida: con el camino calculado desde el estado, las dos cosas tienen que ser una sola.
- Costo: un análisis completo tarda unos 60 ms en WebAssembly; en nativo debería sobrar para replanificar cada 5 s con 2 o 3 alternativas. Verificar. Histéresis como la de `SuggestionGate` (dura 8 s, cambia si se sostiene 6 s) para que no titile.

### 4.4 Escalón 4: el formato de objetivos (solo si el escalón 3 gana)

- `schema_version` 3: `meta + goals[] + tactics[]`. Los pasos pasan a ser un resultado derivado. Borrador del formato en la sección 6.
- Migración de las 129 BO: se extraen sus objetivos y se compara lo que genera el motor contra su tiempo real.
- Cambia el esquema de Supabase (`build_orders`, columnas generadas, `build_order_style`), la API de la app, el editor de la web (paleta, línea de tiempo, guard, aldeanos por número, asistido: todo construido alrededor de pasos), la app (que hoy solo es caché), el overlay y el informe. Es lo más caro de lejos: semanas sin que ninguna partida mejore, por eso va último.
- Versiones viejas: la web y la app leen schema 1, 2 y 3 durante la transición.
- Presentación en capas completa (crítico, ahora, después, de fondo).

### 4.5 Transversal: registro de partida (primero local, después envío)

Lo decidido está en la sección 9. Se hace en dos partes:

- **Local, barato y de valor inmediato** (puede ir con el escalón 2): guardar por partida lecturas crudas del HUD (antes de suavizar, con confianza) cada 2 s, la creencia y sus correcciones, pronósticos y resultado, acciones mostradas y respuesta, contexto. Es un `lecturas.csv` más rico: permite reproducir partidas viejas con código nuevo sin PNG (tests con partidas reales sin pantalla) y medir el tiempo de reacción del jugador para la fórmula de casas.
- **Envío a la base, consentimiento y términos:** después, cuando haya qué calibrar. Antes de prenderlo, medir el espacio: la base gratis de Supabase tiene 500 MB, y a cientos de KB por partida son unas mil partidas; conviene el storage, no una tabla.

## 5. Arquitectura destino en tres capas

Es a lo que converge la evolución; los escalones la van armando.

### 5.1 Creencia del estado (escalón 2)
Fusiona las fuentes, con fuerzas; no sabe nada de la BO.

### 5.2 Intención (la BO)
Hasta el escalón 4: la lista de pasos, con los hitos derivados (escalón 3). Desde el escalón 4: una cadena ordenada de **objetivos**, cada uno un estado deseado con condiciones ("4 exploradores en Feudal"; después "33 arqueros a caballo con todas las mejoras de galería, establo, herrería y universidad"). Inamovibles y en orden; mientras el activo no se cumple todo apunta a él; importa la cantidad y las condiciones, no cómo se llega (2, 3 o 4 galerías da igual). Ya existe en embrión en el modo asistido del editor (D12: `AssistedGenerator`, `GoalPolicy`, `IStepPolicy`, `PlanView`).

### 5.3 Decisión y presentación (escalones 1, 3 y 4)
Desde la creencia y el hito activo: la brecha, el camino más corto, las colisiones, los planes B y la escalera de prioridades.

## 6. Formato de objetivos (borrador para el escalón 4, no definitivo)

```json
{
  "schema_version": 3,
  "name": "Scouts a arqueros a caballo",
  "civ": "huns",
  "map": null,
  "goals": [
    { "id": "g1", "kind": "units", "what": "scout_cavalry", "count": 4, "in_age": "feudal" },
    { "id": "g2", "kind": "units", "what": "cavalry_archer", "count": 33,
      "with_upgrades": ["thumb_ring", "bodkin_arrow", "padded_archer_armor"] }
  ],
  "tactics": [
    { "when": "villagers_on_hunt >= 1", "note": "Atraé 3 ciervos" }
  ]
}
```

## 7. Presentación (destino)

- Una acción principal.
- Un renglón crítico solo cuando hay algo crítico (escalón 1).
- Las colisiones como aviso con tiempo ("en 40 s te falta madera"), no como orden sin explicación (escalón 3).
- La GD desde el simulador y el hito activo (escalón 3).

## 8. La apertura (decidido el 2026-11-02)

La apertura estándar (6 a comida contando los 3 iniciales, 2 casas con los iniciales, 4 a madera, el primer jabalí, bayas con molino) es una práctica de 26 años y sigue vigente en mid-elo (video "Your First Build Order - AoE2 New Player Guide", 8 años).

- Es una **plantilla estándar**. El generador **no la modifica solo**: solo propone variantes y, si encuentra una mejora en la estándar, se muestra con el número y decide el usuario.
- **Se expresa como una cola de destinos:** adónde va cada aldeano según el número que nace ("hasta 6 a ovejas, el siguiente construye el campamento maderero, 3 más a madera, el siguiente al jabalí, el 13 construye el molino..."), más reglas de edificio por aldeano. No como pasos con hora. Esto no depende de ningún escalón: hoy se expresa con pasos de aldeanos, y es la mejor idea del borrador para el generador del escalón 3.
- **Los hitos de tiempo salen de la producción continua del centro urbano:** población esperada = 4 + t/25 (3 aldeanos y el explorador; 11 de población a las 3:00 es 7 aldeanos por 25 s, 17 a las 5:30 es 13 por 25 s, con unos segundos de margen). Es un hito de referencia, no el criterio de "centro urbano parado": no cuenta el telar ni las edades (paran el centro urbano) ni las civs con producción más rápida. El "parado" sigue saliendo de la cola. A confirmar contra el reloj real.
- **Las civs con bonificaciones se agrupan desde `CivBonuses`**, sin lista a mano. Casos de prueba de aceptación: mayas (arrancan en 5/5 de población, no pueden entrenar: telar primero con los 4 aldeanos y dos casas), chinos (6 aldeanos y sin comida: dos casas y telar, los 6 a las ovejas lo antes posible, el primero que sale va a madera) y hunos (sin casas: los iniciales van directo a la comida). El generador tiene que reproducir las dos primeras sin reglas especiales. El "scalp 4 sheep" de los hunos es una táctica (va en `tactics`), no se deriva.
- **Hitos verificables e informativos:** los verificables salen del HUD (población, aldeanos por recurso, edad, edificios). Los informativos no los ve la pantalla ("encontrar las ovejas a los 30 a 45 s", "haber encontrado todos los recursos a los 4:00"); se muestran como referencia o se infieren por señales indirectas (aldeanos trabajando en ovejas o caza), sin exigirlos.

## 9. Registro de partida y envío (decidido el 2026-11-02; orden en 4.5)

Objetivo: que el sistema guarde de forma nativa lo necesario para perfeccionar el modelo, sin depender de grabaciones de pantalla (que quedan para modo desarrollador).

**Preguntas que el registro tiene que contestar**
1. ¿Qué tan bien predice el simulador? Se guarda el pronóstico de recursos a 60 s y lo que pasó. Calibra las tasas por fuente, edad y civ.
2. ¿Cuánto tarda el jugador en reaccionar? Desde que el coach muestra un aviso hasta que el estado cambia. Calibra el tiempo de reacción de la fórmula de casas.
3. ¿Dónde falla la lectura? Cada vez que la creencia se contradice, se registra el hecho (sin recorte de pantalla por defecto).
4. ¿El consejo sirvió? Qué se mostró, cuánto duró, si el estado cambió como se pedía.
5. ¿Cómo salió la partida? Tiempos de cada objetivo, centro urbano parado, población trabada, inactivos.

**Contenido de un registro:** lecturas crudas del HUD (antes de suavizar, con confianza) cada 2 s; la creencia y sus correcciones; pronósticos y resultado real; acciones mostradas y respuesta; contexto (civ, mapa, civ del rival, BO, versión de la app, versión de `game_data`, diseño del HUD). Formato versionado, comprimido, de cientos de KB por partida. Con las lecturas crudas se puede reproducir una partida vieja con el código nuevo (tests con partidas reales sin pantalla).

**Envío (decisión del usuario)**
- Al terminar cada partida la app intenta enviar el registro a la base. Si no hay internet, queda en una cola local y se reintenta al terminar la partida siguiente, enviando todo lo pendiente.
- Consentimiento: los términos al descargar, más una pantalla clara en "Primeros pasos", un interruptor en la ventana y un pedido de borrado. Prendido por defecto. Revisar los términos con alguien del rubro si se publica en otros países.
- Qué viaja: solo números y eventos. Sin nombre del jugador, sin pantalla de carga, sin el panel de puntajes con nombres y sin recortes de pantalla (si algún día se suben, solo de regiones sin texto de jugadores y con un permiso aparte).
- Identidad: un identificador aleatorio por instalación, no atado a la cuenta de la web.
- Idempotencia: cada partida lleva un identificador único; reenviar no duplica.
- Cola local con tope de espacio y de antigüedad; reintentos con espera creciente; se envía solo al terminar la partida, nunca jugando.
- Contra el abuso: endpoint de solo inserción (función de la base con token de la instalación, como `app_publish`), tope de tamaño, límite por instalación y por día, validación del esquema, versión de la app en cada registro. Los datos recibidos se tratan con desconfianza antes de calibrar nada.
- Costo y almacenamiento (Supabase gratis) a medir antes de prenderlo: storage antes que tabla.

## 10. Riesgos

- Hay conocimiento humano que no se deriva del simulador (atraer ciervos y jabalíes, micro). Va en `tactics`.
- La calidad del modelo económico pasa a ser todo. Sin tasas buenas, el motor da malos consejos con más confianza que hoy. Por eso el escalón 3 se mide contra las 129 guías antes de tocar el formato.
- Un motor que replanifica puede titilar. Histéresis (ver el glosario): se conserva la recomendación actual salvo que la nueva sea claramente mejor o se sostenga un tiempo. Ya está hecho para las sugerencias (`SuggestionGate`).
- Costo de cómputo de simular hacia adelante en vivo: a verificar en el escalón 3.
- Los parches de percepción siguen: cada ícono o letra nueva del juego se enseña igual que hoy. El escalón 2 solo acota el daño de una lectura mala.

## Glosario

- **Histéresis:** tener dos umbrales distintos para cambiar y para volver, o exigir que el cambio se sostenga, para que una decisión no salte de un lado al otro por una diferencia mínima.
