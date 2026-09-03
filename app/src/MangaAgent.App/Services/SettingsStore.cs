using System.Text.Json;

namespace MangaAgent.App.Services;

/// <summary>阅读器动作（快捷键绑定目标）。</summary>
public enum ReaderAction
{
    NextPage,          // 分页：下一页（方向感知由默认绑定 DirNext 体现）
    PrevPage,          // 分页：上一页
    WebtoonNextPage,   // 滚动：下一页整页顶对齐跳
    WebtoonPrevPage,   // 滚动：上一页整页顶对齐跳
    WebtoonScrollDown, // 滚动：向下滑动一屏
    WebtoonScrollUp,   // 滚动：向上滑动一屏
    ToggleMode,        // 切换阅读模式
    ToggleDirection,   // 切换阅读方向
    ToggleTheme,       // 切换主题
    JumpDialog,        // 跳页对话框
    DebugPanel,        // 调试面板
    CycleFit,          // 适应模式循环
    ResetView,         // 复位
    ZoomIn,            // 放大
    ZoomOut,           // 缩小
    TogglePreview,     // 预览栏开关
    GoHome,            // 一键首页（v3 #8）
    GoEnd,             // 一键末页（v3 #8）
}

/// <summary>应用设置（settings.json 持久化；用户可直接编辑文件）。</summary>
public class AppSettings
{
    // —— 阅读基准 ——
    public double ZoomSensitivity { get; set; } = 1.0;   // 缩放速度基准（滚轮缩放每格幅度，0.2~4.0）
    public double WebtoonSpeed { get; set; } = 1.0;      // 滚动速度基准（滚动模式一档距离，0.2~4.0）
    // —— 打开书行为 ——
    public string DefaultMode { get; set; } = "Single";  // 无保存状态时的默认模式：Single/Double/Webtoon
    public string DefaultDirection { get; set; } = "Rtl"; // 无保存状态时的默认方向：Rtl/Ltr（v3 #2b）
    public bool ResumePosition { get; set; } = true;     // 续读：打开书恢复上次位置
    public bool PdfCropDefault { get; set; }             // PDF 裁切默认（v4.1）：开=按位图本身显示（去纸面留白），关=纸面视图（Chrome 样）
    public string DoubleClickAction { get; set; } = "Zoom"; // 双击行为：Zoom/Reset/None
    public string DefaultFitMode { get; set; } = "FitScreen"; // 分页默认适应：FitScreen/FitWidth
    // —— 滚动手感 ——
    public double ScrollFriction { get; set; } = 6.0;    // 停止快慢（越大停越快）
    public double DragFlingThreshold { get; set; } = 150.0; // 甩动触发阈值（px/s）
    public double DragFlingMax { get; set; } = 3000.0;   // 甩动限幅
    // —— 性能与行为 ——
    public int MaxDecodeWidth { get; set; } = 2048;      // 旧字段（v3 前上限；保留兼容，UI 改用 DecodeQuality/DecodeLimit）
    public string DecodeQuality { get; set; } = "Auto";  // 解码清晰度档位：Auto=min(原图宽,4096) / Limit=DecodeLimit / Original=原图全尺寸（v3）
    public int DecodeLimit { get; set; } = 4096;         // Limit 档的解码宽度上限
    public int PrefetchRange { get; set; } = 2;          // 预取范围 ±N 页（1~3，v3）
    public string PerformancePreset { get; set; } = "Balanced"; // 性能预设：Smooth/Balanced/Sharp/Custom（快捷填值，v3）
    public int CacheCap { get; set; } = 12;              // 位图缓存页数（默认 12，均衡档）
    public bool InvertScroll { get; set; }               // 滚动方向反转（自然滚动）
    public string PagedWheelMode { get; set; } = "Zoom"; // 分页滚轮：Zoom | Page
    public bool DebugLog { get; set; }                   // 诊断日志：WHEEL/SCROLL 高频项（默认关，性能；关键事件始终记）

    /// <summary>性能预设 → 参数组合（v3）。Custom 无固定组合，用户自定义。</summary>
    public static (string Quality, int Limit, int Prefetch, int Cache) PresetValues(string preset) => preset switch
    {
        "Smooth" => ("Limit", 2048, 1, 8),    // 流畅优先：低配/只看默认视图
        "Sharp" => ("Original", 0, 3, 16),    // 清晰优先：高配/重度放大阅读
        _ => ("Auto", 4096, 2, 12),           // 均衡（默认）
    };

