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


class FuenteCaida(Exception):
    """No se pudo bajar un archivo de la fuente."""


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


def _ruta(path: Path) -> str:
    return path.as_posix().replace("'", "''")


def normalizar(origen: Path, destino: Path, civs_validas: set[str]) -> None:
    """Traduce la foto de origen a la tabla de partidas de la caché (cache.COLUMNAS_*) y la escribe en destino."""
    con = duckdb.connect()
    partidas, jugadores = _ruta(origen / "matches.parquet"), _ruta(origen / "players.parquet")
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
                (completion_time - start_time)::DOUBLE AS duracion_real_s, patch::BIGINT AS parche,
                avg_elo::DOUBLE AS elo_medio, '{NOMBRE}' AS fuente
                FROM '{partidas}' WHERE match_id NOT IN (SELECT match_id FROM fuera))
                TO '{_ruta(destino / 'partidas.parquet')}' (FORMAT parquet)""")
    con.execute(f"""COPY (SELECT match_id::BIGINT AS id_partida, civ, team::BIGINT AS equipo,
                old_rating::BIGINT AS elo_previo, won::BOOLEAN AS gano
                FROM j WHERE match_id NOT IN (SELECT match_id FROM fuera))
                TO '{_ruta(destino / 'jugadores.parquet')}' (FORMAT parquet)""")
