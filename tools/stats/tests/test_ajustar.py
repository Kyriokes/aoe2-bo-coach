import json

import numpy as np
import pytest

import stats
import validacion
from cache import sumar
from lectura import leer_1v1
from sinteticos import escribir_tablas, generar_1v1, generar_equipos

CIVS = ["aztecs", "britons", "celts", "franks", "goths", "huns"]
CORRIMIENTO_EQUIPOS = 1_000_000  # los ids de las partidas por equipos, para que no choquen con los de 1v1


def cache_sintetica(carpeta, sin_jugar=frozenset(), mapas=None):
    partidas, jugadores, _ = generar_1v1(40_000, CIVS, semilla=7, sin_jugar=sin_jugar, mapas=mapas)
    p_eq, j_eq, _ = generar_equipos(20_000, CIVS, semilla=8)
    partidas += [{**p, "id": p["id"] + CORRIMIENTO_EQUIPOS} for p in p_eq]
    jugadores += [{**j, "id_partida": j["id_partida"] + CORRIMIENTO_EQUIPOS} for j in j_eq]
    escribir_tablas(carpeta / "nuevas", partidas, jugadores)
    sumar(carpeta / "cache", carpeta / "nuevas")
    (carpeta / "cache" / "fuente.json").write_text(
        json.dumps({"name": "prueba", "url": "https://ejemplo", "patch_label": "Parche de prueba"}), encoding="utf-8")
    return carpeta / "cache"


def correr_ajustar(cache, salida):
    # Con 40 mil partidas inventadas (16 mil apartadas), el ruido de un decil ronda 1,2 puntos y el de equipos más.
    with pytest.MonkeyPatch.context() as parche:
        parche.setattr(validacion, "MARGEN_CALIBRACION", 0.05)
        return stats.main(["ajustar", "--cache", str(cache), "--salida", str(salida), "--veces", "3"])


@pytest.fixture(scope="module")
def normal(tmp_path_factory):
    carpeta = tmp_path_factory.mktemp("normal")
    cache = cache_sintetica(carpeta)
    salida = carpeta / "matchup_stats.json"
    return cache, salida, correr_ajustar(cache, salida)


@pytest.fixture(scope="module")
def con_huecos(tmp_path_factory):
    carpeta = tmp_path_factory.mktemp("huecos")
    mapas = {"arabia": 0.5, "arena": 0.2, "black_forest": 0.1, "megarandom": 0.2}
    cache = cache_sintetica(carpeta, sin_jugar=frozenset({("aztecs", "huns")}), mapas=mapas)
    salida = carpeta / "matchup_stats.json"
    return cache, salida, correr_ajustar(cache, salida)


def test_ajustar_escribe_el_json(normal):
    cache, salida, codigo = normal
    assert codigo == 0
    datos = json.loads(salida.read_text(encoding="utf-8"))
    assert datos["schema_version"] == 1
    assert len(datos["one_vs_one"]) == 15
    assert all(a < b for a, b in (clave.split("|") for clave in datos["one_vs_one"]))
    assert sorted(datos["team"]) == CIVS
    assert datos["matches"]["1v1"] == len(leer_1v1(cache).gana_a)
    assert datos["source"]["name"] == "prueba"
    assert datos["patch"]["label"] == "Parche de prueba"
    assert datos["bands"]["maps"] == ["arabia", "arena", "black_forest", "nomad"]
    assert datos["interval"] == 0.9
    assert len(datos["validation"]["one_vs_one"]["calibration"]) == 10
    assert sorted(datos["validation"]["one_vs_one"]["brakes"]) == sorted(
        ["civ", "civ_mapa", "civ_elo", "civ_dur", "cruce", "cruce_mapa", "cruce_dur", "cruce_elo"])
    assert datos["validation"]["team"]["elo_only_scale"] == pytest.approx(1.0, abs=0.2)  # los inventados, escala 1
    assert "elo_only_scale" not in datos["validation"]["one_vs_one"]


def test_pareja_sin_partidas_sale_con_n_cero(con_huecos):
    _, salida, codigo = con_huecos
    assert codigo == 0
    casilleros = np.array(json.loads(salida.read_text(encoding="utf-8"))["one_vs_one"]["aztecs|huns"]).reshape(5, 6, 6, 4)
    assert (casilleros[..., 3] == 0).all()
    assert ((casilleros[..., 0] >= 1) & (casilleros[..., 0] <= 999)).all()


def test_ajustar_sin_un_mapa_no_deja_nan(con_huecos):
    _, salida, codigo = con_huecos
    assert codigo == 0
    texto = salida.read_text(encoding="utf-8")
    assert "NaN" not in texto
    datos = json.loads(texto)
    for valores in datos["one_vs_one"].values():  # por equipos sí hay Nómada, como en los datos reales
        casilleros = np.array(valores).reshape(5, 6, 6, 4)
        assert (casilleros[4, ..., 3] == 0).all()  # nomad
        assert ((casilleros[4, ..., 0] >= 1) & (casilleros[4, ..., 0] <= 999)).all()


def test_ajustar_que_no_valida_no_toca_la_salida(normal, tmp_path, monkeypatch):
    cache, _, _ = normal
    salida = tmp_path / "matchup_stats.json"
    salida.write_text("viejo", encoding="utf-8")
    monkeypatch.setattr(validacion, "validar",
                        lambda *args, **kwargs: validacion.Validacion(0.7, 0.6, None, [(0.5, 0.5)] * 10))

    assert stats.main(["ajustar", "--cache", str(cache), "--salida", str(salida), "--veces", "3"]) == 1
    assert salida.read_text(encoding="utf-8") == "viejo"


def test_ajustar_pide_al_menos_dos_remuestreos(tmp_path):
    # Con una sola muestra el desvío es NaN y los rangos salían como -9223372036854775808 sin aviso.
    with pytest.raises(SystemExit) as salida:
        stats.main(["ajustar", "--cache", str(tmp_path), "--salida", str(tmp_path / "x.json"), "--veces", "1"])
    assert salida.value.code == 2
    assert not (tmp_path / "x.json").exists()
