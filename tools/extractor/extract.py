"""Exporta los datos del juego (.dat de AoE2:DE) a data/game_data/<version>.json, y las frases del log de
eventos (textos y árboles de tecnologías del juego) a data/events.json, y las mismas frases con el juego en
castellano de España y de Latinoamérica a data/events.es.json y data/events.mx.json.

Se corre una vez por parche:

    python extract.py [--dat RUTA_AL_DAT] [--out RUTA_DE_SALIDA] [--eventos RUTA_DE_EVENTOS]

Todo se mapea por ID del .dat, nunca por nombre interno (los nombres están
desfasados: el tech 101 "Middle Age" es el paso a Feudal).
"""

from __future__ import annotations

import argparse
from collections import Counter
import json
import re
import sys
import unicodedata
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

# Edificios que valen por varios a la vez (el Settlement de los incas): para confirmar los pasos de la BO, para los
# requisitos de edad y para lo que se produce en ellos.
CUENTA_COMO: dict[str, list[str]] = {
    "settlement": ["lumber_camp", "mining_camp", "mill", "house"],
    "mule_cart": ["lumber_camp", "mining_camp"],
}

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
    "eagle_scout": ("unit", 751),
    "cavalry_archer": ("unit", 39),
    "hand_cannoneer": ("unit", 5),
    "camel_rider": ("unit", 329),
    "battle_elephant": ("unit", 1132),
    "steppe_lancer": ("unit", 1370),
    "galley": ("unit", 539),
    "fire_galley": ("unit", 1103),
    "demolition_raft": ("unit", 1104),
    "transport_ship": ("unit", 545),
    "battering_ram": ("unit", 1258),  # el 35 es el ariete viejo; los árboles de las civs usan el 1258
    "scorpion": ("unit", 279),
    # Edificios
    "town_center": ("building", 621),
    "house": ("building", 70),
    "mill": ("building", 68),
    "lumber_camp": ("building", 562),
    "mining_camp": ("building", 584),
    "settlement": ("building", 2556),  # incas: campamento maderero, minero, molino y casa en uno
    "mule_cart": ("building", 1808),  # armenios y georgianos: lo hace un aldeano y vale por los campamentos
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
    "two_man_saw": ("tech", 221),
    "horse_collar": ("tech", 14),
    "heavy_plow": ("tech", 13),
    "crop_rotation": ("tech", 12),
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

# D12: comida de cada objeto para la comida finita del modo asistido (los ciervos no cuentan). La granja no guarda su
# comida en la unidad sino en un recurso global de la civ (36), que Gaia trae en 0: se lee de la primera civ real.
COMIDA_POR_OBJETO = {"sheep": 594, "hunt": 48, "berries": 59}  # oveja, jabalí, arbusto de bayas
RECURSO_COMIDA_GRANJA = 36
CIV_CON_RECURSOS = 1
# Lo que rinde la comida de cada fuente por civ: cada tarea de recolección multiplica lo que junta por un recurso de
# la civ (el `resource_multiplier` de la tarea en el .dat; ovejas 216 = "Sheep +50% food" de los tártaros, caza 268 =
# "Hunting bonuses" de los godos). Las bayas usan el 219, que hoy se mueve siempre junto con el 190 (la comida de las
# demás tareas): si un parche los separa, el extractor avisa.
RENDIMIENTO_COMIDA = {"sheep": 216, "hunt": 268, "berries": 219}
RECURSO_RENDIMIENTO_COMIDA = 190
RECURSO_MEJORAS_GRANJA = 69  # multiplica lo que suman la collera, el arado pesado y la rotación (sicilianos ×2,25)
RECURSO_GRANJA_EXTRA = 237  # comida de más por granja, como fracción del recurso 36 (folwark de los polacos: 10 %)


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
ATTR_CARGA = 14         # cuánto lleva un aldeano por viaje
ATTR_VELOCIDAD = 5      # velocidad de movimiento
ATTR_UNIDAD_MUERTA = 57  # en qué se convierte la unidad al morir
ID_CENTRO_URBANO = 109
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


# ---------------------------------------------------------------------------------------------------------------
# Eventos del log (data/events.json).
#
# El juego arma cada mensaje con una plantilla de sus textos (resources/en/strings/key-value) y el nombre que
# muestra la entidad (language_dll_name del .dat, que puede cambiar según la civ). Qué unidades, edificios y
# tecnologías existen en una partida sale de los árboles de cada civ (resources/_common/dat/CivTechTrees).
# ---------------------------------------------------------------------------------------------------------------

ARCHIVOS_DE_TEXTO = ("key-value-strings-utf8.txt", "key-value-paphos-strings-utf8.txt")

# Tipo de entidad -> (id del texto con la plantilla, sufijo del evento).
PLANTILLAS = {
    "unit": (37159, "created"),         # "--%s Created--"
    "building": (37158, "built"),       # "--%s Built--"
    "tech": (37157, "researched"),      # "--%s Research Complete--"
}

