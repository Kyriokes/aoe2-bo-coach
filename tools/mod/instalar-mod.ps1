# Genera el mod local "BoCoach HUD" a partir del panel de recursos que el juego usa hoy
# (el del mod activo con el número de prioridad más bajo, o el original). Solo cambia el
# estilo de los aldeanos de cada recurso para que sea igual al del total de aldeanos, así la
# letra "villagers" los lee; las posiciones no cambian y la calibración sigue sirviendo.
# Si cambiás los mods del panel de recursos, volvé a correrlo.
param([string]$Juego = 'H:\Steam\steamapps\common\AoE2DE')
$ErrorActionPreference = 'Stop'
$nombre = 'BoCoach HUD'

function Panel-Activo([string]$mods) {
    $estado = Join-Path $mods 'mod-status.json'
    if (Test-Path -LiteralPath $estado) {
        $activos = ([IO.File]::ReadAllText($estado) | ConvertFrom-Json).Mods |
            Where-Object { $_.Enabled -and $_.Path -notmatch [regex]::Escape($nombre) } |
            Sort-Object Priority
        foreach ($m in $activos) {
            $p = [IO.Path]::Combine($mods, ($m.Path -replace '/+', '\'), 'widgetui', 'resourcepanel.json')
            if (Test-Path -LiteralPath $p) { return $p }
        }
    }
    Join-Path $Juego 'widgetui\resourcepanel.json'
}

function Hijos($w) { foreach ($c in @($w.ChildWidgets)) { if ($c) { $c.Widget } } }
function Todos($w) { $w; foreach ($h in @(Hijos $w)) { Todos $h } }

$perfiles = Get-ChildItem (Join-Path $env:USERPROFILE 'Games\Age of Empires 2 DE') -Directory |
    Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName 'mods') }
if (-not $perfiles) { throw 'No encontré la carpeta de perfiles de AoE2:DE.' }

foreach ($perfil in $perfiles) {
    $mods = Join-Path $perfil.FullName 'mods'
    $base = Panel-Activo $mods
    if ($base.StartsWith($Juego)) {
        "Perfil $($perfil.Name): usa el panel original, donde los aldeanos ya tienen el estilo del total; no hace falta el mod"
        continue
    }
    $j =[IO.File]::ReadAllText($base) | ConvertFrom-Json
    $widgets = @(foreach ($w in $j.Collection.Widgets) { Todos $w.Widget })

    $total = @($widgets | Where-Object { $_.Name -eq 'Population' } | ForEach-Object { Hijos $_ } |
        Where-Object { $_.Name -eq 'Workers' })[0]
    if (-not $total) { throw "No encontré el total de aldeanos en $base" }

    $cambiados = 0
    foreach ($r in @($widgets | Where-Object { $_.Name -in 'Wood', 'Food', 'Gold', 'Stone' })) {
        foreach ($h in @(Hijos $r | Where-Object { $_.Name -eq 'Workers' })) {
            $h.StateMaterials = $total.StateMaterials
            $cambiados++
        }
    }
    if ($cambiados -ne 4) { throw "Esperaba 4 contadores de aldeanos y encontré $cambiados en $base" }

    $destino = Join-Path $mods "local\$nombre"
    New-Item -ItemType Directory -Force (Join-Path $destino 'widgetui') | Out-Null
    $utf8 = New-Object Text.UTF8Encoding $false
    [IO.File]::WriteAllText((Join-Path $destino 'widgetui\resourcepanel.json'), ($j | ConvertTo-Json -Depth 100), $utf8)
    $info = [ordered]@{
        Author      = 'BoCoach'
        CacheStatus = 0
        Description = "Aldeanos de cada recurso con el mismo color que el total, para que BoCoach los lea. Basado en: $(Split-Path -Leaf (Split-Path -Parent (Split-Path -Parent $base)))"
        Title       = $nombre
    }
    [IO.File]::WriteAllText((Join-Path $destino 'info.json'), ($info | ConvertTo-Json), $utf8)
    "Perfil $($perfil.Name): mod en $destino (base: $base)"
}
