# 技术选型评估报告

> 项目：Windows 优先的本地漫画阅读器（日漫从右到左、高度可定制阅读模式、日/夜主题、内置本地漫画库与文件管理），未来 1–2 年可能扩展 iOS/Android。纯调研，结论具体到库与框架。资料检索时间：2024–2026。
> 结论先行：**主方案 = Avalonia 11（C#/.NET）+ SkiaSharp/SharpCompress/PDFium 生态；备选 = Flutter**。若移动端被确认为近期硬需求，切换 Flutter；若确认纯 Windows，重估 WinUI 3。

## 框架对比矩阵

| 维度 | Avalonia 11 | .NET MAUI | WinUI 3 / Windows App SDK | Tauri 2 | Flutter | Qt 6 | Compose Multiplatform |
|---|---|---|---|---|---|---|---|
| **Windows 桌面体验成熟度** | 高。Skia 渲染、虚拟化列表、自绘控件强；11.x 已大量生产落地 | 中低。Windows 目标底层是 WinUI 3/Windows App SDK，但桌面控件与稳定性不足，2025 年社区仍大量反馈崩溃/兼容问题；NativeAOT 不完整 | 极高（Windows 专属）。原生控件、Mica、Snap、拖放、WIC/Direct2D 集成最佳；但框架 bug 与版本更替频繁、社区口碑波动 | 中高。基于 WebView2（Win10/11 预装），系统集成走 Rust 侧（托盘/快捷键/单实例/拖放）；UI 为 Web 技术需自行打磨 | 中。Windows 桌面自 2022 起稳定，自绘一致性强；原生集成（托盘/外部拖放/系统对话框）依赖第三方插件；Impeller 在 Windows 上 2025–2026 仍有光栅化性能回退 | 极高。Windows 上成熟数十年，Widgets/QML 皆可，Per-Monitor DPI、拖放、系统集成完善 | 中。Windows 走 JVM（Compose Desktop），Skia 渲染可用；桌面专属 API 有限、系统集成靠三方库 |
| **移动端（iOS/Android）成熟度与工作量** | iOS/Android 仍为 experimental，VS2026 下模板仍有构建问题；需等 12.x 或自担实验风险 | iOS/Android 是官方主战场，成熟度中高（相对桌面好很多），但桌面↔移动 UI 共享价值有限 | 不支持移动端，无移动路线 | 官方支持 iOS/Android 但处于预览/早期：需 Xcode/Android Studio，WKWebView/Android WebView 承载，插件生态未就绪 | **移动端最佳**，iOS/Android 一级公民 | Qt for Android/iOS 支持多年、成熟，可单一代码库覆盖桌面+移动 | iOS 自 1.8.0（2025-05）起稳定，Android 原生；桌面为 JVM，UI 可大部共享 |
| **图像解码/渲染（漫画场景）** | Skia 引擎；SkiaSharp 高性能缩放/解码/缩略图；ImageSharp（纯托管，WebP 3.1+）、Magick.NET、NetVips 均可挂 | 同 .NET 图像库（SkiaSharp/ImageSharp 跨平台）；Windows 可加 WIC | WIC 内建编解码 + Win2D/Direct2D，Windows 上速度与系统集成最佳；仅 Windows | 前端 Canvas 2D/WebGL（WebView2 自带 Chromium 编解码，含 WebP/AVIF）；Rust 侧 image/libvips，链路较长 | 引擎自绘（Skia/Impeller），内置 JPEG/PNG/WebP/GIF 解码，AVIF 需插件；CustomPainter 可控但大图分块/缓存需自建 | QImageReader 插件式解码（JPEG/PNG/WebP，AVIF 自 Qt 6.4 经 qtimageformats）；QPainter/QML Scene Graph 高性能渲染；YACReader 即 Qt 漫画阅读器 | Skia 渲染（Skiko）；JVM 侧 ImageIO/PDFBox 等生态，大图流水线需自建 |
| **压缩包随机读取（CBZ/CBR）** | SharpCompress：ZIP/7z/TAR/GZ + RAR（含 RAR5）只读，跨平台（含移动端）；内置 ZipArchive 随机读；SevenZipExtractor 仅 Windows（依赖 7z.dll） | 同 .NET 生态（SharpCompress/ZipArchive 跨平台可用） | 同 .NET 生态 + 可调 7z.dll | Rust zip（随机读）/sevenz-rust/unrar crate，性能好、含移动端 | archive 包（纯 Dart）：ZIP 可用，RAR 弱 —— CBZ 可、CBR 是短板 | QuaZip/minizip、libarchive（BSD）、libunrar（RARLAB 许可，仅解压免费） | JVM：java.util.zip（随机读）/commons-compress/SevenZipJBinding 强；iOS（Kotlin/Native）无 JVM，ZIP 生态最弱 |
| **PDF 渲染** | PdfiumViewer（WPF/WinForms）、PdfiumCore（跨桌面）；移动端需自行打包 PDFium | 同左，移动端需自行打包 PDFium | 无官方控件，用 PDFium 绑定（同 .NET 生态） | pdfium-render crate（PDFium 绑定，Windows/Android/iOS 均可编译） | **pdfrx（PDFium）支持 Win/iOS/Android/macOS/Linux/Web，移动端现成最佳** | QtPdf 模块（基于 PDFium），桌面+移动可用，GPL/商业许可 | JVM：Apache PDFBox；iOS 无好方案（需原生 PDFKit 互操作） |
| **本地文件管理** | FileSystemWatcher（Win 用 ReadDirectoryChangesW，.NET Core 跨平台）；Microsoft.Data.Sqlite/EF Core；ComicInfo.xml 有 ComicMeta、Kavita 成熟 C# 实现可参考 | 同左（.NET 生态全复用） | 同左（StorageFile/FileSystemWatcher） | notify crate 跨平台；rusqlite/sqlx；quick-xml+serde 手写 ComicInfo | dart:io FileSystemEntity.watch（Win 用 ReadDirectoryChangesW）+ watcher 包；drift/sqflite；xml 包手写 ComicInfo | QFileSystemWatcher；QSqlDatabase（SQLite 内建）；QXmlStreamReader | JVM WatchService ✅ / iOS 无统一方案 ⚠️；SQLDelight；XML 手写 |
| **打包/更新/体积/内存** | MSIX+AppInstaller 自动更新、Squirrel/Velopack；self-contained .NET 约 60–150MB；内存约 100–200MB（估算） | 同 .NET 打包；Windows 侧走 WinAppSDK 部署体系 | MSIX（App Installer 自动更新）/MSI/unpackaged；包体小、自动更新最成熟 | NSIS/MSI/WiX/MSIX + 官方 updater（需签名）；体积最小（5–15MB）；内存看 WebView2 进程（100MB+） | msix/Inno/NSIS；无官方自动更新（desktop_update/Shorebird 三方）；约 20–60MB | windeployqt+NSIS/Inno/WiX；Qt IFW 在线更新（许可注意）；约 30–90MB | jpackage 打包 JRE 约 80–200MB；无官方更新；JVM 内存偏高 |
| **生态/学习曲线** | .NET/WPF 经验可迁移、XAML 相似；生态中（Community Toolkit 等）；MIT | XAML 官方生态；桌面坑多 | 微软官方 Windows 生态最佳；API 变动与文档滞后；开源协作刚起步 | Rust 学习曲线陡；前端任意 Web 栈；Rust 侧生态强 | Dart 易学；移动生态巨大；桌面生态一般 | C++ 门槛高、QML 快；生态最全；Qt Company 政策变动风险 | Kotlin 现代易学；JetBrains 背书；桌面生态小 |
| **许可证风险** | MIT，无风险 | 微软 MIT，无风险 | MIT，无风险；产品方向有不确定性（开源化过渡） | MIT/Apache，无风险 | BSD-3，无风险 | **LGPLv3（动态链接+可重链接义务，移动端合规麻烦）或商业按席位年费；收费与政策调整风险** | Apache-2.0，无风险 |

