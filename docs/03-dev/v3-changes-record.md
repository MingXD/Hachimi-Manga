# v3 变更记录（第一批）

> 2026-08-25。v3 第一批：**#14 排序、#2a 续读、#8 首页、#6 新建文件夹、#3 跨库移动**（方案见 v3-issues-plan.md §16）。
> 测试基线：67 → **82/82 通过**。
> 上一批：v2-changes-record.md（v2.0→v5.2）。

## 一、本次改动（2026-08-25，第一批）

| # | 修复/改动 | 文件位置 | 可能影响的功能（风险点） |
|---|---|---|---|
| 3-1 | **#14 页面排序修复**：`ImageInfo.CompareNatural` 两分支返回值互换（原实现"一方有数字"时两个方向都返回 -1 → 比较器违反反对称性 → List.Sort 结果不可预测，字母命名的后记页（xy）常排到数字正文页（_1/0002）之前成为封面）；数字优先，与 Windows 资源管理器一致 | Core/Comics/ImageInfo.cs + 新增 Tests/Comics/ImageInfoTests.cs（5 测试：字母后记排后/补零/对称性等） | 页面顺序、封面识别（Zip/Folder 源共用） |
| 3-2 | **#2a Webtoon 续读修复**：`SaveState` 按三模式完整映射（原把 Webtoon 存成 Single）；恢复 `switch` 三模式（原只认 Double/Single）→ 滚动模式续读不再降级为单页 | App/ViewModels/ReaderViewModel.State.cs + SettingsSystemTests.cs（Resume_Webtoon_Mode_Preserved） | 续读行为、书签/进度 |
| 3-3 | **#8 一键首页/末页**：新增 `ReaderAction.GoHome/GoEnd`（默认键 Home/End）；`ReaderViewModel.GoHome/GoEnd` 命令（单页=物理页边界、双页=逻辑页边界）；视图层 Webtoon 分支滚动到物理页边界；工具栏 ⏮/⏭ 按钮 | SettingsStore.cs（枚举+默认键）、ReaderViewModel.Nav.cs、ReaderWindow.Input.cs（ExecuteAction+点击方法）、ReaderWindow.axaml + QuickJumpTests.cs（GoHome_GoEnd） | 快捷键表（旧 settings.json 无新键 → 默认补齐）、工具栏布局 |
| 3-4 | **#6 新建文件夹**：`NewFolderDialog`（输入名称）+ `MainWindowViewModel.NewFolder` 命令 + `NewFolderCore`（可测纯逻辑，同名/非法名报错）+ 重建树并保持当前位置（RefreshTreeKeepFolderAsync）+ 面包屑区"＋新建文件夹"按钮 | 新增 Views/NewFolderDialog.cs、MainWindowViewModel.cs、MainWindow.axaml + FolderBrowserTests.cs（NewFolder_Creates） | 目录浏览刷新、面包屑 |
| 3-5 | **#3 跨库/跨盘移动修复**：`FileOps.MoveTo/CopyTo` 返回 `(bool, string?)`（原跨卷必失败且静默吞异常）；跨卷回退"复制成功后才删源"；拒绝源=目标/目标=源所在目录/目标在源内部；`FolderEntryItemViewModel` 复制/移动改异步执行 + 失败弹窗（新增 AlertDialog） | Core/Library/FileOps.cs、ViewModels/FolderEntryItemViewModel.cs、MainWindowViewModel.cs（NotifyError 注入）、新增 Views/AlertDialog.cs + Tests/Library/FileOpsTests.cs（7 测试） | 文件操作右键菜单、UI 响应性（不再卡界面） |

## 二、本次小改（2026-08-25，第一批后用户反馈 4 项）

