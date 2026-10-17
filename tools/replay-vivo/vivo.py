"""Muestra en vivo lo que el juego va escribiendo en el replay (.aoe2record) durante una partida.

Sirve para saber si el replay se escribe en tiempo real: encolá un aldeano o construí algo y fijate si aparece acá
en el momento (cada orden tuya suena con un pitido, así no hace falta mirar la consola mientras jugás).

    vivo.bat [--jugador N] [--sin-sonido] [--carpeta RUTA] [--segundos N]

Solo muestra las órdenes y el total de recursos del jugador elegido (por defecto el 1). Las del resto no se muestran:
en una partida contra humanos, ver en vivo lo que hace el rival es trampa.

Qué trae el replay (verificado sobre una partida de la versión 101.103.54800):
- El cuerpo es una lista de operaciones: órdenes de los jugadores (ACTION), sincronizaciones con el avance del
  tiempo (SYNC), la cámara del que graba (VIEWLOCK), chat y, al final, las estadísticas de la partida (POSTGAME).
- Cada unos 12 s, una sincronización trae valores por jugador; uno es el total de recursos en stock (madera +
  comida + oro + piedra). Cierra con los costos: 1075 → 700 al construir un centro urbano (275 de madera + 100 de
  piedra).
- La cabecera (la foto del arranque) cambió en esta versión y mgz 1.8.51 no la entiende: se saltea.

Además de la consola, deja un registro en %LOCALAPPDATA%\\BoCoach\\replay-vivo\\<fecha>.csv con cada vez que el
archivo creció (hora, bytes y hasta qué minuto del juego llegó), para medir cada cuánto escribe el juego.
"""

from __future__ import annotations

import argparse
import csv
import ctypes
import json
import msvcrt
import os
import re
import struct
import sys
import time
import winsound
from collections import deque
from ctypes import wintypes
from datetime import datetime
from pathlib import Path

from mgz import fast
from mgz.fast.enums import Operation

REPO = Path(__file__).resolve().parents[2]
CARPETA_JUEGO = Path(os.environ["USERPROFILE"]) / "Games" / "Age of Empires 2 DE"
CARPETA_REGISTROS = Path(os.environ["LOCALAPPDATA"]) / "BoCoach" / "replay-vivo"
INTERVALO = 0.2  # segundos entre lecturas

VERDE, AMARILLO, GRIS, NORMAL = "\x1b[92m", "\x1b[93m", "\x1b[90m", "\x1b[0m"

# Nombres en castellano de las claves de game_data más comunes (el resto se muestra con la clave).
NOMBRES = {
    "villager": "aldeano", "militia": "milicia", "spearman": "lancero", "archer": "arquero",
    "skirmisher": "hostigador", "scout_cavalry": "explorador", "knight": "caballero", "monk": "monje",
    "fishing_ship": "barco pesquero", "trade_cart": "carreta de comercio", "mangonel": "mangonel",
    "town_center": "centro urbano", "house": "casa", "mill": "molino", "lumber_camp": "campamento maderero",
    "mining_camp": "campamento minero", "farm": "granja", "dock": "muelle", "barracks": "cuartel",
    "archery_range": "galería de tiro", "stable": "establo", "blacksmith": "herrería", "market": "mercado",
    "monastery": "monasterio", "university": "universidad", "siege_workshop": "taller de asedio",
    "palisade_wall": "empalizada", "stone_wall": "muro de piedra", "outpost": "puesto de avanzada",
    "watch_tower": "torre de vigilancia", "castle": "castillo", "loom": "telar", "town_watch": "vigilancia",
    "wheelbarrow": "carretilla", "hand_cart": "carreta de mano", "double_bit_axe": "hacha de doble filo",
    "bow_saw": "sierra de arco", "horse_collar": "collera", "heavy_plow": "arado pesado",
    "gold_mining": "minería de oro", "stone_mining": "minería de piedra", "feudal_age": "Edad Feudal",
    "castle_age": "Edad de los Castillos", "imperial_age": "Edad Imperial", "man_at_arms": "hombre de armas",
    "fletching": "emplumado", "forging": "forja", "bloodlines": "linaje", "husbandry": "cría de caballos",
}

