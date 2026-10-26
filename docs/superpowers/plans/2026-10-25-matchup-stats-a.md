# Plan de implementación: estadísticas reales de cruces, sub-paso (a) (`tools/stats` y el primer JSON)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Una herramienta Python offline (`tools/stats`) que baja las partidas clasificatorias de aoe2-guide, las acumula en una caché, ajusta el modelo de cruces 1v1 y de civs por equipos, lo valida y, si pasa, escribe el primer `data/matchup_stats.json`.

**Architecture:** Cada fuente traduce sus archivos a una tabla de partidas propia (dos `.parquet`: partidas y jugadores) que se suma a la caché sin repetir. La lectura arma arreglos de numpy con las franjas (mapa, Elo, duración) y el parche elegido. El modelo es una regresión logística con penalización por grupo sobre una matriz rala; los frenos se eligen prediciendo los últimos 14 días; el rango sale de 20 remuestreos. La validación decide si se escribe el JSON. Nada de esto toca la app ni el Core (son los sub-pasos b a d).

**Tech Stack:** Python 3.12, DuckDB (leer y escribir `.parquet`, cruzar tablas), numpy, scipy (matriz rala y L-BFGS-B), pytest.

**Spec:** `docs/superpowers/specs/2026-10-25-matchup-stats-design.md` (secciones 2, 3.1, 3.2, 4, 6, 7 y 8; leerla antes de cada tarea).

## Global Constraints

- Todo vive en `tools/stats/`. Venv en `tools/stats/.venv` y caché en `tools/stats/.cache`, las dos en `.gitignore`. `requirements.txt`: `duckdb==1.5.6`, `numpy==2.5.4`, `scipy==1.18.1`, `pytest==9.1.1`.
- Los tests se corren desde `tools/stats` con `.venv/Scripts/python -m pytest -q` (en el CI, `python -m pytest -q`): así los módulos de `tools/stats` se importan sin instalar nada.
- Valores de la spec, tal cual: duración de juego = segundos reales / 60 × **1,7**; se descartan las partidas de **menos de 5 minutos de juego**; franjas de duración en minutos de juego **20, 30, 40, 55**; franjas de Elo **850, 1050, 1300, 1600**; mapas **arabia, arena, black_forest, nomad** (en el modelo, un quinto "otros"; en la salida, "todos" primero); rango del **90 %** (percentiles 5 y 95) con **20** remuestreos; se apartan los últimos **14 días**; si el parche actual abarca **menos de 28 días**, se suma el anterior.
- Civs: las claves de game_data (el `.json` más nuevo de `data/game_data`); de aoe2-guide solo se traduce `mayans` → `mayan`. Una civ que no se puede traducir frena todo con su nombre.
- Salida: `data/matchup_stats.json` como en la sección 3.2 de la spec: `schema_version` 1; por entrada, 180 casilleros `[p, desde, hasta, n]` en milésimas (n en partidas), en el orden mapa (todos, arabia, arena, black_forest, nomad) × Elo (todas y las 5) × duración (todas y las 5); claves de pareja en orden alfabético, `"a|b"`, con `p` para la primera civ a Elo parejo. Una entrada por renglón, para que los cambios de cada parche se lean en el diff. Se agrega un bloque `validation` con las pérdidas y la calibración (los lectores ignoran lo que no conocen).
- Validación (spec 4.6): pérdida del modelo menor que la de "solo el Elo" y que la del % crudo del casillero; calibración por deciles a **0,02** o menos. Decisión de este plan: la comparación con el % crudo es solo para 1v1, porque el casillero de la spec es de pareja; equipos tiene que ganarle a "solo el Elo" y estar calibrado. Si algo no pasa, el JSON no se toca y el comando termina con código 1.
- Equipos (decisión de este plan sobre la spec 4.3): cada jugador suma su civ (+1 en su equipo, -1 en el otro), así la `p` de una civ es la de un equipo que tiene esa civ en lugar de una civ promedio. Con el promedio de cada equipo (lo que dice la spec entre paréntesis), la pieza valdría por el equipo entero y exageraría la chance.
- Descargas con el User-Agent `aoe2-bo-coach-stats (+https://github.com/Kyriokes/aoe2-bo-coach)`.
- Las fechas se manejan como segundos unix (enteros): las funciones de DuckDB que devuelven fechas a Python piden `pytz`, que no está.
- La salida por consola va en UTF-8 (`sys.stdout.reconfigure(encoding="utf-8")` al empezar `stats.py`), como `--publish` de la app.
- Estilo del extractor: nombres, docstrings, comentarios y mensajes en castellano rioplatense, sin guiones largos.
- Commits chicos, uno por cambio, en `fase-9-refinamiento`, terminados en `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

## Review Focus

1. **Nómada no tiene partidas 1v1** (en la foto de hoy no aparece): sus casilleros tienen que salir con `n` 0 y una `p` finita, sin división por cero. Test en la Tarea 5 (`test_casilleros_sin_partidas_en_un_mapa_usa_pesos_parejos`) y en la Tarea 8 (`test_ajustar_sin_un_mapa_no_deja_nan`).
2. **Una pareja que nunca se enfrentó** (civs poco jugadas): sale igual, con `n` 0 y la `p` de las piezas de cada civ. Test en la Tarea 8 (`test_pareja_sin_partidas_sale_con_n_cero`).
3. **Bajar dos veces la misma foto** (aoe2-guide publica fotos que se pisan): no se cuenta doble. Test en la Tarea 2 (`test_sumar_dos_veces_no_duplica`).
4. **La fuente caída, o una tabla nueva rota:** la caché queda como estaba y el mensaje dice qué pasó. Tests en la Tarea 2 (`test_sumar_tabla_rota_no_toca_la_cache`) y la Tarea 3 (`test_bajar_con_la_fuente_caida`).
5. **Una civ nueva de una DLC antes de correr el extractor:** se frena nombrándola, sin escribir nada. Test en la Tarea 3 (`test_civ_desconocida_frena_con_su_nombre`).

---

### Task 1: La herramienta y las franjas

**Files:**
- Create: `tools/stats/requirements.txt`, `tools/stats/franjas.py`
- Create: `tools/stats/tests/test_franjas.py`
- Modify: `.gitignore` (dos renglones al final: `tools/stats/.venv/` y `tools/stats/.cache/`)

**Interfaces:**
- Produces (`franjas.py`): `FACTOR_JUEGO = 1.7`, `MINIMO_JUEGO_MIN = 5`, `MAPAS = ("arabia", "arena", "black_forest", "nomad")`, `OTROS = 4`, `LIMITES_ELO = (850, 1050, 1300, 1600)`, `LIMITES_JUEGO_MIN = (20, 30, 40, 55)`; `minutos_de_juego(duracion_real_s: np.ndarray) -> np.ndarray`, `franja_duracion(minutos_juego: np.ndarray) -> np.ndarray` (enteros 0 a 4), `franja_elo(elo: np.ndarray) -> np.ndarray` (0 a 4), `indice_mapa(mapas: Sequence[str]) -> np.ndarray` (0 a 3, y `OTROS` para el resto).

- [ ] **Step 1: Crear el venv e instalar** `python -m venv tools/stats/.venv` y `tools/stats/.venv/Scripts/python -m pip install -r tools/stats/requirements.txt`.

- [ ] **Step 2: Escribir los tests**

```python
def test_minutos_de_juego():
    assert minutos_de_juego(np.array([600.0])).tolist() == [pytest.approx(17.0)]

