param(
    [string] $LightIcoPath = (Join-Path $PSScriptRoot "..\src\WithWindows.App\Assets\tray-lightning-thin-light.ico"),
    [string] $DarkIcoPath = (Join-Path $PSScriptRoot "..\src\WithWindows.App\Assets\tray-lightning-thin-dark.ico")
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$fontName = "Segoe Fluent Icons"
$glyph = [string] [char] 0xE945
$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)

$families = (New-Object System.Drawing.Text.InstalledFontCollection).Families
if (-not ($families | Where-Object { $_.Name -eq $fontName })) {
    throw "缺少字体 $fontName"
}

function New-GlyphPng {
    param(
        [int] $Size,
        [string] $ColorHex,
        [double] $FillRatio,
        [double] $HalfThicknessRatio
    )

    # 先在 4 倍尺寸上渲染，再裁掉透明边界并缩回目标尺寸。
    # FillRatio 控制字形在方形 ICO 内占多大：1.0 表示按长边铺满画布。
    [int] $renderSize = [Math]::Max(256, $Size * 4)
    $source = New-Object System.Drawing.Bitmap(
        $renderSize,
        $renderSize,
        [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($source)
    try {
        $graphics.Clear([System.Drawing.Color]::Transparent)
        $graphics.TextRenderingHint =
            [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality

        $font = New-Object System.Drawing.Font(
            $fontName,
            [single] ($renderSize * 0.78),
            [System.Drawing.FontStyle]::Regular,
            [System.Drawing.GraphicsUnit]::Pixel)
        $brush = New-Object System.Drawing.SolidBrush(
            [System.Drawing.ColorTranslator]::FromHtml($ColorHex))
        $format = New-Object System.Drawing.StringFormat
        $format.Alignment = [System.Drawing.StringAlignment]::Center
        $format.LineAlignment = [System.Drawing.StringAlignment]::Center
        $format.FormatFlags = [System.Drawing.StringFormatFlags]::NoClip

        $rectangle = New-Object System.Drawing.RectangleF(0, 0, $renderSize, $renderSize)
        # 以字形中心为基准向 8 个方向轻微叠绘，等效于加粗描边。
        # 细线档使用较小位移，保持接近系统托盘图标的线条粗细。
        $halfThickness = [single] ($renderSize * $HalfThicknessRatio)
        for ($index = 0; $index -lt 8; $index++) {
            $angle = $index * [Math]::PI / 4
            $graphics.TranslateTransform(
                [single] ([Math]::Cos($angle) * $halfThickness),
                [single] ([Math]::Sin($angle) * $halfThickness))
            $graphics.DrawString($glyph, $font, $brush, $rectangle, $format)
            $graphics.ResetTransform()
        }
        $graphics.DrawString($glyph, $font, $brush, $rectangle, $format)

        $bounds = Get-AlphaBounds -Bitmap $source
        $bitmap = New-Object System.Drawing.Bitmap(
            $Size,
            $Size,
            [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $target = [System.Drawing.Graphics]::FromImage($bitmap)
        try {
            $target.Clear([System.Drawing.Color]::Transparent)
            $target.CompositingMode =
                [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
            $target.CompositingQuality =
                [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
            $target.InterpolationMode =
                [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $target.PixelOffsetMode =
                [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality

            $scale = [Math]::Min(
                ($Size * $FillRatio) / [double] $bounds.Width,
                ($Size * $FillRatio) / [double] $bounds.Height)
            $width = [Math]::Max(1, [int] [Math]::Round($bounds.Width * $scale))
            $height = [Math]::Max(1, [int] [Math]::Round($bounds.Height * $scale))
            $x = [int] [Math]::Round(($Size - $width) / 2.0)
            $y = [int] [Math]::Round(($Size - $height) / 2.0)
            $target.DrawImage(
                $source,
                (New-Object System.Drawing.Rectangle($x, $y, $width, $height)),
                $bounds,
                [System.Drawing.GraphicsUnit]::Pixel)

            $stream = New-Object System.IO.MemoryStream
            $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
            return $stream.ToArray()
        }
        finally {
            $target.Dispose()
            $bitmap.Dispose()
        }
    }
    finally {
        $graphics.Dispose()
        $source.Dispose()
        if ($null -ne $brush) { $brush.Dispose() }
        if ($null -ne $font) { $font.Dispose() }
        if ($null -ne $format) { $format.Dispose() }
        if ($null -ne $stream) { $stream.Dispose() }
    }
}

function Get-AlphaBounds {
    param(
        [System.Drawing.Bitmap] $Bitmap
    )

    $rectangle = New-Object System.Drawing.Rectangle(
        0,
        0,
        $Bitmap.Width,
        $Bitmap.Height)
    $data = $Bitmap.LockBits(
        $rectangle,
        [System.Drawing.Imaging.ImageLockMode]::ReadOnly,
        [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    try {
        $bytes = New-Object byte[] ($data.Stride * $data.Height)
        [System.Runtime.InteropServices.Marshal]::Copy(
            $data.Scan0,
            $bytes,
            0,
            $bytes.Length)

        $left = $Bitmap.Width
        $top = $Bitmap.Height
        $right = -1
        $bottom = -1
        for ($y = 0; $y -lt $Bitmap.Height; $y++) {
            $offset = $y * $data.Stride
            for ($x = 0; $x -lt $Bitmap.Width; $x++) {
                if ($bytes[$offset + $x * 4 + 3] -ne 0) {
                    if ($x -lt $left) { $left = $x }
                    if ($x -gt $right) { $right = $x }
                    if ($y -lt $top) { $top = $y }
                    if ($y -gt $bottom) { $bottom = $y }
                }
            }
        }

        if ($right -lt $left -or $bottom -lt $top) {
            throw "字形没有可见像素"
        }

        $width = $right - $left + 1
        $height = $bottom - $top + 1
        return [System.Drawing.Rectangle]::new($left, $top, $width, $height)
    }
    finally {
        $Bitmap.UnlockBits($data)
    }
}

function Write-Ico {
    param(
        [string] $Path,
        [string] $ColorHex,
        [double] $FillRatio,
        [double] $HalfThicknessRatio
    )

    $images = foreach ($size in $sizes) {
        [pscustomobject] @{
            Size = $size
            Bytes = New-GlyphPng `
                -Size $size `
                -ColorHex $ColorHex `
                -FillRatio $FillRatio `
                -HalfThicknessRatio $HalfThicknessRatio
        }
    }

    $stream = [System.IO.File]::Open(
        $Path,
        [System.IO.FileMode]::Create,
        [System.IO.FileAccess]::Write)
    try {
        $writer = New-Object System.IO.BinaryWriter($stream)
        try {
            $writer.Write([UInt16] 0)
            $writer.Write([UInt16] 1)
            $writer.Write([UInt16] $images.Count)

            $offset = 6 + 16 * $images.Count
            foreach ($image in $images) {
                $dimension = if ($image.Size -ge 256) { 0 } else { $image.Size }
                $writer.Write([byte] $dimension)
                $writer.Write([byte] $dimension)
                $writer.Write([byte] 0)
                $writer.Write([byte] 0)
                $writer.Write([UInt16] 1)
                $writer.Write([UInt16] 32)
                $writer.Write([UInt32] $image.Bytes.Length)
                $writer.Write([UInt32] $offset)
                $offset += $image.Bytes.Length
            }

            foreach ($image in $images) {
                $writer.Write([byte[]] $image.Bytes)
            }
        }
        finally {
            $writer.Dispose()
        }
    }
    finally {
        $stream.Dispose()
    }
}

$LightIcoPath = [System.IO.Path]::GetFullPath($LightIcoPath)
$DarkIcoPath = [System.IO.Path]::GetFullPath($DarkIcoPath)
# 只生成最终采用的细线闪电：字形按长边铺满画布，不额外留边距。
Write-Ico `
    -Path $LightIcoPath `
    -ColorHex "#1A1A1A" `
    -FillRatio 1.0 `
    -HalfThicknessRatio 0.004
Write-Ico `
    -Path $DarkIcoPath `
    -ColorHex "#FFFFFF" `
    -FillRatio 1.0 `
    -HalfThicknessRatio 0.004
Write-Output "已更新：$LightIcoPath"
Write-Output "已更新：$DarkIcoPath"
