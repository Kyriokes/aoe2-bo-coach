"""Franjas de mapa, Elo y duración que usan el modelo y la salida (spec D10, sección 4.2).

La duración que dan las fuentes es tiempo real (la de aoe2-guide es completion_time - start_time). El reloj del juego
corre a 1,7 veces el tiempo real en la velocidad normal de DE, así que los minutos de juego son los reales × 1,7.
Falta verificarlo con una partida grabada del usuario contra su historial de la API: si la pantalla de carga entra en
la duración, hay que restarla.
"""

from __future__ import annotations

from collections.abc import Sequence

import numpy as np

FACTOR_JUEGO = 1.7
MINIMO_JUEGO_MIN = 5  # menos que esto son abandonos y desconexiones

MAPAS = ("arabia", "arena", "black_forest", "nomad")  # claves de data/maps.json
OTROS = 4  # en el modelo, el resto de los mapas va junto

LIMITES_ELO = (850, 1050, 1300, 1600)
LIMITES_JUEGO_MIN = (20, 30, 40, 55)


def minutos_de_juego(duracion_real_s: np.ndarray) -> np.ndarray:
    """Los minutos del reloj del juego que duró cada partida."""
    return np.asarray(duracion_real_s, dtype=float) / 60 * FACTOR_JUEGO


def franja_duracion(minutos_juego: np.ndarray) -> np.ndarray:
    """De 0 (menos de 20 minutos de juego) a 4 (55 o más). Un valor en el borde va a la franja de arriba."""
    return np.searchsorted(LIMITES_JUEGO_MIN, np.asarray(minutos_juego, dtype=float), side="right")


def franja_elo(elo: np.ndarray) -> np.ndarray:
    """De 0 (menos de 850) a 4 (1600 o más). Un valor en el borde va a la franja de arriba."""
    return np.searchsorted(LIMITES_ELO, np.asarray(elo, dtype=float), side="right")


def indice_mapa(mapas: Sequence[str]) -> np.ndarray:
    """El lugar de cada mapa en MAPAS, y OTROS para los que no están."""
    lugar = {mapa: i for i, mapa in enumerate(MAPAS)}
    return np.array([lugar.get(mapa, OTROS) for mapa in mapas], dtype=np.int64)
