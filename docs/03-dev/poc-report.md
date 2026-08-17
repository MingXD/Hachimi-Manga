# M0.5 技术 PoC 报告（2026-02 实测）

> 结论：**三个高风险点全部验证通过，且余量巨大**。Avalonia 模板实际安装的是 **12.1.1**（比调研时的 11.x 更新，移动端支持更成熟），无兼容问题。

## 1. CBZ 随机读基准（目标：索引 <1s、随机翻页 <100ms）
| 指标 | 实测 | 目标 | 结论 |
|---|---|---|---|
| 300 页 CBZ 索引（打开+排序+读全部页尺寸） | **29.0ms** | <1000ms | ✅ 余量 34× |
| 随机翻页 30 次平均（解压+读全文） | **0.1ms** | <100ms | ✅ 余量 1000× |
| 页均字节 | 7.8KB（纯色 PNG） | — | 真实扫描页会更大，但解压瓶颈远未触及 |

实现说明：CBZ 用 **.NET 内置 ZipArchive**（随机读取零依赖、性能极佳），SharpCompress 已引入备用（未来 CBR/7z）。自然排序（page1,page2,…,page10）由 ImageInfo.CompareNatural 提供。

## 2. SQLite 库索引基准（目标：插入 10k <5s）
| 指标 | 实测 | 结论 |
|---|---|---|
| 插入 10k 本书（单事务） | **72ms** | ✅ |
| 计数 + 按系列索引查询 | **3.8ms**（200 条） | ✅ |

实现：LibraryDb（Microsoft.Data.Sqlite 10.0.11，Books 表 + Series 索引 + 批量插入）。

## 3. Avalonia UI 冒烟（Headless，Avalonia 12.1.1）
- ✅ 创建窗口 + **1 万项虚拟化列表**渲染（封面墙可行性；Avalonia 12 中 ListBox 虚拟化默认开启，原 11.x 的 VirtualizationMode API 已移除）
- ✅ **大图（800×1200 PNG）** 在窗口内渲染（RenderTargetBitmap 断言）
- 测试基础设施：Avalonia.Headless.XUnit 12.1.1 + **xunit.v3 3.2.2**（注意：Headless 12.1.1 按 xunit.v3 3.x 构建，不能配 v4；已锁定版本）

## 4. 分页布局器（PageLayouter 纯函数）
- ✅ 5 个单元测试：封面单页→成对、奇数余页末页单页、横向跨页单页、封面关闭从第 0 页成对、自然排序
- 夹具：TestSeriesA/B/C 系列 6 本 CBZ + 300 页压力书 + 文件夹书（tools/make-fixtures.ps1 生成，含 ComicInfo.xml 与 Manga=RTL 标记）

## 5. 测试总览：**11/11 通过**（9 逻辑 + 2 UI 冒烟）

## 6. 对 M1 的影响
- 阅读器渲染管线：OpenPageAsync → SkiaSharp 解码（Avalonia Bitmap）→ 尺寸分级缓存，直接采用 PoC 验证的 ZipComicSource
- 封面墙：Avalonia 虚拟化列表 + 缩略图缓存（三档：原始/屏幕/缩略图）
- 库扫描：ComicSourceFactory.Open 自动分发文件夹/CBZ；LibraryDb 入库
- 双页布局：PageLayouter 已就绪，UI 层按 LogicalPage 渲染 + RTL 对齐
- 风险提示：真实大图（4K/8K 扫描页）解码性能需在 M1 用真实样本再验证一次（PoC 用的是 7KB 纯色页）
