$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$src = Join-Path $root 'src'
$dist = Join-Path $root 'dist'
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'

if (-not (Test-Path $csc)) { throw "csc.exe not found: $csc" }
if (-not (Test-Path $dist)) { New-Item -ItemType Directory -Path $dist | Out-Null }

# Add UTF-8 BOM so the old csc.exe reads Chinese literals correctly.
Get-ChildItem $src -Filter *.cs | ForEach-Object {
    $text = [IO.File]::ReadAllText($_.FullName, [Text.Encoding]::UTF8)
    $utf8Bom = New-Object System.Text.UTF8Encoding -ArgumentList $true
    [IO.File]::WriteAllText($_.FullName, $text, $utf8Bom)
}

$sources = Get-ChildItem $src -Filter *.cs | ForEach-Object { $_.FullName }
$outExe = Join-Path $dist 'HonorChargeLimit.exe'

$arguments = @(
    '/nologo',
    '/target:winexe',
    '/platform:x64',
    '/optimize+',
    '/codepage:65001',
    "/out:$outExe",
    "/win32manifest:$(Join-Path $src 'app.manifest')",
    "/win32icon:$(Join-Path $src 'icon.ico')",
    '/reference:System.dll',
    '/reference:System.Core.dll',
    '/reference:System.Drawing.dll',
    '/reference:System.Windows.Forms.dll',
    '/reference:System.ServiceProcess.dll'
) + $sources

& $csc @arguments
if ($LASTEXITCODE -ne 0) { throw "Build failed, exit code $LASTEXITCODE" }

if (Test-Path (Join-Path $root 'docs\QUICKSTART.txt')) {
    Copy-Item (Join-Path $root 'docs\QUICKSTART.txt') (Join-Path $dist 'README.txt') -Force
}

Get-ChildItem $dist | Select-Object Name, Length
Write-Host "Build OK: $outExe"



