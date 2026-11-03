# Registro de partida (local y envío)

Paso 2 del "camino al público" (`PLAN.md`, sección 11, ítem 15) y sección transversal 4.5 / 9 del spec de la BO por objetivos (`2026-11-02-bo-por-objetivos-design.md`), donde está lo que el usuario ya decidió: qué contesta el registro, qué contiene, consentimiento, envío, identidad, idempotencia, cola local y antiabuso. Este spec fija el formato y cómo se implementa.

## 1. Objetivo

Que cada partida con reloj deje un archivo chico con todo lo que el coach vio y dijo, de modo que (a) `ReplaySession coach` pueda correr el coach entero sobre él sin PNG y dar las mismas líneas y los mismos cierres que sobre la grabación, y (b) se pueda subir con consentimiento para que partidas ajenas mejoren el coach sin que nadie juegue.

## 2. Parte 1: el registro local

### 2.1 Archivo

- `%LOCALAPPDATA%\BoCoach\registros\<aaaammdd-hhmmss>.jsonl.gz`: JSON Lines (un objeto por renglón, UTF-8, `\n`) comprimido con gzip. El nombre es la hora local en que empezó la partida (primera lectura del reloj).
- Versionado: la primera línea es `meta` con `"schema": 1`. Un lector ignora las claves y los tipos de línea que no conoce (como las BO). Un archivo cortado (la app se cerró sin terminar) se lee hasta donde llega.
- Se escribe en vivo, línea por línea, y se vacía el búfer cada 10 s reales. Tamaño esperado: cientos de KB por partida (a medir sobre la grabación 20261102-212823; el número va a `CLAUDE.md`).
- Sin nombres de jugadores, sin pantalla de carga, sin los nombres del panel de puntajes, sin recortes de pantalla. Solo números, textos del HUD (reloj, edad), renglones del log tal cual los leyó el OCR (son mensajes del juego) y lo que mostró el overlay.

### 2.2 Líneas

Todas llevan `t` (tipo). Las de partida llevan `ms` (milisegundos reales desde que empezó a leerse esta partida) y, cuando hay reloj, `clock` (segundos del reloj del juego, crudo).

- `meta` (una vez, primero): `schema`, `game_id` (GUID aleatorio de esta partida), `started` (hora local ISO), `app` (texto de `AppVersion`, o "ReplaySession"), `game_data` (`GameData.DatVersion`), `hud` (nombre del diseño del HUD, o null), `hud_scale`, `civ`, `bo` (código o id de la BO), `bo_name`, `map` (null si no se leyó), `rival_civ` (la del rival de enfrente, null si no se sabe), `slot` (número del jugador, o null), `language`, `interval_ms`.
- `cap` (una por captura leída, tenga o no reloj): lo crudo antes de suavizar, con `null` donde no se leyó.
  - `clock`, `food`, `wood`, `gold`, `stone`, `vil` (total), `idle`, `pop`, `cap` (N y M de "N/M").
  - `on`: `{food, wood, gold, stone}` aldeanos por recurso (solo los leídos).
  - `conf`: confianza 0 a 1 de cada campo leído por plantillas (`GlyphReading.Confidence`), por clave: `clock`, `food`, `food_vil`, ..., `vil`, `pop`, `idle`.
  - `clipped`: regiones con el número cortado.
  - `log`: los renglones que salieron del OCR, de arriba hacia abajo. Solo cuando cambian respecto de la captura anterior (incluida la primera; `[]` = sin texto). Ausente = iguales a los de la captura anterior.
  - `age`: el texto crudo del OCR de la edad, solo en las capturas en que se leyó (cada 2 s).
  - `queue`: lista de `[clave, espera]`, o null si no se lee.
  - `scores`: lista de `[número, puntaje, edad_oscura]`, solo en las capturas en que se leyó.
- `done`: cada completación de la creencia (`StateBelief.TakeNew`): `id` (evento), `src` (fuente), `str` (fuerza). Las del log y las sintéticas.
- `belief`: cuando cambia algo de la creencia que no es el HUD: `buildings`, `research`, `units` (diccionarios), `age`, `built_cap`.
- `show`: cada vez que cambia lo que muestra el overlay (`CoachText.Lines` sobre `CoachView`): `critical` y `critical_kind`, `lanes` (lista de `[línea, etiqueta, texto, tono]`), `running`, `suggestion`, `alert`, `idle`, `deviation`, `card` (con la BO terminada), `gd` y `actual` (reparto, `[F, W, S, G]`), `house` (pide casa), `margin`, `tc_idle` (segundos de centro urbano parado, o null), `abandoned`. La línea anterior vale hasta la siguiente `show`.
- `forecast`: cada 10 s de reloj, con aldeanos por recurso y stock leídos: `at` (reloj + 60), `stock`, `on`, `rates` (lo que junta un aldeano por segundo según el modelo, `EconomyReport.NominalRates`), `pred` (stock + 60 × aldeanos × tasa, sin gastos). `outcome`: en la primera captura con reloj ≥ `at`: `forecast` (el reloj del pronóstico), `pred`, `actual` (el stock leído). Con las `cap` entre medio se reconstruye lo gastado.
- `end` (última): `reason` (`fin`: 30 s sin reloj; `nueva`: empezó otra partida; `detenido`: se detuvo el coach o se cerró la app), `length` (segundos de partida del informe).

