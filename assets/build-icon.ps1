$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
# Vector geometry matches robim-manager.svg. PNG-compressed ICO frames preserve alpha at each Windows size.
$images = @()
foreach ($size in @(16,20,24,32,40,48,64,128,256)) {
    $bitmap = New-Object Drawing.Bitmap $size,$size
    $g = [Drawing.Graphics]::FromImage($bitmap)
    $g.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.ScaleTransform(($size / 256.0),($size / 256.0))
    $navy = New-Object Drawing.SolidBrush ([Drawing.ColorTranslator]::FromHtml('#14283f'))
    $white = New-Object Drawing.SolidBrush ([Drawing.Color]::White)
    $mint = New-Object Drawing.SolidBrush ([Drawing.ColorTranslator]::FromHtml('#38d6bd'))
    $tile = New-Object Drawing.Drawing2D.GraphicsPath
    foreach ($arc in @(@(8,8,180),@(144,8,270),@(144,144,0),@(8,144,90))) { $tile.AddArc($arc[0],$arc[1],104,104,$arc[2],90) }
    $tile.CloseFigure(); $g.FillPath($navy,$tile)
    $r = New-Object Drawing.Drawing2D.GraphicsPath
    $r.AddLine(64,56,132,56); $r.AddBezier(132,56,170,56,192,74,192,106)
    $r.AddBezier(192,106,192,129,180,145,159,152)
    $r.AddLines([Drawing.PointF[]]@([Drawing.PointF]::new(159,152),[Drawing.PointF]::new(201,200),[Drawing.PointF]::new(160,200),[Drawing.PointF]::new(123,155),[Drawing.PointF]::new(96,155),[Drawing.PointF]::new(96,200),[Drawing.PointF]::new(64,200)))
    $r.CloseFigure(); $g.FillPath($white,$r)
    $hole = New-Object Drawing.Drawing2D.GraphicsPath
    $hole.AddLine(96,86,130,86); $hole.AddBezier(130,86,148,86,158,93,158,106)
    $hole.AddBezier(158,106,158,119,148,126,130,126); $hole.AddLine(130,126,96,126); $hole.CloseFigure()
    $g.FillPath($navy,$hole)
    $accent = [Drawing.PointF[]]@([Drawing.PointF]::new(123,155),[Drawing.PointF]::new(160,200),[Drawing.PointF]::new(201,200),[Drawing.PointF]::new(159,152))
    $g.FillPolygon($mint,$accent)
    $stream = New-Object IO.MemoryStream
    $bitmap.Save($stream,[Drawing.Imaging.ImageFormat]::Png)
    $images += ,@{ Size=$size; Bytes=$stream.ToArray() }
    if ($size -eq 256) { $bitmap.Save((Join-Path $PSScriptRoot 'robim-manager.png'),[Drawing.Imaging.ImageFormat]::Png) }
    $stream.Dispose(); $tile.Dispose(); $r.Dispose(); $hole.Dispose(); $navy.Dispose(); $white.Dispose(); $mint.Dispose(); $g.Dispose(); $bitmap.Dispose()
}
$file = [IO.File]::Create((Join-Path $PSScriptRoot 'RoBIM.Manager.ico'))
$writer = New-Object IO.BinaryWriter $file
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$images.Count)
    $offset = 6 + 16 * $images.Count
    foreach ($item in $images) {
        $dimension = if ($item.Size -eq 256) { 0 } else { $item.Size }
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension); $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32); $writer.Write([uint32]$item.Bytes.Length); $writer.Write([uint32]$offset)
        $offset += $item.Bytes.Length
    }
    foreach ($item in $images) { $writer.Write([byte[]]$item.Bytes) }
} finally { $writer.Dispose(); $file.Dispose() }
