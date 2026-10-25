"""El modelo de cruces 1v1 y de civs por equipos (spec D10, sección 4.3).

Regresión logística con una penalización por grupo de parámetros (equivale a previas normales con un desvío por
grupo, el "freno"). El Elo entra primero, fijo, con la fórmula del Elo: lo que ajustan las piezas es de la civ.

1v1: cada partida va con la civ A primera en orden alfabético y la salida es si gana A. Piezas de civ (+A y -B):
fuerza general, ajuste por mapa, por franja de Elo y por franja de duración. Piezas del cruce: el cruce en sí, ajuste
por mapa, por duración y por Elo. Todas las parejas tienen columnas aunque no hayan jugado (la penalización las deja
en 0).

Equipos: cada jugador suma su civ (+1 en su equipo, -1 en el otro); la chance de una civ es la de un equipo que la
tiene en lugar de una civ promedio. Sin piezas de cruce.
"""

from __future__ import annotations

from dataclasses import dataclass

import numpy as np
from scipy import sparse
from scipy.optimize import minimize

from lectura import Datos1v1, DatosEquipos

GRUPOS_1V1 = ("civ", "civ_mapa", "civ_elo", "civ_dur", "cruce", "cruce_mapa", "cruce_dur", "cruce_elo")
GRUPOS_EQUIPOS = ("civ", "civ_mapa", "civ_elo", "civ_dur")
# Del prototipo del 2026-10-25 con los datos reales de aoe2-guide.
FRENOS_INICIALES = {"civ": 0.1, "civ_mapa": 0.05, "civ_elo": 0.05, "civ_dur": 0.1,
                    "cruce": 0.05, "cruce_mapa": 0.025, "cruce_dur": 0.05, "cruce_elo": 0.025}
FRANJAS = 5  # mapas del modelo (con "otros"), franjas de Elo y de duración


@dataclass(frozen=True)
class Diseno:
    matriz: sparse.csr_matrix
    grupos: dict[str, tuple[int, int]]
    offset: np.ndarray
    y: np.ndarray
    n_civs: int
    entidades: int


def indice_par(i: np.ndarray, j: np.ndarray, n_civs: int) -> np.ndarray:
    """El lugar de la pareja (i, j), con i < j, en el orden de itertools.combinations(range(n_civs), 2)."""
    i, j = np.asarray(i), np.asarray(j)
    return i * n_civs - i * (i + 1) // 2 + (j - i - 1)


def _grupos(nombres: tuple[str, ...], c: int, p: int) -> dict[str, tuple[int, int]]:
    tamanos = {"civ": c, "civ_mapa": c * FRANJAS, "civ_elo": c * FRANJAS, "civ_dur": c * FRANJAS,
               "cruce": p, "cruce_mapa": p * FRANJAS, "cruce_dur": p * FRANJAS, "cruce_elo": p * FRANJAS}
    grupos, inicio = {}, 0
    for nombre in nombres:
        grupos[nombre] = (inicio, tamanos[nombre])
        inicio += tamanos[nombre]
    return grupos


def _offset(dif_elo: np.ndarray) -> np.ndarray:
    return np.asarray(dif_elo, dtype=float) * np.log(10) / 400


def _columnas_de_civ(grupos, civ, mapa, elo, dur) -> list[np.ndarray]:
    return [grupos["civ"][0] + civ,
            grupos["civ_mapa"][0] + civ * FRANJAS + mapa,
            grupos["civ_elo"][0] + civ * FRANJAS + elo,
            grupos["civ_dur"][0] + civ * FRANJAS + dur]


def diseno_1v1(d: Datos1v1) -> Diseno:
    c = len(d.civs)
    p = c * (c - 1) // 2
    grupos = _grupos(GRUPOS_1V1, c, p)
    n = len(d.a)
    filas = np.arange(n)
    par = indice_par(d.a, d.b, c)
    columnas_a = _columnas_de_civ(grupos, d.a, d.mapa, d.elo, d.dur)
    columnas_b = _columnas_de_civ(grupos, d.b, d.mapa, d.elo, d.dur)
    columnas_par = [grupos["cruce"][0] + par,
                    grupos["cruce_mapa"][0] + par * FRANJAS + d.mapa,
                    grupos["cruce_dur"][0] + par * FRANJAS + d.dur,
                    grupos["cruce_elo"][0] + par * FRANJAS + d.elo]
    columnas = np.concatenate(columnas_a + columnas_b + columnas_par)
    valores = np.concatenate([np.ones(4 * n), -np.ones(4 * n), np.ones(4 * n)])
    total = sum(tamano for _, tamano in grupos.values())
    matriz = sparse.csr_matrix((valores, (np.tile(filas, 12), columnas)), shape=(n, total))
    return Diseno(matriz, grupos, _offset(d.dif_elo), np.asarray(d.gana_a, dtype=float), c, p)


