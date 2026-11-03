# Registro de partida: plan de implementación

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** cada partida deja un `.jsonl.gz` con lecturas crudas, creencia, pronósticos y lo que mostró el overlay; `ReplaySession coach` lo reproduce sin PNG; la app lo sube con consentimiento a Supabase Storage.

**Architecture:** `Core/Record` arma y lee el registro con DTOs sin Vision; `Vision/Hud/ScreenReader` produce la `RawCapture` de cada captura y la vuelve a leer (`Replay`) con el mismo suavizado; `LiveCoach` escribe en vivo y avisa al terminar; la cola y el envío viven en `Core/Record` sobre `WebLibraryClient`; la web reserva, sube al bucket y marca con funciones de la base validadas por el secreto de la instalación.

**Tech Stack:** C# .NET 10 (System.Text.Json.Nodes, GZipStream), xUnit; Next.js 16 route handlers; Supabase (Postgres, Storage), vitest sobre PGlite.

**Spec:** `docs/superpowers/specs/2026-11-02-registro-de-partida-design.md`

## Global Constraints

- `Core` no referencia Vision ni la app (`ArquitecturaTests`).
- Texto de la app en castellano rioplatense, sin guiones largos.
- Un commit por cambio, con `Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>`. No subir `<Version>`.
- Tests: `dotnet test BoCoach.slnx -c Release` (no correr dos a la vez). Web: `cd web && npm test`.
- Sin nombres de jugadores en el registro (ni el propio, ni los del panel de puntajes, ni la pantalla de carga).

## Review Focus

1. Un registro cortado por un cierre brusco de la app: el lector devuelve lo que hay y `ReplaySession coach` corre hasta ahí (Tarea 1 lo prueba).
2. Una `cap` sin reloj (pantalla tapada) se registra igual y el replay la pasa al `HudTracker` como `ReadAsync` (Tarea 3).
3. Sin internet dos partidas seguidas: la cola conserva las dos y la segunda no se reintenta antes de `next_try` (Tarea 7).
4. Un registro de más de 1 MB o una versión larga: la web responde 413/400 y la cola lo descarta en vez de reintentar para siempre (Tarea 7 y 8).
5. El mismo `game_id` reenviado (la app se cerró antes de marcar enviado): la base devuelve `exists` y la app lo da por enviado (Tarea 8).

---

### Tarea 1: formato del registro (escritor y lector)

**Files:**
- Create: `src/BoCoach.Core/Record/RawCapture.cs`, `src/BoCoach.Core/Record/GameRecordWriter.cs`, `src/BoCoach.Core/Record/GameRecordReader.cs`
- Test: `tests/BoCoach.Core.Tests/Record/GameRecordFileTests.cs`

**Interfaces:**
- `public sealed record RawCapture(double? Clock, int? Food, int? Wood, int? Gold, int? Stone, int? Villagers, int? Idle, int? Population, int? PopulationCap, IReadOnlyDictionary<Resource, int>? VillagersOn, IReadOnlyDictionary<string, float> Confidence, IReadOnlyList<string> Clipped, IReadOnlyList<string>? LogLines, string? AgeText, IReadOnlyList<QueueItem>? Queue, IReadOnlyList<PlayerScore>? Scores)` con `JsonObject ToJson(bool logChanged)` y `static RawCapture FromJson(JsonObject, IReadOnlyList<string> previousLog)` (si falta `log`, toma `previousLog`).
- `public sealed class GameRecordWriter : IDisposable` con `static GameRecordWriter Create(string path, JsonObject meta)`, `void Write(JsonObject line)`, `void Flush()`, `void End(string reason, double length)`; `Dispose` escribe `end` con `detenido` si no se escribió.
- `public sealed class GameRecordReader` con `static GameRecordReader Load(string path)`, `JsonObject? Meta`, `IReadOnlyList<JsonObject> Lines`, `bool Truncated`.
- `public static class GameRecordKeys` con las constantes de `t` (`meta`, `cap`, `done`, `belief`, `show`, `forecast`, `outcome`, `end`).

