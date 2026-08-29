# v3 问题排查与解决方案（13 项）— 2026-08-25

> 状态：**只排查 + 出方案，未实施**。等用户决策后按优先级分批实施。
> 来源：用户使用中发现的 13 个问题；本报告逐条对照代码确认现状与根因，给出方案与优先级。

## 0. 结论总表

| # | 问题 | 代码确认结果 | 根因/现状 | 建议优先级 |
|---|---|---|---|---|
| 1 | 删除/移动及时刷新 + 进度条 | 已有刷新但不同步目标库；复制/移动**同步阻塞无进度** | `FileOps.CopyTo/MoveTo` 同步执行；`RefreshAction` 只刷源目录 | **P1** |
| 2 | 开启时默认阅读顺序 | **无默认方向设置**；另有续读 Webtoon 丢失 | `State.cs` 无状态时 `Direction=Rtl` 硬编码；`SaveState` 把 Webtoon 存成 Single | P1 |
| 3 | 跨资料库移动失败 | **确认 bug（静默失败）** | `File.Move/Directory.Move` **不支持跨卷**（IOException 被 catch 吞掉 → 返回 false 无提示） | **P0** |
| 4 | 资料库页资源管理器式快捷键 | **完全没有**（仅 F12） | `MainWindow.axaml.cs` 无 Ctrl+C/V/X/侧键处理 | P2 |
| 5 | 其他文件类别（图标+后缀+操作） | **其他文件被忽略** | `FolderTreeScanner` 只收 cbz/zip/图片目录 | P2 |
| 6 | 新建文件夹 | **无此功能** | 无 NewFolder 命令/入口 | P2 |
| 7 | 阅读中查看图片信息 | **无**（库属性框不含分辨率） | `_sizes`(PageSize) 已有，未暴露到阅读器 UI | P2 |
| 8 | 一键首页 | **无**（需跳页框输入 1） | `ReaderAction` 枚举无 Home；工具栏无按钮 | P2 |
| 9 | 连续下滚（中键）+ 帧率 | **无中键自动滚动**；动画 16ms DispatcherTimer | `Webtoon.cs` 动量模型已较好；帧率受 timer 精度+每帧检查开销 | P1 |
| 10 | 翻页先模糊后清晰 | **无渐进加载**（解码完成才换图；Webtoon 只有灰块占位） | `LoadCurrentAsync` 单次解码；无两阶段 | P1 |
| 11 | 图片是否最清晰 | **默认上限 2048px，多数情况降采样** | `_maxDecodeWidth=2048`；want 基于 BaseH=800 固定基准 ×scale | **P0** |
| 12 | 配对起点应包含封面 | 封面恒单页（`CoverSingle=true`），**无"封面配对"选项** | `PageLayouter` 封面单页硬编码可配置项未暴露 | P2 |
| 13 | 双页配对逻辑（第6页起配对？按钮失效？） | 见 §13 详细分析：横向页单页 + 尺寸异步占位 + 切换不保持物理页 + 不持久化 | 多因素叠加 | **P0** |

---

## 1. 删除/移动及时刷新 + 进度条（P1）

**现状**：
- 删除/重命名/复制/移动后 `RefreshAction` → `ShowFolderContents(CurrentFolder)`（`FolderEntryItemViewModel`），刷新是有的
- 但**只刷新源目录**；移动/复制到"另一个已加载资料库"后，目标库的树/内容不刷新
- 复制/移动是**同步**调用（`MainWindowViewModel.WireEntry` → `FileOps.CopyTo/MoveTo` 直接在 UI 线程执行），大文件夹（几百 MB CBZ/图片目录）会**卡死 UI**，无进度

**方案**：
1. `FileOps` 增加异步进度回调版本：`CopyToAsync(path, dest, IProgress<(int done, int total)>)`、`MoveToAsync`（文件级计数，文件夹递归时先枚举总数）
2. 调用方（`FolderEntryItemViewModel.CopyTo/MoveTo`）改为 `Task.Run` + `IProgress` 更新 UI（状态栏 `StatusText` 显示"正在复制 x/y…"，复用扫描进度模式）
3. 操作完成后：刷新源目录（现有 RefreshAction）+ **若目标路径落在某个已加载资料库内，同步刷新该库的树与目录**（`MainWindowViewModel` 提供 `RefreshLibraryPath(path)`）
4. 可选：操作完成/失败 toast 提示（失败原因见 §3）