def test_franja_duracion_con_los_bordes():
    assert franja_duracion(np.array([19.99, 20, 29.99, 30, 54.99, 55, 90])).tolist() == [0, 1, 1, 2, 3, 4, 4]

def test_franja_elo_con_los_bordes():
    assert franja_elo(np.array([500, 849, 850, 1299, 1300, 1599, 1600, 2600])).tolist() == [0, 0, 1, 2, 3, 3, 4, 4]

def test_indice_mapa():
    assert indice_mapa(["arabia", "arena", "black_forest", "nomad", "megarandom"]).tolist() == [0, 1, 2, 3, 4]
```

- [ ] **Step 3: Correrlos y ver que fallan** (`ModuleNotFoundError: franjas`).

- [ ] **Step 4: Implementar `franjas.py`** con `np.searchsorted(..., side="right")` para las franjas (un valor en el borde va a la franja de arriba). Docstring del módulo con el porqué del 1,7 (velocidad normal de DE; a verificar con una partida del usuario, spec 4.2).

- [ ] **Step 5: Correr los tests y ver que pasan.**

- [ ] **Step 6: Commit** `stats: herramienta tools/stats y franjas de mapa, Elo y duración`.

### Task 2: La caché de partidas

**Files:**
- Create: `tools/stats/cache.py`
- Create: `tools/stats/tests/sinteticos.py` (ayudas para armar tablas en los tests)
- Test: `tools/stats/tests/test_cache.py`

**Interfaces:**
- Produces (`cache.py`): `COLUMNAS_PARTIDAS = {"id": "BIGINT", "ladder": "VARCHAR", "mapa": "VARCHAR", "inicio": "BIGINT", "duracion_real_s": "DOUBLE", "parche": "BIGINT", "elo_medio": "DOUBLE", "fuente": "VARCHAR"}`, `COLUMNAS_JUGADORES = {"id_partida": "BIGINT", "civ": "VARCHAR", "equipo": "BIGINT", "elo_previo": "BIGINT", "gano": "BOOLEAN"}`; `sumar(cache: Path, nuevas: Path) -> int` (suma `nuevas/partidas.parquet` y `nuevas/jugadores.parquet` a `cache/partidas.parquet` y `cache/jugadores.parquet` y devuelve cuántas partidas nuevas entraron); `class TablaInvalida(Exception)`.
- Produces (`tests/sinteticos.py`): `escribir_tablas(carpeta: Path, partidas: list[dict], jugadores: list[dict]) -> None` (escribe los dos `.parquet` con las columnas de arriba).

- [ ] **Step 1: Escribir los tests**

```python
def test_sumar_a_una_cache_vacia(tmp_path): ...       # 3 partidas, 6 jugadores → devuelve 3; la caché tiene 3 y 6
def test_sumar_dos_veces_no_duplica(tmp_path): ...    # la misma tabla dos veces → la segunda devuelve 0; siguen 3 y 6
def test_sumar_se_queda_con_lo_que_ya_estaba(tmp_path): ...  # una partida repetida con otro mapa → queda el mapa viejo y sus jugadores viejos
def test_sumar_tabla_rota_no_toca_la_cache(tmp_path): ...   # nuevas sin la columna "parche" → TablaInvalida que nombra la columna; los .parquet de la caché, byte a byte iguales
```

- [ ] **Step 2: Correrlos y ver que fallan.**

- [ ] **Step 3: Implementar `sumar`** con DuckDB: controlar primero que `nuevas` tenga las columnas de `COLUMNAS_*` (si no, `TablaInvalida`); unir lo de la caché con las partidas de `nuevas` cuyo `id` no está, y los jugadores de esas partidas nuevas; escribir a `partidas.parquet.tmp` y `jugadores.parquet.tmp` y recién al final reemplazar con `os.replace` (si algo falla antes, la caché queda como estaba).

- [ ] **Step 4: Correr los tests y ver que pasan.**

- [ ] **Step 5: Commit** `stats: caché de partidas que suma sin repetir`.

### Task 3: La fuente aoe2-guide y el comando `bajar`

**Files:**
- Create: `tools/stats/fuentes/__init__.py` (vacío), `tools/stats/fuentes/aoe2guide.py`, `tools/stats/stats.py`
- Test: `tools/stats/tests/test_aoe2guide.py`

**Interfaces:**
- Consumes: `cache.COLUMNAS_*`, `cache.sumar`, `sinteticos.escribir_tablas`.
- Produces (`fuentes/aoe2guide.py`): `URL = "https://aoe2-guide.vercel.app/data"`, `NOMBRE = "aoe2-guide"`, `CIVS = {"mayans": "mayan"}`, `IGNORADAS: frozenset[str] = frozenset()`; `class FuenteCaida(Exception)`, `class CivDesconocida(Exception)` (con `.civs: list[str]`); `claves_de_civs(repo: Path) -> set[str]`; `bajar(destino: Path, url: str = URL) -> None` (baja `matches.parquet`, `players.parquet` y `ranked_data.json` a `destino`); `normalizar(origen: Path, destino: Path, civs_validas: set[str]) -> None` (escribe la tabla de partidas en `destino`); `etiqueta_de_parche(origen: Path) -> str | None` (`meta.patchLabel` de `ranked_data.json`).
- Produces (`stats.py`): `main(argv: list[str] | None = None) -> int` con el subcomando `bajar [--fuente aoe2guide] [--cache RUTA]` (por defecto `tools/stats/.cache`). Baja a `<cache>/descargas/aoe2guide`, normaliza a `<cache>/normalizadas/aoe2guide`, suma a `<cache>` y guarda `<cache>/fuente.json` con `{"name", "url", "patch_label"}`. Escribe "N partidas nuevas, M en total".

- [ ] **Step 1: Escribir los tests** (con `.parquet` chicos con las columnas de aoe2-guide de la spec, sección 2, armados en el test con DuckDB):

```python
def test_normalizar_traduce_columnas_y_civs(tmp_path): ...
    # 1 partida 1v1 (mayans contra franks) → partidas: ladder "1v1", mapa igual, inicio = start_time,
    # duracion_real_s = completion_time - start_time, parche, elo_medio = avg_elo, fuente "aoe2-guide";
    # jugadores: civ "mayan" y "franks", equipo, elo_previo = old_rating, gano = won
