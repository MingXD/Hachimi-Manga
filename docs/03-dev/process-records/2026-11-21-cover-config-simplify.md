# 2026-11-21 封面机制瘦身：只保留库内 covers.json（v4.20 收尾）过程记录

## 任务概述

v4.20 第一步把封面搬进库内 `covers.json` 后，用户实测发现两个问题：①清空库后新建 `25.08` 库根，本来没有封面却冒出两个（迁移把 db 残留记录按新库根拼路径"认领"了）；
②再加 `bz` 库根，本应能读到 25.08 的全部封面，却仍然只有那两个。
排查确认是两处缺陷：读取只用"最长匹配库根"的文件（父库根文件被屏蔽）、迁移靠"键拼路径"猜归属（会错配）。
用户据此判断"两套真相太复杂"，要求：**只保留库内 covers.json 一套**，删掉迁移、导出/导入等历史机制，并清理代码。

## 过程简述

1. **取舍分析**（先给方案等决策）：明确保留 3 条规则——写最具体库根 / 读逐级回退 / 清除清全部候选；删除 db 回退、迁移、导出导入；
   嵌套库根本身不算问题，规则一致即可预测；产品建议"只加最外层库根"最简单。
2. **Core 减法**：
   - `ReadingStateStore`：删 `FolderCovers` 建表、全部封面 CRUD、`MoveFolderCover`、`LoadAllFolderCovers`/`DeleteFolderCoverByStorageKey`/`ValidateFolderCoverRelPath`、
     `ExportToJson`/`ImportFromJson` + 3 个 DTO + `ResolveStoredPath`、`TryLoadLibraryRootsFromFile`；
     新增 `BackupLegacyFolderCovers`（一次性把历史表导出成 JSON 备份后 `DROP TABLE`）；
   - `FolderCoverConfig`：删 `MigrateFromDatabase`/`ExportWithLibraryCovers` 及私有辅助。
3. **App 减法与修正**：
   - `ApplyFolderCovers` 改为"按候选库根（长→短）逐级回退"；
   - `SetFolderCoverAsync` 只写最具体库根，库外路径/写失败直接提示（不再回退 db）；
   - `ClearFolderCover` 清除所有候选库根的同键；
   - `ApplyCoverAfterMove` 目标端写最具体、源端清全部候选；
   - `FolderEntryItemViewModel` 删除旧语义 `SetFolderCover(folderPath, relPath)`；
   - `SettingsDialog` 的"数据管理"卡片改为存储位置说明（无导出/导入按钮）；
   - `RefreshLibraryRootsAndMigrate` 内改为调用一次性 legacy 备份。
4. **测试**：删除 `FolderCoverTests`、`CoverFixAndExportTests`、`PathStorageTests` 中的封面用例、`CoverConfigTests` 的 3 个迁移用例；
   新增 legacy 备份测试、「嵌套库根回退」、「清除清全部候选」测试。
5. **验证**：全量 **168/168 通过**（原 177，删 12 加 3）；真实库探针确认 `bz\25.08` 下 93 个文件夹条目中 **90 个有封面**（来自 `bz\covers.json` 的逐级回退）。

## 遇到的问题（现象 → 原因 → 解决）

| 现象 | 原因 | 解决 |
|---|---|---|
| 新库根下莫名出现 2 个封面 | 迁移拿 db 残留记录的相对键去新库根下拼路径，拼到同名文件夹就"认领" | 删除迁移机制，改为一次性备份历史表后删表 |
| 加父库根后仍读不到子库根内的封面 | 读取只查"最长匹配库根"那一个文件 | 读取改为候选库根逐级回退 |
| 清除封面后"又回来了" | 只清最长库根的键，父库根残留仍会被回退命中 | 清除时清全部候选库根 |
| 测试里 `30.08\Gallery` 设封面失败 | 封面图放在外层库根，内层库根校验"必须在库内"不通过 | 每个库根各放一张库内封面图（校验语义正确，测试写错） |
| legacy 备份断言失败 | `JsonSerializer` 默认把中文转义成 `\uXXXX` | 改用 `JsonNode` 解析后断言 |

## 最耗时项

删除波及面（Core 200+ 行、App 100+ 行、5 个测试文件）+ 保证删除后"读/写/清"三条规则在嵌套库根下仍然自洽；期间多次编译错误来自残留引用，需要逐个清理。

## 最耗 token 项

`MainWindowViewModel` 与 `ReadingStateStore` 的大段删除与替换（含"移动文件夹"的三种归属情形重写），以及反复编译-修错循环。

## 规避与完善方案

- 规则收敛到 Core（`FolderCoverConfig`）+ App 的 3 个入口（读/写/清），不再有第二条存储路径；
- 嵌套库根的产品建议写进设计文档（只加最外层最简单），代码层面用"最具体优先 + 外层兜底"兜住各种目录习惯；
- 第二步（`progress.json`）将复用同一套原子写/回退规则，不再引入迁移与导出。
