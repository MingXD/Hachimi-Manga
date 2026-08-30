using CommunityToolkit.Mvvm.Input;
using MangaAgent.Core.Storage;

namespace MangaAgent.App.ViewModels;

/// <summary>ReaderViewModel：翻页/书签/跳转/模式切换（P1-2 拆分自 ReaderViewModel.cs，纯搬移，行为零变化）。</summary>
public partial class ReaderViewModel
{
    /// <summary>当前物理页（0-based）：单页 = 直接物理页；双页/滚动 = 当前逻辑页首物理页。
    /// v3 #17：滚动模式返回 SyncWebtoonPosition 同步的滚动位置（原返回逻辑页映射 → 续读/书签存旧页）。</summary>
    public int CurrentPhysical
    {
        get
        {
            if (ViewMode == ViewMode.Webtoon) return _currentPhysical;
            if (ViewMode == ViewMode.Single) return _currentPhysical;
            if (_logical.Count == 0) return 0;
            return LogicalFirstPhysical(Math.Clamp(CurrentIndex, 0, _logical.Count - 1));
        }
        private set => _currentPhysical = Math.Clamp(value, 0, Math.Max(0, _source.PageCount - 1));
    }

    /// <summary>逻辑页首物理页。</summary>
    private int LogicalFirstPhysical(int logicalIndex)
        => _logical.Count > 0 ? _logical[Math.Clamp(logicalIndex, 0, _logical.Count - 1)].FirstIndex : 0;

    /// <summary>定位到物理页：同步 CurrentIndex（单页模式直接设置；双页/滚动找包含它的逻辑页）。</summary>
    public void SetCurrentPhysical(int physical)
    {
        // 2C 修复：避免双重 LoadCurrentAsync（CurrentIndex setter 会触发一次；仅当其未变化时手动补一次）
        CurrentPhysical = physical;
        var target = physical;
        int prevIndex = CurrentIndex;
        int best = prevIndex;
        if (_logical.Count > 0)
        {
            for (int i = 0; i < _logical.Count; i++)
            {
                var lp = _logical[i];
                if (lp.FirstIndex <= target)
                {
                    best = i;
                    if (lp.SecondIndex >= 0 && target <= lp.SecondIndex) { best = i; break; }
                    if (lp.SecondIndex < 0 && lp.FirstIndex == target) { best = i; break; }
                }
                else break;
            }
            CurrentIndex = best;   // 若 best != prevIndex → setter 触发 OnCurrentIndexChanged → LoadCurrentAsync
        }
        if (ViewMode == ViewMode.Single) _currentPhysical = target;
        // 仅当 CurrentIndex 未变化（setter 未触发，如同逻辑页内换物理页）时手动刷新，避免重复解码
        if (best == prevIndex) _ = LoadCurrentAsync();
    }

    /// <summary>物理页 → 所在逻辑页索引。</summary>
    private int LogicalIndexOfPhysical(int physical)
    {
        if (_logical.Count == 0) return 0;
        for (int i = 0; i < _logical.Count; i++)
        {
            var p = _logical[i];
            if (p.SecondIndex >= 0) { if (p.FirstIndex <= physical && physical <= p.SecondIndex) return i; }
            else if (p.FirstIndex == physical) return i;
            if (p.FirstIndex > physical) break;
        }
        return 0;
    }

    [RelayCommand]
    private void NextPage()
    {
        // BUG-A v1.9：物理页导航 —— 单页模式逐物理页推进（修复跳页）；双页模式跳到下一逻辑页起点
        if (_logical.Count == 0) return;
        if (ViewMode == ViewMode.Single)
        {
            int phys = Math.Min(CurrentPhysical + 1, _source.PageCount - 1);
            SetCurrentPhysical(phys);
        }
        else if (CurrentIndex < TotalLogical - 1)
        {
            SetCurrentPhysical(LogicalFirstPhysical(CurrentIndex + 1));
        }
        App.SLog($"PAGE -> phys {CurrentPhysical + 1}/{_source.PageCount} logical {CurrentIndex + 1}/{TotalLogical} mode={ViewMode}");
        SaveState();
    }

    [RelayCommand]
    private void PrevPage()
    {
        if (_logical.Count == 0) return;
        if (ViewMode == ViewMode.Single)
        {
            int phys = Math.Max(CurrentPhysical - 1, 0);
            SetCurrentPhysical(phys);
        }
        else if (CurrentIndex > 0)
        {
            SetCurrentPhysical(LogicalFirstPhysical(CurrentIndex - 1));
        }
        App.SLog($"PAGE -> phys {CurrentPhysical + 1}/{_source.PageCount} logical {CurrentIndex + 1}/{TotalLogical} mode={ViewMode}");
        SaveState();
    }

