"""Íconos de la cola de producción sacados de los archivos del juego, para reconocerlos sin haberlos visto en una partida.

Para cada unidad y tecnología que alguna civ puede hacer (los árboles de CivTechTrees, con las claves de events.json),
toma su ícono de `widgetui/textures/ingame/{units,tech}` y lo resume en 11×11 colores, como ProductionQueue.Features
resume un lugar de la cola. Los teñidos (verde que avanza, amarillo que espera) los arma la app al cargar
(Vision/Hud/QueueIconCatalog). En los íconos de unidades el canal alfa marca dónde va el color del jugador: esas celdas
se anotan en `skip`, porque cambian con el color de cada partida.

Los íconos son de Microsoft: se usan bajo las "Game Content Usage Rules" y no son parte de la licencia MIT del repo
(ver data/icons/LEEME.md).

Uso:
    tools/extractor/.venv/Scripts/python.exe tools/extractor/queue_icons.py [--dat ...] [--juego H:/Steam/.../AoE2DE]
"""

from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path

from genieutils.datfile import DatFile

from extract import ENTIDADES, Extractor, cargar_disponibles, cargar_edades_de_unidades, cargar_textos, clave_de_nombre
from icons import JUEGO_POR_DEFECTO, buscar, leer_dds

REPO = Path(__file__).resolve().parents[2]
GRILLA = 11

# Una celda con más de esto de color del jugador no se compara (el resto del ícono alcanza para reconocerlo).
MASCARA = 0.3

# Si casi todo el ícono es color del jugador no queda qué comparar: se compara entero.
MAX_CELDAS_DE_MASCARA = 60

# Las celdas apenas teñidas por el color del jugador, que no se sabe cuál es, se llenan con un gris.
GRIS = 0.25


def resumen(ruta: Path, es_unidad: bool) -> tuple[list[float], list[int]]:
    """Los 11×11 colores (R, G, B entre 0 y 1) y las celdas del color del jugador."""
    ancho, alto, pixeles = leer_dds(ruta)
    colores: list[float] = []
    mascara: list[float] = []
    for gy in range(GRILLA):
        y0, y1 = alto * gy // GRILLA, alto * (gy + 1) // GRILLA
        for gx in range(GRILLA):
            x0, x1 = ancho * gx // GRILLA, ancho * (gx + 1) // GRILLA
            r = g = b = a = n = 0
            for y in range(y0, y1):
                fila = y * ancho * 4
                for x in range(x0, x1):
                    i = fila + x * 4
                    alfa = pixeles[i + 3]
                    r, g, b, a, n = r + pixeles[i] * alfa, g + pixeles[i + 1] * alfa, b + pixeles[i + 2] * alfa, a + alfa, n + 1
            resto = 1 - a / n / 255
            mascara.append(resto)
            colores += [r / n / 65025 + resto * GRIS, g / n / 65025 + resto * GRIS, b / n / 65025 + resto * GRIS]
    saltear = [i for i, m in enumerate(mascara) if m > MASCARA] if es_unidad else []
    return colores, [] if len(saltear) > MAX_CELDAS_DE_MASCARA else saltear


def claves_comunes() -> set[str]:
    """Las claves que puede pedir una BO: las de game_data y las mejoras de sus unidades (Core/Engine/UnitLines)."""
    datos = sorted((REPO / "data" / "game_data").glob("v*.json"), key=lambda p: [int(n) for n in p.stem[1:].split(".")])[-1]
    comunes = set(json.loads(datos.read_text(encoding="utf-8"))["entities"])
    lineas = (REPO / "src" / "BoCoach.Core" / "Engine" / "UnitLines.cs").read_text(encoding="utf-8")
    return comunes | set(re.findall(r'\["(\w+)"\] = "\w+"', lineas))


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--dat", type=Path, default=JUEGO_POR_DEFECTO / "resources" / "_common" / "dat" / "empires2_x2_p1.dat")
    parser.add_argument("--juego", type=Path, default=JUEGO_POR_DEFECTO)
    parser.add_argument("--salida", type=Path, default=REPO / "data" / "icons" / "queue_game.json")
    args = parser.parse_args()

    print(f"Leyendo {args.dat.name} (unos 30 s)...")
    dat = DatFile.parse(str(args.dat))
    arboles = args.dat.parent / "CivTechTrees"
    disponibles = cargar_disponibles(arboles)
    extractor = Extractor(dat, cargar_edades_de_unidades(arboles))
    textos = cargar_textos(args.dat.parents[2] / "en" / "strings" / "key-value")
    claves_tech = {i: k for k, (t, i) in ENTIDADES.items() if t == "tech"}
    comunes = claves_comunes()
    texturas = args.juego / "widgetui" / "textures" / "ingame"

    iconos: dict[tuple[str, str], dict] = {}
    for tipo in ("unit", "tech"):
        for dat_id in sorted(set(disponibles[tipo]) | {i for t, i in ENTIDADES.values() if t == tipo}):
            if tipo == "tech":
                if dat_id >= len(dat.techs):
                    continue
                numero = dat.techs[dat_id].icon_id
                nombre = textos.get(dat.techs[dat_id].language_dll_name)
                clave = claves_tech.get(dat_id) or (clave_de_nombre(nombre) if nombre else None)
            else:
                numero = dat.civs[0].units[dat_id].icon_id
                nombres = extractor.nombres_de_unidad(dat_id, textos)
                clave = extractor.clave_por_unidad.get(dat_id) or (clave_de_nombre(nombres[0]) if nombres else None)
            archivo = buscar(texturas / ("units" if tipo == "unit" else "tech"), numero) if clave and numero >= 0 else None
            if archivo is None or (clave, tipo) in iconos:
                continue
            colores, saltear = resumen(archivo, tipo == "unit")
            iconos[(clave, tipo)] = {
                "key": clave,
                "unit": tipo == "unit",
                "common": clave in comunes,
                "skip": saltear,
                "features": [round(c, 3) for c in colores],
            }

    args.salida.parent.mkdir(parents=True, exist_ok=True)
    ordenados = sorted(iconos.values(), key=lambda i: (not i["common"], i["key"], i["unit"]))
    args.salida.write_text("[\n" + ",\n".join(json.dumps(i, separators=(",", ":")) for i in ordenados) + "\n]\n", encoding="utf-8")
    print(f"{len(ordenados)} íconos en {args.salida} ({sum(i['common'] for i in ordenados)} comunes)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
