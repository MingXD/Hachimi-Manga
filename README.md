# MangaAgent — 本地漫画阅读器

> 状态：**已实施**（当前版本 v2.3，2026-08-22）。Windows 优先、面向日漫（RTL）阅读的高度可定制本地阅读器，自带本地库与文件管理；未来考虑 iOS/Android。

## 已实现功能总览

### 📖 阅读器（Reader）
| 功能 | 说明 |
|---|---|
| 画布模型交互 | 滚轮=以鼠标为焦点精细缩放（2%–800%）、拖拽平移（可拖出边界、空白不拖）、双击放大/复位、←→翻页、复位 |
| 三种阅读模式 | 单页 / 双页（RTL 右页在前、严密贴合、配对起点可选）/ Webtoon（上下无缝拼接、平滑滚动+惯性、Ctrl+滚轮缩放） |
| 物理页导航 | 单页逐物理页推进；进度/书签存物理页，跨模式稳定；页码显示（单页=物理页号，双页=逻辑页号） |
| 缩放控制 | −/+ 按钮（1.4 倍）、缩放灵敏度（快/中/慢）、适应模式循环（宽度/高度/屏幕）、百分比显示、缩放×N 按钮 |
| **预览栏（v2.3.1）** | 右侧悬浮覆盖层（不占布局、不推挤图片）；鼠标移到右缘 48px 立即唤出；竖排 7 张缩略图（150px 小图解码）+ **页码在卡片右侧**（不占纵向空间，小屏不遮挡）；**当前页高亮**（2px 主题色边框夜金/昼蓝 + 页码变色）；▲▼整排跳 ±7、滚轮逐页、点击跳转（三模式映射）；Webtoon 双向联动；**背景绑定主题色（夜 #CC1C1C24 / 昼 #CCFFFFFF，均半透明）** |
| 主题 | 白天/夜间一键切换（T 键），工具栏/预览栏配色联动 |
| 书签 | 当前页书签 / 上一 / 下一书签（快捷键 B）——**工具栏按钮 v2.3 起暂时隐藏，命令保留** |
| 调试 | F12 调试窗口（状态 + 最近 100 条事件流）、按日轮转日志 |

### 📚 本地库与文件管理（Library）
| 功能 | 说明 |
|---|---|
| 资料库 | 添加/扫描（进度提示、坏文件容错）、"已在库中"自动切换、重新扫描、启动恢复 |
| 目录树 | 文件夹层级树 + 面包屑导航 + 混合展示（文件夹 + 漫画封面） |
| 封面墙 | 展示切换：大 / 中 / 小 / 详情 |
| 文件操作 | 右键：打开 / 重命名 / 复制 / 移动 / 删除（回收站）/ 在资源管理器中显示 / 属性 |
| 格式 | ZIP/CBZ 漫画包、纯图片文件夹自动识别为条目 |

### 🛠 开发支撑
- 自动化测试 **36/36 通过**（含阅读器交互、缩放焦点、模式切换、Webtoon 虚拟化、快速跳页、库扫描、重置/方向、预览栏覆盖层回归等）
- 真实书压力测试 4/4 零崩溃、内存稳定（~300–420MB）
- 全局崩溃日志 + 事件流（F12 可视化）

## 目录导航
| 目录 | 内容 |
|---|---|
| [docs/00-overview.md](docs/00-overview.md) | 项目总览与文档导航 |
| [docs/01-research](docs/01-research) | 竞品与生态调研（开源、商业/UX、技术选型） |
| [docs/02-design](docs/02-design) | 方案设计（需求、架构、阅读引擎、文件管理、UI/UX、路线图） |
| [docs/03-dev](docs/03-dev) | 开发与维护（代码地图、变更记录、Bug 跟踪、预览栏方案） |
| [reference](reference) | 参考资料（外部链接、笔记存档） |
| [app](app) | 软件代码（C# / Avalonia 12 / .NET 9） |
| [tools](tools) | 开发辅助脚本（make-fixtures.ps1 / run.ps1） |

## 常用命令（Windows）
```powershell
# 构建（Debug）
& "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe" build app\src\MangaAgent.App -m:1 -p:UseSharedCompilation=false

# 测试
& "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe" test app\src\MangaAgent.Tests -m:1 -p:UseSharedCompilation=false

# 发布（Release，输出到 app\publish）
& "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe" publish app\src\MangaAgent.App -c Release -r win-x64 --self-contained -p:PublishSingleFile=false -m:1 -p:UseSharedCompilation=false
# 然后 robocopy ...\bin\Release\net9.0\win-x64\publish app\publish /MIR
```

> 完整发布流程（含参数原理/环境坑位/验证清单）：[docs/03-dev/release.md](docs/03-dev/release.md)

## 版本历史（最近）
| 版本 | 内容 |
|---|---|
| **v2.3** | 预览栏覆盖层（不推挤图片）+ 主题半透明背景 + 书签按钮临时隐藏 + 双页高度匹配修复；36/36 测试 |
| v2.2c / v2.2b | 右缘热区不占布局（48px 检测）+ 白天按钮文字黑色修复（TemplateBinding/通知） |
| v2.2 | 预览栏移到右侧竖排（7 张卡片、▲▼±7、滚轮逐页、占位图） |
| v2.1 | 预览栏重构（150px 小图解码、ObservableCollection、seq 防并发）——根治 CPU/内存拉满崩溃 |
| v2.0 | 底部悬浮预览栏（进度条+缩略图融合） |
| v1.9.x | 物理页导航、缩放百分比、添加库切换、按钮模板根治、F12 调试窗口 |
| v1.8 | 12 项修复（竞态防护、扫描进度、滚动重构、右键文件操作等） |
| v1.0–1.7 | 画布模型重构、Webtoon 模式、按需高清重解码、快速翻页、目录浏览器、位图引用计数 |

> 详细变更记录：[docs/03-dev/v2-changes-record.md](docs/03-dev/v2-changes-record.md)（v2.x）、[docs/03-dev/v1.9-changes-record.md](docs/03-dev/v1.9-changes-record.md)（v1.8+v1.9）、Bug 跟踪：[docs/03-dev/bugs](docs/03-dev/bugs)。