ORDENES = {
    "MOVE": "Mover", "ORDER": "Orden (recolectar, atacar, construir o reparar)", "STOP": "Detener",
    "GATHER_POINT": "Punto de reunión", "DE_MULTI_GATHERPOINT": "Punto de reunión", "DELETE": "Borrar",
    "BACK_TO_WORK": "Volver al trabajo", "UNGARRISON": "Desguarnecer", "DE_TRANSFORM": "Transformar",
    "PATROL": "Patrullar", "DE_ATTACK_MOVE": "Atacar en movimiento", "FORMATION": "Formación",
    "STANCE": "Postura", "SPECIAL": "Habilidad especial", "RESIGN": "Rendirse", "TOWN_BELL": "Campana",
    "REPAIR": "Reparar", "GUARD": "Escoltar", "FOLLOW": "Seguir", "FLARE": "Bengala", "GATE": "Puerta",
    "DE_AUTOSCOUT": "Exploración automática", "AI_ORDER": "Orden automática", "ATTACK_GROUND": "Atacar el suelo",
}
RECURSOS = {0: "comida", 1: "madera", 2: "piedra", 3: "oro"}


# ---- Datos del juego: id del .dat -> nombre ----

def cargar_nombres() -> tuple[dict[int, str], dict[int, str]]:
    """Unidades y edificios por id, y tecnologías por id, sacados del game_data más nuevo del repo."""
    archivos = sorted(
        (REPO / "data" / "game_data").glob("v*.json"),
        key=lambda p: tuple(int(n) for n in re.findall(r"\d+", p.stem)),
    )
    if not archivos:
        return {}, {}
    entidades = json.loads(archivos[-1].read_text(encoding="utf-8"))["entities"]
    unidades = {e["dat_id"]: NOMBRES.get(k, k) for k, e in entidades.items() if e["kind"] != "tech"}
    techs = {e["dat_id"]: NOMBRES.get(k, k) for k, e in entidades.items() if e["kind"] == "tech"}
    return unidades, techs


UNIDADES, TECHS = cargar_nombres()


def describir(nombre: str, d: dict) -> str:
    n = len(d.get("object_ids") or [])
    cuantas = f" ({n} unidad{'es' if n != 1 else ''})" if n else ""
    if nombre in ("DE_QUEUE", "QUEUE", "MULTIQUEUE", "MAKE"):
        unidad = d.get("unit_id")
        return f"Encolar {d.get('amount', 1)} × {UNIDADES.get(unidad, f'unidad {unidad}')}"
    if nombre == "RESEARCH":
        tech = d.get("technology_id")
        return f"Investigar {TECHS.get(tech, f'tecnología {tech}')}"
    if nombre in ("BUILD", "WALL"):
        edificio = d.get("building_id")
        return f"Construir {UNIDADES.get(edificio, f'edificio {edificio}')} en ({d['x']:.0f}, {d['y']:.0f}){cuantas}"
    if nombre in ("BUY", "SELL"):
        verbo = "Comprar" if nombre == "BUY" else "Vender"
        return f"{verbo} {RECURSOS.get(d.get('resource_id'), 'recurso')} en el mercado"
    return ORDENES.get(nombre, nombre) + cuantas


# ---- Lectura del archivo mientras el juego lo escribe ----

_kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
_kernel32.CreateFileW.argtypes = [
    wintypes.LPCWSTR, wintypes.DWORD, wintypes.DWORD, wintypes.LPVOID, wintypes.DWORD, wintypes.DWORD, wintypes.HANDLE,
]
_kernel32.CreateFileW.restype = wintypes.HANDLE
_LEER, _COMPARTIR_TODO, _SI_EXISTE = 0x80000000, 0x1 | 0x2 | 0x4, 3
_INVALIDO = ctypes.c_void_p(-1).value


