# aoe2-bo-coach

Overlay liviano para Age of Empires II: Definitive Edition que lee la pantalla con OCR, sigue una build order, se adapta cuando el jugador se desvía y sugiere cómo redistribuir aldeanos.

Solo lee la pantalla: no lee memoria del proceso, no inyecta código y no envía inputs al juego.

El diseño completo y las fases están en [PLAN.md](PLAN.md).

## Requisitos para desarrollar

- Windows 10 (2004 o posterior) u 11
- SDK de .NET 10 (la versión exacta está en `global.json`)

## Compilar y correr los tests

```bash
dotnet build BoCoach.slnx
dotnet test BoCoach.slnx
```

## Regenerar los datos del juego (una vez por parche)

`data/game_data/v<versión>.json` sale del `.dat` del juego con un script de Python que no se usa durante la partida.

```bash
py -3.12 -m venv tools/extractor/.venv
tools/extractor/.venv/Scripts/python -m pip install -r tools/extractor/requirements.txt
tools/extractor/.venv/Scripts/python tools/extractor/extract.py
```

Por defecto lee `H:\Steam\steamapps\common\AoE2DE\resources\_common\dat\empires2_x2_p1.dat`; se cambia con `--dat`. Para agregar entidades, sumalas a `ENTIDADES` en `extract.py` por su id del `.dat`.

## Estructura

| Proyecto | Target | Qué tiene |
|---|---|---|
| `src/BoCoach.Core` | `net10.0` | Lógica pura: datos del juego, build orders, motor y economía. Sin dependencias de Windows. |
| `src/BoCoach.Vision` | `net10.0-windows10.0.19041.0` | Captura de pantalla, OCR y suavizado. |
| `src/BoCoach.App` | `net10.0-windows10.0.19041.0` | App WPF: overlay, calibración, editor de BO y hotkeys. |
| `tests/BoCoach.Core.Tests` | `net10.0` | Tests de `Core`, incluido uno que verifica que no dependa de Windows. |
| `tests/BoCoach.Vision.Tests` | `net10.0-windows10.0.19041.0` | Tests de `Vision`. |

## Licencia

[MIT](LICENSE)
