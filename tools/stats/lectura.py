"""Lectura de la caché para el modelo: elige el parche, filtra y arma los arreglos con las franjas (spec D10, 4.1).

Entran las partidas del parche actual (el de la partida más nueva); si ese parche abarca menos de 28 días, también
las del anterior. Después se descartan las de menos de 5 minutos de juego y, en 1v1, los espejos y las que no tienen
exactamente dos jugadores de equipos distintos; en equipos, las que no tienen exactamente dos equipos.
"""

from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path

import duckdb
import numpy as np
from scipy import sparse

from franjas import FACTOR_JUEGO, MINIMO_JUEGO_MIN, franja_duracion, franja_elo, indice_mapa, minutos_de_juego

DIAS_MINIMOS_DEL_PARCHE = 28


@dataclass(frozen=True)
class Datos1v1:
    civs: list[str]
    a: np.ndarray
    b: np.ndarray
    gana_a: np.ndarray
    dif_elo: np.ndarray
    mapa: np.ndarray
    elo: np.ndarray
    dur: np.ndarray
    inicio: np.ndarray
    previo_mezclado: bool
    inicio_parche: int


@dataclass(frozen=True)
class DatosEquipos:
    civs: list[str]
    civ_equipo: sparse.csr_matrix
    gana_1: np.ndarray
    dif_elo: np.ndarray
    mapa: np.ndarray
    elo: np.ndarray
    dur: np.ndarray
    inicio: np.ndarray
    previo_mezclado: bool
    inicio_parche: int


def seleccionar_parche(inicio: np.ndarray, parche: np.ndarray) -> tuple[np.ndarray, bool]:
    """Las partidas del parche actual, más las del anterior si el actual abarca menos de 28 días."""
    actual = parche[np.argmax(inicio)]
    del_actual = parche == actual
    abarca = inicio[del_actual].max() - inicio[del_actual].min()
    if abarca >= DIAS_MINIMOS_DEL_PARCHE * 86_400 or del_actual.all():
        return del_actual, False
    anterior = parche[~del_actual][np.argmax(inicio[~del_actual])]
    return del_actual | (parche == anterior), True


def _parche(con: duckdb.DuckDBPyConnection, partidas: str) -> tuple[str, bool, int]:
    """La condición SQL de los parches elegidos, si se mezcló el anterior y el inicio del parche actual."""
    p = con.execute(f"SELECT inicio, parche FROM '{partidas}'").fetchnumpy()
    inicio, parche = np.asarray(p["inicio"]), np.asarray(p["parche"])
    mascara, mezclado = seleccionar_parche(inicio, parche)
    actual = parche[np.argmax(inicio)]
    elegidos = ", ".join(str(int(x)) for x in np.unique(parche[mascara]))
    return f"parche IN ({elegidos})", mezclado, int(inicio[parche == actual].min())


def _rutas(cache: Path) -> tuple[str, str]:
    return (cache / "partidas.parquet").as_posix(), (cache / "jugadores.parquet").as_posix()


def _duracion_valida() -> str:
    return f"duracion_real_s / 60 * {FACTOR_JUEGO} >= {MINIMO_JUEGO_MIN}"


