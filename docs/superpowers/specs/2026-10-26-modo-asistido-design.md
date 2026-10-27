# Diseño del modo asistido del editor: objetivos que arman la BO (2026-10-26)

## 1. Objetivo

Que alguien que no sabe armar una BO de cero la arme planteando **objetivos** en vez de pasos: "aldeanos sin parar
hasta 120", "Feudal lo antes posible", "20 exploradores con 2 establos", "Castillos", "Imperial". El sistema calcula los
pasos y, cada vez que se cambia un objetivo (de 2 a 3 establos, por ejemplo), muestra en vivo cómo se corren los
tiempos de todo lo que viene. El resultado es una BO normal (el mismo JSON) que el coach sigue en la partida como
cualquier otra.

Pedido del usuario del 2026-10-26: "quiero un modo asistido en la creación de BO, que arme con lógica BO pero con
toggles y botones que sirvan como atajos. Por ejemplo elegís el toggle de auto aldeanos hasta 120 y acomoda la economía
para pasar a Imperial solo produciendo aldeanos hasta 120, y luego incorporamos que queremos 20 scouts, entonces
modifica la build para crear esos 20 scouts en el menor tiempo posible, y permite al usuario elegir la cantidad de
establos, para ver en vivo cómo afecta la llegada a los siguientes pasos. Digamos que plantees objetivos y te calcule
los pasos."

### Decisiones del usuario (2026-10-26)

1. **Los objetivos mandan:** la BO guarda los objetivos y los pasos salen de ahí cada vez que algo cambia. Los pasos
   se ven pero no se tocan a mano; "Pasar a manual" copia los pasos al editor de hoy y es de ida.
2. **Web primero:** una pestaña "Asistido" en `/subir`, al lado del editor de hoy. El generador va en el Core, así que
   la app lo puede usar después.
3. **Lista ordenada:** los objetivos son una lista que se ordena arrastrando; lo de arriba tiene prioridad sobre los
   recursos y lo de abajo usa lo que sobra (la misma regla que ya usa el Planner con los pasos).
4. **Todo en la primera versión:** además de aldeanos hasta N, edades y unidades con cantidad y edificios, también
   mejoras eco automáticas, centros urbanos extra, tecnologías a pedido y elegir la apertura.
5. **Enfoque A:** el generador "juega" dentro del Planner (una política que agrega pasos mientras se simula), con la
   comida finita como opción del Planner prendida solo en el modo asistido por ahora.
6. **Pantalla A** de la maqueta (`2026-10-26-modo-asistido-maqueta.png`): dos columnas, los objetivos a la izquierda y
   el resultado a la derecha.
7. Aprobadas las cinco partes del diseño (formato, decisiones del generador, Core, pantalla, pruebas y sub-pasos) tal
   como quedan abajo.

Lo que se supone y el usuario no objetó: el público es el que no sabe armar una BO; los tiempos son los del simulador
(optimistas: sin errores ni caminatas largas), igual que en el editor manual.

### Enfoques descartados

- **Plantillas y optimizador:** bloques fijos (apertura, subida de edad, bloque de unidades) pegados en el orden de los
  objetivos y después `BuildOrderOptimizer`. El optimizador prueba cientos de simulaciones (en la web, segundos o
  decenas de segundos por cambio: no hay "en vivo") y "120 aldeanos hasta Imperial" no sale de ninguna plantilla.
- **Búsqueda exhaustiva:** probar muchas combinaciones de decisiones con el simulador. Da lo mejor, pero son miles de
  simulaciones de unos 60 ms cada una en el navegador (el Core corre en WebAssembly sin AOT).

## 2. Cómo se guarda

La BO suma un campo opcional `assisted`, sin subir `schema_version` (como `posture` y `branches`: las versiones viejas
lo ignoran). Los pasos generados **también se guardan** en `steps`: la app, el coach en la partida y las versiones
viejas usan la BO como cualquier otra sin saber nada del generador.

```json
"assisted": {
  "opening": { "houses": true, "sheep": 6, "boar": true, "berries": "auto" },
  "villagers": { "until": 120 },
  "eco_upgrades": true,
  "goals": [
    { "age": "feudal_age" },
    { "unit": "scout_cavalry", "count": 20, "buildings": 2 },
    { "age": "castle_age", "at_pop": 32 },
    { "town_centers": 3 },
    { "tech": "bloodlines" },
    { "age": "imperial_age" }
  ]
}
```

