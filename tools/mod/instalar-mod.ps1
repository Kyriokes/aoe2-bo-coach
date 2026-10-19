# Respaldo: la app hace lo mismo con el botón "Instalar mod del HUD" (Capture/HudMod.cs), que además instala en los
# perfiles sin lista de mods y avisa si otro mod con más prioridad pisa el panel.
# Genera el mod local "BoCoach HUD" a partir de los paneles que el juego usa hoy (los del mod activo con el
# número de prioridad más bajo, o los originales) en cada perfil que usa mods. Cambia dos cosas para leer mejor:
#   - resourcepanel.json: el ícono de cada recurso y el de inactivos pasan a ser un cuadrado negro con la mitad
#     de arriba del color del recurso (azul en población, rojo en inactivos) y abajo la cantidad de aldeanos
#     grande, en blanco. Los stocks con la misma letra y tamaño; detrás de cada stock y del "N/M" de población,
#     un rectángulo negro. La edad no cambia.
#   - worldtimerpanel.json: el reloj más grande con un rectángulo negro liso detrás.
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

function Plano($r, $g, $b) { [pscustomobject][ordered]@{ Material = 'FlatColorWithAlpha'; Color = Color $r $g $b } }
function Vista($x, $y, $w, $h, $alineacion) { [pscustomobject][ordered]@{ xorigin = $x; yorigin = $y; width = $w; height = $h; alignment = $alineacion } }
function Fijar($w, $nombre, $valor) { $w | Add-Member -NotePropertyName $nombre -NotePropertyValue $valor -Force }

# Cuadrado del recurso: negro, la mitad de arriba del color del recurso y abajo el número grande en blanco.
$puntosNumero = 48
$colores = @{ Wood = 0.55, 0.35, 0.15; Food = 0.9, 0.4, 0.4; Gold = 0.95, 0.75, 0.1; Stone = 0.6, 0.6, 0.65; Population = 0.25, 0.5, 1; Idle = 0.9, 0, 0 }
# El reloj: tamaño de la letra y rectángulo negro que tapa "00:00:00" (medido en una captura: a 40 pt el texto
# ocupa unas 121 unidades del panel).
$puntosReloj = 56
$fondoReloj = @{ x = -10; y = 4; ancho = 200; alto = 74 }

function Letra-Numero($fontIndex) {
    [pscustomobject]@{ StateNormal = [pscustomobject]@{ Font = [pscustomobject][ordered]@{
        FontIndex = $fontIndex; PointSize = $puntosNumero; Style = 'Normal'; TextColor = Color 1 1 1
    } } }
}

function Banda($ancho, $alto, $color) {
    [pscustomobject]@{ Widget = [pscustomobject][ordered]@{
        Type              = 'Base'
        Name              = 'BoCoachBand'
        ViewPort          = Vista 0 0 $ancho $alto 'TopLeft'
        ZPlaneLocalOffset = 1
        StateMaterials    = [pscustomobject]@{ StateNormal = Plano $color[0] $color[1] $color[2] }
        ChildWidgets      = @()
    } }
}

