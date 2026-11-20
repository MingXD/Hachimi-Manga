# 库内封面配置文件（v4.20 第一步）

> 目标：把"文件夹自定义封面"从用户目录的 `reading.db` 搬到**库自身**的文件里，让封面随库走——
> 库改名、整库移动/拷贝到别的盘、换机器都不用重新设置或"重新关联"。

## 1. 文件位置与格式

每个库根下一份（可见文件，可直接手改）：

```
<库根>\covers.json
```

```json
{
  "version": 1,
  "covers": {
    ".": "cover.png",
    "zhongzi": "cover.png",
    "zhongzi/nemuke？ (似せ)": "001.jpg",
    "[个人汉化] [暗稿] チンポマン [DL版]": "../_shared/folder-cover.jpg"
  }
}
```

- **键** = 文件夹相对库根的路径，分隔符统一 `/`；库根自身用 `"."`；
- **值** = 封面图片相对库根的路径（可指向库内任意位置，因此多个文件夹能共用一张封面）；
- 顶层其它字段写入时会**原样保留**（只覆盖 `version` / `covers`）。

## 2. 校验规则

- 封面必须位于**该库根内**（`..` 越界一律拒绝；错误文案会说明原因）；
- 必须是图片：`png/jpg/jpeg/webp/gif/bmp/avif`；
- 文件必须存在（挂载盘离线时该条目视为"无封面"，不会报错）。

与旧 schema 的差别：旧规则要求"封面必须在该文件夹内"，新规则放宽到"库内任意位置"。

## 3. 读写时机

| 场景 | 行为 |
|---|---|
| 进入目录 / 重渲染 | `ResolveFor(文件夹列表)` 批量解析（一次读缓存，命中"文件不存在"的条目自动跳过） |
| 设置 / 清除封面 | 改内存 → `Flush()` 原子落盘 |
| 外部手改 `covers.json` | 文件指纹（`LastWriteTimeUtc + 长度`）变化 → 下次读取自动重读（配合 v4.19 的窗口激活/进入目录刷新） |
| 移动文件夹 | 同库内：改键（封面若在源目录内，值的前缀随之替换）；跨库：新库重建 + 老库删键；移出库：老库删键 |

## 4. 写入安全

- **原子写**：先写 `covers.json.tmp` → `File.Replace` 覆盖，旧内容留 `covers.json.bak`；
- **merge-on-write**：写前重读磁盘最新内容，只覆盖本进程改过的键，删除优先（多实例/多机同时改不易互相吞记录）；
- **坏 JSON 不覆盖**：解析失败时把原文件改名为 `covers.json.broken-<yyyyMMdd-HHmmss>` 备份，再从空表重建并提示；
- **库不可写**（只读盘/网络盘无权限）：`Flush()` 返回 false → 回退写 `reading.db` 并在状态栏提示一次"该库无法写入 covers.json，封面已暂存到本地"。

## 5. 旧数据迁移（一次性、幂等）

启动 / 添加库后调用 `FolderCoverConfig.MigrateFromDatabase(StateDbPath)`：

1. 读 `reading.db` 的 `FolderCovers` 全部记录（键可能是库根相对或历史绝对路径）；
2. 按键/绝对路径定位实际文件夹 → 找所属库根（最长匹配）；
3. 写入该库 `covers.json` → 落盘成功后**从 db 删除该条**；
4. 路径不属于任何库、或库不可写 → 保留在 db，界面显示时作为回退数据（读取时 `covers.json` 优先）。

⚠️ 迁移后旧版本软件看不到这些封面（db 已清空）；需要保留旧版本可用时，先到设置页导出一份 JSON。

## 6. 导出 / 导入（设置页"数据管理"）

- **导出**：`reading.db` 记录 + 各库 `covers.json` 汇总成一份 JSON（文件名仍为 `HachimiManga-data.json`）；
  跨目录引用的封面（值含 `..`）无法用旧导出格式表达，会被跳过（数量会提示）。
- **导入**：写入 db → 随即触发迁移把封面搬进各库 `covers.json`；进度/书签仍在 db（第二步处理）。
- 设置页执行前会自动从 `libraries.txt` 载入库根，保证相对路径解析正确。

## 7. 限制与后续

- **阅读进度 / 书签仍在 `reading.db`**：第二步（v4.20b）再搬到库内 `progress.json`，需要节流与原子写；
- 删除文件夹时不会主动清理其封面键（无害：读取时文件夹不存在即不显示），如需干净可在"清理"功能里顺手做；
- 不监听 `covers.json` 变化事件（只按 mtime 在读取时校验），够用且零开销。

## 8. 代码入口

- `app/src/MangaAgent.Core/Storage/FolderCoverConfig.cs`：读写/校验/缓存/原子写/迁移/导出汇总
- `app/src/MangaAgent.Core/Storage/ReadingStateStore.cs`：`LoadAllFolderCovers` / `DeleteFolderCoverByStorageKey` / `TryLoadLibraryRootsFromFile`（迁移与设置页支撑）
- `app/src/MangaAgent.App/ViewModels/MainWindowViewModel.cs`：`ApplyFolderCovers` / `CoverConfigFor` / `SetFolderCoverAsync` / `ClearFolderCover` / `ApplyCoverAfterMove` / `MigrateCoversFromDatabase`
- `app/src/MangaAgent.App/ViewModels/FolderEntryItemViewModel.cs`：`SetFolderCoverPath`（绝对路径注入统一入口）
- `app/src/MangaAgent.App/Views/SettingsDialog.cs`：导出/导入适配
- 测试：`app/src/MangaAgent.Tests/Storage/CoverConfigTests.cs`、`app/src/MangaAgent.Tests/PoC/LibraryCoverTests.cs`
