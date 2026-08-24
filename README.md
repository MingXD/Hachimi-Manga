# Hachimi Manga — 本地漫画阅读器

> **Hachimi Manga**（工程代号 *MangaAgent*）——Windows 优先、面向日漫（RTL）阅读的高度可定制本地漫画阅读器，自带本地库与文件管理。
> 状态：**已实施并发布**（内部版本 v5.2 + code-audit，对外发布 v1.0，2026-08-24）。自动化测试 **67/67 通过**。
> 数据全在本机（`%LOCALAPPDATA%\HachimiManga`），程序**不联网、不上传**。

---

## 目录

- [功能总览](#功能总览)
- [技术栈与环境要求](#技术栈与环境要求)
- [快速开始](#快速开始)
- [项目结构](#项目结构)
- [架构与代码地图](#架构与代码地图)
- [文档导航](#文档导航)
- [新开发者 / 新 Agent 接手指南](#新开发者--新-agent-接手指南)
- [开发工作流与规范](#开发工作流与规范)
- [版本历史](#版本历史)
- [数据与隐私](#数据与隐私)
- [常见问题与坑位](#常见问题与坑位)

---

## 功能总览

### 📖 阅读器（Reader）

| 功能 | 说明 |
|---|---|
| 画布模型交互 | 滚轮以鼠标为焦点精细缩放（2%–800%）、拖拽平移（可拖出边界/空白不拖）、双击放大/复位、←→ 翻页、复位 |
| 三种阅读模式 | 单页 / 双页（RTL 右页在前、严密贴合、配对起点可选）/ Webtoon（上下无缝拼接、平滑滚动+惯性、Ctrl+滚轮缩放） |
| 物理页导航 | 单页逐物理页推进；进度/书签存物理页，跨模式稳定；页码显示（单页=物理页号，双页=逻辑页号） |
| 缩放控制 | −/+ 按钮（1.4 倍）、缩放灵敏度（快/中/慢）、适应模式循环（宽度/高度/屏幕）、百分比显示、缩放×N 按钮 |
| 预览栏 | 右侧悬浮覆盖层（不占布局、不推挤图片）；右缘 48px 唤出；竖排缩略图（动态张数 1~11、页码在卡片右侧、当前页高亮）；▲▼ 整排跳、滚轮逐页、点击跳转；Webtoon 双向联动 |
| 主题 | 白天/夜间一键切换（T 键），工具栏/预览栏配色联动 |
| 书签 | 当前页书签 / 上一 / 下一书签（快捷键 B；工具栏按钮 v2.3 起暂时隐藏，命令保留） |
| 调试 | F12 调试窗口（状态 + 最近事件流）、按日轮转日志、DebugLog 开关（默认关） |

### 📚 本地库与文件管理（Library）

| 功能 | 说明 |
|---|---|
| 资料库 | 添加/扫描（进度提示、坏文件容错）、"已在库中"自动切换、重新扫描、启动恢复 |
| 目录树 | 文件夹层级树 + 面包屑导航 + 混合展示（文件夹 + 漫画封面） |
| 封面墙 | 展示切换：大 / 中 / 小 / 详情（详情为文件管理器式行列表） |
| 搜索 | 右上角全库搜索（命中文件夹 + 漫画，按类型+名称排序） |
| 文件操作 | 右键：打开 / 重命名 / 复制 / 移动 / 删除（回收站）/ 在资源管理器中显示 / 属性 |
| 格式 | ZIP/CBZ 漫画包、纯图片文件夹自动识别为条目 |

### ⚙️ 设置系统（v4–v5.2）

- 三页签设置窗口：**阅读**（基准/手感/性能滑块+开关+下拉）/ **快捷键**（三模式重绑）/ **使用介绍**（usage.md 轻量渲染）
- `settings.json` 持久化于 `%LOCALAPPDATA%\HachimiManga`，用户可直接手动编辑
- 快捷键四组分类（通用/分页左到右/分页右到左/滚动），一方块一键/组合键（Ctrl+Shift+P）、点击捕获/右键删除/[+]新增、冲突检测
- 默认阅读模式（Single/Double/Webtoon）、恢复上次位置（续读）、双击行为、默认适应模式
- 速度基准 0.2~4.0 + ×0.5/1/2 快速档位、滚动方向反转、分页滚轮行为（缩放/翻页）
- 使用介绍 `guide\usage.md`（exe 旁优先，否则 `%LOCALAPPDATA%`），随包分发、可编辑

### 📦 发布

- `tools\pack-release.ps1` 一键打包绿色版（self-contained，目标机无需安装 .NET）
- 产物：`app\dist\HachimiManga-<ver>-win-x64.zip`（含 `guide\`），可上传 GitHub Releases 分发
- 完整发布流程：`docs/03-dev/release.md`

---

## 技术栈与环境要求

| 项 | 值 |
|---|---|
| 语言 / 框架 | C# / .NET 9（SDK 9.0.317，运行时 9.0.19） |
| UI 框架 | Avalonia 12.1.1（Skia / Win32） |
| MVVM | CommunityToolkit.Mvvm 8.4.2 |
| 测试 | xUnit（headless Avalonia） |
| 存储 | SQLite（资料库索引 / 阅读状态） |
| 系统 | Windows 10+（开发机实测） |
| 构建环境 | PowerShell 5.1（注意编码坑，见 FAQ） |
| 程序名 | `HachimiManga.exe` / `HachimiManga.Core.dll`（内部命名空间仍为 `MangaAgent.*`） |
| 数据目录 | `%LOCALAPPDATA%\HachimiManga`（settings.json / reading.db / logs / guide） |

---

## 快速开始

```powershell
# 克隆（或使用本机已有仓库 D:\MangaAgent）
git clone https://github.com/MingXD/Hachimi-Manga.git
cd Hachimi-Manga

# 构建（Debug）
& "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe" build app\src\MangaAgent.App -m:1 -p:UseSharedCompilation=false

# 全量测试
& "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe" test app\src\MangaAgent.Tests -m:1 -p:UseSharedCompilation=false

# 运行（dev 构建）
powershell -ExecutionPolicy Bypass -File tools\run.ps1

# 发布（Release 自包含 → app\publish\HachimiManga.exe）
& "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe" publish app\src\MangaAgent.App -c Release -r win-x64 --self-contained -p:PublishSingleFile=false -m:1 -p:UseSharedCompilation=false

# 打包绿色版 zip（publish + robocopy + zip 一步完成 → app\dist\）
powershell -NoProfile -ExecutionPolicy Bypass -File tools\pack-release.ps1
```

> 发布参数原理 / 环境坑位 / 验证清单见 `docs/03-dev/release.md`；环境搭建见 `docs/03-dev/setup.md`。

---

## 项目结构

```
D:\MangaAgent\
├─ app\
│  ├─ src\MangaAgent.Core\     # 领域层（无 UI）：Library / Comics / Paging / Storage
│  ├─ src\MangaAgent.App\      # UI 层（Avalonia）：Views / ViewModels / Services
│  ├─ src\MangaAgent.Tests\    # xUnit 测试（headless Avalonia）
│  ├─ guide\                   # 分发用使用介绍（usage.md），打包时并入 zip
│  ├─ MangaAgent.sln
│  └─ publish\ / dist\          # 可再生成发布产物（不入库，已被 .gitignore 排除）
├─ docs\
│  ├─ 00-overview.md           # 项目总览与文档导航（入口）
│  ├─ 01-research\             # 竞品与生态调研
│  ├─ 02-design\               # 方案设计（需求/架构/阅读引擎/设置系统等）
│  └─ 03-dev\                  # 开发维护（CODE-MAP/约定/发布流程/变更记录/Bug 跟踪）
├─ tools\                      # 开发辅助脚本
│  ├─ pack-release.ps1         # 一键打包绿色版 zip
│  ├─ run.ps1                  # 运行 dev 构建
│  ├─ make-fixtures.ps1        # 生成测试占位书（纯色块+页码）
│  └─ fix-arrow-lines.ps1 / split-readerwindow.ps1 / test-regex.ps1
├─ reference\
│  ├─ fixtures\                # 测试夹具（CBZ / 文件夹书 / 压力书，均为占位内容）
│  ├─ links\  research-raw\    # 调研存档
├─ .gitignore                  # 已排除：bin/obj/dist/publish/test-file/reference/screenshots 等
└─ README.md
```

---

## 架构与代码地图

三层结构：**Core（领域，无 UI）→ App（UI，Avalonia + MVVM）→ Tests（xUnit）**。

- 阅读器核心交互集中在 `ReaderWindow`（拆分为 Canvas / Webtoon / Preview / Input / Dialog 五个 partial）+ `ReaderViewModel`（拆为 Decode / Nav / State 三个 partial）
- 每个文件的职责、常见修改场景、定位关键词 → `docs/03-dev/CODE-MAP.md`
- 架构决策记录 → `docs/02-design/architecture.md`、`decision-log.md`

---

## 文档导航

| 文档 | 内容 |
|---|---|
| `docs/00-overview.md` | 项目总览 + 全部文档索引（**先读这个**） |
| `docs/01-research/` | 调研：competitive-summary / open-source-readers / commercial-ux / tech-stack-eval |
| `docs/02-design/` | 设计：requirements / architecture / reading-engine / library-file-management / ui-ux-design / settings-system / settings-dialog-v2 / roadmap 等 |
| `docs/03-dev/` | 开发维护：README(索引) / CODE-MAP / conventions / setup / release / **v2-changes-record（v2.0→v5.2 全记录）** / v1.9-changes-record / bugs\ |
| `reference/` | 调研存档（links / research-raw）+ 测试夹具（fixtures） |

---

## 新开发者 / 新 Agent 接手指南

1. **先读**：本 README → `docs/00-overview.md` → `docs/03-dev/CODE-MAP.md`；发布前必读 `docs/03-dev/release.md`
2. **改代码前**：查阅 `docs/02-design/` 对应方案 + `docs/03-dev/v2-changes-record.md` 了解历史决策与踩坑（避免重蹈覆辙）
3. **环境**：dotnet 在 `%LOCALAPPDATA%\Microsoft\dotnet`（**不在 PATH**，build 用完整路径）；文件 CRLF；Avalonia 12 API 有坑（见 release.md「四、环境坑位」）
4. **工作流**：改代码 → 加/改测试 → 全量测试（67/67 基线）→ 提交（`feat/fix/chore` 前缀 + 中文说明 + 测试数）→ 必要时发布（release.md）
5. **Bug 处理**：新建 `docs/03-dev/bugs/BUG-xxx.md` 跟踪，修完归档 ✅
6. **隐私红线**：`test-file/`（真实书）、`reference/screenshots/`、`dist/`、`publish/`、`bin/`、`obj/` 均被 `.gitignore` 排除，**不得取消忽略或提交个人/版权数据**；新增敏感目录先加进 `.gitignore`

---

## 开发工作流与规范

- git 三连：`git add .` → `git commit -m "..."` → `git push`（`main` 已绑定 `origin`，push 无需参数）
- 提交信息风格：`类型(范围): 中文描述；测试数`（例：`feat(v5.2): 设置扩展 + 阅读页布局；65/65 通过`）
- 代码规范：`docs/03-dev/conventions.md`（dotnet format 统一：4 空格/CRLF/UTF-8 去 BOM）
- 测试要求：改阅读器 / 库 / 设置逻辑必须带回归测试（xUnit，headless Avalonia）

---

## 版本历史

| 版本 | 内容 |
|---|---|
| **v1.0 发布**（2026-08-24） | 对外命名 HachimiManga（exe/dll/进程名/清单全换名）+ 绿色版打包（pack-release.ps1）+ guide 重写；67/67 |
| **code-audit**（2026-11） | P0-P2：DisposeAsync 守卫修复（句柄/位图泄漏根治）、partial 拆分、LoadSizes 异步化、日志门控；67/67 |
| **v5.2**（2026-08-23） | 设置扩展（默认阅读模式/恢复上次位置/双击行为/默认适应模式）+ 速度基准 0.2~4.0 + 阅读页布局两行标签；65/65 |
| v5.1 | 设置 UI 优化 + exe 旁 guide 优先；64/64 |
| v5 | 设置窗口 v2：快捷键四组分类、一方块一键/组合键、冲突检测、滑块数值框双向联动；64/64 |
| v4 | 设置系统：settings.json 持久化、guide 自动生成、快捷键查表、三页签设置窗口；62/62 |
| v3.3 | 滚轮自适应一档归一化、缩放速度按钮、滑动帧率优化；58/58 |
| v3.2 | 滚轮手感归一化（一档≈80% 视口高）、适应高度与单页一致；58/58 |
| v3.1 | 滚轮纯比例脉冲根治弱 Delta 设备、适应宽度铺满、F12 日志；57/57 |
| v3 | 滚动模式 6 问题修复（黑屏/惯性回弹/复位语义等）；56/56 |
| v2.5 | 滚动模式三问题修复（输入累积器/默认视图/跳页占位/拖拽限幅/解码提速）；52/52 |
| v2.3 系列 | 预览栏覆盖层 + 主题色 + 书签隐藏 + 双页高度匹配 + 动态张数等；36→38 |
| v2.0–v2.2 | 底部悬浮预览栏 → 右侧竖排重构 |
| v1.9.x | 物理页导航、缩放百分比、F12 调试窗口 |
| v1.8 | 12 项修复（竞态防护、扫描进度、滚动重构、右键文件操作等） |
| v1.0–1.7 | 画布模型重构、Webtoon、按需高清重解码、目录浏览器、位图引用计数 |

> 逐项详细记录：`docs/03-dev/v2-changes-record.md`（v2.0→v5.2 全记录）、`v1.9-changes-record.md`、`bugs/BUG-xxx.md`。

---

## 数据与隐私

- 设置、阅读进度、书签、日志全部保存在本机 `%LOCALAPPDATA%\HachimiManga`（或 exe 旁 `guide\`）
- 程序**不联网、不上传任何数据**；删除该目录即可清空全部数据
- 本仓库**不含任何用户数据 / 真实漫画**：`test-file\`、`reference\screenshots\`、`reference\fixtures\big\`、`dist\`、`publish\`、`bin\`、`obj\` 均被 `.gitignore` 排除且从未提交

---

## 常见问题与坑位

1. **dotnet 不在 PATH**：SDK 装在用户目录，必须用完整路径 `"$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe"`（详见 `setup.md`）
2. **行尾 CRLF**：axaml/cs 为 CRLF，脚本批量替换多行文本需用 `\r\n`，否则匹配失败
3. **Avalonia 12 API**：`Visual.VisualParent` 为 internal、`IsVisualDescendantOf` 不存在 → 可视树向上遍历用 `GetVisualParent()` 扩展（Avalonia.VisualTree）
4. **发布版双击提示"需要 .NET"**：用 self-contained publish（已内置运行时），**不要直接拷 Debug exe**
5. **测试输出被吞**：headless 下 `Console.WriteLine` 不显示，诊断用 Assert 消息
6. **内存受限**：构建/测试加 `-m:1 -p:UseSharedCompilation=false` 防并行 OOM / 残留编译进程
