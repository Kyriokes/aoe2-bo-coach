import numpy as np
import pytest

from cache import sumar
from lectura import leer_1v1
from modelo import (FACTORES_FRENO, FRENOS_INICIALES, GRUPOS_1V1, ajustar, casilleros, diseno_1v1, elegir_frenos, eta,
                    pesos_globales, rango, remuestrear)
from sinteticos import DIA, escribir_tablas, generar_1v1

CIVS = ["aztecs", "britons", "celts", "franks", "goths", "huns"]


def preparar(tmp_path, n=40_000, semilla=7):
    partidas, jugadores, w_verdadero = generar_1v1(n, CIVS, semilla=semilla)
    escribir_tablas(tmp_path / "nuevas", partidas, jugadores)
    sumar(tmp_path / "cache", tmp_path / "nuevas")
    d = leer_1v1(tmp_path / "cache")
    apartadas = d.inicio >= d.inicio.max() - 14 * DIA
    return d, diseno_1v1(d), w_verdadero, ~apartadas, apartadas


def test_el_rango_cubre_lo_verdadero(tmp_path):
    d, dis, w_verdadero, entrenamiento, apartadas = preparar(tmp_path)
    frenos = elegir_frenos(dis, entrenamiento, apartadas, FRENOS_INICIALES)
    w = ajustar(dis, frenos)
    pesos = pesos_globales(d.mapa, d.elo, d.dur)
    muestras = [casilleros(eta(dis, m), pesos) for m in remuestrear(dis, frenos, w, veces=20)]
    desde, hasta = rango(casilleros(eta(dis, w), pesos), muestras)
    verdadero = casilleros(eta(dis, w_verdadero), pesos)

    dentro = (desde[:, :, 0, 0] <= verdadero[:, :, 0, 0]) & (verdadero[:, :, 0, 0] <= hasta[:, :, 0, 0])
    assert dentro.shape == (15, 5)
    assert dentro.mean() >= 0.85


def test_remuestrear_repite_con_la_misma_semilla(tmp_path):
    _, dis, _, _, _ = preparar(tmp_path, n=5_000)
    w = ajustar(dis, FRENOS_INICIALES)
    uno = remuestrear(dis, FRENOS_INICIALES, w, veces=2, semilla=5)
    otro = remuestrear(dis, FRENOS_INICIALES, w, veces=2, semilla=5)
    assert len(uno) == 2
    assert all(np.array_equal(a, b) for a, b in zip(uno, otro))
    assert not np.array_equal(uno[0], uno[1])


def test_elegir_frenos_devuelve_un_freno_por_grupo(tmp_path):
    _, dis, _, entrenamiento, apartadas = preparar(tmp_path, n=5_000)
    frenos = elegir_frenos(dis, entrenamiento, apartadas, FRENOS_INICIALES)
    assert tuple(frenos) == GRUPOS_1V1
    probados = {1, *FACTORES_FRENO}
    assert all(any(np.isclose(frenos[g] / FRENOS_INICIALES[g], f) for f in probados) for g in GRUPOS_1V1)


def test_rango_usa_el_desvio_en_escala_logit():
    p = np.array([0.5, 0.5])
    muestras = [np.array([1 / (1 + np.exp(1)), 0.5]), np.array([1 / (1 + np.exp(-1)), 0.5])]  # logit -1 y +1; 0 y 0
    desde, hasta = rango(p, muestras)
    s = np.sqrt(2)  # desvío de (-1, +1) con ddof=1
    assert desde[0] == pytest.approx(1 / (1 + np.exp(1.645 * s)))
    assert hasta[0] == pytest.approx(1 / (1 + np.exp(-1.645 * s)))
    assert desde[1] == pytest.approx(0.5) and hasta[1] == pytest.approx(0.5)
