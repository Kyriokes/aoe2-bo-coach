# Genera el mod local "BoCoach HUD" a partir de los paneles que el juego usa hoy (los del mod activo con el
# número de prioridad más bajo, o los originales) en cada perfil que usa mods. Cambia dos cosas para leer mejor:
#   - resourcepanel.json: los aldeanos de cada recurso con la letra, el tamaño y el color del stock de ese
#     recurso (más un borde negro para despegarlos del ícono), así los lee la letra "stock". Posiciones iguales.
#   - worldtimerpanel.json: un rectángulo negro liso detrás del reloj; el texto no cambia (sigue la letra "clock").
# Si el mod ya existe, parte de sus propios paneles (así no cambian las posiciones ni la calibración aunque se
# hayan apagado los mods de los que salió). Con -Regenerar vuelve a partir de los mods activos.
param([string]$Juego = 'H:\Steam\steamapps\common\AoE2DE', [switch]$Regenerar)
$ErrorActionPreference = 'Stop'
$nombre = 'BoCoach HUD'

function Panel-Activo([string]$mods, $activos, [string]$archivo) {
    foreach ($m in $activos) {
        $p = [IO.Path]::Combine($mods, ($m.Path -replace '/+', '\'), 'widgetui', $archivo)
        if (Test-Path -LiteralPath $p) { return $p }
    }
    Join-Path $Juego "widgetui\$archivo"
}

function Origen([string]$base) {
    if ($base.StartsWith($Juego)) { 'original' } else { Split-Path -Leaf (Split-Path -Parent (Split-Path -Parent $base)) }
}

function Hijos($w) { foreach ($c in @($w.ChildWidgets)) { if ($c) { $c.Widget } } }
function Todos($w) { $w; foreach ($h in @(Hijos $w)) { Todos $h } }
function Leer([string]$p) { [IO.File]::ReadAllText($p) | ConvertFrom-Json }
function Color($r, $g, $b) { [pscustomobject][ordered]@{ r = $r; g = $g; b = $b; a = 1 } }

function Panel-Recursos([string]$base) {
    $j = Leer $base
    $widgets = @(foreach ($w in $j.Collection.Widgets) { Todos $w.Widget })
    $cambiados = 0
    foreach ($recurso in 'Wood', 'Food', 'Gold', 'Stone') {
        $stock = @($widgets | Where-Object { $_.Name -eq "${recurso}Storage" })[0]
        $icono = @($widgets | Where-Object { $_.Name -eq $recurso })[0]
        if (-not $stock -or -not $icono) { throw "No encontré $recurso en $base" }
        $letra = $stock.StateMaterials.StateNormal.Font
        foreach ($h in @(Hijos $icono | Where-Object { $_.Name -eq 'Workers' })) {
            $h.StateMaterials = [pscustomobject]@{ StateNormal = [pscustomobject]@{ Font = [pscustomobject][ordered]@{
                FontIndex        = $letra.FontIndex
                PointSize        = $letra.PointSize
                Style            = $letra.Style
                TextColor        = Color 1 1 1
                TextOutlineWidth = 0.05
                TextOutlineColor = Color 0 0 0
            } } }
            $cambiados++
        }
    }
    if ($cambiados -ne 4) { throw "Esperaba 4 contadores de aldeanos y encontré $cambiados en $base" }
    $j
}

function Panel-Reloj([string]$base) {
    $j = Leer $base
    $reloj = @($j.Collection.Widgets | ForEach-Object { $_.Widget } | Where-Object { $_.Name -eq 'WorldTimer' })[0]
    $etiqueta = @(Hijos $reloj | Where-Object { $_.Name -eq 'WorldTimerLabel' })[0]
    if (-not $etiqueta) { throw "No encontré el reloj en $base" }
    $fondo = [pscustomobject]@{ Widget = [pscustomobject][ordered]@{
        Type           = 'Base'
        Name           = 'BoCoachClockBackground'
        ViewPort       = [pscustomobject][ordered]@{ xorigin = -10; yorigin = 6; width = 220; height = 52; alignment = 'TopLeft' }
        StateMaterials = [pscustomobject]@{ StateNormal = [pscustomobject][ordered]@{ Material = 'FlatColorWithAlpha'; Color = Color 0 0 0 } }
        ChildWidgets   = @()
    } }
    $etiqueta | Add-Member -NotePropertyName ZPlaneLocalOffset -NotePropertyValue 1 -Force
    $reloj.ChildWidgets = @($fondo) + @($reloj.ChildWidgets | Where-Object { $_.Widget.Name -ne 'BoCoachClockBackground' })
    $j
}

$perfiles = Get-ChildItem (Join-Path $env:USERPROFILE 'Games\Age of Empires 2 DE') -Directory
$utf8 = New-Object Text.UTF8Encoding $false
$instalados = 0
foreach ($perfil in $perfiles) {
    $mods = Join-Path $perfil.FullName 'mods'
    $estado = Join-Path $mods 'mod-status.json'
    if (-not (Test-Path -LiteralPath $estado)) { continue }
    $lista = @((Leer $estado).Mods)
    if ($lista.Count -eq 0) { continue }
    $activos = $lista | Where-Object { $_.Enabled -and $_.Path -notmatch [regex]::Escape($nombre) } | Sort-Object Priority

    $destino = Join-Path $mods "local\$nombre"
    $propio = Join-Path $destino 'widgetui'
    $baseRecursos, $baseReloj = foreach ($archivo in 'resourcepanel.json', 'worldtimerpanel.json') {
        $p = Join-Path $propio $archivo
        if (-not $Regenerar -and (Test-Path -LiteralPath $p)) { $p } else { Panel-Activo $mods $activos $archivo }
    }
    New-Item -ItemType Directory -Force (Join-Path $destino 'widgetui') | Out-Null
    [IO.File]::WriteAllText((Join-Path $destino 'widgetui\resourcepanel.json'), ((Panel-Recursos $baseRecursos) | ConvertTo-Json -Depth 100), $utf8)
    [IO.File]::WriteAllText((Join-Path $destino 'widgetui\worldtimerpanel.json'), ((Panel-Reloj $baseReloj) | ConvertTo-Json -Depth 100), $utf8)
    $info = [ordered]@{
        Author      = 'BoCoach'
        CacheStatus = 0
        Description = "Aldeanos de cada recurso con la letra del stock y fondo liso detrás del reloj, para que BoCoach los lea. Basado en: $(Origen $baseRecursos) (recursos), $(Origen $baseReloj) (reloj)"
        Title       = $nombre
    }
    [IO.File]::WriteAllText((Join-Path $destino 'info.json'), ($info | ConvertTo-Json), $utf8)
    "Perfil $($perfil.Name): mod en $destino (recursos: $(Origen $baseRecursos); reloj: $(Origen $baseReloj))"
    $instalados++
}
if ($instalados -eq 0) { 'Ningún perfil usa mods: abrí el juego, entrá a Mods una vez y volvé a correrlo.' }
