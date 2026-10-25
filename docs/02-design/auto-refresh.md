# 目录/文件变化的自动更新机制（v4.19）

> 目标：在**不占性能**的前提下，让"外部改动的文件系统"能在软件里及时可见——
> 覆盖用户三个真实场景：①把漫画**移动/复制**进某个目录 ②**新下载**了一本漫画 ③**新建**了一个文件夹。
> 设计原则：便宜的操作做在关键路径上，昂贵的操作只在必要时（或后台）做。

## 1. 问题背景（v4.18 及以前）

- **没有 FileSystemWatcher**：目录树只在 启动 / 添加库 / 手动「重新扫描」/ **软件内**新建·移动·复制·删除后重建；
- 更关键的是**重建很贵**：`FolderTreeScanner` 递归全库并**逐个打开压缩包/PDF 读页数**；
  `LibraryScanner` 同样逐个开包（且每层 `GetFiles` 多次）。
- 实测（用户库 `F:\额外储存\bz`，2610 个目录、67286 个 zip/cbz）：仅枚举就要 5 秒，逐个开包则是分钟级。
- 结果：重建只能省着用 → 在资源管理器里下载/移动/新建之后，软件里"什么都看不到"，必须手动重新扫描（且很慢）。

## 2. 设计（三层，全部可单独关闭）

| 层 | 机制 | 触发点 | 成本 |
|---|---|---|---|
| **P1 浅扫描 / 浅刷新** | 扫描**不再打开压缩包**（页数=0，界面后台懒补）；重建只重列当前目录一层 + 直接子目录的书（父层据此判定单行本/统计） | 所有原先"重建树"的路径（新建/移动/复制/删除/粘贴） | 目录枚举级，毫秒 |
| **P2 自动触发** | ①**导航**（进入目录/返回上级/面包屑/前进后退）→ 浅刷新目标层；②**窗口重新激活** → 比较"当前目录 + 库根"的 `LastWriteTime`，变了才浅刷新；③**侧边栏树**改为 `ObservableCollection`，原地增删子文件夹时可观察更新（保留展开状态） | 用户动作 | 1 次 `stat`（不变化时≈0） |
| **P3 当前目录 watcher** | 只给**当前显示的那一层目录**挂 `FileSystemWatcher`（切目录即重挂），事件去抖 800ms 合并；缓冲溢出 → 整体浅刷新兜底；关窗释放 | 文件系统事件 | 单目录 watch，几乎为零；不监听库根（避免内核缓冲溢出/句柄压力） |

### 2.1 页数懒补（配合 P1）

扫描不再读页数 → 文件型书（CBZ/PDF）先显示 `… 页`，`ShowFolderContents` 渲染后启动后台任务
（`EnrichPageCountsAsync`，每个视图最多 200 本，`Task.Run` 里开包读索引），完成后回填
`FolderEntryItemViewModel.ResolvedPageCount`（可观察属性，`Subtitle` 自动刷新）并同步节点 `Books` 记录。
文件夹书（散图）页数=图片数，扫描即得，无需懒补。

### 2.2 去重与重入

- 刷新请求用 `_refreshRequest` 排队：刷新期间到来的请求**不丢弃**，本轮结束立刻再跑一次
  （旧实现"正在刷新就 return"会让粘贴/移动后的刷新静默失效）。
- 当前目录被外部移走/删除 → 回退到最近仍存在的祖先（不再残留幽灵目录）。

## 3. 触发矩阵

| 用户行为 | 触发机制 | 延迟 |
|---|---|---|
| 软件内 新建/删除/移动/复制/粘贴 | P1（直接调用浅刷新） | 即时 |
| 软件内 进入目录 / 向上 / 面包屑 / 前进后退 | P2① | 即时 |
| 资源管理器里往**当前目录**拷入/删除/改名 | P3（+P2②兜底） | ≈0.8s（去抖） |
| 资源管理器里往**别处**改动，然后切回软件 | P2②（整层浅刷新 + 侧边栏树更新） | 切回时 |
| 库根下新增顶层文件夹 | P2②（库根时间戳变化 → 浅刷新库根） | 切回时 |
| 深层子目录里的改动（非当前层） | 进入该目录时 P2① | 进入时 |
| 需要页数全量校准 / 怀疑视图不一致 | 手动「重新扫描」（v4.19 起也是浅扫描，秒级） | 手动 |

## 4. 性能实测（同一用户库）

| 操作 | v4.18 | v4.19 |
|---|---|---|
| 建整棵树 | 逐个打开 67286 个压缩包（分钟级） | **0.9~1.2 s**（纯目录枚举） |
| 库扫描（`LibraryScanner`） | 2314 ms（每层多次枚举 + 开包） | **533 ms**（每层枚举 1 次，不开包） |
| 刷新当前目录（新建/移动后） | 重建整棵库树（分钟级） | **毫秒级**（一层 + 子目录书目） |
| 窗口切回（无变化） | — | **1 次 stat** |

## 5. 限制（有意为之）

- **不做全库 watcher**：库大时内核缓冲易溢出、句柄多；当前目录 watcher + 激活检查已覆盖用户场景。
- **页数/ComicInfo 标题**：浅扫描不读压缩包内部 → 网格里 CBZ 标题=文件名（阅读器内仍读真实元数据），页数后台懒补。
- **深层递归目录**（如图库根有 200 层）：文件系统已限制路径长度，实际不构成问题。

## 6. 代码入口

- `app/src/MangaAgent.Core/Library/FolderTree.cs`：`ScanAsync`（浅结构全量）、`RefreshShallowAsync`（原地浅刷新）
- `app/src/MangaAgent.Core/Library/LibraryScanner.cs`：`readPageCounts=false` 轻量模式（启动/添加库/重扫）
- `app/src/MangaAgent.App/Services/FolderWatcher.cs`：当前目录 watcher（去抖/重挂/溢出兜底）
- `app/src/MangaAgent.App/ViewModels/MainWindowViewModel.cs`：
  `ShowFolderContents`（挂 watcher + 启动懒补）、`RefreshCurrentViewAsync`、`RefreshNodeAndRenderAsync`、
  `RefreshIfStaleAsync`、`EnrichPageCountsAsync`
- `app/src/MangaAgent.App/Views/MainWindow.axaml.cs`：`Activated` → `RefreshIfStaleAsync`，`Closed` → `StopWatching`
- 回归测试：`app/src/MangaAgent.Tests/PoC/AutoRefreshTests.cs`