# Otros mensajes del log: id del texto -> id del evento.
AVISOS = {
    37162: "farm_exhausted",            # "--Farm Exhausted--"
    37163: "fish_trap_expired",
    37160: "under_attack",              # "--Warning: You are being attacked by Player %s!!!--"
    37165: "attacked_by_animals",
    37161: "sheep_found",
    37155: "cow_found",
    37156: "llama_found",
    37167: "turkey_found",
    37168: "goat_found",
    37164: "relic_picked_up",
}

# Avisos largos que el log parte en dos renglones antes de una parte variable: también se reconoce el primero solo.
# Ej. (verificado en la grabación 205040): "--Warning: You are being attacked by" / "Player 2 Timur Qutlugh!!!--".
# En castellano ("--Atención: ¡estás siendo atacado por el jugador %s!--") el corte es una suposición: falta ver un
# log real.
PRIMER_RENGLON = {37160: (" Player %s", " el jugador %s")}

# Idiomas del juego además del inglés: archivo de eventos -> carpetas de textos (resources/<carpeta>). Castellano de
# España (es) y de Latinoamérica (mx): las plantillas son iguales pero muchos nombres cambian ("Caballero" y
# "Jinete"), así que el archivo junta los dos.
IDIOMAS = {"events.es.json": ("es",), "events.mx.json": ("mx",)}

# Nombre de cada civ como lo muestra el juego (la pantalla de carga): el texto 10270 + número de civ en el .dat
# ("Britons" es el 10271). Verificado para las 62 civs en inglés y en los dos castellanos.
TEXTO_NOMBRE_DE_CIV = 10270
IDIOMAS_DE_NOMBRES = ("es", "mx")

TIPO_DE_NODO = {"Unit": "unit", "Building": "building", "Tech": "tech"}

# Frases verificadas en una partida real (2026-10-15); el resto sigue las mismas plantillas.
VERIFICADAS = (
    "Villager/Fishing Ship/Galley/Man-at-Arms Created, House/Mill/Lumber Camp/Mining Camp/Dock/Barracks/"
    "Archery Range/Blacksmith/Stable/Market Built y Feudal Age/Horse Collar/Double-Bit Axe/Scale Mail Armor "
    "Research Complete"
)


def cargar_textos(carpeta: Path) -> dict[int, str]:
    """Textos en inglés del juego: id -> texto. Los de las Crónicas (paphos) solo completan lo que falta."""
    textos: dict[int, str] = {}
    for nombre in ARCHIVOS_DE_TEXTO:
        for renglon in (carpeta / nombre).read_text(encoding="utf-8").splitlines():
            m = re.match(r'^(\d+)\s+"(.*)"\s*$', renglon)
            if m:
                textos.setdefault(int(m.group(1)), m.group(2))
    return textos


def cargar_disponibles(carpeta: Path) -> dict[str, set[int]]:
    """Unidades, edificios y techs que alguna civ puede hacer: los árboles marcan lo que falta como NotAvailable.

    Las mejoras de unidad ("Man-at-Arms") y de edificio ("Guard Tower") son a la vez una unidad y la tech que la
    habilita (Trigger Tech ID): se investigan.
    """
    ids: dict[str, set[int]] = {tipo: set() for tipo in PLANTILLAS}
    for archivo in sorted(carpeta.glob("*.json")):
        arbol = json.loads(archivo.read_text(encoding="utf-8"))
        for nodos in arbol.values():
            if not isinstance(nodos, list):
                continue
            for nodo in nodos:
                tipo = TIPO_DE_NODO.get(nodo.get("Use Type"))
                if tipo is None or nodo.get("Node Status") == "NotAvailable":
                    continue
                ids[tipo].add(nodo["Node ID"])
                es_mejora = nodo.get("Node Type") == "UnitUpgrade" or nodo.get("Building upgraded from ID", -1) >= 0
                if es_mejora and nodo.get("Trigger Tech ID", -1) >= 0:
                    ids["tech"].add(nodo["Trigger Tech ID"])
    return ids


def cargar_no_disponibles(carpeta: Path) -> dict[str, set[tuple[str, int]]]:
    """Lo que cada civ no tiene según su árbol, por el civ_id del árbol ("AZTECS"): pares (tipo, id del .dat) con Node
    Status NotAvailable, o que aparecen en el árbol de otra civ y en el suyo no (el carro de mulas solo está en los
    árboles de armenios y georgianos). Las mejoras de unidad también se marcan por su tech (Trigger Tech ID). Las
    unidades de un edificio que la civ no tiene a veces no aparecen: eso lo deduce el Core por el edificio."""
    faltan: dict[str, set[tuple[str, int]]] = {}
    tiene: dict[str, set[tuple[str, int]]] = {}
    for archivo in sorted(carpeta.glob("*.json")):
        arbol = json.loads(archivo.read_text(encoding="utf-8"))
        civ_id = str(arbol.get("civ_id", archivo.stem)).upper()
        civ_faltan = faltan.setdefault(civ_id, set())
        civ_tiene = tiene.setdefault(civ_id, set())
        for nodos in arbol.values():
            if not isinstance(nodos, list):
                continue
            for nodo in nodos:
                tipo = TIPO_DE_NODO.get(nodo.get("Use Type"))
                if tipo is None:
                    continue
                pares = {(tipo, nodo["Node ID"])}
                if nodo.get("Trigger Tech ID", -1) >= 0:
                    pares.add(("tech", nodo["Trigger Tech ID"]))
                (civ_faltan if nodo.get("Node Status") == "NotAvailable" else civ_tiene).update(pares)
    en_algun_arbol = set().union(*tiene.values())
    for civ_id, civ_faltan in faltan.items():
        civ_faltan |= en_algun_arbol - tiene[civ_id]
    return faltan