## 关键库清单

> ✅=开箱即用/官方支持　⚠️=可用但需自建/交叉编译/三方方案　❌=不可用 —— 按"Windows 桌面"与"移动端（iOS/Android）"分别标注。

| 分类 | 库 | Windows | 移动端 | 备注 |
|---|---|---|---|---|
| 压缩/解压 | SharpCompress（.NET） | ✅ | ✅ | ZIP/7z/TAR/GZ + RAR 只读，netstandard 跨平台；RAR 为独立实现，兼容性需测试 |
| 压缩/解压 | System.IO.Compression.ZipArchive（.NET 内置） | ✅ | ✅（MAUI） | CBZ 随机读首选，快且零依赖 |
| 压缩/解压 | SevenZipExtractor（.NET，包装 7z.dll） | ✅ | ❌ | 需原生 7z.dll，Windows 专用；RAR/7z 速度最快 |
| 压缩/解压 | archive（Dart）[Flutter] | ✅ | ✅ | 纯 Dart；ZIP 可、RAR 弱 |
| 压缩/解压 | zip / sevenz-rust / unrar（Rust）[Tauri] | ✅ | ✅ | 随机读 + 交叉编译均成熟 |
| 压缩/解压 | QuaZip / libarchive / libunrar（C++）[Qt] | ✅ | ✅ | libarchive 为 BSD；libunrar 需遵守 RARLAB 许可 |
| 压缩/解压 | java.util.zip / commons-compress / SevenZipJBinding（JVM）[Compose] | ✅ | ✅（Android）/ ❌（iOS） | iOS 无 JVM，ZIP 生态最弱 |
| 图像解码 | SkiaSharp（.NET） | ✅ | ✅（MAUI） | 缩放/缩略图/解码性能好；不含 AVIF 需补 |
| 图像解码 | ImageSharp v3（.NET，纯托管） | ✅ | ✅（MAUI） | WebP 3.1+；无原生依赖，移动友好 |
| 图像解码 | Magick.NET / NetVips（.NET） | ✅ | ⚠️ | libvips 最快最省内存但移动端需自行交叉编译 |
| 图像解码 | WIC + Win2D（Windows 专属） | ✅ | ❌ | 系统级编解码 + Direct2D 硬件加速 |
| 图像解码 | Flutter 引擎解码器 | ✅ | ✅ | JPEG/PNG/WebP/GIF 内置；AVIF 走插件 |
| 图像解码 | QImageReader（Qt） | ✅ | ✅ | 插件式；AVIF 自 Qt 6.4 |
| 图像解码 | image / libvips（Rust）[Tauri] | ✅ | ✅ | 服务端式批处理、缩略图 |
| PDF | PdfiumViewer（WPF/WinForms）/ PdfiumCore | ✅ | ⚠️ | 移动端需自行打包 PDFium 原生库 |
| PDF | pdfrx（Flutter，PDFium） | ✅ | ✅ | 六平台现成，移动端最佳 |
| PDF | QtPdf（Qt，PDFium） | ✅ | ✅ | GPL/商业许可 |
| PDF | pdfium-render（Rust）[Tauri] | ✅ | ✅ | Android/iOS 可编译 |
| PDF | Apache PDFBox（JVM） | ✅ | ✅（Android）/ ❌（iOS） | Android 用 pdfbox-android |
| 数据库 | Microsoft.Data.Sqlite / EF Core（.NET） | ✅ | ✅（MAUI） | 标准方案 |
| 数据库 | drift / sqflite_common_ffi（Flutter） | ✅ | ✅ | 桌面走 FFI |
| 数据库 | rusqlite / sqlx（Rust）[Tauri] | ✅ | ✅ | |
| 数据库 | QSqlDatabase（Qt 内建 SQLite） | ✅ | ✅ | |
| 数据库 | SQLDelight（KMP） | ✅（JVM） | ✅ | 桌面 JVM / Android / iOS 原生驱动 |
| 元数据 | ComicMeta（.NET，ComicInfo.xml） | ✅ | ✅（MAUI） | 现成模型类 |
| 元数据 | Kavita 本地元数据实现（C#，可参考） | ✅ | — | 工业级参考（Kavita 服务端） |
| 元数据 | quick-xml+serde（Rust）/ xml 包（Dart）/ QXmlStreamReader（Qt）/ XmlPull（JVM） | ✅ | ⚠️ | ComicInfo 无官方 schema 库，各栈均需手写映射（字段约 40 个） |
| 缩略图 | SkiaSharp/ImageSharp 降采样缓存 | ✅ | ✅ | 常规方案 |
| 缩略图 | NetVips（libvips） | ✅ | ⚠️ | 大批量缩略图吞吐最强 |
| 缩略图 | WIC（Windows） | ✅ | ❌ | 系统级、极快 |
| 文件监听 | FileSystemWatcher（.NET） | ✅ | ✅（MAUI） | ReadDirectoryChangesW；.NET Core 跨平台 |
| 文件监听 | dart:io watch + watcher 包（Flutter） | ✅ | ✅ | Windows 走 ReadDirectoryChangesW |
| 文件监听 | notify crate（Rust）[Tauri] | ✅ | ✅ | |
| 文件监听 | QFileSystemWatcher（Qt） | ✅ | ✅ | |
| 文件监听 | java.nio WatchService（JVM） | ✅ | ✅（Android） | iOS（K/N）⚠️ 无统一方案 |
| 打包更新 | MSIX AppInstaller / Squirrel / Velopack（.NET 系） | ✅ | — | .NET/Avalonia/MAUI/WinUI 通用 |
| 打包更新 | Tauri updater（签名）/ NSIS / WiX / MSIX | ✅ | ⚠️（预览） | 体积最小 |
| 打包更新 | desktop_update / Shorebird（Flutter） | ⚠️ | ✅（移动端 Shorebird 较成熟） | 无官方桌面更新 |

