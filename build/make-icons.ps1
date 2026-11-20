# Draws the white-on-transparent header icons of the in-game windows (the game font has no emoji).
# Output: src\StarOverlay.Ingame\Icons\*.png (the agent tints them with the button state).
Add-Type -AssemblyName System.Drawing
$root = Split-Path $PSScriptRoot
$out = Join-Path $root 'src\StarOverlay.Ingame\Icons'
New-Item -ItemType Directory -Force $out | Out-Null

function New-Canvas { $b = New-Object Drawing.Bitmap 64, 64; $g = [Drawing.Graphics]::FromImage($b); $g.SmoothingMode = 'AntiAlias'; $g.Clear([Drawing.Color]::Transparent); return @($b, $g) }

# pin: a tilted push pin
$b, $g = New-Canvas
$white = [Drawing.Brushes]::White
$g.TranslateTransform(32, 32); $g.RotateTransform(35); $g.TranslateTransform(-32, -32)
$g.FillEllipse($white, 20, 6, 24, 14)                       # head
$g.FillPolygon($white, @([Drawing.Point]::new(24,16), [Drawing.Point]::new(40,16), [Drawing.Point]::new(44,34), [Drawing.Point]::new(20,34))) # body
$g.FillRectangle($white, 14, 33, 36, 6)                     # collar
$pen = New-Object Drawing.Pen ([Drawing.Color]::White), 4; $pen.StartCap = 'Round'; $pen.EndCap = 'Round'
$g.DrawLine($pen, 32, 39, 32, 58)                           # needle
$b.Save((Join-Path $out 'pin.png'), [Drawing.Imaging.ImageFormat]::Png); $g.Dispose(); $b.Dispose()

# reset: a circular arrow
$b, $g = New-Canvas
$pen = New-Object Drawing.Pen ([Drawing.Color]::White), 7; $pen.StartCap = 'Round'; $pen.EndCap = 'Flat'
$g.DrawArc($pen, 12, 12, 40, 40, -60, 300)
$g.FillPolygon([Drawing.Brushes]::White, @([Drawing.Point]::new(38,2), [Drawing.Point]::new(58,16), [Drawing.Point]::new(34,26))) # arrow head
$b.Save((Join-Path $out 'reset.png'), [Drawing.Imaging.ImageFormat]::Png); $g.Dispose(); $b.Dispose()
# collapse: an underbar
$b, $g = New-Canvas
$g.FillRectangle([Drawing.Brushes]::White, 10, 42, 44, 9)
$b.Save((Join-Path $out 'collapse.png'), [Drawing.Imaging.ImageFormat]::Png); $g.Dispose(); $b.Dispose()

# settings: a gear (teeth around a disc with a hole)
$b, $g = New-Canvas
$g.TranslateTransform(32, 32)
for ($i = 0; $i -lt 8; $i++) { $g.FillRectangle([Drawing.Brushes]::White, -5, -29, 10, 14); $g.RotateTransform(45) }
$g.ResetTransform()
$g.FillEllipse([Drawing.Brushes]::White, 11, 11, 42, 42)
$g.CompositingMode = 'SourceCopy'
$g.FillEllipse((New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(0,0,0,0))), 23, 23, 18, 18)
$b.Save((Join-Path $out 'settings.png'), [Drawing.Imaging.ImageFormat]::Png); $g.Dispose(); $b.Dispose()

# trash: lid, handle and a body with slits
$b, $g = New-Canvas
$g.FillRectangle([Drawing.Brushes]::White, 10, 15, 44, 7)
$g.FillRectangle([Drawing.Brushes]::White, 24, 7, 16, 8)
$g.FillPolygon([Drawing.Brushes]::White, @([Drawing.Point]::new(14,25), [Drawing.Point]::new(50,25), [Drawing.Point]::new(46,58), [Drawing.Point]::new(18,58)))
$g.CompositingMode = 'SourceCopy'
$clear = New-Object Drawing.Pen ([Drawing.Color]::FromArgb(0,0,0,0)), 4
foreach ($x in 25, 32, 39) { $g.DrawLine($clear, $x, 31, $x, 52) }
$b.Save((Join-Path $out 'trash.png'), [Drawing.Imaging.ImageFormat]::Png); $g.Dispose(); $b.Dispose()