def no_disponibles_por_civ(faltan: dict[str, set[tuple[str, int]]], civs: dict) -> dict[str, list[str]]:
    """Clave de civ -> claves de game_data que su árbol marca como no disponibles. El árbol se identifica por la clave o
    el nombre en inglés de la civ, sin la "s" del plural ("MAYANS" es "mayan"), o por los nombres viejos del .dat
    ("INDIANS" son los hindustanis)."""
    def plano(texto: str) -> str:
        return re.sub(r"[^a-z]", "", texto.lower()).rstrip("s")

    resultado = {}
    for clave, civ in civs.items():
        nombres = {plano(clave), plano(civ.get("dat_name", ""))} | {plano(n) for n in civ.get("names", {}).values()}
        arbol = next((ids for civ_id, ids in faltan.items()
                      if plano(civ_id) in nombres or clave_de_civ(civ_id.title()) == clave), None)
        if arbol is None:
            continue
        resultado[clave] = sorted(k for k, (tipo, dat_id) in ENTIDADES.items() if (tipo, dat_id) in arbol)
    return resultado


def cargar_requisitos(carpeta: Path) -> dict[str, str]:
    """Clave de edificio -> el edificio que pide antes (la galería y el establo, el cuartel; el taller de asedio, la
    herrería; el mercado y la granja, el molino), según los árboles: un nodo de edificio cuelga de otro que lo habilita
    ("Link ID" con "Link Node Type" BuildingTech; las torres y murallas cuelgan de su versión anterior, que no es un
    requisito). El más común entre las civs: a los incas la granja les cuelga del asentamiento, que vale por molino."""
    claves = {dat_id: clave for clave, (tipo, dat_id) in ENTIDADES.items() if tipo == "building"}
    votos: dict[str, Counter] = {}
    for archivo in sorted(carpeta.glob("*.json")):
        for nodo in json.loads(archivo.read_text(encoding="utf-8")).get("civ_techs_buildings", []):
            if nodo.get("Link Node Type") != "BuildingTech" or nodo.get("Node Status") == "NotAvailable":
                continue
            clave, requisito = claves.get(nodo.get("Node ID")), claves.get(nodo.get("Link ID", -1))
            if clave and requisito and clave != requisito:
                votos.setdefault(clave, Counter())[requisito] += 1
    return {clave: c.most_common(1)[0][0] for clave, c in sorted(votos.items())}


def cargar_sin_requisitos(carpeta: Path) -> dict[str, set[tuple[str, int]]]:
    """Por el civ_id del árbol, los edificios que esa civ hace sin el que piden las demás: el nodo no cuelga de ninguno
    (los jemeres no necesitan edificios para habilitar otros), o cuelga de uno que no está en los datos ni vale por el
    de siempre (la granja de los polacos cuelga del folwark, que reemplaza al molino). Pares (tipo, id del .dat), como
    los de cargar_no_disponibles."""
    requisitos = cargar_requisitos(carpeta)
    claves = {dat_id: clave for clave, (tipo, dat_id) in ENTIDADES.items() if tipo == "building"}
    resultado: dict[str, set[tuple[str, int]]] = {}
    for archivo in sorted(carpeta.glob("*.json")):
        arbol = json.loads(archivo.read_text(encoding="utf-8"))
        civ_id = str(arbol.get("civ_id", archivo.stem)).upper()
        resultado[civ_id] = set()
        for nodo in arbol.get("civ_techs_buildings", []):
            requisito = requisitos.get(claves.get(nodo.get("Node ID")))
            if requisito is None or nodo.get("Node Status") == "NotAvailable":
                continue
            de = claves.get(nodo.get("Link ID", -1))
            if de != requisito and requisito not in CUENTA_COMO.get(de, []):
                resultado[civ_id].add(("building", nodo["Node ID"]))
    return resultado


