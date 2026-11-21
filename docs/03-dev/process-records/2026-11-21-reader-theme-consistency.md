# 2026-11-21 阅读器白天/夜间主题一致性（v4.23）过程记录

## 任务概述

用户反馈两处：①夜间书签按钮只有文案变化、没有颜色高亮；②白天书签面板「清空/关闭」按钮 hover 变白与背景融合。
并要求全面检查白天 UI 差异（预览栏选取框、当前页黄色文字高亮等），做白天/夜间一致性；同时延长书签条目可点击区域。
决策：白天高亮色用 `#0057B8`；书签条目"当前页标记"不做（保持极简）。

## 过程简述

1. **排查**：逐一核对阅读器 XAML 的样式与本地属性，定位 9 处不一致，归为三类根因：
   硬编码颜色（`#555`、`#666666`）、默认模板（浅色下 hover 变白）、本地值覆盖样式（书签 Foreground、卡片 BorderBrush/BorderThickness）。
2. **主题色板**：`ReaderViewModel` 新增/调整 6 个属性（`PreviewStripHighlight` 白天改 `#0057B8`、`PreviewStripHighlightText`、
   `PreviewStripPanelHoverBg`、`PreviewStripCardBorder`、`ToolbarSeparator`、`BookmarkActiveHoverBg`），全部在 `OnIsDarkChanged` 通知。
3. **统一样式**：新增 `Button.panelFlat`（自定义模板 + 雾面 hover），应用于预览栏 ▲/▼、书签面板 4 个按钮；
   书签选中态选择器提升特异性为 `StackPanel.toolbar Button.bookmarkState.on` 并移除按钮本地 `Foreground`；
   预览卡片删除内联 `BorderBrush`/`BorderThickness`，改由样式控制（普通 1px / 当前页 2px + 主题色）。
4. **书签条目**：跳转按钮 `HorizontalAlignment=Stretch` + `HorizontalContentAlignment=Left`（整行可点，× 独立 `MinWidth=24`）；面板宽 248。
5. **验证**：新增 `ReaderThemeConsistencyTests`（3 例），全量 **189/189 通过**。

## 遇到的问题（现象 → 原因 → 解决）

| 现象 | 原因 | 解决 |
|---|---|---|
| 书签按钮选中后只有 ★ 文案变、颜色不变 | 高亮样式与工具栏样式特异性相同、后者在后；且按钮有 `Foreground` 本地值 | 选择器加 `StackPanel.toolbar` 前缀；删本地 `Foreground` |
| 预览栏当前页边框在白天"没有框" | 卡片上有内联 `BorderBrush="#555"`（本地值覆盖样式）；且 `BorderThickness="1"` 覆盖 2px | 删除两处本地值，样式分层设置 |
| 白天面板按钮 hover 变白 | 用 Avalonia 默认 Button 模板（浅色 hover 白底） | 统一 `Button.panelFlat` 自定义模板 + 雾面 hover 色 |
| 编译报 "XML comment cannot contain '--'" | 我在注释结尾误写 `*/` 而非 `-->` | 修正注释结尾 |
| PowerShell 批量替换 XAML 未生效 | 源文件 CRLF 与脚本 here-string 的 LF 不一致 | 改用精确编辑工具逐处修改 |

## 最耗时项

在 Avalonia 的样式优先级（本地值 > 样式；同特异性后定义胜）与默认控件模板之间反复定位——"看起来设置了却不起作用"全部来自这两条规则。

## 最耗 token 项

XAML 与 VM 的多处交叉修改（9 处样式 + 7 个色板属性 + 面板布局），以及为验证"样式真的生效"而写的 headless 测试。

## 规避与完善方案

- 以后新增阅读器内可交互控件：**颜色一律走 VM 主题属性**、**按钮一律加 `panelFlat`**（或工具栏 `toolbar` 类）、
  **不要在控件上写会覆盖样式的本地颜色/边框值**；
- 设计文档记录色板表，新增颜色前先查表，避免再出现硬编码；
- 可选后续：预览卡片内的大页码占位文字也纳入当前页变色（不急）。
