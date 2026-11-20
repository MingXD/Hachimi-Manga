# 2026-11-21 封面改为库内 covers.json（v4.20 第一步）过程记录

## 任务概述

用户提出：现有"封面保存/导出"机制太麻烦（记录在 `%LOCALAPPDATA%\HachimiManga\reading.db`，库改名/移动后要靠重新关联或导出导入），
希望简化成"封面相对路径写进库里的一个文件，软件每次读这个文件，增删改都在库里完成"，这样库名/位置随便改。
经决策：文件放**库根下 `covers.json`**、封面路径**相对库根**、库不可写时**回退 SQLite 并提示**、旧记录**迁移后删除**、
并且**阅读进度/书签也一并入库**（分两步实施，本次为第一步：只做封面）。

## 过程简述

1. 梳理现状：`ReadingStateStore.FolderCovers(FolderPath, RelPath, UpdatedAt)`，路径为库根相对（v4.13），
   移动目录需 `MoveFolderCover` 手工迁移（v4.12），封面图限制"必须在文件夹内"（`ValidateFolderCoverRelPath`）。
2. 新增 Core `FolderCoverConfig`（库内 `covers.json`）：
   - 键 = 文件夹相对库根（`/` 分隔，库根自身 `"."`），值 = 封面相对库根（可跨目录共用）；
   - 指纹（`LastWriteTimeUtc` + 长度）检测外部手改；脏标记 + 原子写（`.tmp` → `File.Replace`，留 `.bak`）；
   - 写前 merge-on-write；坏 JSON 备份为 `covers.json.broken-<时间戳>` 后重建，绝不静默覆盖；
   - `MigrateFromDatabase`（db → 库文件 → 删 db 记录，幂等）、`ExportWithLibraryCovers`（导出汇总）。
3. `ReadingStateStore` 增加 `LoadAllFolderCovers` / `DeleteFolderCoverByStorageKey` / `TryLoadLibraryRootsFromFile`。
4. App 侧：`ShowFolderContents` 改 `ApplyFolderCovers`（库文件优先 + db 回退）；设置/清除封面写库文件，失败回退 db 并提示一次；
   移动目录时"移动前快照 → 移动后改键"（同库改键 / 跨库重建 / 移出库删键）；库加载后自动迁移；设置页导出/导入适配。
5. 验证：新增 `CoverConfigTests`（10 例）与 `LibraryCoverTests`（2 例），全量 **177/177 通过**。

## 遇到的问题（现象 → 原因 → 解决）

| 现象 | 原因 | 解决 |
|---|---|---|
| 测试 `Rejects_Outside_Library...` 断言失败 | 测试里用的是不存在的图片文件，先命中了"图片不存在"分支 | 先在库外真实创建图片再断言"必须在库内" |
| 外部手改 `covers.json` 后读不到新值 | `GetCoverFullPath` 漏了文件指纹检查（只有 `ResolveFor`/`HasCover`/`All` 有） | 补 `FileChanged()` 检查后重读 |
| 导出 JSON 的键名对不上 | 导出 DTO 用的是 `[JsonPropertyName("folder"/"image")]` 小写键 | 汇总时改用 `covers`/`folder`/`image` 小写键 |
| 设置页导出拿不到库内封面 | `SettingsDialog` 里没有库根列表（`ReadingStateStore.LibraryRoots` 为空） | 新增 `TryLoadLibraryRootsFromFile`，导出/导入前从 `libraries.txt` 载入（顺带修好导入的相对路径匹配） |
| 移动目录后封面丢失风险 | 移动后源路径已不存在，取不到旧封面值 | 移动前 `SnapshotCoverForMove` 捕获（库根 + 键 + 相对值），移动后按前缀替换改键 |

## 最耗时项

移动目录的封面键迁移逻辑（同库改键 / 跨库重建 / 移出库删除三种情形，且要处理"封面是否随目录移动"的前缀改写），
以及"库不可写 → 回退 db"的分支在 4 个写入点（设置/清除/移动/迁移）各跑一遍的连通性验证。

## 最耗 token 项

`MainWindowViewModel`（1000+ 行）中封面相关的多处替换与新增辅助方法；以及为迁移/导出设计"键与值都相对库根"的规范化与校验一致性。

## 规避与完善方案

- 所有封面逻辑收敛到 `FolderCoverConfig`（Core，可单测），App 只做触发与提示；
- 写失败只在每库提示一次（`_coverFallbackWarned`），避免刷状态栏；
- 第二步（`progress.json`）将复用同一套原子写/merge/回退思路，但需要节流与"关书/关窗强制落盘"；
- 待办：删除文件夹时顺带清理封面键（目前无害残留）、跨目录封面的导出格式（当前跳过并计数）。
