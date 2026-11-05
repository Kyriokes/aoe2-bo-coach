---
name: nueva-version
description: Cómo cerrar una tanda de cambios de AoE2 BO Coach para que el usuario la pruebe: commits separados, subir el número de versión, compilar en Release, verificar que se copiaron los datos, pushear la rama y dejarle instrucciones. Usala siempre que termines arreglos o funciones que el usuario va a probar jugando, cuando diga "lo buildié y sigue abriendo la versión vieja", pida una versión nueva, o antes de decirle "probalo", aunque no mencione la palabra versión.
---

# Nueva versión para probar

El usuario prueba compilando y jugando. Ve la versión arriba en la ventana (`AppVersion`: número · commit · hora de compilación). Si el número no sube o el commit no coincide, cree que está probando lo viejo; ya pasó con la 0.9.5, cuando nadie había subido el número. Estos pasos evitan eso.

## 1. Commits de los cambios, uno por cada cambio

Regla del usuario: cada cambio va en su propio commit, no se juntan arreglos distintos. Revisá `git status` y `git diff` y separá por tema (lógica, panel, notas).

Commiteá con PowerShell y un here-string de comillas simples (el `'@` de cierre va en la columna 0), con el trailer de `Co-Authored-By` del modelo con el que estés trabajando, sin copiarlo de un commit viejo:

```powershell
git add <archivos del cambio>
git commit -q -m @'
Qué cambió, en castellano y en una línea

Por qué, y en qué partida o grabación se vio, si aplica.

Co-Authored-By: <tu modelo> <noreply@anthropic.com>
'@
```

Sin comillas dobles en el mensaje: Windows PowerShell 5.1 no las escapa al pasárselo a git, que lo parte en varios argumentos (y falla, o se comen las comillas).

Las notas nuevas de `CLAUDE.md` (lo aprendido del juego, decisiones) van en un commit aparte.

## 2. Tests

```powershell
dotnet test BoCoach.slnx -c Release
```

`Directory.Build.props` trata los warnings como errores: un warning nuevo rompe el build y el CI (salvo los avisos de seguridad de NuGet, NU1901 a NU1904, que quedan como avisos). Si algo falla, se arregla antes de seguir; no se sube versión con tests rotos.

## 3. Subir el número

En `src/BoCoach.App/BoCoach.App.csproj`, `<Version>`: sumá 1 al último número (0.9.11 → 0.9.12) salvo que el usuario pida otra cosa. Tiene que ser `N.N.N`: `release.yml` rechaza cualquier otra cosa. Commit solo con ese cambio y mensaje `v0.9.12`. Va después de los otros commits porque `AppVersion` muestra el commit de la compilación: así el commit que ve el usuario es el que tiene todo.

## 4. Compilar en Release y verificar

```powershell
dotnet build src/BoCoach.App -c Release
```

Release y no Debug: si la app está abierta, el `BoCoach.exe` de Debug queda bloqueado. Después verificá que el ejecutable tenga sus datos al lado, en `src/BoCoach.App/bin/Release/net10.0-windows10.0.19041.0/data/`: `game_data`, `events.json`, `fonts`, `icons` y `hud_layout.json` (desde la v0.9.52 las BO no van con la app: vienen de la web).

Si agregaste un tipo de archivo de datos nuevo, tiene que estar en el `ItemGroup` de datos del csproj, con `ExcludeFromSingleFile` para la versión portable. El CI lo controla: `.github/scripts/verificar-portable.ps1` falla si algún `data\**\*.json` no quedó al lado del `.exe` publicado. Si el archivo no va con la app (como `donaciones.json`, que es de la web), sumalo a `$soloWeb` en ese script.

## 5. Pushear la rama

```powershell
git push -q origin <rama de la fase>
```

- A `main` solo cuando el usuario lo pide en el chat (fast-forward). Por iniciativa propia el clasificador de permisos lo bloquea.
- No subas etiquetas `v*`: la release pública sale sola con cada push a `main` que trae una versión nueva (`release.yml` arma el `.zip`, su `.sha256` y la etiqueta), así que publicar es pedir el push a `main`. Por eso a `main` va solo lo que el usuario ya probó.
- El CI corre en cada push; si hace falta, se consulta sin autenticación: `Invoke-RestMethod "https://api.github.com/repos/Kyriokes/aoe2-bo-coach/actions/runs?per_page=3"`.

## 6. Avisarle al usuario

En castellano rioplatense, sin guiones largos y paso a paso:
- qué versión quedó y en qué rama (y que no está en `main` si no la pidió);
- qué cambió, contado desde lo que él ve en la partida, no desde el código;
- cuántos tests pasan;
- qué tiene que hacer: compilar o abrir la versión nueva (comprobando el número arriba en la ventana), volver a instalar el mod si cambió el diseño del HUD, y qué mirar mientras juega y graba.

Lo que solo puede hacer él (jugar, probar en su PC, pedir el push a `main`, publicar) se anota también en `docs/pendientes-usuario.md`.
