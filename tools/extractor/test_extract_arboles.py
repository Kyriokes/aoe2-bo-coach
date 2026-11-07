"""Los árboles de tecnologías (CivTechTrees). Se corre con el venv del extractor: .venv\\Scripts\\python -m pytest -q"""

import json

import extract as e


def arbol(carpeta, nombre, centro_urbano_en, ariete_en=3, carretilla_en=2):
    edificios = [
        {"Use Type": "Building", "Node ID": 109, "Name": "Town Center", "Age ID": 1, "Node Status": "ResearchedCompleted"},
        {"Use Type": "Building", "Node ID": 621, "Name": "Town Center", "Age ID": centro_urbano_en, "Node Status": "ResearchedCompleted"},
        {"Use Type": "Building", "Node ID": 12, "Name": "Barracks", "Age ID": 1, "Node Status": "ResearchedCompleted"},
    ]
    unidades = [
        {"Use Type": "Unit", "Node ID": 1258, "Name": "Battering Ram", "Age ID": ariete_en, "Node Status": "ResearchedCompleted"},
        {"Use Type": "Tech", "Node ID": 213, "Name": "Wheelbarrow", "Age ID": carretilla_en, "Node Status": "ResearchedCompleted"},
    ]
    datos = {"civ_id": nombre, "civ_techs_buildings": edificios, "civ_techs_units": unidades}
    (carpeta / f"{nombre}.json").write_text(json.dumps(datos), encoding="utf-8")


ENTIDADES = {"town_center": {"age": "castle"}, "barracks": {"age": "dark"}, "battering_ram": {"age": "castle"}, "wheelbarrow": {"age": "feudal"}}


def test_las_edades_que_cambian_segun_la_civ(tmp_path):
    arbol(tmp_path, "BRITONS", 3)
    arbol(tmp_path, "CUMANS", 2, ariete_en=2)
    arbol(tmp_path, "BURGUNDIANS", 3, carretilla_en=1)
    civs = {"britons": {"dat_name": "British"}, "cumans": {"dat_name": "Cumans"}, "burgundians": {"dat_name": "Burgundians"}}

    assert e.edades_propias_por_civ(e.cargar_edades_por_civ(tmp_path), ENTIDADES, civs) == {
        "cumans": {"battering_ram": "feudal", "town_center": "feudal"},
        "burgundians": {"wheelbarrow": "dark"},
    }


def test_lo_no_disponible_no_cuenta(tmp_path):
    arbol(tmp_path, "CUMANS", 2)
    datos = json.loads((tmp_path / "CUMANS.json").read_text(encoding="utf-8"))
    datos["civ_techs_buildings"][1]["Node Status"] = "NotAvailable"
    (tmp_path / "CUMANS.json").write_text(json.dumps(datos), encoding="utf-8")

    assert e.edades_propias_por_civ(e.cargar_edades_por_civ(tmp_path), ENTIDADES, {"cumans": {"dat_name": "Cumans"}}) == {}
