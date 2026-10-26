"""Fuente aoe2-guide.vercel.app/data: fotos de partidas clasificatorias en .parquet (spec D10, sección 2).

Publica matches.parquet (una fila por partida), players.parquet (una por jugador) y ranked_data.json (totales ya
calculados, de donde sale el nombre del parche). No dice quién lo mantiene; los datos salen de la API oficial. Cada
foto cubre unas semanas: la caché las va juntando.
"""

from __future__ import annotations

import json
import re
import urllib.error
import urllib.request
from datetime import datetime, timezone
from pathlib import Path

import duckdb

URL = "https://aoe2-guide.vercel.app/data"
NOMBRE = "aoe2-guide"
ARCHIVOS = ("matches.parquet", "players.parquet", "ranked_data.json")
USER_AGENT = "aoe2-bo-coach-stats (+https://github.com/Kyriokes/aoe2-bo-coach)"

# Nombre en aoe2-guide → clave de game_data, solo donde difieren.
CIVS = {"mayans": "mayan"}
# Civs que se descartan con sus partidas (por ejemplo, una de broma o de un modo especial).
IGNORADAS: frozenset[str] = frozenset()
# Las columnas de cada archivo que se usan.
COLUMNAS = {
    "matches.parquet": ("match_id", "ladder", "map", "start_time", "completion_time", "avg_elo"),
    "players.parquet": ("match_id", "civ_id", "civ", "team", "old_rating", "won"),
}
# patchStart es una fecha sin hora: la primera partida de la foto real empezó 2 horas antes de las 0 UTC de ese día.
TOLERANCIA_PARCHE_S = 86_400


class FuenteCaida(Exception):
    """No se pudo bajar un archivo de la fuente."""


class FuenteInvalida(Exception):
    """La foto no tiene la forma que se espera (otro formato, un archivo roto o datos que no cierran)."""


class CivDesconocida(Exception):
    """La fuente trae civs que no tienen clave en game_data (una DLC nueva antes de correr el extractor)."""

    def __init__(self, civs: list[str]):
        super().__init__("civs sin clave en game_data: " + ", ".join(civs)
                         + " (correr el extractor con el parche nuevo, o agregarlas a CIVS o a IGNORADAS)")
        self.civs = civs


def claves_de_civs(repo: Path) -> set[str]:
    """Las claves de civ del game_data más nuevo (data/game_data/v<mayor>.<menor>.json)."""
    def version(archivo: Path) -> tuple[int, ...]:
        return tuple(int(n) for n in re.findall(r"\d+", archivo.stem))

    archivos = sorted((repo / "data" / "game_data").glob("v*.json"), key=version)
    if not archivos:
        raise FileNotFoundError(f"no hay game_data en {repo / 'data' / 'game_data'}")
    return set(json.loads(archivos[-1].read_text(encoding="utf-8"))["civs"])


def bajar(destino: Path, url: str = URL) -> None:
    """Baja los tres archivos de la fuente a destino."""
    destino.mkdir(parents=True, exist_ok=True)
    for archivo in ARCHIVOS:
        direccion = f"{url}/{archivo}"
        pedido = urllib.request.Request(direccion, headers={"User-Agent": USER_AGENT})
        tmp = destino / (archivo + ".tmp")
        try:
            with urllib.request.urlopen(pedido, timeout=120) as respuesta, tmp.open("wb") as salida:
                while bloque := respuesta.read(1 << 20):
                    salida.write(bloque)
        except (urllib.error.URLError, OSError) as error:
            tmp.unlink(missing_ok=True)
            raise FuenteCaida(f"no se pudo bajar {direccion}: {error}") from error
        tmp.replace(destino / archivo)


def etiqueta_de_parche(origen: Path) -> str | None:
    """El nombre del parche que da ranked_data.json ("Update 185872"), o None si no está."""
    try:
        etiqueta = json.loads((origen / "ranked_data.json").read_text(encoding="utf-8"))["meta"]["patchLabel"]
    except (OSError, ValueError, KeyError, TypeError):
        return None
    return etiqueta if isinstance(etiqueta, str) else None


def inicio_del_parche(origen: Path) -> int:
    """El inicio del parche de la foto (meta.patchStart de ranked_data.json) en segundos unix, a las 0 UTC.

    El "patch" de matches.parquet no sirve para separar parches: es relativo a cada foto (vale 0 en todas).
    """
    try:
        meta = json.loads((origen / "ranked_data.json").read_text(encoding="utf-8"))["meta"]
        fecha = datetime.fromisoformat(meta["patchStart"]).replace(tzinfo=timezone.utc)
    except (OSError, ValueError, KeyError, TypeError) as error:
        raise FuenteInvalida(f"ranked_data.json no dice cuándo empezó el parche (meta.patchStart): {error}") from error
    return int(fecha.timestamp())