    /// <summary>最坏内存估算（MB）：按档位典型位图宽 × 缓存页数 × 4 字节（v3，设置界面提示用）。</summary>
    public static long EstimateWorstMemoryMB(string quality, int limit, int cacheCap)
    {
        int width = quality switch
        {
            "Original" => 4000,                       // 4K 扫描页全尺寸
            "Limit" => Math.Clamp(limit, 2048, 4000), // 限幅上限（典型）
            _ => 4096,                                // 自动档安全上限
        };
        int height = (int)(width / 0.7);              // 竖版典型比例
        long bytes = (long)width * height * 4 * Math.Max(1, cacheCap);
        return bytes / 1024 / 1024;
    }
    // —— 快捷键：组 → 动作 → 按键列表（组：General 通用 / PagedLtr 分页·左到右 / PagedRtl 分页·右到左 / Webtoon 滚动；
    // 键 token = Key 枚举名，组合键 = 修饰键+主键，如 "Ctrl+Shift+P"）——
    public Dictionary<string, Dictionary<string, List<string>>> Hotkeys { get; set; } = DefaultHotkeys();

    /// <summary>默认快捷键（四组：通用 / 分页·左到右 / 分页·右到左 / 滚动）。</summary>
    public static Dictionary<string, Dictionary<string, List<string>>> DefaultHotkeys()
    {
        var general = new Dictionary<string, List<string>>
        {
            [nameof(ReaderAction.ToggleMode)] = new() { "M" },
            [nameof(ReaderAction.ToggleDirection)] = new() { "D" },
            [nameof(ReaderAction.ToggleTheme)] = new() { "T" },
            [nameof(ReaderAction.JumpDialog)] = new() { "G" },
            [nameof(ReaderAction.DebugPanel)] = new() { "F12" },
            [nameof(ReaderAction.CycleFit)] = new() { "F" },
            [nameof(ReaderAction.ResetView)] = new() { "R" },
            [nameof(ReaderAction.ZoomIn)] = new() { "OemPlus", "Add" },
            [nameof(ReaderAction.ZoomOut)] = new() { "OemMinus", "Subtract" },
            [nameof(ReaderAction.TogglePreview)] = new() { "P" },
            [nameof(ReaderAction.GoHome)] = new() { "Home" },
            [nameof(ReaderAction.GoEnd)] = new() { "End" },
        };
        return new()
        {
            ["General"] = general,
            ["PagedLtr"] = new()
            {
                [nameof(ReaderAction.NextPage)] = new() { "Right", "PageDown", "Space" },
                [nameof(ReaderAction.PrevPage)] = new() { "Left", "PageUp" },
            },
            ["PagedRtl"] = new()
            {
                [nameof(ReaderAction.NextPage)] = new() { "Left", "PageDown", "Space" },
                [nameof(ReaderAction.PrevPage)] = new() { "Right", "PageUp" },
            },
            ["Webtoon"] = new()
            {
                [nameof(ReaderAction.WebtoonNextPage)] = new() { "Right", "PageDown", "Space" },
                [nameof(ReaderAction.WebtoonPrevPage)] = new() { "Left", "PageUp" },
                [nameof(ReaderAction.WebtoonScrollDown)] = new() { "Down" },
                [nameof(ReaderAction.WebtoonScrollUp)] = new() { "Up" },
            },
        };
    }
}

/// <summary>设置存取：%LOCALAPPDATA%\HachimiManga\settings.json + guide\usage.md（用户可直接编辑）。</summary>
public static class SettingsStore
{
    /// <summary>按键 → 存储 token：修饰键(排序) + 主键（如 Ctrl+Shift+P）。</summary>
    public static string KeyToken(Avalonia.Input.Key key, Avalonia.Input.KeyModifiers mods)
    {
        var parts = new List<string>();
        if (mods.HasFlag(Avalonia.Input.KeyModifiers.Control)) parts.Add("Ctrl");
        if (mods.HasFlag(Avalonia.Input.KeyModifiers.Shift)) parts.Add("Shift");
        if (mods.HasFlag(Avalonia.Input.KeyModifiers.Alt)) parts.Add("Alt");
        parts.Add(key.ToString());
        return string.Join("+", parts);
    }

