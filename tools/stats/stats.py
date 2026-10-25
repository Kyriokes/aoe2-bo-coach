"""Estadísticas reales de cruces (D10): baja partidas clasificatorias, ajusta el modelo y escribe
data/matchup_stats.json. Se corre una vez por parche, desde tools/stats con su venv:

    python stats.py bajar [--fuente aoe2guide] [--cache RUTA]

La caché (por defecto tools/stats/.cache, fuera de git) junta las fotos que se van bajando sin repetir partidas.
"""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

import duckdb

from cache import TablaInvalida, sumar
from fuentes import aoe2guide
from fuentes.aoe2guide import CivDesconocida, FuenteCaida

AQUI = Path(__file__).resolve().parent
REPO = AQUI.parents[1]
CACHE_POR_DEFECTO = AQUI / ".cache"


def _bajar(args: argparse.Namespace) -> int:
    cache: Path = args.cache
    descargas = cache / "descargas" / "aoe2guide"
    normalizadas = cache / "normalizadas" / "aoe2guide"
    print(f"Bajando de {aoe2guide.URL} ...")
    aoe2guide.bajar(descargas, aoe2guide.URL)
    aoe2guide.normalizar(descargas, normalizadas, aoe2guide.claves_de_civs(REPO))
    nuevas = sumar(cache, normalizadas)
    total = duckdb.connect().execute(
        f"SELECT count(*) FROM '{(cache / 'partidas.parquet').as_posix()}'").fetchone()[0]
    fuente = {"name": aoe2guide.NOMBRE, "url": aoe2guide.URL, "patch_label": aoe2guide.etiqueta_de_parche(descargas)}
    (cache / "fuente.json").write_text(json.dumps(fuente, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"{nuevas} partidas nuevas, {total} en total")
    return 0


def main(argv: list[str] | None = None) -> int:
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8")
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    comandos = parser.add_subparsers(dest="comando", required=True)
    bajar = comandos.add_parser("bajar", help="baja una foto de partidas y la suma a la caché")
    bajar.add_argument("--fuente", choices=["aoe2guide"], default="aoe2guide")
    bajar.add_argument("--cache", type=Path, default=CACHE_POR_DEFECTO)
    args = parser.parse_args(argv)

    try:
        return _bajar(args)
    except (FuenteCaida, CivDesconocida, TablaInvalida) as error:
        print(f"Error: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
