# MangaAgent 代码地图（CODE-MAP）

> 用途：查功能 / 修 bug 时快速定位代码位置。最后更新：2026-11 (v2.13, code-audit P1)

## 项目结构总览

```
D:\MangaAgent\
├─ app\
│  ├─ src\MangaAgent.Core\    # 领域层（无 UI，纯逻辑）
│  │  ├─ Library\             # 库扫描 / 文件夹树 / 文件操作
│  │  ├─ Comics\              # 漫画源（CBZ/文件夹）+ 图片信息
│  │  ├─ Paging\              # 双页布局纯函数
│  │  └─ Storage\             # 数据库 / 阅读状态存储
│  ├─ src\MangaAgent.App\     # UI 层（Avalonia）
│  │  ├─ Views\               # 窗口 / 视图（xaml + code-behind）
│  │  ├─ ViewModels\          # MVVM 视图模型
│  │  └─ Services\            # 设置存储 / 缩略图服务
│  ├─ src\MangaAgent.Tests\   # xUnit 测试（headless Avalonia）
│  └─ publish\                # 发布版（自包含，双击运行）
├─ docs\
│  ├─ 01-research\            # 调研
│  ├─ 02-design\              # 方案设计（settings-system, settings-dialog-v2 等）
│  └─ 03-dev\                 # 开发记录（code-audit-optimization-plan, v2-changes-record, bugs\）
└─ reference\fixtures\       # 测试夹具（CBZ / 文件夹书 / 压力书）
```

---

## 核心层 MangaAgent.Core

| 文件 | 职责 | 常见修改场景 |
|---|---|---|
| Library\LibraryScanner.cs | 全库递归扫描 → List<BookInfo> | 扫描规则 / 进度回调 / 系列聚合 |
| Library\FolderTree.cs | 目录树扫描 → FolderNode（保留所有子文件夹） | 目录浏览器数据结构 |
| Library\FolderNode.cs | 树节点（Children/Books/Parent/PathChain） | 面包屑 / 导航 |
| Library\BookInfo.cs | 书实体（Kind: Zip/Folder） | 元数据字段 |
| Library\FileOps.cs | 删除(回收站)/重命名/资源管理器显示 | 右键操作（复制/移动待加） |
| Comics\ComicSource.cs | 漫画源抽象（OpenPageAsync/GetPageSize/ReadMetadata） | 新格式支持 |
| Comics\ZipComicSource.cs | CBZ/ZIP 实现（ZipArchive 随机读） | 解压/元数据 |
| Comics\FolderComicSource.cs | 文件夹型漫画（直接图片）实现 | 图片排序 |
| Comics\ImageInfo.cs | 图片头解析（尺寸/比例）+ 自然排序 | 双页判宽 / 封面 |
| Paging\PageLayouter.cs | 双页布局纯函数（封面单页/配对/横向页单页/FirstPairOffset） | 配对规则修改 |
| Storage\LibraryDb.cs | 资料库索引（sqlite） | 索引查询 |
| Storage\ReadingStateStore.cs | 阅读进度/书签/模式持久化 + ReadingMode/ReadingDirection2 枚举 | 续读逻辑 |

---

## UI 层 MangaAgent.App

### 阅读器（核心交互都在这里）

