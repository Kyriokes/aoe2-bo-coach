"""La búsqueda de la carpeta del juego. Se corre con el pytest de tools/stats: python -m pytest -q ../extractor"""

from pathlib import Path

import pytest

import juego as j


class Parser:
    """Como argparse: error() corta el programa."""

    mensaje = ""

    def error(self, mensaje: str):
        self.mensaje = mensaje
        raise SystemExit(2)


def instalar(biblioteca: Path) -> Path:
    carpeta = biblioteca / j.CARPETA
    (carpeta / "widgetui" / "textures" / "ingame").mkdir(parents=True)
    return carpeta


def steam_con(tmp_path: Path, *otras: Path) -> Path:
    """Una carpeta de Steam con su libraryfolders.vdf, que escribe las barras dobles como el de verdad."""
    steam = tmp_path / "Steam"
    (steam / "steamapps").mkdir(parents=True)
    bloques = "\n".join(f'\t"{i}"\n\t{{\n\t\t"path"\t\t"{str(b).replace(chr(92), chr(92) * 2)}"\n\t\t"label"\t\t""\n\t}}'
                        for i, b in enumerate([steam, *otras]))
    (steam / "steamapps" / "libraryfolders.vdf").write_text(f'"libraryfolders"\n{{\n{bloques}\n}}\n', encoding="utf-8")
    return steam


def test_la_variable_de_entorno_manda(tmp_path):
    steam = steam_con(tmp_path)
    instalar(steam)
    propia = tmp_path / "otra carpeta"
    assert j.buscar_juego({j.VARIABLE: str(propia)}, steam) == propia


def test_una_variable_vacia_es_como_si_no_estuviera(tmp_path):
    assert j.buscar_juego({j.VARIABLE: ""}, steam_con(tmp_path)) is None


def test_lo_encuentra_en_la_carpeta_de_steam(tmp_path):
    steam = steam_con(tmp_path)
    carpeta = instalar(steam)
    assert j.buscar_juego({}, steam) == carpeta


def test_lo_encuentra_en_otra_biblioteca_de_libraryfolders(tmp_path):
    biblioteca = tmp_path / "SteamLibrary"
    carpeta = instalar(biblioteca)
    assert j.buscar_juego({}, steam_con(tmp_path, biblioteca)) == carpeta


def test_una_carpeta_sin_los_archivos_del_juego_no_cuenta(tmp_path):
    biblioteca = tmp_path / "SteamLibrary"
    (biblioteca / j.CARPETA).mkdir(parents=True)
    assert j.buscar_juego({}, steam_con(tmp_path, biblioteca)) is None


def test_sin_libraryfolders_mira_solo_la_carpeta_de_steam(tmp_path):
    steam = tmp_path / "Steam"
    carpeta = instalar(steam)
    assert j.buscar_juego({}, steam) == carpeta


def test_sin_steam_no_encuentra_nada(monkeypatch):
    monkeypatch.setattr(j, "carpeta_de_steam", lambda: None)
    assert j.buscar_juego({}) is None


def test_el_dat_a_mano_no_necesita_buscar_el_juego(tmp_path, monkeypatch):
    monkeypatch.setattr(j, "buscar_juego", lambda: None)
    dat = tmp_path / "empires2_x2_p1.dat"
    dat.write_bytes(b"x")
    assert j.ubicar_dat(Parser(), dat) == dat


def test_el_dat_sale_de_la_carpeta_del_juego(tmp_path):
    dat = tmp_path / j.DAT
    dat.parent.mkdir(parents=True)
    dat.write_bytes(b"x")
    assert j.ubicar_dat(Parser(), None, tmp_path) == dat


def test_si_el_juego_esta_a_medio_instalar_dice_donde_falta_el_dat(tmp_path):
    parser = Parser()
    with pytest.raises(SystemExit):
        j.ubicar_dat(parser, None, tmp_path)
    assert str(tmp_path) in parser.mensaje


def test_si_no_encuentra_el_juego_dice_como_indicarlo(monkeypatch):
    monkeypatch.setattr(j, "buscar_juego", lambda: None)
    parser = Parser()
    with pytest.raises(SystemExit):
        j.ubicar_juego(parser)
    assert j.VARIABLE in parser.mensaje
