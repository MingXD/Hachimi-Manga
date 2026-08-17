# 项目总览与文档导航

> 最后更新：调研完成，设计文档 v1 就绪，等待用户评审决策。

## 一句话
做一个 **Windows 优先、高度可定制、面向日漫阅读的本地漫画阅读器**，自带本地库与文件管理；未来考虑移动端。

## 调研结论速览
- **市场空位**：Windows 端缺少"原生质感 + 移动端级自定义深度 + 内置文件管理"的现代漫画阅读器
- **技术选型已定稿**：Avalonia 11（C#/.NET）（决策记录：[decision-log.md](02-design/decision-log.md)）
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

## 目录结构
| 目录 | 内容 |
|---|---|
| docs/ | 调研与设计文档（本页导航） |
| reference/ | 参考资料（links 链接存档、research-raw 原始抓取材料） |
| app/ | 软件代码（实施阶段启用） |
| tools/ | 开发辅助脚本（占位） |
| docs/03-dev/ | 开发与维护文档（实施阶段补充） |

## 决策流程约定
调研 → 方案设计 → **用户审阅并决策** → 实施（用户明确批准后才开始写软件代码）。