def test_civ_desconocida_frena_con_su_nombre(tmp_path): ...  # civ "atlanteans" → CivDesconocida con civs == ["atlanteans"]; destino sin archivos
def test_las_civs_de_la_foto_tienen_clave():                 # las 53 civs medidas el 2026-10-25 (lista en el test) traducidas están en claves_de_civs(REPO)
def test_bajar_con_la_fuente_caida(tmp_path): ...             # bajar(tmp_path, url="http://127.0.0.1:9") → FuenteCaida con la URL en el mensaje
def test_comando_bajar_con_la_fuente_caida_no_toca_la_cache(tmp_path, monkeypatch): ...
    # caché con 3 partidas; aoe2guide.URL apuntada a 127.0.0.1:9 → main(["bajar", "--cache", str(tmp_path)]) == 1 y siguen 3
```

- [ ] **Step 2: Correrlos y ver que fallan.**

- [ ] **Step 3: Implementar `aoe2guide.py`** con `urllib.request` (timeout de 120 s, el User-Agent de las restricciones; cualquier error de red o HTTP pasa a `FuenteCaida` con la URL) y DuckDB para `normalizar` (las civs se traducen con `CIVS`; las que no están en `civs_validas` ni en `IGNORADAS` juntan `CivDesconocida` antes de escribir nada; las de `IGNORADAS` se descartan con sus partidas). `claves_de_civs` lee `civs` del `.json` de `data/game_data` con la versión más alta.

- [ ] **Step 4: Implementar `stats.py`** con `argparse` (docstring de uso como el del extractor) y el subcomando `bajar`; `FuenteCaida`, `CivDesconocida` y `TablaInvalida` se muestran como un renglón y devuelven 1.

- [ ] **Step 5: Correr los tests y ver que pasan.**

- [ ] **Step 6: Commit** `stats: fuente aoe2-guide y comando bajar`.

### Task 4: La lectura para el modelo

**Files:**
- Create: `tools/stats/lectura.py`
- Test: `tools/stats/tests/test_lectura.py`

**Interfaces:**
- Consumes: `franjas.*`, la caché de la Tarea 2, `sinteticos.escribir_tablas`.
- Produces (`lectura.py`):
  - `@dataclass(frozen=True) class Datos1v1`: `civs: list[str]` (las que aparecen, en orden alfabético), `a: np.ndarray` y `b: np.ndarray` (índices en `civs`, siempre `a < b`), `gana_a: np.ndarray` (0,0 o 1,0), `dif_elo: np.ndarray` (Elo previo de A menos el de B), `mapa`, `elo`, `dur` (franjas de `franjas`, el Elo por `elo_medio`), `inicio: np.ndarray` (segundos unix), `previo_mezclado: bool`.
  - `@dataclass(frozen=True) class DatosEquipos`: `civs: list[str]`, `civ_equipo: scipy.sparse.csr_matrix` (partidas × civs: +1 por cada jugador del equipo 1 con esa civ, -1 por cada uno del otro), `gana_1`, `dif_elo` (Elo medio del equipo 1 menos el del otro), `mapa`, `elo`, `dur`, `inicio`, `previo_mezclado`.
  - `seleccionar_parche(inicio: np.ndarray, parche: np.ndarray) -> tuple[np.ndarray, bool]` (máscara de partidas y si se mezcló el anterior).
  - `leer_1v1(cache: Path) -> Datos1v1`, `leer_equipos(cache: Path) -> DatosEquipos`.

- [ ] **Step 1: Escribir los tests** (con tablas armadas a mano con `escribir_tablas`)

```python
def test_leer_1v1_orienta_y_descarta(tmp_path): ...
    # entre otras: una franks contra britons (sale a = britons, gana_a invertido, dif_elo invertida),
    # un espejo, una de 4 minutos de juego (141 s reales), una con un solo jugador y una "team": ninguna de esas entra
