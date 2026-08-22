# 项目总览与文档导航

> 最后更新：2026-08-22（v2.3 已实施，36/36 测试通过）。
> 产品功能清单见根目录 [README.md](../README.md)；本页为文档导航。

## 一句话
做一个 **Windows 优先、高度可定制、面向日漫阅读的本地漫画阅读器**，自带本地库与文件管理；未来考虑移动端。

## 项目状态
- **阶段**：调研 → 方案设计 → 用户决策 → **实施中**（当前版本 v2.3）
- **技术栈**：C# / .NET 9 / Avalonia 12（Windows 10，PowerShell 5.1 构建）
- **验证**：36/36 自动化测试通过；真实书压力 4/4 零崩溃；内存稳定 ~300–420MB
- **发布**：app/publish/MangaAgent.App.exe（自包含 win-x64）

## 调研结论速览
- **市场空位**：Windows 端缺少"原生质感 + 移动端级自定义深度 + 内置文件管理"的现代漫画阅读器
- **技术选型已定稿**：Avalonia（C#/.NET）（决策记录：[decision-log.md](02-design/decision-log.md)）
- **核心护城河**：阅读引擎（RTL/双页规则/滚动/缩放/动画/滤镜）与设置分层+预设体系

## 调研子报告（docs/01-research）
| 文档 | 内容 |
|---|---|
| [competitive-summary.md](01-research/competitive-summary.md) | 竞品总览与结论汇总（先读这个） |
| [open-source-readers.md](01-research/open-source-readers.md) | 开源阅读器深挖（Mihon/YACReader/Komga/Kavita 等 10 项目） |
| [commercial-ux.md](01-research/commercial-ux.md) | 商业/移动端阅读器与 UX 模式（Perfect Viewer/Panels/Cover 等） |
| [tech-stack-eval.md](01-research/tech-stack-eval.md) | 技术选型评估（7 框架对比 + 关键库清单） |

## 方案设计文档（docs/02-design）
| 文档 | 状态 |
|---|---|
| [requirements.md](02-design/requirements.md) | 需求 v1（含待用户决策项 §7） |
| [architecture.md](02-design/architecture.md) | 架构 v1（模块分层 + 核心库可服务化） |
| [reading-engine.md](02-design/reading-engine.md) | 阅读引擎 v1（双页规则/缩放/渲染管线） |
| [library-file-management.md](02-design/library-file-management.md) | 本地库与文件管理 v1 |
| [ui-ux-design.md](02-design/ui-ux-design.md) | UI/UX v1（四层设置+预设/主题/交互） |
| [roadmap.md](02-design/roadmap.md) | 路线图 M0–M4 |
| [reader-experience-v3.md](02-design/reader-experience-v3.md) | 阅读器交互重构 v1（画布模型，已实施） |
| [ux-v2-plan.md](02-design/ux-v2-plan.md) | UX v2 计划（预览栏等，已实施为 v2.x） |

## 开发与维护文档（docs/03-dev）
| 文档 | 内容 |
|---|---|
| [README.md](03-dev/README.md) | 开发文档索引 |
| [CODE-MAP.md](03-dev/CODE-MAP.md) | 代码地图（模块/文件定位） |
| [conventions.md](03-dev/conventions.md) | 代码规范与约定 |
| [setup.md](03-dev/setup.md) | 环境搭建 |
| [m1-acceptance.md](03-dev/m1-acceptance.md) | M1 验收 |
| [poc-report.md](03-dev/poc-report.md) | PoC 报告 |
| [v2-changes-record.md](03-dev/v2-changes-record.md) | **v2.0–v2.3 变更全记录（最新）** |
| [v1.9-changes-record.md](03-dev/v1.9-changes-record.md) | v1.8 + v1.9 修复改动全记录 |
| [preview-strip-final.md](03-dev/preview-strip-final.md) | 预览栏最终方案（含实施后现状） |
| [bugs](03-dev/bugs) | Bug 跟踪（BUG-001~007 已修复） |

## 目录结构
| 目录 | 内容 |
|---|---|
| docs/ | 调研与设计文档（本页导航） |
| reference/ | 参考资料（links 链接存档、research-raw 原始抓取材料） |
| app/ | 软件代码（Avalonia 12 / .NET 9，已实施） |
| tools/ | 开发辅助脚本（make-fixtures.ps1 / run.ps1） |
| test-file/ | 用户真实测试书（realbook 等） |

## 决策流程约定
调研 → 方案设计 → **用户审阅并决策** → 实施（用户明确批准后才开始写软件代码）→ 验证 → 记录归档。
