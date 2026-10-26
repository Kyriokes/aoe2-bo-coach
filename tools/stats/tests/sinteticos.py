"""Ayudas de los tests: tablas de partidas armadas a mano y partidas inventadas con fuerzas conocidas."""

from __future__ import annotations

import csv
import itertools
import tempfile
from pathlib import Path

import duckdb
import numpy as np

from cache import COLUMNAS_JUGADORES, COLUMNAS_PARTIDAS

T0 = 1_780_000_000
DIA = 86_400
MAPAS_POR_DEFECTO = {"arabia": 0.4, "arena": 0.2, "black_forest": 0.1, "nomad": 0.1, "megarandom": 0.2}
MAPAS_DEL_MODELO = ("arabia", "arena", "black_forest", "nomad")
# Desvíos de los efectos verdaderos; los demás grupos valen 0.
EFECTOS = {"civ": 0.2, "civ_mapa": 0.1, "cruce": 0.15}


def _escribir(con: duckdb.DuckDBPyConnection, ruta: Path, columnas: dict[str, str], filas: list[dict]) -> None:
    con.execute("CREATE OR REPLACE TEMP TABLE t (" + ", ".join(f"{c} {t}" for c, t in columnas.items()) + ")")
    if filas:
        with tempfile.TemporaryDirectory() as carpeta:
            archivo = Path(carpeta) / "filas.csv"
            with archivo.open("w", newline="", encoding="utf-8") as salida:
                escritor = csv.writer(salida)
                for fila in filas:
                    escritor.writerow(fila[c] for c in columnas)
            con.execute(f"COPY t FROM '{archivo.as_posix()}' (FORMAT csv, HEADER false)")
    con.execute(f"COPY t TO '{ruta.as_posix()}' (FORMAT parquet)")


def escribir_tablas(carpeta: Path, partidas: list[dict], jugadores: list[dict]) -> None:
    """Escribe partidas.parquet y jugadores.parquet en la carpeta, con las columnas de la caché."""
    carpeta.mkdir(parents=True, exist_ok=True)
    con = duckdb.connect()
    _escribir(con, carpeta / "partidas.parquet", COLUMNAS_PARTIDAS, partidas)
    _escribir(con, carpeta / "jugadores.parquet", COLUMNAS_JUGADORES, jugadores)


def partida(id: int, mapa: str = "arabia", ladder: str = "1v1", inicio: int = T0,
            duracion_real_s: float = 1200.0, parche: int = 0, elo_medio: float = 1000.0) -> dict:
    return {"id": id, "ladder": ladder, "mapa": mapa, "inicio": inicio, "duracion_real_s": duracion_real_s,
            "parche": parche, "elo_medio": elo_medio, "fuente": "prueba"}


def jugador(id_partida: int, civ: str, equipo: int, gano: bool, elo_previo: int = 1000) -> dict:
    return {"id_partida": id_partida, "civ": civ, "equipo": equipo, "elo_previo": elo_previo, "gano": gano}


def _contexto(rng: np.random.Generator, n: int, mapas: dict[str, float]):
    nombres = list(mapas)
    probabilidades = np.array([mapas[m] for m in nombres]) / sum(mapas.values())
    mapa = [nombres[k] for k in rng.choice(len(nombres), size=n, p=probabilidades)]
    lugar = np.array([MAPAS_DEL_MODELO.index(m) if m in MAPAS_DEL_MODELO else 4 for m in mapa])
    elo_medio = rng.uniform(600, 1800, n).round(1)
    duracion = rng.uniform(300, 3000, n).round(1)
    inicio = T0 + rng.integers(0, 35 * DIA, n)
    dif = rng.normal(0, 30, n)
    return mapa, lugar, elo_medio, duracion, inicio, dif


def _sigmoide(z: np.ndarray) -> np.ndarray:
    return 1 / (1 + np.exp(-z))


