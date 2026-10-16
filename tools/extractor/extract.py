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


# ---------------------------------------------------------------------------------------------------------------
# Bonificaciones de civilización.
#
# En el .dat cada bonificación es una "tecnología" de la civ (tech.civ == id) sin lugar de investigación: se
# investiga sola cuando se cumplen sus requisitos (la edad, o cómo arranca la partida). Además el "árbol de
# tecnologías" de la civ (civ.tech_tree_id) y su bonificación de equipo (civ.team_bonus_id, que también aplica al
# propio jugador) aplican desde el arranque. Cada efecto es una lista de comandos (tipo, a, b, c, d).
# ---------------------------------------------------------------------------------------------------------------

# Variantes del aldeano por tarea (hombre y mujer): las bonificaciones de recolección las tocan por id.
TAREAS = {
    "sheep": (592, 590),
    "hunt": (122, 216),
    "berries": (120, 354),
    "fish": (56, 57),
    "farm": (259, 214),
    "wood": (123, 218),
    "gold": (579, 581, 2333, 2334),
    "stone": (124, 220),
}
CONSTRUCTORES = (118, 212)
CLASE_ALDEANO = 4
ID_ALDEANO = 83
ID_CASA = 70

# Requisitos que se cumplen en una partida estándar (arranca en Oscura con centro urbano): "Town Center Spawn",
# "Shadow TC Annex", "Dark Age" y "Shadow Church -- Age One". Lo que pide otra cosa (arranque nómada, un castillo
# construido, otra tecnología) no se cuenta.
REQUISITOS_DE_INICIO = {639, 307, 104, 107}
NIVEL_POR_TECH_DE_EDAD = {101: 1, 102: 2, 103: 3}

ATTR_POBLACION = 21     # lo que guarda el edificio: en casas y centros urbanos, la población que da
ATTR_TRABAJO = 13       # velocidad de trabajo: recolección, construcción o producción del edificio
ATTR_COSTO_TODO = 100
ATTR_TIEMPO = 101
ATTR_COSTO = {103: "food", 104: "wood", 105: "stone", 106: "gold"}
RECURSO_INICIAL = {91: "food", 92: "wood", 93: "stone", 94: "gold"}
RECURSO_COSTO_TECHS = 85  # multiplicador del costo de todas las tecnologías (chinos)

# Nombre interno de la civ -> clave en las BO (como la comunidad las nombra en inglés).
CLAVES_DE_CIV = {
    "British": "britons", "French": "franks", "Byzantine": "byzantines", "Indians": "hindustanis",
    "Slavs": "slavs", "Incas": "incas",
}


