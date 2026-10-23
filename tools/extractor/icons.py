"""Íconos del juego para la paleta del editor de la web (D8).

Para cada entidad de game_data busca su ícono en la instalación del juego (el `icon_id` del .dat, que es el número con
que empieza el archivo en `widgetui/textures/ingame/{units,buildings,tech}`), lo pasa de DDS (256×256, RGBA sin
comprimir) a PNG de 64×64 y lo deja en `web/public/icons/<clave>.png`, más un `index.json` con las claves.

Los íconos son de Microsoft: se usan bajo las "Game Content Usage Rules" (proyecto gratis, con el aviso en la web) y
no son parte de la licencia MIT del repo (ver web/public/icons/LEEME.md).

Uso:
    tools/extractor/.venv/Scripts/python.exe tools/extractor/icons.py [--dat ...] [--juego H:/Steam/.../AoE2DE]
"""

from __future__ import annotations

import argparse
import json
import struct
import sys
import zlib
from pathlib import Path

from genieutils.datfile import DatFile

REPO = Path(__file__).resolve().parents[2]
JUEGO_POR_DEFECTO = Path(r"H:\Steam\steamapps\common\AoE2DE")
LADO = 64

# Íconos de las fuentes de los aldeanos (unidades y objetos del .dat): oveja, ciervo, arbusto de bayas, pez,
# árbol, mina de oro y de piedra. La granja es un edificio de game_data.
FUENTES = {"sheep": 594, "hunt": 65, "berries": 59, "fish": 53, "wood": 411, "gold": 66, "stone": 102}


def leer_dds(ruta: Path) -> tuple[int, int, bytes]:
    datos = ruta.read_bytes()
    if datos[:4] != b"DDS ":
        raise ValueError(f"{ruta.name}: no es DDS")
    alto, ancho = struct.unpack_from("<II", datos, 12)
    banderas, fourcc, bits, rojo, verde, azul, alfa = struct.unpack_from("<I4sIIIII", datos, 80)
    if fourcc in (b"DXT1", b"DXT5"):
        return ancho, alto, dxt(datos[128:], ancho, alto, fourcc == b"DXT5")
    if bits != 32 or fourcc != b"\0\0\0\0":
        raise ValueError(f"{ruta.name}: formato no soportado ({fourcc!r}, {bits} bits)")
    pixeles = bytearray(datos[128 : 128 + ancho * alto * 4])
    if rojo == 0x00FF0000:  # BGRA: se pasa a RGBA
        for i in range(0, len(pixeles), 4):
            pixeles[i], pixeles[i + 2] = pixeles[i + 2], pixeles[i]
    return ancho, alto, bytes(pixeles)