- **De fondo** (no van en la lista):
  - `opening`: `houses` (las 2 casas con los 3 iniciales; se ignora en las civs sin casas), `sheep` (cuántos aldeanos
    van a las ovejas antes de abrir la madera, contando los iniciales), `boar` (cazar los jabalíes), `berries`
    (`"auto"`: el generador abre las bayas cuando la comida lo pide; `"no"`: nunca).
  - `villagers.until`: aldeanos sin parar hasta ese total (de 1 a 200).
  - `eco_upgrades`: mejoras económicas automáticas.
  - Las casas son automáticas siempre (no hay opción).
- **La lista** (`goals`), en orden de prioridad. Cada objetivo es un registro plano con campos opcionales (los
  serializadores generados al compilar se llevan mal con tipos que heredan) y tiene exactamente uno de estos:
  - `age`: `feudal_age`, `castle_age` o `imperial_age`. Lo antes posible, o `at_pop` para fijar la población del clic.
  - `unit` con `count` (de 1 a 200) y `buildings` (de 1 a 10, cuántos edificios la producen a la vez).
  - `town_centers`: cuántos centros urbanos en total (de 2 a 10).
  - `tech`: una tecnología de game_data.
- Lo que el juego exige y no se pidió lo agrega el generador: los 2 edificios para subir de edad (prefiriendo los que
  los objetivos ya piden más abajo, como el establo para Castillos; si no, los más baratos), el campamento antes de
  mandar gente a la madera, al oro o a la piedra (o lo que cuenta como campamento en la civ: asentamiento, carro de
  mulas), el molino, el edificio de una tecnología.
- La **postura de cierre** sale de los objetivos: aldeanos (si no se llegó a `until`) más la última unidad de la lista
  en cola, y la edad siguiente a la última de la lista. Se escribe en `posture`.
- Al abrir una BO con `assisted`, la web abre el modo asistido y la regenera. Si los pasos generados son distintos de
  los guardados (el generador cambió), avisa "Se recalculó con el generador nuevo".
- **"Pasar a manual"** borra `assisted` y deja los pasos para editarlos como hoy. Una BO manual no vuelve a asistida:
  los objetivos no se pueden adivinar desde los pasos.
- El **porqué** de cada paso ("granjas: las ovejas se acaban a las 6:10") va en la respuesta del análisis que muestra
  la web, no en las notas de la BO: las notas salen en el overlay en la partida y no conviene llenarlo.
- Las ramas (`branches`) de una BO asistida se conservan sin editarse, como en el editor manual.

## 3. Cómo decide el generador

**Cuándo decide.** En cada nacimiento (cada aldeano o unidad nueva) y cuando la BO se queda sin pasos por arrancar. Es
como se escriben las guías ("aldeano 7: campamento maderero"). Si decide esperar algo (las granjas recién cuando se
acaban las ovejas), el paso que agrega más tarde lleva `at_pop` con la población de ese momento. Así la BO generada,
simulada de nuevo sin el generador, da exactamente los mismos tiempos: cada paso queda alcanzado en el mismo segundo
en que el generador lo agregó. Quien lo fija es el Planner, no el generador (sub-paso b): con `at_pop` si en ese
segundo nació alguien (la población llegó recién ahí a ese número) y con `at_time` si no (un paso de aldeanos
agregado cuando arranca un edificio que esperaba madera, por ejemplo). Si lo agregado vuelve a dejar la BO sin pasos
por arrancar, el Planner pregunta otra vez en el mismo segundo. También pregunta a la hora que la política pide (`IStepPolicy.WakeAt`, decidido en el sub-paso c):
mientras el centro urbano investiga una edad no nace nadie y la comida igual se acaba, o el costo de la edad se junta
sin que nazca nadie; lo que agrega entonces lleva `at_time`.

**Qué mira en cada decisión, en este orden:**

1. **Casas:** si la población más lo que está en producción llega al tope menos un margen (3 con un centro urbano, uno
   más por cada edificio que produce sin parar), una casa con un aldeano de la madera que vuelve a la madera.
2. **Comida que se acaba:** con la comida finita (sección 4) sabe cuándo se agota cada fuente. Con el tiempo justo
   para tener lista la siguiente, la arma según la apertura: ovejas, jabalí, bayas (con molino), granjas. Los aldeanos
   de la fuente agotada se mueven a la siguiente; los que van a granjas hacen cada uno la suya (60 de madera).
