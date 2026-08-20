# MangaAgent 代码地图（CODE-MAP）

> 用途：查功能 / 修 bug 时快速定位代码位置。最后更新：2026-08-20 (v1.7)

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
│  │  └─ Services\            # 缩略图服务
│  ├─ src\MangaAgent.Tests\   # xUnit 测试（headless Avalonia）
│  └─ publish\                # 发布版（自包含，双击运行）
├─ docs\
│  ├─ 01-research\            # 调研
│  ├─ 02-design\              # 方案设计（reader-experience-v3, ux-v2-plan 等）
│  └─ 03-dev\                 # 开发记录（impl-plan, bugs\, v1.8-plan）
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
| Storage\ReadingStateStore.cs | 阅读进度/书签/模式持久化 | 续读逻辑 |

---

## UI 层 MangaAgent.App

### 阅读器（核心交互都在这里）

| 文件 | 职责 | 定位线索（搜索关键词） |
|---|---|---|
| Views\ReaderWindow.axaml | 阅读器布局（工具栏/视口/进度条） | `Viewport` Border、`PageLayer` Grid、`JumpSlider` |
| Views\ReaderWindow.axaml.cs | **阅读器核心逻辑**：画布模型/缩放/拖拽/翻页/Webtoon 虚拟化/跳页 | 见下方细分 |
| ViewModels\ReaderViewModel.cs | 逻辑页序列/解码缓存/引用计数/模式/跳页命令 | `_logical`、`_pageCache`、`_uiRefs`、ToggleMode |

### ReaderWindow.axaml.cs 内部导航（大文件，按区域）

| 区域 | 关键词 | 内容 |
|---|---|---|
| 画布状态 | `_scale` `_tx` `_ty` | MatrixTransform 缩放/平移 |
| 布局 | `Relayout()` | 页面尺寸/Fit 计算/模式切换入口 |
| 缩放 | `ZoomToFocus` `EffectiveFocus` `ContentScreenRect` | 焦点锚定缩放（BUG-003 修复：RenderTransformOrigin=TopLeft） |
| 输入 | `OnPointerWheelChanged` `OnKeyDown` | 滚轮(缩放/滚动)/键盘翻页 |
| Webtoon | `EnterWebtoon` `UpdateWebtoonPages` `LoadWebtoonImageAsync` | 虚拟化 Canvas + 增量渲染（v1.7 重构） |
| 拖拽 | `OnViewportPointerMoved` | 图片拖拽平移 |
| 跳页 | `ShowJumpDialog` `OnJumpSliderValueChanged` `SyncJumpSlider` | 进度条/G 键/对话框 |
| 双页顺序 | `ArrangeDoublePageOrder` | RTL 右页在前 |
| 测试访问器 | `CanvasScale` `WebtoonInstancedCount` `LastPressSource` | headless 测试用 |

### 主窗口（资料库）

| 文件 | 职责 | 定位线索 |
|---|---|---|
| Views\MainWindow.axaml | 顶栏/侧边树/面包屑/目录浏览器 | `FolderEntries` ListBox、`Breadcrumbs` |
| Views\MainWindow.axaml.cs | 双击打开/面包屑点击/缩略图懒加载 | `OnBookDoubleTapped` `OnBreadcrumbClick` |
| ViewModels\MainWindowViewModel.cs | 库管理/扫描/目录导航/搜索 | `AddLibraryAsync` `ShowFolderContents` `EnterFolder` |
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

## 测试层 MangaAgent.Tests（PoC 目录）

| 文件 | 覆盖 |
|---|---|
| ReaderInputTests.cs | 滚轮缩放锚定/按钮缩放中心固定/拖拽/双页顺序 |
| ZoomCenteringTests.cs / ZoomWheelTests.cs | 缩放中心/Ctrl+滚轮 |
| ReaderE2eTests.cs | 翻页/双页/主题/续读/书签（真实窗口） |
| ModeSwitchCrashTests.cs | BUG-004 模式切换不崩溃 |
| WebtoonVirtualizationTests.cs | v1.7 Webtoon 虚拟化数量/滚动/缩放 |
| QuickJumpTests.cs | 跳页命令（逻辑/物理） |
| FolderBrowserTests.cs | 目录浏览器混合展示/导航 |
| BugDiagnosticsTests.cs / DecodeIsolationTests.cs | 诊断/解码隔离 |
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

## 测试运行

```powershell
cd D:\MangaAgent\app
dotnet test src\MangaAgent.Tests -m:1 -p:UseSharedCompilation=false
```

## 发布

```powershell
cd D:\MangaAgent\app
dotnet publish src\MangaAgent.App -c Release -r win-x64 --self-contained -p:PublishSingleFile=false -m:1 -p:UseSharedCompilation=false
robocopy src\MangaAgent.App\bin\Release\net9.0\win-x64\publish publish /MIR
```