    /// <summary>测试可覆盖的根目录（默认 %LOCALAPPDATA%\HachimiManga）。</summary>
    public static string BaseDir { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HachimiManga");

    public static string SettingsPath => Path.Combine(BaseDir, "settings.json");

    /// <summary>使用介绍目录：优先 exe 旁边 guide 文件夹（随包分发自定义介绍），否则本机 %LOCALAPPDATA%。</summary>
    public static string GuideDir
    {
        get
        {
            try
            {
                var exeDir = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
                var exeGuide = Path.Combine(exeDir, "guide");
                if (Directory.Exists(exeGuide) && File.Exists(Path.Combine(exeGuide, "usage.md")))
                    return exeGuide;   // 随包分发的介绍优先（收件人可直接看到/编辑）
            }
            catch { }
            return Path.Combine(BaseDir, "guide");
        }
    }

    public static string GuidePath => Path.Combine(GuideDir, "usage.md");

    /// <summary>加载设置（文件缺失/损坏 → 默认值）；同时确保 guide 文件存在。</summary>
    public static AppSettings Load()
    {
        EnsureDefaults();
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                var s = JsonSerializer.Deserialize<AppSettings>(json);
                if (s is not null)
                {
                    // v2 迁移：旧版三模式/DirNext 结构 → 重置为四组默认（旧结构无法映射）
                    if (s.Hotkeys is not { } h || !h.ContainsKey("PagedLtr") || !h.ContainsKey("General"))
                        s.Hotkeys = AppSettings.DefaultHotkeys();
                    else
                    {
                        // v3 #1：旧配置升级 —— 补齐每个组缺失的默认动作（如新增的 GoHome/GoEnd），
                        // 不重置用户已有的自定义绑定；保证 Home/End 出现在"快捷键-通用"且默认绑定生效
                        foreach (var (group, actions) in AppSettings.DefaultHotkeys())
                        {
                            if (!h.TryGetValue(group, out var existing))
                            {
                                h[group] = actions;
                                continue;
                            }
                            foreach (var (action, keys) in actions)
                                if (!existing.ContainsKey(action))
                                    existing[action] = keys;
                        }
                    }
                    return s;
                }
            }
        }
        catch { /* 损坏 → 默认 */ }
        return new AppSettings();
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(BaseDir);
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsPath, json);
        }
        catch { }
    }

    /// <summary>首次运行：创建目录与默认使用介绍（设置文件由 Save 创建）。</summary>
    public static void EnsureDefaults()
    {
        try
        {
            Directory.CreateDirectory(GuideDir);
            if (!File.Exists(GuidePath))
                File.WriteAllText(GuidePath, DefaultGuide, new System.Text.UTF8Encoding(true));
        }
        catch { }
    }

    /// <summary>默认使用介绍模板（用户可直接编辑）。</summary>
    public static string DefaultGuide => """
# Hachimi Manga 使用介绍

## 支持的格式

- 文件夹（内含图片）/ CBZ 压缩包 / PDF 文档
- PDF 页按需渲染：扫描型 PDF 还原原图清晰度；矢量/文字页放大任意倍数依然锐利（自动按显示尺寸重渲染）
- PDF 需随程序分发的 pdfium.dll 渲染组件；缺失时 PDF 不入库（提示见文档）

## 阅读器基本操作

- 滚轮：缩放（以鼠标为焦点）；按住拖动：平移画面
- 双击图片：放大 100% ↔ 恢复适应屏幕
- 键盘翻页：分页模式 ←→ 翻页（RTL：← 下一页）；↑↓ 也可翻页
- 适应按钮循环：适应宽度 / 适应高度 / 适应屏幕；复位回到默认视图

## 三种阅读模式

- 单页：一页一屏，适配并居中
- 双页：两页拼合（RTL 右页在前），配对起点可切换
- 滚动（Webtoon）：上下无缝长条 —— 滚轮连续滚动；← → 整页跳到页顶；
  ↑↓ 平滑滑动一屏；Ctrl+滚轮缩放；拖拽可任意位置拖动

## 预览栏

- 鼠标移到右侧边缘唤出缩略图栏；点击缩略图跳页；▲▼ 整排换页；
  滚轮在缩略图上逐页浏览；当前页有高亮边框

## 快捷键（默认，可在"设置 → 快捷键"中重新绑定）

- 分页：←→ / ↑↓ / PageUp / PageDown / Space 翻页
- 滚动：← → 整页跳；↑↓ 滑动；PageDown / Space 整页跳
- M 切换模式 / D 切换方向 / T 白天夜间 / G 跳页 / F12 调试面板
- F 适应循环 / R 复位 / + − 缩放 / P 预览栏开关

## 设置

- 工具栏最前方"设置"按钮：阅读基准（缩放/滚动速度）、滚动手感、
  性能（解码宽度/缓存页数）、滚动方向反转、分页滚轮行为
- 设置保存于 %LOCALAPPDATA%\HachimiManga\settings.json，可手动编辑
- 诊断日志（滚轮/滚动高频项）默认关闭；排查问题时把 settings.json
  中的 "DebugLog" 改为 true 可开启

## 数据与隐私

- 设置、阅读进度、书签、日志全部保存在本机 %LOCALAPPDATA%\HachimiManga，
  程序不上传、不联网；删除该目录即可清空全部数据

""";
}
