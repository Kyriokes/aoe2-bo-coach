import numpy as np
import pytest

from cache import sumar
from lectura import leer_1v1
from modelo import FRENOS_INICIALES, ajustar, diseno_1v1
from sinteticos import DIA, escribir_tablas, generar_1v1
from validacion import calibracion, particion, perdida_crudo_1v1, validar

CIVS = ["aztecs", "britons", "celts", "franks", "goths", "huns"]


@pytest.fixture(scope="module")
def ajustado(tmp_path_factory):
    # Con menos partidas, el ruido de un decil se acerca a los 2 puntos del margen.
    carpeta = tmp_path_factory.mktemp("validacion")
    partidas, jugadores, _ = generar_1v1(200_000, CIVS, semilla=7)
    escribir_tablas(carpeta / "nuevas", partidas, jugadores)
    sumar(carpeta / "cache", carpeta / "nuevas")
    d = leer_1v1(carpeta / "cache")
    dis = diseno_1v1(d)
    entrenamiento, apartadas = particion(d.inicio)
    w = ajustar(dis, FRENOS_INICIALES, entrenamiento.astype(float))
    return d, dis, w, entrenamiento, apartadas


def test_particion_con_el_borde():
    entrenamiento, apartadas = particion(np.array([0, 10, 20]) * DIA, dias=10)
    assert entrenamiento.tolist() == [True, False, False]
    assert apartadas.tolist() == [False, True, True]


def test_calibracion_de_chances_justas():
    rng = np.random.default_rng(4)  # con 100 mil, el ruido de un decil llegaba a 0,012
    p = rng.uniform(0.2, 0.8, 1_000_000)
    y = (rng.random(1_000_000) < p).astype(float)
    deciles = calibracion(p, y)
    assert len(deciles) == 10
    assert all(abs(predicho - observado) < 0.01 for predicho, observado in deciles)


def test_validar_pasa_con_el_modelo(ajustado):
    d, dis, w, entrenamiento, apartadas = ajustado
    v = validar(dis, w, apartadas, perdida_crudo_1v1(d, entrenamiento, apartadas))
    assert v.motivos == []
    assert v.pasa
    assert v.perdida_modelo < v.perdida_solo_elo < v.perdida_crudo


def test_validar_sin_piezas_no_le_gana_al_elo(ajustado):
    _, dis, w, _, apartadas = ajustado
    v = validar(dis, np.zeros_like(w), apartadas)
    assert not v.pasa
    assert "no predice mejor que solo el Elo" in v.motivos


def test_validar_descalibrado(ajustado):
    _, dis, w, _, apartadas = ajustado
    v = validar(dis, w * 4, apartadas)
    assert not v.pasa
    assert any(m.startswith("descalibrado en el decil") for m in v.motivos)


def test_validar_equipos_sin_civs_no_le_gana_al_elo_con_su_escala(tmp_path, monkeypatch):
    # Con la escala del Elo ajustada, el modelo de equipos le ganaba a la fórmula aunque las civs no aportaran nada.
    import sinteticos
    from lectura import leer_equipos
    from modelo import diseno_equipos
    from sinteticos import generar_equipos
    from validacion import escala_elo

    monkeypatch.setitem(sinteticos.EFECTOS, "civ", 0.0)
    monkeypatch.setitem(sinteticos.EFECTOS, "civ_mapa", 0.0)
    partidas, jugadores, _ = generar_equipos(20_000, CIVS, semilla=12, escala_elo=1.85)
    escribir_tablas(tmp_path / "nuevas", partidas, jugadores)
    sumar(tmp_path / "cache", tmp_path / "nuevas")
    d = leer_equipos(tmp_path / "cache")
    dis = diseno_equipos(d)
    entrenamiento, apartadas = particion(d.inicio)
    w = ajustar(dis, FRENOS_INICIALES, entrenamiento.astype(float))

    escala = escala_elo(dis, entrenamiento)
    assert escala == pytest.approx(1.85, abs=0.15)
    v = validar(dis, w, apartadas, escala_elo=escala)
    assert "no predice mejor que solo el Elo" in v.motivos