def diseno_equipos(d: DatosEquipos) -> Diseno:
    c = len(d.civs)
    grupos = _grupos(GRUPOS_EQUIPOS, c, 0)
    coo = d.civ_equipo.tocoo()
    fila, civ = coo.row, coo.col
    columnas = np.concatenate(_columnas_de_civ(grupos, civ, d.mapa[fila], d.elo[fila], d.dur[fila]))
    total = sum(tamano for _, tamano in grupos.values())
    matriz = sparse.csr_matrix((np.tile(coo.data, 4), (np.tile(fila, 4), columnas)),
                               shape=(d.civ_equipo.shape[0], total))
    return Diseno(matriz, grupos, _offset(d.dif_elo), np.asarray(d.gana_1, dtype=float), c, c)


def _precision(dis: Diseno, frenos: dict[str, float]) -> np.ndarray:
    precision = np.empty(dis.matriz.shape[1])
    for nombre, (inicio, tamano) in dis.grupos.items():
        precision[inicio:inicio + tamano] = 1 / frenos[nombre] ** 2
    return precision


def ajustar(dis: Diseno, frenos: dict[str, float], pesos: np.ndarray | None = None,
            inicial: np.ndarray | None = None, centro: np.ndarray | None = None) -> np.ndarray:
    """Los parámetros que minimizan la pérdida logarítmica (con pesos por partida) más la penalización de cada grupo.

    La penalización tira hacia `centro` (0 si no se da); `remuestrear` lo sortea.
    """
    x, y, offset = dis.matriz, dis.y, dis.offset
    pesos = np.ones(len(y)) if pesos is None else np.asarray(pesos, dtype=float)
    precision = _precision(dis, frenos)
    centro = np.zeros(x.shape[1]) if centro is None else centro

    def objetivo(w: np.ndarray) -> tuple[float, np.ndarray]:
        z = x @ w + offset
        desvio = w - centro
        perdida = np.sum(pesos * (np.logaddexp(0, z) - y * z)) + 0.5 * np.sum(precision * desvio * desvio)
        gradiente = x.T @ (pesos * (_sigmoide(z) - y)) + precision * desvio
        return perdida, gradiente

    w0 = np.zeros(x.shape[1]) if inicial is None else inicial
    return minimize(objetivo, w0, jac=True, method="L-BFGS-B", options={"maxiter": 500}).x


def perdida(dis: Diseno, w: np.ndarray, filas: np.ndarray) -> float:
    """La pérdida logarítmica media de esas partidas, con el Elo."""
    z = dis.matriz[filas] @ w + dis.offset[filas]
    return float(np.mean(np.logaddexp(0, z) - dis.y[filas] * z))


def _sigmoide(z: np.ndarray) -> np.ndarray:
    return 0.5 * (1 + np.tanh(0.5 * z))


def eta(dis: Diseno, w: np.ndarray) -> np.ndarray:
    """La chance en escala logit de cada entidad a Elo parejo, por mapa del modelo, franja de Elo y de duración."""
    c = dis.n_civs

    def tramo(nombre: str, forma: tuple[int, ...]) -> np.ndarray:
        inicio, tamano = dis.grupos[nombre]
        return w[inicio:inicio + tamano].reshape(forma)

    civ = tramo("civ", (c,))
    civ_mapa, civ_elo, civ_dur = (tramo(g, (c, FRANJAS)) for g in ("civ_mapa", "civ_elo", "civ_dur"))
    if "cruce" not in dis.grupos:
        return (civ[:, None, None, None] + civ_mapa[:, :, None, None]
                + civ_elo[:, None, :, None] + civ_dur[:, None, None, :])
    i, j = np.triu_indices(c, k=1)
    p = dis.entidades
    cruce = tramo("cruce", (p,))
    cruce_mapa, cruce_dur, cruce_elo = (tramo(g, (p, FRANJAS)) for g in ("cruce_mapa", "cruce_dur", "cruce_elo"))
    return ((civ[i] - civ[j] + cruce)[:, None, None, None]
            + (civ_mapa[i] - civ_mapa[j] + cruce_mapa)[:, :, None, None]
            + (civ_elo[i] - civ_elo[j] + cruce_elo)[:, None, :, None]
            + (civ_dur[i] - civ_dur[j] + cruce_dur)[:, None, None, :])


def pesos_globales(mapa: np.ndarray, elo: np.ndarray, dur: np.ndarray) -> np.ndarray:
    """Cuántas partidas hay en cada combinación de mapa del modelo, franja de Elo y de duración."""
    pesos = np.zeros((FRANJAS, FRANJAS, FRANJAS))
    np.add.at(pesos, (mapa, elo, dur), 1)
    return pesos


