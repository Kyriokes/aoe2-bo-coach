from pathlib import Path

import duckdb
import pytest

import stats
from cache import sumar
from fuentes import aoe2guide
from fuentes.aoe2guide import CivDesconocida, FuenteCaida, bajar, claves_de_civs, normalizar
from sinteticos import escribir_tablas, jugador, partida

REPO = Path(__file__).resolve().parents[3]

# Las civs de players.parquet de aoe2-guide, medidas el 2026-10-25.
CIVS_DE_LA_FOTO = [
    "armenians", "aztecs", "bengalis", "berbers", "bohemians", "britons", "bulgarians", "burgundians", "burmese",
    "byzantines", "celts", "chinese", "cumans", "dravidians", "ethiopians", "franks", "georgians", "goths", "gurjaras",
    "hindustanis", "huns", "incas", "italians", "japanese", "jurchens", "khitans", "khmer", "koreans", "lithuanians",
    "magyars", "malay", "malians", "mapuche", "mayans", "mongols", "muisca", "persians", "poles", "portuguese",
    "romans", "saracens", "shu", "sicilians", "slavs", "spanish", "tatars", "teutons", "tupi", "turks", "vietnamese",
    "vikings", "wei", "wu",
]


def foto_aoe2guide(carpeta: Path, civs: tuple[str, str] = ("mayans", "franks")) -> None:
    """Un matches.parquet y un players.parquet con las columnas de aoe2-guide y una partida 1v1."""
    carpeta.mkdir(parents=True, exist_ok=True)
    con = duckdb.connect()
    con.execute(f"""COPY (SELECT 77::BIGINT match_id, '1v1' ladder, 6::BIGINT matchtype_id, 'arabia' "map",
        2::BIGINT num_players, 1780000000::BIGINT start_time, 1780001500::BIGINT completion_time, 25.0 duration_min,
        1012.5 avg_elo, '2026-06-07' week_end, 0::BIGINT patch) TO '{(carpeta / 'matches.parquet').as_posix()}' (FORMAT parquet)""")
    con.execute(f"""COPY (SELECT * FROM (VALUES
        (77::BIGINT, 1::BIGINT, 13::BIGINT, '{civs[0]}', 1::BIGINT, true, 1000::BIGINT, 1016::BIGINT, 16::BIGINT, 10::BIGINT, 5::BIGINT, 1::BIGINT),
        (77::BIGINT, 2::BIGINT, 9::BIGINT, '{civs[1]}', 2::BIGINT, false, 1025::BIGINT, 1009::BIGINT, -16::BIGINT, 20::BIGINT, 8::BIGINT, -1::BIGINT))
        t(match_id, profile_id, civ_id, civ, team, won, old_rating, new_rating, rating_change, career_wins, career_losses, streak))
        TO '{(carpeta / 'players.parquet').as_posix()}' (FORMAT parquet)""")


def test_normalizar_traduce_columnas_y_civs(tmp_path):
    foto_aoe2guide(tmp_path / "origen")
    normalizar(tmp_path / "origen", tmp_path / "destino", {"mayan", "franks"})

    con = duckdb.connect()
    p = con.execute(f"SELECT * FROM '{(tmp_path / 'destino' / 'partidas.parquet').as_posix()}'").fetchall()
    j = con.execute(f"SELECT civ, equipo, elo_previo, gano FROM '{(tmp_path / 'destino' / 'jugadores.parquet').as_posix()}' "
                    "ORDER BY equipo").fetchall()
    assert p == [(77, "1v1", "arabia", 1780000000, 1500.0, 0, 1012.5, "aoe2-guide")]
    assert j == [("mayan", 1, 1000, True), ("franks", 2, 1025, False)]


def test_civ_desconocida_frena_con_su_nombre(tmp_path):
    foto_aoe2guide(tmp_path / "origen", ("atlanteans", "franks"))
    with pytest.raises(CivDesconocida) as error:
        normalizar(tmp_path / "origen", tmp_path / "destino", {"mayan", "franks"})
    assert error.value.civs == ["atlanteans"]
    assert not (tmp_path / "destino").exists() or not any((tmp_path / "destino").iterdir())


def test_las_civs_de_la_foto_tienen_clave():
    validas = claves_de_civs(REPO)
    assert [c for c in CIVS_DE_LA_FOTO if aoe2guide.CIVS.get(c, c) not in validas] == []


def test_bajar_con_la_fuente_caida(tmp_path):
    with pytest.raises(FuenteCaida, match="127.0.0.1:9"):
        bajar(tmp_path, url="http://127.0.0.1:9")


def test_comando_bajar_con_la_fuente_caida_no_toca_la_cache(tmp_path, monkeypatch):
    escribir_tablas(tmp_path / "viejas", [partida(i) for i in (1, 2, 3)],
                    [jugador(i, "franks", 1, True) for i in (1, 2, 3)])
    sumar(tmp_path, tmp_path / "viejas")
    monkeypatch.setattr(aoe2guide, "URL", "http://127.0.0.1:9")

    assert stats.main(["bajar", "--cache", str(tmp_path)]) == 1
    con = duckdb.connect()
    assert con.execute(f"SELECT count(*) FROM '{(tmp_path / 'partidas.parquet').as_posix()}'").fetchone()[0] == 3
