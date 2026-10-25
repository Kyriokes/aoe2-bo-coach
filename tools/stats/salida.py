"""La salida data/matchup_stats.json (spec D10, sección 3.2).

Cada entrada (una pareja "a|b" en 1v1, una civ en equipos) es un arreglo plano de 180 casilleros [p, desde, hasta, n]:
p, desde y hasta en milésimas y n en partidas, en el orden mapa (todos, arabia, arena, black_forest, nomad) × Elo
(todas y las 5) × duración (todas y las 5). Una entrada por renglón, así los cambios de cada parche se leen en el diff.
"""

from __future__ import annotations

import json
from pathlib import Path

import numpy as np


def a_entradas(claves: list[str], p: np.ndarray, desde: np.ndarray, hasta: np.ndarray,
               n: np.ndarray) -> dict[str, list[int]]:
    """Las entradas de la salida: para cada clave, sus 180 casilleros [p, desde, hasta, n] en enteros."""
    milesimas = [np.rint(np.clip(x, 0, 1) * 1000).astype(np.int64) for x in (p, desde, hasta)]
    casilleros = np.stack([*milesimas, np.asarray(n, dtype=np.int64)], axis=-1)
    return {clave: [int(v) for v in casilleros[e].ravel()] for e, clave in enumerate(claves)}


def _bloque(nombre: str, entradas: dict[str, list[int]]) -> str:
    renglones = [f"    {json.dumps(clave, ensure_ascii=False)}: {json.dumps(valores, separators=(',', ':'))}"
                 for clave, valores in sorted(entradas.items())]
    return f'  "{nombre}": {{\n' + ",\n".join(renglones) + "\n  }"


def escribir(ruta: Path, cabecera: dict, uno_contra_uno: dict[str, list[int]], equipos: dict[str, list[int]]) -> None:
    """La cabecera con sangría y después "one_vs_one" y "team" con una entrada por renglón, en UTF-8 con LF."""
    texto = json.dumps(cabecera, ensure_ascii=False, indent=2)
    texto = texto[: texto.rindex("}")].rstrip() + ",\n"
    texto += _bloque("one_vs_one", uno_contra_uno) + ",\n" + _bloque("team", equipos) + "\n}\n"
    ruta.parent.mkdir(parents=True, exist_ok=True)
    ruta.write_text(texto, encoding="utf-8", newline="\n")
