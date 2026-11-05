"""Dónde está el juego: la variable de entorno AOE2DE_PATH o, si no está, la biblioteca de Steam que lo tenga. Se busca
igual que en la app (Overlay/GameIcons): SteamPath en el registro, libraryfolders.vdf y la carpeta widgetui del juego."""

from __future__ import annotations

import argparse
import os
import re
import sys
from collections.abc import Mapping
from pathlib import Path

VARIABLE = "AOE2DE_PATH"
CARPETA = Path("steamapps") / "common" / "AoE2DE"
DAT = Path("resources") / "_common" / "dat" / "empires2_x2_p1.dat"
_TEXTURAS = Path("widgetui") / "textures" / "ingame"


def carpeta_de_steam() -> Path | None:
    """La carpeta de Steam según el registro de Windows, o None si no está instalado."""
    if sys.platform != "win32":
        return None
    import winreg

    try:
        with winreg.OpenKey(winreg.HKEY_CURRENT_USER, r"Software\Valve\Steam") as clave:
            return Path(winreg.QueryValueEx(clave, "SteamPath")[0])
    except OSError:
        return None


def bibliotecas(steam: Path) -> list[Path]:
    """La carpeta de Steam y las demás bibliotecas que anota libraryfolders.vdf ("path"  "G:\\\\SteamLibrary")."""
    try:
        texto = (steam / "steamapps" / "libraryfolders.vdf").read_text(encoding="utf-8", errors="replace")
    except OSError:
        return [steam]
    return [steam, *(Path(ruta.replace("\\\\", "\\")) for ruta in re.findall(r'"path"\s+"([^"]+)"', texto))]


def buscar_juego(entorno: Mapping[str, str] | None = None, steam: Path | None = None) -> Path | None:
    """La carpeta del juego, o None si no aparece. La variable de entorno manda y se toma tal cual."""
    entorno = os.environ if entorno is None else entorno
    if propia := entorno.get(VARIABLE):
        return Path(propia)
    steam = steam or carpeta_de_steam()
    if steam is None:
        return None
    for biblioteca in bibliotecas(steam):
        carpeta = biblioteca / CARPETA
        if (carpeta / _TEXTURAS).is_dir():
            return carpeta
    return None


def ubicar_juego(parser: argparse.ArgumentParser) -> Path:
    """La carpeta del juego o, si no aparece, el error de la línea de comandos que dice cómo indicarla."""
    juego = buscar_juego()
    if juego is None:
        parser.error(f"no encontré el juego en las bibliotecas de Steam: pasá la ruta a mano o ponela en la variable {VARIABLE}")
    return juego


def ubicar_dat(parser: argparse.ArgumentParser, dat: Path | None, juego: Path | None = None) -> Path:
    """El .dat que se pidió o el de la carpeta del juego; si no está (un juego a medio instalar), el error que lo dice."""
    dat = dat or (juego or ubicar_juego(parser)) / DAT
    if not dat.is_file():
        parser.error(f"no está el .dat en {dat}: ¿el juego terminó de instalarse?")
    return dat
