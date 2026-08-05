# [design-v1.2.1 §6] Uygulama ikonu ureteci — URUN markasi (Build Orchestrator).
#
# Uretilen dosyalar (ikisi de bu klasore):
#   tray-icon-16.ico  — sistem tepsisi (AppTrayIcon 16px varyantini kullanir)
#   app-icon.ico      — cok boyutlu uygulama ikonu: 16 / 24 / 32 / 48 / 256 (<ApplicationIcon> + Window.Icon)
#
# Kaynak sanat: `.claude/outputs/2026-08-05-01-26-design-v1.2.1/prototype/assets/` — serit ve chevron
#   geometrisi `app-icon.svg` / `app-mark.svg`'de AYNIdir; gradient'ler app-icon.svg'nin <defs>'inden gelir.
#
# ONCEKI SURUM (T62/T64) Delta'nin kendi ikonunu (delta-app-icon.svg) rasterlestiriyordu. design-v1.2.0 ile
# uygulama kendi markasini kazandi; Delta artik FIRMA logosudur ve ikonda yer almaz.
#
# ZEMIN KARARI — KULLANICI TALEBI, TASARIMDAN BILINCLI SAPMA:
#   design-v1.2.1 §6 kullanim matrisi `app-icon.svg`i (near-black tile'li) .exe/taskbar/tepsi icin, seffaf
#   `app-mark.svg`i uygulama ici icin ayirir. Kullanici ikisinde de ZEMIN ISTEMEDI: ikonlar artik SEFFAF
#   uretilir ve isaret tuvale oturur (tile'in birakacagi %42'lik kucuk yerlesim yerine). Tile'i geri koymak
#   icin `-WithTile`.
#   BILINEN BEDEL: markanin iki KOYU seridi (#2A2A30, #44444B) koyu bir taskbar'da neredeyse gorunmez;
#   gorunen kompozisyon amber serit + beyaz serit + gumus serit + chevron olur. Tile bu sorunu cozuyordu.
#
# KUCUK BOYUT KARARI — ayrintili gerekce asagida `$SmallSizes`.
#
# Calistirma:
#   powershell -NoProfile -ExecutionPolicy Bypass -File src/BuildOrchestrator.App/Assets/generate-app-icons.ps1
# NOT: WPF rasterlestirmesi icin STA gerekir; powershell.exe varsayilan olarak STA'dir (pwsh icin -STA gerekir).
#
# Tani modu (dosya YAZMAZ, verilen boyutu ASCII olarak dokumler — kucuk boyut kararini gozle dogrulamak icin):
#   ... -File generate-app-icons.ps1 -DumpAscii 16

