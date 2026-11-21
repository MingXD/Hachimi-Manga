# 阅读器白天/夜间主题一致性（v4.23）

> 问题：阅读器有"主题色板"，但只覆盖了部分控件——其余控件**硬编码颜色**、**用默认模板**（浅色下 hover 变白）、
> 或**被本地属性覆盖**（样式高亮不生效）。白天看起来"很多地方没有框、没有高亮"，与夜间观感不一致。

## 1. 排查出的 9 处不一致（都已修复）

| # | 位置 | 根因 | 修复 |
|---|---|---|---|
| 1 | 书签按钮选中态 | 高亮样式与工具栏样式**特异性相同**、工具栏样式在后 → 被覆盖；按钮上 `Foreground` 本地值压样式 | 选择器改 `StackPanel.toolbar Button.bookmarkState.on`；移除本地 `Foreground`；选中态加粗 |
| 2 | 预览卡片边框 | 样式写 `#555`，卡片上还有**内联 `BorderBrush`**（本地值）→ 高亮边框永不生效 | 删除内联值；样式绑定 `PreviewStripCardBorder` |
| 3 | 预览卡片当前页 2px 边框 | 卡片本地 `BorderThickness="1"` 覆盖样式 `.current` 的 2px | 删本地值；`.previewCard` 样式设 1、`.current` 设 2 |
| 4 | 当前页页码 | 无加粗、白天蓝色偏淡 | 绑定 `PreviewStripHighlightText`（白天 `#004C99`）+ `SemiBold` |
| 5 | 预览栏 ▲/▼ 按钮 | 无自定义模板 → 白天 hover 变白 | 统一 `Classes="panelFlat"` |
| 6 | 书签面板「清空/关闭」 | 同上 | 同上 |
| 7 | 书签条目跳转 / × | 同上 + 点击区域只包住文字 | `panelFlat` + 跳转按钮 `Stretch`（整行可点），× 固定 `MinWidth=24` |
| 8 | 工具栏分隔线 | 硬编码 `#666666` | 绑定 `ToolbarSeparator`（夜 `#666666` / 昼 `#C8C4BC`） |
| 9 | 预览卡片普通边框 | 硬编码 `#555` | 绑定 `PreviewStripCardBorder`（夜 `#555` / 昼 `#A9A6A0`） |

## 2. 主题色板（`ReaderViewModel`，随 `IsDark` 通知）

| 属性 | 夜间 | 白天 | 用途 |
|---|---|---|---|
| `PreviewStripHighlight` | `#FFD700` | `#0057B8` | 当前页卡片边框 / 页码高亮 |
| `PreviewStripHighlightText` | `#FFD700` | `#004C99` | 当前页页码文字 |
| `PreviewStripPanelHoverBg` | `#33FFFFFF` | `#22000000` | 面板/预览栏按钮 hover 雾面底 |
| `PreviewStripCardBorder` | `#555` | `#A9A6A0` | 预览卡片普通边框 |
| `ToolbarSeparator` | `#666666` | `#C8C4BC` | 工具栏分隔线 |
| `BookmarkActiveBg/Border/Foreground` | `#4A3B0F` / `#FFD700` / `#FFE066` | `#DCEBFF` / `#0078D4` / `#005A9E` | 书签按钮选中态 |
| `BookmarkActiveHoverBg` | `#6B5514` | `#C7DFFF` | 选中态悬停底色（不回落成普通按钮色） |

## 3. 统一组件

- `Button.panelFlat`：自定义模板（Border + ContentPresenter，透明底、无边框、圆角 3），
  `:pointerover`/`:pressed` 用 `PreviewStripPanelHoverBg`；应用于预览栏 ▲/▼、书签面板 4 个按钮。
- `Border.previewCard`：`:pointerover` 边框变主题高亮色（hover 反馈）。
- `TextBlock.previewLabel.current`：当前页页码变色 + 半粗。

## 4. 验证

- 手动（夜/昼各一遍）：书签按钮选中态（背景+边框+文字色+加粗）、预览栏当前页卡片（2px 主题色边框 + 页码变色）、
  预览栏 ▲/▼ 与书签面板按钮 hover（雾面半透明，非纯白）、工具栏分隔线颜色。
- 自动化：`ReaderThemeConsistencyTests`
  - `Bookmark_Button_Highlight_And_Panel_Flat_Buttons`：选中态 `Classes` 含 `on`、文案 ★、取消后复原；
  - `Panel_Buttons_Use_PanelFlat_Style`：面板内所有按钮都带 `panelFlat`；
  - `Theme_Palette_Switches_Between_Dark_And_Light`：7 个色板属性两套取值正确。

## 5. 代码入口

- `app/src/MangaAgent.App/ViewModels/ReaderViewModel.cs`：主题色板属性 + `OnIsDarkChanged` 通知
- `app/src/MangaAgent.App/Views/ReaderWindow.axaml`：样式（panelFlat / 书签选中态 / 预览卡片与页码）、按钮类名、分隔线
- 测试：`app/src/MangaAgent.Tests/PoC/ReaderThemeConsistencyTests.cs`
