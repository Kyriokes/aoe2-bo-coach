# La BO por objetivos (borrador de diseño)

Estado: borrador para discutir con el usuario. No hay código escrito. Fecha: 2026-11-02.

## 1. Por qué

Hoy una BO es una lista de pasos ("hacé X") y el coach tiene que confirmar cada paso con fuentes ruidosas (OCR del log, cola, puntaje, tope de población). Cada partida encuentra un caso nuevo (granjas que el juego no avisa, dos galerías con un solo aviso, telar que no se ve) y se agrega un parche. Los parches que ya existen intentan reconstruir un estado objetivo desde la lista: `group`, `after`, `at_pop`, `from`, `Lanes`, `StepTarget`, `NowTarget`, `PlannedVillagers`, `HudTargets`, `CapTargets`, `Posture`, `GuideDistribution`.

El problema de fondo: se exige certeza sobre eventos puntuales, y el juego solo regala certeza sobre conteos (edad, población, aldeanos por recurso, stock, edificios, cola). Además conviven dos modelos de la economía (los conteos de aldeanos de la guía humana y las tasas de nuestro simulador), y cuando discrepan el coach pide mover aldeanos sin que se sepa quién tiene razón.

Meta: dejar de parchar. Que una situación nueva del juego no pida una regla nueva.

## 2. Principios

1. **La BO dice qué lograr, no qué hacer paso a paso.** El camino lo calcula el motor desde el estado real.
2. **Una sola fuente de verdad para la economía:** el simulador. Las guías humanas se importan como objetivos y como referencia, no como segundo modelo.
3. **Un error de lectura y un error del jugador son lo mismo:** el estado real no es el esperado. Los dos se resuelven recalculando desde el estado actual.
4. **El coach nunca pide algo imposible según lo que cree.** Si falta un recurso o un edificio, pide eso.
5. **Al jugador, una sola cosa importante por vez**, y lo crítico nunca es un renglón chico.

## 3. Arquitectura en tres capas

### 3.1 Creencia del estado (no sabe nada de BO)
- Fusiona todas las fuentes: OCR del HUD, log, cola de producción, puntaje.
- Cada fuente aporta evidencia con distinta fuerza. Si hay contradicción, gana la evidencia más fuerte y se replanifica todo desde la creencia corregida. Ejemplo: el log avisa una galería y la cola muestra dos arqueros a la vez, entonces hay dos.
- Reemplaza a `ScoreLedger`, `BuildingsFromQueue`, `FinishedInQueue` y las reglas de granjas/telar sueltas del `Tracker`. Se reaprovecha su conocimiento, no su forma.

### 3.2 Intención (la BO)
Una cadena ordenada de **objetivos**. Cada uno es un estado deseado con condiciones:
- Ejemplos: "4 exploradores en Feudal"; después "33 arqueros a caballo con todas las mejoras de galería, establo, herrería y universidad".
- Los objetivos son inamovibles y se cumplen en orden. Mientras el activo no se cumple, todo apunta a él desde el 0:00. Al cumplirse, cambian las prioridades hacia el siguiente.
- Importa la cantidad y las condiciones, no cómo se llega (2, 3 o 4 galerías da igual).
- Ya existe en embrión: el modo asistido del editor (D12: `AssistedGenerator`, `GoalPolicy`, `IStepPolicy`, `PlanView`) genera una BO desde objetivos. La propuesta es subirlo al centro del coach en vivo.

Borrador ilustrativo del formato (no definitivo):

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

### 3.3 Decisión y presentación
- Desde la creencia y el objetivo activo se calcula la brecha y el camino más corto.
- Se simula hacia adelante (el Planner arrancado desde el estado vivo; hay que verificar que se pueda) para detectar **colisiones**: "en 40 s vas a necesitar 300 de madera y con este reparto tenés 180".
- Los planes B salen de comparar 2 o 3 alternativas simuladas contra el objetivo activo. Ejemplo: 458/500 de comida, ¿clic ya o un aldeano más? Se simulan las dos y se compara cuándo se cumple el objetivo. Los números del ejemplo (cuenta aproximada con 14 granjeros a 0,32): faltan 42, o sea unos 9 s con el centro urbano parado; el aldeano extra cuesta 50 y ocupa el centro urbano 25 s. Cuál conviene depende del objetivo, por eso el objetivo es parte de la BO.
- La misma lógica contesta el caso del aldeano muerto: se compara reponerlo y subir después contra subir ya, midiendo contra el objetivo. Un empate dentro de unos pocos segundos no tiene respuesta confiable, y en ese caso se muestra el empate y no se inventa una orden.

## 4. Prioridades

Escalera general, del sistema (no de cada BO):

1. **Crítico**, renglón grande y rojo: casa por ritmo de producción, centro urbano parado mientras faltan aldeanos, clic de edad cuando ya se puede, colisión inminente.
2. **Ahora**: la acción principal, una sola.
3. **Después**: lo que viene en los próximos 30 a 60 s.
4. **De fondo**: tecnologías y mejoras donde no importa demorarse un poco.

El clic de edad pasa a crítico apenas se cumple su condición (el objetivo activo lo exige y hay recursos y edificios), no queda como un renglón chico.

## 5. Casas por ritmo de producción

Hoy avisa con 3 lugares libres, lo que sirve con un solo centro urbano y llega tarde con varias galerías. Regla propuesta:

