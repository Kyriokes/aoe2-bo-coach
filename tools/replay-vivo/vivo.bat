@echo off
rem Muestra en vivo lo que el juego escribe en el replay. La primera vez prepara el entorno de Python (.venv).
setlocal
cd /d "%~dp0"
if not exist ".venv\Scripts\python.exe" (
    echo Preparando el entorno de Python la primera vez, tarda un minuto...
    python -m venv .venv || goto :error
    ".venv\Scripts\python.exe" -m pip install --quiet --disable-pip-version-check -r requirements.txt || goto :error
)
".venv\Scripts\python.exe" -I vivo.py %*
pause
exit /b 0

:error
echo No se pudo preparar el entorno. Fijate que Python este instalado (python --version).
pause
exit /b 1
