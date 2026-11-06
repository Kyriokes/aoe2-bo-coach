# Datos del juego

Estado: notas vigentes, con valores comprobados contra el `.dat`. Notas en el orden en que se escribieron, movidas tal cual del `CLAUDE.md` viejo el 2026-11-05. Si dos se contradicen manda la más nueva y, siempre, el código; algunas nombran cosas que después cambiaron (por ejemplo, el "editor" de la app se fue en la v0.9.52 y la "biblioteca" local con él). Conviene buscar por el nombre de la clase.

- Instalación: `H:\Steam\steamapps\common\AoE2DE`
- Archivo de datos: `resources\_common\dat\empires2_x2_p1.dat` (VER 8.9, 63 civs, 1510 techs)
- Se lee con `pip install genieutils-py`:
  ```python
  from genieutils.datfile import DatFile
  d = DatFile.parse(path)
  u = d.civs[0].units[83]                      # aldeano
  u.creatable.resource_costs                   # lista con .type, .amount, .flag
  u.creatable.train_locations                  # lista con .unit_id, .train_time
  t = d.techs[101]                             # paso a Feudal
  t.resource_costs, t.research_locations       # .location_id, .research_time
  ```
- Tipos de recurso: 0 comida, 1 madera, 2 piedra, 3 oro.
- **Mapear por ID, no por nombre:** los nombres internos están desfasados (tech 101 "Middle Age" es el paso a Feudal; tech 102 "Feudal Age" es el paso a Castillos).
- Valores verificados: aldeano (83) 50 comida/25 s; casa (70) 25 madera/25 s; cuartel (12) 175 madera/50 s; molino (68), campamento maderero (562), campamento minero (584) 100 madera/35 s; arquero (4) 25 madera + 45 oro/35 s; telar (22) 50 oro/25 s; Feudal (101) 500 comida/130 s; Castillos (102) 800 comida + 200 oro/160 s.
- Python 3.12 con `genieutils-py` 0.1.2 en el venv `tools/extractor/.venv` (ignorado por git). Parsear el `.dat` tarda unos 30 s.
- En el `.dat`, el TC que se construye es la base 621, que se apila en el TC real 109 (el que da población y entrena aldeanos); al subir de edad pasa a 71/141/142. El extractor los resuelve todos como `town_center`.
- La tasa de granjero del `.dat` es 0,53 (sin caminatas); la efectiva ronda 0,32. El Planner tiene que usar eficiencias por fuente, no la tasa cruda.
- Bonificaciones de civ: el extractor las saca del `.dat` (`civs` en game_data, 62 civs, un valor por edad): recolección, construcción, costo y tiempo de cada entidad (incluidas las techs gratis del árbol de la civ), velocidad de producción de los edificios, población que dan y el arranque estándar (recursos, aldeanos de más, sin casas). Cómo están en el `.dat`: techs con `civ == id` y sin lugar de investigación (requisitos: la edad, o 639/307 = partida estándar con centro urbano), más `tech_tree_id` y `team_bonus_id` de la civ. Los efectos nombran por separado al aldeano hombre y a la mujer, y a cada variante de edad de un edificio (el centro urbano 109 pasa a 71/141/142, siempre desde el original): se evalúa sobre una sola unidad por edad para no contar dos veces. Los multiplicadores de recolección menores a 1 se ignoran (mayas, tártaros, godos: vienen con otro efecto que hace rendir más el recurso). También la carga por viaje y la velocidad de los aldeanos, los recursos al subir de edad (dravidianos, etíopes) y la comida de los lituanos (+100: un anexo vacío del centro urbano, 890, pasa a convertirse al morir en un edificio que guarda 100 de comida, 888).
- `CivBonuses` (Core/Data) y `GameData.Civ(clave)`; "any" o una civ desconocida = `CivBonuses.None`. `PlanStart.For(civ)`: recursos, aldeanos de más (a las ovejas), población del centro urbano y sin tope de casas. El Planner, la economía y la detección de clics usan los costos de la civ en la edad actual. El editor tiene la civ en una lista desplegable; el validador avisa si la civ no existe.
- `data/events.json` lo genera el extractor (594 eventos): plantillas del juego (`resources/en/strings/key-value`, textos 37157-37168) con el nombre que muestra cada entidad (`language_dll_name` por civ, sin lo que va entre paréntesis), para todo lo que alguna civ puede hacer según `resources/_common/dat/CivTechTrees`. Ante una lectura dudosa, el matcher prefiere los eventos de game_data con el nombre de siempre (`EventCatalog.PreferringEntitiesOf`): con el catálogo grande, un "Villager Created" mal leído se parecía a "Slinger" o a "Lancer". El aviso de ataque sale con el color del jugador que ataca y la máscara de texto blanco no lo lee.
- Edificios que valen por varios (`Entity.CountsAs`, `counts_as` en game_data): el Settlement de los incas (unidad 2556 del `.dat`, 125 de madera, 40 s, 5 de población y 10 con la bonificación de incas) cuenta como campamento maderero, campamento minero, molino y casa. `SnapshotBuilder` emite un "X Built" por cada uno (la población la da el propio Settlement), el validador los toma para los requisitos de edad y el Planner como productores. Toda entidad nueva de game_data necesita su nombre en castellano en `StepText` (un test lo exige).

## Dónde está el juego (desde el 2026-11-05)

`tools/extractor/juego.py` lo busca en la variable de entorno `AOE2DE_PATH` y, si no está, en las bibliotecas de Steam (`libraryfolders.vdf`); `tools/mod/instalar-mod.ps1` hace lo mismo. `--dat` y `-Juego` mandan. Los tests corren con el venv de `tools/stats`: `python -m pytest -q ../extractor`. La ruta `H:\Steam` de más arriba es de la PC anterior. Con el `.dat` del parche del 27/10, `game_data` e íconos salen iguales, y `events.es` y `events.mx` difieren (nombres abreviados, sin copiar: ver `docs/dev/build-orders.md`, los hunos con Castillos).