param(
  [int]$DumpAscii = 0,
  [int[]]$ChevronOnlyAt = @(),   # tani: verilen boyutlarda yalnizca chevron ciz (varsayilan: HICBIRI — tam isaret)
  [switch]$WithTile              # tani: near-black tile'i geri koy (varsayilan: SEFFAF)
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore, PresentationFramework, WindowsBase

# ---- kaynak sanatin uzayi ve renkleri (app-icon.svg'den BIREBIR) -------------------------------------------
$ArtSize    = 286.0     # <svg viewBox="0 0 286 286">
$TileRadius = 31.0      # <rect rx="31">
$StripDx    = 5.5       # <g transform="translate(5.5 0)">

# 16/24'te tam sanat okunmuyor: serit yuksekligi 21/286 → 16px'te 1.18 piksel. Bu boyutlarda yalnizca chevron
# cizilir (marka tanınırligini tasiyan tek ogedir; amber kontrasti near-black tile'da net kalir).
# TAM ISARET HER BOYUTTA — tepside de. Chevron-yalniz yol yalnizca tani icin durur (-ChevronOnlyAt).
$SmallSizes = $ChevronOnlyAt
# Chevron-yalniz + TILE'LI varyantta chevron'un tile yuksekligine orani (yalnizca o kombinasyonda kullanilir).
$ChevronFill = 0.60

# Seffaf varyantta isaretin her kenarda biraktigi bosluk (tuval orani). SIFIRA yakin: isaret 1.48:1 oranindadir
# ve kare tuvale GENISLIGINDEN sigar — dikeyde tuvalin ~%67'sini kaplar, bu oranin kendi sonucudur (germek
# bozulma olurdu). Dolayisiyla algilanan buyuklugu genislik belirler ve genislik sonuna kadar kullanilir.
# Tam 0 degil: kenardaki yumusatma pikselleri kirpilmasin.
$MarkPadding = 0.012

# Bu boyuta kadar seritler HEDEF PIKSEL izgarasina oturtulur (bkz. Get-SnappedStripRect).
$SnapBelow = 32

function Rgb([string]$hex) {
  return [System.Windows.Media.Color]::FromRgb(
    [Convert]::ToByte($hex.Substring(0, 2), 16),
    [Convert]::ToByte($hex.Substring(2, 2), 16),
    [Convert]::ToByte($hex.Substring(4, 2), 16))
}

# Dikey (0,0)->(0,1) linear gradient — SVG'deki serit gradient'lerinin tamami bu yondedir.
function VerticalBrush($stops) {
  $collection = New-Object System.Windows.Media.GradientStopCollection
  foreach ($s in $stops) {
    $collection.Add((New-Object System.Windows.Media.GradientStop ((Rgb $s[1]), [double]$s[0])))
  }
  $brush = New-Object System.Windows.Media.LinearGradientBrush (
    $collection,
    (New-Object System.Windows.Point (0, 0)),
    (New-Object System.Windows.Point (0, 1)))
  $brush.Freeze()
  return $brush
}

# app-icon.svg <defs> — SVG'deki durak listeleri birebir.
$TileBrush = VerticalBrush @( @(0, '141417'), @(0.56, '0E0E10'), @(1, '0A0A0C') )
$TileBorder = Rgb '2A2A30'

$StripBrushes = @{
  TopDark    = VerticalBrush @( @(0, '54545C'), @(0.20, '494950'), @(0.50, '414147'), @(0.80, '3A3A42'), @(1, '44444B') )
  TopAmber   = VerticalBrush @( @(0, 'FFB52E'), @(0.20, 'F7AE25'), @(0.50, 'F1AB2E'), @(0.80, 'EDA10F'), @(1, 'D9910D') )
  MiddleDark = VerticalBrush @( @(0, '3A3A42'), @(0.20, '313136'), @(0.50, '2A2A30'), @(1, '202024') )
  White      = VerticalBrush @( @(0, 'EDEDEE'), @(0.18, 'DADADD'), @(0.50, 'C8C8CD'), @(0.80, 'B9B9BF'), @(1, 'A9A9B0') )
  Silver     = VerticalBrush @( @(0, 'A9A9B0'), @(0.20, '929299'), @(0.50, '85858D'), @(0.80, '76767E'), @(1, '83838B') )
}

# Seritler: x, y, genislik, yukseklik, yaricap, firca anahtari (app-icon.svg'den birebir)
$Strips = @(
  @{ X = 68; Y = 83;  W = 35; H = 21; R = 10.5; Brush = 'TopDark' },
  @{ X = 114; Y = 83;  W = 51; H = 21; R = 10.5; Brush = 'TopAmber' },
  @{ X = 46; Y = 131; W = 39; H = 21; R = 10.5; Brush = 'MiddleDark' },
  @{ X = 97; Y = 131; W = 60; H = 22; R = 11.0; Brush = 'White' },
  @{ X = 60; Y = 178; W = 57; H = 21; R = 10.5; Brush = 'Silver' }
)

$ChevronPath = 'M151 83 L171.5 83 C176.1 83 179.2 85.1 182.3 88.5 L219.8 131.5 C222.7 134.9 224 138.3 224 142.4 ' +
               'C224 146.5 222.7 149.8 219.8 153.2 L179.1 198.3 C176.0 201.6 172.3 203 168.0 203 L151.0 203 ' +
               'C145.8 203 142.1 200.4 140.5 196.4 C138.8 192.1 140.2 187.8 143.5 184.2 L177.8 146.2 ' +
               'C180.6 143.2 180.8 140.8 178.2 137.8 L144.7 103.7 C141.8 100.7 140.4 96.9 140.4 93.0 ' +
               'C140.4 87.5 145.1 83 151 83 Z'

# Chevron gradient'i userSpaceOnUse (156,82)->(177,204) — sanat uzayinda MUTLAK koordinatlar.
function New-ChevronBrush {
  $stops = @( @(0, 'FFB52E'), @(0.13, 'F9AE25'), @(0.26, 'F4A91D'), @(0.38, 'EDA10F'), @(0.51, 'DE940D'),
              @(0.64, 'CF880C'), @(0.76, 'B87A0B'), @(0.89, 'A66D09'), @(1, '8B5907') )
  $collection = New-Object System.Windows.Media.GradientStopCollection
  foreach ($s in $stops) {
    $collection.Add((New-Object System.Windows.Media.GradientStop ((Rgb $s[1]), [double]$s[0])))
  }
  $brush = New-Object System.Windows.Media.LinearGradientBrush (
    $collection,
    (New-Object System.Windows.Point (156, 82)),
    (New-Object System.Windows.Point (177, 204)))
  $brush.MappingMode = [System.Windows.Media.BrushMappingMode]::Absolute
  $brush.Freeze()
  return $brush
}

# Isaretin sanat uzayindaki sinir kutulari (translate(5.5 0) UYGULANMIS hali).
#   Tam isaret : seritler x 46..165, chevron x 140.4..224 → 51.5..229.5 ; y 83..203
#   Yalniz chevron: x 145.9..229.5 ; y 83..203
$MarkBBox    = @{ X = 51.5;  Y = 83.0; W = 178.0; H = 120.0 }
$ChevronBBox = @{ X = 145.9; Y = 83.0; W = 83.6;  H = 120.0 }

# Sinir kutusunu KARE tuvale oturtan Uniform olcek + ortalama oteleme (sanat uzayinda).
function Get-FitParams($bbox, [double]$pad) {
  $avail = $ArtSize * (1.0 - 2.0 * $pad)
  $fit = [Math]::Min($avail / $bbox.W, $avail / $bbox.H)
  return @{
    Fit = $fit
    Ox  = ($ArtSize - $bbox.W * $fit) / 2.0
    Oy  = ($ArtSize - $bbox.H * $fit) / 2.0
  }
}

# Ayni oturtmayi DrawingContext'e iter (egri geometriler icin — onlar piksele oturtulamaz).
function Push-FitTransform($dc, $bbox, [double]$pad) {
  $p = Get-FitParams $bbox $pad
  $dc.PushTransform((New-Object System.Windows.Media.TranslateTransform ($p.Ox, $p.Oy)))
  $dc.PushTransform((New-Object System.Windows.Media.ScaleTransform ($p.Fit, $p.Fit)))
  $dc.PushTransform((New-Object System.Windows.Media.TranslateTransform (-$bbox.X, -$bbox.Y)))
  $dc.PushTransform((New-Object System.Windows.Media.TranslateTransform ($StripDx, 0)))
}

function Pop-FitTransform($dc) { $dc.Pop(); $dc.Pop(); $dc.Pop(); $dc.Pop() }

# Bir seridin HEDEF PIKSEL uzayindaki dikdortgeni, kenarlari TAM piksele yuvarlanmis.
#
# Neden: 16px'te serit yuksekligi ~1.9 pikseldir. Yuvarlanmadan cizilirse her serit iki piksel satirina
# yayilir, ikisi de yarim opaklikta cikar ve bes serit gri bir bulanikliga doner. Kenarlari tam piksele
# oturtmak seritleri KESKIN yapar — kucuk raster ikonlarda standart yaklasim. Egri chevron ayni islemi
# kaldirmaz, o oturtulmadan cizilir (seritlere degmedigi icin hizalama sorunu olmaz).
function Get-SnappedStripRect($strip, $bbox, [double]$pad, [double]$scale) {
  $p = Get-FitParams $bbox $pad
  $ax = $strip.X + $StripDx
  $x0 = ((($ax - $bbox.X) * $p.Fit) + $p.Ox) * $scale
  $y0 = ((($strip.Y - $bbox.Y) * $p.Fit) + $p.Oy) * $scale
  $x1 = $x0 + ($strip.W * $p.Fit * $scale)
  $y1 = $y0 + ($strip.H * $p.Fit * $scale)

  $left = [Math]::Round($x0); $right = [Math]::Round($x1)
  $top = [Math]::Round($y0);  $bottom = [Math]::Round($y1)
  if ($right - $left -lt 1) { $right = $left + 1 }
  if ($bottom - $top -lt 1) { $bottom = $top + 1 }

  $w = $right - $left; $h = $bottom - $top
  $r = [Math]::Min($strip.R * $p.Fit * $scale, [Math]::Min($w, $h) / 2.0)
  return @{ Rect = (New-Object System.Windows.Rect ($left, $top, $w, $h)); R = $r }
}

# Kaynak sanati $size'a rasterlestirir.
#   $simplify — yalnizca chevron (kucuk boyutlar; bes serit o olcekte okunmuyor)
#   $withTile — near-black tile + kenarlik. KAPALI oldugunda isaret SEFFAF zeminde durur ve tuvale OTURUR
#               (tile varken isaret onun icinde %42'de kalir; zemin yokken o bosluk anlamsizdir).
function New-RenderedBitmap([int]$size, [bool]$simplify, [bool]$withTile) {
  $scale = $size / $ArtSize
  $root = New-Object System.Windows.Media.ContainerVisual
  $bbox = if ($simplify) { $ChevronBBox } else { $MarkBBox }
  # Tile varken isaret kendi SVG konumunda durur; tile yokken tuvale oturtulur.
  $pad = if ($withTile) { $null } else { $MarkPadding }

  if ($withTile) {
    $tile = New-Object System.Windows.Media.DrawingVisual
    $dc = $tile.RenderOpen()
    $dc.PushTransform((New-Object System.Windows.Media.ScaleTransform ($scale, $scale)))
    $dc.DrawRoundedRectangle($TileBrush, $null,
      (New-Object System.Windows.Rect (1, 1, 284, 284)), $TileRadius, $TileRadius)
    $pen = New-Object System.Windows.Media.Pen ((New-Object System.Windows.Media.SolidColorBrush $TileBorder), 1.0)
    $dc.DrawRoundedRectangle($null, $pen,
      (New-Object System.Windows.Rect (1.5, 1.5, 283, 283)), 30.5, 30.5)
    $dc.Pop()
    $dc.Close()
    [void]$root.Children.Add($tile)
  }

  if (-not $simplify) {
    $stripVisual = New-Object System.Windows.Media.DrawingVisual
    $dc = $stripVisual.RenderOpen()
    # Kucuk boyutlarda seritler HEDEF PIKSEL uzayinda, kenarlari tam piksele oturtularak cizilir; buyuk
    # boyutlarda oturtmaya gerek yok (serit zaten cok piksel yuksekliginde) ve sanat uzayi daha sadiktir.
    $snapStrips = ($null -ne $pad) -and ($size -le $SnapBelow)
    if ($snapStrips) {
      foreach ($s in $Strips) {
        $r = Get-SnappedStripRect $s $bbox $pad $scale
        $dc.DrawRoundedRectangle($StripBrushes[$s.Brush], $null, $r.Rect, $r.R, $r.R)
      }
    }
    else {
      $dc.PushTransform((New-Object System.Windows.Media.ScaleTransform ($scale, $scale)))
      if ($null -eq $pad) { $dc.PushTransform((New-Object System.Windows.Media.TranslateTransform ($StripDx, 0))) }
      else { Push-FitTransform $dc $bbox $pad }
      foreach ($s in $Strips) {
        $dc.DrawRoundedRectangle($StripBrushes[$s.Brush], $null,
          (New-Object System.Windows.Rect ($s.X, $s.Y, $s.W, $s.H)), $s.R, $s.R)
      }
      if ($null -eq $pad) { $dc.Pop() } else { Pop-FitTransform $dc }
      $dc.Pop()
    }
    $dc.Close()
    # Golge YALNIZ tile'da: seffaf bir ikonda siyah drop-shadow arkadaki taskbar'a bulasir.
    if ($withTile) {
      $shadow = New-Object System.Windows.Media.Effects.DropShadowEffect
      $shadow.ShadowDepth = 2 * $scale; $shadow.Direction = 270
      $shadow.BlurRadius = 3.1 * $scale; $shadow.Opacity = 0.92
      $shadow.Color = [System.Windows.Media.Colors]::Black
      $stripVisual.Effect = $shadow
    }
    [void]$root.Children.Add($stripVisual)
  }

  $arrowVisual = New-Object System.Windows.Media.DrawingVisual
  $dc = $arrowVisual.RenderOpen()
  $dc.PushTransform((New-Object System.Windows.Media.ScaleTransform ($scale, $scale)))
  if ($null -eq $pad) {
    if ($simplify) {
      # Tile'li sade varyant: chevron tile'in $ChevronFill'i olacak sekilde ortalanir.
      $fit = ($ChevronFill * $ArtSize) / $ChevronBBox.H
      $dc.PushTransform((New-Object System.Windows.Media.TranslateTransform (
        (($ArtSize - $ChevronBBox.W * $fit) / 2.0), (($ArtSize - $ChevronBBox.H * $fit) / 2.0))))
      $dc.PushTransform((New-Object System.Windows.Media.ScaleTransform ($fit, $fit)))
      $dc.PushTransform((New-Object System.Windows.Media.TranslateTransform (-$ChevronBBox.X, -$ChevronBBox.Y)))
      $dc.PushTransform((New-Object System.Windows.Media.TranslateTransform ($StripDx, 0)))
    }
    else { $dc.PushTransform((New-Object System.Windows.Media.TranslateTransform ($StripDx, 0))) }
  }
  else { Push-FitTransform $dc $bbox $pad }

  $dc.DrawGeometry((New-ChevronBrush), $null, [System.Windows.Media.Geometry]::Parse($ChevronPath))

  if ($null -eq $pad) { if ($simplify) { Pop-FitTransform $dc } else { $dc.Pop() } }
  else { Pop-FitTransform $dc }
  $dc.Pop(); $dc.Close()

  if ($withTile) {
    $arrowShadow = New-Object System.Windows.Media.Effects.DropShadowEffect
    $arrowShadow.ShadowDepth = 2.2 * $scale; $arrowShadow.Direction = 270
    $arrowShadow.BlurRadius = 3.6 * $scale; $arrowShadow.Opacity = 0.94
    $arrowShadow.Color = [System.Windows.Media.Colors]::Black
    $arrowVisual.Effect = $arrowShadow
  }
  [void]$root.Children.Add($arrowVisual)

  $rtb = New-Object System.Windows.Media.Imaging.RenderTargetBitmap (
    $size, $size, 96, 96, [System.Windows.Media.PixelFormats]::Pbgra32)
  $rtb.Render($root)
  # ICO icerigi DUZ (premultiply edilmemis) alfa ister — donusumu WIC yapsin.
  return New-Object System.Windows.Media.Imaging.FormatConvertedBitmap (
    $rtb, [System.Windows.Media.PixelFormats]::Bgra32, $null, 0.0)
}

# BitmapSource -> 32bpp BGRA, alttan uste (ICO/BMP duzeni)
function ConvertTo-BottomUpBgra([System.Windows.Media.Imaging.BitmapSource]$bmp) {
  $size = $bmp.PixelWidth
  $stride = $size * 4
  $top = New-Object byte[] ($stride * $size)
  $bmp.CopyPixels($top, $stride, 0)
  $out = New-Object byte[] ($stride * $size)
  for ($y = 0; $y -lt $size; $y++) {
    [Array]::Copy($top, $y * $stride, $out, ($size - 1 - $y) * $stride, $stride)
  }
  return ,$out            # virgul: PowerShell dizi donusunu ACMASIN
}

function ConvertTo-PngBytes([System.Windows.Media.Imaging.BitmapSource]$bmp) {
  $enc = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
  $enc.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bmp))
  $ms = New-Object System.IO.MemoryStream
  $enc.Save($ms)
  return ,$ms.ToArray()
}