| # | 修复/改动 | 文件位置 | 可能影响的功能（风险点） |
|---|---|---|---|
| 3-6 | **首页/末页按钮移到页码块右侧**（◀ ▶ 页码 ⏮ ⏭ 连排）；快捷键 Home/End 保持不变（已在设置-快捷键默认绑定） | ReaderWindow.axaml | 工具栏布局 |
| 3-7 | **属性对话框文本可复制**：TextBlock → SelectableTextBlock | Views/PropertiesDialog.cs | 属性框样式 |
| 3-8 | **删除/移动/复制/新建后自动刷新**：原 RefreshAction 用旧树快照重建列表（文件系统已变但 FolderNode 快照不变 → 界面"不刷新"）；统一改为 `RefreshCurrentViewAsync`（重建库树 + 保持当前目录/搜索态），WireEntry/BookItem Wire 共用 | ViewModels/MainWindowViewModel.cs + FolderBrowserTests（RefreshCurrentView_After_Delete） | 目录浏览刷新、封面墙 |
| 3-9 | **滚动续读 fit 修复**：根因=LoadSizes 只同步读前 2 页，续读恢复页（如第 200 页）比例未加载 → `FitWebtoonToPage` 用 0.7 占位比例 → 页面显示偏小但显示 100%；修复=①构造时同步读恢复页尺寸 ②新增 `WebtoonLayoutChanged` 事件，LoadSizes 后台补全后 RebuildLayout 触发 → 视图 FitWebtoonToPage 重建（真实比例） | ReaderViewModel.State.cs / ReaderViewModel.cs（事件+RebuildLayout）、ReaderWindow.axaml.cs（订阅/退订）+ SettingsSystemTests（Resume_Webtoon_RestorePage_Size_Loaded_Sync） | 滚动模式进入/续读 fit、模式切换（RebuildLayout 路径） |
| 3-10 | 测试基线 82 → **84/84** | — | — |

## 三、本次小改 2（2026-08-25，用户确认方案后实施）

| # | 修复/改动 | 文件位置 | 可能影响的功能（风险点） |
|---|---|---|---|
| 3-11 | **#15 Home/End 文本按钮**：工具栏 ⏮/⏭ → "Home"/"End"（MinWidth 44），点击逻辑与快捷键不变 | ReaderWindow.axaml | 工具栏布局 |
| 3-12 | **#16 新建文件夹无效修复**：原 `CurrentFolder==null` 时命令静默 return（启动未进入目录/树未加载时点按钮"无效"且不创建）；改为有库用**库根**作为创建目标，无库提示"请先添加资料库" | MainWindowViewModel.cs（NewFolder） | 新建文件夹入口 |
| 3-13 | **#17 滚动续读页码修复**：根因=滚动只更新页码显示（UpdateWebtoonPageText）不更新位置 + CurrentPhysical 在滚动模式返回逻辑映射 → 续读/书签存旧页；修复=①新增 `SyncWebtoonPosition`（更新 _currentPhysical + 2s 节流保存）②视图滚动位置变化时调用 ③CurrentPhysical 滚动模式返回滚动位置 ④关闭时 SaveState 存最新值 | ReaderViewModel.State.cs / Nav.cs、ReaderWindow.Webtoon.cs + SettingsSystemTests（Webtoon_Scroll_Position_Persisted_On_Resume） | 续读、书签语义（滚动模式=当前可见页）、预览栏当前页 |
| 3-14 | 测试基线 84 → **85/85** | — | — |

## 四、本次小改 3（2026-08-25，用户确认后实施）

| # | 修复/改动 | 文件位置 | 可能影响的功能（风险点） |
|---|---|---|---|
| 3-15 | **#1 Home/End 快捷键进通用组**：`SettingsStore.Load` 补齐旧配置缺失的默认动作（原只校验组存在 → 旧 settings.json 无 GoHome/GoEnd，设置界面不显示、快捷键不生效）；不重置用户自定义绑定 | Services/SettingsStore.cs + SettingsSystemTests（Load_Merges_Missing_Default_Actions） | 快捷键表迁移 |
| 3-16 | **#2 新建文件夹/重命名"完全无效"根治**：根因=`Window.ShowDialog()` 异步不阻塞，同步写法"调用后立即返回 null"→ 命令从未执行；NewFolderDialog/RenameDialog 改 `await ShowAsync`（tcs 模式，同 ConfirmDialog），调用方（NewFolder/Rename ×2）改 async | Views/NewFolderDialog.cs、RenameDialog.cs、ViewModels/MainWindowViewModel.cs、FolderEntryItemViewModel.cs、BookItemViewModel.cs | 新建文件夹、右键重命名（目录条目+封面墙/搜索条目） |
| 3-17 | 坑位记录：AGENTS.md 环境坑位新增"ShowDialog 异步不阻塞" | AGENTS.md | 后续对话框开发必看 |

## 五、批次 A（2026-08-25，用户确认后实施）