- [ ] Test: `Ida_y_vuelta_con_meta_capturas_y_fin` (escribe meta + 2 `cap` + `end`, lee, compara `Meta["game_id"]`, `Lines.Count == 3`, la segunda `cap` sin `log` toma el de la primera); `Un_archivo_cortado_se_lee_hasta_donde_llega` (escribe 50 líneas, `Flush`, copia el archivo truncado a la mitad en bytes, `Load` da `Truncated == true` y al menos 1 línea); `Dispose_sin_End_escribe_detenido`.
- [ ] Correr: `dotnet test tests/BoCoach.Core.Tests -c Release --filter GameRecordFileTests` → falla por tipos inexistentes.
- [ ] Implementar. `ToJson` omite las claves nulas; `on`, `conf`, `clipped` solo si tienen algo; `queue` como `JsonArray` de `[clave, espera]`; `scores` de `[slot, score, dark]`.
- [ ] Correr el filtro → pasa. Commit: "Registro de partida: formato jsonl.gz (escritor y lector)".

### Tarea 2: `GameRecorder` (creencia, overlay, pronósticos)

**Files:**
- Modify: `src/BoCoach.Core/Engine/SnapshotBuilder.cs` (propiedad `IReadOnlyList<Completion> LastCompletions`)
- Create: `src/BoCoach.Core/Record/GameRecorder.cs`
- Test: `tests/BoCoach.Core.Tests/Record/GameRecorderTests.cs`

**Interfaces:**
- `public sealed class GameRecorder(GameRecordWriter writer, GameData data)` con `void Capture(RawCapture raw, double ms)`, `void Update(GameSnapshot snapshot, Coach coach, SnapshotBuilder builder, double ms)`, `double ForecastEvery = 10`, `double ForecastHorizon = 60`, `int Lines` (cuántas escribió), `void Flush()` (cada 10 s reales lo llama quien escribe).
- `public static JsonObject GameRecorder.Meta(...)` con los campos de la sección 2.2 del spec (parámetros con nombre: `gameId`, `started`, `app`, `gameData`, `hud`, `hudScale`, `civ`, `bo`, `boName`, `map`, `rivalCiv`, `slot`, `language`, `intervalMs`).

- [ ] Test sobre `tests/fixtures/rec-20261015-214512/lecturas.csv` (ya lo copia Core.Tests) con la BO de scouts: `Show_se_escribe_solo_cuando_cambia_lo_mostrado` (dos `show` seguidas nunca iguales en `critical`+`lanes`+`suggestion`+`alert`+`card`); `Cada_pronostico_tiene_su_resultado_60_s_despues` (para cada `forecast` hay un `outcome` con `forecast == clock` y `clock >= at`); `Las_completaciones_llevan_fuente_y_fuerza` (hay `done` con `src == "Log"` y `id == "house_built"`); `La_creencia_se_escribe_cuando_cambia` (la primera `belief` trae `buildings.house >= 1`).
- [ ] Correr el filtro → falla.
- [ ] Implementar: `show` compara una clave de texto armada con los campos listados; `belief` compara `Buildings`, `Research`, `Units`, `Age`, `BuiltCap`; pronóstico con `State.VillagersOn`, `State.Stock` y `coach.Economy.NominalRates`.
- [ ] Correr el filtro → pasa. Commit: "GameRecorder: creencia, overlay y pronósticos en el registro".

### Tarea 3: `RawCapture` desde Vision y `ScreenReader.Replay`

**Files:**
- Modify: `src/BoCoach.Vision/Hud/HudReader.cs` (`HudReading.Confidence`), `src/BoCoach.Vision/Events/EventLogReader.cs` (OCR nulo), `src/BoCoach.Vision/Hud/ScreenReader.cs` (`LastRaw`, `Replay`)
- Test: `tests/BoCoach.Vision.Tests/ScreenReaderReplayTests.cs`

**Interfaces:**
- `HudReading.Confidence: IReadOnlyDictionary<string, float>` (claves `clock`, `food`, `food_vil`, `wood`, `wood_vil`, `gold`, `gold_vil`, `stone`, `stone_vil`, `vil`, `pop`, `idle`).
- `EventLogReader(OcrReader? ocr, EventCatalog catalog)`: `ReadAsync` tira `InvalidOperationException` sin OCR; `LastLines: IReadOnlyList<string>` (lo que pasó por `Process` en la última captura).
- `ScreenReader.LastRaw: RawCapture?` (después de cada `ReadAsync`; `LogLines` null cuando no cambiaron); `HudState Replay(RawCapture raw, double realTime)`.

- [ ] Test: sobre `rec-20261015-214512` (fixture de Vision), 200 capturas: `Replay_da_los_mismos_estados_que_leer_las_imagenes` sin OCR (compara `ToReading()` captura por captura) y, si `OcrDisponibleTests` dice que hay OCR, con OCR (eventos iguales).
- [ ] Correr → falla. Implementar. Correr → pasa. Commit: "ScreenReader: la captura cruda y su reproducción sin imágenes".