def _ruta(path: Path) -> str:
    return path.as_posix().replace("'", "''")


def _controlar_formato(con: duckdb.DuckDBPyConnection, origen: Path) -> None:
    """Que cada archivo se pueda leer como parquet y tenga las columnas que se usan; si no, FuenteInvalida."""
    for archivo, columnas in COLUMNAS.items():
        try:
            tiene = {fila[0] for fila in con.execute(f"DESCRIBE SELECT * FROM '{_ruta(origen / archivo)}'").fetchall()}
        except duckdb.Error as error:
            detalle = str(error).splitlines()[0]
            raise FuenteInvalida(f"{archivo} no se puede leer como parquet (¿cambió el formato de la fuente?): "
                                 f"{detalle}") from error
        faltan = [c for c in columnas if c not in tiene]
        if faltan:
            raise FuenteInvalida(f"a {archivo} le faltan las columnas {', '.join(faltan)} "
                                 f"(¿cambió el formato de la fuente?)")


def normalizar(origen: Path, destino: Path, civs_validas: set[str]) -> None:
    """Traduce la foto de origen a la tabla de partidas de la caché (cache.COLUMNAS_*) y la escribe en destino."""
    parche = inicio_del_parche(origen)
    con = duckdb.connect()
    _controlar_formato(con, origen)
    try:
        _normalizar(con, origen, destino, civs_validas, parche)
    except duckdb.Error as error:
        detalle = str(error).splitlines()[0]
        raise FuenteInvalida(f"no se pudo traducir la foto de {origen}: {detalle}") from error


def _normalizar(con: duckdb.DuckDBPyConnection, origen: Path, destino: Path, civs_validas: set[str],
                parche: int) -> None:
    partidas, jugadores = _ruta(origen / "matches.parquet"), _ruta(origen / "players.parquet")
    primera = con.execute(f"SELECT min(start_time) FROM '{partidas}'").fetchone()[0]
    if primera is not None and primera < parche - TOLERANCIA_PARCHE_S:
        raise FuenteInvalida(f"la foto trae partidas de antes del parche que dice ranked_data.json "
                             f"(la primera, {datetime.fromtimestamp(primera, tz=timezone.utc):%Y-%m-%d}): "
                             f"¿matches.parquet viejo con un ranked_data.json nuevo?")
    traduccion = " ".join(f"WHEN '{viejo}' THEN '{nuevo}'" for viejo, nuevo in CIVS.items())
    civ = f"CASE civ {traduccion} ELSE civ END" if CIVS else "civ"
    con.execute(f"CREATE TEMP TABLE j AS SELECT match_id, civ_id, {civ} AS civ, team, old_rating, won FROM '{jugadores}'")

    desconocidas = sorted(
        (f"civ_id {civ_id}" if nombre is None else nombre)
        for nombre, civ_id in con.execute("SELECT DISTINCT civ, civ_id FROM j").fetchall()
        if nombre is None or (nombre not in civs_validas and nombre not in IGNORADAS)
    )
    if desconocidas:
        raise CivDesconocida(sorted(set(desconocidas)))

    ignoradas = ", ".join(f"'{c}'" for c in IGNORADAS) or "NULL"
    con.execute(f"CREATE TEMP TABLE fuera AS SELECT DISTINCT match_id FROM j WHERE civ IN ({ignoradas})")
    destino.mkdir(parents=True, exist_ok=True)
    con.execute(f"""COPY (SELECT match_id::BIGINT AS id, ladder, "map" AS mapa, start_time::BIGINT AS inicio,
                (completion_time - start_time)::DOUBLE AS duracion_real_s, {parche}::BIGINT AS parche,
                avg_elo::DOUBLE AS elo_medio, '{NOMBRE}' AS fuente
                FROM '{partidas}' WHERE match_id NOT IN (SELECT match_id FROM fuera))
                TO '{_ruta(destino / 'partidas.parquet')}' (FORMAT parquet)""")
    con.execute(f"""COPY (SELECT match_id::BIGINT AS id_partida, civ, team::BIGINT AS equipo,
                old_rating::BIGINT AS elo_previo, won::BOOLEAN AS gano
                FROM j WHERE match_id NOT IN (SELECT match_id FROM fuera))
                TO '{_ruta(destino / 'jugadores.parquet')}' (FORMAT parquet)""")
