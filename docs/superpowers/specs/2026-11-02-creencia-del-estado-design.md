# La creencia del estado (escalón 2 de la BO por objetivos)

Estado: diseño del 2026-11-02, escalón 2 de `2026-11-02-bo-por-objetivos-design.md` (sección 4.2). Se implementa con el plan `docs/superpowers/plans/2026-11-02-creencia-del-estado.md`.

## 1. Qué es

Una sola capa en `BoCoach.Core` (`Engine/Belief/`) que fusiona lo que las fuentes de la pantalla dicen de la partida y entrega **lo que el coach cree que pasó**: edificios por clave, investigaciones hechas y en curso, unidades, aldeanos, población y edad. No sabe nada de la BO: recibe lecturas y devuelve creencias. Cada fuente aporta evidencia con una fuerza; ante una contradicción gana la más fuerte y lo creído se corrige.

Hoy ese conocimiento está repartido en cuatro lugares con cuatro formas distintas: `ScoreLedger` (edificios repetidos por el puntaje), `SnapshotBuilder.BuildingsFromQueue` y `BuildingsFromProduction` (edificios por la cola y por el ritmo de las unidades), `Coach.FinishedInQueue` y `ResearchInQueue` (investigaciones por la cola) y las reglas sueltas del `Tracker` (`FreeAt` para las tecnologías gratis de la civ, `MarkSeenDone` para lo visto en la cola). Cada una deduplica a su manera lo que el log avisa después. La creencia junta todo eso con una sola regla de deduplicación y un solo orden de fuerza.

## 2. Fuentes y fuerza

| Fuente | Qué dice | Fuerza |
|---|---|---|
| HUD (total de aldeanos, aldeanos por recurso, "N/M", stock, inactivos) | valores directos, ya suavizados | **Directa** (4) |
| Log de eventos | "X Built", "X Created", "X Researched": existe al menos uno más; un mensaje repetido no agrega renglón, así que es una **cota inferior**, no una cuenta | **Avisada** (3) |
| Regla de la civ | una tecnología que a la civ le sale gratis queda hecha al llegar a su edad | **Avisada** (3) |
| Cola de producción (dos lecturas seguidas) | N íconos iguales que no esperan = al menos N edificios que los producen; una tecnología en la cola está en curso; una que estuvo y ya no está, pasado el 90 % de su tiempo, terminó | **Observada** (2) |
| Tope de población | una suba que ningún edificio avisado explica es una casa | **Observada** (2) |
| Ritmo de las unidades | más unidades iguales en la mitad de su tiempo de entrenamiento que edificios conocidos = más edificios | **Observada** (2) |
| Población menos aldeanos | las unidades militares que el log no mostró (salieron juntas) | **Inferida** (1) |
| Panel de puntajes | una suba sin explicar del 20 % del costo del último edificio avisado = otro edificio igual | **Inferida** (1) |

La fuerza decide dos cosas:

1. **Qué se rechaza.** Una evidencia no puede afirmar algo que una creencia de fuerza mayor hace imposible. El caso concreto: la cola muestra arqueros avanzando (Observada) y la edad creída por el log (Avisada) es la Edad Oscura, donde no hay galerías. Se rechaza el ícono: en la partida 20261101-230201 una milicia leída como arquero daba una galería a las 9:37, antes de Feudal, y después "Quedó sin hacer" la casa y el campamento minero.
2. **Quién corrige a quién.** Las cuentas (edificios, unidades) se arman con cotas inferiores: lo creído es el máximo de lo que cada fuente afirma. El log avisó una galería y la cola muestra dos arqueros a la vez que no esperan: hay dos. Nadie baja una cuenta (la creencia no ve edificios destruidos).

Lo que la pantalla no muestra sigue sin creerse: las granjas (el juego no avisa "Farm Built", `EventConventions.Unannounced`) no tienen fuente. La creencia lo declara (`StateBelief.CanObserve`) y el `Tracker` las sigue dando por hechas con el avance de la BO, que es conocimiento de la BO y no del estado.

## 3. Qué contiene la creencia (`BeliefView`)

Un registro inmutable por captura:

- `Buildings`: clave → cantidad creída (el centro urbano arranca en 1; el asentamiento de los incas suma a lo que vale, `Entity.CountsAs`).
- `Research`: clave → `InProgress` (desde cuándo, raw) o `Done`.
- `Units`: clave → cantidad creída (sin aldeanos).
- `Villagers`, `VillagersOn`, `Population`, `PopulationCap`: lo último leído del HUD (null si nunca).
- `Age`: la edad alcanzada según el log (una edad investigada, o cualquier entidad avisada de una edad mayor).

Y una lista de **completaciones nuevas** desde la captura anterior, como eventos (`GameEvent`, `Synthetic = true` cuando no salieron del log): es lo que `SnapshotBuilder` pone en `GameSnapshot.Events`, así `PlayerState` y `Tracker` siguen consumiendo eventos como hasta hoy.

## 4. Cómo fusiona