## 推荐方案与备选方案

### 主方案：Avalonia 11（C#/.NET）

**理由**
1. **Windows 桌面体验与性能**：Avalonia 基于 Skia 渲染，布局/虚拟化/自绘完全可控，正好满足"高度可定制阅读模式"（从右到左、双页、长条滚动、翻页动画、缩放手势都能精确实现）；11.x 已被大量生产级应用采用，高 DPI/缩放成熟。
2. **漫画场景关键库覆盖最全且许可证干净**：SharpCompress（MIT，ZIP/RAR/7z 跨平台）、SkiaSharp（MIT）、ImageSharp v3（Apache-2.0）、PdfiumViewer/PdfiumCore（BSD 系）、ComicMeta（ComicInfo.xml）、Microsoft.Data.Sqlite、FileSystemWatcher —— 全部在 .NET 生态内，Windows 与未来 MAUI 移动端可复用同一套库。
3. **移动端出口清晰**：把业务核心（压缩包读取、元数据、数据库、缩略图管线）做成纯 .NET 类库；未来移动端两条路——(a) 用 MAUI 复用同一核心（UI 重写、逻辑保留），(b) 等 Avalonia 移动端随 12.x 成熟。两条路都不需要推倒重来。
4. **许可证零风险**：Avalonia MIT、生态 MIT/Apache/BSD，无商业授权成本。

