import numpy as np
import pytest

from cache import sumar
from lectura import leer_1v1, leer_equipos
from modelo import (FRENOS_INICIALES, ajustar, casilleros, diseno_1v1, diseno_equipos, eta, indice_par,
                    pesos_globales)
from sinteticos import escribir_tablas, generar_1v1, generar_equipos

CIVS = ["aztecs", "britons", "celts", "franks", "goths", "huns"]


def cache_de(tmp_path, partidas, jugadores):
    escribir_tablas(tmp_path / "nuevas", partidas, jugadores)
    sumar(tmp_path / "cache", tmp_path / "nuevas")
    return tmp_path / "cache"


def _sigmoide(z):
    return 1 / (1 + np.exp(-z))


def test_indice_par_sigue_combinations():
    i, j = np.array([0, 0, 1, 2]), np.array([1, 3, 2, 3])
    assert indice_par(i, j, 4).tolist() == [0, 2, 3, 5]


def test_casilleros_promedia_con_los_pesos():
    e = np.zeros((1, 5, 5, 5))
    e[0, 0, 0, 0] = np.log(3)  # p = 0,75 en arabia, Elo 0, duración 0; 0,5 en el resto
    pesos = np.zeros((5, 5, 5))
    pesos[0, 0, 0] = 1
    pesos[1, 0, 0] = 3
    c = casilleros(e, pesos)
    assert c.shape == (1, 5, 6, 6)
    assert c[0, 0, 0, 0] == pytest.approx((0.75 + 3 * 0.5) / 4)  # todos los mapas
    assert c[0, 1, 0, 0] == pytest.approx(0.75)                    # arabia
    assert c[0, 2, 1, 1] == pytest.approx(0.5)                     # arena, franja 0, franja 0


def test_casilleros_sin_partidas_en_un_mapa_usa_pesos_parejos():
    rng = np.random.default_rng(3)
    e = rng.normal(0, 0.5, (2, 5, 5, 5))
    pesos = rng.integers(1, 10, (5, 5, 5)).astype(float)
    pesos[3] = 0  # nomad
    c = casilleros(e, pesos)
    assert np.isfinite(c).all()
    assert c[:, 4, 0, 0] == pytest.approx(_sigmoide(e[:, 3]).mean(axis=(1, 2)))


def test_recupera_las_chances_verdaderas(tmp_path):
    partidas, jugadores, w_verdadero = generar_1v1(40_000, CIVS, semilla=7)
    d = leer_1v1(cache_de(tmp_path, partidas, jugadores))
    dis = diseno_1v1(d)
    w = ajustar(dis, FRENOS_INICIALES)
    pesos = pesos_globales(d.mapa, d.elo, d.dur)

    ajustado = casilleros(eta(dis, w), pesos)[:, 0, 0, 0]
    verdadero = casilleros(eta(dis, w_verdadero), pesos)[:, 0, 0, 0]
    assert ajustado.shape == (15,)
    assert np.abs(ajustado - verdadero).max() < 0.03


def test_equipos_recupera_las_chances_contra_la_civ_promedio(tmp_path):
    partidas, jugadores, w_verdadero = generar_equipos(20_000, CIVS, semilla=8)
    d = leer_equipos(cache_de(tmp_path, partidas, jugadores))
    dis = diseno_equipos(d)
    w = ajustar(dis, FRENOS_INICIALES)
    pesos = pesos_globales(d.mapa, d.elo, d.dur)

    # En equipos solo cuentan las diferencias entre civs (una constante sumada a todas no cambia ninguna partida):
    # el modelo mide contra la civ promedio, así que lo verdadero se centra igual.
    c = len(CIVS)
    centrado = w_verdadero.copy()
    centrado[:c] -= centrado[:c].mean()
    civ_mapa = centrado[c:6 * c].reshape(c, 5)
    centrado[c:6 * c] = (civ_mapa - civ_mapa.mean(axis=0)).ravel()

    ajustado = casilleros(eta(dis, w), pesos)[:, 0, 0, 0]
    verdadero = casilleros(eta(dis, centrado), pesos)[:, 0, 0, 0]
    assert np.abs(ajustado - verdadero).max() < 0.03
