import json

import numpy as np

from salida import a_entradas, escribir


def test_a_entradas_forma_y_rango():
    rng = np.random.default_rng(2)
    p = rng.uniform(0, 1, (2, 5, 6, 6))
    desde, hasta = p * 0.9, np.minimum(p * 1.1, 1)
    n = rng.integers(0, 500, (2, 5, 6, 6))
    entradas = a_entradas(["britons|franks", "celts|goths"], p, desde, hasta, n)

    assert list(entradas) == ["britons|franks", "celts|goths"]
    for valores in entradas.values():
        assert len(valores) == 720
        assert all(isinstance(v, int) for v in valores)
        casilleros = np.array(valores).reshape(5, 6, 6, 4)
        assert ((casilleros[..., :3] >= 0) & (casilleros[..., :3] <= 1000)).all()
    primero = np.array(entradas["britons|franks"]).reshape(5, 6, 6, 4)[0, 0, 0]
    assert primero.tolist() == [round(p[0, 0, 0, 0] * 1000), round(desde[0, 0, 0, 0] * 1000),
                                round(hasta[0, 0, 0, 0] * 1000), int(n[0, 0, 0, 0])]


def test_escribir_una_entrada_por_renglon(tmp_path):
    ruta = tmp_path / "matchup_stats.json"
    uno = {"celts|goths": [1, 2, 3, 4], "britons|franks": [5, 6, 7, 8]}
    escribir(ruta, {"schema_version": 1, "generated": "2026-10-25"}, uno, {"franks": [9, 10, 11, 12]})

    datos = json.loads(ruta.read_text(encoding="utf-8"))
    assert datos["schema_version"] == 1
    assert datos["one_vs_one"] == uno
    assert datos["team"] == {"franks": [9, 10, 11, 12]}
    renglones = ruta.read_bytes().decode("utf-8").split("\n")
    assert '    "britons|franks": [5,6,7,8],' in renglones
    assert '    "celts|goths": [1,2,3,4]' in renglones
    assert renglones.index('    "britons|franks": [5,6,7,8],') < renglones.index('    "celts|goths": [1,2,3,4]')
    assert b"\r\n" not in ruta.read_bytes()
    assert ruta.read_bytes().endswith(b"}\n")