**风险**
- Avalonia iOS/Android 仍为 experimental，VS2026 下模板有构建问题：若移动端在 12 个月内成为硬需求，需切换 UI 层（MAUI 或 Flutter），承担一次 UI 重写（逻辑层可保留）。
- 生态规模小于 Flutter/Qt：托盘、全局快捷键等桌面集成需 Win32 P/Invoke 或三方包。
- SkiaSharp 大图解码为 CPU 路径；若要硬件加速可后续在 Windows 层接 Win2D（仅 Windows）。

### 备选方案：Flutter
**理由**：移动端成熟度全场最高；Windows 桌面自绘一致性强；pdfrx 提供含 Windows/iOS/Android 的现成 PDFium 渲染；dart:io 文件监听可用；drift 提供 SQLite。适合"移动端优先级提前"或团队倾向 Dart 的场景。
**风险**：Windows 系统集成弱（托盘、外部拖放、文件对话框靠插件）；Impeller 在 Windows 上 2025–2026 有光栅化性能回退；无官方桌面自动更新；CBR/RAR 支持弱；大库扫描/文件管理需自建。

> 次要备选：**Tauri 2**（若团队 Rust 强：体积最小、Rust 图像/压缩生态强，但移动端为预览期，WebView 渲染超大图需 canvas 方案）；**Compose Multiplatform**（若团队 Kotlin 强：iOS 已稳定，但 Windows 为 JVM 且 iOS 端 ZIP/PDF 生态最弱）。**Qt 6** 单代码库覆盖桌面+移动最成熟，但许可证风险（LGPLv3 移动端合规困难 / 商业席位年费）与 C++ 生产力成本使其仅适合有 C++ 团队的场景。