- **ritmo de consumo** = suma, por edificio que produce, de (población de la unidad / tiempo de entrenamiento con la velocidad del edificio). Sale de `game_data` y de la cola de producción real.
- **tiempo de reacción** = tiempo de construcción de la casa (25 s) + caminata + tiempo del jugador (unos 10 s, a calibrar).
- Aviso cuando `lugares libres < ritmo de consumo * tiempo de reacción`, descontando las casas que ya están en construcción si se pueden saber.

Ejemplos aproximados: un centro urbano solo consume 0,04 por segundo y 3 lugares dan 75 s de margen (alcanza). Centro urbano, 3 galerías y establo consumen cerca de 0,16 por segundo: la cuenta da unos 6 lugares, que coincide con la intuición del usuario. Es independiente del resto y se puede hacer primero.

## 6. Presentación

- Una acción principal.
- Un renglón crítico solo cuando hay algo crítico.
- Las colisiones se muestran como aviso con tiempo ("en 40 s te falta madera"), no como una orden de mover aldeanos sin explicación.
- La GD se calcula desde el simulador y el objetivo activo (la BO humana deja de ser el segundo modelo).

## 7. Consecuencias para JSON, base y web

- `schema_version` 3: `meta + goals[] + tactics[]`. Los pasos dejan de ser el formato y pasan a ser un resultado derivado.
- Migración de las 129 BO: se extraen sus objetivos automáticamente y se compara lo que genera el motor contra su tiempo real. Es además la prueba del generador (hoy Castillos e Imperial como objetivo llegan no más tarde que las 129 BO, Feudal solo todavía no).
- Cambia el esquema de Supabase (`build_orders`, columnas generadas, `build_order_style`), la API de la app, el editor de la web y la app (que hoy solo es caché), el overlay y el informe.
- Versiones viejas: la web y la app leen schema 1, 2 y 3 durante la transición.

## 8. Sub-proyectos, en orden sugerido

1. Casas por ritmo de producción (independiente, gana ya).
2. Creencia del estado (fusión y corrección de fuentes).
3. Formato de objetivos y generador desde el estado vivo (incluye verificar que el Planner arranque desde un estado vivo).
4. Presentación en capas y colisiones.
5. Migración de JSON, base, web y las 129 BO.

Cada uno tiene su propio spec, plan y versión.

## 9. Riesgos

- Hay conocimiento humano que no se deriva del simulador (atraer ciervos y jabalíes, micro). Va en `tactics`.
- La calidad del modelo económico pasa a ser todo. Sin tasas buenas, el motor da malos consejos con más confianza que hoy.
- Un motor que replanifica puede titilar. Hace falta histéresis (ver el glosario): se conserva la recomendación actual salvo que la nueva sea claramente mejor o se sostenga un tiempo. Ya está hecho para las sugerencias (`SuggestionGate`: dura 8 s, la cantidad cambia si se sostiene 6 s).
- Costo de cómputo de simular hacia adelante en vivo: en WebAssembly un análisis completo tarda unos 60 ms, en nativo debería sobrar. Verificar.

## 10. La apertura (decidido)

La apertura estándar (6 a comida contando los 3 iniciales, 2 casas con los iniciales, 4 a madera, el primer jabalí, bayas con molino) es una práctica de 26 años y sigue vigente en mid-elo (video "Your First Build Order - AoE2 New Player Guide", 8 años). Decisión del usuario del 2026-11-02:

- Es una **plantilla estándar**. El generador **no la modifica solo**: solo propone variantes y, si encuentra una mejora en la estándar, se muestra con el número y decide el usuario.
- **Se expresa como una cola de destinos:** adónde va cada aldeano según el número que nace ("hasta 6 a ovejas, el siguiente construye el campamento maderero, 3 más a madera, el siguiente al jabalí, el 13 construye el molino..."), más reglas de edificio por aldeano. No como pasos con hora.
- **Los hitos de tiempo salen de la producción continua del centro urbano:** población esperada = 4 + t/25 (3 aldeanos y el explorador; 11 de población a las 3:00 es 7 aldeanos por 25 s, 17 a las 5:30 es 13 por 25 s, con unos segundos de margen). Estar por debajo es "centro urbano parado" (crítico). A confirmar contra el reloj real.
- **Las civs con bonificaciones se agrupan desde `CivBonuses`**, sin lista a mano. Casos de prueba de aceptación: mayas (arrancan en 5/5 de población, no pueden entrenar: telar primero con los 4 aldeanos y dos casas), chinos (6 aldeanos y sin comida: dos casas y telar, los 6 a las ovejas lo antes posible, el primero que sale va a madera) y hunos (sin casas: los iniciales van directo a la comida). El generador tiene que reproducir las dos primeras sin reglas especiales. El "scalp 4 sheep" de los hunos es una táctica (va en `tactics`), no se deriva.
- **Hitos verificables e informativos:** los verificables salen del HUD (población, aldeanos por recurso, edad, edificios). Los informativos no los ve la pantalla ("encontrar las ovejas a los 30 a 45 s", "haber encontrado todos los recursos a los 4:00"); se muestran como referencia o se infieren por señales indirectas (aldeanos trabajando en ovejas o caza), sin exigirlos.

## 11. Registro de partida y envío (decidido)

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
- Costo y almacenamiento (Supabase gratis) a medir antes de prenderlo.

## Glosario

- **Histéresis:** tener dos umbrales distintos para cambiar y para volver, o exigir que el cambio se sostenga, para que una decisión no salte de un lado al otro por una diferencia mínima.