def dxt(bloques: bytes, ancho: int, alto: int, con_alfa: bool) -> bytes:
    """Descomprime DXT1 o DXT5 (bloques de 4×4) a RGBA: algunos íconos del juego vienen así."""

    def color(c: int) -> tuple[int, int, int]:
        return ((c >> 11) * 255 // 31, ((c >> 5) & 63) * 255 // 63, (c & 31) * 255 // 31)

    salida = bytearray(ancho * alto * 4)
    i = 0
    for by in range(0, alto, 4):
        for bx in range(0, ancho, 4):
            alfas = [255] * 16
            if con_alfa:
                a0, a1 = bloques[i], bloques[i + 1]
                bits_a = int.from_bytes(bloques[i + 2 : i + 8], "little")
                tabla = [a0, a1] + ([((7 - k) * a0 + k * a1) // 7 for k in range(1, 7)] if a0 > a1 else [((5 - k) * a0 + k * a1) // 5 for k in range(1, 5)] + [0, 255])
                alfas = [tabla[(bits_a >> (3 * k)) & 7] for k in range(16)]
                i += 8
            c0, c1 = struct.unpack_from("<HH", bloques, i)
            p0, p1 = color(c0), color(c1)
            if c0 > c1 or con_alfa:
                p2 = tuple((2 * a + b) // 3 for a, b in zip(p0, p1))
                p3 = tuple((a + 2 * b) // 3 for a, b in zip(p0, p1))
                t3 = 255
            else:
                p2 = tuple((a + b) // 2 for a, b in zip(p0, p1))
                p3, t3 = (0, 0, 0), 0
            paleta = [p0, p1, p2, p3]
            indices = struct.unpack_from("<I", bloques, i + 4)[0]
            i += 8
            for k in range(16):
                x, y = bx + k % 4, by + k // 4
                if x >= ancho or y >= alto:
                    continue
                j = (indices >> (2 * k)) & 3
                o = (y * ancho + x) * 4
                a = alfas[k] if con_alfa else (t3 if j == 3 else 255)
                salida[o : o + 4] = bytes((*paleta[j], a))
    return bytes(salida)


def achicar(ancho: int, alto: int, rgba: bytes, lado: int) -> bytes:
    """Promedio por área (con el alfa premultiplicado, para que los bordes transparentes no oscurezcan)."""
    fx, fy = ancho / lado, alto / lado
    salida = bytearray(lado * lado * 4)
    for y in range(lado):
        y0, y1 = int(y * fy), max(int(y * fy) + 1, int((y + 1) * fy))
        for x in range(lado):
            x0, x1 = int(x * fx), max(int(x * fx) + 1, int((x + 1) * fx))
            r = g = b = a = 0
            for yy in range(y0, y1):
                fila = yy * ancho * 4
                for xx in range(x0, x1):
                    i = fila + xx * 4
                    al = rgba[i + 3]
                    r += rgba[i] * al
                    g += rgba[i + 1] * al
                    b += rgba[i + 2] * al
                    a += al
            n = (y1 - y0) * (x1 - x0)
            o = (y * lado + x) * 4
            if a:
                salida[o : o + 4] = bytes((r // a, g // a, b // a, a // n))
    return bytes(salida)


def escribir_png(ruta: Path, lado: int, rgba: bytes) -> None:
    def bloque(tipo: bytes, datos: bytes) -> bytes:
        return struct.pack(">I", len(datos)) + tipo + datos + struct.pack(">I", zlib.crc32(tipo + datos) & 0xFFFFFFFF)

    filas = b"".join(b"\0" + rgba[y * lado * 4 : (y + 1) * lado * 4] for y in range(lado))
    png = b"\x89PNG\r\n\x1a\n" + bloque(b"IHDR", struct.pack(">IIBBBBB", lado, lado, 8, 6, 0, 0, 0))
    png += bloque(b"IDAT", zlib.compress(filas, 9)) + bloque(b"IEND", b"")
    ruta.write_bytes(png)


def buscar(carpeta: Path, icono: int) -> Path | None:
    for ancho in (3, 4):
        prefijo = f"{icono:0{ancho}d}_"
        for archivo in sorted(carpeta.iterdir()):
            if archivo.name.startswith(prefijo) and archivo.suffix.lower() == ".dds":
                return archivo
    return None


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--dat", type=Path, default=JUEGO_POR_DEFECTO / "resources" / "_common" / "dat" / "empires2_x2_p1.dat")
    parser.add_argument("--juego", type=Path, default=JUEGO_POR_DEFECTO)
    parser.add_argument("--salida", type=Path, default=REPO / "web" / "public" / "icons")
    args = parser.parse_args()

    datos = sorted((REPO / "data" / "game_data").glob("v*.json"), key=lambda p: [int(n) for n in p.stem[1:].split(".")])[-1]
    entidades = json.loads(datos.read_text(encoding="utf-8"))["entities"]
    print(f"Leyendo {args.dat.name} (unos 30 s)...")
    dat = DatFile.parse(str(args.dat))
    unidades = dat.civs[0].units
    texturas = args.juego / "widgetui" / "textures" / "ingame"
    args.salida.mkdir(parents=True, exist_ok=True)

    pedidos: dict[str, tuple[str, int]] = {}
    for clave, e in entidades.items():
        if e["kind"] == "tech":
            pedidos[clave] = ("tech", dat.techs[e["dat_id"]].icon_id)
        else:
            unidad = unidades[e["dat_id"]]
            pedidos[clave] = ("units" if e["kind"] == "unit" else "buildings", unidad.icon_id)
    for fuente, dat_id in FUENTES.items():
        pedidos[f"source_{fuente}"] = ("units", unidades[dat_id].icon_id)

    hechos, faltan = [], []
    for clave, (carpeta, icono) in sorted(pedidos.items()):
        archivo = buscar(texturas / carpeta, icono) if icono >= 0 else None
        if archivo is None:
            faltan.append(f"{clave} ({carpeta} {icono})")
            continue
        ancho, alto, rgba = leer_dds(archivo)
        escribir_png(args.salida / f"{clave}.png", LADO, achicar(ancho, alto, rgba, LADO))
        hechos.append(clave)
    (args.salida / "index.json").write_text(json.dumps(sorted(hechos), indent=2) + "\n", encoding="utf-8")
    print(f"{len(hechos)} íconos en {args.salida}")
    if faltan:
        print("Sin ícono: " + ", ".join(faltan))
    return 0


if __name__ == "__main__":
    sys.exit(main())
