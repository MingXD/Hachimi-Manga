# 2026-11-21 跨库数据迁移 + 书签精简 + 减少冗余文件（v4.22）过程记录

## 任务概述

用户提出三点并给出决策：
1. 库内漫画复制/移动时，若目的地也是库，把**封面与进度**一起带过去；
2. 书签功能精简：删掉"上一个/下一个书签"（与阅读方向混淆），只保留「书签（当前页开关，带选中态高亮）」与「书签列表」两个按钮，列表内新增"清空书签"；
3. 减少程序输出的冗余文件：备份文件都不要（`.bak` 等），只做手动清理入口，不动日志策略。

## 过程简述

1. **减少备份输出**：`FolderCoverConfig` / `LibraryProgressConfig` 写入由 `File.Replace(..., .bak)` 改为 `File.Move(tmp, path, overwrite: true)`（单步原子替换，不产生 `.bak`）；坏 JSON 备份从 `*.broken-<时间戳>`（可堆积）改为单一 `*.broken`（覆盖式）。清掉现有 4 个历史备份文件（H 库根 3 个 + AppData legacy 备份 1 个）。
2. **跨库数据迁移**：
   - `LibraryStateService.CopyOrMoveBookData(src, dst, isCopy)`：按"键/键前缀"把进度与书签从源库搬到目标库（支持整文件夹），目标已有数据覆盖，库外书走 db；
   - `LibraryProgressConfig` 新增 `StateSnapshot` / `BookmarkSnapshot`；
   - `FileOps.CopyToAsync` 增加返回 `TargetPath`（与 `MoveToAsync` 对齐），复制后才能算目标键；
   - 主窗口 `CopyPathsToAsync/MovePathsToAsync` 成功后调用迁移；封面：`ApplyCoverAfterCopy`（新建）/`ApplyCoverAfterMove`（改造为返回跳过数）——封面图不在被移动目录内时**跳过并计数**，状态栏提示"未随迁"。
3. **书签精简**：
   - 删除 `NextBookmark`/`PrevBookmark` 动作、枚举项、默认快捷键（N/Shift+N）、设置页标签、输入分发 case；
   - 书签按钮双态：`IsCurrentPageBookmarked`（`CurrentPhysical` setter 与 `LoadBookmarks` 都会刷新）+ `BookmarkButtonText`（★ 书签）+ 主题色高亮（夜金/昼蓝）；XAML 用 `Classes="bookmarkState" Classes.on=...` 与样式；
   - 列表按钮改名「书签列表（N）」；面板新增「清空书签」（`ConfirmDialog` 二次确认 → `ClearAllBookmarks`）；
   - 存储层新增 `ILibraryStateStore.ClearBookmarks`（库内清书签保留空列表；db 回退删行）。
4. **手动清理入口**：`LibraryFileCleaner.CleanBackups`（删 `*.bak`、`*.bak-*`、`*.tmp`、`*.broken`、`legacy-covers-backup-*`，不动正式文件）→ 设置页「清理备份文件」按钮。
5. **验证**：新增 `CrossLibraryDataTests`（4 例）并更新 3 个受行为变更影响的旧用例，全量 **186/186 通过**。

## 遇到的问题（现象 → 原因 → 解决）

| 现象 | 原因 | 解决 |
|---|---|---|
| `CopyPathsToAsync` 编译失败（元组元素数不匹配） | `FileOps.CopyToAsync` 原本只返回 (Ok, Error)，无法得知实际目标路径 | 扩展为 (Ok, Error, TargetPath)，同步 `MoveToAsync` 内部调用与测试 |
| 文件夹迁移测试失败 | 测试先创建了同名**目录** `新系列/1.zip` 再 `File.Move` 文件 → 冲突 | 测试改为只建文件夹目录 |
| 3 个旧用例失败 | 断言"写入产生 .bak / broken-时间戳"（本轮刻意移除） | 更新断言：不产生 `.bak`；坏文件为单一 `.broken` |
| `ToolTip_Tip` 属性名写错 | Avalonia 附加属性不以 C# 属性名直接存在 | 去掉该行 |

## 最耗时项

跨库数据迁移的"键↔前缀映射 + 目标键计算 + 源清理 + db 回退"四路分支（≈150 行），以及封面"是否随目录移动"的判定与计数。

## 最耗 token 项

书签 UI 改动散布 5 个文件（XAML/code-behind/VM/SettingsStore/SettingsDialog），加上 `CopyToAsync` 签名变更牵连的调用点与测试。

## 规避与完善方案

- 迁移逻辑全部收在 `LibraryStateService.CopyOrMoveBookData`，App 只负责"操作成功后调用"；
- 封面跳过改为"计数 + 状态栏提示"，不再静默失败；
- 后续若需回滚能力，可考虑"写入前快照到 AppData 单文件滚动备份"（不污染库根），本次按用户要求不做。
