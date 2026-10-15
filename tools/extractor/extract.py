"""Exporta los datos del juego (.dat de AoE2:DE) a data/game_data/<version>.json.

Se corre una vez por parche:

    python extract.py [--dat RUTA_AL_DAT] [--out RUTA_DE_SALIDA]

Todo se mapea por ID del .dat, nunca por nombre interno (los nombres están
desfasados: el tech 101 "Middle Age" es el paso a Feudal).
"""

from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path

from genieutils.datfile import DatFile

DAT_POR_DEFECTO = Path(r"H:\Steam\steamapps\common\AoE2DE\resources\_common\dat\empires2_x2_p1.dat")
REPO = Path(__file__).resolve().parents[2]

RECURSOS = {0: "food", 1: "wood", 2: "stone", 3: "gold"}
RECURSO_POBLACION = 4
EDADES = {104: "dark", 101: "feudal", 102: "castle", 103: "imperial"}
NIVEL_EDAD = {"dark": 0, "feudal": 1, "castle": 2, "imperial": 3}
ID_CONSTRUCTOR = 118  # "train location" de los edificios: los construye un aldeano
CIV_BASE = 0  # Gaia: tiene todas las unidades sin bonificaciones de civ

# Entidades que se exportan: clave estable -> (tipo, id en el .dat)
ENTIDADES: dict[str, tuple[str, int]] = {
    # Unidades
    "villager": ("unit", 83),
    "militia": ("unit", 74),
    "spearman": ("unit", 93),
    "archer": ("unit", 4),
    "skirmisher": ("unit", 7),
    "scout_cavalry": ("unit", 448),
    "knight": ("unit", 38),
    "monk": ("unit", 125),
    "fishing_ship": ("unit", 13),
    "trade_cart": ("unit", 128),
    "mangonel": ("unit", 280),
    # Edificios
    "town_center": ("building", 621),
    "house": ("building", 70),
    "mill": ("building", 68),
    "lumber_camp": ("building", 562),
    "mining_camp": ("building", 584),
    "farm": ("building", 50),
    "dock": ("building", 45),
    "barracks": ("building", 12),
    "archery_range": ("building", 87),
    "stable": ("building", 101),
    "blacksmith": ("building", 103),
    "market": ("building", 84),
    "monastery": ("building", 104),
    "university": ("building", 209),
    "siege_workshop": ("building", 49),
    "palisade_wall": ("building", 72),
    "stone_wall": ("building", 117),
    "outpost": ("building", 598),
    "watch_tower": ("building", 79),
    "castle": ("building", 82),
    # Tecnologías
    "loom": ("tech", 22),
    "town_watch": ("tech", 8),
    "wheelbarrow": ("tech", 213),
    "hand_cart": ("tech", 249),
    "double_bit_axe": ("tech", 202),
    "bow_saw": ("tech", 203),
    "horse_collar": ("tech", 14),
    "heavy_plow": ("tech", 13),
    "gold_mining": ("tech", 55),
    "gold_shaft_mining": ("tech", 182),
    "stone_mining": ("tech", 278),
    "stone_shaft_mining": ("tech", 279),
    "feudal_age": ("tech", 101),
    "castle_age": ("tech", 102),
    "imperial_age": ("tech", 103),
    "man_at_arms": ("tech", 222),
    "crossbowman": ("tech", 100),
    "light_cavalry": ("tech", 254),
    "fletching": ("tech", 199),
    "forging": ("tech", 67),
    "scale_mail_armor": ("tech", 74),
    "scale_barding_armor": ("tech", 81),
    "padded_archer_armor": ("tech", 211),
    "bloodlines": ("tech", 435),
    "husbandry": ("tech", 39),
    "coinage": ("tech", 23),
}

# Tasa de recolección base: variante del aldeano que hace cada tarea -> work_rate del .dat.
# Son tasas "de trabajo" sin contar caminatas; la tasa efectiva se aprende en la partida.
VARIANTES_RECOLECCION = {
    "sheep": 592,    # VMSHE pastor
    "hunt": 122,     # VMHUN cazador
    "berries": 120,  # VMFOR recolector
    "fish": 56,      # VMFIS pescador
    "farm": 259,     # VMFAR granjero
    "wood": 123,     # VMLUM leñador
    "gold": 579,     # VMGLD minero de oro
    "stone": 124,    # VMMIN minero de piedra
}


def costo(resource_costs) -> dict[str, int]:
    """Solo los costos que se descuentan (flag 1) y son recursos reales."""
    return {
        RECURSOS[c.type]: c.amount
        for c in resource_costs
        if c.flag == 1 and c.type in RECURSOS and c.amount > 0
    }