    /// <summary>快速翻页：跳转到指定逻辑页（0-based），转成物理页定位。</summary>
    [RelayCommand]
    private void JumpToPage(int page)
    {
        if (_logical.Count == 0) return;
        int target = Math.Clamp(page, 0, _logical.Count - 1);
        SetCurrentPhysical(LogicalFirstPhysical(target));
        App.SLog($"JUMP -> logical {target + 1}/{TotalLogical} phys {CurrentPhysical + 1}");
        SaveState();
    }

    /// <summary>快速翻页：跳转到指定物理页（Webtoon 用），转成逻辑页后跳转。</summary>
    [RelayCommand]
    private void JumpToPhysical(int physical)
    {
        if (_logical.Count == 0) return;
        int target = Math.Clamp(physical, 0, _source.PageCount - 1);
        SetCurrentPhysical(target);
        global::MangaAgent.App.App.SLog($"JUMP_PHYS -> physical {target + 1} logical {CurrentIndex + 1}/{TotalLogical}");
        SaveState();
    }

    [RelayCommand]
    private void ToggleMode()
    {
        _layoutVersion++;
        ViewMode = ViewMode switch
        {
            ViewMode.Single => ViewMode.Double,
            ViewMode.Double => ViewMode.Webtoon,
            _ => ViewMode.Single,
        };
        global::MangaAgent.App.App.SLog($"MODE -> {ViewMode} (index={CurrentIndex} logical={TotalLogical})");
        SaveState();
    }

    /// <summary>直接设置阅读模式（下拉菜单三选一，v1.8）。</summary>
    public void SetViewMode(ViewMode mode)
    {
        if (ViewMode == mode) return;
        _layoutVersion++;
        ViewMode = mode;
        global::MangaAgent.App.App.SLog($"MODE -> {mode} (index={CurrentIndex} logical={TotalLogical})");
        SaveState();
    }

    [RelayCommand] private void ToggleDirection() { Direction = Direction == ReadingDirection2.Rtl ? ReadingDirection2.Ltr : ReadingDirection2.Rtl; SaveState(); }

    /// <summary>直接设置阅读方向（下拉菜单显式选择）。</summary>
    public void SetDirection(ReadingDirection2 dir)
    {
        if (Direction == dir) return;
        Direction = dir;
        SaveState();
    }

    [RelayCommand]
    private void ToggleBookmark()
    {
        var page = CurrentPhysical;   // BUG-A v1.9：书签存物理页（跨模式稳定）
        if (Bookmarks.Contains(page)) _state.RemoveBookmark(Book.Path, page);
        else _state.AddBookmark(Book.Path, page);
        LoadBookmarks();
    }

    private void LoadBookmarks()
    {
        Bookmarks.Clear();
        foreach (var b in _state.LoadBookmarks(Book.Path)) Bookmarks.Add(b);
    }

    [RelayCommand]
    private void NextBookmark()
    {
        if (Bookmarks.Count == 0) return;
        var next = Bookmarks.FirstOrDefault(b => b > CurrentPhysical);
        if (next == 0 && !Bookmarks.Contains(0)) next = Bookmarks[0];
        SetCurrentPhysical(next);
        SaveState();
    }

    [RelayCommand]
    private void PrevBookmark()
    {
        if (Bookmarks.Count == 0) return;
        var prev = Bookmarks.LastOrDefault(b => b < CurrentPhysical);
        if (prev == 0 && !Bookmarks.Contains(0)) prev = Bookmarks[^1];
        SetCurrentPhysical(prev);
        SaveState();
    }

    [RelayCommand]
    private void JumpToBookmark(int page)
    {
        if (page >= 0 && page < TotalLogical) { CurrentIndex = page; SaveState(); }
    }

    /// <summary>一键首页（v3 #8）：单页=物理页0；双页/滚动=逻辑页0（封面）。滚动模式的视图滚动由视图层负责。</summary>
    [RelayCommand]
    private void GoHome()
    {
        if (_logical.Count == 0) return;
        if (ViewMode == ViewMode.Single || ViewMode == ViewMode.Webtoon) SetCurrentPhysical(0);
        else JumpToPage(0);
        App.SLog($"HOME -> phys {CurrentPhysical + 1}/{_source.PageCount}");
        SaveState();
    }

    /// <summary>一键末页（v3 #8）：单页=最后一物理页；双页=最后逻辑页；滚动=最后一物理页。</summary>
    [RelayCommand]
    private void GoEnd()
    {
        if (_logical.Count == 0) return;
        if (ViewMode == ViewMode.Single) SetCurrentPhysical(_source.PageCount - 1);
        else if (ViewMode == ViewMode.Webtoon) SetCurrentPhysical(_source.PageCount - 1);
        else JumpToPage(_logical.Count - 1);
        App.SLog($"END -> phys {CurrentPhysical + 1}/{_source.PageCount}");
        SaveState();
    }
}
