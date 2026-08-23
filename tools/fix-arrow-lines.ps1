# 修复 split 遗留：把主文件中孤立的 => 行移回对应 partial（code-audit P1-1 修正）
$ErrorActionPreference = "Stop"
$dir = "D:\MangaAgent\app\src\MangaAgent.App\Views"
$main = Join-Path $dir "ReaderWindow.axaml.cs"
$canvas = Join-Path $dir "ReaderWindow.Canvas.cs"
$input = Join-Path $dir "ReaderWindow.Input.cs"

function Find-Index([string[]]$arr, [string]$pattern) {
    for ($k = 0; $k -lt $arr.Count; $k++) { if ($arr[$k] -match $pattern) { return $k } }
    throw "未找到: $pattern"
}

$m = Get-Content $main -Encoding UTF8
$c = Get-Content $canvas -Encoding UTF8
$i = Get-Content $input -Encoding UTF8

# 1) ContentToScreen 的 => 行 → Canvas（签名后）
$arrow1 = $null
$newM = @()
foreach ($line in $m) {
    if ($line -match '^\s*=> new\(_scale \* local\.X') { $arrow1 = $line; continue }
    $newM += $line
}
$m = $newM
if ($arrow1 -ne $null) {
    $idx = Find-Index $c 'public Avalonia\.Point ContentToScreen'
    $c = @($c[0..$idx]) + @($arrow1) + @($c[($idx+1)..($c.Count-1)])
}

# 2) OnPageTextClick 的 => 行 → Input（签名后）
$arrow2 = $null
$newM = @()
foreach ($line in $m) {
    if ($line -match '^\s*=> ShowJumpDialog\(\);') { $arrow2 = $line; continue }
    $newM += $line
}
$m = $newM
if ($arrow2 -ne $null) {
    $idx2 = Find-Index $i 'private void OnPageTextClick'
    $i = @($i[0..$idx2]) + @($arrow2) + @($i[($idx2+1)..($i.Count-1)])
}

# 3) ParseAction 的 => 行 → Input（签名后）
$arrow3 = $null
$newM = @()
foreach ($line in $m) {
    if ($line -match '^\s*=> Enum\.TryParse') { $arrow3 = $line; continue }
    $newM += $line
}
$m = $newM
if ($arrow3 -ne $null) {
    $idx3 = Find-Index $i 'private static ReaderAction\? ParseAction'
    $i = @($i[0..$idx3]) + @($arrow3) + @($i[($idx3+1)..($i.Count-1)])
}

[System.IO.File]::WriteAllLines($main, $m, [System.Text.UTF8Encoding]::new($false))
[System.IO.File]::WriteAllLines($canvas, $c, [System.Text.UTF8Encoding]::new($false))
[System.IO.File]::WriteAllLines($input, $i, [System.Text.UTF8Encoding]::new($false))
Write-Output "done. main=$($m.Count) canvas=$($c.Count) input=$($i.Count)"