### 2.3 Código

- `Core/Record/`: `RawCapture` (el DTO de una `cap`, sin tipos de Vision), `GameRecordWriter` (abre el `.jsonl.gz`, escribe líneas, `Flush`, `Dispose` escribe `end`), `GameRecordReader` (devuelve las líneas como `JsonObject`, tolera el archivo cortado; `Meta` y `Captures`), `GameRecorder` (arma las líneas a partir de lo que el coach ya calcula: `Capture(raw, ms)`, `Update(snapshot, coach, builder, ms)`; detecta cambios de creencia y de overlay; lleva los pronósticos pendientes). `Core` no referencia Vision: `Vision` convierte `HudReading` + renglones + edad + cola + puntajes en `RawCapture`.
- `Vision/Hud/HudReading.Confidence`: diccionario campo → confianza, que `HudReader` llena en cada `Parse`.
- `Vision/Hud/ScreenReader`: después de `ReadAsync`, `LastRaw` es la `RawCapture` de esa captura. `Replay(RawCapture raw, double realTime)` da el mismo `HudState` que `ReadAsync` sobre las imágenes: el mismo `HudTracker`, los renglones por `EventLogReader.Process` (matcher + tracker, sin OCR), la edad por `AgeText.Parse`. Para eso `EventLogReader` acepta OCR nulo (solo procesa renglones).
- `SnapshotBuilder.LastCompletions`: las completaciones del último `Build` con fuente y fuerza (hoy se pierden al pasar a `GameEvent`).
- `App/Coaching/LiveCoach`: con `RecordsFolder` puesto, abre el registro en la primera lectura con reloj de cada partida (meta con el contexto de la pantalla de carga si la hubo) y lo cierra en `EndGame`, `StartGame` y `Stop`/`Dispose`; `RecordFinished(GameRecordInfo)` avisa con la ruta, el `game_id` y la duración. Cualquier error al escribir se muestra como los demás errores de captura y no corta el loop.
- `ReplaySession coach <grabación | lecturas.csv | registro.jsonl.gz> ...`: con un registro, cada `cap` pasa por `ScreenReader.Replay` y el resto del comando es el de siempre; la civ y el slot salen de `meta` si no se pasan. `--record <salida.jsonl.gz>` escribe un registro mientras corre sobre una grabación (así se prueba grabación → registro → coach). `--record` imprime cuánto tardó el registro por captura.

### 2.4 Criterio de hecho

Sobre la grabación `20261102-212823` (aztecas, BO `%APPDATA%\BoCoach\cache\bo\5gaddd.json`, campo `build_order`): `coach <grabación> --record r.jsonl.gz` y después `coach r.jsonl.gz` dan las mismas líneas del overlay y los mismos minutos de cierre de cada paso. La app lo escribe en vivo sin subir la CPU de forma notable (el `--replay` de la app con `--stats`, o la medición de `--record`).

## 3. Parte 2: el envío

### 3.1 Del lado de la app

