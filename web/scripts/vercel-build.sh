#!/usr/bin/env bash
# Build en Vercel: la imagen de Vercel no trae .NET, y hace falta para compilar el Core a WebAssembly
# (scripts/build-coach.mjs). Se instala el SDK de global.json en la carpeta del usuario y después se corre el
# build normal de Next (que compila el Core en el prebuild).
set -euo pipefail

export DOTNET_ROOT="$HOME/.dotnet"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1
if [ ! -x "$DOTNET_ROOT/dotnet" ]; then
  # Sin fijar su SHA-256 a propósito: Microsoft lo actualiza seguido y un hash viejo frenaría cada deploy (PLAN.md, B6).
  # -f: si la descarga da un error, corta acá en vez de correr la página de error como script.
  curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
  bash /tmp/dotnet-install.sh --jsonfile ../global.json --install-dir "$DOTNET_ROOT"
fi
export PATH="$DOTNET_ROOT:$PATH"

npm run build