### Tarea 4: `ReplaySession coach` sobre un registro y `--record`

**Files:**
- Modify: `tools/ReplaySession/Program.cs` (`Readings`, `CoachReplay`, ayuda)

- [ ] `Readings(source)`: si `source` termina en `.jsonl.gz`, `GameRecordReader.Load`, un `ScreenReader` sin OCR con el catálogo y por cada `cap` `reader.Replay(raw, ms / 1000).ToReading()`; devuelve también `ms`. `CoachReplay` toma `civ` y `slot` de `meta` si no se pasaron, y con `--record <ruta>` crea el `GameRecorder` (meta con `app = "ReplaySession"`) y llama `Capture`/`Update`; al final imprime "registro: N líneas, X KB, Y ms por captura".
- [ ] Verificación manual (criterio de hecho): `coach <20261102-212823> <5gaddd.json> --civ aztecs --guide --record r.jsonl.gz > a.txt` y `coach r.jsonl.gz <5gaddd.json> --guide > b.txt`; `diff` de las líneas y de los cierres sin diferencias. Anotar el tamaño del registro.
- [ ] Commit: "ReplaySession coach acepta un registro y lo escribe con --record".

### Tarea 5: `LiveCoach` escribe el registro en vivo

**Files:**
- Modify: `src/BoCoach.App/Coaching/LiveCoach.cs`, `src/BoCoach.App/Coaching/CoachSessionViewModel.cs`
- Test: `tests/BoCoach.App.Tests/LiveCoachTests.cs`

**Interfaces:**
- `LiveCoach.RecordsFolder: string?` (init; null = no registra), `LiveCoach.AppVersion: string` (init), `event Action<GameRecordInfo>? RecordFinished`; `public sealed record GameRecordInfo(string Path, string GameId, double Length, string Reason)`.
- `CoachSessionViewModel.DefaultRecordsFolder` (como `DefaultReportsFolder`; `TestFolders` lo cambia).

- [ ] Test: `Una_partida_grabada_deja_su_registro_y_avisa_al_terminar` (RecordedFrames sobre el fixture con `GapFrames` de 70 capturas sin reloj; al terminar hay un `.jsonl.gz` en la carpeta temporal con `meta.civ == "any"`, más de 10 `cap` y `end.reason == "fin"`; `RecordFinished` una vez). `Una_partida_nueva_cierra_el_registro_anterior_con_nueva`.
- [ ] Correr → falla. Implementar: abrir en la primera captura con `ClockRead` de cada partida; `Flush` cada 10 s reales; cerrar en `EndGame` (`fin`), `StartGame` (`nueva`), `Stop`/`Dispose` (`detenido`). Errores de escritura → `_error`.
- [ ] Correr → pasa. Commit: "LiveCoach escribe el registro de cada partida".

### Tarea 6: medir la CPU

- [ ] `BoCoach.exe --replay <20261102-212823> --bo <5gaddd.json> --speed 8 --stats antes.csv` (compilado antes de la Tarea 5, en la rama base) contra `despues.csv` con el registro; comparar promedio de CPU. Alternativa: el "ms por captura" de `--record`. Anotar el resultado en `CLAUDE.md` (Tarea 10).

### Tarea 7: identidad, cola y envío (Core)

**Files:**
- Create: `src/BoCoach.Core/Record/InstallIdentity.cs`, `src/BoCoach.Core/Record/RecordQueue.cs`, `src/BoCoach.Core/Record/RecordUploader.cs`
- Modify: `src/BoCoach.Core/Web/WebLibraryClient.cs`
- Test: `tests/BoCoach.Core.Tests/Record/RecordUploadTests.cs`

**Interfaces:**
- `InstallIdentity(string Id, string Secret)` con `static InstallIdentity LoadOrCreate(string path)`.
- `RecordQueue(string folder)`: `Enqueue(string file, string gameId, double length, DateTimeOffset now)`, `IReadOnlyList<QueuedRecord> Due(DateTimeOffset now)`, `Sent(QueuedRecord)`, `Failed(QueuedRecord, DateTimeOffset now)`, `Dropped(QueuedRecord)`, `IReadOnlyList<QueuedRecord> Pending`; constantes `MaxBytes = 50 MB`, `MaxAge = 30 días`, `BaseDelay = 15 min`, `MaxDelay = 3 días`.
- `WebLibraryClient.UploadRecordAsync(byte[] gzip, string installId, string secret, string gameId, string appVersion, CancellationToken) → RecordUpload(bool Duplicate)`; `ForgetRecordsAsync(installId, secret, cancel) → int`.
- `RecordUploader.SendPendingAsync(RecordQueue queue, WebLibraryClient client, InstallIdentity identity, string appVersion, DateTimeOffset now, CancellationToken) → RecordSendResult(int Sent, int Failed, int Dropped)`.

