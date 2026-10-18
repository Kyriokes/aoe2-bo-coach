---
name: armar-letras-e-iconos
description: Cómo armar o ampliar las letras de los números del HUD (data/fonts/*.json) y la biblioteca de íconos de la cola de producción (data/icons/production_queue.json) de AoE2 BO Coach a partir de grabaciones, con ReplaySession glyphs/font e icons/iconlib/queue. Usala siempre que un número del HUD se lea mal o no se lea (falta un dígito, "33/40" sale "15/40", un reloj con letras raras), cuando la cola de producción muestre "?" o confunda un ícono, cuando cambie el diseño del HUD o del mod, o cuando aparezca una unidad o tecnología nueva en la cola.
---

# Letras del HUD e íconos de la cola

Los números del HUD no se leen con OCR sino comparando cada carácter con plantillas (`data/fonts/<letra>.json`), y la cola de producción comparando cada lugar con una biblioteca de íconos (`data/icons/production_queue.json`). Las dos cosas se arman igual: recortar de grabaciones, agrupar por parecido, etiquetar mirando una imagen y guardar. El paso delicado es etiquetar: una etiqueta mal puesta hace que se lea mal en todas las partidas, y en las imágenes chicas el ojo se equivoca.

Todos los comandos son `dotnet run -c Release --project tools/ReplaySession -- <comando>`; el detalle de cada uno está en el comentario al principio de `tools/ReplaySession/Program.cs`. Las grabaciones están en `%LOCALAPPDATA%\BoCoach\recordings\<aaaammdd-hhmmss>`. Trabajá en el scratchpad y copiá al repo solo el `.json` final.

## Letras de números

1. **Elegir la tinta y la región.**
   - Tintas: `white` (blanco fijo), `white-auto` (blanco con umbral por imagen, para los dos diseños de HUD), `white-on-black` (blanco sobre el negro del mod BoCoach HUD) y `blue` (el total azul).
   - Con el mod, la región puede ser `wood:square` (el número dentro del cuadrado negro) o `wood:stock` (lo de la derecha).
   - Letras actuales con el mod: `mod_numbers` (cuadrados y stocks), `mod_clock` (reloj), `mod_population` ("N/M", con "/"). Las viejas (`stock`, `clock`, `villagers`) son del HUD sin el mod.
2. **Recortar y agrupar.**
   ```bash
   dotnet run -c Release --project tools/ReplaySession -- glyphs "<grab1>;<grab2>" wood:square,food:square white-on-black <dir> --every 4
   ```
   Usá varias grabaciones, mejor de civs con HUD distinto, para que aparezcan todos los dígitos. Escribe `clusters.json` y `clusters*.png` (una fila por grupo, 20 grupos por página).
3. **Etiquetar.**
   - Primero con datos verificados: la golden de `tests/fixtures/rec-*` o capturas donde el valor se confirma por otro lado (el total de aldeanos, el reloj que avanza de a uno). Asigná cada dígito conocido a su grupo más cercano.
   - A ojo solo los grupos que no aparecen en lo verificado. Ojo: en la imagen un "9" grueso parece "0" o "Q", y el "3" y el "5" se confunden en letras chicas.
   - Si un dígito no aparece en ninguna grabación (pasó con el "3" de `mod_population`), no lo inventes: hace falta una partida donde salga.
4. **Guardar.**
   ```bash
   dotnet run -c Release --project tools/ReplaySession -- font <dir> "0=7,1=0,2=:,..." <nombre> <tinta> data/fonts/<nombre>.json
   ```
   Los grupos sin etiqueta (basura, bordes) se descartan.
5. **Probar.** `read <grabación> <región> data/fonts/<nombre>.json --every 20` en grabaciones que no se usaron para armarla. Si baja la seguridad o aparece un carácter cortado, revisá la etiqueta de ese grupo.

## Íconos de la cola de producción

Los íconos miden 33×33 a 1920×1080 y cambian de color con el estado: verde con el progreso, amarillo o naranja esperando, rojo sin población. Por eso un mismo ícono tiene varios grupos (todos con la misma clave). Si espera o avanza no sale de la biblioteca sino del color (`ProductionQueue.IsWaiting`); `queue` lo muestra con "…".

**Si son pocos íconos** (un "?" que aparece, uno confundido con otro), no hace falta reagrupar: `teach` toma el lugar de una captura y renombra el "?" más parecido o lo agrega como ícono nuevo, y muestra cómo se lee la captura después.
```bash
dotnet run -c Release --project tools/ReplaySession -- teach <grabación> <captura> <lugar> <clave> data/icons/production_queue.json
```
El lugar empieza en 0 a la izquierda. Antes mirá la captura (el PNG de la región, o una hoja de contactos con un script `.ps1` en el scratchpad si son varias) para estar seguro de qué es; si no lo podés identificar, dejalo como "?" (no suma edificios ni confunde nada). Para lo que no es un ícono (adornos del panel, mapa) usá la clave `-`.

**Para armar la biblioteca de nuevo:**

1. **Recortar y agrupar.** `iconlib` arma la biblioteca entera desde un solo `clusters.json`, así que para agregar íconos hay que volver a agrupar con las grabaciones de siempre más la nueva, y etiquetar todo de nuevo:
   ```bash
   dotnet run -c Release --project tools/ReplaySession -- icons "<grab1>;<grab2>;<nueva>" <dir> --every 4
   ```
   La biblioteca actual sale de 20261018-011721, 014649 y 021924. Si la fila queda corrida, probá `--offset-y`.
2. **Etiquetar mirando `clusters.png`.**
   - Usá claves de `data/game_data` (`villager`, `archer`, `loom`, `wheelbarrow`, `feudal_age`, `castle_age`, `double_bit_axe`, `horse_collar`, `fletching`...) y `housed` para el ícono rojo de sin población.
   - Etiquetá `-` en todo lo que no es ícono: negro y pedazos de mapa. Son necesarios: sin ellos, el ícono rojo oscuro se confundía con el negro. La lectura corta en el primer `-`.
   - Etiquetá `?` en los íconos que se ven pero no identificás: se muestran como "?" en vez de confundirse con otro.
   - Para orientarte, mirá las etiquetas actuales en `data/icons/production_queue.json` y los íconos que ya se confundieron: la malla verde es la carretilla avanzando, la hoja verde el hacha de doble filo y la pluma naranja el emplumado.
3. **Guardar.** `iconlib <dir> "0=villager,1=-,2=archer,..." data/icons/production_queue.json`.
4. **Probar.** `queue <grabación> data/icons/production_queue.json --every 2` muestra cada cambio de la cola; contrastalo con las capturas y con el log (un aldeano sale, el telar termina). Los tests de `tests/BoCoach.Vision.Tests/ProductionQueueTests.cs` usan capturas de `tests/fixtures/queue-20261018-021924/` con lo esperado.

## Cerrar

- Si se arregló una lectura que estaba mal, sumá un test con las capturas que la muestran (pocas, en `tests/fixtures/<tema>-<grabación>/`) y lo que tiene que leer.
- `dotnet test BoCoach.slnx -c Release`: los golden de las grabaciones viejas no pueden empeorar.
- Un commit por letra o biblioteca, y anotá en `CLAUDE.md` con qué grabaciones se armó y qué se aprendió (como el "3" que faltaba).
- Las letras y los íconos están medidos a 1920×1080. La escala automática es un pendiente; si el usuario juega con otra resolución, avisale que no van a servir.
