# Coach por líneas paralelas

Fecha: 2026-10-30. Pedido del usuario después de la partida 20261029-220928 (británicos, Fast Archers mejorada).

## Problema

El coach sigue la BO como una sola fila: el paso actual es el primero abierto y todo lo demás espera. En la partida
20261029-220928:

- De 9:38 a 11:47 mostró "Investigar Hacha de doble filo · Ya se puede" con la Feudal todavía en curso (llegó a las
  10:31). El "ya se puede" (`Tracker.IsReady`) mira el orden de la BO, la población y los recursos, no la edad.
- De 12:56 a 17:27 mostró "Investigar Emplumado · Ya se puede" sin herrería (se construyó a las 16:42 y figuraba en
  "Atrasado").
- Si una tecnología no sale por falta de recursos, los aldeanos que siguen saliendo (26, 27...) no tienen a dónde ir:
  la indicación queda trabada en la tecnología.

Lo que pide el usuario: la BO no se traba nunca esperando una tecnología; las tecnologías van en paralelo; el coach
nunca muestra algo que no es de la edad en la que está el jugador; y siempre dice a dónde mandar el próximo aldeano.

## Decisiones (del usuario, 2026-10-29 y 30)

1. Líneas paralelas (opción B de tres: filtro sobre el orden actual, líneas, objetivos por edad).
2. El panel muestra un renglón fijo por línea (maqueta A, no una lista mezclada de "lo que toca ahora").
3. El destino de cada aldeano nuevo sale de los pasos de aldeanos de la BO en orden; si no hay ninguno de la edad del
   jugador, de la GD.

## Diseño

### 1. Las cuatro líneas

Cada paso de la BO cae en una sola línea (`Lane`) según su acción:

| Línea | Pasos |
|---|---|
| Aldeanos | `Train` de `villager`, y `Move` |
| Construir | `Build` (casas y granjas incluidas) |
| Investigar | `Research` (tecnologías y clics de edad) |
| Producir | `Train` de todo lo que no es aldeano |

Los pasos mixtos (`Step.Group`) se parten: cada meta va a su línea. El paso "aldeano 7: campamento maderero → madera"
del editor (paso de aldeanos + construir desde el destino a N de población) queda como un paso en Aldeanos y otro en
Construir; el de Construir espera la población N como hoy (`AtPop`).

### 2. Qué muestra cada renglón

El renglón de una línea es el primer paso de esa línea, en el orden de la BO, que no está hecho y que es **elegible**:

1. **Edad:** la edad que pide el paso (la de su entidad en game_data, o la edad anterior para un clic de edad) es la
   del jugador o una anterior. La edad del jugador es la que confirma el coach (pasos y log), no el clic: lo de la edad
   siguiente se destraba al llegar.
2. **Edificio:** si el paso investiga o entrena, existe el edificio que lo hace (`made_in`), contado como hoy
   (`PlayerState`: log, cola de producción, deducción por producción). Para construir, los requisitos de edificio de
   game_data (`requires`).
3. **Dependencias:** los pasos de `After` están hechos; `AtPop` y `AtTime` se cumplen.
4. **Clic de edad:** además, los 2 edificios de la edad que pide (`BuildOrderValidator.AgeUpRequirement`).

Un paso elegible al que solo le faltan recursos se muestra igual, en amarillo, con lo que falta y en cuánto llega (lo
que hoy dice "Faltan 20 de oro (en 0:52)"). No frena a las otras líneas.

Un paso no elegible no se muestra, y el renglón pasa al siguiente paso de su línea que sí lo sea. Lo que quedó sin hacer
de una edad anterior sigue adelante en su línea hasta que se haga. Desaparece el estado "Atrasado" (`StepState.Late`):
cada línea ya muestra lo primero que se debe.

**Fallback.** Si una línea no tiene pasos elegibles de la BO, muestra en gris lo que el coach ya hace hoy al terminar
la BO:

- Aldeanos: la GD ("Aldeano 27 → oro, según lo que falta").
- Construir: la casita cuando quedan 3 lugares de población o menos (`Coach.HouseMargin`).
- Investigar: las tecnologías que faltan (`TechReminders`).
- Producir: la postura (`Posture`: edificios parados, "1 arquero en cola en cada galería").

Si el fallback tampoco tiene nada, el renglón se oculta.

### 3. A qué paso se le cuenta cada cosa

La lectura de la partida no cambia (log, HUD, "N/M", cola de producción, clic por la baja del stock). Cambia la
asignación de lo que pasa a los pasos:

- **Aldeano que sale:** al paso que muestra el renglón de Aldeanos (el que dijo a dónde mandarlo). Los pasos de
  aldeanos contra el HUD (`Tracker.HudTargets`, `HudConfirmSeconds`) siguen igual dentro de la línea.