**涉及**：`Core/Library/FileOps.cs`、`ViewModels/FolderEntryItemViewModel.cs`、`ViewModels/MainWindowViewModel.cs`
**测试**：`FolderBrowserTests` 扩展——移动后源/目标内容断言；大文件夹复制不阻塞（超时断言）

## 2. 开启时默认阅读顺序（P1）

**现状**：
- 设置已有 `DefaultMode`（默认阅读模式 Single/Double/Webtoon，v5.2）
- **没有"默认方向（RTL/LTR）"设置**：`ReaderViewModel.State.cs` 无保存状态时 `_direction = saved?.Direction ?? ReadingDirection2.Rtl` 硬编码 RTL
- **关联小 bug**：`SaveState()` 把 `ViewMode.Webtoon` 存成 `ReadingMode.Single`；恢复时 `Mode==Double?Double:Single` → **滚动模式下续读会以单页打开**（`ReadingMode` 枚举本身有 Webtoon，是存取逻辑漏了）

**方案**：
1. `AppSettings` 加 `DefaultDirection`（"Rtl"/"Ltr"），`SettingsDialog` 阅读页加下拉
2. `OpenBook` 注入 `ReaderViewModel.DefaultDirection`（同 DefaultMode 模式）
3. 修 Webtoon 续读丢失：`SaveState` 完整映射三模式；恢复时 `switch` 三模式

**涉及**：`Services/SettingsStore.cs`、`Views/SettingsDialog.cs`、`ViewModels/ReaderViewModel.State.cs`、`ViewModels/MainWindowViewModel.cs`
**测试**：`SettingsSystemTests` 扩展（默认方向存取 + 打开书生效）；续读 Webtoon 回归

## 3. 跨资料库移动失败（P0，确认 bug）

**现状**：`FileOps.MoveTo`：
```csharp
if (Directory.Exists(path)) Directory.Move(path, dest);
else if (File.Exists(path)) File.Move(path, dest);
```
- **跨卷（不同盘）时 `Directory.Move`/`File.Move` 抛 `IOException`**（.NET 原生不支持跨卷 Move）→ 被 `catch { return false; }` 吞掉
- 调用方只 `SLog`，用户看到"没反应"→ **静默失败**
- 注释写着"跨盘复制+删除"但**代码没实现**
- 即使同盘：目标在源内部（父子关系）也会抛异常；源=目标也会抛

**方案**：
1. `MoveTo` 先判断卷：`Path.GetPathRoot` 不同 → 回退"递归复制 + 删除源"（复制成功才删源，防数据丢失）；同盘 → `Move`
2. **失败不再静默**：`MoveTo` 返回 `(bool ok, string? error)`，UI 显示具体原因（跨盘/目标已存在/权限/源=目标）
3. 目标路径选择器（`PickTargetFolderAction`）可加"目标在哪个库"提示；跨库移动后同步刷新两个库（§1）
4. 移动大目录复用 §1 的进度

**涉及**：`Core/Library/FileOps.cs`、`ViewModels/FolderEntryItemViewModel.cs`
**测试**：新增 FileOps 单测——同盘 Move、跨盘回退复制+删除、失败返回错误信息（fixtures 造跨盘难，用 Mock 卷/直接验证失败路径返回值）

## 4. 资料库页资源管理器式快捷键（P2）

**现状**：`MainWindow.axaml.cs` 只有 F12；无任何复制/粘贴/剪切/删除/重命名/上级快捷键，无鼠标侧键

**方案**（MainWindow 级 KeyDown + 指针侧键）：
- `Ctrl+C` 复制选中（内部缓存 `_clipboardEntry`，不做系统剪贴板也可）、`Ctrl+V` 粘贴到当前目录、`Ctrl+X` 剪切（标记→移动）、`Delete` 删除（回收站）、`F2` 重命名、`Enter` 打开/进入、`Backspace`/`Alt+↑` 上级、`Ctrl+A` 全选
- 鼠标 **XButton1（侧键前进）/ XButton2（后退）**：需要导航历史栈（`ShowFolderContents` 入栈，前进/后退）
- 选中态：ListBox 单选已有；多选（Ctrl+点击）若做批量操作则需 ListBox SelectionMode=Multiple + 批量命令（先做单选版，多选后续）
- 粘贴冲突：与阅读器快捷键互不干扰（不同窗口）；与系统剪贴板联动可选（剪贴板文件复制粘贴更"资源管理器"）

