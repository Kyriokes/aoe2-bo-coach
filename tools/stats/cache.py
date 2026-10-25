"""La caché de partidas: la tabla común a todas las fuentes, acumulada entre bajadas (spec D10, sección 3.1).

Son dos .parquet: partidas.parquet (una fila por partida) y jugadores.parquet (una por jugador de cada partida). Una
partida que ya está en la caché se queda como estaba, con sus jugadores: así bajar dos veces la misma foto no cuenta
nada doble.
"""

from __future__ import annotations

import os
from pathlib import Path

import duckdb

COLUMNAS_PARTIDAS = {
    "id": "BIGINT",
    "ladder": "VARCHAR",
    "mapa": "VARCHAR",
    "inicio": "BIGINT",
    "duracion_real_s": "DOUBLE",
    "parche": "BIGINT",
    "elo_medio": "DOUBLE",
    "fuente": "VARCHAR",
}
COLUMNAS_JUGADORES = {
    "id_partida": "BIGINT",
    "civ": "VARCHAR",
    "equipo": "BIGINT",
    "elo_previo": "BIGINT",
    "gano": "BOOLEAN",
}


class TablaInvalida(Exception):
    """Una tabla nueva no tiene las columnas de la caché."""


def _ruta(path: Path) -> str:
    return path.as_posix().replace("'", "''")


def _controlar(con: duckdb.DuckDBPyConnection, archivo: Path, columnas: dict[str, str]) -> None:
    if not archivo.exists():
        raise TablaInvalida(f"falta {archivo}")
    tiene = {fila[0] for fila in con.execute(f"DESCRIBE SELECT * FROM '{_ruta(archivo)}'").fetchall()}
    faltan = [c for c in columnas if c not in tiene]
    if faltan:
        raise TablaInvalida(f"a {archivo} le faltan las columnas {', '.join(faltan)}")


def _seleccion(archivo: Path, columnas: dict[str, str]) -> str:
    return "SELECT " + ", ".join(f"CAST({c} AS {t}) AS {c}" for c, t in columnas.items()) + f" FROM '{_ruta(archivo)}'"


def sumar(cache: Path, nuevas: Path) -> int:
    """Suma las partidas de nuevas/ a la caché sin repetir ids y devuelve cuántas partidas nuevas entraron."""
    con = duckdb.connect()
    nuevas_p, nuevas_j = nuevas / "partidas.parquet", nuevas / "jugadores.parquet"
    _controlar(con, nuevas_p, COLUMNAS_PARTIDAS)
    _controlar(con, nuevas_j, COLUMNAS_JUGADORES)

    cache.mkdir(parents=True, exist_ok=True)
    cache_p, cache_j = cache / "partidas.parquet", cache / "jugadores.parquet"
    vacia_p = "SELECT " + ", ".join(f"NULL::{t} AS {c}" for c, t in COLUMNAS_PARTIDAS.items()) + " WHERE false"
    vacia_j = "SELECT " + ", ".join(f"NULL::{t} AS {c}" for c, t in COLUMNAS_JUGADORES.items()) + " WHERE false"
    con.execute(f"CREATE TEMP TABLE vp AS {_seleccion(cache_p, COLUMNAS_PARTIDAS) if cache_p.exists() else vacia_p}")
    con.execute(f"CREATE TEMP TABLE vj AS {_seleccion(cache_j, COLUMNAS_JUGADORES) if cache_j.exists() else vacia_j}")
    con.execute(f"CREATE TEMP TABLE np AS SELECT DISTINCT ON (id) * FROM ({_seleccion(nuevas_p, COLUMNAS_PARTIDAS)}) "
                "WHERE id NOT IN (SELECT id FROM vp)")
    con.execute(f"CREATE TEMP TABLE nj AS SELECT * FROM ({_seleccion(nuevas_j, COLUMNAS_JUGADORES)}) "
                "WHERE id_partida IN (SELECT id FROM np)")
    entraron = con.execute("SELECT count(*) FROM np").fetchone()[0]

    tmp_p, tmp_j = cache / "partidas.parquet.tmp", cache / "jugadores.parquet.tmp"
    con.execute(f"COPY (SELECT * FROM vp UNION ALL SELECT * FROM np) TO '{_ruta(tmp_p)}' (FORMAT parquet)")
    con.execute(f"COPY (SELECT * FROM vj UNION ALL SELECT * FROM nj) TO '{_ruta(tmp_j)}' (FORMAT parquet)")
    con.close()
    os.replace(tmp_p, cache_p)
    os.replace(tmp_j, cache_j)
    return entraron
