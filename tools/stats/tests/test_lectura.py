import numpy as np

from cache import sumar
from lectura import leer_1v1, leer_equipos, seleccionar_parche
from sinteticos import escribir_tablas, jugador, partida

DIA = 86_400
T0 = 1_780_000_000


def cache_con(tmp_path, partidas, jugadores):
    escribir_tablas(tmp_path / "nuevas", partidas, jugadores)
    sumar(tmp_path / "cache", tmp_path / "nuevas")
    return tmp_path / "cache"


def test_leer_1v1_orienta_y_descarta(tmp_path):
    partidas = [
        partida(1, mapa="arabia", elo_medio=1050, duracion_real_s=1200, inicio=T0 + 5),
        partida(2), partida(3, duracion_real_s=141), partida(4), partida(5, ladder="team"),
        partida(6, mapa="arena", elo_medio=1700, duracion_real_s=3000, inicio=T0),
    ]
    jugadores = [
        jugador(1, "franks", 1, True, 1100), jugador(1, "britons", 2, False, 1000),
        jugador(2, "franks", 1, True), jugador(2, "franks", 2, False),
        jugador(3, "celts", 1, True), jugador(3, "goths", 2, False),
        jugador(4, "celts", 1, True),
        jugador(5, "celts", 1, True), jugador(5, "goths", 2, False),
        jugador(6, "celts", 1, False, 1690), jugador(6, "goths", 2, True, 1710),
    ]
    d = leer_1v1(cache_con(tmp_path, partidas, jugadores))

    assert d.civs == ["britons", "celts", "franks", "goths"]
    orden = np.argsort(d.inicio)  # la 6 primero, después la 1
    assert d.a[orden].tolist() == [1, 0]
    assert d.b[orden].tolist() == [3, 2]
    assert d.gana_a[orden].tolist() == [0.0, 0.0]
    assert d.dif_elo[orden].tolist() == [-20.0, -100.0]
    assert d.mapa[orden].tolist() == [1, 0]
    assert d.elo[orden].tolist() == [4, 2]  # 1700 (1600 o más); 1050 en el borde va a la de arriba
    assert d.dur[orden].tolist() == [4, 2]  # 3000 s = 85 min de juego (55 o más); 1200 s = 34 min (de 30 a 40)
    assert d.previo_mezclado is False
    assert d.inicio_parche == T0


def test_leer_equipos_suma_las_civs_de_cada_lado(tmp_path):
    partidas = [partida(1, ladder="team", elo_medio=1200)]
    jugadores = [jugador(1, "franks", 2, True, 1250), jugador(1, "britons", 2, True, 1150),
                 jugador(1, "franks", 5, False, 1300), jugador(1, "celts", 5, False, 1100)]
    d = leer_equipos(cache_con(tmp_path, partidas, jugadores))

    assert d.civs == ["britons", "celts", "franks"]
    assert d.civ_equipo.toarray().tolist() == [[1, -1, 0]]
    assert d.gana_1.tolist() == [1.0]
    assert d.dif_elo.tolist() == [0.0]
    assert d.elo.tolist() == [2]


def test_leer_equipos_descarta_tres_equipos(tmp_path):
    partidas = [partida(1, ladder="team"), partida(2, ladder="team")]
    jugadores = [jugador(1, "franks", 1, True), jugador(1, "britons", 2, False), jugador(1, "celts", 3, False),
                 jugador(2, "franks", 1, False), jugador(2, "celts", 2, True)]
    d = leer_equipos(cache_con(tmp_path, partidas, jugadores))

    assert d.civ_equipo.shape[0] == 1
    assert d.gana_1.tolist() == [0.0]


def test_parche_nuevo_con_pocas_semanas_suma_el_anterior():
    inicio = np.array([0, 10, 15, 35]) * DIA + T0
    parche = np.array([1, 1, 2, 2])
    mascara, mezclado = seleccionar_parche(inicio, parche)
    assert mascara.tolist() == [True, True, True, True]
    assert mezclado is True


def test_parche_con_cuatro_semanas_va_solo():
    inicio = np.array([0, 10, 15, 45]) * DIA + T0
    parche = np.array([1, 1, 2, 2])
    mascara, mezclado = seleccionar_parche(inicio, parche)
    assert mascara.tolist() == [False, False, True, True]
    assert mezclado is False