**涉及**：`Views/MainWindow.axaml.cs`、`Views/MainWindow.axaml`（SelectionMode、KeyBindings）、`ViewModels/MainWindowViewModel.cs`
**测试**：headless 键盘事件测试（FolderBrowserTests 扩展）

## 5. 其他文件类别（统一图标 + 名字后缀 + 基本操作）（P2）

**现状**：`FolderTreeScanner.FillAsync` 只收集 `.cbz/.zip` 与含图片目录；**txt/pdf/rar 等普通文件完全不显示**

**方案**：
1. `FolderNode` 增加 `OtherFiles: List<OtherFileInfo>`（路径/名称/扩展名/大小）；`FillAsync` 收集"非图片、非 cbz/zip 的普通文件"
2. `FolderEntryItemViewModel` 增加 `IsOtherFile` 分支：统一图标（📄）+ 名称.后缀（DisplayName = 文件名含扩展名）+ 大小
3. 右键菜单复用现有：复制/移动/删除（回收站）/重命名/在资源管理器中显示/属性（属性框扩展显示扩展名/大小/修改时间）
4. 可选：搜索命中"其他文件"（默认不命中，设置可选）；封面墙（Books 集合）不收录（保持"漫画库"语义）
5. 注意排除规则要与 `ImageExts`/cbz/zip 严格互斥；文件夹型漫画（纯图片目录）判定保持现状

**涉及**：`Core/Library/FolderNode.cs`、`Core/Library/FolderTree.cs`、`ViewModels/FolderEntryItemViewModel.cs`、`Views/MainWindow.axaml`、`Core/Library/FileOps.cs`（属性）
**测试**：`FolderBrowserTests` 扩展——混合目录含其他文件条目断言

## 6. 新建文件夹（P2）

**现状**：无任何入口

**方案**：
1. `FolderEntryItemViewModel`/`MainWindowViewModel` 加 `NewFolder` 命令：小对话框（复用 `RenameDialog` 样式）输入名称 → `Directory.CreateDirectory(Path.Combine(CurrentFolder.Path, name))` → 刷新 + 进入新文件夹（可选）
2. 右键菜单加"新建文件夹"项 + 工具栏/面包屑区加按钮
3. 命名冲突（同名已存在）提示

**涉及**：`ViewModels/MainWindowViewModel.cs`、`ViewModels/FolderEntryItemViewModel.cs`、`Views/MainWindow.axaml`、`Views/RenameDialog.cs`（或新建 NewFolderDialog）
**测试**：`FolderBrowserTests` 扩展

## 7. 阅读中查看图片信息（分辨率等）（P2）

**现状**：`_sizes[physical] = PageSize(Width,Height)` 已在内存（`LoadSizes` 全量后台补全）；`ImageInfo.ParseHeader` 可解析头；阅读器内**无任何展示**

**方案**：
1. `ReaderViewModel` 加 `CurrentPageInfo` 属性（当前物理页：分辨率 WxH、文件大小（读流长度）、格式、解码缓存宽度 vs 原图宽度 = 清晰度指标、逻辑页结构"第 x 页/共 y 页·配对"）
2. 入口：工具栏"ℹ"按钮 + 快捷键 `I`（新 `ReaderAction.ImageInfo`，加入设置快捷键组默认绑定）；显示为小浮层/对话框（样式参考跳页对话框）
3. Webtoon 模式显示当前可见页（`PhysicalAt`）

**涉及**：`ViewModels/ReaderViewModel.cs`（+Nav/Decode partial）、`Views/ReaderWindow.Dialog.cs`、`Views/ReaderWindow.axaml`、`Services/SettingsStore.cs`（ReaderAction）
**测试**：属性断言（fixtures 已知尺寸）

## 8. 一键首页（P2）

