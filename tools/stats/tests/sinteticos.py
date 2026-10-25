"""Ayudas de los tests: tablas de partidas armadas a mano y partidas inventadas con fuerzas conocidas."""

from __future__ import annotations

from pathlib import Path

import duckdb

from cache import COLUMNAS_JUGADORES, COLUMNAS_PARTIDAS


def _escribir(con: duckdb.DuckDBPyConnection, ruta: Path, columnas: dict[str, str], filas: list[dict]) -> None:
    con.execute("CREATE OR REPLACE TEMP TABLE t (" + ", ".join(f"{c} {t}" for c, t in columnas.items()) + ")")
    if filas:
        con.executemany(
            "INSERT INTO t VALUES (" + ", ".join("?" for _ in columnas) + ")",
            [[fila[c] for c in columnas] for fila in filas],
        )
    con.execute(f"COPY t TO '{ruta.as_posix()}' (FORMAT parquet)")


def escribir_tablas(carpeta: Path, partidas: list[dict], jugadores: list[dict]) -> None:
    """Escribe partidas.parquet y jugadores.parquet en la carpeta, con las columnas de la caché."""
    carpeta.mkdir(parents=True, exist_ok=True)
    con = duckdb.connect()
    _escribir(con, carpeta / "partidas.parquet", COLUMNAS_PARTIDAS, partidas)
    _escribir(con, carpeta / "jugadores.parquet", COLUMNAS_JUGADORES, jugadores)


def partida(id: int, mapa: str = "arabia", ladder: str = "1v1", inicio: int = 1_780_000_000,
            duracion_real_s: float = 1200.0, parche: int = 0, elo_medio: float = 1000.0) -> dict:
    return {"id": id, "ladder": ladder, "mapa": mapa, "inicio": inicio, "duracion_real_s": duracion_real_s,
            "parche": parche, "elo_medio": elo_medio, "fuente": "prueba"}


def jugador(id_partida: int, civ: str, equipo: int, gano: bool, elo_previo: int = 1000) -> dict:
    return {"id_partida": id_partida, "civ": civ, "equipo": equipo, "elo_previo": elo_previo, "gano": gano}