### 纯 Windows 路线（若移动端放弃/推迟 3 年以上）
**WinUI 3 / Windows App SDK**：系统集成与 MSIX 自动更新最成熟，WIC/Win2D 图像管线最强；风险是框架变动与口碑波动。若走此路线，建议同时预留 Komga/Kavita 服务端模式以对冲未来移动端需求。

## 移动端路径取舍分析

| | a. 现在就选跨平台框架 | b. 先 Windows 原生 + 自托管服务端 |
|---|---|---|
| 代表 | Avalonia / Flutter / Tauri / Compose | WinUI 3 / WPF + Komga/Kavita |
| 优点 | 单代码库、共享逻辑、UI 一致 | 桌面体验与开发速度最佳；服务端集中元数据/缩略图/转码，移动端做成薄客户端，客户端量小 |
| 缺点 | 要么桌面妥协（Flutter/Compose/Tauri），要么移动端妥协（Avalonia/MAUI） | 服务端运维成本；多套 UI；离线体验依赖同步设计 |
| 行业先例 | Mihon/Tachiyomi 系（Kotlin 移动） | YACReader（Qt 桌面 + 独立移动客户端连库）；Kavita/Komga 客户端生态 |

**建议（混合路线，切换成本最低）**：
1. 桌面端按 Windows 原生质量标准做——Avalonia 本地库 + 文件管理，但把"库扫描/元数据/缩略图/压缩读取"抽象为**平台无关的 .NET 核心库**（不引用 UI）。
2. 若 18–24 个月后移动端确认，二选一：**(1) 核心库 → MAUI**（.NET 全家桶直达 iOS/Android，UI 重写、逻辑全保留）；**(2) 自托管 Komga/Kavita 服务端 + Flutter/KMP 薄客户端**（服务端承担扫描与元数据，客户端只做阅读器，工作量最小）。
3. 若移动端提前到 12 个月内，直接切 Flutter 主方案。

## 结论
- **主方案 Avalonia 11（C#/.NET）**：Windows 桌面体验与性能达标（Skia 渲染）、漫画场景关键库覆盖最全、许可证零风险、且为未来移动端保留了"共享 .NET 核心"的清晰出口；主要代价是 Avalonia 移动端尚为实验态。
- **备选 Flutter**：移动端成熟度与桌面一致性最佳，适合移动端优先级提前；代价是 Windows 系统集成与 RAR/更新等生态短板。
- **不建议作为主选**：MAUI（桌面坑多）、Tauri 2（移动端预览期 + WebView 渲染链路长）、Qt（许可证与成本风险）、Compose（Windows 为 JVM、iOS 端压缩/PDF 生态弱）、WinUI 3（仅限纯 Windows 定位）。
- **移动端路径**：按"混合路线"执行——桌面 Avalonia + 平台无关 .NET 核心库；移动端确认后按时间窗口在 MAUI 复用核心 / Komga-Kavita 服务端薄客户端 / Flutter 重写之间选择，切换成本可控。

## 来源附录