**现状**：跳页对话框（G）可输入 1；无 Home 快捷键/按钮

**方案**：
1. `ReaderAction` 加 `GoHome`，默认快捷键 `Home`（通用组）
2. 执行：Single→`SetCurrentPhysical(0)`；Double→`JumpToPage(0)`（封面）；Webtoon→`ScrollWebtoonToPhysical(0)`
3. 工具栏加"⏮ 首页"按钮（或复用页码按钮左缘）
4. 连带可加 `GoEnd`（End 键）——成本低，一并做

**涉及**：`Services/SettingsStore.cs`（枚举+默认键）、`Views/ReaderWindow.Input.cs`（ExecuteAction）、`Views/ReaderWindow.axaml`、`ViewModels/ReaderViewModel.Nav.cs`
**测试**：`QuickJumpTests` 扩展

## 9. 连续下滚（中键按住）+ 滚动帧率（P1）

**现状**：
- 无"中键按住自动滚动"；滚动输入 = 滚轮（动量脉冲）/键盘（`ScrollWebtoonPages` 速度脉冲）/拖拽甩动
- 动画：`DispatcherTimer` 16ms（≈60fps）+ 指数摩擦；每帧 `SmoothScrollTick` → `UpdateWebtoonPages`：
  - 范围未变时跳过增删（已有 `rangeChanged` 守卫）
  - 但每帧仍执行 `TryRefreshWebtoonImage` 遍历可见页（`HasCachedWidth` 字典查）+ `SyncPreviewStripToScroll` + PageText 节流（已有）

**方案**：
1. **中键自动滚动（浏览器式）**：视口 `PointerPressed` 中键 → autoscroll 模式：记录按下点 → 移动鼠标改变滚动速度/方向（或固定速度持续滚）→ 释放停止（浏览器 Chrome 语义：按住中键，上下移鼠标控制速度，松手停）。实现：autoscroll 复用 `AddScrollVelocity`/`SmoothScrollTick`，目标速度 = f(鼠标偏移)，每帧修正速度
2. **帧率优化**：
   - `DispatcherTimer` → `CompositionTarget.Rendering`（每渲染帧回调，天然 vsync 同步）或保持 timer 但降开销
   - `TryRefreshWebtoonImage` 只在"范围变化或 scale 变化"时执行（当前每帧遍历）；增加 `_lastCheckScale` 守卫
   - 先**基准测量**（F12 有 `RELAYOUT SLOW` 日志；可加滚动帧间隔统计）再优化，避免盲改
3. 键盘连续滚动已存在（↑↓/PageDown），中键是补浏览器式体验

**涉及**：`Views/ReaderWindow.Webtoon.cs`、`Views/ReaderWindow.Input.cs`
**测试**：headless 模拟中键 + 移动 → 断言滚动速度/停止；帧间隔统计断言（宽松阈值防 CI 抖动）

## 10. 翻页先模糊后清晰（位置不变）（P1）

**现状**：`LoadCurrentAsync` 一次性按 `DecodeTargetWidth` 解码 → 解码期间显示旧图/空白（无占位）；Webtoon 只有灰块占位（无模糊图）

**方案**：
1. 分页模式两阶段：
   - 阶段 1：`DecodeAsync(phys, want=400)`（或当前缓存若有小图直接用）→ 立即显示
   - 阶段 2：目标宽度 `DecodeTargetWidth` 解码完成 → **原位替换**：`AcquireUiBitmap(new)` → `Image1 = new` → `ReleaseUiBitmap(old)`（BUG-004 引用计数顺序不能错）；`Ratio` 不变 → Relayout 不位移 → **位置不变**
   - 复用现成机制：`DecodeAsync` 缓存按宽度；`CurrentPageNeedsRedecode` 0.85 迟滞可参考
2. Webtoon：占位灰块 → 可选"小图模糊垫底"（占位 Border 内嵌 400px 缩略图 Image，解码完成后替换）——成本略高，可后置
3. 注意：预解码（`PrefetchNeighborsAsync`）与小图解码不冲突（不同宽度缓存键已按宽度区分？——**注意现状 `_pageCache` 一个物理页只存一张位图**，小图/大图会互相覆盖；需要"按宽度多级缓存"或"临时小图不入缓存"设计，见实现细节）