- [ ] Tests con `FakeWeb`: `Se_sube_y_sale_de_la_cola`, `Sin_internet_queda_y_espera_15_minutos`, `Vencida_se_reintenta_y_la_espera_crece`, `Un_duplicado_cuenta_como_enviado` (200 con `{"duplicate":true}`), `Un_413_se_descarta`, `La_cola_descarta_lo_mas_viejo_al_pasar_el_tope` (archivos de 20 MB falsos: `MaxBytes` configurable en el test), `La_cola_descarta_lo_de_mas_de_30_dias`, `La_identidad_se_crea_una_vez`.
- [ ] Correr → falla. Implementar. Correr → pasa. Commit: "Cola local y envío de registros con identidad por instalación".

### Tarea 8: migración de Supabase y rutas de la web

**Files:**
- Create: `web/supabase/migrations/20261102000000_registros.sql`, `web/supabase/tests/registros.test.ts`, `web/src/app/api/app/registros/route.ts`
- Modify: `docs/pendientes-usuario.md`

- [ ] Tests PGlite: reserva nueva devuelve `new` y crea la instalación; el mismo `game_id` devuelve `exists`; otro secreto da error `28000`; tamaño > 1 MB da error; el registro 31 del día da error; `app_record_forget` con el secreto correcto marca y devuelve las rutas; `anon` no puede leer `installs` ni `game_records`.
- [ ] Correr `cd web && npm test -- registros` → falla. Escribir la migración (bloque `do $$ ... if exists (select 1 from pg_namespace where nspname = 'storage') then execute ... $$` para el bucket y las políticas). Correr → pasa.
- [ ] Ruta `POST`/`DELETE` como en el spec 3.2; `npx next typegen && npx tsc --noEmit` en verde; `npm run lint`.
- [ ] `docs/pendientes-usuario.md`: ítem con los pasos (SQL Editor con la migración; verificar en Storage que exista el bucket `registros` privado con el tope de 1 MB, y si no, crearlo a mano con esos valores y correr solo el bloque de las políticas).
- [ ] Commit: "Supabase: registros de partida en storage con funciones por instalación" y otro "Web: /api/app/registros".

### Tarea 9: interruptor, Primeros pasos y pedido de borrado

**Files:**
- Modify: `src/BoCoach.App/Coaching/CoachSettings.cs`, `CoachSessionViewModel.cs`, `src/BoCoach.App/MainWindow.xaml`, `src/BoCoach.App/FirstRun/FirstRunViewModel.cs`, `FirstRunWindow.xaml`, `src/BoCoach.App/MainWindow.xaml.cs`
- Test: `tests/BoCoach.App.Tests/CoachSessionTests.cs`, `FirstRunTests.cs`

- [ ] Tests: `share_games` falta → true; `false` va y vuelve; `FirstRunViewModel.ShareGames` escribe en los ajustes al cambiar; con el interruptor apagado `RecordFinished` no encola.
- [ ] Implementar: `ShareGames` observable, `RecordFinished` → si `ShareGames` y `Length >= DefaultMinReportSeconds`, `Enqueue` + `SendPendingAsync` en segundo plano con el `WebLibraryClient` de la cuenta (`web.Client`), estado en la barra ("Registro de la partida enviado" / "guardado para enviar después"); al abrir la app, `SendPendingAsync` una vez. Botón "Borrar lo compartido…" con `ICoachHost.Confirm(string)` (nuevo en la interfaz; `FakeHost` devuelve true).
- [ ] Commit: "Compartir mis partidas: interruptor, Primeros pasos y pedido de borrado".

### Tarea 10: documentación y suite completa

- [ ] `CLAUDE.md`: bullet "Registro de partida" con el formato, dónde se guarda, cómo reproducir una partida ajena (`ReplaySession coach <registro> <bo.json>`), el tamaño medido, la CPU, cuántas partidas entran en el plan gratis, y lo que solo el usuario puede hacer. `PLAN.md` ítem 15 (2): hecho, con lo que queda.
- [ ] `dotnet test BoCoach.slnx -c Release` → todo en verde; `cd web && npm test` → verde.
- [ ] Commit: "Notas: registro de partida".