def test_leer_equipos_suma_las_civs_de_cada_lado(tmp_path): ...
    # 2v2 con franks y britons contra franks y celts → fila: franks 0, britons +1, celts -1; gana_1 del equipo de número menor
def test_leer_equipos_descarta_tres_equipos(tmp_path): ...
def test_parche_nuevo_con_pocas_semanas_suma_el_anterior():
    # parche 2 abarca 20 días, el 1 antes → máscara con los dos y True
def test_parche_con_cuatro_semanas_va_solo():
    # parche 2 abarca 30 días → solo el 2 y False
```

- [ ] **Step 2: Correrlos y ver que fallan.**

- [ ] **Step 3: Implementar `lectura.py`.** El parche actual es el de la partida más nueva; "abarca" = la más nueva menos la más vieja de ese parche; el anterior es el parche de la partida más nueva entre las que no son del actual. Primero se eligen las partidas del parche y después se aplican los filtros (espejos, menos de 5 minutos de juego, cantidad de jugadores y equipos).

- [ ] **Step 4: Correr los tests y ver que pasan.**

- [ ] **Step 5: Commit** `stats: lectura de la caché para el modelo, con el parche y los filtros`.

### Task 5: El modelo: diseño, ajuste y casilleros

**Files:**
- Create: `tools/stats/modelo.py`
- Modify: `tools/stats/tests/sinteticos.py` (generadores de partidas)
- Test: `tools/stats/tests/test_modelo.py`

**Interfaces:**
- Consumes: `lectura.Datos1v1`, `lectura.DatosEquipos`, `lectura.leer_1v1`, `lectura.leer_equipos`, `sinteticos.escribir_tablas`.
- Produces (`tests/sinteticos.py`): `EFECTOS = {"civ": 0.2, "civ_mapa": 0.1, "cruce": 0.15}` (desvíos de los efectos verdaderos; los demás grupos en 0); `generar_1v1(n: int, civs: list[str], semilla: int, sin_jugar: frozenset[tuple[str, str]] = frozenset(), mapas: dict[str, float] | None = None) -> tuple[list[dict], list[dict], np.ndarray]` (partidas, jugadores y `w` verdadero en el orden de columnas de abajo, con las civs en orden alfabético); `generar_equipos(n: int, civs: list[str], semilla: int) -> tuple[list[dict], list[dict], np.ndarray]` (2 contra 2, `w` con las columnas de `GRUPOS_EQUIPOS`). Por defecto: mapas `{"arabia": 0.4, "arena": 0.2, "black_forest": 0.1, "nomad": 0.1, "megarandom": 0.2}`, `elo_medio` parejo entre 600 y 1800, duración real pareja entre 300 y 3000 s, diferencia de Elo normal de desvío 30, todo en un mismo parche dentro de 35 días, y el resultado sorteado con la chance del modelo verdadero (Elo más efectos). La chance verdadera de un casillero es `casilleros(eta(dis, w_verdadero), pesos)`.
- Produces (`modelo.py`):
  - `GRUPOS_1V1 = ("civ", "civ_mapa", "civ_elo", "civ_dur", "cruce", "cruce_mapa", "cruce_dur", "cruce_elo")`, `GRUPOS_EQUIPOS = ("civ", "civ_mapa", "civ_elo", "civ_dur")`.
  - `FRENOS_INICIALES = {"civ": 0.1, "civ_mapa": 0.05, "civ_elo": 0.05, "civ_dur": 0.1, "cruce": 0.05, "cruce_mapa": 0.025, "cruce_dur": 0.05, "cruce_elo": 0.025}` (del prototipo del 2026-10-25 con los datos reales).
  - `@dataclass(frozen=True) class Diseno`: `matriz: sparse.csr_matrix`, `grupos: dict[str, tuple[int, int]]` (primera columna y cantidad), `offset: np.ndarray`, `y: np.ndarray`, `n_civs: int`, `entidades: int` (parejas o civs).
  - `indice_par(i: np.ndarray, j: np.ndarray, n_civs: int) -> np.ndarray` (con `i < j`, en el orden de `itertools.combinations(range(n_civs), 2)`).
  - `diseno_1v1(d: Datos1v1) -> Diseno`, `diseno_equipos(d: DatosEquipos) -> Diseno`.
  - `ajustar(dis: Diseno, frenos: dict[str, float], pesos: np.ndarray | None = None, inicial: np.ndarray | None = None) -> np.ndarray` (`pesos` por partida: 0/1 para entrenar con una parte, cuentas para remuestrear).
  - `perdida(dis: Diseno, w: np.ndarray, filas: np.ndarray) -> float` (pérdida logarítmica media de las filas, con el Elo).
  - `eta(dis: Diseno, w: np.ndarray) -> np.ndarray` de forma `(entidades, 5, 5, 5)`: a Elo parejo, por mapa del modelo (con "otros"), franja de Elo y franja de duración.
  - `pesos_globales(mapa: np.ndarray, elo: np.ndarray, dur: np.ndarray) -> np.ndarray` `(5, 5, 5)`: cuántas partidas hay en cada combinación.
  - `casilleros(eta: np.ndarray, pesos: np.ndarray) -> np.ndarray` `(E, 5, 6, 6)` con la chance de cada casillero de salida.
  - `contar(entidad: np.ndarray, mapa: np.ndarray, elo: np.ndarray, dur: np.ndarray, entidades: int) -> np.ndarray` `(E, 5, 6, 6)` enteros (para equipos, `entidad` es la civ de cada jugador).

Columnas (en este orden): `civ` (C), `civ_mapa` (C·5, columna `civ·5 + mapa`), `civ_elo` (C·5), `civ_dur` (C·5); en 1v1 además `cruce` (P = C(C-1)/2, todas las parejas aunque no hayan jugado), `cruce_mapa`, `cruce_dur` y `cruce_elo` (P·5 cada uno, `par·5 + franja`). Una partida 1v1 pone +1 en las cuatro columnas de civ de A, -1 en las de B y +1 en las cuatro de su pareja; una de equipos suma `civ_equipo` en las cuatro columnas de civ de cada jugador. `offset = dif_elo · ln(10) / 400`. Penalización `Σ w² / (2 · freno²)` de cada grupo; `scipy.optimize.minimize(method="L-BFGS-B", jac=True, maxiter=500)` con la pérdida y su gradiente (`np.logaddexp` para no desbordar).

Casilleros de salida (el algoritmo que la firma no decide):

```
q = sigmoide(eta) · pesos                     # (E, 5, 5, 5)
cada eje de salida tiene "todos" en 0: mapa → (los 5 del modelo, con "otros"), arabia, arena, black_forest, nomad;
Elo y duración → (las 5), franja 0..4
p[e, m, l, d] = suma de q en el conjunto (m, l, d) / suma de pesos en el mismo conjunto
si los pesos de un conjunto suman 0 (Nómada sin partidas 1v1), se usan pesos parejos en ese conjunto
```

- [ ] **Step 1: Escribir los tests**

```python
def test_indice_par_sigue_combinations():
    i, j = np.array([0, 0, 1, 2]), np.array([1, 3, 2, 3])
    assert indice_par(i, j, 4).tolist() == [0, 2, 3, 5]