**涉及**：`ViewModels/ReaderViewModel.Decode.cs`、`Views/ReaderWindow.Canvas.cs`（替换点）
**测试**：`DecodeIsolationTests`/`ReaderE2eTests` 扩展——两阶段后最终宽度达标、位置不变断言

## 11. 图片是否最清晰（P0）

**现状**：
- 解码目标宽度：`want = ComputeBaseW() * _scale * dpr`，clamp `[1200, _maxDecodeWidth]`，`_maxDecodeWidth` 默认 **2048**（设置 1200~4096）
- 布局基准 `BaseH = 800`（固定）；**1:1 显示时 want ≈ 800×ratio×dpr，远小于 4K 原图宽度** → 几乎所有书都降采样，"不是最清晰"
- 原图尺寸（`_sizes`）**已有**但未参与解码决策
- 放大时 `CurrentPageNeedsRedecode` 会重解码，但**上限 2048/4096 挡住了原图尺寸**

**方案**：
1. 解码宽度决策改为：`want = min(显示需求, 原图宽度)`，显示需求 = 实际内容像素宽（`ComputeBaseW * scale * dpr`，去掉 2048 主观上限；或上限改为原图宽）
   - 原图宽来源：`_sizes[physical].Width`（已有）——**若未知（坏文件）回退现有逻辑**
2. 设置：`MaxDecodeWidth` 语义改为"解码宽度上限（0=原图全尺寸）"，默认改为 0 或 4096；或新增"始终原图质量"开关
3. 内存权衡：全尺寸解码内存峰值大（4K JPEG 解码 ~100MB 中间态）；保留降采样默认（如默认上限 4096 或原图宽），设置里显式说明；`CacheCap` 已有兜底
4. 结合 §7：信息面板显示"显示宽度 vs 原图宽度 vs 当前解码宽度"，用户可直接确认是否最清晰
5. 需先**基准**：4K 原图全尺寸解码耗时 vs 2048（fixtures 有 stress 书可测）

**涉及**：`Views/ReaderWindow.Canvas.cs`（want 计算）、`ViewModels/ReaderViewModel.Decode.cs`、`Services/SettingsStore.cs`
**测试**：解码宽度断言——显示需求>原图宽时 want=原图宽；设置 0=全尺寸

## 12. 配对起点应包含封面（第1页与第2页配对）（P2）

**现状**：`PageLayoutOptions.CoverSingle=true` 封面恒单页（第 1 页单独显示）；`FirstPairOffset` 只控制第 2 页起是否单页 → 封面从不参与配对

**方案**：
1. `PageLayoutOptions` 加 `CoverPair`（封面与第 2 页配对，即 CoverSingle=false 时从 (0,1) 起对）；UI：配对按钮改为菜单（配对起点 0/1 + 封面单页/参与配对），或设置项"封面处理"
2. 注意方向语义：RTL 下封面在右页，配对时封面右、第 2 页左
3. 与 §13 的配对逻辑梳理一起做

**涉及**：`Core/Paging/PageLayouter.cs`、`ViewModels/ReaderViewModel.cs`（PairText/按钮）、`Views/ReaderWindow.axaml`、`Core/Paging/PageLayouterTests`
**测试**：`PageLayouterTests` 扩展——CoverPair 布局断言

## 13. 双页配对逻辑确认（为什么"从第6页开始配对"？按钮为何失效？）（P0）

### 现状逻辑（代码确认）
`PageLayouter.Layout`：
1. 封面（索引0）恒单页
2. `FirstPairOffset=1` 时第 2 页（索引1）也单页
3. 从当前位置起两两配对；**`AspectRatio ≥ 1.6` 的页强制单页（横向跨页图）**——会打断配对序列
4. `RebuildLayout` 用 `_sizes`（AspectRatio）布局；**打开书只同步读前 2 页尺寸，其余后台补全**（补全前全部按 0.7 竖图占位）

### "从第6页开始配对"的可能根因
- 若书中前面若干页是**横向页**（比例≥1.6，如跨页图/风景页），它们各自单页 → 配对序列从后面重新开始，用户看到"从第 6 页开始配对（6-7）"
- 或：**尺寸未补全时**布局按 0.7 全配对；后台补全后 `RebuildLayout` 重排，横向页单页化 → 配对位置漂移（页数多的书补全慢，漂移发生得"晚"且突兀）

