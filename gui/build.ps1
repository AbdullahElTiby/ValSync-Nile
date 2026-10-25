# Builds dist\VALSYNC-Nile.exe with the csc.exe that ships with .NET Framework 4.x (no SDK needed).
#   powershell -ExecutionPolicy Bypass -File gui\build.ps1            # release build (asks for admin)
#   powershell -ExecutionPolicy Bypass -File gui\build.ps1 -Preview   # no admin prompt, for --preview/--selftest
param([switch]$Preview)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$gui  = $PSScriptRoot
$root = Split-Path $gui -Parent
$dist = Join-Path $root 'dist'
$obj  = Join-Path $gui 'obj'
New-Item -ItemType Directory -Force $dist, $obj | Out-Null

# --- Icon (16-256 px PNG frames) and a 128 px header logo from the VALSYNC logo.
$logo = [System.Drawing.Image]::FromFile((Join-Path $gui 'assets\logo.png'))
function Resize-Png([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = 'HighQualityBicubic'; $g.PixelOffsetMode = 'HighQuality'; $g.SmoothingMode = 'AntiAlias'
    $g.DrawImage($logo, 0, 0, $size, $size)
    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return ,$ms.ToArray()
}
$sizes = 16, 24, 32, 48, 64, 128, 256
$frames = $sizes | ForEach-Object { ,(Resize-Png $_) }
$ico = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $ico
$w.Write([UInt16]0); $w.Write([UInt16]1); $w.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $len = $frames[$i].Length
    $w.Write([byte]($s % 256)); $w.Write([byte]($s % 256)); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([UInt16]1); $w.Write([UInt16]32); $w.Write([UInt32]$len); $w.Write([UInt32]$offset)
    $offset += $len
}
foreach ($f in $frames) { $w.Write($f) }
$w.Flush()
[System.IO.File]::WriteAllBytes((Join-Path $obj 'icon.ico'), $ico.ToArray())
[System.IO.File]::WriteAllBytes((Join-Path $obj 'logo128.png'), (Resize-Png 128))
$logo.Dispose()

# --- Embedded resources: engine files are extracted to ProgramData on first run.
$res = New-Object System.Collections.Generic.List[string]
function Add-Res([string]$path, [string]$name) { $res.Add("/resource:`"$path`",$name") }
foreach ($dir in 'x86', 'x86_64', 'voice', 'licenses') {
    Get-ChildItem (Join-Path $root $dir) -Recurse -File | ForEach-Object {
        $rel = $_.FullName.Substring($root.Length + 1).Replace('\', '/')
        Add-Res $_.FullName "engine/$rel"
    }
}
Add-Res (Join-Path $root 'egypt-blacklist.txt') 'engine/egypt-blacklist.txt'
Add-Res (Join-Path $gui 'assets\OFL.txt') 'engine/licenses/LICENSE-fonts-OFL.txt'
Add-Res (Join-Path $gui 'assets\big_shoulders_display_bold.ttf') 'font/display.ttf'
Add-Res (Join-Path $gui 'assets\barlow_condensed_medium.ttf') 'font/ui.ttf'
Add-Res (Join-Path $gui 'assets\barlow_condensed_semibold.ttf') 'font/ui-strong.ttf'
Add-Res (Join-Path $obj 'logo128.png') 'img/logo.png'

# --- Compile.
$manifest = Join-Path $gui 'app.manifest'
$out = Join-Path $dist 'VALSYNC-Nile.exe'
if ($Preview) {
    $manifest = Join-Path $obj 'preview.manifest'
    (Get-Content (Join-Path $gui 'app.manifest') -Raw).Replace('requireAdministrator', 'asInvoker') | Set-Content -Encoding UTF8 $manifest
    $out = Join-Path $obj 'VALSYNC-Nile-preview.exe'
}
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
$cscArgs = @('/nologo', '/target:winexe', '/platform:anycpu', '/optimize+', '/codepage:65001',
    "/out:$out", "/win32icon:$(Join-Path $obj 'icon.ico')", "/win32manifest:$manifest",
    '/r:System.dll', '/r:System.Core.dll', '/r:System.Drawing.dll', '/r:System.Windows.Forms.dll', '/r:System.ServiceProcess.dll') +
    $res + @((Join-Path $gui 'ValSyncNile.cs'))
& $csc @cscArgs
if ($LASTEXITCODE -ne 0) { throw "csc failed with exit code $LASTEXITCODE" }
Get-Item $out | Select-Object FullName, Length