def cargar_edades_de_unidades(carpeta: Path) -> dict[int, str]:
    """Edad en que se entrena cada unidad según los árboles ("Age ID": 1 Oscura a 4 Imperial): la más común entre
    las civs que la tienen. Las habilitaciones del .dat no alcanzan: el camello lo habilita un efecto de cada civ sin
    requisito de edad y salía de la Edad Oscura (es de Castillos)."""
    edades: dict[int, Counter] = {}
    for archivo in sorted(carpeta.glob("*.json")):
        for nodos in json.loads(archivo.read_text(encoding="utf-8")).values():
            if not isinstance(nodos, list):
                continue
            for nodo in nodos:
                if nodo.get("Use Type") != "Unit" or nodo.get("Node Status") == "NotAvailable" or nodo.get("Age ID", 0) not in (1, 2, 3, 4):
                    continue
                edades.setdefault(nodo["Node ID"], Counter())[nodo["Age ID"]] += 1
    # La más común; ante un empate, la más temprana.
    return {uid: _edad_por_nivel(min(c, key=lambda a: (-c[a], a)) - 1) for uid, c in edades.items()}


def clave_de_nombre(nombre: str) -> str:
    """'Man-at-Arms' -> 'man_at_arms' (sin tildes)."""
    ascii_ = unicodedata.normalize("NFKD", nombre).encode("ascii", "ignore").decode()
    return re.sub(r"[^a-z0-9]+", "_", ascii_.lower()).strip("_")


def frase(plantilla: str, nombre: str = "") -> str:
    """'--%s Built--' + 'House' -> 'House Built'."""
    return plantilla.replace("%s", nombre).strip("-").strip()


def costo(resource_costs) -> dict[str, int]:
    """Solo los costos que se descuentan (flag 1) y son recursos reales."""
    return {
        RECURSOS[c.type]: c.amount
        for c in resource_costs
        if c.flag == 1 and c.type in RECURSOS and c.amount > 0
    }


