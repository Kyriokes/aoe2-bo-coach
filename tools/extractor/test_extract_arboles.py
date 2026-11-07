"""Los árboles de tecnologías (CivTechTrees). Se corre con el venv del extractor: .venv\\Scripts\\python -m pytest -q"""

import json

import extract as e


def arbol(carpeta, nombre, centro_urbano_en):
    nodos = [
        {"Use Type": "Building", "Node ID": 109, "Name": "Town Center", "Age ID": 1, "Node Status": "ResearchedCompleted"},
        {"Use Type": "Building", "Node ID": 621, "Name": "Town Center", "Age ID": centro_urbano_en, "Node Status": "ResearchedCompleted"},
        {"Use Type": "Building", "Node ID": 12, "Name": "Barracks", "Age ID": 1, "Node Status": "ResearchedCompleted"},
    ]
    (carpeta / f"{nombre}.json").write_text(json.dumps({"civ_id": nombre, "civ_techs_buildings": nodos}), encoding="utf-8")


def test_el_centro_urbano_de_los_cumanos_es_de_feudal(tmp_path):
    arbol(tmp_path, "BRITONS", 3)
    arbol(tmp_path, "CUMANS", 2)
    entidades = {"town_center": {"age": "castle"}, "barracks": {"age": "dark"}}
    civs = {"britons": {"dat_name": "British"}, "cumans": {"dat_name": "Cumans"}}

    assert e.edades_propias_por_civ(e.cargar_edades_de_edificios(tmp_path), entidades, civs) == {"cumans": {"town_center": "feudal"}}


def test_un_edificio_no_disponible_no_cuenta(tmp_path):
    arbol(tmp_path, "CUMANS", 2)
    datos = json.loads((tmp_path / "CUMANS.json").read_text(encoding="utf-8"))
    datos["civ_techs_buildings"][1]["Node Status"] = "NotAvailable"
    (tmp_path / "CUMANS.json").write_text(json.dumps(datos), encoding="utf-8")

    assert e.edades_propias_por_civ(e.cargar_edades_de_edificios(tmp_path), {"town_center": {"age": "castle"}},
                                    {"cumans": {"dat_name": "Cumans"}}) == {}