def abrir(ruta: Path):
    """Abre el archivo sin trabar al juego: lo deja seguir escribiendo, renombrarlo o borrarlo."""
    h = _kernel32.CreateFileW(str(ruta), _LEER, _COMPARTIR_TODO, None, _SI_EXISTE, 0, None)
    if h in (None, _INVALIDO):
        raise ctypes.WinError(ctypes.get_last_error())
    return os.fdopen(msvcrt.open_osfhandle(h, os.O_RDONLY | os.O_BINARY), "rb")


class Estricto:
    """Envuelve el archivo para mgz: si faltan bytes (el juego todavía no los escribió), corta en vez de leer de menos.

    mgz lee las acciones con read(n) y, si vienen cortadas, las devuelve como ERROR sin avisar.
    """

    def __init__(self, archivo):
        self.archivo = archivo

    def read(self, n: int = -1) -> bytes:
        if n is None or n < 0:
            return self.archivo.read()
        datos = self.archivo.read(n)
        if len(datos) < n:
            raise EOFError
        return datos

    def seek(self, posicion: int, desde: int = 0) -> int:
        return self.archivo.seek(posicion, desde)

    def tell(self) -> int:
        return self.archivo.tell()


def reloj(ms: int) -> str:
    s = ms // 1000
    return f"{s // 3600}:{s // 60 % 60:02d}:{s % 60:02d}" if s >= 3600 else f"{s // 60}:{s % 60:02d}"


class Replay:
    """Un .aoe2record que está creciendo: se lee de a pedazos desde la última operación completa."""

    # Si la primera lectura ya trae más que esto de partida, lo leído es historial: el script arrancó con la partida
    # empezada, o el juego movió o renombró el archivo al terminar. La cabecera sola pesa más de 1 MB, así que el
    # tamaño no sirve para decidirlo.
    HISTORIAL_MS = 30_000

    def __init__(self, ruta: Path, ya_estaba: bool):
        self.ruta = ruta
        self.ya_estaba = ya_estaba  # existía al arrancar el script: lo primero que se lee es historial
        self.primera_lectura = True
        self.siguiente: int | None = None  # dónde empieza la próxima operación sin leer
        self.tiempo = 0  # ms de juego hasta la última sincronización
        self.otros: set[int] = set()
        self.terminado = False
        self.problema: str | None = None

    def leer(self, salida: "Salida") -> None:
        with abrir(self.ruta) as archivo:
            tamano = os.fstat(archivo.fileno()).st_size
            lector = Estricto(archivo)
            if self.siguiente is None:
                if tamano < 8:
                    return
                largo = struct.unpack("<I", archivo.read(4))[0]
                if largo <= 8 or largo > tamano:
                    return  # la cabecera todavía no está completa
                archivo.seek(largo)
                try:
                    fast.meta(lector)
                except (EOFError, ValueError):
                    return
                self.siguiente = archivo.tell()
                salida.aviso(f"Cabecera de {largo:,} bytes; las órdenes empiezan en el byte {self.siguiente:,}.")

            archivo.seek(self.siguiente)
            pendientes = []  # en la primera lectura se junta todo hasta saber si es historial
            mostrar = pendientes.append if self.primera_lectura else lambda f: f()
            while True:
                try:
                    operacion, datos = fast.operation(lector)
                except EOFError:
                    break  # operación a medio escribir: se retoma en la próxima lectura
                except ValueError:
                    self.siguiente = archivo.tell()  # una acción que mgz no conoce: se saltea entera
                    continue
                except RuntimeError as e:
                    if self.problema is None:
                        self.problema = f"No entiendo lo que sigue en el byte {self.siguiente:,} ({e})."
                        salida.aviso(self.problema)
                    break
                self.siguiente = archivo.tell()

                if operacion == Operation.SYNC:
                    incremento, _, valores = datos
                    self.tiempo = valores.get("current_time", self.tiempo + incremento)
                    propio = valores.get(salida.jugador)
                    if propio:
                        mostrar(lambda t=self.tiempo, total=propio["total_res"]: salida.recursos(t, total, tamano))
                elif operacion == Operation.ACTION:
                    tipo, d = datos
                    jugador = d.get("player_id")
                    if jugador == salida.jugador:
                        mostrar(lambda t=self.tiempo, texto=describir(tipo.name, d): salida.orden(t, texto, tamano))
                    elif jugador is not None and jugador not in self.otros:
                        self.otros.add(jugador)
                        salida.aviso(f"Hay órdenes del jugador {jugador} (no las muestro). Si sos vos, usá --jugador {jugador}.")
                elif operacion == Operation.POSTGAME:
                    self.terminado = True
                    salida.aviso("Llegaron las estadísticas del final: la partida terminó.")
                    break

            if self.primera_lectura:
                self.primera_lectura = False
                if self.ya_estaba or self.tiempo > self.HISTORIAL_MS:
                    salida.aviso(
                        f"Lo que ya estaba escrito llega al {reloj(self.tiempo)} del juego (no lo muestro). Desde "
                        "ahora muestro todo lo nuevo."
                    )
                else:
                    for pendiente in pendientes:
                        pendiente()


