using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using MangaAgent.App.Services;
using MangaAgent.App.ViewModels;
using MangaAgent.App.Views;
using MangaAgent.Core.Storage;
using static MangaAgent.Tests.PoC.ReaderTestFixtures;

namespace MangaAgent.Tests.PoC;

/// <summary>设置系统回归（P1-3 拆自 ScrollModeFixesTests）：
/// 设置存取往返/介绍生成、快捷键重绑、组合键、分页滚轮=翻页、LTR/RTL 分组、默认模式+续读。</summary>
public class SettingsSystemTests
{
    /// <summary>设置存取往返 + 使用介绍默认生成。</summary>
    [AvaloniaFact]
    public void SettingsStore_RoundTrip_AndGuideGenerated()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), $"manga_set_{Guid.NewGuid():N}");
        var old = SettingsStore.BaseDir;
        SettingsStore.BaseDir = baseDir;
        try
        {
            var s = new AppSettings { ZoomSensitivity = 1.5, WebtoonSpeed = 1.2, ScrollFriction = 8, CacheCap = 16, InvertScroll = true, PagedWheelMode = "Page" };
            s.Hotkeys["Webtoon"][nameof(ReaderAction.WebtoonNextPage)] = new() { "X" };
            s.DefaultMode = "Webtoon";
            s.ResumePosition = false;
            s.DoubleClickAction = "Reset";
            s.DefaultFitMode = "FitWidth";
            SettingsStore.Save(s);
            var loaded = SettingsStore.Load();
            Assert.Equal(1.5, loaded.ZoomSensitivity);
            Assert.Equal(1.2, loaded.WebtoonSpeed);
            Assert.Equal(8, loaded.ScrollFriction);
            Assert.Equal(16, loaded.CacheCap);
            Assert.True(loaded.InvertScroll);
            Assert.Equal("Page", loaded.PagedWheelMode);
            Assert.Equal("Webtoon", loaded.DefaultMode);
            Assert.False(loaded.ResumePosition);
            Assert.Equal("Reset", loaded.DoubleClickAction);
            Assert.Equal("FitWidth", loaded.DefaultFitMode);
            Assert.Equal(new[] { "X" }, loaded.Hotkeys["Webtoon"][nameof(ReaderAction.WebtoonNextPage)]);
            Assert.True(File.Exists(SettingsStore.GuidePath), "首次运行应生成默认使用介绍");
        }
        finally
        {
            SettingsStore.BaseDir = old;
            try { Directory.Delete(baseDir, true); } catch { }
        }
    }

    /// <summary>快捷键重绑生效：把滚动"下一页整页跳"绑到 X → X 触发、Right 不再触发。</summary>
    [AvaloniaFact]
    public async Task HotkeyRebind_ChangesAction()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), $"manga_set_{Guid.NewGuid():N}");
        var old = SettingsStore.BaseDir;
        SettingsStore.BaseDir = baseDir;
        try
        {
            var s = new AppSettings();
            s.Hotkeys["Webtoon"][nameof(ReaderAction.WebtoonNextPage)] = new() { "X" };
            SettingsStore.Save(s);

            var db = Path.Combine(Path.GetTempPath(), $"manga_wtf18_{Guid.NewGuid():N}.db");
            var vm = new ReaderViewModel(StressBook, db);
            await WaitUntilAsync(() => vm.Image1 is not null);
            var win = await OpenWebtoonAsync(vm);
            await WaitUntilAsync(() => win.WebtoonInstancedCount >= 1, 8000);

            // Right 已解绑 → 不应整页跳
            win.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.None);
            await Task.Delay(200);
            Assert.True(Math.Abs(win.CanvasY) < 15, $"解绑后 → 不应跳页（ty={win.CanvasY:F0}）");

            // X → 整页跳
            win.KeyPressQwerty(PhysicalKey.X, RawInputModifiers.None);
            await Task.Delay(200);
            var vp = win.ViewportSize;
            double pageH = Math.Min(vp.Width, vp.Height * PageRatio) / PageRatio;
            Assert.True(Math.Abs(win.CanvasY - (-pageH)) < 15,
                $"重绑 X 应整页跳（ty={win.CanvasY:F0} 期望 {-pageH:F0}）");
            win.Close();
            await vm.DisposeAsync();
        }
        finally
        {
            SettingsStore.BaseDir = old;
            try { Directory.Delete(baseDir, true); } catch { }
        }
    }

    /// <summary>分页滚轮行为=翻页：分页模式滚轮翻页而非缩放。</summary>
    [AvaloniaFact]
    public async Task PagedWheelMode_Page_Pages()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), $"manga_set_{Guid.NewGuid():N}");
        var old = SettingsStore.BaseDir;
        SettingsStore.BaseDir = baseDir;
        try
        {
            var s = new AppSettings { PagedWheelMode = "Page" };
            SettingsStore.Save(s);

            var db = Path.Combine(Path.GetTempPath(), $"manga_wtf19_{Guid.NewGuid():N}.db");
            var vm = new ReaderViewModel(StressBook, db);
            await WaitUntilAsync(() => vm.Image1 is not null);
            var win = new ReaderWindow { DataContext = vm };
            win.Show();
            win.Measure(new Size(1100, 800));
            win.Arrange(new Rect(0, 0, 1100, 800));

            int p0 = vm.CurrentPhysical;
            win.MouseWheel(new Point(550, 400), new Vector(0, -120), RawInputModifiers.None);   // 向下滚 → 下一页
            await Task.Delay(300);
            Assert.True(vm.CurrentPhysical == p0 + 1, $"分页滚轮=翻页：向下应下一页（{p0 + 1} -> {vm.CurrentPhysical + 1}）");
            win.Close();
            await vm.DisposeAsync();
        }
        finally
        {
            SettingsStore.BaseDir = old;
            try { Directory.Delete(baseDir, true); } catch { }
        }
    }

    /// <summary>v2 回归：分页 LTR/RTL 分组独立 —— 默认 RTL：←=下一页；切 LTR 后：←=上一页。</summary>
    [AvaloniaFact]
    public async Task Paged_DirectionGroups_Separate()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), $"manga_set_{Guid.NewGuid():N}");
        var old = SettingsStore.BaseDir;
        SettingsStore.BaseDir = baseDir;
        try
        {
            var db = Path.Combine(Path.GetTempPath(), $"manga_wtf20_{Guid.NewGuid():N}.db");
            var vm = new ReaderViewModel(StressBook, db);
            await WaitUntilAsync(() => vm.Image1 is not null);
            var win = new ReaderWindow { DataContext = vm };
            win.Show();
            win.Measure(new Size(1100, 800));
            win.Arrange(new Rect(0, 0, 1100, 800));
            vm.SetDirection(ReadingDirection2.Rtl);
            await Task.Delay(200);

            // RTL：← = 下一页
            int p0 = vm.CurrentPhysical;
            win.KeyPressQwerty(PhysicalKey.ArrowLeft, RawInputModifiers.None);
            await Task.Delay(300);
            Assert.True(vm.CurrentPhysical == p0 + 1, $"RTL ← 应下一页（{p0 + 1} -> {vm.CurrentPhysical + 1}）");

            // 切 LTR：← = 上一页
            vm.SetDirection(ReadingDirection2.Ltr);
            await Task.Delay(200);
            int p1 = vm.CurrentPhysical;
            win.KeyPressQwerty(PhysicalKey.ArrowLeft, RawInputModifiers.None);
            await Task.Delay(300);
            Assert.True(vm.CurrentPhysical == p1 - 1, $"LTR ← 应上一页（{p1 + 1} -> {vm.CurrentPhysical + 1}）");
            win.Close();
            await vm.DisposeAsync();
        }
        finally
        {
            SettingsStore.BaseDir = old;
            try { Directory.Delete(baseDir, true); } catch { }
        }
    }

    /// <summary>v2 回归：组合键（Ctrl+Shift+P）绑定生效；解绑的普通键不再触发。</summary>
    [AvaloniaFact]
    public async Task ComboKey_Binding_Works()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), $"manga_set_{Guid.NewGuid():N}");
        var old = SettingsStore.BaseDir;
        SettingsStore.BaseDir = baseDir;
        try
        {
            var s = new AppSettings();
            s.Hotkeys["Webtoon"][nameof(ReaderAction.WebtoonNextPage)] = new() { "Ctrl+Shift+P" };
            SettingsStore.Save(s);

            var db = Path.Combine(Path.GetTempPath(), $"manga_wtf21_{Guid.NewGuid():N}.db");
            var vm = new ReaderViewModel(StressBook, db);
            await WaitUntilAsync(() => vm.Image1 is not null);
            var win = await OpenWebtoonAsync(vm);
            await WaitUntilAsync(() => win.WebtoonInstancedCount >= 1, 8000);

            // Right 已解绑 → 不跳
            win.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.None);
            await Task.Delay(200);
            Assert.True(Math.Abs(win.CanvasY) < 15, $"解绑后 → 不应跳页（ty={win.CanvasY:F0}）");

            // Ctrl+Shift+P → 整页跳
            win.KeyPressQwerty(PhysicalKey.P, RawInputModifiers.Control | RawInputModifiers.Shift);
            await Task.Delay(200);
            var vp = win.ViewportSize;
            double pageH = Math.Min(vp.Width, vp.Height * PageRatio) / PageRatio;
            Assert.True(Math.Abs(win.CanvasY - (-pageH)) < 15,
                $"组合键应整页跳（ty={win.CanvasY:F0} 期望 {-pageH:F0}）");

            // 单独 P（无修饰）不应触发
            double yAfter = win.CanvasY;
            win.KeyPressQwerty(PhysicalKey.P, RawInputModifiers.None);
            await Task.Delay(200);
            Assert.True(Math.Abs(win.CanvasY - yAfter) < 15, $"单独 P 不应触发组合键绑定（ty={win.CanvasY:F0}）");
            win.Close();
            await vm.DisposeAsync();
        }
        finally
        {
            SettingsStore.BaseDir = old;
            try { Directory.Delete(baseDir, true); } catch { }
        }
    }

    /// <summary>v5.2 回归：默认阅读模式 + 续读开关（设置注入静态，须复位）。</summary>
    [AvaloniaFact]
    public async Task DefaultMode_And_ResumePosition_Apply()
    {
        var db = Path.Combine(Path.GetTempPath(), $"manga_wtf22_{Guid.NewGuid():N}.db");
        var oldMode = ReaderViewModel.DefaultMode;
        var oldResume = ReaderViewModel.ResumePosition;
        try
        {
            // 1) 制造保存状态：翻到物理页 5 并保存
            ReaderViewModel.ResumePosition = true;
            ReaderViewModel.DefaultMode = ViewMode.Single;
            var vm1 = new ReaderViewModel(StressBook, db);
            await WaitUntilAsync(() => vm1.Image1 is not null);
            for (int i = 0; i < 5; i++) vm1.NextPageCommand.Execute(null);
            await Task.Delay(300);
            await vm1.DisposeAsync();

            // 2) 续读开 + 默认模式 Single → 恢复物理页 5
            ReaderViewModel.DefaultMode = ViewMode.Single;
            ReaderViewModel.ResumePosition = true;
            var vm2 = new ReaderViewModel(StressBook, db);
            await WaitUntilAsync(() => vm2.Image1 is not null);
            Assert.Equal(5, vm2.CurrentPhysical);
            Assert.Equal(ViewMode.Single, vm2.ViewMode);
            await vm2.DisposeAsync();

            // 3) 续读关 + 默认模式 Webtoon → 从第 1 页、滚动模式
            ReaderViewModel.DefaultMode = ViewMode.Webtoon;
            ReaderViewModel.ResumePosition = false;
            var vm3 = new ReaderViewModel(StressBook, db);
            await WaitUntilAsync(() => vm3.Image1 is not null);
            Assert.Equal(ViewMode.Webtoon, vm3.ViewMode);
            Assert.Equal(0, vm3.CurrentPhysical);
            await vm3.DisposeAsync();
        }
        finally
        {
            ReaderViewModel.DefaultMode = oldMode;
            ReaderViewModel.ResumePosition = oldResume;
            try { File.Delete(db); } catch { }
        }
    }

    /// <summary>v3 #2a 回归：滚动模式续读不再丢失（原实现 SaveState 把 Webtoon 存成 Single → 恢复成单页）。</summary>
    [AvaloniaFact]
    public async Task Resume_Webtoon_Mode_Preserved()
    {
        var db = Path.Combine(Path.GetTempPath(), $"manga_wtf23_{Guid.NewGuid():N}.db");
        var oldMode = ReaderViewModel.DefaultMode;
        var oldResume = ReaderViewModel.ResumePosition;
        try
        {
            // 1) 打开 → 切到滚动模式 → 翻几页 → 保存（Dispose 触发 SaveState）
            ReaderViewModel.ResumePosition = true;
            ReaderViewModel.DefaultMode = ViewMode.Single;
            var vm1 = new ReaderViewModel(StressBook, db);
            await WaitUntilAsync(() => vm1.Image1 is not null);
            vm1.ToggleModeCommand.Execute(null);   // Single -> Double
            vm1.ToggleModeCommand.Execute(null);   // Double -> Webtoon
            await WaitUntilAsync(() => vm1.ViewMode == ViewMode.Webtoon);
            for (int i = 0; i < 10; i++) vm1.NextPageCommand.Execute(null);
            await Task.Delay(300);
            await vm1.DisposeAsync();

            // 2) 重新打开（续读开）→ 应恢复 Webtoon 模式
            ReaderViewModel.DefaultMode = ViewMode.Single;
            ReaderViewModel.ResumePosition = true;
            var vm2 = new ReaderViewModel(StressBook, db);
            await WaitUntilAsync(() => vm2.ViewMode == ViewMode.Webtoon);
            Assert.Equal(ViewMode.Webtoon, vm2.ViewMode);
            await vm2.DisposeAsync();
        }
        finally
        {
            ReaderViewModel.DefaultMode = oldMode;
            ReaderViewModel.ResumePosition = oldResume;
            try { File.Delete(db); } catch { }
        }
    }

    /// <summary>v3 #4 回归：续读恢复页尺寸必须同步加载（滚动模式 fit 用真实比例，否则 0.7 占位 → 页面"缩小但显示 100%"）。</summary>
    [AvaloniaFact]
    public async Task Resume_Webtoon_RestorePage_Size_Loaded_Sync()
    {
        var db = Path.Combine(Path.GetTempPath(), $"manga_wtf24_{Guid.NewGuid():N}.db");
        var oldMode = ReaderViewModel.DefaultMode;
        var oldResume = ReaderViewModel.ResumePosition;
        try
        {
            // 1) 翻到后半段（如物理页 200）并保存
            ReaderViewModel.ResumePosition = true;
            ReaderViewModel.DefaultMode = ViewMode.Single;
            var vm1 = new ReaderViewModel(StressBook, db);
            await WaitUntilAsync(() => vm1.Image1 is not null);
            for (int i = 0; i < 200; i++) vm1.NextPageCommand.Execute(null);
            await Task.Delay(300);
            await vm1.DisposeAsync();

            // 2) 重新打开：恢复物理页应在 200 附近，且其尺寸已同步加载（RatioOfPage != 0.7 占位）
            var vm2 = new ReaderViewModel(StressBook, db);
            int phys = vm2.CurrentPhysical;
            Assert.True(phys >= 190, $"恢复物理页应在 200 附近（实际 {phys}）");
            Assert.True(Math.Abs(vm2.RatioOfPage(phys) - 0.7) > 0.01,
                $"恢复页尺寸应已同步加载（RatioOfPage={vm2.RatioOfPage(phys):F3}，不应是 0.7 占位）");
            await vm2.DisposeAsync();
        }
        finally
        {
            ReaderViewModel.DefaultMode = oldMode;
            ReaderViewModel.ResumePosition = oldResume;
            try { File.Delete(db); } catch { }
        }
    }

    /// <summary>v3 #17 回归：滚动模式下滚动位置必须持久化（原实现只更新页码显示，续读/书签存旧页）。</summary>
    [AvaloniaFact]
    public async Task Webtoon_Scroll_Position_Persisted_On_Resume()
    {
        var db = Path.Combine(Path.GetTempPath(), $"manga_wtf25_{Guid.NewGuid():N}.db");
        var oldMode = ReaderViewModel.DefaultMode;
        var oldResume = ReaderViewModel.ResumePosition;
        try
        {
            // 1) 打开 → 切滚动模式 → 模拟滚动到物理页 250 → 关闭（SaveState 存 250）
            ReaderViewModel.ResumePosition = true;
            ReaderViewModel.DefaultMode = ViewMode.Single;
            var vm1 = new ReaderViewModel(StressBook, db);
            await WaitUntilAsync(() => vm1.Image1 is not null);
            vm1.ToggleModeCommand.Execute(null);   // Single -> Double
            vm1.ToggleModeCommand.Execute(null);   // Double -> Webtoon
            await WaitUntilAsync(() => vm1.ViewMode == ViewMode.Webtoon);
            vm1.SyncWebtoonPosition(250);
            await vm1.DisposeAsync();

            // 2) 重新打开 → 恢复滚动模式 + 物理页 250
            ReaderViewModel.DefaultMode = ViewMode.Single;
            ReaderViewModel.ResumePosition = true;
            var vm2 = new ReaderViewModel(StressBook, db);
            Assert.Equal(ViewMode.Webtoon, vm2.ViewMode);
            Assert.Equal(250, vm2.CurrentPhysical);
            await vm2.DisposeAsync();
        }
        finally
        {
            ReaderViewModel.DefaultMode = oldMode;
            ReaderViewModel.ResumePosition = oldResume;
            try { File.Delete(db); } catch { }
        }
    }

    /// <summary>v3 #1 回归：旧配置文件（缺新动作）加载时自动补齐默认动作（GoHome/GoEnd），不重置用户绑定。</summary>
    [Fact]
    public void Load_Merges_Missing_Default_Actions()
    {
        var old = SettingsStore.BaseDir;
        var baseDir = Path.Combine(Path.GetTempPath(), $"manga_set_{Guid.NewGuid():N}");
        try
        {
            SettingsStore.BaseDir = baseDir;
            var s = new AppSettings();
            // 模拟旧配置：General 组缺 GoHome/GoEnd，且用户自定义了 ToggleTheme 的键
            s.Hotkeys["General"].Remove("GoHome");
            s.Hotkeys["General"].Remove("GoEnd");
            s.Hotkeys["General"]["ToggleTheme"] = new() { "X" };
            SettingsStore.Save(s);

            var loaded = SettingsStore.Load();
            // 缺失动作补齐 + 默认键 Home/End
            Assert.True(loaded.Hotkeys["General"].ContainsKey("GoHome"));
            Assert.True(loaded.Hotkeys["General"].ContainsKey("GoEnd"));
            Assert.Contains("Home", loaded.Hotkeys["General"]["GoHome"]);
            Assert.Contains("End", loaded.Hotkeys["General"]["GoEnd"]);
            // 用户自定义保留
            Assert.Equal(new[] { "X" }, loaded.Hotkeys["General"]["ToggleTheme"]);
        }
        finally
        {
            SettingsStore.BaseDir = old;
            try { Directory.Delete(baseDir, true); } catch { }
        }
    }

    /// <summary>v3 #2b 回归：默认方向设置存取 + 打开书生效（无保存状态时用设置的默认方向）。</summary>
    [Fact]
    public void DefaultDirection_RoundTrip_And_Apply()
    {
        var old = SettingsStore.BaseDir;
        var baseDir = Path.Combine(Path.GetTempPath(), $"manga_set_{Guid.NewGuid():N}");
        try
        {
            SettingsStore.BaseDir = baseDir;
            var s = new AppSettings { DefaultDirection = "Ltr" };
            SettingsStore.Save(s);
            var loaded = SettingsStore.Load();
            Assert.Equal("Ltr", loaded.DefaultDirection);
        }
        finally
        {
            SettingsStore.BaseDir = old;
            try { Directory.Delete(baseDir, true); } catch { }
        }
    }

    /// <summary>v3 清晰度×缓存：性能预设参数组合 + 最坏内存估算。</summary>
    [Fact]
    public void PerformancePreset_And_MemoryEstimate()
    {
        Assert.Equal(("Limit", 2048, 1, 8), AppSettings.PresetValues("Smooth"));
        Assert.Equal(("Auto", 4096, 2, 12), AppSettings.PresetValues("Balanced"));
        Assert.Equal(("Original", 0, 3, 16), AppSettings.PresetValues("Sharp"));

        // 内存估算随档位/缓存变化
        long smooth = AppSettings.EstimateWorstMemoryMB("Limit", 2048, 8);
        long sharp = AppSettings.EstimateWorstMemoryMB("Original", 0, 16);
        long auto = AppSettings.EstimateWorstMemoryMB("Auto", 4096, 12);
        Assert.True(smooth < auto && auto < sharp, $"期望 smooth({smooth}) < auto({auto}) < sharp({sharp})");
    }

    /// <summary>v3 清晰度×缓存：解码宽度按档位计算（Auto/Limit/Original）。</summary>
    [AvaloniaFact]
    public async Task ComputeTargetWidth_Respects_Quality_Mode()
    {
        var db = Path.Combine(Path.GetTempPath(), $"manga_wtf26_{Guid.NewGuid():N}.db");
        try
        {
            var vm = new ReaderViewModel(StressBook, db);
            await WaitUntilAsync(() => vm.SizesLoaded);

            // Auto：需求 3000 → 结果 ≤ min(原图宽, 4096)（压力书原图小可能 < 1200，无下限要求）
            vm.DecodeQuality = "Auto";
            int autoW = vm.ComputeTargetWidth(3000, 0);
            Assert.True(autoW > 0 && autoW <= 4096, $"Auto 档宽度越界：{autoW}");

            // Limit 2048：大需求钳到 2048
            vm.DecodeQuality = "Limit";
            vm.DecodeLimit = 2048;
            Assert.Equal(2048, vm.ComputeTargetWidth(5000, 0));

            // Original：至少不差于 Auto（=原图宽）
            vm.DecodeQuality = "Original";
            int origW = vm.ComputeTargetWidth(5000, 0);
            Assert.True(origW >= autoW, $"Original({origW}) 应 >= Auto({autoW})");

            await vm.DisposeAsync();
        }
        finally { try { File.Delete(db); } catch { } }
    }

    /// <summary>v3 清晰度×缓存：存取往返（新字段持久化）。</summary>
    [Fact]
    public void ClarityCache_Settings_RoundTrip()
    {
        var old = SettingsStore.BaseDir;
        var baseDir = Path.Combine(Path.GetTempPath(), $"manga_set_{Guid.NewGuid():N}");
        try
        {
            SettingsStore.BaseDir = baseDir;
            var s = new AppSettings
            {
                DecodeQuality = "Original",
                DecodeLimit = 4096,
                PrefetchRange = 3,
                PerformancePreset = "Sharp",
                CacheCap = 16,
            };
            SettingsStore.Save(s);
            var loaded = SettingsStore.Load();
            Assert.Equal("Original", loaded.DecodeQuality);
            Assert.Equal(3, loaded.PrefetchRange);
            Assert.Equal("Sharp", loaded.PerformancePreset);
            Assert.Equal(16, loaded.CacheCap);
        }
        finally
        {
            SettingsStore.BaseDir = old;
            try { Directory.Delete(baseDir, true); } catch { }
        }
    }

    /// <summary>v3 修复回归：翻页/显示需求变化后，解码宽度跟随实时 DisplayWidth（不再用旧 DecodeTargetWidth）。
    /// 旧代码缩小过视图后翻页会用旧宽度 → 放大/高分屏下模糊且不升级。</summary>
    [AvaloniaFact]
    public async Task DecodeWidth_Follows_DisplayWidth_On_Page_Turn()
    {
        var db = Path.Combine(Path.GetTempPath(), $"manga_wtf27_{Guid.NewGuid():N}.db");
        try
        {
            var vm = new ReaderViewModel(StressBook, db);
            await WaitUntilAsync(() => vm.Image1 is not null);
            int firstPage = vm.CurrentPhysical;

            // 模拟"放大"：DisplayWidth 变大 → 翻页（不缩放）后新页按新宽度解码
            vm.DisplayWidth = 3000;
            vm.NextPageCommand.Execute(null);
            await WaitUntilAsync(() => vm.CurrentPhysical != firstPage);
            int expect = vm.ComputeTargetWidth(3000, vm.CurrentPhysical);
            await WaitUntilAsync(() => vm.CachedDecodeWidth(vm.CurrentPhysical) == expect);
            Assert.Equal(expect, vm.CachedDecodeWidth(vm.CurrentPhysical));

            // 模拟"缩小"：DisplayWidth 小 → 缓存已有大图则直接满足（大图缩小显示更清晰），无需重解
            vm.DisplayWidth = 600;
            vm.RefreshCurrentPage();
            await Task.Delay(400);
            int expectSmall = vm.ComputeTargetWidth(600, vm.CurrentPhysical);
            Assert.True(vm.CachedDecodeWidth(vm.CurrentPhysical) >= expectSmall,
                $"缩小后缓存宽度 {vm.CachedDecodeWidth(vm.CurrentPhysical)} 应满足需求 {expectSmall}");

            await vm.DisposeAsync();
        }
        finally { try { File.Delete(db); } catch { } }
    }
}
