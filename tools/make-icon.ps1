param([Parameter(Mandatory=$true)][string]$OutputPath)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
if (-not ('CoolMacIconNative' -as [type])) {
    Add-Type -TypeDefinition 'using System; using System.Runtime.InteropServices; public static class CoolMacIconNative { [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr icon); }'
}
function Add-RoundedShape {
    param($Graphics, [float]$X, [float]$Y, [float]$Width, [float]$Height, [float]$Radius, [string]$Color)
    $shape = New-Object System.Drawing.Drawing2D.GraphicsPath
    $diameter = $Radius * 2
    $shape.AddArc($X, $Y, $diameter, $diameter, 180, 90)
    $shape.AddArc($X + $Width - $diameter, $Y, $diameter, $diameter, 270, 90)
    $shape.AddArc($X + $Width - $diameter, $Y + $Height - $diameter, $diameter, $diameter, 0, 90)
    $shape.AddArc($X, $Y + $Height - $diameter, $diameter, $diameter, 90, 90)
    $shape.CloseFigure()
    $brush = New-Object System.Drawing.SolidBrush ([System.Drawing.ColorTranslator]::FromHtml($Color))
    try { $Graphics.FillPath($brush, $shape) } finally { $brush.Dispose(); $shape.Dispose() }
}
$bitmap = New-Object System.Drawing.Bitmap 64,64
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
try {
    Add-RoundedShape $graphics 0 0 64 64 15 '#18253d'
    Add-RoundedShape $graphics 12 20 12 17 4 '#6099ff'
    Add-RoundedShape $graphics 26 16 12 21 4 '#61d7be'
    Add-RoundedShape $graphics 40 20 12 17 4 '#b39cff'
    Add-RoundedShape $graphics 10 41 44 9 4.5 '#eef4ff'
    $iconHandle = $bitmap.GetHicon()
    try {
        $icon = [System.Drawing.Icon]::FromHandle($iconHandle)
        $file = [System.IO.File]::Create($OutputPath)
        try { $icon.Save($file) } finally { $file.Dispose(); $icon.Dispose() }
    } finally { [CoolMacIconNative]::DestroyIcon($iconHandle) | Out-Null }
} finally { $graphics.Dispose(); $bitmap.Dispose() }