3. **Objetivo activo** (el primero de la lista sin terminar):
   - *Edad:* junta el costo con prioridad, se asegura los 2 edificios que pide la edad y hace clic apenas alcanza (o a
     `at_pop`). Lo que necesita la edad nueva (el establo) se agrega mientras sube y espera la llegada (el Planner ya
     lo marca como espera, no como trabón).
   - *Unidades:* construye los N edificios cuando la edad lo permite y pone las unidades en cola en todos a la vez.
   - *Centros urbanos:* los construye (275 de madera y 100 de piedra cada uno, desde Castillos: manda gente a la piedra
     y hace el campamento minero si falta) y suma su producción de aldeanos.
   - *Tecnología:* el edificio si falta, y la investiga.
4. **Lo que sigue en la lista** usa lo que sobra: el Planner ya le da prioridad sobre los recursos a lo que va antes
   (`Pays` y `_claimed`), así que Castillos se junta con lo que no gastan los exploradores. Un objetivo empieza a
   trabajarse cuando el anterior ya arrancó todos sus pasos.
5. **Aldeanos sin parar:** cada centro urbano entrena hasta llegar a `until` (salvo mientras investiga: el telar, la
   carretilla y las edades frenan los aldeanos, como en el juego). Cada aldeano nuevo va al recurso que más va a faltar:
   - Horizonte H: hasta la hora estimada del objetivo activo, entre 60 y 180 s.
   - Demanda en H: lo que falta pagar del objetivo activo y del siguiente, más la producción continua (aldeanos de cada
     centro urbano y unidades en cola en cada edificio, costo dividido tiempo) y las resiembras de las granjas.
   - Lo que entra en H: el stock más los aldeanos de cada recurso por su tasa (`GatherModel`) por H.
   - El aldeano va al recurso con más segundos de trabajo faltantes (faltante dividido la tasa de un aldeano). Sin
     faltantes, a la madera. La comida va a la fuente de comida que corresponde según la apertura y lo que queda.
   - Antes de mandar al primer aldeano a un recurso nuevo, su campamento.
   - Es la idea de la guía dinámica de la partida aplicada hacia adelante. Los números (horizonte, márgenes) se ajustan
     con las pruebas contra las guías (sección 9).
6. **Mejoras eco automáticas** (si `eco_upgrades`): con los umbrales de `TechReminders` (hachas con 3 en madera, collera
   con 6 granjeros, minería con 3 en oro, etc.) y con prioridad debajo del objetivo activo. El telar va antes del clic
   de Feudal. La carretilla y la carreta, que frenan al centro urbano, con 15 aldeanos o más y nunca entre el momento
   en que se junta el costo de una edad y su clic.

**Pasos que salen: por tramos** (decisión del usuario del 2026-10-26, después de la revisión del sub-paso b: con un
paso por aldeano, cada uno salía fijado con `at_pop`, no se podían juntar y la BO se acercaba al tope de 300 pasos).
- *Cuándo arma un tramo:* cuando ya no quedan aldeanos por encolar de los pasos de aldeanos anteriores, en el
  nacimiento en que se encola el último (o al arrancar). El centro urbano no para: el aldeano que está saliendo da 25 s
  de margen.
- *De qué largo:* los aldeanos que salen hasta lo próximo que el generador va a necesitar hacer (una casa, un
  campamento, el molino, las granjas, el clic de edad), con un tope de 6. Se reparten por lo que más va a faltar en
  ese tiempo (regla 5) y sale un paso por recurso, en orden ("Aldeanos 7 a 10 → madera"). El primero lleva la única
  fijación del tramo; los siguientes quedan alcanzados detrás de él y no la necesitan.
- *Edificios con el tramo:* el campamento de un recurso nuevo lo construye el primer aldeano que va ahí, como arma el
  editor manual ("Aldeano 7: campamento maderero → madera", D8 (7)); el molino antes de las bayas o las granjas.
- No hace falta una pasada que junte pasos: los tramos ya salen juntos.
- El coach no muestra "a N de población" en un paso de aldeanos (`StepText` los escribe como "N aldeanos en X");
  "A N de población (vas N-1)" sale solo mientras se termina el aldeano anterior, que es lo que pasa.

