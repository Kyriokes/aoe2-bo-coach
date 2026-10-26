"""Estadísticas reales de cruces (D10): baja partidas clasificatorias, ajusta el modelo y escribe
data/matchup_stats.json. Se corre una vez por parche, desde tools/stats con su venv:

    python stats.py bajar [--fuente aoe2guide] [--cache RUTA]
    python stats.py ajustar [--cache RUTA] [--salida RUTA] [--veces 20] [--apartar-dias 14]
    python stats.py informe [--cache RUTA]

La caché (por defecto tools/stats/.cache, fuera de git) junta las fotos que se van bajando sin repetir partidas.
`ajustar` valida el modelo con los últimos días apartados y, si no pasa, no toca la salida.
"""

from __future__ import annotations

import argparse
import json
import sys
from datetime import date, datetime, timezone
from pathlib import Path

import duckdb
import numpy as np

import validacion
from cache import TablaInvalida, sumar
from franjas import LIMITES_ELO, LIMITES_JUEGO_MIN, MAPAS
from fuentes import aoe2guide
from fuentes.aoe2guide import CivDesconocida, FuenteCaida, FuenteInvalida
from lectura import leer_1v1, leer_equipos
from modelo import (FRENOS_INICIALES, ajustar, casilleros, contar, diseno_1v1, diseno_equipos, elegir_frenos, eta,
                    indice_par, pesos_globales, rango, remuestrear)
from salida import a_entradas, escribir

AQUI = Path(__file__).resolve().parent
REPO = AQUI.parents[1]
CACHE_POR_DEFECTO = AQUI / ".cache"
SALIDA_POR_DEFECTO = REPO / "data" / "matchup_stats.json"
INTERVALO = 0.9


