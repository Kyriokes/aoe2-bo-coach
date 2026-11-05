# Controla la versión portable recién publicada, sin abrirla. Un archivo de data sin ExcludeFromSingleFile en
# BoCoach.App.csproj queda adentro del .exe y la app no lo encuentra, con todos los tests en verde; uno nuevo que nadie
# agregó al csproj directamente no viaja. Lo corren ci.yml y release.yml después de dotnet publish.
param(
    [Parameter(Mandatory = $true)][string]$Salida,    # la carpeta de dotnet publish -o
    [string]$Repo = '.'                               # la raíz del repo (la que tiene data/)
)
$ErrorActionPreference = 'Stop'

# Lo que está en data/ y no va con la app, a propósito: las donaciones son de la web (web/scripts/build-coach.mjs).
$soloWeb = @('donaciones.json')

$faltan = New-Object System.Collections.Generic.List[string]
foreach ($archivo in 'BoCoach.exe', 'LEEME.txt', 'LICENSE.txt', 'THIRD-PARTY-NOTICES.txt', 'THIRD-PARTY-NOTICES-dotnet.txt') {
    if (-not (Test-Path -LiteralPath (Join-Path $Salida $archivo) -PathType Leaf)) { $faltan.Add($archivo) }
}

$data = (Resolve-Path -LiteralPath (Join-Path $Repo 'data')).Path
$controlados = 0
foreach ($archivo in Get-ChildItem -LiteralPath $data -Filter '*.json' -Recurse -File) {
    $relativa = $archivo.FullName.Substring($data.Length).TrimStart('\', '/')
    if ($soloWeb -contains $relativa) { continue }
    $controlados++
    if (-not (Test-Path -LiteralPath (Join-Path (Join-Path $Salida 'data') $relativa) -PathType Leaf)) { $faltan.Add("data\$relativa") }
}

if ($faltan.Count -gt 0) {
    foreach ($archivo in $faltan) { Write-Output "::error::Falta en la versión portable: $archivo" }
    Write-Output 'Un archivo de data tiene que estar en un <None ... ExcludeFromSingleFile="true" /> de BoCoach.App.csproj (o en $soloWeb de este script, si no va con la app).'
    exit 1
}
Write-Output "Versión portable completa: BoCoach.exe, LEEME, licencias y $controlados archivos de data."
exit 0