def test_casilleros_promedia_con_los_pesos():
    eta = np.zeros((1, 5, 5, 5)); eta[0, 0, 0, 0] = np.log(3)   # p = 0,75 en arabia, Elo 0, duración 0; 0,5 en el resto
    pesos = np.zeros((5, 5, 5)); pesos[0, 0, 0] = 1; pesos[1, 0, 0] = 3
    c = casilleros(eta, pesos)
    assert c[0, 0, 0, 0] == pytest.approx((0.75 + 3 * 0.5) / 4)   # todos los mapas
    assert c[0, 1, 0, 0] == pytest.approx(0.75)                     # arabia
    assert c[0, 2, 1, 1] == pytest.approx(0.5)                      # arena, franja 0, franja 0

def test_casilleros_sin_partidas_en_un_mapa_usa_pesos_parejos():
    # pesos en 0 para nomad (índice 3 del modelo) → c[:, 4] finito e igual al promedio simple de sigmoide(eta[:, 3])

def test_recupera_las_chances_verdaderas(tmp_path):
    # generar_1v1(40_000, 6 civs, semilla 7), FRENOS_INICIALES → para las 15 parejas,
    # |casilleros(...)[:, 0, 0, 0] - chance verdadera con los mismos pesos| < 0,03

def test_equipos_recupera_el_orden_de_las_civs(tmp_path):
    # generar_equipos(20_000, 6 civs, semilla 8) → el orden de casilleros(...)[:, 0, 0, 0] es el de los efectos verdaderos de civ