| 文件 | 职责 | 定位线索（搜索关键词） |
|---|---|---|
| Views\ReaderWindow.axaml | 阅读器布局（工具栏/视口/预览栏） | `Viewport` Border、`PageLayer` Grid、`PreviewStrip` |
| Views\ReaderWindow.axaml.cs | 阅读器壳：构造/DataContext/OnClosed/主题/VM 订阅退订 + 测试访问器 | `_vm`、`OnDataContextChanged`、`CanvasScale` |
| Views\ReaderWindow.Canvas.cs | 分页画布：布局/Fit 计算/缩放锚定/拖拽/双页顺序 | `Relayout` `ZoomToFocus` `ArrangeDoublePageOrder` |
| Views\ReaderWindow.Webtoon.cs | 滚动模式：虚拟化渲染/占位/惯性/复位/适应模式 | `EnterWebtoon` `UpdateWebtoonPages` `_scrollVelocity` |
| Views\ReaderWindow.Preview.cs | 预览栏缩略图条/点击跳页 | `PreviewStrip` `RefreshThumbStrip` |
| Views\ReaderWindow.Input.cs | 键盘/鼠标/快捷键分派（四组：General/PagedLtr/PagedRtl/Webtoon） | `OnKeyDown` `DispatchHotkey` `ParseAction` |
| Views\ReaderWindow.Dialog.cs | 跳页对话框/设置入口/调试面板 | `ShowJumpDialog` `OnSettingsClick` `RunZoomDiagnostics` |
| ViewModels\ReaderViewModel.cs | 壳：属性/布局/主题/命令 CycleFit 等 | `_logical`、`PairOffset`、`OnIsDarkChanged` |
| ViewModels\ReaderViewModel.Decode.cs | 解码/位图 LRU 缓存/UI 引用计数/缩略图缓存 | `DecodeAsync` `EvictCache` `AcquireUiBitmap` |
| ViewModels\ReaderViewModel.Nav.cs | 翻页/书签/跳转/模式方向切换 | `NextPage` `SetCurrentPhysical` `ToggleBookmark` |
| ViewModels\ReaderViewModel.State.cs | 构造/续读恢复/SaveState/DisposeAsync | `ReaderViewModel(...)` `LoadSizes` `_disposeGuard` |

### 设置系统（v2.x 新增）

| 文件 | 职责 | 定位线索 |
|---|---|---|
| Services\SettingsStore.cs | AppSettings 模型 + 存取（%LOCALAPPDATA%\MangaAgent\settings.json）+ 快捷键 KeyToken + GuideDir/DefaultGuide | `SettingsStore.Load/Save` `AppSettings.Hotkeys` `ReaderAction` |
| Views\SettingsDialog.cs | 设置对话框：阅读/快捷键/使用介绍三页签；滑块+数值框；快捷键卡片 | `MakeSliderRow` `MakeHotkeyCard` `OpenGuideFile` |
| 使用介绍文件 | `%LOCALAPPDATA%\MangaAgent\guide\usage.md`（exe 旁 guide\ 优先，不存在则首启生成默认模板） | `SettingsStore.GuidePath` `DefaultGuide` |

### 主窗口（资料库）

| 文件 | 职责 | 定位线索 |
|---|---|---|
| Views\MainWindow.axaml | 顶栏/侧边树/面包屑/目录浏览器 | `FolderEntries` ListBox、`Breadcrumbs` |
| Views\MainWindow.axaml.cs | 双击打开/面包屑点击/缩略图懒加载 | `OnBookDoubleTapped` `OnBreadcrumbClick` |
| ViewModels\MainWindowViewModel.cs | 库管理/扫描/目录导航/搜索；OpenBook 读设置注入 ReaderViewModel 静态 | `AddLibraryAsync` `ShowFolderContents` `EnterFolder` |
| ViewModels\FolderEntryItemViewModel.cs | 目录条目（文件夹/漫画混合） | `IsFolder`、`OpenAction` |
| ViewModels\BookItemViewModel.cs | 封面墙条目（库搜索视图用） | `LoadThumbnailAsync` |
| Services\ThumbnailService.cs | 封面缩略图（弱引用缓存） | `GetThumbnailAsync` |
| Views\RenameDialog.cs / PropertiesDialog.cs | 重命名/属性对话框 | |

### 应用入口 / 日志

| 文件 | 职责 |
|---|---|
| Program.cs | 入口 |
| App.axaml.cs | 启动参数(--open)、全局异常记录、SLog（按日期轮转日志 → %LOCALAPPDATA%\MangaAgent\logs\app-YYYY-MM-DD.log） |

---

## 测试层 MangaAgent.Tests

