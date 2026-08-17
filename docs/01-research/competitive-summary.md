# 竞品总览与结论汇总

> 汇总自三份子报告：`open-source-readers.md`（开源）、`commercial-ux.md`（商业/移动端 UX）、`tech-stack-eval.md`（技术选型）。原始资料在 `reference/research-raw/`。

## 1. 市场格局：Windows 漫画阅读器"缺一个现代化选手"

**移动端**（Android/iOS）生态极度繁荣：Mihon（Tachiyomi 继任，22.9k★）、Kotatsu、Panels、Chunky、Aidoku、Perfect Viewer 等，功能深度惊人。

**桌面端（尤其 Windows）** 呈现明显断层：
- 老牌强手：**YACReader**（Qt/C++，1.35k★，单人维护，持续迭代）、**CDisplayEx**（经典但年久）、**MangaMeeya**（2011 年停更）、**ComicRack**（停止活跃）
- Electron 系：**OpenComic**（格式面最广 + AI 图像增强）、**Houdoku**（开发放缓）
- 服务端向：**Kavita**（11.5k★，.NET+Angular）、**Komga**（6.6k★，Kotlin+Spring，MIT，生态最开放）
- **结论**：没有一个"Windows 原生质感 + 移动端级自定义深度 + 内置文件管理"的现代产品——这正是本项目的市场空位。

## 2. 竞品特性横评（我们应达到的基准线）

| 能力 | 行业标杆（谁做得最好） | 我们 V1 目标 |
|---|---|---|
| RTL 默认 + 方向可覆盖 | Mihon（默认 RTL，per-manga 覆盖）；Komga（元数据自动推断） | ✅ 全局→系列→单本三层 |
| 双页细节（封面单页/首尾页单页/横向页单页/中缝/offset） | Komga（首尾页与横向页强制单页）、Houdoku（offset 模拟实体书）、Panels（封面恒单页+手动分组） | ✅ 全套规则显式设计 |
| 跨页图处理 | Mihon（拆分宽页/旋转适配）、Komga（横向页单页） | ✅ |
| 连续滚动（Webtoon） | Mihon WebtoonViewer（滚动预加载、菜单自动隐藏） | ✅ |
| 缩放体系 | Mihon 六档 scale + 起始焦点 + 插值算法可选（OpenComic/YACReader Lanczos） | ✅ |
| 翻页动画可选/可关 | Mihon（传统+WebGPU 11 种 3D 动画）、Foliate（reduce-animation） | ✅ 无/淡入/滑动，可关 |
| 点击区预设 | Mihon（Edge/L形/Kindle/左右/禁用+反相） | ✅ |
| 夜间模式细节 | Mihon（软件亮度 -75~100、RGB 滤镜+混合模式）、Kavita（darkness level） | ✅ |
| 放大镜 | YACReader（矩形/圆形+描边）、OpenComic（浮动） | 后续 |
| E-ink/低刷屏适配 | Mihon（翻页闪烁） | 后续 |
| 进度/书签/续读 | 全行业标配；Komga↔Mihon 双向同步 | ✅ 本地版 |
| 库扫描/增量/元数据 | Komga（扫描→分析→元数据刷新四层；ComicInfo.xml；按页 hash 去重） | ✅ |
| 备份/迁移 | Mihon（JSON 备份+官方查看器）、YACReader（库自动备份/修复） | ✅ 导出/重建 |
| 格式面 | OpenComic（AVIF/JXL/JP2/7z/LZH/ACE…）、Kavita/Komga（RAR5/7z/PDF/EPUB） | V1：文件夹+CBZ；V2：CBR/7z/PDF |

## 3. 关键设计共识（三份报告的交叉结论）

1. **阅读引擎 = 核心护城河**：RTL/双页/宽页/滚动/缩放/动画/滤镜这些细节是用户感知最强的部分，值得做深。
2. **双页不是"两张图并排"那么简单**：封面单页、首尾页单页、横向页单页、中缝可调、offset 错位、按方向对齐（RTL 首单页靠左/末单页靠右）——这些规则要显式建模。
3. **方向与缩放必须正交**（KOReader 的锁定 bug 是反面教材）。
4. **设置分层**：全局→系列→单本→会话 + 命名预设（Panels 预设、Mihon per-manga、ComicRack Workspaces 都验证了这条路径）。
5. **库设计**："目录约定兜底（Mihon local 目录约定）+ ComicInfo.xml 元数据 + 索引缓存 + 增量扫描（Komga 四层模型）"。
6. **为手机端留接口**：YACReader（桌面+服务器+手机远程端）与 Komga（REST/OPDS+官方扩展+第三方客户端）证明"本地核心 + HTTP 服务 + 薄客户端"是覆盖手机端最稳路径——**V1 就该把库/阅读引擎做成可服务化的核心库**。
7. **许可证纪律**：核心代码 Apache-2.0/MIT；RAR 解码走 7-Zip 二进制或注意 unRAR 许可（MangaMeeya 因违反 GPL 停更是最著名警示）。
8. **可持续性**：Kotatsu 停运、Houdoku 放缓是单人开源项目的前车之鉴；个人项目要控制范围、留好扩展点。

## 4. 技术选型结论（详见 tech-stack-eval.md）

- **主方案：Avalonia 11（C#/.NET）**——Skia 渲染满足自定义阅读器；SharpCompress（ZIP/RAR/7z）、SkiaSharp、PdfiumViewer、ComicMeta、SQLite、FileSystemWatcher 全套干净许可证库；核心逻辑做成纯 .NET 类库，未来可 MAUI 复用或服务端化。
- **备选：Flutter**——若移动端优先级提前（pdfrx PDFium 六平台现成；但 Windows 系统集成弱、RAR 弱）。
- **纯 Windows 路线：WinUI 3**——若放弃移动端；系统集成最佳，但要接受框架波动。
- **不建议**：MAUI 桌面（坑多）、Tauri 2（移动端预览期+渲染链路长）、Qt（许可证/成本）、Compose（Windows JVM、iOS 生态弱）。

## 5. 机会点（差异化卖点候选）
1. Windows 原生质感 + 移动端级自定义深度（市场空位）
2. 内置文件管理（对标 Calibre 的整理能力，多数阅读器没有）
3. 高质量渲染（Lanczos 插值、平滑缩放、翻页动画）——老牌 Windows 阅读器普遍粗糙
4. 面向未来：核心库服务化 + OPDS/轻量 API，V2 手机端零成本接入
5. 开源许可干净（Apache-2.0/MIT），无 GPL 传染、无在线源法律风险
