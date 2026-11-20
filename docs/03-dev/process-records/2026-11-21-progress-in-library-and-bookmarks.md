# 2026-11-21 P0/P1 收尾：进度书签入库 + 书签上架 + 删除清键 + 封面检查（v4.21）过程记录

## 任务概述

用户要求把此前排定的 P0/P1 除 CBZ 相关项外全部做完：
P0-1 书签重新上架、P0-2 阅读进度/书签搬进库内 `progress.json`（含旧数据一次性迁移）、P1-1 删除时清理封面键、P1-4 封面配置健康检查。
（P0-3 失效库重新关联已在上一轮完成；P1-2/P1-3 属 CBZ/PDF 显示优化，用户明确暂不做。）

## 过程简述

1. **Core 存储层**：
   - 新增 `LibraryProgressConfig`（`<库根>\progress.json`：books 状态 + bookmarks 列表；指纹热加载、原子写、`.bak`、坏 JSON 备份、merge-on-write）；
   - 新增 `ILibraryStateStore` 抽象 + `LibraryStateService`（多库路由：写最具体库根、读逐级回退、库外/只读回退 db、2 秒节流 + 后台落盘、`MigrateFromDatabase` 一次性迁移、`ClearBook` 清理）；
   - `ReadingStateStore` 实现接口（`Flush()` 空实现）、补 `LoadBookmarkEntries` / `LoadAllStates` / `LoadAllBookmarks` / `Delete*ByStorageKey`。
2. **阅读器**：
   - `ReaderViewModel` 改为注入 `ILibraryStateStore`（保留 `(book, dbPath)` 便捷构造给测试）；关书时 `Flush`；
   - 书签：`LoadBookmarks` 同时生成 `BookmarkItems`（页码+备注）；新增 `ToggleBookmarkPanel` / `GoToBookmarkPage` / `RemoveBookmarkAt` / `HasBookmarks` / `BookmarkSummary`；
   - UI：三个书签按钮去掉 `IsVisible=False`、新增「书签（N）」面板（右侧覆盖层：条目跳转 + × 删除）、快捷键 B/N/Shift+N/L，设置页快捷键表与中文标签同步。
3. **主窗口**：持有共享 `LibraryStateService` 并注入阅读器；3 秒定时 `FlushStateIfDirty`；关窗 `ShutdownStateService`；库根同步时 `InvalidateCache`；`RefreshLibraryRootsAndMigrate` 末尾执行一次性迁移；`DeletePaths` 删除后调用 `CleanStoredKeysFor`（清封面 + 进度键）。
4. **设置页**：数据管理卡片改为说明 + 「检查封面配置 / 清理无效条目」（`FolderCoverConfig.Inspect` / `RemoveInvalid`）。
5. **验证**：新增 `LibraryProgressConfigTests`（7 例）与 `ReaderBookmarkAndCleanupTests`（3 例），全量 **182/182 通过**。

## 遇到的问题（现象 → 原因 → 解决）

| 现象 | 原因 | 解决 |
|---|---|---|
| 删除书签后重读又出现 | merge-on-write 里"书签被删"没有 tombstone，合并时磁盘旧值被写回 | `RemoveBookmark` 保留**空列表**（用空值覆盖磁盘）；`ClearBook` 走 `_removedBooks` 整键删除 |
| 清空书签后旧 db 记录"复活" | `LoadBookmarkEntries` 只认"非空列表"，空结果回退 db | 新增 `ContainsBookKey/ContainsStateKey`：库文件**有该书记录**（哪怕为空）即权威，不回退 db |
| `Padding = "8,3"` 编译失败 | Avalonia 属性是 `Thickness` 类型 | 改 `new Thickness(8, 3)` |
| 书签面板"关闭"无效风险 | code-behind 直接改控件 `IsVisible` 与绑定冲突 | 只改 VM 属性，`IsVisible` 交给绑定 |
| `GoToBookmarkPage` 不可访问 | `BookmarkItemViewModel` 调用 private 方法 | 改为 `public` |

## 最耗时项

进度写入的节流/合并语义（tombstone、空列表覆盖、库文件与 db 的权威判定），以及阅读器构造签名变更带来的调用点与测试适配。

## 最耗 token 项

新增 `LibraryProgressConfig` + `LibraryStateService` 两个完整存储类（≈700 行），以及书签 UI（XAML 面板 + 快捷键动作表 + 设置页标签）的多文件同步修改。

## 规避与完善方案

- 存储抽象收敛到 `ILibraryStateStore`，将来若换存储（如 SQLite 分库）只需换实现；
- 所有"库内配置文件"（covers/progress）共用同一套设计语言（最具体写、逐级读、原子写、坏文件备份、只读回退），降低心智负担；
- 待办（明确不做/延后）：CBZ 页数按可见项补全、ComicInfo 标题后台补全（用户当前只用文件夹形式）；书签备注编辑 UI。