| 文件 | 覆盖 |
|---|---|
| ReaderInputTests.cs | 滚轮缩放锚定/按钮缩放中心固定/拖拽/双页顺序 |
| ZoomCenteringTests.cs / ZoomWheelTests.cs | 缩放中心/Ctrl+滚轮 |
| ReaderE2eTests.cs | 翻页/双页/主题/续读/书签（真实窗口） |
| ModeSwitchCrashTests.cs | BUG-004 模式切换不崩溃 |
| WebtoonVirtualizationTests.cs | v1.7 Webtoon 虚拟化数量/滚动/缩放 |
| QuickJumpTests.cs | 跳页命令（逻辑/物理） |
| FolderBrowserTests.cs | 目录浏览器混合展示/导航 |
| PoC\ReaderTestFixtures.cs | 共享基建：压力书/等待/进滚动模式/坏 CBZ 构造 |
| PoC\ScrollModeFixesTests.cs | 滚动模式核心回归：黑屏/默认视图/惯性/跳页/拖拽/坏页占位/键盘 |
| PoC\ScrollModeFitTests.cs | 滚动适应模式：FitWidth/FitHeight/放大重解码/复位无残留 |
| PoC\SettingsSystemTests.cs | 设置系统：存取往返/快捷键重绑/组合键/滚轮翻页/LTR-RTL 分组/默认模式+续读 |
| PoC\ArchiveBenchmarkTests.cs | Zip 随机读基准（300p/5000p 压力） |
| PoC\BugDiagnosticsTests.cs / DecodeIsolationTests.cs | 诊断/解码隔离 |
| Library\LibraryScannerTests.cs | 扫描 |
| Paging\PageLayouterTests.cs | 双页布局纯函数 |

> 约定：headless 无法注入真实输入时用 --diag（真实应用自动诊断，写 logs）或公开访问器断言。

---

## 关键修复历史速查

| Bug | 根因一句话 | 修复位置 |
|---|---|---|
| BUG-001 | Image Source 绑定缺失/文件夹书识别 | ReaderViewModel.DecodeAsync |
| BUG-002 | 封面只加载前 24 本 | MainWindow 滚动懒加载 |
| BUG-003 | Avalonia RenderTransformOrigin 默认 Center（非 WPF TopLeft）→ 缩放焦点右下漂移 | ReaderWindow 构造函数 + Relayout 页高 |
| BUG-004 | 位图 LRU 淘汰 Dispose 掉 UI 引用 → ObjectDisposedException | ReaderViewModel._uiRefs 引用计数 + DecodeAsync Acquire |
| v1.7 竞态 | Webtoon Load 并发重复 Release / DecodeAsync 重解码竞态 | ReaderWindow._webtoonLoading / ReaderViewModel DecodeAsync |
| scroll-mode-fix A | 滚动模式 LoadCurrentAsync 解码链与 Webtoon 任务互踩 → 黑屏 | LoadCurrentAsync Webtoon 短路 + 视图虚拟化 |
| scroll-mode-fix B | 复位/重进滚动残留分页缩放平移 | EnterWebtoon/ResetWebtoonView 重算 fit |
| scroll-mode-fix C | 跳页竞态黑屏 | ScrollWebtoonToPhysical 同步定位 + 占位兜底 |
| code-audit P0-1 | DisposeAsync 守卫写反（恒真）→ 从不清理，每本书泄漏 | Interlocked.Exchange 幂等守卫 |

## 测试运行

```powershell
cd D:\MangaAgent\app
dotnet test src\MangaAgent.Tests -m:1 -p:UseSharedCompilation=false -p:UsedAvaloniaProducts=
```

## 发布

```powershell
cd D:\MangaAgent\app
dotnet publish src\MangaAgent.App -c Release -r win-x64 --self-contained -p:PublishSingleFile=false -m:1 -p:UseSharedCompilation=false -p:UsedAvaloniaProducts=
robocopy src\MangaAgent.App\bin\Release\net9.0\win-x64\publish publish /MIR
```