# Bir kare icin ICO govdesi: BITMAPINFOHEADER + XOR (BGRA) + AND maskesi (1bpp, satir 4 bayta hizali).
function New-BmpFrameBytes([int]$size, [byte[]]$bgra) {
  $maskStride = [Math]::Ceiling($size / 8.0)
  $maskStride = [int]([Math]::Ceiling($maskStride / 4.0) * 4)
  $andMask = New-Object byte[] ($maskStride * $size)   # hepsi 0: tumu opak/alfa gecerli

  $ms = New-Object System.IO.MemoryStream
  $bw = New-Object System.IO.BinaryWriter($ms)
  $bw.Write([uint32]40); $bw.Write([int32]$size); $bw.Write([int32]($size * 2))
  $bw.Write([uint16]1); $bw.Write([uint16]32); $bw.Write([uint32]0)
  $bw.Write([uint32]($bgra.Length + $andMask.Length))
  $bw.Write([int32]0); $bw.Write([int32]0); $bw.Write([uint32]0); $bw.Write([uint32]0)
  $bw.Write($bgra); $bw.Write($andMask)
  $bw.Flush()
  return ,$ms.ToArray()
}

function New-IcoBytes($frames) {
  $ms = New-Object System.IO.MemoryStream
  $bw = New-Object System.IO.BinaryWriter($ms)
  $bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$frames.Count)
  $offset = 6 + (16 * $frames.Count)
  foreach ($f in $frames) {
    $dim = if ($f.Size -ge 256) { 0 } else { $f.Size }   # 256 => 0 (ICO alan genisligi 1 bayt)
    $bw.Write([byte]$dim); $bw.Write([byte]$dim); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32)
    $bw.Write([uint32]$f.Body.Length); $bw.Write([uint32]$offset)
    $offset += $f.Body.Length
  }
  foreach ($f in $frames) { $bw.Write($f.Body) }
  $bw.Flush()
  return ,$ms.ToArray()
}

