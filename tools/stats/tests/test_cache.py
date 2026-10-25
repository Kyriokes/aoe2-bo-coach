from pathlib import Path

import duckdb
import pytest

from cache import TablaInvalida, sumar
from sinteticos import escribir_tablas, jugador, partida


def tres_partidas(mapa: str = "arabia") -> tuple[list[dict], list[dict]]:
    partidas = [partida(i, mapa=mapa) for i in (1, 2, 3)]
    jugadores = [j for i in (1, 2, 3) for j in (jugador(i, "franks", 1, True), jugador(i, "britons", 2, False))]
    return partidas, jugadores


def contar(cache: Path) -> tuple[int, int]:
    con = duckdb.connect()
    p = con.execute(f"SELECT count(*) FROM '{(cache / 'partidas.parquet').as_posix()}'").fetchone()[0]
    j = con.execute(f"SELECT count(*) FROM '{(cache / 'jugadores.parquet').as_posix()}'").fetchone()[0]
    return p, j


def test_sumar_a_una_cache_vacia(tmp_path):
    escribir_tablas(tmp_path / "nuevas", *tres_partidas())
    assert sumar(tmp_path / "cache", tmp_path / "nuevas") == 3
    assert contar(tmp_path / "cache") == (3, 6)


def test_sumar_dos_veces_no_duplica(tmp_path):
    escribir_tablas(tmp_path / "nuevas", *tres_partidas())
    sumar(tmp_path / "cache", tmp_path / "nuevas")
    assert sumar(tmp_path / "cache", tmp_path / "nuevas") == 0
    assert contar(tmp_path / "cache") == (3, 6)


def test_sumar_se_queda_con_lo_que_ya_estaba(tmp_path):
    escribir_tablas(tmp_path / "viejas", *tres_partidas("arabia"))
    sumar(tmp_path / "cache", tmp_path / "viejas")
    partidas = [partida(3, mapa="arena"), partida(4, mapa="arena")]
    jugadores = [jugador(3, "celts", 1, True), jugador(3, "goths", 2, False),
                 jugador(4, "celts", 1, True), jugador(4, "goths", 2, False)]
    escribir_tablas(tmp_path / "nuevas", partidas, jugadores)

    assert sumar(tmp_path / "cache", tmp_path / "nuevas") == 1
    con = duckdb.connect()
    mapa = con.execute(f"SELECT mapa FROM '{(tmp_path / 'cache' / 'partidas.parquet').as_posix()}' WHERE id = 3").fetchone()[0]
    civs = sorted(r[0] for r in con.execute(
        f"SELECT civ FROM '{(tmp_path / 'cache' / 'jugadores.parquet').as_posix()}' WHERE id_partida = 3").fetchall())
    assert mapa == "arabia"
    assert civs == ["britons", "franks"]
    assert contar(tmp_path / "cache") == (4, 8)


def test_sumar_tabla_rota_no_toca_la_cache(tmp_path):
    escribir_tablas(tmp_path / "viejas", *tres_partidas())
    sumar(tmp_path / "cache", tmp_path / "viejas")
    antes = {r.name: r.read_bytes() for r in (tmp_path / "cache").iterdir()}
    rota = tmp_path / "rota"
    escribir_tablas(rota, [partida(9)], [jugador(9, "celts", 1, True)])
    con = duckdb.connect()
    sin_parche = (rota / "partidas.parquet").as_posix()
    con.execute(f"COPY (SELECT * EXCLUDE (parche) FROM '{sin_parche}') TO '{(rota / 'tmp.parquet').as_posix()}' (FORMAT parquet)")
    (rota / "tmp.parquet").replace(rota / "partidas.parquet")

    with pytest.raises(TablaInvalida, match="parche"):
        sumar(tmp_path / "cache", rota)
    assert {r.name: r.read_bytes() for r in (tmp_path / "cache").iterdir()} == antes
