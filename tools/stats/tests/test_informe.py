import stats
from cache import sumar
from sinteticos import escribir_tablas, generar_1v1, generar_equipos

CIVS = ["aztecs", "britons", "celts", "franks", "goths", "huns"]


def test_informe_muestra_los_mapas(tmp_path, capsys):
    partidas, jugadores, _ = generar_1v1(3_000, CIVS, semilla=3)
    p_eq, j_eq, _ = generar_equipos(1_000, CIVS, semilla=4)
    partidas += [{**p, "id": p["id"] + 1_000_000} for p in p_eq]
    jugadores += [{**j, "id_partida": j["id_partida"] + 1_000_000} for j in j_eq]
    escribir_tablas(tmp_path / "nuevas", partidas, jugadores)
    sumar(tmp_path / "cache", tmp_path / "nuevas")

    assert stats.main(["informe", "--cache", str(tmp_path / "cache")]) == 0
    salida = capsys.readouterr().out
    assert "arabia" in salida
    assert "nomad" in salida
    assert "parejas" in salida
    assert "3000" in salida  # partidas 1v1
