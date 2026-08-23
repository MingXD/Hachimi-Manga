# 拆分 ReaderWindow.axaml.cs 为 partial 文件（code-audit P1-1）
$ErrorActionPreference = "Stop"
$dir = "D:\MangaAgent\app\src\MangaAgent.App\Views"
$main = Join-Path $dir "ReaderWindow.axaml.cs"
$bak = Join-Path $dir "ReaderWindow.axaml.cs.bak"
Copy-Item $main $bak -Force
$content = Get-Content $main -Encoding UTF8

$usings = @'
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using MangaAgent.App.Services;
using MangaAgent.App.ViewModels;
using MangaAgent.Core.Storage;
'@

# 方法 → @(startLine0, endLine0)；含单行表达式体
$map = @{}
$i = 0
while ($i -lt $content.Count) {
    $line = $content[$i]
    if ($line -notmatch '^\s*//' -and $line -match '^\s*(private|public|protected|internal)\s+(async\s+)?[\w<>\[\],.? ]+\s+(\w+)\s*\(') {
        $name = $Matches[3]
        if ($name -in @("OnPageTextClick","ShowJumpDialog","OnKeyDown")) { Write-Output "TRACE-ENTER: idx=$i name=$name" }
        # 单行（表达式体/字段式，行尾 ;）→ 不做花括号扫描（防误吞后续方法）
        if ($line -match ';\s*$') { if ($name -in @("OnPageTextClick")) { Write-Output "  -> single-line" }; $map[$name] = @($i, $i); $i++; continue }
        # 两行表达式体：签名行 + '=> ...;' 行
        $nextIsArrow = $false
        if ($i + 1 -lt $content.Count) { $nextIsArrow = $content[$i + 1] -match '^\s*=>' }
        if ($nextIsArrow) { if ($name -in @("OnPageTextClick")) { Write-Output "  -> two-line, i=$i -> $($i + 2)" }; $map[$name] = @($i, $i + 1); $i += 2; continue }
        # 多行方法：从签名行起找第一个 '{'（可能换行才出现）
        $brace = -1
        for ($j = $i; $j -lt $content.Count; $j++) {
            if ($content[$j].Contains('{')) { $brace = $j; break }
        }
        if ($brace -ge 0) {
            $depth = 0
            $end = -1
            for ($j = $brace; $j -lt $content.Count; $j++) {
                $l = $content[$j]
                $depth += ([regex]::Matches($l, '\{')).Count - ([regex]::Matches($l, '\}')).Count
                if ($depth -le 0) { $end = $j; break }
            }
            if ($end -ge 0) { $map[$name] = @($i, $end); $i = $end + 1; continue }
        }
        # 单行表达式体（=> ...;，未以分号结尾的兜底）
        if ($line -match '=>') { $map[$name] = @($i, $i) }
        $i++
    } else { $i++ }
}

# 分类
Write-Output "DIAG: map.Count=$($map.Count) hasShowJumpDialog=$($map.ContainsKey('ShowJumpDialog')) hasOnPageTextClick=$($map.ContainsKey('OnPageTextClick')) hasToggleDebug=$($map.ContainsKey('ToggleDebugPanel'))"
$groups = @{
    "ReaderWindow.Canvas.cs" = @("RunZoomDiagnostics","ComputeBaseW","FitScreenScale","UpdateZoomPercent","CurrentFitScale","ApplyTransform","Relayout","ZoomTo","ClampToViewportIfSmaller","ZoomRelative","ContentToScreen","ContentScreenRect","EffectiveFocus","ZoomToFocus","OnViewportSizeChanged","ResetPagedView","ArrangeDoublePageOrder","OnViewportDoubleTapped")
    "ReaderWindow.Webtoon.cs" = @("EnterWebtoon","FitWebtoonHeight","FitWebtoonWidth","FitWebtoonToPage","ClearWebtoonImages","ExitWebtoon","ClearWebtoonPlaceholders","EnsureWebtoonPlaceholder","RemoveWebtoonPlaceholder","PageOffset","BuildWebtoonOffsets","PhysicalAt","UpdateWebtoonPages","TryRefreshWebtoonImage","RemoveWebtoonImage","LoadWebtoonImageAsync","RetryWebtoonImageAsync","ScrollWebtoonToPhysical","ScrollWebtoonByEvent","AddScrollVelocity","CancelSmoothScroll","StartSmoothScroll","SmoothScrollTick","ZoomWebtoon","ClampWebtoonScroll","ResetWebtoonView","WebtoonJumpPage","ScrollWebtoonPages")
    "ReaderWindow.Preview.cs" = @("OnPreviewStripPointerMoved","OnPreviewStripPointerExited","StartHoverTimer","StopHoverTimer","HidePreviewStripDelayed","IsPointerOver","ShowPreviewStrip","RefreshThumbStrip","OnThumbStripItemClick","OnPreviewStripPrev","OnPreviewStripNext","OnPreviewStripWheel","SyncPreviewStripToScroll","TogglePreviewStrip")
    "ReaderWindow.Input.cs" = @("OnPointerWheelChanged","OnViewportPointerPressed","StartDrag","IsOverPreviewStrip","OnViewportPointerMoved","OnViewportPointerReleased","OnZoomInClick","OnSensitivityClick","OnWebtoonSpeedClick","OnZoomOutClick","OnResetZoomClick","OnNavPrevClick","OnNavNextClick","OnModeMenuClick","OnDirectionMenuClick","OnDirectionMenuItemClick","OnModeMenuItemClick","OnPageTextClick","OnKeyDown","ResolveAction","TryMatch","ParseAction","ExecuteAction")
    "ReaderWindow.Dialog.cs" = @("ToggleDebugPanel","OnSettingsClick","ShowJumpDialog")
}

# 移动：先收集要删除的行号，再生成 partial 文件
$allMoved = @{}
$removedLines = @{}
foreach ($file in $groups.Keys) {
    $block = New-Object System.Collections.Generic.List[string]
    $block.Add($usings)
    $block.Add("")
    $block.Add("namespace MangaAgent.App.Views;")
    $block.Add("")
    $block.Add("/// <summary>ReaderWindow partial —— $($file -replace 'ReaderWindow\.' -replace '\.cs','') 区域（code-audit P1 分文件，纯搬移）。</summary>")
    $block.Add("public partial class ReaderWindow : Window")
    $block.Add("{")
    foreach ($m in $groups[$file]) {
        if (-not $map.ContainsKey($m)) { Write-Error "未找到方法: $m"; exit 1 }
        $range = $map[$m]
        for ($k = $range[0]; $k -le $range[1]; $k++) { $block.Add($content[$k]) }
        $block.Add("")
        $removedLines[$m] = $range
        $allMoved[$m] = $file
    }
    $block.Add("}")
    [System.IO.File]::WriteAllLines((Join-Path $dir $file), $block, [System.Text.UTF8Encoding]::new($false))
    Write-Output "生成 $file（$($groups[$file].Count) 个方法）"
}

# 从主文件删除已移动方法（按行号从大到小）
$sorted = $removedLines.Values | Sort-Object { $_[0] } -Descending
foreach ($range in $sorted) {
    $content = @($content[0..($range[0]-1)]) + @($content[($range[1]+1)..($content.Count-1)])
}
[System.IO.File]::WriteAllLines($main, $content, [System.Text.UTF8Encoding]::new($false))
Write-Output "主文件剩余行数: $($content.Count)（原 $((Get-Content $bak).Count)）"
Write-Output "完成。"
