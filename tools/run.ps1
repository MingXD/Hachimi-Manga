# run.ps1 - launch MangaAgent (dev build)
# Usage: right-click "Run with PowerShell", or: powershell -ExecutionPolicy Bypass -File tools\run.ps1
$ErrorActionPreference = "Stop"
$exe = "D:\MangaAgent\app\src\MangaAgent.App\bin\Debug\net9.0\MangaAgent.App.exe"
if (-not (Test-Path $exe)) {
  Write-Host "build output not found, building..." -ForegroundColor Yellow
  $dotnet = "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe"
  & $dotnet build "D:\MangaAgent\app\src\MangaAgent.App" -c Debug
  if ($LASTEXITCODE -ne 0) { Write-Host "build failed" -ForegroundColor Red; exit 1 }
}
Write-Host "launching MangaAgent..." -ForegroundColor Green
& $exe