class Extractor:
    def __init__(self, dat: DatFile, edades_de_unidades: dict[int, str] | None = None):
        self.dat = dat
        self.unidades = dat.civs[CIV_BASE].units
        self.clave_por_unidad = self._indexar_claves()
        self._edad_tech: dict[int, int] = {}
        self._habilitadores = self._indexar_habilitadores()
        self._edades_de_unidades = edades_de_unidades or {}

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
        if uid in self._edades_de_unidades:
            return self._edades_de_unidades[uid]
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
            datos = {
                "kind": tipo,
                "dat_id": dat_id,
                "dat_name": tech.name,
                "cost": costo(tech.resource_costs),
                "time": loc.research_time,
                "made_in": self.lugar(loc.location_id),
                "age": _edad_por_nivel(self.nivel_edad_requerida(dat_id)),
            }
            comandos = self.dat.effects[tech.effect_id].effect_commands if 0 <= tech.effect_id < len(self.dat.effects) else []
            if recoleccion := self.efectos_de_recoleccion(comandos):
                datos["gather"] = recoleccion
            if granja := self.comida_de_granja(comandos):
                datos["farm_food"] = granja
            return datos

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
            if clave in CUENTA_COMO:
                datos["counts_as"] = CUENTA_COMO[clave]
        return datos

    def tasas(self) -> dict[str, float]:
        return {
            recurso: round(self.unidades[uid].bird.work_rate, 3)
            for recurso, uid in VARIANTES_RECOLECCION.items()
        }

    def comida_por_objeto(self) -> dict[str, float]:
        """La comida de una oveja, un jabalí, un arbusto de bayas y una granja sin mejoras."""
        comida = {
            fuente: next(s.amount for s in self.unidades[uid].resource_storages if s.type == 0)
            for fuente, uid in COMIDA_POR_OBJETO.items()
        }
        comida["farm"] = self.dat.civs[CIV_CON_RECURSOS].resources[RECURSO_COMIDA_GRANJA]
        return comida

    @staticmethod
    def comida_de_granja(comandos) -> float:
        """Lo que suma una tecnología a la comida de cada granja (collera +75, arado pesado +125)."""
        return sum(c.d for c in comandos if c.type == 1 and c.a == RECURSO_COMIDA_GRANJA and c.b == 1)

    def cargas(self) -> dict[str, float]:
        """Cuánto lleva por viaje el aldeano de cada tarea (10; el cazador, 35)."""
        return {fuente: self.unidades[ids[0]].resource_capacity for fuente, ids in TAREAS.items()}

    def efectos_de_recoleccion(self, comandos) -> dict[str, dict]:
        """
        Lo que unos comandos de efecto le hacen a cada tarea de los aldeanos: trabajo y velocidad (multiplicadores)
        y carga (se multiplica y después se suma: la carretilla ×1,27; el arado pesado +1 a los granjeros). Se evalúa
        sobre el aldeano hombre, como las bonificaciones de civ.
        """
        efectos = {}
        for fuente, ids in TAREAS.items():
            unidad = self.unidades[ids[0]]
            trabajo = self.atributo(comandos, {ids[0]}, CLASE_ALDEANO, ATTR_TRABAJO, unidad.bird.work_rate) / unidad.bird.work_rate
            velocidad = self.atributo(comandos, {ids[0]}, CLASE_ALDEANO, ATTR_VELOCIDAD, unidad.speed) / unidad.speed
            # La carga es lineal en el valor de partida (c · m + a): con dos puntos salen m y a.
            con10 = self.atributo(comandos, {ids[0]}, CLASE_ALDEANO, ATTR_CARGA, 10.0)
            con20 = self.atributo(comandos, {ids[0]}, CLASE_ALDEANO, ATTR_CARGA, 20.0)
            multiplica = (con20 - con10) / 10
            suma = con10 - 10 * multiplica
            efecto = {}
            if round(trabajo, 4) != 1:
                efecto["work"] = round(trabajo, 4)
            if round(multiplica, 4) != 1 or round(suma, 3) != 0:
                efecto["carry"] = [round(multiplica, 4), round(suma, 3)]
            if round(velocidad, 4) != 1:
                efecto["speed"] = round(velocidad, 4)
            if efecto:
                efectos[fuente] = efecto
        return efectos

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
        """
        La unidad que representa a uid en esa edad (los edificios cambian de unidad al subir de edad). Cada edad
        reemplaza a la unidad original (109 → 71 en Feudal, 109 → 141 en Castillos), no a la de la edad anterior.
        """
        actual = uid
        for n in range(1, nivel + 1):
            mejoras = self.mejoras_de_edad()[n]
            actual = mejoras.get(uid, mejoras.get(actual, actual))
        return actual

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

    @staticmethod
    def recurso(comandos, rid: int, base: float) -> float:
        """Un recurso de la civ después de los comandos: el 6 lo multiplica y el 1 lo fija (modo 0) o le suma (modo 1)."""
        valor = base
        for c in comandos:
            if c.a != rid:
                continue
            if c.type == 6:
                valor *= c.d
            elif c.type == 1 and c.b == 0:
                valor = c.d
            elif c.type == 1 and c.b == 1:
                valor += c.d
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
        # Carga por viaje (absoluta, por edad) y velocidad de los aldeanos (multiplicador).
        carga = {}
        for fuente, ids in TAREAS.items():
            base = self.unidades[ids[0]].resource_capacity
            valores = por_edad(lambda cmds, n, uid=ids[0], base=base: round(self.atributo(cmds, {uid}, CLASE_ALDEANO, ATTR_CARGA, base), 3))
            if any(v != base for v in valores):
                carga[fuente] = valores
        if carga:
            datos["carry"] = carga
        lenador = self.unidades[TAREAS["wood"][0]]
        velocidad = por_edad(lambda cmds, n: round(
            self.atributo(cmds, {TAREAS["wood"][0]}, CLASE_ALDEANO, ATTR_VELOCIDAD, lenador.speed) / lenador.speed, 4))
        if any(v != 1 for v in velocidad):
            datos["villager_speed"] = velocidad

        # D12: lo que rinde la comida, para la comida finita del modo asistido. La fuente dura más y el aldeano trabaja
        # más lento en la misma proporción (tártaros: ovejas ×1,57 con el pastor ×0,637): la tasa casi no cambia y el
        # total sí. El folwark solo cuenta si lo fija una bonificación propia de la civ (las tecnologías genéricas de
        # granja también tocan el 237, pero sin folwark no hace nada).
        recursos_civ = self.dat.civs[civ_id].resources
        rendimiento = {}
        for fuente, rid in RENDIMIENTO_COMIDA.items():
            valores = por_edad(lambda cmds, n, rid=rid: round(self.recurso(cmds, rid, recursos_civ[rid]), 4))
            if any(v != 1 for v in valores):
                rendimiento[fuente] = valores
        if rendimiento:
            datos["food_yield"] = rendimiento
        bayas = RENDIMIENTO_COMIDA["berries"]
        if por_edad(lambda cmds, n: self.recurso(cmds, bayas, recursos_civ[bayas])) != por_edad(
                lambda cmds, n: self.recurso(cmds, RECURSO_RENDIMIENTO_COMIDA, recursos_civ[RECURSO_RENDIMIENTO_COMIDA])):
            print(f"  aviso: {self.dat.civs[civ_id].name}: el rendimiento de las bayas ({bayas}) ya no es igual al de la "
                  f"comida ({RECURSO_RENDIMIENTO_COMIDA}); revisar cuál usa el recolector", file=sys.stderr)
        mejoras = por_edad(lambda cmds, n: round(self.recurso(cmds, RECURSO_MEJORAS_GRANJA, recursos_civ[RECURSO_MEJORAS_GRANJA]), 4))
        if any(v != 1 for v in mejoras):
            datos["farm_upgrade_food"] = mejoras
        extra = por_edad(lambda cmds, n: round(next(
            (c.d for c in reversed(cmds)
             if c.type == 1 and c.a == RECURSO_GRANJA_EXTRA and c.b == 0 and c.c == RECURSO_COMIDA_GRANJA), 0.0), 4))
        if any(v != 0 for v in extra):
            datos["farm_extra"] = extra

        # Recursos que da cada edad al llegar (dravidianos +200 de madera, etíopes +100 de comida y de oro): los
        # comandos nuevos de cada edad que suman al stock.
        al_subir = {}
        for n in range(1, 4):
            for c in niveles[n][len(niveles[n - 1]):]:
                if c.type == 1 and c.a in RECURSOS and c.b == 1 and c.d > 0:
                    edad = al_subir.setdefault(_edad_por_nivel(n), {})
                    edad[RECURSOS[c.a]] = edad.get(RECURSOS[c.a], 0) + int(c.d)
        if al_subir:
            datos["age_resources"] = al_subir

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
                # Solo la unidad de esa edad: los efectos nombran a cada variante por separado con el mismo valor.
                variante = self.variante(uid, n)
                return {variante}, self.unidades[variante].class_

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
        anexos = {a.unit_id for a in self.unidades[ID_CENTRO_URBANO].building.annexes if a.unit_id >= 0}
        for c in niveles[0]:
            if c.type == 1 and c.a in RECURSO_INICIAL and c.b == 1:
                inicio[RECURSO_INICIAL[c.a]] = inicio.get(RECURSO_INICIAL[c.a], 0) + int(c.d)
            elif c.type == 0 and c.c == ATTR_UNIDAD_MUERTA and c.a in anexos:
                # Lituanos: un anexo vacío del centro urbano pasa a convertirse en un edificio que guarda comida
                # ("Lithuanian food building", 100), que se suma al stock.
                for s in self.unidades[int(c.d)].resource_storages:
                    if s.type in RECURSOS and s.amount > 0:
                        inicio[RECURSOS[s.type]] = inicio.get(RECURSOS[s.type], 0) + int(s.amount)
            elif c.type == 7 and c.a == ID_ALDEANO:
                inicio["villagers"] = inicio.get("villagers", 0) + int(c.c)
            elif c.type == 0 and c.a == ID_CASA and c.c == 0 and c.d < 0:
                inicio["no_houses"] = True  # hunos: las casas no existen y la población no tiene tope de casas
        if inicio:
            datos["start"] = inicio
        return datos

    def eventos_traducidos(self, textos_en: dict[int, str], traducciones: list[dict[int, str]],
                           disponibles: dict[str, set[int]]) -> tuple[dict[str, list[str]], list[str]]:
        """Los mismos eventos (mismos ids, que salen de los textos en inglés) con las frases de otros idiomas, juntas."""
        eventos: dict[str, list[str]] = {}
        conflictos: list[str] = []
        for textos in traducciones:
            parciales, otros = self.eventos(textos, disponibles, textos_clave=textos_en)
            conflictos += [c for c in otros if c not in conflictos]
            for evento, frases in parciales.items():
                eventos.setdefault(evento, []).extend(f for f in frases if f not in eventos[evento])
        return eventos, conflictos

    def nombres_de_unidad(self, uid: int, textos: dict[int, str]) -> list[str]:
        """Nombres que muestra la unidad o el edificio: primero el de Gaia y después los que cambian según la civ.

        Sin lo que va entre paréntesis: el aldeano se llama "Villager (Male)" y el log dice "Villager Created"
        (verificado); lo mismo con "Trade Cart (Empty)", "Trebuchet (Packed)" o "Ratha (Ranged)".
        """
        nombres: list[str] = []
        for civ in self.dat.civs:
            u = civ.units[uid] if uid < len(civ.units) else None
            texto = textos.get(u.language_dll_name) if u is not None else None
            texto = re.sub(r"\s*\([^)]*\)", "", texto).strip() if texto else None
            if texto and texto not in nombres:
                nombres.append(texto)
        return nombres

    def eventos(self, textos: dict[int, str], disponibles: dict[str, set[int]],
                textos_clave: dict[int, str] | None = None) -> tuple[dict[str, list[str]], list[str]]:
        """id de evento -> frases del log, y los conflictos (frases que ya eran de otro evento).

        La clave es la de game_data si la entidad está ahí, si no sale del nombre en inglés (de `textos_clave`, que
        son los mismos `textos` si no se dan: así otro idioma da los mismos ids). Dos entidades con el mismo nombre
        (el huscarle del castillo y el del cuartel) comparten evento: el log no las distingue.
        """
        textos_clave = textos_clave or textos
        claves_tech = {i: k for k, (t, i) in ENTIDADES.items() if t == "tech"}
        ids = {tipo: set(disponibles[tipo]) for tipo in PLANTILLAS}
        for tipo, dat_id in ENTIDADES.values():
            ids[tipo].add(dat_id)

        candidatos = []
        for tipo in PLANTILLAS:
            for dat_id in sorted(ids[tipo]):
                if tipo == "tech":
                    dll = self.dat.techs[dat_id].language_dll_name if dat_id < len(self.dat.techs) else None
                    texto, texto_clave = textos.get(dll), textos_clave.get(dll)
                    nombres, conocida = ([texto] if texto else []), claves_tech.get(dat_id)
                    nombres_clave = [texto_clave] if texto_clave else []
                else:
                    nombres, conocida = self.nombres_de_unidad(dat_id, textos), self.clave_por_unidad.get(dat_id)
                    nombres_clave = self.nombres_de_unidad(dat_id, textos_clave)
                if nombres and (conocida or nombres_clave):
                    candidatos.append((conocida is None, tipo, conocida or clave_de_nombre(nombres_clave[0]), nombres))

        eventos: dict[str, list[str]] = {}
        duenio: dict[str, str] = {}
        conflictos: list[str] = []
        # Primero el nombre principal de cada entidad (el de Gaia) y después los que cambian según la civ: en castellano
        # el lancero se llama "Lancero" y el caballero de alguna civ también, y el log de casi todas es del lancero.
        # Dentro de cada vuelta, primero las entidades de game_data: si una frase choca, gana la clave que usan las BO.
        ordenados = sorted(candidatos, key=lambda c: (c[0], c[2]))
        for principal, (_, tipo, clave, nombres) in [(p, c) for p in (True, False) for c in ordenados]:
            texto_id, sufijo = PLANTILLAS[tipo]
            evento = f"{clave}_{sufijo}"
            for nombre in (nombres[:1] if principal else nombres[1:]):
                texto = frase(textos[texto_id], nombre)
                dueno = duenio.setdefault(re.sub(r"[^a-z]+", " ", texto.lower()).strip(), evento)
                if dueno != evento:
                    conflictos.append(f"{texto!r} es de {dueno}, no de {evento}")
                elif texto not in eventos.setdefault(evento, []):
                    eventos[evento].append(texto)
        for texto_id, evento in AVISOS.items():
            plantilla = textos[texto_id]
            eventos[evento] = [frase(plantilla)]
            corte = next((c for c in PRIMER_RENGLON.get(texto_id, ()) if c in plantilla), None)
            if corte:
                eventos[evento].insert(0, frase(plantilla.split(corte)[0]))
        return {e: f for e, f in eventos.items() if f}, conflictos

    def civs(self, entidades: dict) -> dict:
        civs = {}
        for civ_id, civ in enumerate(self.dat.civs):
            if civ_id == CIV_BASE:
                continue
            civs[clave_de_civ(civ.name)] = self.civ(civ_id, entidades)
            if self.sube_sin_edificios(civ_id):
                civs[clave_de_civ(civ.name)]["no_age_buildings"] = True
        return civs

    def sube_sin_edificios(self, civ_id: int) -> bool:
        """Si la civ sube de edad sin los 2 edificios: cada edad pide una "Shadow Node" que se cumple con la tech de los
        edificios (120, 135, 142) o con una tech propia de la civ sin requisitos (la 638, "Khmer requirement")."""
        for edad in ("feudal_age", "castle_age", "imperial_age"):
            tech = self.dat.techs[ENTIDADES[edad][1]]
            for nodo in (r for r in tech.required_techs if r >= 0):
                alternativas = [r for r in self.dat.techs[nodo].required_techs if r >= 0]
                if self.dat.techs[nodo].required_tech_count == 1 and any(self.dat.techs[r].civ == civ_id for r in alternativas):
                    return True
        return False

    def exportar(self) -> dict:
        entidades = {clave: self.entidad(clave, tipo, i) for clave, (tipo, i) in ENTIDADES.items()}
        return {
            "dat_version": self.dat.version,
            "entities": entidades,
            "gather_rates": self.tasas(),
            "gather_carry": self.cargas(),
            "food_per_object": self.comida_por_objeto(),
            "civs": self.civs(entidades),
        }