```

- [ ] **Step 2: Correrlos y ver que fallan.**

- [ ] **Step 3: Implementar `modelo.py`.**

- [ ] **Step 4: Correr los tests y ver que pasan.** El de recuperación tiene que tardar menos de 10 s.

- [ ] **Step 5: Commit** `stats: modelo de cruces y de civs por equipos (diseño, ajuste y casilleros)`.

### Task 6: Los frenos y el rango probable

**Files:**
- Modify: `tools/stats/modelo.py`
- Test: `tools/stats/tests/test_rango.py`

**Interfaces:**
- Consumes: todo `modelo` de la Tarea 5.
- Produces (`modelo.py`): `FACTORES_FRENO = (0.25, 0.5, 2, 4)`; `elegir_frenos(dis: Diseno, entrenamiento: np.ndarray, apartadas: np.ndarray, iniciales: dict[str, float]) -> dict[str, float]`; `remuestrear(dis: Diseno, frenos: dict[str, float], w: np.ndarray, veces: int = 20, semilla: int = 1) -> list[np.ndarray]`; `rango(muestras: list[np.ndarray]) -> tuple[np.ndarray, np.ndarray]` (percentiles 5 y 95, elemento a elemento).

`elegir_frenos`: una pasada por los grupos en el orden de `GRUPOS_*`; para cada uno prueba el freno actual por cada factor de `FACTORES_FRENO` (ajustando con `pesos = entrenamiento`, arrancando del último ajuste) y se queda con el de menor `perdida` en `apartadas` (empate: el actual). `remuestrear`: `pesos = np.bincount(rng.integers(0, n, n), minlength=n)` con `np.random.default_rng(semilla)`, arrancando de `w`.

- [ ] **Step 1: Escribir los tests**

```python
def test_el_rango_cubre_lo_verdadero(tmp_path):
    # generar_1v1(40_000, 6 civs, semilla 7); frenos elegidos con los últimos 14 días apartados; 20 remuestreos;
    # en los 75 casilleros (15 parejas × 5 mapas de salida, Elo y duración "todas"), la chance verdadera cae en [desde, hasta]
    # en el 85 % o más (spec 4.6)
def test_remuestrear_repite_con_la_misma_semilla(tmp_path): ...
def test_elegir_frenos_devuelve_un_freno_por_grupo(tmp_path): ...  # claves == GRUPOS_1V1 y cada valor es uno de los probados
```

- [ ] **Step 2: Correrlos y ver que fallan.**

- [ ] **Step 3: Implementar.**

- [ ] **Step 4: Correr los tests y ver que pasan** (el de cobertura, menos de 60 s).

- [ ] **Step 5: Commit** `stats: frenos elegidos con las semanas apartadas y rango por remuestreo`.

### Task 7: La validación

**Files:**
- Create: `tools/stats/validacion.py`
- Test: `tools/stats/tests/test_validacion.py`

**Interfaces:**
- Consumes: `modelo.Diseno`, `modelo.perdida`, `lectura.Datos1v1`.
- Produces (`validacion.py`): `DIAS_APARTADOS = 14`, `MARGEN_CALIBRACION = 0.02`; `particion(inicio: np.ndarray, dias: int = DIAS_APARTADOS) -> tuple[np.ndarray, np.ndarray]` (entrenamiento = antes de la más nueva menos `dias`; apartadas = el resto, el borde incluido); `perdida_solo_elo(dis: Diseno, filas: np.ndarray) -> float`; `perdida_crudo_1v1(d: Datos1v1, entrenamiento: np.ndarray, apartadas: np.ndarray) -> float` (casillero pareja × mapa del modelo × Elo × duración, chance `(ganadas + 0,5) / (n + 1)` con lo de entrenamiento); `calibracion(p: np.ndarray, y: np.ndarray, grupos: int = 10) -> list[tuple[float, float]]` (deciles de `p`: media predicha y observada); `@dataclass(frozen=True) class Validacion` con `perdida_modelo`, `perdida_solo_elo`, `perdida_crudo: float | None`, `calibracion` y las propiedades `motivos: list[str]` (vacía si pasa) y `pasa: bool`; `validar(dis: Diseno, w: np.ndarray, apartadas: np.ndarray, perdida_crudo: float | None = None) -> Validacion`.

Motivos (textos exactos, que `ajustar` muestra): `"no predice mejor que solo el Elo"`, `"no predice mejor que el % crudo de cada casillero"`, `"descalibrado en el decil N: predice X, pasa Y"` (X e Y con tres decimales). `validar` lee `MARGEN_CALIBRACION` del módulo en cada llamada (no como valor por defecto de un parámetro), así los tests de la Tarea 8 lo pueden subir.

- [ ] **Step 1: Escribir los tests**

```python
def test_particion_con_el_borde():           # inicio = [0, 10, 20] días, dias=10 → entrenamiento [True, False, False]
def test_calibracion_de_chances_justas():     # p parejo en [0,2, 0,8], y sorteado con p, 100 000 → cada decil a menos de 0,01
def test_validar_pasa_con_el_modelo(tmp_path):
    # generar_1v1(200_000, 6 civs, semilla 7) (con menos, el ruido de un decil se acerca a los 2 puntos),
    # ajuste con FRENOS_INICIALES y las semanas de entrenamiento → pasa
