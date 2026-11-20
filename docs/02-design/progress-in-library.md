# 阅读进度 / 书签入库（v4.21）+ 书签功能重新上架

> 目标：与封面一致——**进度与书签随库走**（`<库根>\progress.json`），不再依赖本机 `reading.db`；
> 同时把被隐藏的书签功能重新上架（工具栏按钮 + 快捷键 + 书签面板）。

## 1. 文件与格式

```json
// <库根>\progress.json
{
  "version": 1,
  "books": {
    "作者A/书.zip": { "page": 37, "mode": "Double", "direction": "Rtl", "updatedAt": "2026-11-21T…Z" }
  },
  "bookmarks": {
    "作者A/书.zip": [ { "page": 5, "note": "chapter start", "createdAt": "…" } ]
  }
}
```

- 键 = 书相对库根的路径（`/` 分隔），与封面一致；
- `mode`：Single/Double/Webtoon；`direction`：Ltr/Rtl；`page` = 物理页（跨模式稳定）；
- 书签按页去重（同页重复添加 = 覆盖备注），删除后保留空列表（避免 merge-on-write 把旧值合并回来）。

## 2. 读写规则（与封面同构）

| 操作 | 规则 |
|---|---|
| **写** | 写**最具体**（最长匹配）库根的 `progress.json` |
| **读** | 从最具体库根往最外层**逐级回退**；库文件里"有该书记录"（哪怕已清空）即视为权威，不再回退 db |
| 不属于任何库的书 | 回退 `reading.db`（外部临时文件/库外路径） |
| 库根不可写 | 回退 db 并在状态栏提示一次"该库无法写入 progress.json（只读/权限）" |
| 坏 JSON | 备份 `progress.json.broken-<时间戳>` 后重建 |
| 原子写 | `.tmp` → `File.Replace`，旧内容留 `.bak`；写前与磁盘合并 |

## 3. 写入节流（关键）

翻页/滚动会高频调用 `SaveState`，不能每次写盘：

- 内存更新立即生效；距上次落盘 < **2 秒**时只写内存；
- 超过 2 秒 → 后台线程落盘（不阻塞翻页）；
- **关书**（`ReaderViewModel.DisposeAsync`）与**关窗**（主窗口 `Closed` / `ShutdownStateService`）强制 `Flush`；
- 主窗口另有 **3 秒定时器**兜底（`FlushStateIfDirty`）。

→ 异常退出最多丢最近 2~3 秒的页码（旧实现是每次翻页立即写 SQLite）。

## 4. 书签功能重新上架

之前三个书签按钮被 `IsVisible="False"` 隐藏、也没有快捷键。现在：

| 入口 | 行为 |
|---|---|
| 工具栏「书签」 | 当前页加/取消书签（快捷键 **B**） |
| 工具栏「◀书签」「书签▶」 | 上一个/下一个书签（**Shift+N** / **N**） |
| 工具栏「书签（N）」 | 打开/关闭**书签面板**（快捷键 **L**）：列出页码 + 备注，点击跳转，右侧 × 删除 |
| 设置 → 快捷键 | 新增 4 个动作，可自定义 |

书签与进度同存 `progress.json`（含备注字段），随库移动/改名。

## 5. 一次性迁移（reading.db → progress.json）

启动时 `LibraryStateService.MigrateFromDatabase`：

1. 读 db 的 `ReadingState` / `Bookmarks` 全量；
2. 存储键 → 实际书路径 → 所属库根 → 写入该库 `progress.json`（库里已有记录不覆盖）；
3. 落盘成功后删除 db 行；无法定位/库不可写 → **保留在 db**（读取时回退使用，不会丢）。

日志：`STATE_MIGRATE N 条 → progress.json（保留 M 条在 db）`。

## 6. 删除时清理（P1-1）

删除书/文件夹（回收站）后，同时清理所有候选库根里的：
- 封面键（`covers.json`）；
- 进度与书签键（`progress.json`，支持文件夹前缀）。

## 7. 封面配置健康检查（P1-4）

设置 → 数据管理：
- 「检查封面配置」：统计指向**已删除文件夹**或**已删除图片**的封面条目数（列前 5 条样例）；
- 「清理无效条目」：删除这些条目并落盘。

## 8. 代码入口

- `app/src/MangaAgent.Core/Storage/LibraryProgressConfig.cs`：`progress.json` 读写（含书签、坏文件备份、原子写）
- `app/src/MangaAgent.Core/Storage/ILibraryStateStore.cs`：存储抽象（ReaderViewModel 只依赖它）
- `app/src/MangaAgent.Core/Storage/LibraryStateService.cs`：多库路由 + db 回退 + 节流 + 迁移 + 清理
- `app/src/MangaAgent.Core/Storage/ReadingStateStore.cs`：实现接口（db 回退）+ 迁移查询/删除 API + `Flush()` 空实现
- `app/src/MangaAgent.App/ViewModels/ReaderViewModel.State.cs` / `.Nav.cs`：存储注入、书签命令与面板数据
- `app/src/MangaAgent.App/ViewModels/BookmarkItemViewModel.cs`：面板行（跳转/删除）
- `app/src/MangaAgent.App/Views/ReaderWindow.axaml` / `.Preview.cs` / `.Input.cs`：书签按钮、面板、快捷键动作
- `app/src/MangaAgent.App/Views/MainWindow.axaml.cs`：3 秒定时 flush + 关窗 flush/释放
- `app/src/MangaAgent.App/Views/SettingsDialog.cs`：快捷键动作表 + 封面配置检查
- 测试：`app/src/MangaAgent.Tests/Storage/LibraryProgressConfigTests.cs`、`PoC/ReaderBookmarkAndCleanupTests.cs`
