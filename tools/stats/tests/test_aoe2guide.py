import json
from datetime import datetime, timezone
from pathlib import Path

import duckdb
import pytest

import stats
from cache import sumar
from fuentes import aoe2guide
from fuentes.aoe2guide import CivDesconocida, FuenteCaida, FuenteInvalida, bajar, claves_de_civs, normalizar
from lectura import leer_1v1
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


def unix(fecha: str) -> int:
    return int(datetime.fromisoformat(fecha).replace(tzinfo=timezone.utc).timestamp())


def foto_aoe2guide(carpeta: Path, civs: tuple[str, str] = ("mayans", "franks"), inicios: tuple[int, ...] = (1780000000,),
                   patch_start: str | None = "2026-05-28", primer_id: int = 77) -> None:
    """matches.parquet, players.parquet y ranked_data.json como los de aoe2-guide, con partidas 1v1 (una por inicio)."""
    carpeta.mkdir(parents=True, exist_ok=True)
    con = duckdb.connect()
    filas_p = ", ".join(f"({primer_id + k}::BIGINT, '1v1', 6::BIGINT, 'arabia', 2::BIGINT, {s}::BIGINT, {s + 1500}::BIGINT,"
                        f" 25.0, 1012.5, '2026-06-07', 0::BIGINT)" for k, s in enumerate(inicios))
    con.execute(f"""COPY (SELECT * FROM (VALUES {filas_p}) t(match_id, ladder, matchtype_id, "map", num_players,
        start_time, completion_time, duration_min, avg_elo, week_end, patch))
        TO '{(carpeta / 'matches.parquet').as_posix()}' (FORMAT parquet)""")
    filas_j = ", ".join(
        f"({primer_id + k}::BIGINT, 1::BIGINT, 13::BIGINT, '{civs[0]}', 1::BIGINT, true, 1000::BIGINT, 1016::BIGINT, 16::BIGINT, 10::BIGINT, 5::BIGINT, 1::BIGINT), "
        f"({primer_id + k}::BIGINT, 2::BIGINT, 9::BIGINT, '{civs[1]}', 2::BIGINT, false, 1025::BIGINT, 1009::BIGINT, -16::BIGINT, 20::BIGINT, 8::BIGINT, -1::BIGINT)"
        for k in range(len(inicios)))
    con.execute(f"""COPY (SELECT * FROM (VALUES {filas_j})
        t(match_id, profile_id, civ_id, civ, team, won, old_rating, new_rating, rating_change, career_wins, career_losses, streak))
        TO '{(carpeta / 'players.parquet').as_posix()}' (FORMAT parquet)""")
    meta = {"patchLabel": "Update de prueba"}
    if patch_start is not None:
        meta["patchStart"] = patch_start
    (carpeta / "ranked_data.json").write_text(json.dumps({"meta": meta}), encoding="utf-8")


def test_normalizar_traduce_columnas_y_civs(tmp_path):
    foto_aoe2guide(tmp_path / "origen")
    normalizar(tmp_path / "origen", tmp_path / "destino", {"mayan", "franks"})

    con = duckdb.connect()
    p = con.execute(f"SELECT * FROM '{(tmp_path / 'destino' / 'partidas.parquet').as_posix()}'").fetchall()
    j = con.execute(f"SELECT civ, equipo, elo_previo, gano FROM '{(tmp_path / 'destino' / 'jugadores.parquet').as_posix()}' "
                    "ORDER BY equipo").fetchall()
    assert p == [(77, "1v1", "arabia", 1780000000, 1500.0, unix("2026-05-28"), 1012.5, "aoe2-guide")]
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


def test_dos_fotos_de_parches_distintos_no_se_mezclan(tmp_path):
    # El "patch" de aoe2-guide es relativo a cada foto (0 en todas): el parche sale del inicio que da ranked_data.json.
    dia = 86_400
    junio = tuple(unix("2026-06-02") + k * dia for k in range(30))
    julio = tuple(unix("2026-07-20") + k * dia for k in range(30))
    foto_aoe2guide(tmp_path / "a", inicios=junio, patch_start="2026-06-02", primer_id=1)
    foto_aoe2guide(tmp_path / "b", inicios=julio, patch_start="2026-07-20", primer_id=1000)
    validas = claves_de_civs(REPO)
    for foto in ("a", "b"):
        normalizar(tmp_path / foto, tmp_path / f"n{foto}", validas)
        sumar(tmp_path / "cache", tmp_path / f"n{foto}")

    d = leer_1v1(tmp_path / "cache")
    assert len(d.gana_a) == 30
    assert d.previo_mezclado is False
    assert d.inicio_parche == unix("2026-07-20")


def test_foto_sin_inicio_de_parche(tmp_path):
    foto_aoe2guide(tmp_path / "origen", patch_start=None)
    with pytest.raises(FuenteInvalida, match="patchStart"):
        normalizar(tmp_path / "origen", tmp_path / "destino", {"mayan", "franks"})


def test_foto_con_partidas_de_antes_del_parche(tmp_path):
    foto_aoe2guide(tmp_path / "origen", inicios=(unix("2026-06-20"),), patch_start="2026-07-01")
    with pytest.raises(FuenteInvalida, match="antes del parche"):
        normalizar(tmp_path / "origen", tmp_path / "destino", {"mayan", "franks"})


def test_columna_renombrada_da_un_mensaje_claro(tmp_path):
    foto_aoe2guide(tmp_path / "origen")
    matches = (tmp_path / "origen" / "matches.parquet").as_posix()
    otra = (tmp_path / "origen" / "otra.parquet").as_posix()
    duckdb.connect().execute(f"""COPY (SELECT * EXCLUDE ("map"), "map" AS mapa FROM '{matches}') TO '{otra}' (FORMAT parquet)""")
    (tmp_path / "origen" / "otra.parquet").replace(tmp_path / "origen" / "matches.parquet")
    with pytest.raises(FuenteInvalida, match=r"matches\.parquet.*map"):
        normalizar(tmp_path / "origen", tmp_path / "destino", {"mayan", "franks"})


def test_archivo_que_no_es_parquet_da_un_mensaje_claro(tmp_path):
    foto_aoe2guide(tmp_path / "origen")
    (tmp_path / "origen" / "players.parquet").write_text("<html>Mantenimiento</html>", encoding="utf-8")
    with pytest.raises(FuenteInvalida, match=r"players\.parquet"):
        normalizar(tmp_path / "origen", tmp_path / "destino", {"mayan", "franks"})