def test_validar_sin_piezas_no_le_gana_al_elo(tmp_path):  # w = ceros → pasa False y "no predice mejor que solo el Elo" en motivos
def test_validar_descalibrado(tmp_path):     # w del ajuste × 4 → un motivo que empieza con "descalibrado en el decil"
```

- [ ] **Step 2: Correrlos y ver que fallan.**

- [ ] **Step 3: Implementar `validacion.py`.**

- [ ] **Step 4: Correr los tests y ver que pasan.**

- [ ] **Step 5: Commit** `stats: validación contra solo el Elo, el % crudo y la calibración`.

### Task 8: La salida y el comando `ajustar`

**Files:**
- Create: `tools/stats/salida.py`
- Modify: `tools/stats/stats.py` (subcomando `ajustar`)
- Test: `tools/stats/tests/test_salida.py`, `tools/stats/tests/test_ajustar.py`

**Interfaces:**
- Consumes: `lectura.leer_1v1`, `lectura.leer_equipos`, todo `modelo` y `validacion`, `<cache>/fuente.json`.
- Produces (`salida.py`): `a_entradas(claves: list[str], p: np.ndarray, desde: np.ndarray, hasta: np.ndarray, n: np.ndarray) -> dict[str, list[int]]` (milésimas redondeadas, 720 enteros por entrada en el orden de las restricciones); `escribir(ruta: Path, cabecera: dict, uno_contra_uno: dict[str, list[int]], equipos: dict[str, list[int]]) -> None`.
- Produces (`stats.py`): subcomando `ajustar [--cache RUTA] [--salida RUTA] [--veces 20] [--apartar-dias 14]` (salida por defecto `data/matchup_stats.json` del repo).

Formato de `escribir`: la cabecera con sangría de 2 espacios y, después, `"one_vs_one"` y `"team"` con una entrada por renglón (`  "britons|franks": [512,480,543,1830,...],`), claves ordenadas, UTF-8, fin de renglón LF y un salto al final. Cabecera: `schema_version` 1, `generated` (fecha de hoy, ISO), `source` (`name` y `url` de `fuente.json`, `from` y `to` = fechas de la primera y la última partida usadas), `patch` (`label` de `fuente.json`, `start` = fecha de la primera partida del parche actual, `previous_mixed`), `matches` (`1v1` y `team`), `bands` (`elo`, `game_minutes`, `maps`), `interval` 0.9 y `validation` (`held_out_days`, y por tabla las tres pérdidas y la calibración).

`ajustar`, en orden y por tabla (1v1 y equipos): leer; armar el diseño; partir con `particion`; elegir los frenos; ajustar con el entrenamiento y validar (1v1 con `perdida_crudo_1v1`). Si alguna tabla no pasa, mostrar sus motivos y devolver 1 sin tocar la salida. Si las dos pasan: ajustar con todo, remuestrear (`--veces`), `casilleros` del ajuste y de cada muestra (`rango`), `contar`, armar las claves (`"a|b"` con `civs[i]` y `civs[j]` de cada pareja; en equipos, la civ) y escribir. En 1v1, la entidad de `contar` es `indice_par(a, b)`; en equipos, la columna de cada elemento no nulo de `civ_equipo`, repetida tantas veces como su valor absoluto, con el mapa, el Elo y la duración de su partida.

Los tests de `ajustar` suben `validacion.MARGEN_CALIBRACION` a 0,05 con `monkeypatch`: con 40 mil partidas sintéticas (16 mil apartadas) el ruido de un decil ronda 1,2 puntos y el de equipos más.

- [ ] **Step 1: Escribir los tests**

```python
def test_a_entradas_forma_y_rango(): ...              # 2 entidades → 2 claves, 720 enteros cada una, p/desde/hasta en [0, 1000]
def test_escribir_una_entrada_por_renglon(tmp_path): ...  # json.load funciona; cada clave de one_vs_one está en su propio renglón
def test_ajustar_escribe_el_json(tmp_path): ...
    # caché sintética: generar_1v1(40_000, 6 civs) + generar_equipos(20_000, 6 civs) → main(["ajustar", ..., "--veces", "3"]) == 0;
    # schema_version 1, 15 claves "a|b" con a < b, 6 en team, matches["1v1"] == partidas leídas
