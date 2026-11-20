# 资料库"重新关联"（v4.20）

> 解决的问题：把资料库目录**改名/移动到别处**后，`libraries.txt` 里记的旧绝对路径失效 →
> 软件启动时跳过该库 → 用户只能"清空资料库 → 重新添加"，还会丢库顺序与当前浏览位置。
> 现在：失效的库会**保留在列表里并标记**，选中后点"关联"按钮选新位置即可原地修复。

## 1. 行为

| 场景 | 现在 |
|---|---|
| 启动时库路径失效 | 库**仍出现在下拉列表**，显示为 `库名（路径失效）`；不自动选中；状态栏提示"点关联按钮重新指定新位置" |
| 选中失效库 | 不扫描（避免报错），清空树与目录视图，状态栏提示 |
| 点"关联"按钮 | 弹出目录选择器（标题带库名）→ 选新目录 → 库条目路径/名称更新 → `libraries.txt` 重写 → 库根列表同步 → 重建树并显示内容 |
| 封面 / 进度 | 无需额外操作：`covers.json` 在库目录里，随目录一起改名/移动，重新关联后立即生效 |
| 关联到不存在的目录 / 已在列表中的目录 | 拒绝并提示，不改动任何数据 |

## 2. 细节

- `libraries.txt` 改为**用库列表整体重写**（`RewriteLibrariesFileAsync`），添加/关联/清空统一走这条路径，避免追加式写入留下旧行；
- 封面配置缓存（`_coverConfigs`）与 `ReadingStateStore.LibraryRoots` 在关联后立即同步；
- 日志：`LIB_RELINK 旧路径 -> 新路径`。

## 3. 代码入口

- `app/src/MangaAgent.App/ViewModels/LibraryItemViewModel.cs`：`Path`/`Name`/`IsValid` 可观察；`DisplayName`（失效时加"（路径失效）"）；`UpdatePath()`
- `app/src/MangaAgent.App/ViewModels/MainWindowViewModel.cs`：
  `RelinkLibraryAsync(lib, newPath)`（核心，可单测）/ `[RelayCommand] RelinkLibrary()`（弹选择器）/ `ShowLibraryAsync()` / `RewriteLibrariesFileAsync()` / `LoadSavedLibrariesAsync()`（保留失效库）
- `app/src/MangaAgent.App/Views/MainWindow.axaml`：库下拉旁新增"关联"按钮；下拉项显示 `DisplayName`
- 测试：`app/src/MangaAgent.Tests/PoC/LibraryRelinkTests.cs`（失效库保留+关联恢复封面、非法目标拒绝）

## 4. 使用与验证

使用：库改名/移动后启动软件 → 左侧下拉选中 `xxx（路径失效）` → 点右侧 **关联** → 选中新位置 → 树与封面自动恢复。

验证（不依赖手工点选）：`dotnet test --filter LibraryRelinkTests` 覆盖"改名后重新关联恢复封面"与"拒绝非法目标"。
