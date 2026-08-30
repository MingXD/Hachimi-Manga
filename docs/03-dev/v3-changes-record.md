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

## 四、待办（第二批，见 v3-issues-plan.md §16）
- #4 多选版快捷键、#1 进度条+跨库刷新、#2b 默认方向设置、#9 中键滑动、#10 模糊优先、#11 清晰度三档
- #13 配对（用户指示暂缓）、#5 其他文件、#7 图片信息

## 三、Avalonia 备忘（本次踩坑）
- `TextBox.Watermark` 已过时 → 用 `PlaceholderText`
- Core 层 public 方法才能被 Tests 项目直接访问（无 InternalsVisibleTo）