def test_pareja_sin_partidas_sale_con_n_cero(tmp_path): ...
    # generar_1v1 con sin_jugar={("aztecs", "huns")} → entrada "aztecs|huns" con todos los n en 0 y p entre 1 y 999
def test_ajustar_sin_un_mapa_no_deja_nan(tmp_path): ...
    # generar_1v1 sin "nomad" en mapas → el archivo no tiene NaN, los casilleros de nomad tienen n 0
def test_ajustar_que_no_valida_no_toca_la_salida(tmp_path, monkeypatch): ...
    # salida existente con "viejo"; validacion.validar parcheado para no pasar → main(...) == 1 y el archivo sigue diciendo "viejo"
```

- [ ] **Step 2: Correrlos y ver que fallan.**

- [ ] **Step 3: Implementar `salida.py` y el subcomando `ajustar`.** Mostrar el progreso por consola (lectura, frenos elegidos, validación, remuestreos) como lo hace el extractor.

- [ ] **Step 4: Correr toda la suite de `tools/stats` y ver que pasa.**

- [ ] **Step 5: Commit** `stats: comando ajustar y salida data/matchup_stats.json`.

### Task 9: El comando `informe` y el CI

**Files:**
- Modify: `tools/stats/stats.py` (subcomando `informe`)
- Modify: `.github/workflows/ci.yml` (trabajo nuevo `stats`)
- Test: `tools/stats/tests/test_informe.py`

**Interfaces:**
- Consumes: `lectura.leer_1v1`, `lectura.leer_equipos`, `modelo.contar`, `modelo.indice_par`.
- Produces: `informe [--cache RUTA]`: partidas 1v1 y por equipos, desde y hasta, si se mezcló el parche anterior, y por mapa (todos y los 4) cuántas parejas tienen partidas y la mediana, el p10 y el p90 de partidas por pareja; por equipos, el mínimo y la mediana de apariciones por civ y mapa.

Trabajo del CI (después de `web`, mismo estilo):

```yaml
  stats:
    # La herramienta offline de las estadísticas de cruces: solo los tests con datos inventados (no baja nada).
    runs-on: ubuntu-24.04
    defaults:
      run:
        working-directory: tools/stats
    steps:
      - uses: actions/checkout@fbc6f3992d24b796d5a048ff273f7fcc4a7b6c09 # v5.1.0
      - uses: actions/setup-python@5fda3b95a4ea91299a34e894583c3862153e4b97 # v7.0.0
        with:
          python-version: "3.12"
          cache: pip
          cache-dependency-path: tools/stats/requirements.txt
      - run: pip install -r requirements.txt
      - run: python -m pytest -q
```

- [ ] **Step 1: Escribir el test** `test_informe_muestra_los_mapas(tmp_path, capsys)`: caché sintética → `main(["informe", "--cache", ...]) == 0` y la salida tiene "arabia", "nomad" y "parejas".

- [ ] **Step 2: Correrlo y ver que falla.**

- [ ] **Step 3: Implementar `informe`.**

- [ ] **Step 4: Correr la suite y ver que pasa.**

- [ ] **Step 5: Commit** `stats: comando informe`.

- [ ] **Step 6: Agregar el trabajo `stats` al CI, commit** `CI: tests de tools/stats` **y push** de `fase-9-refinamiento`. Ver el CI con `curl -s "https://api.github.com/repos/Kyriokes/aoe2-bo-coach/actions/runs?per_page=3"`: el trabajo `stats` tiene que terminar en verde.

### Task 10: El primer JSON con los datos reales y los documentos

**Files:**
- Create: `data/matchup_stats.json` (generado)
- Modify: `PLAN.md` (D10: sub-paso (a) hecho, con los números), `CLAUDE.md` (cómo correr `tools/stats`)

- [ ] **Step 1: Bajar y ajustar.** Desde `tools/stats`: `.venv/Scripts/python stats.py bajar` (unas 330 mil partidas 1v1 y 170 mil por equipos con la foto del 2026-10-25), `.venv/Scripts/python stats.py informe` y `.venv/Scripts/python stats.py ajustar`. Esperado (prototipo del 2026-10-25): pérdida del modelo cerca de 0,673 contra 0,678 de solo el Elo, cada decil a menos de 0,02, y el comando termina con 0.

- [ ] **Step 2: Mirar el archivo.** Pesa entre 3 y 6 MB; `"britons|franks"` existe; no hay `NaN`; las entradas con "nomad" tienen `n` 0 en 1v1 (Nómada no estaba en la rotación del 1v1 de esa foto).

- [ ] **Step 3: Commit** `Datos: primer data/matchup_stats.json (aoe2-guide, partidas del 2026-06-01 al 2026-07-03)`.

- [ ] **Step 4: Documentos.** En PLAN.md, D10: "(a) hecho el <fecha>" con las partidas usadas, las pérdidas, la calibración y los frenos elegidos. En CLAUDE.md, en el renglón de D10: cómo se corre (`tools/stats`, venv propio, `bajar` / `informe` / `ajustar`, caché fuera de git) y lo que salió. Commits separados: `PLAN: D10 (a) hecho` y `CLAUDE.md: cómo correr tools/stats`.

- [ ] **Step 5: Push** de `fase-9-refinamiento` y CI en verde.