- `Core/Record/InstallIdentity`: `%APPDATA%\BoCoach\instalacion.json` con `id` (GUID aleatorio) y `secret` (32 bytes aleatorios en hex). Se crea la primera vez que hace falta. No está atado a la cuenta de la web.
- `Core/Record/RecordQueue`: `registros/cola.json` con los registros por enviar: `file`, `game_id`, `length`, `added`, `attempts`, `next_try`. Tope de espacio 50 MB y de antigüedad 30 días (se descartan los más viejos). Espera creciente: `next_try = ahora + 15 min × 2^intentos`, con tope de 3 días.
- `Core/Record/RecordUploader.SendPendingAsync(client, identity, appVersion, now)`: solo se llama al terminar una partida (nunca jugando) y al abrir la app. Envía lo vencido; `201`/`200` (duplicado) = enviado; `400`, `413`, `422` = se descarta (no va a entrar nunca); lo demás (sin internet, `429`, `5xx`) = reintento con espera. Se encolan solo las partidas de 60 s o más (`LiveCoach.DefaultMinReportSeconds`), y solo si el interruptor está prendido al terminar la partida.
- `WebLibraryClient.UploadRecordAsync(bytes, installId, secret, gameId, appVersion)`: `POST /api/app/registros`, cuerpo gzip (`Content-Type: application/gzip`), cabeceras `X-BoCoach-Install`, `X-BoCoach-Secret`, `X-BoCoach-Game`, `X-BoCoach-Version`. `ForgetRecordsAsync(installId, secret)`: `DELETE /api/app/registros`. Las dos solo por https o localhost, como el token.
- Interruptor "Compartir mis partidas para mejorar el coach" (`settings.json` `share_games`, prendido por defecto) en la ventana principal y en "Primeros pasos" (sección 6), con un texto fijo: "Al terminar cada partida se sube un registro con números y eventos: lo que leyó del HUD, lo que creyó y lo que te mostró. No viaja tu nombre, ni los de los rivales, ni capturas de pantalla." Botón "Borrar lo compartido…": pide confirmación y manda el pedido de borrado; la barra de estado dice cuántos se borraron.

### 3.2 Del lado de la web y de Supabase

- Migración `web/supabase/migrations/20261102000000_registros.sql`: tablas `installs` (`id`, `secret_hash`, `created_at`, `last_seen_at`, `records_forgotten_at`) y `game_records` (`install_id`, `game_id`, `path`, `size`, `app_version`, `reserved_at`, `uploaded_at`, `forgotten_at`; clave `(install_id, game_id)`). Funciones de solo inserción con el secreto de la instalación (`security definer`, como `app_publish`): `app_record_reserve(p_install, p_secret, p_game, p_size, p_version)` registra la instalación la primera vez (hash del secreto) y después exige el mismo secreto; valida el tamaño (≤ 1 MB), la versión (≤ 40 caracteres), el límite por instalación (30 por día) y el global (1000 por día); devuelve `{path, status}` con `status` `new` o `exists` (idempotente por `game_id`). `app_record_done` marca subido. `app_record_forget` marca `forgotten_at` en todos los registros de la instalación y devuelve sus rutas. Nada de esto se puede leer con la clave pública (sin políticas de `select`).
- Storage, no tabla: bucket privado `registros` con `file_size_limit` 1 MB y `allowed_mime_types` `application/gzip`. Políticas sobre `storage.objects`: `anon` inserta solo en una ruta reservada y no subida (`game_records.path = name and uploaded_at is null`), y borra solo las marcadas `forgotten_at`. El bucket y las políticas van en la migración dentro de un bloque que solo corre si existe el esquema `storage` (PGlite no lo tiene); los pasos a mano quedan en `docs/pendientes-usuario.md` por si el bloque falla.
- Rutas `web/src/app/api/app/registros/route.ts`: `POST` valida cabeceras y tamaño, reserva, sube al bucket con el cliente público y marca subido; `DELETE` marca y borra los objetos. Tests de la migración en `web/supabase/tests/registros.test.ts` (reserva, secreto equivocado, duplicado, tope de tamaño, límite diario, borrado).
- Capacidad: Supabase gratis da 1 GB de storage y 500 MB de base; a unos cientos de KB por registro entran varios miles de partidas (el número exacto, con el tamaño medido, va a `CLAUDE.md`).

## 4. Pruebas

- Core: ida y vuelta del escritor y el lector (archivo cortado incluido), `GameRecorder` sobre `lecturas.csv` de la partida de scouts (`show` solo cuando cambia, pronóstico y resultado, `done` con fuente), cola (sin internet queda y reintenta cuando vence, el duplicado cuenta como enviado, `413` descarta, topes de espacio y edad), identidad (se crea una vez).
- Vision: `ScreenReader.Replay` sobre las `RawCapture` de la grabación `rec-20261015-214512` da los mismos `HudState` que `ReadAsync` sobre las imágenes (con y sin OCR).
- App: `LiveCoach` escribe el registro de una partida grabada y avisa al terminar; `share_games` va y vuelve de `settings.json`; "Primeros pasos" tiene el interruptor.
- Supabase: los tests de la migración sobre PGlite.

## 5. Decisiones chicas

- El registro local se escribe aunque el interruptor esté apagado: es del jugador y sirve para el modo desarrollador. El interruptor decide solo el envío.
- Los registros locales no se borran solos (a 200 KB por partida, 500 partidas son 100 MB); la cola sí tiene tope.
- El pedido de borrado deja las filas con `forgotten_at` (sin archivo): la instalación puede volver a compartir después.