# opacity down / up: a ring (dashed = fainter) with a minus / plus
foreach ($kind in 'dim', 'bright') {
    $b, $g = New-Canvas
    $pen = New-Object Drawing.Pen ([Drawing.Color]::White), 6
    if ($kind -eq 'dim') { $pen.DashStyle = 'Dash' }
    $g.DrawEllipse($pen, 6, 6, 52, 52)
    $mark = New-Object Drawing.Pen ([Drawing.Color]::White), 6
    $g.DrawLine($mark, 20, 32, 44, 32)
    if ($kind -eq 'bright') { $g.DrawLine($mark, 32, 20, 32, 44) }
    $b.Save((Join-Path $out ($kind + '.png')), [Drawing.Imaging.ImageFormat]::Png); $g.Dispose(); $b.Dispose()
}

# grip: a right triangle in the lower right corner (the resize handle)
$b, $g = New-Canvas
$g.FillPolygon([Drawing.Brushes]::White, @([Drawing.Point]::new(62,2), [Drawing.Point]::new(62,62), [Drawing.Point]::new(2,62)))
$b.Save((Join-Path $out 'grip.png'), [Drawing.Imaging.ImageFormat]::Png); $g.Dispose(); $b.Dispose()
# expand: a window outline with a thick title bar (unfold the folded window)
$b, $g = New-Canvas
$pen = New-Object Drawing.Pen ([Drawing.Color]::White), 6
$g.DrawRectangle($pen, 10, 12, 44, 40)
$g.FillRectangle([Drawing.Brushes]::White, 10, 12, 44, 12)
$b.Save((Join-Path $out 'expand.png'), [Drawing.Imaging.ImageFormat]::Png); $g.Dispose(); $b.Dispose()
# bubble: a rounded rectangle (nine-sliced in game) behind every chat message
$b, $g = New-Canvas
$path = New-Object Drawing.Drawing2D.GraphicsPath
$r = 28
$path.AddArc(0, 0, $r, $r, 180, 90); $path.AddArc(63 - $r, 0, $r, $r, 270, 90)
$path.AddArc(63 - $r, 63 - $r, $r, $r, 0, 90); $path.AddArc(0, 63 - $r, $r, $r, 90, 90); $path.CloseFigure()
$g.FillPath([Drawing.Brushes]::White, $path)
$b.Save((Join-Path $out 'bubble.png'), [Drawing.Imaging.ImageFormat]::Png); $g.Dispose(); $b.Dispose()
# copy: two overlapping sheets
$b, $g = New-Canvas
$pen = New-Object Drawing.Pen ([Drawing.Color]::White), 6
$g.DrawRectangle($pen, 22, 6, 32, 38)
$g.FillRectangle([Drawing.Brushes]::White, 10, 20, 32, 38)
$b.Save((Join-Path $out 'copy.png'), [Drawing.Imaging.ImageFormat]::Png); $g.Dispose(); $b.Dispose()
"icons: " + (Get-ChildItem $out -Filter *.png | ForEach-Object Name) -join ', '

# flag: a pole with a pennant (marks who attacked first in a record)
$b, $g = New-Canvas
$g.FillRectangle([Drawing.Brushes]::White, 12, 6, 7, 54)    # pole
$g.FillPolygon([Drawing.Brushes]::White, @([Drawing.Point]::new(19,8), [Drawing.Point]::new(56,20), [Drawing.Point]::new(19,34))) # pennant
$b.Save((Join-Path $out 'flag.png'), [Drawing.Imaging.ImageFormat]::Png); $g.Dispose(); $b.Dispose()

# whisper: a speech bubble with a tail and three dots (the game has no tab icon for whispers)
$b, $g = New-Canvas
$g.FillEllipse([Drawing.Brushes]::White, 6, 8, 52, 38)                     # bubble
$g.FillPolygon([Drawing.Brushes]::White, @([Drawing.Point]::new(16,40), [Drawing.Point]::new(14,58), [Drawing.Point]::new(32,44))) # tail
$g.CompositingMode = 'SourceCopy'
$hole = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(0,0,0,0))
foreach ($x in 18, 29, 40) { $g.FillEllipse($hole, $x, 22, 7, 7) }          # the dots, cut out
$b.Save((Join-Path $out 'whisper.png'), [Drawing.Imaging.ImageFormat]::Png); $g.Dispose(); $b.Dispose()