class Extractor:
    def __init__(self, dat: DatFile):
        self.dat = dat
        self.unidades = dat.civs[CIV_BASE].units
        self.clave_por_unidad = self._indexar_claves()
        self._edad_tech: dict[int, int] = {}
        self._habilitadores = self._indexar_habilitadores()

    def _indexar_claves(self) -> dict[int, str]:
        """id de unidad -> clave, incluyendo las variantes de un mismo edificio.

        Ej.: el TC que se construye (621) es una base que se "apila" en el TC real (109),
        y 109 se reemplaza por 71/141/142 al subir de edad; todas son "town_center".
        """
        claves = {i: k for k, (t, i) in ENTIDADES.items() if t != "tech"}
        for uid, clave in list(claves.items()):
            edificio = self.unidades[uid].building
            if edificio is not None and edificio.stack_unit_id >= 0:
                claves.setdefault(edificio.stack_unit_id, clave)
        for u in self.unidades:
            if u is not None and u.building is not None and u.building.head_unit in claves:
                claves.setdefault(u.id, claves[u.building.head_unit])
        return claves

    def edificio_final(self, unidad):
        """El edificio terminado: si es una base que se apila en otro, devuelve ese otro."""
        if unidad.building is not None and unidad.building.stack_unit_id >= 0:
            return self.unidades[unidad.building.stack_unit_id]
        return unidad

    def _indexar_habilitadores(self) -> dict[int, list[int]]:
        """unidad -> techs genéricas (no de una civ) que la habilitan."""
        indice: dict[int, list[int]] = {}
        for tid, tech in enumerate(self.dat.techs):
            if tech.civ != -1 or not (0 <= tech.effect_id < len(self.dat.effects)):
                continue
            for cmd in self.dat.effects[tech.effect_id].effect_commands:
                if cmd.type == 2 and cmd.b == 1:  # habilitar unidad
                    indice.setdefault(cmd.a, []).append(tid)
        return indice

    def nivel_edad_requerida(self, tid: int, visitando: frozenset[int] = frozenset()) -> int:
        """Edad mínima para investigar la tech: la mayor edad en su cadena de requisitos."""
        if tid in self._edad_tech:
            return self._edad_tech[tid]
        nivel = 0
        for req in self.dat.techs[tid].required_techs:
            if req < 0 or req in visitando:
                continue
            if req in EDADES:
                nivel = max(nivel, NIVEL_EDAD[EDADES[req]])
            else:
                nivel = max(nivel, self.nivel_edad_requerida(req, visitando | {tid}))
        self._edad_tech[tid] = nivel
        return nivel

    def edad_de_unidad(self, uid: int) -> str:
        habilitadores = self._habilitadores.get(uid)
        if not habilitadores:
            return "dark"  # disponible desde el inicio
        nivel = min(self.nivel_edad_requerida(t) for t in habilitadores)
        return _edad_por_nivel(nivel)

    def lugar(self, uid: int) -> str:
        return self.clave_por_unidad.get(uid, f"dat:{uid}")

    def entidad(self, clave: str, tipo: str, dat_id: int) -> dict:
        if tipo == "tech":
            tech = self.dat.techs[dat_id]
            loc = tech.research_locations[0]
            return {
                "kind": tipo,
                "dat_id": dat_id,
                "dat_name": tech.name,
                "cost": costo(tech.resource_costs),
                "time": loc.research_time,
                "made_in": self.lugar(loc.location_id),
                "age": _edad_por_nivel(self.nivel_edad_requerida(dat_id)),
            }

        unidad = self.unidades[dat_id]
        if unidad is None:
            raise ValueError(f"{clave}: la unidad {dat_id} no existe en el .dat")
        entrenamiento = unidad.creatable.train_locations[0]
        datos = {
            "kind": tipo,
            "dat_id": dat_id,
            "dat_name": unidad.name,
            "cost": costo(unidad.creatable.resource_costs),
            "time": entrenamiento.train_time,
            "age": self.edad_de_unidad(dat_id),
        }
        if tipo == "unit":
            datos["made_in"] = self.lugar(entrenamiento.unit_id)
            datos["pop"] = sum(
                int(c.amount) for c in unidad.creatable.resource_costs if c.type == RECURSO_POBLACION
            )
        else:
            if entrenamiento.unit_id != ID_CONSTRUCTOR:
                raise ValueError(f"{clave}: el edificio {dat_id} no lo construye un aldeano")
            bono = sum(
                s.amount
                for s in self.edificio_final(unidad).resource_storages
                if s.type == RECURSO_POBLACION and s.amount > 0
            )
            if bono:
                datos["pop_bonus"] = int(bono)
        return datos

    def tasas(self) -> dict[str, float]:
        return {
            recurso: round(self.unidades[uid].bird.work_rate, 3)
            for recurso, uid in VARIANTES_RECOLECCION.items()
        }

    def exportar(self) -> dict:
        entidades = {clave: self.entidad(clave, tipo, i) for clave, (tipo, i) in ENTIDADES.items()}
        return {
            "dat_version": self.dat.version,
            "entities": entidades,
            "civ_overrides": {},
            "gather_rates": self.tasas(),
        }


def _edad_por_nivel(nivel: int) -> str:
    return next(nombre for nombre, n in NIVEL_EDAD.items() if n == nivel)


def nombre_de_archivo(version: str) -> str:
    """'VER 8.9' -> 'v8.9.json'."""
    m = re.search(r"(\d+(?:\.\d+)*)", version)
    if not m:
        raise ValueError(f"No se pudo leer el número de versión de {version!r}")
    return f"v{m.group(1)}.json"


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--dat", type=Path, default=DAT_POR_DEFECTO, help="ruta a empires2_x2_p1.dat")
    parser.add_argument("--out", type=Path, help="archivo de salida (por defecto data/game_data/v<versión>.json)")
    args = parser.parse_args()

    print(f"Leyendo {args.dat} (tarda ~30 s)...")
    dat = DatFile.parse(str(args.dat))
    datos = Extractor(dat).exportar()

    salida = args.out or REPO / "data" / "game_data" / nombre_de_archivo(dat.version)
    salida.parent.mkdir(parents=True, exist_ok=True)
    salida.write_text(json.dumps(datos, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")

    print(f"{len(datos['entities'])} entidades exportadas a {salida}")
    for clave, e in datos["entities"].items():
        print(f"  {clave:22} {e['kind']:8} {e['dat_id']:>5} {e['dat_name']:28} {e['age']:8} {e['time']:>4}s {e['cost']}")
    print(f"  tasas: {datos['gather_rates']}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