def leer_1v1(cache: Path) -> Datos1v1:
    partidas, jugadores = _rutas(cache)
    con = duckdb.connect()
    condicion, mezclado, inicio_parche = _parche(con, partidas)
    f = con.execute(f"""
        WITH m AS (SELECT * FROM '{partidas}' WHERE ladder = '1v1' AND {condicion} AND {_duracion_valida()}),
             j AS (SELECT * FROM '{jugadores}' WHERE id_partida IN (SELECT id FROM m)),
             dos AS (SELECT id_partida FROM j GROUP BY id_partida
                     HAVING count(*) = 2 AND count(DISTINCT equipo) = 2 AND count(DISTINCT civ) = 2)
        SELECT m.mapa, m.inicio, m.duracion_real_s, m.elo_medio, x.civ AS ca, y.civ AS cb,
               x.gano::DOUBLE AS ga, (x.elo_previo - y.elo_previo)::DOUBLE AS dif
        FROM m JOIN dos ON dos.id_partida = m.id
             JOIN j x ON x.id_partida = m.id
             JOIN j y ON y.id_partida = m.id AND x.civ < y.civ
        ORDER BY m.id
    """).fetchnumpy()
    ca, cb = [str(c) for c in f["ca"]], [str(c) for c in f["cb"]]
    civs = sorted(set(ca) | set(cb))
    lugar = {c: i for i, c in enumerate(civs)}
    return Datos1v1(
        civs=civs,
        a=np.array([lugar[c] for c in ca], dtype=np.int64),
        b=np.array([lugar[c] for c in cb], dtype=np.int64),
        gana_a=np.asarray(f["ga"], dtype=float),
        dif_elo=np.asarray(f["dif"], dtype=float),
        mapa=indice_mapa([str(m) for m in f["mapa"]]),
        elo=franja_elo(f["elo_medio"]),
        dur=franja_duracion(minutos_de_juego(f["duracion_real_s"])),
        inicio=np.asarray(f["inicio"], dtype=np.int64),
        previo_mezclado=mezclado,
        inicio_parche=inicio_parche,
    )


def leer_equipos(cache: Path) -> DatosEquipos:
    partidas, jugadores = _rutas(cache)
    con = duckdb.connect()
    condicion, mezclado, inicio_parche = _parche(con, partidas)
    con.execute(f"""
        CREATE TEMP TABLE m AS SELECT * FROM '{partidas}' WHERE ladder = 'team' AND {condicion} AND {_duracion_valida()};
        CREATE TEMP TABLE j AS SELECT * FROM '{jugadores}' WHERE id_partida IN (SELECT id FROM m);
        CREATE TEMP TABLE eq AS SELECT id_partida, min(equipo) AS e1 FROM j GROUP BY id_partida
                                HAVING count(DISTINCT equipo) = 2;
    """)
    f = con.execute("""
        SELECT m.id, m.mapa, m.inicio, m.duracion_real_s, m.elo_medio,
               (avg(CASE WHEN j.equipo = eq.e1 THEN j.elo_previo END)
                - avg(CASE WHEN j.equipo <> eq.e1 THEN j.elo_previo END))::DOUBLE AS dif,
               bool_or(CASE WHEN j.equipo = eq.e1 THEN j.gano END)::DOUBLE AS gana1
        FROM m JOIN eq ON eq.id_partida = m.id JOIN j ON j.id_partida = m.id
        GROUP BY m.id, m.mapa, m.inicio, m.duracion_real_s, m.elo_medio
        ORDER BY m.id
    """).fetchnumpy()
    c = con.execute("""
        SELECT j.id_partida, j.civ, sum(CASE WHEN j.equipo = eq.e1 THEN 1 ELSE -1 END)::INTEGER AS v
        FROM j JOIN eq ON eq.id_partida = j.id_partida
        GROUP BY j.id_partida, j.civ
        ORDER BY j.id_partida, j.civ
    """).fetchnumpy()
    ids = np.asarray(f["id"])
    civs = sorted({str(x) for x in c["civ"]})
    lugar = {x: i for i, x in enumerate(civs)}
    filas = np.searchsorted(ids, np.asarray(c["id_partida"]))
    columnas = np.array([lugar[str(x)] for x in c["civ"]], dtype=np.int64)
    civ_equipo = sparse.csr_matrix((np.asarray(c["v"], dtype=float), (filas, columnas)), shape=(len(ids), len(civs)))
    civ_equipo.eliminate_zeros()
    return DatosEquipos(
        civs=civs,
        civ_equipo=civ_equipo,
        gana_1=np.asarray(f["gana1"], dtype=float),
        dif_elo=np.asarray(f["dif"], dtype=float),
        mapa=indice_mapa([str(x) for x in f["mapa"]]),
        elo=franja_elo(f["elo_medio"]),
        dur=franja_duracion(minutos_de_juego(f["duracion_real_s"])),
        inicio=np.asarray(f["inicio"], dtype=np.int64),
        previo_mezclado=mezclado,
        inicio_parche=inicio_parche,
    )