function Panel-Recursos([string]$base) {
    $j = Leer $base
    $widgets = @(foreach ($w in $j.Collection.Widgets) { Todos $w.Widget })
    $fontIndex = @($widgets | Where-Object { $_.Name -eq 'WoodStorage' })[0].StateMaterials.StateNormal.Font.FontIndex
    # Rectángulo negro detrás de cada stock y del "N/M" de población, del alto de los cuadrados y separado de ellos
    # por un poco de panel: así el lector distingue el cuadrado del stock y nunca lee la textura del panel.
    $fondos = @{}
    foreach ($recurso in 'Wood', 'Food', 'Gold', 'Stone', 'Population') {
        $icono = @($widgets | Where-Object { $_.Name -eq $recurso -and $_.Type -eq 'Base' })[0]
        $aldeanos = @(Hijos $icono | Where-Object { $_.Name -eq 'Workers' })[0]
        if (-not $aldeanos) { throw "No encontré los aldeanos de $recurso en $base" }
        $ancho = $icono.ViewPort.width; $alto = $icono.ViewPort.height; $banda = [int]($alto * 0.43)
        if ($recurso -eq 'Population') {
            # La población tiene dos estados (con ícono y sin él): los dos en negro.
            $icono.StateMaterials = [pscustomobject][ordered]@{ UserStates = 2; UserState0 = Plano 0 0 0; UserState1 = Plano 0 0 0 }
            $stock = @($widgets | Where-Object { $_.Name -eq 'PopulationCount' })[0]
            $siguiente = @($widgets | Where-Object { $_.Name -eq 'Idle' })[0]
        } else {
            $icono.StateMaterials = [pscustomobject]@{ StateNormal = Plano 0 0 0 }
            # El stock con la misma letra y tamaño que los aldeanos, hasta el borde del cuadrado siguiente.
            $stock = @($widgets | Where-Object { $_.Name -eq "${recurso}Storage" })[0]
            $siguiente = @($widgets | Where-Object { $_.Type -eq 'Base' -and $_.ViewPort.xorigin -gt $icono.ViewPort.xorigin -and $_.Name -in 'Wood', 'Food', 'Gold', 'Stone', 'Population' } |
                Sort-Object { $_.ViewPort.xorigin })[0]
        }
        $limite = $siguiente.ViewPort.xorigin - [int]($ancho / 2) - 4
        # El fondo arranca un poco después del cuadrado y el texto un poco más adentro, para que no toque el borde.
        $x0 = $icono.ViewPort.xorigin + [int]($ancho / 2) + 5
        $xTexto = [Math]::Max($stock.ViewPort.xorigin, $x0 + 8)
        if ($recurso -eq 'Population') {
            $stock.ViewPort.xorigin = $xTexto
        } else {
            $stock.ViewPort = Vista $xTexto $stock.ViewPort.yorigin ($limite - $xTexto) $puntosNumero 'CentreLeft'
            $stock.StateMaterials = Letra-Numero $fontIndex
        }
        Fijar $stock ZPlaneLocalOffset 2
        $fondos[$stock.Name] = [pscustomobject]@{ Widget = [pscustomobject][ordered]@{
            Type              = 'Base'
            Name              = "BoCoachBack$($stock.Name)"
            ViewPort          = Vista $x0 ($icono.ViewPort.yorigin - [int]($alto / 2)) ($limite - $x0) $alto 'TopLeft'
            ZPlaneLocalOffset = 1
            StateMaterials    = [pscustomobject]@{ StateNormal = Plano 0 0 0 }
            ChildWidgets      = @()
        } }
        $aldeanos.ViewPort = Vista 0 $banda $ancho ($alto - $banda) 'TopLeft'
        $aldeanos.StateMaterials = Letra-Numero $fontIndex
        Fijar $aldeanos TextAnchor 'CentreCentre'
        Fijar $aldeanos ZPlaneLocalOffset 2
        $icono.ChildWidgets = @(Banda $ancho $banda $colores[$recurso]) + @($icono.ChildWidgets | Where-Object { $_.Widget.Name -ne 'BoCoachBand' })
    }
    $fondo = $j.Collection.Widgets[0].Widget
    $fondo.ChildWidgets = @(foreach ($c in @($fondo.ChildWidgets)) {
        if ($c.Widget.Name -like 'BoCoachBack*') { continue }
        if ($fondos.ContainsKey($c.Widget.Name)) { $fondos[$c.Widget.Name] }
        $c
    })

    # Inactivos: el mismo cuadrado que los recursos, a su altura (el botón de KoBHV era alto para la flecha y
    # sin sus texturas el juego estira el ícono original).
    $madera = @($widgets | Where-Object { $_.Name -eq 'Wood' -and $_.Type -eq 'Base' })[0].ViewPort
    $boton = @($widgets | Where-Object { $_.Name -eq 'Idle' })[0]
    $numero = @($widgets | Where-Object { $_.Name -eq 'IdleWorkers' })[0]
    if (-not $boton -or -not $numero) { throw "No encontré el botón de inactivos en $base" }
    $ancho = $madera.width; $alto = $madera.height; $banda = [int]($alto * 0.43)
    $x = $boton.ViewPort.xorigin; $y = $madera.yorigin
    $boton.ViewPort = Vista $x $y $ancho $alto 'CentreCentre'
    $boton.StateMaterials = [pscustomobject][ordered]@{
        StateNormal = Plano 0 0 0; StateHover = Plano 0.25 0.25 0.25; StatePressed = Plano 0.25 0.25 0.25; StateDisabled = Plano 0 0 0
    }
    Fijar $boton ChildWidgets @(Banda $ancho $banda $colores.Idle)
    $numero.ViewPort = Vista ($x - [int]($ancho / 2)) ($y - [int]($alto / 2) + $banda) $ancho ($alto - $banda) 'TopLeft'
    $numero.StateMaterials = Letra-Numero $fontIndex
    Fijar $numero TextAnchor 'CentreCentre'
    $j
}

function Panel-Reloj([string]$base) {
    $j = Leer $base
    $reloj = @($j.Collection.Widgets | ForEach-Object { $_.Widget } | Where-Object { $_.Name -eq 'WorldTimer' })[0]
    $etiqueta = @(Hijos $reloj | Where-Object { $_.Name -eq 'WorldTimerLabel' })[0]
    if (-not $etiqueta) { throw "No encontré el reloj en $base" }
    $etiqueta.StateMaterials.StateNormal.Font.PointSize = $puntosReloj
    $etiqueta.ViewPort.height = $puntosReloj
    # Espectadores e ID de partida van debajo del reloj: se bajan para que el reloj grande no los pise.
    foreach ($w in @(Hijos $reloj | Where-Object { $_.Name -in 'SpectatorsIcon', 'GameIDLabel' })) {
        $w.ViewPort.yorigin = $etiqueta.ViewPort.yorigin + $puntosReloj + 12
    }
    $fondo = [pscustomobject]@{ Widget = [pscustomobject][ordered]@{
        Type           = 'Base'
        Name           = 'BoCoachClockBackground'
        ViewPort       = Vista $fondoReloj.x $fondoReloj.y $fondoReloj.ancho $fondoReloj.alto 'TopLeft'
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
        Description = "Aldeanos de cada recurso e inactivos en grande sobre fondo negro y fondo liso detrás del reloj, para que BoCoach los lea. Basado en: $(Origen $baseRecursos) (recursos), $(Origen $baseReloj) (reloj)"
        Title       = $nombre
    }
    [IO.File]::WriteAllText((Join-Path $destino 'info.json'), ($info | ConvertTo-Json), $utf8)
    "Perfil $($perfil.Name): mod en $destino (recursos: $(Origen $baseRecursos); reloj: $(Origen $baseReloj))"
    $instalados++
}
if ($instalados -eq 0) { 'Ningún perfil usa mods: abrí el juego, entrá a Mods una vez y volvé a correrlo.' }