def nombres_de_civs(dat: DatFile, textos_por_idioma: dict[str, dict[int, str]]) -> dict[str, dict[str, str]]:
    """Clave de civ -> {idioma: nombre que muestra el juego}."""
    nombres = {}
    for civ_id, civ in enumerate(dat.civs):
        if civ_id == CIV_BASE:
            continue
        nombres[clave_de_civ(civ.name)] = {idioma: textos[TEXTO_NOMBRE_DE_CIV + civ_id]
                                           for idioma, textos in textos_por_idioma.items()
                                           if TEXTO_NOMBRE_DE_CIV + civ_id in textos}
    return nombres


def _edad_por_nivel(nivel: int) -> str:
    return next(nombre for nombre, n in NIVEL_EDAD.items() if n == nivel)


SECCIONES_DE_EVENTOS = (("Unidades", "_created"), ("Edificios", "_built"), ("Tecnologías", "_researched"))


ENCABEZADO_EN = [
    "  // Frases del log de eventos del juego -> id de evento. Lo genera tools/extractor/extract.py: no editar a mano.",
    '  // Convención de ids: "<entidad>_created" (unidades), "<entidad>_built" (edificios) y',
    '  // "<entidad>_researched" (tecnologías), con la clave de game_data o, si no está ahí, el nombre en inglés.',
    '  // Las frases son las plantillas del juego ("--%s Created--", "--%s Built--", "--%s Research Complete--")',
    "  // con el nombre que muestra cada entidad, que puede cambiar según la civ. Están las entidades que alguna civ",
    "  // puede hacer según los árboles de tecnologías del juego (CivTechTrees).",
    f"  // Verificadas en una partida real (2026-10-15): {VERIFICADAS}.",
    "  // Ojo: si un mensaje se repite, el juego no agrega otro renglón sino que mueve el viejo al final.",
]