function Save-Ico([string]$name, $frames) {
  $bytes = New-IcoBytes $frames
  $out = Join-Path $PSScriptRoot $name
  [System.IO.File]::WriteAllBytes($out, $bytes)
  $sizes = ($frames | ForEach-Object { $_.Size }) -join '/'
  Write-Host "written: $out ($($bytes.Length) bytes, frames: $sizes)"
}

function Get-Frame([int]$size) {
  return New-RenderedBitmap $size ($SmallSizes -contains $size) ([bool]$WithTile)
}

# --- tani modu: pikselleri ASCII olarak dokumle (dosya yazmaz) ---
if ($DumpAscii -gt 0) {
  $bmp = Get-Frame $DumpAscii
  $stride = $DumpAscii * 4
  $px = New-Object byte[] ($stride * $DumpAscii)
  $bmp.CopyPixels($px, $stride, 0)
  # Luminans kademeleri: koyu tile '.', orta seritler '+'/'o', parlak (beyaz serit/amber) '#'
  for ($y = 0; $y -lt $DumpAscii; $y++) {
    $row = ''
    for ($x = 0; $x -lt $DumpAscii; $x++) {
      $o = $y * $stride + $x * 4
      $lum = (0.114 * $px[$o]) + (0.587 * $px[$o + 1]) + (0.299 * $px[$o + 2])
      if ($px[$o + 3] -lt 32) { $row += ' ' }
      elseif ($lum -lt 40)  { $row += '.' }
      elseif ($lum -lt 90)  { $row += '+' }
      elseif ($lum -lt 150) { $row += 'o' }
      else                  { $row += '#' }
    }
    Write-Host $row
  }
  return
}

# --- tepsi ikonu: 16px (AppTrayIcon bunu kullanir) ---
$tray16 = @{ Size = 16; Body = (New-BmpFrameBytes 16 (ConvertTo-BottomUpBgra (Get-Frame 16))) }
Save-Ico 'tray-icon-16.ico' @($tray16)

# --- uygulama ikonu: 16/24 sadelestirilmis, 32/48 tam sanat BMP, 256 PNG-sikistirilmis (ICO standardi) ---
$appFrames = @($tray16)
foreach ($size in 24, 32, 48) {
  $appFrames += @{ Size = $size; Body = (New-BmpFrameBytes $size (ConvertTo-BottomUpBgra (Get-Frame $size))) }
}
# 256 icin ham BMP ~264 KB olurdu; ICO 256'da PNG govdesini destekler (Vista+/WIC) — repoda kucuk kalir.
$appFrames += @{ Size = 256; Body = (ConvertTo-PngBytes (Get-Frame 256)) }
Save-Ico 'app-icon.ico' $appFrames
