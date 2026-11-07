"""Arma el ícono de la app (src/BoCoach.App/BoCoach.ico) y el de la web (web/src/app/favicon.ico): una torre de castillo dorada sobre el fondo oscuro, con los
colores de la web (--panel y --amber en web/src/app/globals.css). Se dibuja grande y se achica a cada tamaño, así los
bordes salen suaves; el de 16 px se dibuja aparte, sin la puerta, porque achicada no se distingue.

Uso, con Pillow instalado: python tools/icono/icono.py [carpeta para ver los PNG]
"""

import sys
from pathlib import Path

from PIL import Image, ImageDraw

REPO = Path(__file__).resolve().parents[2]
FONDO = "#2b2622"
BORDE = "#5c5046"
DORADO = "#e8c26a"
LADO = 1024
TAMANIOS = [16, 24, 32, 48, 64, 128, 256]
TAMANIOS_WEB = [16, 32, 48, 256]


def dibujar(con_puerta: bool = True) -> Image.Image:
    im = Image.new("RGBA", (LADO, LADO), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    d.rounded_rectangle((24, 24, LADO - 24, LADO - 24), radius=210, fill=FONDO, outline=BORDE, width=28)
    # Las tres almenas, la parte de arriba y el cuerpo de la torre.
    for x in (262, 457, 652):
        d.rectangle((x, 186, x + 110, 316), fill=DORADO)
    d.rectangle((262, 300, 762, 420), fill=DORADO)
    d.rectangle((332, 400, 692, 850), fill=DORADO)
    if con_puerta:
        d.rectangle((452, 690, 572, 850), fill=FONDO)
        d.ellipse((452, 630, 572, 750), fill=FONDO)
        d.rectangle((492, 500, 532, 590), fill=FONDO)
    return im


def main() -> None:
    grande, chico = dibujar(), dibujar(con_puerta=False)
    imagenes = [(chico if n <= 16 else grande).resize((n, n), Image.LANCZOS) for n in TAMANIOS]
    salida = REPO / "src" / "BoCoach.App" / "BoCoach.ico"
    imagenes[-1].save(salida, format="ICO", sizes=[(n, n) for n in TAMANIOS], append_images=imagenes[:-1])
    print(f"{salida} ({', '.join(map(str, TAMANIOS))} px)")
    # El de la pestaña del navegador. Solo favicon.ico: el proxy de idiomas (web/src/proxy.ts) deja pasar ese y nada más.
    web = [im for n, im in zip(TAMANIOS, imagenes) if n in TAMANIOS_WEB]
    favicon = REPO / "web" / "src" / "app" / "favicon.ico"
    web[-1].save(favicon, format="ICO", sizes=[(n, n) for n in TAMANIOS_WEB], append_images=web[:-1])
    print(f"{favicon} ({', '.join(map(str, TAMANIOS_WEB))} px)")
    if len(sys.argv) > 1:
        carpeta = Path(sys.argv[1])
        carpeta.mkdir(parents=True, exist_ok=True)
        for n, im in zip(TAMANIOS, imagenes):
            im.save(carpeta / f"icono-{n}.png")


if __name__ == "__main__":
    main()
