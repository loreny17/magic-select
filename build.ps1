# Compila o Magic Select e o instalador usando o compilador C# que já vem no Windows (.NET Framework 4.x).
#   build.ps1           -> bin\MagicSelect.exe
#   build.ps1 -Setup    -> também gera dist\MagicSelect-Setup.exe (o arquivo para compartilhar)
param([switch]$Setup)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$fw = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$csc = Join-Path $fw 'csc.exe'
$icon = Join-Path $root 'assets\icon.ico'
if (-not (Test-Path $icon)) { & (Join-Path $root 'tools\make-icon.ps1') | Out-Null }

$refs = @('/r:System.dll', '/r:System.Drawing.dll', '/r:System.Windows.Forms.dll', '/r:System.Web.Extensions.dll',
          '/r:System.Security.dll', "/r:$(Join-Path $fw 'WPF\System.Speech.dll')", '/r:System.Data.dll',
          '/r:System.Xml.dll', '/r:System.Core.dll', '/r:Microsoft.CSharp.dll')
$common = @('/nologo', '/target:winexe', '/platform:anycpu', '/optimize+', '/utf8output', '/codepage:65001',
            "/win32icon:$icon", "/resource:$icon,app.ico", "/resource:$(Join-Path $root 'assets\logo.png'),logo.png")

New-Item -ItemType Directory -Force (Join-Path $root 'bin') | Out-Null
$app = Join-Path $root 'bin\MagicSelect.exe'
$sources = Get-ChildItem (Join-Path $root 'src') -Filter *.cs | ForEach-Object { $_.FullName }
& $csc @common "/out:$app" @refs $sources
if ($LASTEXITCODE -ne 0) { throw "Falha na compilacao do app" }
Write-Host "OK -> $app"

if ($Setup) {
    $dist = Join-Path $root 'dist'
    New-Item -ItemType Directory -Force $dist | Out-Null
    $out = Join-Path $dist 'MagicSelect-Setup.exe'
    $setupSources = @((Join-Path $root 'installer\Setup.cs'), (Join-Path $root 'src\Controls.cs'),
                      (Join-Path $root 'src\Native.cs'), (Join-Path $root 'src\Brand.cs'), (Join-Path $root 'src\Installation.cs'))
    & $csc @common "/out:$out" "/resource:$app,payload.exe" @refs $setupSources
    if ($LASTEXITCODE -ne 0) { throw "Falha na compilacao do instalador" }
    Write-Host "OK -> $out"
}