def _extender(x: np.ndarray, eje: int, es_mapa: bool) -> np.ndarray:
    """Agrega "todos" (la suma) al principio del eje; en el eje del mapa, sin "otros" como casillero propio."""
    total = x.sum(axis=eje, keepdims=True)
    partes = np.take(x, range(FRANJAS - 1), axis=eje) if es_mapa else x
    return np.concatenate([total, partes], axis=eje)


def _sumas(x: np.ndarray) -> np.ndarray:
    """(E, 5, 5, 5) → (E, 5, 6, 6): mapa (todos, arabia, arena, black_forest, nomad) × Elo y duración (todas, 0..4)."""
    return _extender(_extender(_extender(x, 1, True), 2, False), 3, False)


def casilleros(eta: np.ndarray, pesos: np.ndarray) -> np.ndarray:
    """La chance de cada casillero de salida, promediando con los pesos (parejos si un casillero no tiene partidas)."""
    p = _sigmoide(eta)
    con_pesos = _sumas(p * pesos[None]) / np.maximum(_sumas(np.broadcast_to(pesos[None], p.shape)), 1e-300)
    parejo = _sumas(p) / _sumas(np.ones_like(p))
    sin_partidas = _sumas(np.broadcast_to(pesos[None], p.shape)) == 0
    return np.where(sin_partidas, parejo, con_pesos)


def contar(entidad: np.ndarray, mapa: np.ndarray, elo: np.ndarray, dur: np.ndarray, entidades: int) -> np.ndarray:
    """Cuántas partidas reales hay detrás de cada casillero de salida."""
    cuenta = np.zeros((entidades, FRANJAS, FRANJAS, FRANJAS), dtype=np.int64)
    np.add.at(cuenta, (entidad, mapa, elo, dur), 1)
    return _sumas(cuenta)


FACTORES_FRENO = (0.25, 0.5, 2, 4)


def elegir_frenos(dis: Diseno, entrenamiento: np.ndarray, apartadas: np.ndarray,
                  iniciales: dict[str, float]) -> dict[str, float]:
    """Una pasada por los grupos: para cada uno se queda con el freno que mejor predice las partidas apartadas."""
    frenos = {nombre: iniciales[nombre] for nombre in dis.grupos}
    pesos = np.asarray(entrenamiento, dtype=float)
    w = ajustar(dis, frenos, pesos)
    mejor = perdida(dis, w, apartadas)
    for nombre in dis.grupos:
        actual = frenos[nombre]
        for factor in FACTORES_FRENO:
            prueba = {**frenos, nombre: actual * factor}
            w_prueba = ajustar(dis, prueba, pesos, inicial=w)
            perdida_prueba = perdida(dis, w_prueba, apartadas)
            if perdida_prueba < mejor:
                mejor, frenos, w_nuevo = perdida_prueba, prueba, w_prueba
        if frenos[nombre] != actual:
            w = w_nuevo
    return frenos


def remuestrear(dis: Diseno, frenos: dict[str, float], w: np.ndarray, veces: int = 20,
                semilla: int = 1) -> list[np.ndarray]:
    """Ajustes con las partidas sorteadas con reposición (cada partida pesa las veces que salió) y el centro de cada
    freno sorteado con su mismo desvío. Solo con las partidas, el rango no incluye lo que los frenos encogen hacia el
    50 % y queda angosto (con datos inventados cubría entre el 72 y el 87 %; con el centro sorteado, entre el 87 y el
    93 %).
    """
    rng = np.random.default_rng(semilla)
    n = len(dis.y)
    desvio = 1 / np.sqrt(_precision(dis, frenos))
    muestras = []
    for _ in range(veces):
        pesos = np.bincount(rng.integers(0, n, n), minlength=n)
        muestras.append(ajustar(dis, frenos, pesos, inicial=w, centro=rng.normal(0, desvio)))
    return muestras


Z_90 = 1.645


def rango(p: np.ndarray, muestras: list[np.ndarray]) -> tuple[np.ndarray, np.ndarray]:
    """El rango probable del 90 % alrededor de p: ±1,645 desvíos de las muestras, en escala logit.

    Con 20 muestras, los percentiles 5 y 95 caen casi en la segunda y la anteúltima y el rango cubre en promedio un
    81 %; el desvío aprovecha las 20.
    """
    def logit(x: np.ndarray) -> np.ndarray:
        x = np.clip(x, 1e-6, 1 - 1e-6)
        return np.log(x / (1 - x))

    desvio = logit(np.stack(muestras)).std(axis=0, ddof=1)
    centro = logit(p)
    return _sigmoide(centro - Z_90 * desvio), _sigmoide(centro + Z_90 * desvio)