# ---- Pantalla y registro ----

class Salida:
    def __init__(self, jugador: int, sonido: bool, registro: Path):
        self.jugador = jugador
        self.sonido = sonido
        self.consola = sys.stdout.isatty()
        self.estado_visible = False
        registro.parent.mkdir(parents=True, exist_ok=True)
        self._archivo = registro.open("w", newline="", encoding="utf-8")
        self._csv = csv.writer(self._archivo)
        self._csv.writerow(["hora", "evento", "bytes", "tiempo_juego_ms", "detalle"])

    def _hora(self) -> str:
        return datetime.now().strftime("%H:%M:%S.%f")[:-3]

    def _limpiar_estado(self) -> None:
        if self.estado_visible:
            print("\r" + " " * 119 + "\r", end="")
            self.estado_visible = False

    def _renglon(self, texto: str, color: str = "") -> None:
        self._limpiar_estado()
        print(f"{color}{texto}{NORMAL if color else ''}", flush=True)

    def registrar(self, evento: str, tamano: int, tiempo_ms: int, detalle: str = "") -> None:
        self._csv.writerow([datetime.now().isoformat(timespec="milliseconds"), evento, tamano, tiempo_ms, detalle])
        self._archivo.flush()

    def aviso(self, texto: str) -> None:
        self._renglon(f"{self._hora()}  {texto}", GRIS)

    def orden(self, tiempo_ms: int, texto: str, tamano: int) -> None:
        self._renglon(f"{self._hora()}  juego {reloj(tiempo_ms):>7}  » {texto}", VERDE)
        self.registrar("orden", tamano, tiempo_ms, texto)
        if self.sonido:
            winsound.Beep(1200, 70)

    def recursos(self, tiempo_ms: int, total: int, tamano: int) -> None:
        self._renglon(f"{self._hora()}  juego {reloj(tiempo_ms):>7}  Σ recursos en stock: {total} (madera + comida + oro + piedra)", AMARILLO)
        self.registrar("recursos", tamano, tiempo_ms, str(total))

    def estado(self, texto: str) -> None:
        if self.consola:
            print("\r" + texto[:119].ljust(119), end="", flush=True)
            self.estado_visible = True

    def cerrar(self) -> None:
        self._limpiar_estado()
        self._archivo.close()


PATRONES_DEL_JUEGO = ("*/savegame/*.aoe2record", "*/savegame/*/*.aoe2record")  # savegame de cada perfil y "multi"