### "配对起点按钮失效"的根因
- `TogglePairOffset` → `RebuildLayout()`：**CurrentIndex 保持不变**（只 clamp 越界）→ 逻辑页结构变化后，同一逻辑索引对应的物理页变了 → 画面跳到别处（或双页↔单页变化），**用户预期"只看配对变化"而实际"整个画面跳走"**
- **PairOffset 不持久化**：每次打开书都从 0 开始，用户记忆中的起点与当前不符
- 页数多时：尺寸补全滞后 + 上述 CurrentIndex 不变 → 视觉上"改了没反应/乱跳"

### 方案（需用户确认语义后实施）
1. **配对切换保持当前物理页**：`TogglePairOffset` 时记录 `CurrentPhysical`，RebuildLayout 后 `SetCurrentPhysical(旧物理页)` 重定位（画面留在同一物理页，只变配对结构）
2. **尺寸补全完成后再重建配对**：LoadSizes 补全后 RebuildLayout（已有），但切换配对时若 `SizesLoaded==false`，先不重建、等补全后一次性重建（或切换后立即触发补全完成的 RebuildLayout）
3. **横向页规则透明化**：把 `SpreadThreshold` 暴露到设置（可关/可调），并在信息面板/预览栏标注"横向页已单页显示"；确认用户是否希望横向页仍参与配对（拉伸显示）
4. **PairOffset 持久化**：存 `settings.json`（全局默认），打开书时读入（书级可临时切换不保存，参考方向键的存取语义）
5. **封面配对选项**（§12）合并进配对菜单
6. 更新配对按钮 ToolTip/菜单文案，明确当前规则（封面单页 + 起点 1/2 + 横向页单页）

**涉及**：`ViewModels/ReaderViewModel.cs`、`Core/Paging/PageLayouter.cs`、`Views/ReaderWindow.axaml`、`Services/SettingsStore.cs`
**测试**：`PairOffsetSwitchTests` 扩展——切换后 CurrentPhysical 不变断言；横向页混合布局断言；补全后重建断言

---

## 14. 优先级与实施顺序建议

**P0（先修，影响核心体验/数据安全）**：
1. **#3 跨库/跨盘移动失败（静默）** — 数据操作正确性
2. **#13 配对逻辑**（含 #12 封面配对的语义确认）— 用户最困惑的阅读行为
3. **#11 清晰度** — "不是最清晰"是阅读器核心质量

**P1（体验增强，小改动大收益）**：
4. **#1 刷新+进度条**（与 #3 共用进度/异步改造，一起做）
5. **#2 默认方向**（含 Webtoon 续读修复）
6. **#10 模糊优先加载**（翻页体验）
7. **#9 中键连续滚动 + 帧率优化**（需先基准）

**P2（新功能，按需做）**：
8. **#8 首页**（成本最低，可插队）
9. **#4 快捷键**、**#5 其他文件**、**#6 新建文件夹**、**#7 图片信息**

依赖关系：#3 与 #1 同改 FileOps/进度；#13 与 #12 同改 PageLayouter/配对 UI；#11 与 #7 共用"解码宽度 vs 原图宽度"数据。

## 15. 需要用户决策的点

1. **#13 配对语义**：a) 你希望"封面参与配对"（第 1 页+第 2 页成对）吗？还是保持封面单页、仅修"切换跳页/不持久化/横向页错位"？b) "从第 6 页开始配对"的书，前面几页是否是横向（宽>高）的图？
2. **#9 中键行为**：浏览器式（按住中键，移动鼠标控制速度，松手停止）OK 吗？
3. **#11 清晰度策略**：默认改为"原图全尺寸"（内存开销大）还是"上限 4096"？可接受按需放大重解码（现状机制）+ 上限提至原图宽？
4. **#5 其他文件**：搜索要不要命中其他文件？封面墙/详情视图是否也显示？（建议：仅目录浏览器显示，不参与搜索）
5. **#4 快捷键**：先做单选版（Ctrl+C/V/X/Delete/F2/Enter/Backspace/侧键），还是直接做多选批量版？
