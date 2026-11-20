# 库内封面配置（v4.20，最终形态）

> **唯一存储**：文件夹自定义封面保存在**资料库目录**下的 `covers.json`，随库移动/改名。
> `reading.db` 里不再有封面表（历史表 `FolderCovers` 会在首次启动时备份成 JSON 后删除）。
> 没有迁移功能、没有导出/导入功能——**备份 = 复制资料库目录**。

## 1. 文件位置与格式

```
<资料库根>\covers.json
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
- **值** = 封面图片相对库根的路径（可指向库内任意位置，多个文件夹可共用同一张图）；
- 顶层其它字段写回时**原样保留**（只覆盖 `version` / `covers`）。

## 2. 校验

- 封面必须位于**该库根内**（`..` 越界拒绝，错误文案说明原因）；
- 图片格式：`png/jpg/jpeg/webp/gif/bmp/avif`；文件必须存在；
- 与旧 schema 的差别：旧规则要求"封面必须在该文件夹内"，现在放宽到"库内任意位置"。

## 3. 规则（一句话记住三条）

| 操作 | 规则 |
|---|---|
| **写** | 写到**最具体**（最长匹配）库根的 `covers.json` |
| **读** | 从最具体库根往最外层**逐级回退**，谁有键用谁（嵌套库根场景：`bz\25.08` 的封面写在 `bz\covers.json` 里也能读到） |
| **清** | 清除时**所有候选库根的同键都删掉**（避免父库根残留导致"清了又回来"） |

嵌套库根（如同时把 `F:\额外储存\bz` 和 `F:\额外储存\bz\25.08` 加为库）时，这三条规则保证行为可预测；但**产品建议仍是"只加最外层库根"**，语义最简单。

## 4. 读写时机与安全

| 场景 | 行为 |
|---|---|
| 进入目录 / 重渲染 | 按候选库根逐级查找（内存缓存 + 文件指纹，命中即用） |
| 设置 / 清除封面 | 改内存 → `Flush()` 原子落盘 |
| 外部手改 `covers.json` | 文件指纹（`LastWriteTimeUtc + 长度`）变化 → 下次读取自动重读（窗口激活/进入目录时生效） |
| 移动文件夹 | 移动前快照 → 移动后：目标端写最具体库根、源端清全部候选库根（封面随目录移动时值前缀随之替换） |
| 库根不可写 | `Flush()` 失败 → 状态栏提示"该库无法写入 covers.json（只读/权限）"，**不再静默回退 db** |
| 坏 JSON | 原文件改名 `covers.json.broken-<yyyyMMdd-HHmmss>` 备份后从空表重建（绝不覆盖手写内容） |
| 原子写 | 先写 `covers.json.tmp` → `File.Replace` 覆盖，旧内容留 `covers.json.bak`；写前与磁盘内容合并（merge-on-write，删除优先） |

## 5. 历史数据的最终处理

启动/添加库时调用一次 `ReadingStateStore.BackupLegacyFolderCovers(StateDbPath, AppData)`：

1. 若 `reading.db` 里还存在 `FolderCovers` 表且有记录 → 导出为 `%LOCALAPPDATA%\HachimiManga\legacy-covers-backup-<yyyyMMdd-HHmmss>.json`；
2. 然后 `DROP TABLE FolderCovers`（幂等，之后该表不再存在，代码也不再创建）；
3. 日志记录备份路径（`LEGACY_COVER_BACKUP`）。

⚠️ 备份只是**留痕**，不会自动恢复。需要找回归属时按备份里的 `Folder`/`Image` 手工写进对应库的 `covers.json`。

## 6. 阅读进度 / 书签（后续）

仍在 `%LOCALAPPDATA%\HachimiManga\reading.db`（`ReadingState` / `Bookmarks` 两张表）。第二步（v4.20b）计划搬到库内 `progress.json`，届时备份同样是"复制库目录"。

## 7. 代码入口

- `app/src/MangaAgent.Core/Storage/FolderCoverConfig.cs`：读写/校验/缓存/原子写/多级库根解析（`ResolveFor`/`GetCoverFullPath`/`TrySet`/`TrySetByRel`/`TryClear`/`MoveKey`/`Flush`）
- `app/src/MangaAgent.Core/Storage/ReadingStateStore.cs`：阅读进度/书签 + `BackupLegacyFolderCovers`（删除历史封面表）
- `app/src/MangaAgent.App/ViewModels/MainWindowViewModel.cs`：`CandidateRoots`（长→短）、`ApplyFolderCovers`（逐级回退）、`SetFolderCoverAsync`（写最具体）、`ClearFolderCover`（清全部候选）、`ApplyCoverAfterMove`
- `app/src/MangaAgent.App/ViewModels/FolderEntryItemViewModel.cs`：`SetFolderCoverPath`（唯一注入入口）
- `app/src/MangaAgent.App/Views/SettingsDialog.cs`：数据管理页仅说明存储位置（无导出/导入）
- 测试：`app/src/MangaAgent.Tests/Storage/CoverConfigTests.cs`、`app/src/MangaAgent.Tests/PoC/LibraryCoverTests.cs`
