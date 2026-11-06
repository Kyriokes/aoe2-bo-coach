# Entorno de desarrollo

Estado: vigente lo de "La PC de hoy"; lo de abajo son notas de la PC anterior que todavía pueden servir.

## La PC de hoy (desde el 2026-11-05)

- Windows 11 Pro (build 26300), en inglés (`en-US`). Ryzen 7 5700X (8 núcleos, 16 hilos), 16 GB. Dos monitores de 1920×1080: DISPLAY1 principal en (0,0) y DISPLAY2 a la derecha en (1920,0). En el juego, "Full desktop" se estira a los dos monitores y es injugable: usar "Full screen" o "Windowed".
- OCR de Windows disponible en `en-US` y `es-MX`. Antivirus: Windows Defender (Avast ya no está; lo de Avast de más abajo sirve para jugadores que lo tengan, y está resumido en `docs/LEEME-portable.txt`).
- Repo en `F:\aoe2-bo-coach`. El juego en `G:\SteamLibrary\steamapps\common\AoE2DE` (`tools/extractor/juego.py` lo encuentra solo).
- Herramientas instaladas con winget el 2026-11-05: Git 2.56, .NET SDK 10.0.403 (`global.json` pide 10.0.203 con `latestFeature`), Node 26.13 con npm 11.20 (el CI usa Node 24) y Python 3.12.10.
- La carpeta del repo quedó a nombre del usuario del Windows anterior, así que git la rechaza ("dubious ownership") hasta correr `git config --global --add safe.directory F:/aoe2-bo-coach`. Tampoco hay `user.name` ni `user.email` globales: los commits van con el noreply de GitHub (`106128316+Kyriokes@users.noreply.github.com`).
- Se perdieron `%LOCALAPPDATA%\BoCoach` y `%APPDATA%\BoCoach`: las grabaciones, los ajustes, el token y la identidad de la instalación. **No hay partidas para comparar en esta PC** hasta volver a grabar; las referencias a grabaciones de las notas ya no se pueden correr. Lo que queda son los fixtures de `tests/fixtures`.
- Los `.venv` de `tools/extractor` y `tools/stats` apuntan a un Python que ya no existe: hay que rearmarlos (ver `CONTRIBUTING.md`). `tools/stats/.pytest_cache` quedó sin permisos: git avisa "could not open directory", y es inofensivo. El acceso directo `BoCoach.exe.lnk` de la raíz (ignorado por git) sigue apuntando a `G:`.
- **Trampas de PowerShell 5.1, que se usa en esta sesión:** `[IO.File]` con ruta relativa usa la carpeta del proceso, no la de `Set-Location`: dos veces escribió en la copia principal en vez de la de trabajo; usá rutas absolutas. Comillas dobles adentro de `git commit -m @'...'@` rompen el comando. No mandes `git add` en paralelo con ediciones: agarra archivos a medio escribir. `Set-Content -Encoding utf8` agrega BOM. `$env:X=''` borra la variable.
- **Con la PC cargada:** `dotnet` llegó a dar NU1900 por falta de memoria en la auditoría de NuGet (se esquiva con `-p:NuGetAudit=false`), pytest tardó 8 minutos sin estar colgado y el build de la web 18.
- Las copias de trabajo viejas de `.claude/worktrees` se movieron a `F:\aoe2-bo-coach-worktrees-viejos`: estaban limpias y sus ramas, en git. Colgaban las búsquedas y duplicaban los skills.

## Notas de la PC anterior

Notas en el orden en que se escribieron, movidas tal cual del `CLAUDE.md` viejo el 2026-11-05. Si dos se contradicen manda la más nueva y, siempre, el código; algunas nombran cosas que después cambiaron (por ejemplo, el "editor" de la app se fue en la v0.9.52 y la "biblioteca" local con él). Conviene buscar por el nombre de la clase.

- **Editor del usuario:** Cursor / VS Code (no hay Visual Studio instalado).
- SDK .NET 10.0.203 fijado en `global.json` (también hay 5, 6, 8 y 9 instalados).
- Windows 10 Enterprise LTSC 2021 (build 19044). Dos monitores de 1920×1080: DISPLAY1 principal en (0,0) y DISPLAY2 a la derecha en (1920,0). En el juego, "Full desktop" se estira a los dos monitores y es injugable: usar "Full screen" o "Windowed". OCR de Windows disponible en `en-US` y `es-MX`; el juego está en inglés, así que se usa `en-US`.
- Avast 26.10 (antivirus del usuario) aísla la primera apertura de cada `BoCoach.exe` recién compilado (Auto-Sandbox: lo corre unos 10 s en su caja aislada, lo cierra y lo vuelve a abrir afuera; las aperturas siguientes del mismo archivo pasan). Se ve como "la app se cierra y se vuelve a abrir sola a los 10 s" y no es un error de la app: lo confirma `C:\ProgramData\AVAST Software\Avast\log\autosandbox.log` ("Result: Sandboxing" la primera vez, "Not sandboxing (because the file is in the exception list)" después) y la carpeta `G:\avast! sandbox`. Lo que la app escribe en esos segundos queda en la caja aislada y se pierde. Mientras está aislada se ven dos procesos: el del doble clic queda en pausa y Avast corre una copia que muestra su ventana, la mata a los ~20 s y recién ahí despierta el original. Arreglado el 2026-10-24 con una excepción en Avast (General → Excepciones) para `G:\aoe2-bo-coach\src\BoCoach.App\bin\*`: desde entonces Avast ni lo anota en el log. Lo que se compile fuera de esa carpeta (la versión portable en el scratchpad, ReplaySession) se sigue aislando la primera vez.
- PC del usuario: i5-4590, 4 núcleos, 16 GB. El 3 % de CPU total son unos 60 ms de trabajo cada 0,5 s. La RAM se mide como el Administrador de tareas (memoria privada, `PrivateWorkingSetSize` de `GetProcessMemoryInfo`); `WorkingSet64` incluye las DLL compartidas y da mucho más.
- Docker Desktop está instalado pero su motor WSL2 no arranca ("deploying WSL2 distributions"): no se usa; arreglarlo es cosa del usuario.
- No se puede copiar al disco E: desde acá (permiso denegado): para probar "en otra carpeta" usar el scratchpad.
