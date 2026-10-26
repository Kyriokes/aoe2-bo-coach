import numpy as np
import pytest

from franjas import franja_duracion, franja_elo, indice_mapa, minutos_de_juego


def test_minutos_de_juego():
    assert minutos_de_juego(np.array([600.0])).tolist() == [pytest.approx(17.0)]


def test_franja_duracion_con_los_bordes():
    assert franja_duracion(np.array([19.99, 20, 29.99, 30, 54.99, 55, 90])).tolist() == [0, 1, 1, 2, 3, 4, 4]


def test_franja_elo_con_los_bordes():
    assert franja_elo(np.array([500, 849, 850, 1299, 1300, 1599, 1600, 2600])).tolist() == [0, 0, 1, 2, 3, 3, 4, 4]


def test_indice_mapa():
    assert indice_mapa(["arabia", "arena", "black_forest", "nomad", "megarandom"]).tolist() == [0, 1, 2, 3, 4]