def buscar(carpetas: list[tuple[Path, tuple[str, ...]]]) -> dict[Path, int]:
    """Los .aoe2record de cada carpeta, con su tamaño."""
    encontrados = {}
    for carpeta, patrones in carpetas:
        for patron in patrones:
            for ruta in carpeta.glob(patron):
                try:
                    encontrados[ruta] = ruta.stat().st_size
                except OSError:
                    pass  # se borró o se renombró entre el glob y el stat
    return encontrados


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--jugador", type=int, default=1, help="número de jugador tuyo en la partida (por defecto 1)")
    parser.add_argument("--sin-sonido", action="store_true", help="no pitar con cada orden")
    parser.add_argument("--carpeta", type=Path, action="append", help="otra carpeta donde buscar replays")
    parser.add_argument("--segundos", type=float, help="cortar solo después de N segundos")
    args = parser.parse_args()

    os.system("")  # activa los colores en la consola de Windows
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")

    if args.carpeta:
        carpetas = [(c, ("*.aoe2record",)) for c in args.carpeta]
    else:
        # Por si el juego escribe la grabación en otro lado y la mueve al final, también se mira TEMP.
        carpetas = [(CARPETA_JUEGO, PATRONES_DEL_JUEGO), (Path(os.environ["TEMP"]), ("*.aoe2record",))]
    registro = CARPETA_REGISTROS / f"{datetime.now():%Y%m%d-%H%M%S}.csv"
    salida = Salida(args.jugador, not args.sin_sonido, registro)

    print("BoCoach · replay en vivo")
    for carpeta, _ in carpetas:
        print(f"  Busco replays en: {carpeta}")
    print(f"  Muestro solo las órdenes del jugador {args.jugador} (se cambia con --jugador N).")
    print(f"  Registro: {registro}")
    print("  Arrancá una partida. Cada orden tuya que aparezca suena con un pitido. Ctrl+C para salir.\n")

    al_arrancar = buscar(carpetas)
    tamanos = dict(al_arrancar)
    activo: Replay | None = None
    ultimo_crecimiento: float | None = None
    crecimientos: deque[tuple[float, int]] = deque()
    inicio = time.monotonic()

    try:
        while args.segundos is None or time.monotonic() - inicio < args.segundos:
            ahora = time.monotonic()
            actuales = buscar(carpetas)
            nuevos = actuales.keys() - tamanos.keys()
            for ruta in nuevos:
                salida.aviso(f"Archivo nuevo: {ruta.name} ({actuales[ruta]:,} bytes)")
            crecidos = [r for r, t in actuales.items() if t != tamanos.get(r)]
            tamanos = actuales

            if crecidos:
                ruta = activo.ruta if activo and activo.ruta in crecidos else max(crecidos, key=lambda r: actuales[r])
                if activo is None or activo.ruta != ruta:
                    activo = Replay(ruta, ya_estaba=ruta in al_arrancar)
                    crecimientos.clear()
                    salida.aviso(f"Se está escribiendo: {ruta}")
                ultimo_crecimiento = ahora
                crecimientos.append((ahora, actuales[ruta]))
                try:
                    activo.leer(salida)
                except OSError as e:
                    if activo.problema is None:
                        activo.problema = f"No puedo abrirlo ({e}); sigo mostrando solo el tamaño."
                        salida.aviso(activo.problema)
                salida.registrar("crece", actuales[ruta], activo.tiempo)

            while crecimientos and ahora - crecimientos[0][0] > 5:
                crecimientos.popleft()
            if activo is None:
                salida.estado(f"{datetime.now():%H:%M:%S}  Esperando que algún replay empiece a crecer...")
            else:
                tamano = tamanos.get(activo.ruta, 0)
                ritmo = (crecimientos[-1][1] - crecimientos[0][1]) / max(crecimientos[-1][0] - crecimientos[0][0], 1) if len(crecimientos) > 1 else 0
                hace = ahora - ultimo_crecimiento if ultimo_crecimiento else 0
                salida.estado(
                    f"{datetime.now():%H:%M:%S}  {tamano:,} bytes · {ritmo:,.0f} B/s · el archivo llega al "
                    f"{reloj(activo.tiempo)} del juego · última escritura hace {hace:.1f} s"
                    + (" · terminada" if activo.terminado else "")
                )
            time.sleep(INTERVALO)
    except KeyboardInterrupt:
        pass
    finally:
        salida.cerrar()
        print(f"\nRegistro guardado en {registro}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
