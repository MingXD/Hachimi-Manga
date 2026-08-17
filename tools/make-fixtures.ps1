# make-fixtures.ps1 — 生成漫画阅读器测试夹具（样本 CBZ 库）
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing | Out-Null
Add-Type -AssemblyName System.IO.Compression | Out-Null
Add-Type -AssemblyName System.IO.Compression.FileSystem | Out-Null

function New-ComicPage([int]$w, [int]$h, [int]$pageNo, [System.Drawing.Color]$bg, [System.Drawing.Color]$ink, [int]$barCount) {
  $bmp = New-Object System.Drawing.Bitmap($w, $h)
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.Clear($bg)
  $colors = @(
    ([System.Drawing.Color]::FromArgb(220,60,60)),
    ([System.Drawing.Color]::FromArgb(60,200,90)),
    ([System.Drawing.Color]::FromArgb(70,120,240)),
    ([System.Drawing.Color]::FromArgb(240,200,40)),
    ([System.Drawing.Color]::FromArgb(180,80,220)),
    ([System.Drawing.Color]::FromArgb(40,200,200))
  )
  $barH = 24
  for ($b = 0; $b -lt $barCount; $b++) {
    $brush = New-Object System.Drawing.SolidBrush($colors[$b % $colors.Length])
    $g.FillRectangle($brush, 0, $b * $barH, $w, $barH)
    $brush.Dispose()
  }
  $font = New-Object System.Drawing.Font("Arial", 110, [System.Drawing.FontStyle]::Bold)
  $brush2 = New-Object System.Drawing.SolidBrush($ink)
  $g.DrawString($pageNo.ToString(), $font, $brush2, ($w - 220), ($h - 180))
  $font.Dispose(); $brush2.Dispose()
  $ms = New-Object System.IO.MemoryStream
  $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
  $g.Dispose(); $bmp.Dispose()
  return $ms.ToArray()
}

function New-Cbz([string]$path, [System.Collections.Generic.List[object]]$pages, [string]$comicInfo) {
  $dir = Split-Path $path -Parent
  if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
  if (Test-Path $path) { Remove-Item $path -Force }
  $zip = [System.IO.Compression.ZipFile]::Open($path, [System.IO.Compression.ZipArchiveMode]::Create)
  try {
    for ($i = 0; $i -lt $pages.Count; $i++) {
      $entry = $zip.CreateEntry(("{0:D4}.png" -f ($i+1)), [System.IO.Compression.CompressionLevel]::Optimal)
      $es = $entry.Open(); $es.Write($pages[$i], 0, $pages[$i].Length); $es.Dispose()
    }
    if ($comicInfo) {
      $e = $zip.CreateEntry("ComicInfo.xml")
      $s = $e.Open(); $b = [System.Text.Encoding]::UTF8.GetBytes($comicInfo); $s.Write($b, 0, $b.Length); $s.Dispose()
    }
  } finally { $zip.Dispose() }
}

$libRoot = Join-Path $PSScriptRoot "..\reference\fixtures\library"
if (Test-Path $libRoot) { Remove-Item $libRoot -Recurse -Force }

function New-Series([string]$seriesName, [int]$volumes, [int]$pagesPerVol, [System.Drawing.Color]$bg, [System.Drawing.Color]$ink, [bool]$includeSpreads, [bool]$includeInfo) {
  for ($v = 1; $v -le $volumes; $v++) {
    $volPages = New-Object System.Collections.Generic.List[object]
    $total = $pagesPerVol
    if ($includeSpreads) { $total += 3 }
    for ($i = 1; $i -le $total; $i++) {
      if ($includeSpreads -and ($i -eq 8 -or $i -eq 16 -or $i -eq 24)) {
        $volPages.Add((New-ComicPage 1600 1200 $i $bg $ink (($i % 6) + 1)))
      } else {
        $volPages.Add((New-ComicPage 800 1200 $i $bg $ink (($i % 6) + 1)))
      }
    }
    $info = $null
    if ($includeInfo) {
      $info = "<ComicInfo><Title>Volume $v</Title><Series>$seriesName</Series><Volume>$v</Volume><Manga>YesAndRightToLeft</Manga><PageCount>$total</PageCount></ComicInfo>"
    }
    $cbzName = $seriesName + "_vol" + $v + ".cbz"
    New-Cbz (Join-Path $libRoot ($seriesName + "\" + $cbzName)) $volPages $info
    Write-Output ("  series {0} vol {1} done" -f $seriesName, $v)
  }
}

Write-Output "generating fixtures..."
New-Series "TestSeriesA" 3 30 ([System.Drawing.Color]::FromArgb(250,250,248)) ([System.Drawing.Color]::FromArgb(40,40,40)) $true $true
New-Series "TestSeriesB" 2 20 ([System.Drawing.Color]::FromArgb(238,244,252)) ([System.Drawing.Color]::FromArgb(20,50,120)) $false $true
New-Series "TestSeriesC" 1 12 ([System.Drawing.Color]::FromArgb(250,242,238)) ([System.Drawing.Color]::FromArgb(120,30,30)) $false $false

Write-Output "stress book..."
$stressPages = New-Object System.Collections.Generic.List[object]
Write-Output ("DEBUG stressPages-null=" + ($null -eq $stressPages))
for ($i = 1; $i -le 300; $i++) {
  $shade = [byte](30 + ($i * 7) % 180)
  $pg = New-ComicPage 800 1200 $i ([System.Drawing.Color]::FromArgb($shade,$shade,$shade)) ([System.Drawing.Color]::FromArgb(255,255,255)) (($i % 6) + 1)
  $stressPages.Add($pg)
}
Write-Output ("DEBUG stressPages count=" + $stressPages.Count)
New-Cbz (Join-Path $libRoot "Stress\stress_300p.cbz") $stressPages $null
Write-Output "  stress book done"

Write-Output "folder book..."
$folderBook = Join-Path $libRoot "TestSeriesA\TestSeriesA_vol1_folder"
New-Item -ItemType Directory -Path $folderBook -Force | Out-Null
$zip = [System.IO.Compression.ZipFile]::OpenRead((Join-Path $libRoot "TestSeriesA\TestSeriesA_vol1.cbz"))
try {
  foreach ($entry in $zip.Entries) {
    if ($entry.Name -like "*.png") {
      $es = $entry.Open(); $ms = New-Object System.IO.MemoryStream; $es.CopyTo($ms); $es.Dispose()
      [System.IO.File]::WriteAllBytes((Join-Path $folderBook $entry.Name), $ms.ToArray())
    }
  }
} finally { $zip.Dispose() }
Write-Output "  folder book done"

Write-Output "Fixtures generated at: $libRoot"
$files = Get-ChildItem $libRoot -Recurse -File
$sum = ($files | Measure-Object -Property Length -Sum).Sum
Write-Output ("files: {0}  total: {1:N1} MB" -f $files.Count, ($sum/1MB))
