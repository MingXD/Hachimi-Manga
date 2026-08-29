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

## 二、待办（第二批，见 v3-issues-plan.md §16）
- #4 多选版快捷键、#1 进度条+跨库刷新、#2b 默认方向设置、#9 中键滑动、#10 模糊优先、#11 清晰度三档
- #13 配对（用户指示暂缓）、#5 其他文件、#7 图片信息

## 三、Avalonia 备忘（本次踩坑）
- `TextBox.Watermark` 已过时 → 用 `PlaceholderText`
- Core 层 public 方法才能被 Tests 项目直接访问（无 InternalsVisibleTo）
