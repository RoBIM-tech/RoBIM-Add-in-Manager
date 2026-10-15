$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$output = Join-Path $PSScriptRoot 'RoBIM Manager.extension\RoBIM TEST.tab\TEST_jm.panel\ManagerReload.pushbutton'
foreach ($theme in @('light','dark')) {
    $bitmap = New-Object Drawing.Bitmap 32,32
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.Clear([Drawing.Color]::Transparent)
    $color = if ($theme -eq 'dark') { [Drawing.Color]::FromArgb(121,194,255) } else { [Drawing.Color]::FromArgb(24,96,180) }
    $pen = New-Object Drawing.Pen $color,3
    $pen.StartCap = [Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap = [Drawing.Drawing2D.LineCap]::Round
    $graphics.DrawArc($pen,5,5,22,22,42,282)
    $brush = New-Object Drawing.SolidBrush $color
    $points = [Drawing.PointF[]]@([Drawing.PointF]::new(26,3),[Drawing.PointF]::new(28,13),[Drawing.PointF]::new(18,10))
    $graphics.FillPolygon($brush,$points)
    $graphics.DrawLine($pen,12,17,15,20)
    $graphics.DrawLine($pen,15,20,21,13)
    $name = if ($theme -eq 'dark') { 'icon.dark.png' } else { 'icon.png' }
    $bitmap.Save((Join-Path $output $name),[Drawing.Imaging.ImageFormat]::Png)
    $brush.Dispose(); $pen.Dispose(); $graphics.Dispose(); $bitmap.Dispose()
}
