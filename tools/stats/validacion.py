"""La validación del modelo: el criterio de salida de la spec D10, sección 4.6.

Con las partidas de los últimos 14 días apartadas, el modelo ajustado con el resto tiene que predecirlas mejor que
"solo el Elo" y (en 1v1) que el % crudo de cada casillero, y estar calibrado: por deciles de lo que predice, lo que
pasó queda a 2 puntos o menos.
"""

from __future__ import annotations

from dataclasses import dataclass

import numpy as np

from lectura import Datos1v1
from modelo import FRANJAS, Diseno, indice_par, perdida

DIAS_APARTADOS = 14
MARGEN_CALIBRACION = 0.02


def particion(inicio: np.ndarray, dias: int = DIAS_APARTADOS) -> tuple[np.ndarray, np.ndarray]:
    """Entrenamiento (antes de la partida más nueva menos `dias`) y apartadas (el resto, el borde incluido)."""
    entrenamiento = np.asarray(inicio) < np.max(inicio) - dias * 86_400
    return entrenamiento, ~entrenamiento


def perdida_solo_elo(dis: Diseno, filas: np.ndarray) -> float:
    """La pérdida logarítmica media de esas partidas prediciendo solo con la diferencia de Elo."""
    z = dis.offset[filas]
    return float(np.mean(np.logaddexp(0, z) - dis.y[filas] * z))


def perdida_crudo_1v1(d: Datos1v1, entrenamiento: np.ndarray, apartadas: np.ndarray) -> float:
    """La pérdida de predecir cada partida apartada con el % crudo de su casillero (pareja, mapa, Elo y duración)."""
    par = indice_par(d.a, d.b, len(d.civs))
    casillero = ((par * FRANJAS + d.mapa) * FRANJAS + d.elo) * FRANJAS + d.dur
    total = int(casillero.max()) + 1
    ganadas = np.bincount(casillero[entrenamiento], weights=d.gana_a[entrenamiento], minlength=total)
    jugadas = np.bincount(casillero[entrenamiento], minlength=total)
    p = ((ganadas + 0.5) / (jugadas + 1))[casillero[apartadas]]
    y = d.gana_a[apartadas]
    return float(-np.mean(y * np.log(p) + (1 - y) * np.log(1 - p)))


def calibracion(p: np.ndarray, y: np.ndarray, grupos: int = 10) -> list[tuple[float, float]]:
    """Por deciles de p: la chance media predicha y la proporción de victorias que pasó."""
    orden = np.argsort(p, kind="stable")
    return [(float(np.mean(p[g])), float(np.mean(y[g]))) for g in np.array_split(orden, grupos)]


@dataclass(frozen=True)
class Validacion:
    perdida_modelo: float
    perdida_solo_elo: float
    perdida_crudo: float | None
    calibracion: list[tuple[float, float]]

    @property
    def motivos(self) -> list[str]:
        motivos = []
        if not self.perdida_modelo < self.perdida_solo_elo:
            motivos.append("no predice mejor que solo el Elo")
        if self.perdida_crudo is not None and not self.perdida_modelo < self.perdida_crudo:
            motivos.append("no predice mejor que el % crudo de cada casillero")
        for n, (predicho, observado) in enumerate(self.calibracion, start=1):
            if abs(predicho - observado) > MARGEN_CALIBRACION:
                motivos.append(f"descalibrado en el decil {n}: predice {predicho:.3f}, pasa {observado:.3f}")
        return motivos

    @property
    def pasa(self) -> bool:
        return not self.motivos


def validar(dis: Diseno, w: np.ndarray, apartadas: np.ndarray, perdida_crudo: float | None = None) -> Validacion:
    """La validación de un ajuste hecho sin las partidas apartadas."""
    z = dis.matriz[apartadas] @ w + dis.offset[apartadas]
    p = 0.5 * (1 + np.tanh(0.5 * z))
    return Validacion(perdida(dis, w, apartadas), perdida_solo_elo(dis, apartadas), perdida_crudo,
                      calibracion(p, dis.y[apartadas]))