**Completaciones.** Cada cosa que termina (edificio, unidad, tecnología) es una completación con su fuente y fuerza. Las fuentes que no son el log producen completaciones **inferidas** que quedan un tiempo (`CreditSeconds`, 20 s; 2 s para lo que el log muestra al instante, como un edificio deducido por la cola) esperando el aviso del log: si llega, lo absorbe (el aviso no es otra cosa). Hoy eso lo hacen por separado `_inferredHouses`, `_inferredUnits` y `_unitCredits`; las investigaciones vistas en la cola no lo hacían (un "Loom Research Complete" tardío contaba dos veces).

**Orden dentro de una captura** (el de hoy, para no cambiar los minutos de cierre): primero el log; después el tope (casas); la cola (edificios y tecnologías, filtrados por la edad creída); el puntaje (con las completaciones de esta captura como crédito); por último la población menos los aldeanos (unidades). Al final se actualiza la edad y se dan por hechas las tecnologías gratis de la civ para esa edad.

**Edad.** Sube con lo avisado por el log (y lo que eso implica: una galería es de Feudal). No sube con un clic en curso ni con la cola. Es el filtro de la cola: un ícono de una unidad cuyo productor es de una edad mayor no cuenta.

**Investigaciones.** `InProgress` cuando la tecnología está en la cola en dos lecturas seguidas (desde la primera). `Done` cuando el log la avisa, cuando falta de la cola en dos lecturas seguidas pasado el 90 % de su tiempo con la civ desde que se la vio (antes de eso, que falte es una lectura mala y sigue en curso), o cuando a la civ le sale gratis y se llegó a su edad. El clic visto en el stock (baja del costo) queda donde está, en `Coach.DetectResearchClick`, porque elige entre los pasos de la BO; en un escalón siguiente puede entrar como evidencia Inferida.

**El jugador en el panel de puntajes** (`PlayerSlot`): como hoy, de la pantalla de carga o por votación.

## 5. Quién la consume

- **`SnapshotBuilder`** la crea (con `GameData` y la civ) y le pasa cada `ScreenReading` con el reloj crudo. Se queda con lo que no es creencia: el reloj y `StartOffset`, el stock que conserva su último valor, la estimación de "N/M" cuando no se lee. Pone en el `GameSnapshot` las completaciones nuevas (como eventos) y la `BeliefView` (`GameSnapshot.Belief`). Pierde `_buildings`, `_ledger`, `BuildingsFromQueue`, `BuildingsFromProduction`, `InferHousesFromCap`, `InferUnitsFromPopulation` y `LoggedUnit`, que pasan a la creencia con el mismo conocimiento.
- **`Coach`**: marca en curso los pasos de investigación cuya tecnología la creencia tiene `InProgress` (`ResearchInQueue` sin cola propia) y deja de tener `FinishedInQueue` y `_seenInQueue`: la tecnología terminada llega como evento `<tech>_researched` sintético y el `Tracker` la confirma por el camino de siempre ("Atrás" descarta el evento, como con cualquier confirmación). `DetectResearchClick`, `TrackTownCenter` (la cola para el centro urbano parado) y `Built` quedan.
- **`Tracker`**: pierde `FreeAt` y `MarkSeenDone` (las dos llegan como eventos). Las reglas de lo que el juego no avisa (granjas) quedan, y consultan `StateBelief.CanObserve` en vez de la lista de `EventConventions`.
- **`PlayerState`**: sin cambios. Sigue fusionando el total de aldeanos del HUD con el log (es una fusión más, que queda para el escalón siguiente porque tiene su propio camino cuando no hay `SnapshotBuilder`).

Un `GameSnapshot` armado a mano (tests, ramas) puede venir sin `Belief`: el coach no marca nada por la cola en ese caso. Los tests que fijaban casos reales con una cola armada a mano pasan a armar `ScreenReading` y a usar `SnapshotBuilder`, con el mismo caso.

## 6. Errores y límites

- Un total de aldeanos mayor que 200 se descarta (como hoy).
- Una suba de más de 6 unidades a la vez en la población es una lectura mala (como hoy).
- Un ícono "?" o `housed` de la cola no dice nada.
- La creencia nunca tira: una entidad que no está en game_data se ignora.

## 7. Verificación

- Tests de la creencia sola (`tests/BoCoach.Core.Tests/Engine/StateBeliefTests.cs`): los casos de la tabla de fuentes, la contradicción log contra cola, el rechazo por edad, la absorción del aviso tardío de cada fuente, la tecnología gratis.
- Los tests que fijan casos reales (`ScoreLedgerTests`, los de cola y producción en `CoachTests`, `SettlementTests`, `GameStartTests`) pasan igual o migrados al `SnapshotBuilder` con el mismo caso.
- Las grabaciones con cola y puntaje (`20261101-020049`, `20261101-125353`, `20261101-165020`, `20261101-230201`, `20261031-215314`, `20261018-132830`; la `20261018-011721` ya no está en la PC): los minutos de cierre de cada paso y los avisos de centro urbano parado contra la v0.9.82, con `ReplaySession coach`. Lo que cambie tiene que ser para mejor y explicarse por la regla nueva (el rechazo por edad en la 230201).
