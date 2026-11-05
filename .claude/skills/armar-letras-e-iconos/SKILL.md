---
name: armar-letras-e-iconos
description: Cómo armar o ampliar las letras de los números del HUD (data/fonts/*.json) y la biblioteca de íconos de la cola de producción (data/icons/production_queue.json, con el catálogo queue_game.json) de AoE2 BO Coach a partir de grabaciones, con ReplaySession glyphs/font y teach/queue. Usala siempre que un número del HUD se lea mal o no se lea (falta un dígito, "33/40" sale "15/40", un reloj con letras raras), cuando la cola de producción muestre "?" o confunda un ícono, cuando cambie el diseño del HUD o del mod, o cuando aparezca una unidad o tecnología nueva en la cola.
---

# Letras del HUD e íconos de la cola

Los números del HUD no se leen con OCR sino comparando cada carácter con plantillas (`data/fonts/<letra>.json`), y la cola de producción comparando cada lugar con muestras reales (`data/icons/production_queue.json`) y, después, con los íconos de los archivos del juego (`data/icons/queue_game.json`). Las letras se arman recortando de grabaciones, agrupando por parecido, etiquetando mirando una imagen y guardando; los íconos, casi siempre, enseñando de a uno con `teach`. El paso delicado es etiquetar: una etiqueta mal puesta hace que se lea mal en todas las partidas, y en las imágenes chicas el ojo se equivoca.

Todos los comandos son `dotnet run -c Release --project tools/ReplaySession -- <comando>`, desde la raíz del repo; el detalle de cada uno está en el comentario al principio de `tools/ReplaySession/Program.cs`. Las grabaciones están en `%LOCALAPPDATA%\BoCoach\recordings\<aaaammdd-hhmmss>`. Trabajá en el scratchpad y copiá al repo solo el `.json` final.

## Letras de números

1. **Elegir la tinta y la región.**
   - Tintas: `white` (blanco fijo), `white-auto` (blanco con umbral por imagen, para los dos diseños de HUD), `white-on-black` (blanco sobre fondo oscuro: las letras del mod) y `blue` (el total azul del HUD sin el mod).
   - Con el diseño del mod (`data/hud_layout.json`; grabaciones con `hud_layout` en `session.json`), la región es `celda:campo`: `wood:villagers`, `wood:stock` (también comida, oro y piedra), `population:total`, `population:cap`, `idle:count`.
   - Con el mod viejo, `wood:square` (el número dentro del cuadrado negro) o `wood:stock` (lo de la derecha). Sin el mod, el nombre de la región (`clock`, `wood`...).
   - Letras actuales:
     - `hud_numbers`: los números del diseño propio (48 pt, dígitos y "/"), armada con la 20261025-024356.
     - `mod_clock`: el reloj del mod (dígitos, ":", "(" y la "N" de "(Normal"). Si no da una hora, se prueba `clock`, que también lee el reloj grande sobre la textura.
     - `mod_numbers` y `mod_population`: el mod viejo, para lo que no tiene el casillero del diseño.
     - `stock`, `clock` y `villagers`: el HUD sin el mod.
2. **Recortar y agrupar.**
   ```powershell
   dotnet run -c Release --project tools/ReplaySession -- glyphs "<grab1>;<grab2>" wood:stock,food:stock white-on-black <dir> --every 4
   ```
   Usá varias grabaciones, mejor de civs con HUD distinto, para que aparezcan todos los dígitos. Escribe `clusters.json` y `clusters*.png` (una fila por grupo, 20 grupos por página).
3. **Etiquetar.**
   - Primero con datos verificados: la golden de `tests/fixtures/rec-*` y `tests/fixtures/hud-*`, o capturas donde el valor se confirma por otro lado (el total de aldeanos, el reloj que avanza de a uno). Asigná cada dígito conocido a su grupo más cercano.
   - A ojo solo los grupos que no aparecen en lo verificado. Ojo: en la imagen un "9" grueso parece "0" o "Q", y el "3" y el "5" se confunden en letras chicas.
   - Si un dígito no aparece en ninguna grabación (pasó con el "3" de `mod_population`), no lo inventes: hace falta una partida donde salga.
4. **Guardar.**
   ```powershell
   dotnet run -c Release --project tools/ReplaySession -- font <dir> "0=7,1=0,2=:,..." <nombre> <tinta> data/fonts/<nombre>.json
   ```
   Los grupos sin etiqueta (basura, bordes) se descartan.
5. **Probar.** `read <grabación> <región> data/fonts/<nombre>.json --every 20` en grabaciones que no se usaron para armarla. Si baja la seguridad o aparece un carácter cortado, revisá la etiqueta de ese grupo. Las letras se comparan normalizadas por alto, pero un tamaño de letra mucho más chico se lee peor: el mod usa 48 pt a propósito.

Las letras y los íconos están medidos a 1920×1080 y sirven a otra resolución: la calibración sola mide la escala del HUD (`hud_scale` en `regions.json` y en `session.json`) y cada región se lleva a 1080p antes de leerla (`CapturedImage.AtHudScale`), también al leer una grabación. No hace falta una letra por resolución. Nadie jugó todavía a otra resolución: los tests agrandan capturas de 1080p.

## Íconos de la cola de producción

Los íconos miden 33×33 a 1920×1080 y cambian de color con el estado: verde con el progreso, amarillo o naranja esperando, rojo sin población. Por eso un mismo ícono tiene varias muestras (todas con la misma clave). Si espera o avanza no sale de la biblioteca sino del color (`ProductionQueue.IsWaiting`); `queue` lo muestra con "…".

Hay dos fuentes y las muestras reales ganan a igual parecido:

- `production_queue.json`: muestras reales de las grabaciones, con `-` (lo que no es un ícono) y `?` (íconos vistos sin identificar).
- `queue_game.json`: los íconos de los archivos del juego, resumidos en 11×11 colores. Lo genera `tools/extractor/queue_icons.py` (se vuelve a correr si un parche trae unidades o tecnologías nuevas) y `QueueIconCatalog` los tiñe al cargar. Por eso una unidad nueva suele leerse sin hacer nada: mirá primero con `queue` qué sale antes de enseñar algo.

**Lo normal es `teach`:** toma el lugar de una captura y renombra el "?" más parecido o lo agrega como muestra nueva, y muestra cómo se lee la captura después.
```powershell
dotnet run -c Release --project tools/ReplaySession -- teach <grabación> <captura> <lugar> <clave> data/icons/production_queue.json [--row N]
```
El lugar empieza en 0 a la izquierda. Antes mirá la captura (el PNG de la región, o una hoja de contactos con un script `.ps1` en el scratchpad si son varias) para estar seguro de qué es; si no lo podés identificar, dejalo como "?" (no suma edificios ni confunde nada). Para lo que no es un ícono (adornos del panel, mapa) usá la clave `-`. Las claves son las de `data/game_data` y `data/events.json` (`crossbowman`, `bodkin_arrow`), más `housed` para el ícono rojo de sin población.

Antes de borrar a mano una entrada de la biblioteca, probá las otras grabaciones: una muestra sacada puede ser la que reconocía otra cosa. Y al partir el texto por `},{"key"`, si se borra la última se va el `}]` del final.

**No uses `iconlib` para sumar íconos.** Arma la biblioteca entera desde un solo `clusters.json` y pisa todo lo enseñado con `teach`, que es la mayor parte de lo que hay (163 muestras), de grabaciones que quizá ya no estén. Solo sirve para empezar de cero, y entonces hay que volver a enseñar todo:

1. `icons "<grab1>;<grab2>;<nueva>" <dir> --every 4` recorta y agrupa (si la fila queda corrida, `--offset-y`).
2. Etiquetá mirando `clusters.png`, con las mismas claves. Etiquetá `-` en todo lo que no es ícono, negro y pedazos de mapa: sin ellos, el ícono rojo oscuro se confundía con el negro. Para orientarte, mirá las etiquetas actuales y los que ya se confundieron: la malla verde es la carretilla avanzando, la hoja verde el hacha de doble filo y la pluma naranja el emplumado.
3. `iconlib <dir> "0=villager,1=-,2=archer,..." <salida.json>`, a una salida en el scratchpad, y comparala con la de ahora antes de reemplazarla.

**Probar.** `queue <grabación> data/icons/production_queue.json --every 2` muestra cada cambio de la cola (`--no-game` lee sin el catálogo; `--detail [--features]` muestra cada lugar con su distancia). Contrastalo con las capturas y con el log, que es independiente: lo correcto se juzga contra los `done` del registro con fuente `Log` (un aldeano sale, el telar termina), no a ojo. Para comparar dos bibliotecas, copiá también `queue_game.json` al lado de la otra: sin él `IconLibrary` no carga el catálogo y las lecturas no son comparables. Los tests de `tests/BoCoach.Vision.Tests/ProductionQueueTests.cs` usan capturas de `tests/fixtures/queue-*/` con lo esperado.

## Cerrar

- Si se arregló una lectura que estaba mal, sumá un test con las capturas que la muestran (pocas, en `tests/fixtures/<tema>-<grabación>/`) y lo que tiene que leer.
- Corré `dotnet test BoCoach.slnx -c Release`: los golden de las grabaciones viejas no pueden empeorar.
- Un commit por letra o biblioteca, y anotá en `CLAUDE.md` con qué grabaciones se armó y qué se aprendió (como el "3" que faltaba).
