# M1 MVP 验收记录（2026-02）

> 状态：**M1 验收目标全部达成**。测试 14/14 通过。

## 验收清单对照（roadmap.md M1）

| # | 验收项 | 结果 | 证据 |
|---|---|---|---|
| 1 | 5000 页 CBZ 索引 <1s、随机翻页 <100ms | ✅ **索引+50次随机读共 180ms**（索引约 150ms、单页 <2ms） | ArchiveBenchmarkTests.Stress_5000p |
| 2 | RTL 双页：封面单页、横向页单页、末页单页 | ✅ | PageLayouterTests 5 项 + E2E |
| 3 | 黑夜模式切换 | ✅ | ReaderE2eTests（T 主题切换断言） |
| 4 | 中断后重开恢复续读 | ✅ | ReaderE2eTests（新开 VM 恢复页/模式） |
| 5 | 单本设置（模式/方向）持久化 | ✅ | ReadingStateStore + E2E 断言 DoubleMode 恢复 |
| 6 | 阅读器 E2E（翻页/双页/缩放/主题/续读/书签/窗口渲染） | ✅ | ReaderE2eTests |
| 7 | self-contained 发布（无 .NET 环境可运行） | ✅ | 清空 DOTNET_ROOT 启动成功，窗口出现；208MB/230 文件 |

## 测试总览：14/14 通过
- 分页布局器 5 项（封面/奇数余页/横向页/方向/自然排序）
- 库扫描 + 阅读状态存储 2 项
- 基准 4 项（300p / 5000p / SQLite / 元数据+跨页）
- Avalonia UI 冒烟 2 项（1 万项虚拟化列表 / 大图渲染）
- 阅读器 E2E 1 项（翻页/双页/主题/续读/书签/窗口）

## 过程中修复的关键问题
1. **UI 线程阻塞异步死锁**：ReaderViewModel 构造时 .GetAwaiter().GetResult() 阻塞异步读页尺寸 → 与 Avalonia 调度器死锁（真实应用打开大书会卡）。修复：ComicSource 增加同步 GetPageSize。
2. **xunit 并行死锁**：两个 AvaloniaFact 测试类并行操作 Headless 平台 → 死锁。修复：[assembly: CollectionBehavior(DisableTestParallelization = true)]。
3. **内存压力下的构建**：机器内存紧张（Overwatch 等占用）时 MSBuild 多节点/Roslyn 编译服务器触发"页面文件太小"。缓解：-m:1 -p:UseSharedCompilation=false + DOTNET_CLI_USE_MSBUILD_SERVER=0。

## 交付物
- 可运行 MVP：`app/publish/MangaAgent.App.exe`（self-contained，双击即用，自带夹具库演示）
- 开发版启动：`tools/run.ps1`
- 测试：`dotnet test src/MangaAgent.Tests`（14 项）
- 用户数据：`%LOCALAPPDATA%\MangaAgent\`（libraries.txt / library.db / reading.db）

## M1 范围外（下一阶段）
- 增量扫描/文件监听、元数据编辑、系列详情、搜索增强（M2）
- Webtoon 模式、点击区预设、放大镜、翻页动画（M3）
