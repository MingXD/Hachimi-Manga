# 本地库与文件管理设计（v1 — 结合竞品调研）

> 参考：Komga（四层扫描模型、ComicInfo.xml、按页 hash 去重、导入 move/copy/hardlink）、Mihon（local 目录约定、JSON 备份+查看器）、YACReader（库自动备份/修复）、Calibre-Web（元数据管理/权限/OPDS 服务端）。详见 ../01-research/。

## 1. 目标
- 用户把本地漫画文件夹添加为资料库；软件扫描、展示、阅读、并在软件内整理文件
- 文件是真相源；数据库可重建

## 2. 库模型
```
LibraryRoot（可多个）
  └── Series（父目录名 或 ComicInfo Series 聚合）
        └── Volume（可选，文件名规则解析）
              └── Book（文件夹 / CBZ / CBR[V2] / PDF[V2]）
                    ├── Pages（惰性索引）
                    ├── ComicMetadata
                    └── ReadingState（进度/书签/单本设置）
```

## 3. 扫描：Komga 式四层模型
1. **扫描**：遍历库根，mtime 增量（首次全量）
2. **分析**：容器类型、图片格式、页尺寸、页数（为转码/缩略图决策收集事实）
3. **元数据刷新**：ComicInfo.xml / 文件名推断 / 封面提取
4. **索引写入**：SQLite 更新；缺失文件标记"丢失"（不自动删记录，用户确认后清理）
- FileSystemWatcher 增量 + 启动对账；事件合并节流；后台执行、可暂停、显示进度

## 4. 元数据
- **ComicInfo.xml**（ComicRack 标准，约 40 字段子集）：系列/卷/标题/作者/页类型/阅读方向(`<Manga>`)等；读写压缩包内或文件夹旁
- 文件名/目录推断：`[作者] 系列名 vol.01` 等规则 + 自然排序
- 封面：取 FrontCover 页或第 1 页 → 256px 缩略图缓存
- 在线刮削（AniList/MAL/Comic Vine）【待定，需网络与用户确认】
- 细节：库目录放 `.nomedia` 类标记防误入系统相册（Mihon 思路，Windows 上防缩略图索引）

## 5. 文件管理功能
| 功能 | 说明 | V1 |
|---|---|---|
| 库浏览 | 封面墙 + 文件树双视图 | ✅ |
| 导入 | 拖放 move/copy 入库；同名升级替换 | ✅ |
| 批量重命名 | 模板（系列/卷/话数） | ✅ |
| 自动整理 | 按规则建目录（作者/系列/卷）并移动 | ✅（干跑预览+可撤销） |
| 删除 | 移入回收站 | ✅ |
| 格式转换 | CBR→CBZ、图片重压缩（WebP）【待定】 | 后续 |
| 去重 | 文件 hash + 按页 hash 检测（Komga 亮点） | 后续 |
| 导出 | 书目清单 CSV/JSON | 后续 |

**可撤销原则**：所有整理操作 → 干跑预览 → 确认 → 操作日志 → 可撤销/回滚（YACReader 库修复思路：损坏/异常留 recovery 目录）

## 6. 缓存与备份
- 缓存：`%LOCALAPPDATA%\MangaAgent\cache`（缩略图/解码，可清理）
- 备份：库索引+进度定期导出 JSON（Mihon 思路）；库数据库自动备份 + 损坏检测修复（YACReader 思路）
- 备份格式可被外部工具查看（对标 Mihon Backup Viewer）【后续】

## 7. 为手机端预留
- 库扫描/元数据/进度做成可服务化的核心库；V2 可选：本地 HTTP + OPDS 暴露（Komga 模式），手机端薄客户端直接消费
- 多用户权限模型（Komga/Kavita 的 ACL/年龄限制）【仅未来服务端需要】