def generar_1v1(n: int, civs: list[str], semilla: int, sin_jugar: frozenset[tuple[str, str]] = frozenset(),
                mapas: dict[str, float] | None = None) -> tuple[list[dict], list[dict], np.ndarray]:
    """Partidas 1v1 con fuerzas conocidas; devuelve también el w verdadero con las columnas de modelo.GRUPOS_1V1."""
    rng = np.random.default_rng(semilla)
    civs = sorted(civs)
    c = len(civs)
    pares = list(itertools.combinations(range(c), 2))
    p = len(pares)
    civ = rng.normal(0, EFECTOS["civ"], c)
    civ_mapa = rng.normal(0, EFECTOS["civ_mapa"], (c, 5))
    cruce = rng.normal(0, EFECTOS["cruce"], p)
    w = np.concatenate([civ, civ_mapa.ravel(), np.zeros(10 * c), cruce, np.zeros(15 * p)])

    jugables = [k for k, (i, j) in enumerate(pares) if (civs[i], civs[j]) not in sin_jugar]
    mapa, lugar, elo_medio, duracion, inicio, dif = _contexto(rng, n, mapas or MAPAS_POR_DEFECTO)
    elegidos = rng.choice(jugables, size=n)
    partidas, jugadores = [], []
    for fila in range(n):
        k = elegidos[fila]
        i, j = pares[k]
        ea, eb = int(round(elo_medio[fila] + dif[fila] / 2)), int(round(elo_medio[fila] - dif[fila] / 2))
        m = lugar[fila]
        z = (ea - eb) * np.log(10) / 400 + civ[i] - civ[j] + civ_mapa[i, m] - civ_mapa[j, m] + cruce[k]
        gana_a = bool(rng.random() < _sigmoide(z))
        id = fila + 1
        partidas.append(partida(id, mapa=mapa[fila], inicio=int(inicio[fila]), duracion_real_s=float(duracion[fila]),
                                elo_medio=float(elo_medio[fila])))
        jugadores += [jugador(id, civs[i], 1, gana_a, ea), jugador(id, civs[j], 2, not gana_a, eb)]
    return partidas, jugadores, w


def generar_equipos(n: int, civs: list[str], semilla: int,
                    escala_elo: float = 1.0) -> tuple[list[dict], list[dict], np.ndarray]:
    """Partidas 2 contra 2 con fuerzas de civ conocidas; w verdadero con las columnas de modelo.GRUPOS_EQUIPOS.

    escala_elo: cuánto pesa la diferencia de Elo medio contra la fórmula del Elo (en los datos reales, unas 1,85).
    """
    rng = np.random.default_rng(semilla)
    civs = sorted(civs)
    c = len(civs)
    civ = rng.normal(0, EFECTOS["civ"], c)
    civ_mapa = rng.normal(0, EFECTOS["civ_mapa"], (c, 5))
    w = np.concatenate([civ, civ_mapa.ravel(), np.zeros(10 * c), [escala_elo - 1]])

    mapa, lugar, elo_medio, duracion, inicio, dif = _contexto(rng, n, MAPAS_POR_DEFECTO)
    partidas, jugadores = [], []
    for fila in range(n):
        cuatro = rng.integers(0, c, 4)
        e1, e2 = int(round(elo_medio[fila] + dif[fila] / 2)), int(round(elo_medio[fila] - dif[fila] / 2))
        m = lugar[fila]
        z = (escala_elo * (e1 - e2) * np.log(10) / 400 + civ[cuatro[:2]].sum() + civ_mapa[cuatro[:2], m].sum()
             - civ[cuatro[2:]].sum() - civ_mapa[cuatro[2:], m].sum())
        gana_1 = bool(rng.random() < _sigmoide(z))
        id = fila + 1
        partidas.append(partida(id, mapa=mapa[fila], ladder="team", inicio=int(inicio[fila]),
                                duracion_real_s=float(duracion[fila]), elo_medio=float(elo_medio[fila])))
        jugadores += [jugador(id, civs[cuatro[0]], 1, gana_1, e1), jugador(id, civs[cuatro[1]], 1, gana_1, e1),
                      jugador(id, civs[cuatro[2]], 2, not gana_1, e2), jugador(id, civs[cuatro[3]], 2, not gana_1, e2)]
    return partidas, jugadores, w