def _bajar(args: argparse.Namespace) -> int:
    cache: Path = args.cache
    descargas = cache / "descargas" / "aoe2guide"
    normalizadas = cache / "normalizadas" / "aoe2guide"
    print(f"Bajando de {aoe2guide.URL} ...")
    aoe2guide.bajar(descargas, aoe2guide.URL)
    aoe2guide.normalizar(descargas, normalizadas, aoe2guide.claves_de_civs(REPO))
    nuevas = sumar(cache, normalizadas)
    total = duckdb.connect().execute(
        f"SELECT count(*) FROM '{(cache / 'partidas.parquet').as_posix()}'").fetchone()[0]
    fuente = {"name": aoe2guide.NOMBRE, "url": aoe2guide.URL, "patch_label": aoe2guide.etiqueta_de_parche(descargas)}
    (cache / "fuente.json").write_text(json.dumps(fuente, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"{nuevas} partidas nuevas, {total} en total")
    return 0


def _fecha(segundos: int) -> str:
    return datetime.fromtimestamp(int(segundos), tz=timezone.utc).date().isoformat()


def _resumen_validacion(v: validacion.Validacion, frenos: dict[str, float], w, dis) -> dict:
    perdidas = {"model": round(v.perdida_modelo, 5), "elo_only": round(v.perdida_solo_elo, 5)}
    if v.perdida_crudo is not None:
        perdidas["raw_cell"] = round(v.perdida_crudo, 5)
    resumen = {"log_loss": perdidas, "calibration": [[round(a, 4), round(b, 4)] for a, b in v.calibracion],
               "brakes": {g: f for g, f in frenos.items()}}
    if "elo" in dis.grupos:
        resumen["elo_only_scale"] = round(v.escala_elo, 3)
        resumen["model_elo_scale"] = round(1 + float(w[dis.grupos["elo"][0]]), 3)
    return resumen


def _cuenta_1v1(d) -> np.ndarray:
    c = len(d.civs)
    return contar(indice_par(d.a, d.b, c), d.mapa, d.elo, d.dur, c * (c - 1) // 2)


def _cuenta_equipos(d) -> np.ndarray:
    """Las apariciones de cada civ (cada jugador cuenta) por casillero."""
    coo = d.civ_equipo.tocoo()
    veces = np.abs(coo.data).astype(np.int64)
    fila, civ = np.repeat(coo.row, veces), np.repeat(coo.col, veces)
    return contar(civ, d.mapa[fila], d.elo[fila], d.dur[fila], len(d.civs))


def _entradas_1v1(d, p, desde, hasta) -> dict[str, list[int]]:
    i, j = np.triu_indices(len(d.civs), k=1)
    return a_entradas([f"{d.civs[a]}|{d.civs[b]}" for a, b in zip(i, j)], p, desde, hasta, _cuenta_1v1(d))


def _entradas_equipos(d, p, desde, hasta) -> dict[str, list[int]]:
    return a_entradas(list(d.civs), p, desde, hasta, _cuenta_equipos(d))


def _ajustar(args: argparse.Namespace) -> int:
    cache: Path = args.cache
    archivo_fuente = cache / "fuente.json"
    fuente = json.loads(archivo_fuente.read_text(encoding="utf-8")) if archivo_fuente.exists() else {}

    d1, de = leer_1v1(cache), leer_equipos(cache)
    tablas = {"one_vs_one": (d1, diseno_1v1(d1), _entradas_1v1), "team": (de, diseno_equipos(de), _entradas_equipos)}
    validadas = {}
    for nombre, (d, dis, _) in tablas.items():
        print(f"{nombre}: {len(dis.y)} partidas, {len(d.civs)} civs")
        entrenamiento, apartadas = validacion.particion(d.inicio, args.apartar_dias)
        frenos = elegir_frenos(dis, entrenamiento, apartadas, FRENOS_INICIALES)
        print("  frenos: " + ", ".join(f"{g} {f:g}" for g, f in frenos.items()))
        w = ajustar(dis, frenos, entrenamiento.astype(float))
        crudo = validacion.perdida_crudo_1v1(d, entrenamiento, apartadas) if nombre == "one_vs_one" else None
        # En equipos el modelo ajusta la escala del Elo: "solo el Elo" también, si no le gana sin las civs.
        escala = validacion.escala_elo(dis, entrenamiento) if "elo" in dis.grupos else 1.0
        v = validacion.validar(dis, w, apartadas, crudo, escala)
        print(f"  pérdida en los últimos {args.apartar_dias} días: modelo {v.perdida_modelo:.5f}, "
              f"solo el Elo {v.perdida_solo_elo:.5f}" + (f" (escala {escala:.3f})" if escala != 1.0 else "")
              + (f", % crudo {crudo:.5f}" if crudo is not None else ""))
        if not v.pasa:
            print(f"No pasa la validación ({nombre}): " + "; ".join(v.motivos) + ". No se toca la salida.",
                  file=sys.stderr)
            return 1
        validadas[nombre] = (frenos, w, v)

    entradas = {}
    for nombre, (d, dis, armar) in tablas.items():
        frenos, w_entrenamiento, _ = validadas[nombre]
        w = ajustar(dis, frenos, inicial=w_entrenamiento)
        pesos = pesos_globales(d.mapa, d.elo, d.dur)
        p = casilleros(eta(dis, w), pesos)
        print(f"{nombre}: {args.veces} remuestreos ...")
        muestras = [casilleros(eta(dis, m), pesos) for m in remuestrear(dis, frenos, w, args.veces)]
        desde, hasta = rango(p, muestras)
        entradas[nombre] = armar(d, p, desde, hasta)

    inicios = np.concatenate([d1.inicio, de.inicio])
    cabecera = {
        "schema_version": 1,
        "generated": date.today().isoformat(),
        "source": {"name": fuente.get("name"), "url": fuente.get("url"),
                   "from": _fecha(inicios.min()), "to": _fecha(inicios.max())},
        "patch": {"label": fuente.get("patch_label"), "start": _fecha(d1.inicio_parche),
                  "previous_mixed": d1.previo_mezclado},
        "matches": {"1v1": len(d1.gana_a), "team": len(de.gana_1)},
        "bands": {"elo": list(LIMITES_ELO), "game_minutes": list(LIMITES_JUEGO_MIN), "maps": list(MAPAS)},
        "interval": INTERVALO,
        "validation": {"held_out_days": args.apartar_dias,
                       **{nombre: _resumen_validacion(v, frenos, w, tablas[nombre][1])
                          for nombre, (frenos, w, v) in validadas.items()}},
    }
    escribir(args.salida, cabecera, entradas["one_vs_one"], entradas["team"])
    print(f"Escrito {args.salida}")
    return 0


def _informe(args: argparse.Namespace) -> int:
    d1, de = leer_1v1(args.cache), leer_equipos(args.cache)
    inicios = np.concatenate([d1.inicio, de.inicio])
    print(f"Partidas 1v1: {len(d1.gana_a)}, por equipos: {len(de.gana_1)} "
          f"(del {_fecha(inicios.min())} al {_fecha(inicios.max())})")
    print(f"Parche actual desde el {_fecha(d1.inicio_parche)}; "
          f"{'con' if d1.previo_mezclado else 'sin'} el parche anterior")
    mapas = ["todos", *MAPAS]
    uno, equipos = _cuenta_1v1(d1), _cuenta_equipos(de)
    print(f"1v1, partidas por pareja ({uno.shape[0]} parejas):")
    for m, mapa in enumerate(mapas):
        n = uno[:, m, 0, 0]
        print(f"  {mapa:<13} parejas con partidas {int((n > 0).sum()):>5}   p10 {np.percentile(n, 10):>6.0f}"
              f"   mediana {np.median(n):>6.0f}   p90 {np.percentile(n, 90):>6.0f}")
    print(f"Equipos, apariciones por civ ({equipos.shape[0]} civs):")
    for m, mapa in enumerate(mapas):
        n = equipos[:, m, 0, 0]
        print(f"  {mapa:<13} mínimo {int(n.min()):>7}   mediana {np.median(n):>7.0f}")
    return 0


def _al_menos_dos(texto: str) -> int:
    """El rango sale del desvío de las muestras: con menos de 2 no hay desvío."""
    valor = int(texto)
    if valor < 2:
        raise argparse.ArgumentTypeError("hacen falta al menos 2 remuestreos")
    return valor


def main(argv: list[str] | None = None) -> int:
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8")
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    comandos = parser.add_subparsers(dest="comando", required=True)
    bajar = comandos.add_parser("bajar", help="baja una foto de partidas y la suma a la caché")
    bajar.add_argument("--fuente", choices=["aoe2guide"], default="aoe2guide")
    bajar.add_argument("--cache", type=Path, default=CACHE_POR_DEFECTO)
    ajuste = comandos.add_parser("ajustar", help="ajusta el modelo, lo valida y escribe la salida")
    ajuste.add_argument("--cache", type=Path, default=CACHE_POR_DEFECTO)
    ajuste.add_argument("--salida", type=Path, default=SALIDA_POR_DEFECTO)
    ajuste.add_argument("--veces", type=_al_menos_dos, default=20)
    ajuste.add_argument("--apartar-dias", type=int, default=validacion.DIAS_APARTADOS)
    informe = comandos.add_parser("informe", help="cuántas partidas hay en la caché por mapa, pareja y civ")
    informe.add_argument("--cache", type=Path, default=CACHE_POR_DEFECTO)
    args = parser.parse_args(argv)

    try:
        return {"bajar": _bajar, "ajustar": _ajustar, "informe": _informe}[args.comando](args)
    except (FuenteCaida, FuenteInvalida, CivDesconocida, TablaInvalida) as error:
        print(f"Error: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
