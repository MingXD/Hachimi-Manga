# 跨库复制/移动带数据 + 书签 UI 精简 + 减少冗余文件（v4.22）

> 三件事：
> ①库内漫画复制/移动时，把**封面与进度/书签**一起带到目标库；
> ②书签功能精简（去掉"上一个/下一个书签"，书签按钮带选中态，列表加重命名与清空）；
> ③**减少程序在库根输出的冗余文件**（不再产生 `.bak`），并提供手动清理入口。

## 1. 跨库复制 / 移动带数据

| 情形 | 细节 |
|---|---|
| 同库内移动 | 封面改键（原有）+ **进度/书签迁到新键**（本次补齐；此前移动会丢进度） |
| 同库内复制 | 封面按新键写入（封面图随目录一起复制）+ 进度/书签复制到新键 |
| 跨库移动/复制 | 源库记录 → 目标库 `progress.json` / `covers.json`；源端删除（移动时） |
| 库外 → 库内 | 数据在 `reading.db` → 读出后写入目标库，移动时删 db 行 |
| 库内 → 库外 | 数据写入 `reading.db`（库外书的权威存储） |
| **目标已有同名数据** | **以本次操作为准覆盖**（与"复制文件"语义一致） |
| **封面图不在被移动/复制的目录内**（跨目录共用封面） | **跳过并计数**，状态栏提示"N 项封面图不在被移动目录内未随迁"（不搬图片本体） |
| 文件夹整体移动 | 以**键前缀**匹配，其下所有书的进度/书签一起迁 |

实现：`LibraryStateService.CopyOrMoveBookData(srcAbs, dstAbs, isCopy)`（进度/书签，支持前缀），
主窗口 `CopyPathsToAsync/MovePathsToAsync` 在每次文件操作成功后调用；封面由 `ApplyCoverAfterCopy` / `ApplyCoverAfterMove` 处理。
`FileOps.CopyToAsync` 现在也返回实际目标路径（与 `MoveToAsync` 一致），便于目标键计算。

## 2. 书签 UI 精简

| 之前 | 现在 |
|---|---|
| 书签 / ◀书签 / 书签▶ / 书签（N） 四个按钮；快捷键 B、N、Shift+N、L | 只保留 **书签** 与 **书签列表（N）** 两个按钮；快捷键 **B**（切换）、**L**（列表） |
| 书签按钮无状态 | **选中态高亮**：当前页是书签时按钮变「★ 书签」+ 高亮底色/描边/文字（夜间金色 `#FFD700`、白天蓝色 `#0078D4`），翻页实时刷新 |
| 面板只能单条删除 | 面板顶部新增 **清空书签**（二次确认后清空本书全部书签；只清书签、不动进度） |
| 列表点条目跳转 | 保留（这是唯一的跳转入口，避免与阅读方向混淆） |

- 存储层新增 `ILibraryStateStore.ClearBookmarks(bookPath)`（库内 `progress.json` 清空该书书签；保留空列表防合并回吐）。
- 设置页快捷键表同步：删除"下一个/上一个书签"两项，保留"当前页加/取消书签"与"书签列表"。

## 3. 减少冗余文件（用户要求）

| 项目 | 之前 | 现在 |
|---|---|---|
| 写入备份 | 每次写入都留 `covers.json.bak` / `progress.json.bak`（库根下多 2 个文件、体积翻倍） | **不再产生 `.bak`**：写入改为 `.tmp` → `File.Move(overwrite)` 单步原子替换 |
| 坏 JSON 备份 | `*.broken-<时间戳>`（可能堆积多个） | **单一 `*.broken`**（覆盖式，只在异常时出现一个） |
| 历史遗留清理 | 无 | 设置 → 数据管理 → **清理备份文件**（删库根下 `*.bak`、`*.bak-*`、`*.tmp`、`*.broken` 与 AppData 的 `legacy-covers-backup-*`；不动正式文件与漫画文件） |
| 日志 | 未变 | 本次不动（用户选择"只做手动，不改日志策略"） |

> 代价：不再保留"写入前快照"，写坏/误删无法从 `.bak` 回滚（用户明确不需要回滚）。

## 4. 代码入口

- `app/src/MangaAgent.Core/Storage/LibraryStateService.cs`：`CopyOrMoveBookData` / `ClearBookmarks`
- `app/src/MangaAgent.Core/Storage/LibraryProgressConfig.cs`：`ClearBookmarks` / `StateSnapshot` / `BookmarkSnapshot`
- `app/src/MangaAgent.Core/Storage/FolderCoverConfig.cs`、`LibraryProgressConfig.cs`：去掉 `.bak`、坏文件单一 `.broken`
- `app/src/MangaAgent.Core/Storage/LibraryFileCleaner.cs`：`CleanBackups`
- `app/src/MangaAgent.Core/Library/FileOps.cs`：`CopyToAsync` 返回 `TargetPath`
- `app/src/MangaAgent.App/ViewModels/MainWindowViewModel.cs`：复制/移动后迁移数据 + 跳过计数 + `CleanBackupFiles`
- `app/src/MangaAgent.App/ViewModels/ReaderViewModel.Nav.cs`：`IsCurrentPageBookmarked` / `BookmarkButtonText` / 高亮色 / `ClearAllBookmarks`
- `app/src/MangaAgent.App/Views/ReaderWindow.axaml` / `.Preview.cs`：按钮双态 + 面板清空按钮（确认）
- `app/src/MangaAgent.App/Views/SettingsDialog.cs`：清理备份文件按钮
- 测试：`app/src/MangaAgent.Tests/PoC/CrossLibraryDataTests.cs`（4 例，含清理器）

## 5. 验证要点

1. 库 A 的一本书（有封面 + 进度 + 书签）复制到库 B → 库 B 的 `covers.json` / `progress.json` 出现对应键；源保持不动。
2. 同一本书从 A **移动**到 B → 目标有数据、源端记录消失；目标原本有旧数据 → 被覆盖。
3. 封面图放在库 A 其他目录（不在被移动的文件夹内）→ 移动后状态栏提示"N 项封面图不在被移动目录内未随迁"。
4. 书签按钮：在已加书签的页显示「★ 书签」且高亮；翻到别页恢复普通外观。
5. 书签列表 → 清空书签 → 确认 → 列表空、`progress.json` 的 `bookmarks` 变空。
6. 设置 → 数据管理 → 清理备份文件：库根下 `*.bak` 等消失而 `covers.json`/`progress.json` 保留。