**Cuando no se puede:**
- Un objetivo que la civ no tiene (incas sin establo, `GameData.CanMake`) queda en rojo con el motivo ("Los incas no
  tienen establo") y se saltea; los de abajo siguen.
- Uno que no se cumple antes de `PlannerSettings.MaxTime` dice "No llega antes de 1:00:00".
- Imperial necesita su requisito de edificios: se suma `imperial_age` a `BuildOrderValidator.AgeUpRequirements` (2 de
  universidad, monasterio y taller de asedio, o un castillo), que también le sirve al validador.

## 4. Comida finita

Opción del Planner, `PlannerSettings.Food` (null = infinita, como hoy; el editor manual, el optimizador, las BO por civ,
el coach y el replanificador en vivo siguen así). Prendida en el modo asistido:

- **Cantidades:** la comida de cada objeto sale del `.dat` con el extractor a game_data (`food_per_object`: oveja 100,
  jabalí 340, arbusto de bayas 125, granja 175, verificado el 2026-10-26) y lo que suma cada tecnología a la granja
  (`farm_food`: collera +75, arado pesado +125). La rotación de cultivos (+175) no es una entidad de game_data todavía:
  entra en el sub-paso (d) con las mejoras eco de Castillos. Cuántos objetos hay de cada uno va por mapa en
  `data/maps.json` (`food`: ovejas, jabalíes, arbustos; por defecto los de Arabia, 8, 2 y 6, para que el usuario los
  revise como el resto del archivo). Los ciervos no cuentan.
- **Simulación:** cada fuente lleva lo que le queda y lo baja con lo que se recolecta. Los aldeanos de una fuente
  agotada quedan **parados en esa fuente** (no juntan nada) hasta que un paso los mueve, así un "mover de ovejas al
  jabalí" de la BO los encuentra donde estaban; queda registrado en `PlanResult.Idle` ("6 aldeanos parados: se
  acabaron las ovejas a las 6:10").
- **Granjas:** cada granjero tiene su granja (el Planner lleva la comida de una granja por granjero; el que llega a la
  granja sin un paso de granjas también trae la suya, y el generador hace siempre una por granjero). El que llega con
  un paso de granjas ya alcanzado y sin empezar que la hace desde la granja la espera sin juntar (uno que la BO todavía
  no alcanzó no cuenta: si contara, la BO generada simulada de nuevo daría otra cosa) (`IdleReason.WaitingFarm`; revisión
  del 2026-10-26: con la comida infinita juntaba de una granja que no existía). Al agotarse se
  resiembra sola cobrando la madera de una granja con el costo de la civ (60 sin bonificaciones), antes que cualquier
  otro gasto, como la cola de resiembra del molino. Sin madera, el granjero espera y queda registrado. Las mejoras de
  granja valen para las granjas nuevas y las resembradas, como en el juego.
- Pesca, ciervos y mercado no entran en la primera versión (sección 11).

## 5. Piezas del Core

- **`Engine/Planner.cs`**, dos agregados apagados por defecto (nada de lo que existe cambia):
  - La comida finita (sección 4).
  - `IStepPolicy`: en cada punto de decisión el Planner le pasa una vista de solo lectura (`PlanView`: hora, stock,
    población y tope, aldeanos por fuente y sin tarea, edificios hechos y en obra, edad, tecnologías hechas, colas de
    cada edificio, comida que queda en cada fuente, los pasos agregados hasta ahora y su estado) y agrega al final los
    pasos que devuelve.
- **`Core/Assisted/`** (nuevo), en piezas chicas que se prueban solas:
  - `AssistedPlan` y `AssistedGoal`: el modelo del campo `assisted`, en `CoreJsonContext`. `BuildOrder.Assisted`.
  - `GoalPolicy : IStepPolicy`, armada con una regla por tema (casas, comida, objetivo activo, reparto de aldeanos,
    mejoras eco), cada una en su archivo.
  - `AssistedGenerator.Generate(bo)`: devuelve la BO con sus pasos y su postura, el porqué de cada paso (por id), la
    hora en que se cumple cada objetivo (o null) y los problemas (lo que no se puede o no llega).
- **`BuildOrderValidator`:** reglas de `assisted` (cada objetivo con un solo tipo, claves que existen, rangos de la
  sección 2) y el requisito de Imperial. Un objetivo que la civ no tiene es un problema del generador, no un error de
  validación (la BO se puede guardar igual).
- **`BuildOrderAnalysis.Analyze`:** si la BO trae `assisted`, primero genera y después analiza la BO generada como
  siempre, con la comida finita. La respuesta suma `assisted`: los porqués por paso, la hora de cada objetivo, los
  problemas y la versión del generador. La web sigue llamando a `Analyze` (no hace falta otra función exportada).
- **Tiempo:** cada cambio cuesta una simulación con la política más la de `Analyze`. La política no simula nada adentro:
  solo cuentas directas sobre la vista. Meta: menos de 300 ms en la web para "120 aldeanos hasta Imperial" con 5
  objetivos. Si no llega, el Core pasa a un Web Worker (en el sub-paso e).
- `ArquitecturaTests` sigue valiendo: todo es `net10.0` puro.

## 6. La web (pantalla A)

En `/subir`, arriba de todo, las pestañas **Asistido** y **Manual**. Una BO nueva arranca en Asistido con objetivos de
ejemplo (aldeanos hasta 120, Feudal, Castillos); una BO cargada sin `assisted` abre en Manual. Los datos de la BO
(nombre, civ, mapa, autor, descripción, etiquetas) son los mismos en las dos pestañas.

Asistido, en dos columnas (en PC):

- **Izquierda, los objetivos:**
  - Tarjeta "De fondo": la apertura en una línea con "Cambiar" (abre sus opciones), "Aldeanos sin parar hasta" con
    deslizador y número, "Mejoras eco automáticas" y "Casas (siempre)", marcada y en gris porque no se puede apagar.
  - "Objetivos, en orden de prioridad": una fila por objetivo con agarradera para arrastrar, número, nombre, sus
    controles (la edad: "lo antes posible" o "a N de población"; las unidades: − cantidad + "en" − edificios +), la
    hora en que se cumple y, debajo, la diferencia con el cálculo anterior (verde si se adelantó, rojo si se atrasó).
    Un objetivo que no se puede o no llega, en rojo con el motivo. Con el teclado, mover arriba y abajo.
  - "+ Agregar objetivo": edad, unidades, centros urbanos o tecnología (solo lo que la civ puede hacer, con el filtro
    de la paleta de hoy).
  - "Pasar a manual", con confirmación.
- **Derecha, el resultado:**
  - Franja **"Último cambio"**: qué se tocó ("establos 2 a 3") y cómo se corrió cada objetivo ("Castillos 18:20 a
    19:00 (+0:40)"). Sale de comparar las horas de los objetivos con las del análisis anterior; se borra al cargar
    otra BO.
  - La línea de tiempo de hoy (`EditorTimeline`) **solo para mirar** (sin arrastre ni paleta), con una línea vertical
    por cada objetivo cumplido.
  - Los pasos generados escritos como en las guías (`guideLabels`), con su porqué en gris, solo para leer.
- **Celular** (debajo de 640 px): una columna con lo de fondo, la lista de objetivos, la franja, la línea de tiempo
  (se desliza de costado) y los pasos.
- Cada cambio pide un análisis nuevo; los toques seguidos de − y + se juntan (se espera a que dejen de llegar unos
  150 ms) para no simular de más.
- Publicar y descargar funcionan igual que en Manual.

## 7. La app

- Una BO asistida se abre, se sugiere y se juega como cualquiera: el coach usa los pasos.
- `assisted` se conserva al guardar si no se tocaron los pasos.
- Si en el editor de la app se cambia algún paso, al guardar avisa "Esta BO era asistida: pasa a manual" y borra
  `assisted`, así la web no pisa el cambio al regenerarla.
- El modo asistido dentro de la app queda para después (sección 11).

## 8. Errores y casos límite

- Objetivo que la civ no tiene, o que no llega antes de una hora: en rojo con el motivo; los demás siguen (sección 3).
- La civ cambia y un objetivo deja de existir (de francos a incas con exploradores): queda en rojo, no se borra.
- Una BO con `assisted` que no valida (un JSON tocado a mano): se muestra el error como en el editor manual y no se
  genera.
- Mapa con agua: el generador juega en tierra igual (la pesca queda para después) y no avisa nada.
- Civs especiales: sin casas (hunos) no hace casas; con asentamiento o carro de mulas los usa como campamento; con
  aldeanos de más al arrancar (chinos, mayas) los cuenta en `sheep`. El centro urbano en Feudal de los cumanos no lo
  conoce el validador y queda afuera.
- Si la simulación de `Analyze` diera otros tiempos que los del generador (no debería: lo controla el test de
  equivalencia), la web muestra los de `Analyze`, que son los que va a ver el coach.

## 9. Pruebas

Cada cambio con un test que falle primero, en su propio commit.

- **Reglas por separado** (Core.Tests), con vistas armadas a mano: "con 14 de 15 de población y un aldeano en cola,
  pide casa", "las ovejas se acaban en 40 s: arma el jabalí", "con el costo de Feudal juntado y un solo edificio de la
  Edad Oscura, agrega el que falta antes del clic".
- **Comida finita:** con la opción apagada, las 127 BO dan los mismos tiempos que hoy; con la opción prendida, una BO
  que nunca saca a nadie de las ovejas deja aldeanos parados a la hora calculada; la resiembra cobra madera.
- **Equivalencia:** para una grilla de civs (francos, incas, hunos, chinos, armenios) y combinaciones de objetivos, la
  BO generada valida sin errores, se simula completa y, simulada de nuevo sin el generador, da los mismos tiempos paso
  por paso.
- **Sentido común:** más edificios nunca terminan las unidades más tarde; agregar un objetivo al final no atrasa a los
  de arriba más de 10 s; ningún aldeano queda parado por una fuente agotada; el centro urbano no para salvo cuando
  investiga o cuando se llegó a `until`.
- **Contra las guías:** "aldeanos + Feudal lo antes posible" da un clic de Feudal a ±60 s del `uptime` teórico de las
  guías de `tests/fixtures/buildorderguide` de la misma civ.
- **Tiempo:** un test en Node con el Core en WebAssembly (lo corre `npm test`) mide "120 aldeanos hasta Imperial con 5
  objetivos" y falla si pasa de 300 ms.
- **Web** (vitest): el modelo de la pantalla (agregar, quitar y reordenar objetivos; la franja de diferencias; pasar a
  manual) y la ida y vuelta de una BO asistida por `Analyze`.

## 10. Sub-pasos y criterio de salida

De a uno, como D10, cada uno con su plan en `docs/superpowers/plans/`:

- **(a)** Comida finita en el Planner, como opción, con las cantidades del extractor y de `maps.json`.
- **(b)** `IStepPolicy` en el Planner, con una política de prueba y el test de equivalencia.
- **(c)** El generador base: `AssistedPlan`, validador (y el requisito de Imperial), apertura, aldeanos hasta N por
  tramos, casas, comida, campamentos y edades. Antes, lo que pidió la revisión del (b): `PlanView` con propiedades con
  valores por defecto (para armar fotos a mano en los tests de cada regla), una guarda para cantidades menores a 1 en
  lo que agrega la política, no fijar un paso cuyas condiciones todavía no se cumplen, y el aviso "la población nunca
  llega a N" del validador desde el arranque de la civ (no del estándar).
- **(d)** El resto de los objetivos: unidades con sus edificios, centros urbanos extra, tecnologías a pedido y mejoras
  eco automáticas.
- **(e)** `Analyze` y la web: pestaña Asistido con la pantalla A, la franja "Último cambio", el celular y "Pasar a
  manual" (y el Web Worker si hace falta).
- **(f)** La app: aviso al editar una BO asistida y paso a manual al guardar.

**Criterio de salida:** en `/subir`, una BO nueva en Asistido con francos y "aldeanos hasta 120, Feudal, 20
exploradores con 2 establos, Castillos e Imperial" se recalcula en menos de medio segundo por cambio; pasar de 2 a 3
establos muestra la franja con lo que se corrió; la BO generada valida sin errores, no deja aldeanos parados, se
publica y se abre en la app y en el coach como cualquier otra; y "Pasar a manual" la deja editable en el editor de hoy.

## 11. Después (fuera de esta versión)

- Prender la comida finita en todo (editor manual, optimizador, BO por civ), mirando cómo cambian los tiempos.
- Pesca y barcos en mapas de agua, ciervos, mercado y comercio, arranque nómada.
- El modo asistido en el editor de la app.
- Margen "humano" (tiempos más lentos que los del simulador) como opción.
- Centro urbano en Feudal de los cumanos y otras bonificaciones que el validador no conoce.
