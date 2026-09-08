param([string]$OutputDirectory = (Join-Path $PSScriptRoot 'Assets'))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null

function Draw-Insignia([System.Drawing.Graphics]$Graphics, [float]$X, [float]$Y, [float]$Size) {
    $pen = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(224, 173, 69), $Size * 0.044)
    $points = [System.Drawing.PointF[]]@(
        [System.Drawing.PointF]::new($X + $Size * 0.10, $Y + $Size * 0.68),
        [System.Drawing.PointF]::new($X + $Size * 0.10, $Y + $Size * 0.18),
        [System.Drawing.PointF]::new($X + $Size * 0.44, $Y + $Size * 0.68),
        [System.Drawing.PointF]::new($X + $Size * 0.44, $Y + $Size * 0.18),
        [System.Drawing.PointF]::new($X + $Size * 0.86, $Y + $Size * 0.18))
    $Graphics.DrawLines($pen, $points)
    $Graphics.DrawLine($pen, $X + $Size * 0.60, $Y + $Size * 0.18, $X + $Size * 0.60, $Y + $Size * 0.68)
    $Graphics.DrawLine($pen, $X + $Size * 0.60, $Y + $Size * 0.42, $X + $Size * 0.83, $Y + $Size * 0.42)
    $Graphics.DrawLine($pen, $X + $Size * 0.08, $Y + $Size * 0.86, $X + $Size * 0.88, $Y + $Size * 0.86)
    $pen.Dispose()
}

foreach ($asset in @(@('Wizard', 328, 628), @('WizardSmall', 110, 110), @('Icon', 256, 256))) {
    $bitmap = [System.Drawing.Bitmap]::new($asset[1], $asset[2])
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.Clear([System.Drawing.Color]::FromArgb(22, 27, 26))
    $grid = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(34, 42, 38), 1)
    for ($offset = 0; $offset -lt $asset[2]; $offset += 24) { $graphics.DrawLine($grid, 0, $offset, $asset[1], $offset) }
    for ($offset = 0; $offset -lt $asset[1]; $offset += 24) { $graphics.DrawLine($grid, $offset, 0, $offset, $asset[2]) }
    $grid.Dispose()
    if ($asset[0] -eq 'Wizard') {
        Draw-Insignia $graphics 40 74 250
        $font = [System.Drawing.Font]::new('Segoe UI', 24, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
        $small = [System.Drawing.Font]::new('Segoe UI', 13, [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Pixel)
        $brush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(234, 232, 216))
        $graphics.DrawString("NEON`nFRONTIER", $font, $brush, 42, 355)
        $graphics.DrawString('REAL-TIME STRATEGY', $small, $brush, 44, 459)
        $graphics.DrawString('WINDOWS EDITION', $small, $brush, 44, 561)
        $font.Dispose(); $small.Dispose(); $brush.Dispose()
    } else { Draw-Insignia $graphics ($asset[1] * .08) ($asset[2] * .08) ($asset[1] * .84) }
    $graphics.Dispose()
    if ($asset[0] -eq 'Icon') {
        $iconSizes = @(16, 24, 32, 48, 64, 128, 256)
        $iconImages = @()
        foreach ($iconSize in $iconSizes) {
            $scaled = [System.Drawing.Bitmap]::new($iconSize, $iconSize)
            $scaledGraphics = [System.Drawing.Graphics]::FromImage($scaled)
            $scaledGraphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $scaledGraphics.DrawImage($bitmap, 0, 0, $iconSize, $iconSize)
            $scaledGraphics.Dispose()
            $png = [System.IO.MemoryStream]::new()
            $scaled.Save($png, [System.Drawing.Imaging.ImageFormat]::Png)
            $iconImages += ,$png.ToArray()
            $scaled.Dispose(); $png.Dispose()
        }
        $file = [System.IO.File]::Create((Join-Path $OutputDirectory 'NeonFrontier.ico'))
        $writer = [System.IO.BinaryWriter]::new($file)
        $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$iconSizes.Count)
        $payloadOffset = 6 + 16 * $iconSizes.Count
        for ($imageIndex = 0; $imageIndex -lt $iconSizes.Count; $imageIndex++) {
            $dimensionByte = if ($iconSizes[$imageIndex] -eq 256) { 0 } else { $iconSizes[$imageIndex] }
            $writer.Write([byte]$dimensionByte); $writer.Write([byte]$dimensionByte); $writer.Write([byte]0); $writer.Write([byte]0)
            $writer.Write([uint16]1); $writer.Write([uint16]32); $writer.Write([uint32]$iconImages[$imageIndex].Length); $writer.Write([uint32]$payloadOffset)
            $payloadOffset += $iconImages[$imageIndex].Length
        }
        foreach ($payload in $iconImages) { $writer.Write([byte[]]$payload) }
        $writer.Dispose()
    } else { $bitmap.Save((Join-Path $OutputDirectory ($asset[0] + '.bmp')), [System.Drawing.Imaging.ImageFormat]::Bmp) }
    $bitmap.Dispose()
}
