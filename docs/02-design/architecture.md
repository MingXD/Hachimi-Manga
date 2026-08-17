# 架构设计（v1 — 结合调研成果）

> 前置：技术选型已定稿 **Avalonia 11（C#/.NET）**（见 ../01-research/tech-stack-eval.md 与 ../02-design/decision-log.md D1）。架构原则与模块划分保持框架无关，按该方案落地。

## 1. 设计原则
1. **本地优先（Local-first）**：所有数据在用户磁盘；无账号、无在线源；数据格式开放可迁移。
2. **阅读引擎与 UI 解耦 + 核心库可服务化**：领域层做成不引用 UI 的纯 .NET 类库（对标 Komga 的"服务器只存事实、一切走 API"与 YACReader 的"桌面+服务器+手机远程端"分层）。V1 虽只做单机，但**为未来手机端保留"本地 HTTP 服务 + 轻量 API/OPDS"的接入点**。
3. **文件系统是真相源，数据库是索引**：库索引、元数据、进度都可从文件重建；数据库可删。
4. **设置四层 + 命名预设**：全局默认 → 系列级 → 单本 → 会话；用户可保存/分享命名预设（Panels/Mihon/ComicRack Workspaces 均验证此模式）。
5. **阅读器可插拔（Viewer 抽象）**：单页/双页/连续滚动是不同 Viewer 实现，共享同一分页模型与渲染后端（Mihon Pager/Webtoon/WebGPU 的架构思路）。
6. **性能预算**：5000 页 CBZ 随机翻页 <100ms、内存上限可配置、缩略图缓存、扫描后台化。

## 2. 逻辑分层
```
┌────────────────────────────────────────────────┐
│ UI 层（Avalonia）：库界面 / 阅读器界面 / 设置界面      │
├────────────────────────────────────────────────┤
│ 应用服务层                                       │
│  · LibraryService（扫描/索引/系列聚合/元数据）        │
│  · ReadingSessionService（打开书、状态、书签）        │
│  · FileManagerService（导入/重命名/整理/转换/回收站）  │
│  · SettingsService（四层设置 + 预设）               │
│  · [未来] LocalServerService（HTTP/OPDS 端口）      │
├────────────────────────────────────────────────┤
│ 领域层（纯 .NET 类库，不引用 UI）                    │
│  · Core.Archives：ComicSource 抽象 + ZIP/CBZ/       │
│    RAR/CBR/文件夹 实现（SharpCompress）             │
│  · Core.Imaging：解码、降采样、滤镜、插值、缩略图      │
│    （SkiaSharp / ImageSharp，AVIF 待评估）           │
│  · Core.Paging：分页模型、双页规则、跨页识别、          │
│    方向、offset（纯函数，可单测）                     │
│  · Core.Viewers：Pager / DoublePage / Webtoon        │
│  · Core.Metadata：ComicInfo.xml 读写、文件名推断       │
│  · Core.Storage：SQLite（库/进度/设置）、缓存管理       │
├────────────────────────────────────────────────┤
│ 基础设施：FileSystemWatcher、线程池、日志、崩溃恢复      │
└────────────────────────────────────────────────┘
```

## 3. 关键抽象
### 3.1 ComicSource（归档/来源统一接口）
```
interface ComicSource {
  pageCount(): int
  page(i): Task<DecodedPage>          // 随机访问、懒解码
  pageSize(i): Size
  metadata(): ComicMetadata
  thumbnail(i): Task<Bitmap>
  name/path/size/modifiedAt
}
```
实现：FolderSource（自然排序图片序列）、ZipSource（内置 ZipArchive 随机读）、RarSource（SharpCompress/7-Zip，V2）、PdfSource（PdfiumViewer，V2）。

### 3.2 分页模型
- PhysicalPage（归档内一张图）→ LogicalPage（用户看到的一页，可为双页合成）
- 双页规则由 `PageLayouter` 计算：封面单页 → 成对 → 横向页单页 → 首尾页单页 → offset
- 方向（RTL/LTR）只影响推进方向与对齐（RTL 首单页靠左、末单页靠右——Cover/Komga 规则）

### 3.3 设置模型
```
GlobalDefaults > SeriesOverrides > BookOverrides > SessionTweaks
PresetManager: 命名预设 = 一组设置值的具名快照（可应用/保存/导出）
```

### 3.4 阅读状态机与持久化
状态：书 ID + 逻辑页 + 双页偏移 + 缩放/平移 + 方向 + 模式 + 主题
持久化：reading.db（防抖落盘 + 退出强写）；书签列表；完成标记

## 4. 数据存储
- `library.db`（SQLite）：库根、系列、书、路径索引、元数据、标签、封面路径、页哈希（去重）
- `reading.db`（SQLite）：进度、书签（按 路径+修改时间 定位，文件移动后可重定位）
- `settings`（JSON）+ 预设文件（可导出分享）
- 缓存：缩略图 / 解码缓存（`%LOCALAPPDATA%\MangaAgent\cache`，可清理）
- 备份：库与进度定期导出 JSON（Mihon 备份思路 + YACReader 自动备份/修复思路）

## 5. 线程与性能
- UI 线程只渲染与手势；解码/解压/扫描在后台线程池
- 翻页预解码 ±N 页（可配置）；解码结果三档缓存（原始/屏幕级/缩略图）
- 扫描：Komga 式四层模型——`扫描(mtime 增量) → 分析(容器/格式/页尺寸) → 元数据刷新 → 索引写入`，全后台、可暂停

## 6. 未来移动端预留（混合路线）
1. 领域层纯 .NET 类库 → 移动端可 MAUI 复用（UI 重写、逻辑保留）
2. 或 LocalServerService（HTTP + OPDS）→ 手机端薄客户端（Komga/Kavita 模式）
3. 决策窗口见 roadmap；若移动端提前至 12 个月内则切换 Flutter 主方案

## 7. 风险清单
| 风险 | 缓解 |
|---|---|
| Avalonia 移动端 experimental | 领域层解耦；移动端走 MAUI 复用或服务端模式 |
| RAR 解码许可/兼容 | SharpCompress 只读实现；备选 7-Zip 二进制（注意 LGPL）；V2 再启用 |
| AVIF/JXL 解码 | ImageSharp v3（WebP）起步；AVIF 评估插件/原生库 |
| 大图内存 | 三档缓存 + 降采样 + 内存上限 |
| 单人维护可持续性 | 范围控制、扩展点、文档化（Kotatsu/Houdoku 前车之鉴） |