| # | 修复/改动 | 文件位置 | 可能影响的功能（风险点） |
|---|---|---|---|
| 3-18 | **#2b 默认阅读方向**：`AppSettings.DefaultDirection`（Rtl/Ltr）+ 设置窗口"打开书行为"加下拉 + OpenBook 注入 `ReaderViewModel.DefaultDirection` + State.cs 无状态时使用 | SettingsStore.cs、SettingsDialog.cs、MainWindowViewModel.cs、ReaderViewModel.State.cs + SettingsSystemTests（DefaultDirection_RoundTrip） | 设置界面布局、打开书方向 |
| 3-19 | **#1 复制/移动进度条（状态栏文本）**：`FileOps.CopyToAsync/MoveToAsync`（IProgress 逐个报告，跨卷移动=进度复制+成功后删源）；条目 ProgressAction 注入状态栏"正在复制 x/y…"；粘贴等刷新 | FileOps.cs、FolderEntryItemViewModel.cs、MainWindowViewModel.cs + FileOpsTests（CopyToAsync_Reports_Progress、MoveToAsync） | 复制/移动右键菜单、UI 响应性 |
| 3-20 | **#4 多选版资源管理器快捷键**：两列表 SelectionMode=Multiple；Ctrl+C/X 复制/剪切（内部剪贴板）、Ctrl+V 粘贴（UniquePath 防覆盖）、Delete 批量回收站、Ctrl+A 全选、F2 重命名（单选）、Enter 打开/进入、Backspace/Alt+↑ 上级；鼠标侧键 XButton1/XButton2 前进/后退（导航历史栈，回退后重扫防旧快照）；搜索框聚焦时不劫持 | MainWindow.axaml、MainWindow.axaml.cs、MainWindowViewModel.cs + FolderBrowserTests（Clipboard_CopyPaste_And_NavHistory） | 主窗口键盘/鼠标交互、导航历史、搜索框文本编辑不受影响 |
| 3-21 | 测试基线 86 → **90/90** | — | — |
| 3-22 | **顺带发现并修复**：历史回退显示旧树快照（GoBack/GoForward 现回退后重扫） | MainWindowViewModel.cs | 侧键/历史导航 |

## 六、排查修复 2（2026-08-25，用户报错线索定位）

| # | 修复/改动 | 根因 | 文件位置 |
|---|---|---|---|
| 3-23 | **目录扫描失败（"Failed to compare two elements in the array"）** | `ImageInfo.CompareNatural` 用 `long.Parse` 比较数字块——文件名含超长数字串（>long.MaxValue）时溢出抛异常 → `List.Sort` 崩溃 → 整个目录树扫描失败（bz 目录命中）；改用"去前导零比长度+逐位比"溢出安全比较 | Core/Comics/ImageInfo.cs + ImageInfoTests（Huge_Digit_Names_Do_Not_Overflow） |
| 3-24 | **Enter 无效** | ① Avalonia 12 主推 `Key.Return`（兼容 `Key.Enter`）② 焦点在搜索框时 Enter 被 TextBox 拦截——用户点过搜索框后点条目，焦点不自动回列表；修复=列表选中/点击时自动聚焦（OnListSelectionChanged/OnListPointerPressed）+ Enter 兼容 Key.Return（主窗口+跳页框） | MainWindow.axaml(.cs)、ReaderWindow.Dialog.cs + FolderBrowserTests（Enter_Key_Opens_Selected_Or_Blocked_By_SearchBox） |
| 3-25 | **测试基建隔离** | `new MainWindowViewModel()` 异步加载真实 `%LOCALAPPDATA%\HachimiManga\libraries.txt` 污染测试；`AppData` 改为可覆盖（同 SettingsStore.BaseDir 模式） | MainWindowViewModel.cs |
| 3-26 | 测试基线 90 → **92/92** | — | — |

## 七、待办（下一批，见 v3-issues-plan.md §16）
- #4 多选版快捷键、#1 进度条+跨库刷新、#2b 默认方向设置、#9 中键滑动、#10 模糊优先、#11 清晰度三档
- #13 配对（用户指示暂缓）、#5 其他文件、#7 图片信息

## 三、Avalonia 备忘（本次踩坑）
- `TextBox.Watermark` 已过时 → 用 `PlaceholderText`
- Core 层 public 方法才能被 Tests 项目直接访问（无 InternalsVisibleTo）