def clave_de_civ(nombre: str) -> str:
    return CLAVES_DE_CIV.get(nombre, re.sub(r"[^a-z0-9]+", "_", nombre.lower()).strip("_"))


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

    # ---- Bonificaciones de civilización ----

    def comandos_por_nivel(self, civ_id: int) -> list[list]:
        """Comandos de efecto que aplica la civ en cada edad (0 = Oscura ... 3 = Imperial), acumulados."""
        civ = self.dat.civs[civ_id]
        por_nivel: list[list] = [[], [], [], []]

        def agregar(effect_id: int, nivel: int):
            if 0 <= effect_id < len(self.dat.effects):
                por_nivel[nivel].extend(c for c in self.dat.effects[effect_id].effect_commands if c.type != 102)

        agregar(civ.tech_tree_id, 0)
        agregar(civ.team_bonus_id, 0)
        for tech in self.dat.techs:
            if tech.civ != civ_id or any(l.location_id >= 0 for l in tech.research_locations):
                continue
            requisitos = [r for r in tech.required_techs if r >= 0]
            if any(r not in REQUISITOS_DE_INICIO and r not in NIVEL_POR_TECH_DE_EDAD for r in requisitos):
                continue
            agregar(tech.effect_id, max((NIVEL_POR_TECH_DE_EDAD.get(r, 0) for r in requisitos), default=0))
        return [sum(por_nivel[: n + 1], []) for n in range(4)]

    def mejoras_de_edad(self) -> dict[int, dict[int, int]]:
        """Por edad (1 a 3): unidad -> unidad que la reemplaza al subir (el centro urbano 109 pasa a 71, ...)."""
        if not hasattr(self, "_mejoras"):
            self._mejoras = {}
            for tech_id, nivel in NIVEL_POR_TECH_DE_EDAD.items():
                efecto = self.dat.techs[tech_id].effect_id
                self._mejoras[nivel] = {
                    c.a: c.b for c in self.dat.effects[efecto].effect_commands if c.type == 3
                } if 0 <= efecto < len(self.dat.effects) else {}
        return self._mejoras

    def variante(self, uid: int, nivel: int) -> int:
        """La unidad que representa a uid en esa edad (los edificios cambian de unidad al subir de edad)."""
        for n in range(1, nivel + 1):
            uid = self.mejoras_de_edad()[n].get(uid, uid)
        return uid

    @staticmethod
    def aplica(cmd, ids, clase) -> bool:
        """El comando toca a esta unidad (por id o por clase); los que no nombran ni unidad ni clase se ignoran."""
        if cmd.a == -1 and cmd.b == -1:
            return False
        return (cmd.a == -1 or cmd.a in ids) and (cmd.b == -1 or cmd.b == clase)

    def atributo(self, comandos, ids, clase, attr: int, base: float, solo_subas: bool = False) -> float:
        """
        Valor del atributo después de los comandos. Con solo_subas se ignoran los multiplicadores menores a 1: en
        la recolección vienen con otro efecto que los compensa (mayas, tártaros, godos: el recurso rinde más).
        """
        valor = base
        for c in comandos:
            if c.c != attr or not self.aplica(c, ids, clase):
                continue
            if c.type == 5:
                if not (solo_subas and c.d < 1):
                    valor *= c.d
            elif c.type == 4:
                valor += c.d
            elif c.type == 0:
                valor = c.d
        return valor

    def costo_con_bonos(self, comandos, ids, clase, base: dict[str, int]) -> dict[str, int]:
        resultado = {}
        for recurso in RECURSOS.values():
            attr = next(a for a, r in ATTR_COSTO.items() if r == recurso)
            valor = float(base.get(recurso, 0))
            for c in comandos:
                if not self.aplica(c, ids, clase) or c.c not in (ATTR_COSTO_TODO, attr):
                    continue
                if c.type == 5:
                    valor *= c.d
                elif c.type == 4 and c.c == attr:
                    valor += c.d
                elif c.type == 0 and c.c == attr:
                    valor = c.d
            if round(valor) > 0:
                resultado[recurso] = int(round(valor))
        return resultado

    def tech_con_bonos(self, comandos, tid: int, base_costo: dict[str, int], base_tiempo: float):
        costo_tech = {r: float(base_costo.get(r, 0)) for r in RECURSOS.values()}
        tiempo = float(base_tiempo)
        factor = 1.0
        for c in comandos:
            if c.type == 1 and c.a == RECURSO_COSTO_TECHS and c.b == 0:
                factor = c.d
            if c.a != tid:
                continue
            if c.type == 101 and c.b in RECURSOS:
                recurso = RECURSOS[c.b]
                costo_tech[recurso] = {0: c.d, 1: costo_tech[recurso] + c.d, 2: costo_tech[recurso] * c.d}.get(c.c, costo_tech[recurso])
            elif c.type == 103:
                tiempo = {0: c.d, 1: tiempo + c.d, 2: tiempo * c.d}.get(c.c, tiempo)
        return ({r: int(round(v * factor)) for r, v in costo_tech.items() if round(v * factor) > 0}, round(tiempo, 2))

    def civ(self, civ_id: int, entidades: dict) -> dict:
        niveles = self.comandos_por_nivel(civ_id)
        base_tasas = {fuente: self.unidades[ids[0]].bird.work_rate for fuente, ids in TAREAS.items()}

        def por_edad(calcular):
            """calcular(comandos de esa edad, nivel) para cada edad."""
            return [calcular(niveles[n], n) for n in range(4)]

        datos: dict = {"dat_name": self.dat.civs[civ_id].name}

        # Recolección y construcción: multiplicador sobre la tasa base de cada tarea (se evalúa sobre el aldeano
        # hombre: los efectos tocan por separado a hombres y mujeres con el mismo valor).
        recoleccion = {}
        for fuente, ids in TAREAS.items():
            valores = por_edad(lambda cmds, n, uid=ids[0], fuente=fuente: round(
                self.atributo(cmds, {uid}, CLASE_ALDEANO, ATTR_TRABAJO, base_tasas[fuente], solo_subas=True) / base_tasas[fuente], 3))
            if any(v != 1 for v in valores):
                recoleccion[fuente] = valores
        if recoleccion:
            datos["gather"] = recoleccion
        base_construccion = self.unidades[CONSTRUCTORES[0]].bird.work_rate
        construccion = por_edad(lambda cmds, n: round(
            self.atributo(cmds, {CONSTRUCTORES[0]}, CLASE_ALDEANO, ATTR_TRABAJO, base_construccion, solo_subas=True) / base_construccion, 3))
        if any(v != 1 for v in construccion):
            datos["build_rate"] = construccion

        # Costos, tiempos y velocidad de producción de cada entidad, por edad (solo lo que cambia). Cada edad se
        # evalúa sobre la unidad que la representa en esa edad (el centro urbano de Feudal es otra unidad).
        costos, tiempos, produccion, poblacion = {}, {}, {}, {}
        for clave, (tipo, dat_id) in ENTIDADES.items():
            base = entidades[clave]
            if tipo == "tech":
                valores = por_edad(lambda cmds, n, dat_id=dat_id, base=base: self.tech_con_bonos(cmds, dat_id, base["cost"], base["time"]))
                if any(v[0] != base["cost"] for v in valores):
                    costos[clave] = [v[0] for v in valores]
                if any(v[1] != base["time"] for v in valores):
                    tiempos[clave] = [v[1] for v in valores]
                continue

            def ids_y_clase(n, uid=dat_id):
                variante = self.variante(uid, n)
                return {uid, variante}, self.unidades[variante].class_

            valores = por_edad(lambda cmds, n, base=base: self.costo_con_bonos(cmds, *ids_y_clase(n), base["cost"]))
            if any(v != base["cost"] for v in valores):
                costos[clave] = valores
            valores = por_edad(lambda cmds, n, base=base: round(self.atributo(cmds, *ids_y_clase(n), ATTR_TIEMPO, base["time"]), 2))
            if any(v != base["time"] for v in valores):
                tiempos[clave] = valores
            if tipo == "building":
                final = self.edificio_final(self.unidades[dat_id]).id

                def trabajo(cmds, n, final=final):
                    variante = self.variante(final, n)
                    b = (self.unidades[variante].bird.work_rate if self.unidades[variante].bird else 0) or 1.0
                    return round(self.atributo(cmds, {variante}, self.unidades[variante].class_, ATTR_TRABAJO, b) / b, 3)

                valores = por_edad(trabajo)
                if any(v != 1 for v in valores):
                    produccion[clave] = valores
                if "pop_bonus" in base:
                    valores = por_edad(lambda cmds, n, final=final, base=base: int(self.atributo(
                        cmds, {self.variante(final, n)}, self.unidades[self.variante(final, n)].class_, ATTR_POBLACION, base["pop_bonus"])))
                    if any(v != base["pop_bonus"] for v in valores):
                        poblacion[clave] = valores
        for nombre, valores in (("costs", costos), ("times", tiempos), ("work_rate", produccion), ("pop_bonus", poblacion)):
            if valores:
                datos[nombre] = valores

        # Arranque de una partida estándar: recursos, aldeanos extra y casas.
        inicio = {}
        for c in niveles[0]:
            if c.type == 1 and c.a in RECURSO_INICIAL and c.b == 1:
                inicio[RECURSO_INICIAL[c.a]] = inicio.get(RECURSO_INICIAL[c.a], 0) + int(c.d)
            elif c.type == 7 and c.a == ID_ALDEANO:
                inicio["villagers"] = inicio.get("villagers", 0) + int(c.c)
            elif c.type == 0 and c.a == ID_CASA and c.c == 0 and c.d < 0:
                inicio["no_houses"] = True  # hunos: las casas no existen y la población no tiene tope de casas
        if inicio:
            datos["start"] = inicio
        return datos

    def civs(self, entidades: dict) -> dict:
        civs = {}
        for civ_id, civ in enumerate(self.dat.civs):
            if civ_id == CIV_BASE:
                continue
            civs[clave_de_civ(civ.name)] = self.civ(civ_id, entidades)
        return civs

    def exportar(self) -> dict:
        entidades = {clave: self.entidad(clave, tipo, i) for clave, (tipo, i) in ENTIDADES.items()}
        return {
            "dat_version": self.dat.version,
            "entities": entidades,
            "gather_rates": self.tasas(),
            "civs": self.civs(entidades),
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
    print(f"{len(datos['civs'])} civilizaciones:")
    for clave, civ in datos["civs"].items():
        partes = [f"{fuente} {v}" for fuente, v in civ.get("gather", {}).items()]
        if "build_rate" in civ:
            partes.append(f"construcción {civ['build_rate']}")
        if "start" in civ:
            partes.append(f"arranque {civ['start']}")
        for nombre in ("costs", "times", "work_rate", "pop_bonus"):
            if nombre in civ:
                partes.append(f"{nombre}: {', '.join(civ[nombre])}")
        print(f"  {clave:14} {' · '.join(partes)}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