- **Edificio terminado:** al primer paso abierto de Construir con ese edificio, aunque no sea el del renglón.
- **Investigación terminada o empezada:** a su paso en Investigar, aunque no sea el del renglón (empezada = en curso,
  como hoy `StepState.InProgress`).
- **Unidades:** a Producir, como hoy (`Tracker.Producing`, "Seguí produciendo arqueros: 7/24").
- Las granjas que el juego no avisa (`EventConventions.Unannounced`) se dan por hechas cuando avanza un paso posterior
  **de Construir**, o cuando los aldeanos de su paso mixto llegan a granjas.

Lo que se hace antes de que la BO lo pida en su línea se marca hecho igual; el renglón sigue mostrando lo primero que
falta.

### 4. La GD y los repartos

- "Al terminar el paso" (`CoachView.StepTarget`) y "Ahora" (`NowTarget`) salen de los pasos de Aldeanos hasta el que
  muestra el renglón, no del paso actual.
- `GuideDistribution.Adjust` usa ese mismo objetivo como `keep`.
- Con el fallback a la GD no hay meta de la BO: no se muestran "Al terminar" ni "Ahora".

### 5. Lo que se va y lo que cambia en la vista

- `Tracker.Current`, `Next`, `Silent`, `CurrentGoals` y `StepState.Late` se reemplazan por `Tracker.Lanes`: un
  renglón por línea (paso, si es fallback, lo que falta).
- `CoachView` deja de tener paso actual, siguiente, atrasados y "hacé también", y suma los cuatro renglones.
- "Ya está", "Atrás" y "Saltear" (atajos y panel "Lo que ve el coach") actúan sobre la línea de Aldeanos, salvo que se
  elija otra en el panel. Los atajos Ctrl+Alt+→/←/↓ actúan sobre Aldeanos.
- BO abandonada (`BuildOrderAbandonment`): se detecta igual (militares que la BO no tiene, o 3 minutos sin avanzar
  ninguna línea); en ese caso todas las líneas pasan a su fallback.
- BO terminada: el panel compacto (`CoachLines.Card`) se arma con los fallbacks de las líneas.
- Las ramas por señales (`Branches`) van primero, como hoy, encima de los renglones.

### 6. El panel

Renglones fijos en este orden: Aldeanos, Construir, Investigar, Producir. Cada uno con su etiqueta y el texto de su paso
(`StepText`). Blanco si se puede hacer ya, amarillo si solo esperan recursos (con lo que falta), gris si es el
fallback. Un renglón sin nada se oculta y el panel se achica. Los avisos de arriba (centro urbano parado, casa,
inactivos, señales) y la GD en los casilleros no cambian.

### 7. Fuera de esta tanda

- El simulador (`Planner`) sigue igual: ya hace avanzar los aldeanos aunque otro paso espere; hacer que construir e
  investigar también vayan en paralelo cambiaría los horarios de las 127 BO, la web y el modo asistido. Se anota en la
  hoja de ruta.
- El formato de la BO no cambia (las líneas se deducen de la acción de cada paso). La web y el editor no cambian.
- El informe de la partida (`GameReport`) sigue comparando cuándo se hizo cada paso contra la BO bien jugada.

## Pruebas

Tests de motor con `Partida` (`tests/BoCoach.Core.Tests/Engine/Partida.cs`):

1. Con la Feudal en curso, Investigar no muestra el hacha de doble filo y Aldeanos sigue dando destinos; al llegar a
   Feudal, aparece el hacha.
2. Sin herrería, Investigar no muestra el emplumado (muestra el paso siguiente de la línea o el fallback); al
   terminarse la herrería, aparece.
3. El hacha esperando madera (amarillo, con lo que falta) mientras salen los aldeanos 26 y 27, cada uno con el destino
   de su paso de aldeanos.
4. Sin más pasos de aldeanos en la edad, el renglón de Aldeanos pasa a la GD (gris).
5. Un edificio hecho antes de tiempo marca su paso de Construir y el renglón sigue en el primero que falta.
6. Paso mixto partido: el aldeano en Aldeanos y el edificio en Construir, cada uno con su confirmación.

Contra grabaciones, con `ReplaySession coach <grabación> <bo> --guide`, antes y después con los minutos: la de scouts
(20261015-214512, fixture `lecturas.csv`), la de arqueros (20261018-132830) y la 20261029-220928. Se comprueba que no
quede ningún renglón trabado en algo de otra edad o sin su edificio, y que los pasos se sigan cerrando cuando
corresponde.

## Orden de trabajo

Cada parte en su commit, con la suite en verde:

1. Las líneas en el `Tracker` (elegibilidad, asignación, fallbacks), con los tests de motor.
2. `CoachView` y `Coach` con los cuatro renglones y los repartos.
3. `CoachText` y el overlay.
4. El panel "Lo que ve el coach", los botones y los atajos.
