# pack-release.ps1 - build + package HachimiManga portable zip (run from repo root)
# Usage: powershell -NoProfile -ExecutionPolicy Bypass -File tools\pack-release.ps1
# Output: app\dist\HachimiManga-<ver>-win-x64.zip (contains HachimiManga\ top folder + guide\)
# NOTE: keep this file pure ASCII (PS 5.1 reads no-BOM UTF-8 as ANSI; Chinese comments corrupt parsing).
$root = Split-Path -Parent $PSScriptRoot
$app  = Join-Path $root 'app'
$ver  = 'v1.0'                                  # bump version here before repacking
$dotnet = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet\dotnet.exe'

# 1) publish (self-contained win-x64; single line - PS 5.1 backtick line-continuation breaks on -p: args)
& $dotnet publish (Join-Path $app 'src\MangaAgent.App') -c Release -r win-x64 --self-contained -p:PublishSingleFile=false -m:1 -p:UseSharedCompilation=false -p:UsedAvaloniaProducts=
if ($LASTEXITCODE -ne 0) { throw 'publish failed' }

# 2) mirror to app\publish (/MIR removes stale files -> clean distribution folder)
robocopy (Join-Path $app 'src\MangaAgent.App\bin\Release\net9.0\win-x64\publish') (Join-Path $app 'publish') /MIR /NFL /NDL /NJH /NP | Out-Null
if ($LASTEXITCODE -gt 7) { throw 'robocopy publish failed' }

# 3) staging + bundled guide + zip
$dist  = Join-Path $app 'dist'
$stage = Join-Path $dist 'HachimiManga'
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force -Path $stage | Out-Null
robocopy (Join-Path $app 'publish') $stage /MIR /NFL /NDL /NJH /NP | Out-Null
robocopy (Join-Path $app 'guide') (Join-Path $stage 'guide') /MIR /NFL /NDL /NJH /NP | Out-Null
$zip = Join-Path $dist "HachimiManga-$ver-win-x64.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path $stage -DestinationPath $zip -CompressionLevel Optimal

Write-Host "OK: $zip ($([Math]::Round((Get-Item $zip).Length/1MB,1)) MB)"