ENCABEZADO_ES = [
    "  // Frases del log con el juego en castellano -> id de evento. Lo genera tools/extractor/extract.py: no editar a mano.",
    "  // Los ids son los mismos que en events.json (salen de los nombres en inglés); la app carga los dos archivos.",
    '  // Plantillas del juego: "--%s: creación finalizada--", "--%s: construcción finalizada--" y',
    '  // "--%s: desarrollo finalizado--", con los nombres de resources/es (España) y resources/mx (Latinoamérica),',
    "  // que en muchas entidades difieren. Sin verificar todavía en una partida real.",
]


def escribir_eventos(eventos: dict[str, list[str]], ruta: Path, encabezado: list[str] = ENCABEZADO_EN) -> None:
    """data/events.json: JSON con comentarios, un evento por renglón y agrupado por tipo."""
    grupos = [(titulo, sorted(e for e in eventos if e.endswith(sufijo))) for titulo, sufijo in SECCIONES_DE_EVENTOS]
    grupos.append(("Otros mensajes", [e for e in eventos if not any(e.endswith(s) for _, s in SECCIONES_DE_EVENTOS)]))

    renglones = ["{", *encabezado]
    entradas = [
        (titulo, [f"  {json.dumps(e)}: {json.dumps(eventos[e], ensure_ascii=False)}" for e in ids])
        for titulo, ids in grupos
    ]
    total = sum(len(lineas) for _, lineas in entradas)
    escritas = 0
    for titulo, lineas in entradas:
        renglones += ["", f"  // {titulo} ({len(lineas)})"]
        for linea in lineas:
            escritas += 1
            renglones.append(linea + ("," if escritas < total else ""))
    renglones.append("}")
    ruta.write_text("\n".join(renglones) + "\n", encoding="utf-8")


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
    parser.add_argument("--eventos", type=Path, default=REPO / "data" / "events.json", help="frases del log")
    args = parser.parse_args()

    # El .dat está en resources/_common/dat; los textos en inglés, en resources/en/strings/key-value.
    textos = cargar_textos(args.dat.parents[2] / "en" / "strings" / "key-value")
    disponibles = cargar_disponibles(args.dat.parent / "CivTechTrees")

    print(f"Leyendo {args.dat} (tarda ~30 s)...")
    dat = DatFile.parse(str(args.dat))
    extractor = Extractor(dat, cargar_edades_de_unidades(args.dat.parent / "CivTechTrees"))
    datos = extractor.exportar()
    recursos = args.dat.parents[2]
    textos_por_idioma = {"en": textos} | {c: cargar_textos(recursos / c / "strings" / "key-value") for c in IDIOMAS_DE_NOMBRES
                                          if (recursos / c / "strings" / "key-value").is_dir()}
    for clave, nombres in nombres_de_civs(dat, textos_por_idioma).items():
        datos["civs"][clave]["names"] = nombres
    for clave, requisito in cargar_requisitos(args.dat.parent / "CivTechTrees").items():
        datos["entities"][clave]["requires"] = requisito
    for clave, claves in no_disponibles_por_civ(cargar_sin_requisitos(args.dat.parent / "CivTechTrees"), datos["civs"]).items():
        if claves:
            datos["civs"][clave]["no_requires"] = claves
    for clave, claves in no_disponibles_por_civ(cargar_no_disponibles(args.dat.parent / "CivTechTrees"), datos["civs"]).items():
        datos["civs"][clave]["unavailable"] = claves

    eventos, conflictos = extractor.eventos(textos, disponibles)
    escribir_eventos(eventos, args.eventos)
    for titulo, sufijo in SECCIONES_DE_EVENTOS:
        print(f"  {titulo}: {sum(e.endswith(sufijo) for e in eventos)} eventos")
    print(f"{len(eventos)} eventos exportados a {args.eventos}")
    for conflicto in conflictos:
        print(f"  frase repetida: {conflicto}")

    # Los mismos eventos con el juego en otros idiomas, al lado de events.json.
    for archivo, carpetas in IDIOMAS.items():
        traducciones = [cargar_textos(recursos / c / "strings" / "key-value") for c in carpetas
                        if (recursos / c / "strings" / "key-value").is_dir()]
        if not traducciones:
            continue
        traducidos, conflictos = extractor.eventos_traducidos(textos, traducciones, disponibles)
        ruta = args.eventos.parent / archivo
        escribir_eventos(traducidos, ruta, ENCABEZADO_ES)
        print(f"{len(traducidos)} eventos exportados a {ruta} ({', '.join(carpetas)})")
        for conflicto in conflictos:
            print(f"  frase repetida: {conflicto}")

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
        for nombre in ("costs", "times", "work_rate", "pop_bonus", "carry"):
            if nombre in civ:
                partes.append(f"{nombre}: {', '.join(civ[nombre])}")
        for nombre in ("villager_speed", "age_resources"):
            if nombre in civ:
                partes.append(f"{nombre}: {civ[nombre]}")
        print(f"  {clave:14} {' · '.join(partes)}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