| 编号 | URL |
|---|---|
| S1 | https://docs.avaloniaui.net/docs/supported-platforms |
| S2 | https://avaloniaui.net/blog/avalonia-12.0-preview-1/ |
| S3 | https://blog.csdn.net/weixin_44643352/article/details/154142085 |
| S4 | https://github.com/AvaloniaUI/Avalonia/issues/20671 |
| S5 | https://avaloniaui.net/avalonia |
| S6 | https://visualstudiomagazine.com/articles/2025/10/24/windows-11-update-borks-net-maui-projects-built-on-net-8-microsoft-what-have-you-done.aspx |
| S7 | https://appisto.app/blog/state-of-dotnet-maui |
| S8 | https://startdebugging.net/2026/05/maui-vs-avalonia-vs-uno-in-2026/ |
| S9 | https://github.com/dotnet/maui/discussions/27185 |
| S10 | https://github.com/dotnet/maui/issues/31227 |
| S11 | https://github.com/microsoft/WindowsAppSDK/discussions/5454 |
| S12 | https://github.com/microsoft/microsoft-ui-xaml/discussions/10700 |
| S13 | https://www.theregister.com/oses/2026/05/13/microsoft-aims-to-speed-windows-with-leap-forward-in-winui-3-perf/5239559 |
| S14 | https://learn.microsoft.com/zh-cn/windows/apps/windows-app-sdk/deployment-architecture |
| S15 | https://learn.microsoft.com/en-au/answers/questions/5968479/winui-3-published-unpackaged-self-contained-app-cr |
| S16 | https://tauri.app/release/updater/all-versions/ |
| S17 | https://takazudomodular.com/pj/zudo-tauri/docs/mobile/ |
| S18 | https://www.youngju.dev/blog/culture/2026-05-14-tauri-2-rust-webview-desktop-mobile-electron-alternative-deep-dive-2026.en |
| S19 | https://github.com/orgs/tauri-apps/discussions/15565 |
| S20 | https://tauri.app/zh-cn/release/tauri-bundler/v2.2.3/ |
| S21 | https://v2.tauri.org.cn/blog/tauri-20/ |
| S22 | https://docs.flutter.dev/reference/supported-platforms |
| S23 | https://github.com/flutter/flutter/issues/189827 |
| S24 | https://voxturrlabs.com/blog/can-flutter-build-enterprise-desktop-applications-2025/ |
| S25 | https://api.flutter.dev/flutter/dart-io/FileSystemEntity/watch.html |
| S26 | https://pub.dev/packages/watcher |
| S27 | https://github.com/espresso3389/pdfrx |
| S28 | https://doc.qt.io/qt-6/android.html |
| S28b | https://doc.qt.io/qt-6/ios.html |
| S29 | https://www.eet-china.com/mp/a385858.html |
| S30 | https://opensource.stackexchange.com/feeds/question/15510 |
| S31 | https://somcosoftware.com/en/blog/quo-vadis-qt-future-of-the-qt-framework-part-2 |
| S32 | https://blog.jetbrains.com/kotlin/2025/05/compose-multiplatform-1-8-0-released-compose-multiplatform-for-ios-is-stable-and-production-ready/ |
| S33 | https://blog.jetbrains.com/kotlin/2024/10/kotlin-multiplatform-development-roadmap-for-2025/ |
| S34 | https://kotlinlang.org/docs/multiplatform/supported-platforms.html |
| S35 | https://www.jetbrains.com.cn/en-us/help/kotlin-multiplatform-dev/compose-desktop-components.html |
| S36 | https://paulyu.me/articles/1132 |
| S37 | https://deepwiki.com/Nanook/GrindCore.SharpCompress/4-reading-archives |
| S37b | https://github.com/adamhathcock/sharpcompress |
| S38 | https://www-0.nuget.org/packages/SevenZipExtractor/1.0.5 |
| S39 | https://github.com/bezzad/pdfiumviewer |
| S39b | https://relatedrepos.com/gh/Dtronix/PDFiumCore |
| S40 | https://feed.nuget.org/packages/ComicMeta/1.0.7 |
| S41 | https://github.com/Kareadita/Kavita/pull/820 |
| S42 | https://learn.microsoft.com/zh-cn/windows/msix/app-installer/auto-update-and-repair--overview |
| S43 | https://learn.microsoft.com/en-us/answers/questions/2236090/best-approach-for-auto-updates-in-a-winui-3-applic |
| S44 | https://selfhosting.sh/compare/kavita-vs-komga/ |
| S45 | https://github.com/netclaw-dev/netclaw/commit/098f7a2e2a4131eb38047be71b3863c47dbd8bd5 |
| S46 | https://www.yacreader.com |
| S47 | https://github.com/mihonapp/mihon |
| S48 | https://www.youngju.dev/transcribe/culture/2026-05-16-modern-dotnet-2026-dotnet-9-10-aspire-blazor-maui-avalonia-fastendpoints-deep-dive.en |
| S49 | https://packages.debian.org/zh-tw/sid/qt-avif-image-plugin |
| S50 | https://sixlabors.com/posts/announcing-imagesharp-310/ |
