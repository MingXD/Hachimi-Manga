# 预览栏系列 + v2.3 修复改动全记录（v2.0–v2.3）

> 用途：用户逐个功能检查时对照排查。每项含：改动内容、文件位置、可能影响的功能。
> 上一篇：v1.9-changes-record.md（v1.8 + v1.9）。

---

## 一、v2.3 本次改动（2026-08-22，最新）

> 用户确认方案后实施：预览栏改覆盖层 + 主题色 + 书签隐藏 + 双页高度匹配。

| # | 修复/改动 | 文件位置 | 可能影响的功能（风险点） |
|---|---|---|---|
| 2.3-1 | **预览栏改覆盖层**：PreviewStrip 从 DockPanel 右侧布局元素移入 Viewport 内部（`<Grid><PageLayer/><PreviewStrip/></Grid>` 叠加，HorizontalAlignment=Right，Width=120）→ 不再占布局，图片不再被左推；显示时遮挡图片右侧可接受（用户确认） | ReaderWindow.axaml：Viewport 内容包 Grid + PreviewStrip 移入 | **高危**：阅读器布局、预览栏显示/隐藏、滚轮/双击/拖拽交互（见守卫） |
| 2.3-2 | **预览栏背景绑定主题色（均半透明）**：夜 `#CC1C1C24` / 昼 `#CCFFFFFF`；缩略图条目底色夜 `#3A3A44` / 昼 `#E8E6E1`；页码/▲▼文字夜 `#CCC` / 昼 `#333`；OnIsDarkChanged 补 3 个通知 | ReaderViewModel.cs：PreviewStripBg/PreviewStripItemBg/PreviewStripLabel + OnIsDarkChanged | 主题切换后预览栏配色即时刷新 |
| 2.3-3 | **书签 3 按钮临时隐藏**：书签 / ◀书签 / 书签▶ 加 `IsVisible="False"`（命令保留，后续恢复） | ReaderWindow.axaml：工具栏 | 工具栏布局、快捷键 B 仍生效 |
| 2.3-4 | **双页高度匹配修复**：LoadCurrentAsync 改为 Ratio 先于 Image 设置（`Ratio1→Image1→Ratio2→Image2`），Relayout 用正确的宽高比布局两图（原来 Image 先设触发 Relayout 用旧 Ratio=0.7 → 双页两图高度不一致） | ReaderViewModel.cs：LoadCurrentAsync | **高危**：双页显示、Relayout |
| 2.3-5 | **覆盖层守卫**：PreviewStrip 在 Viewport 内部后事件冒泡到 Viewport 处理器（Avalonia XAML 处理器默认忽略 Handled）→ ①滚轮不再"翻页+缩放"同时发生 ②双击预览栏不缩放 ③鼠标在预览栏左半不再被旧隐藏分支误判隐藏（隐藏条件改 `edgePos.X < PreviewStrip.Bounds.Left - 10`） | ReaderWindow.axaml.cs：IsOverPreviewStrip（GetVisualParent 遍历）+ OnPointerWheelChanged/OnViewportDoubleTapped 守卫 + OnViewportPointerMoved 隐藏分支 | 预览栏滚轮/双击/悬停、Viewport 滚轮缩放 |
| 2.3-6 | **新增覆盖层回归测试**：PreviewStripOverlayTests.cs（覆盖层结构 / 不占 DockPanel / 显隐不改 Viewport 尺寸 / 主题色切换） | MangaAgent.Tests/PoC/PreviewStripOverlayTests.cs | 测试基线 35→36 |

### Avalonia 12 API 备忘（实现中踩坑）
- `Visual.VisualParent` 为 internal（外部不可用）、`IsVisualDescendantOf` 不存在 → 用 `GetVisualParent()` 扩展（Avalonia.VisualTree 命名空间）
- axaml 文件为 CRLF 行尾，脚本替换多行文本需用 \r\n 否则不匹配

---

## 一·五、v2.3.1 预览栏页码右侧 + 当前页高亮（2026-08-22）

> 用户反馈：全屏时预览栏第 7 张被遮挡 8 成、当前页无高亮。

| # | 修复/改动 | 文件位置 | 可能影响的功能（风险点） |
|---|---|---|---|
| 2.3.1-1 | **第7张被遮根因**：卡片高度公式只算卡片高，漏算每张下方页码 TextBlock（~15px×7=105px 固定纵向溢出）→ 页码移到卡片右侧（横向 卡片|页码），不占纵向空间，溢出根治 | ReaderWindow.axaml：ThumbStrip DataTemplate 改 Horizontal StackPanel | 预览栏布局、页码显示位置 |
| 2.3.1-2 | 预览栏宽 120→132（容纳右侧页码 ~22px + 间距） | ReaderWindow.axaml：PreviewStrip Width | 覆盖层遮挡范围（+12px，用户确认可接受） |
| 2.3.1-3 | **当前页高亮**：ThumbStripItem.IsCurrent + RefreshThumbStrip 标记（== CurrentPhysical）；卡片 2px 主题色边框（夜金 #FFD700 / 昼蓝 #0078D4）+ 页码变色；样式走 Window.Styles（Border.previewCard.current / TextBlock.previewLabel.current，class 绑定） | ReaderWindow.axaml(.cs) + ReaderViewModel.cs：PreviewStripHighlight + OnIsDarkChanged | 高亮绑定、主题切换 |
| 2.3.1-4 | 新增回归测试：唤出后恰 7 项、恰一项 IsCurrent 且为当前物理页、ScrollViewer 纵向不溢出 | PreviewStripOverlayTests.cs | 测试基线 36→37 |

### 设计权衡备忘
- 卡片上限 130 **保留**：缩略图按 150px 宽解码（性能），130px 显示接近 1:1 不糊；去掉上限需提高解码分辨率（内存翻倍）
- 页码在卡片右侧垂直居中、固定宽 22px 右对齐，1–3 位数字均放得下

---

## 一·六、v2.3.2 预览栏动态张数 + 固定卡片 + 按钮等宽 + 缩略图铺满（2026-08-22）

> 用户反馈：非全屏矮窗只显示 6 张且末张点不到；全屏第 7 张仍遮一点点。

| # | 修复/改动 | 文件位置 | 可能影响的功能（风险点） |
|---|---|---|---|
| 2.3.2-1 | **动态张数**：固定卡片 96×130，张数 n = floor((视口高−58)/136)，范围 1..11（奇偶皆可、不强制居中）；窗口越矮张数越少，**连 1 张都不够高才遮挡**（用户确认） | ReaderWindow.axaml.cs：RefreshThumbStrip + _thumbStripCount 字段 | 预览栏张数、▲▼步长 |
| 2.3.2-2 | **防溢出根治**：总高 = n×130+(n−1)×6 ≤ 视口高−58 → 第 n 张任何窗口高度完整可见；全屏"遮一点点"的渲染误差一并消失（原公式零余量设计，±几 px 偏差就顶出） | ReaderWindow.axaml.cs：RefreshThumbStrip | 预览栏内容完整性 |
| 2.3.2-3 | **▲▼ 步长跟随张数**：跳 ±n（整排换，显示几张跳几张） | ReaderWindow.axaml.cs：OnPreviewStripPrev/Next | 预览栏翻页 |
| 2.3.2-4 | **▲▼ 按钮等宽 124px**（= 卡片96 + 间距6 + 页码22），与下方缩略图行视觉对齐 | ReaderWindow.axaml：▲▼ Button Width | 预览栏观感 |
| 2.3.2-5 | **缩略图铺满卡片**：Image Stretch UniformToFill（卡片 ClipToBounds 裁剪）——缩略图用于一眼识别，允许裁剪 | ReaderWindow.axaml：ThumbStrip Image | 缩略图显示（日漫竖版比例接近，裁剪轻微） |
| 2.3.2-6 | **尺寸变化自动重算**：Viewport SizeChanged 时预览栏可见则重算张数（全屏/拖窗切换后立即正确） | ReaderWindow.axaml.cs：OnViewportSizeChanged | 全屏切换、窗口缩放 |
| 2.3.2-7 | 测试改动态断言（张数与公式一致）+ 新增极小窗口（1100×340 → n=1）用例 | PreviewStripOverlayTests.cs | 测试基线 37→38 |

### 设计备忘
- 张数不强制奇数/居中：首尾页时当前页就在第一/最后一张（用户确认可接受）
- 卡片固定 96×130 不随窗口变高（150px 小图 1:1 清晰度），屏幕大就展示更多张（上限 11）

---

## 一·七、v2.3.3 卡片高度连续填满 + 按钮图标居中（2026-08-22）

> 用户反馈：全屏 5 张但上下余量大（张数死区）。

| # | 修复/改动 | 文件位置 | 可能影响的功能（风险点） |
|---|---|---|---|
| 2.3.3-1 | **卡片高连续填满**：cardH = clamp((视口高−58−6(n−1))/n, 80, 150)，宽度 96 不变；张数逻辑不变（n = floor((H−58)/136)，1..11） | ReaderWindow.axaml.cs：RefreshThumbStrip | 卡片尺寸、预览栏观感 |
| 2.3.3-2 | **死区余量消除**：固定 130 时张数死区（如 850px 视口 5 张余 112px）；填满后余量 ≤ ~41px（卡片封顶 150 前） | ReaderWindow.axaml.cs | 全屏/常规窗口余量 |
| 2.3.3-3 | **清晰度保护**：上限 150 = 150px 源图缩小显示（不放大不糊）；下限 80（极小窗口 1 张保底） | ReaderWindow.axaml.cs | 缩略图清晰度 |
| 2.3.3-4 | **▲▼ 图标居中**：按钮加 Padding=0 + Horizontal/VerticalContentAlignment=Center | ReaderWindow.axaml：▲▼ Button | 按钮观感 |
| 2.3.3-5 | 测试加 cardH 公式一致性断言（实际=期望公式，容差 0.5）+ 范围 80..150 | PreviewStripOverlayTests.cs | 测试基线 38 |

### 设计备忘
- 卡片高 150 时比例 0.64（96:150），UniformToFill 裁剪从"上下裁"变"左右裁"（各 ~4.5px，日漫主体居中无感）
- 若用户觉得 150 高时卡片过窄长 → 备选 A2：cardW = cardH×0.7（预览栏加宽至 ~140，▲▼/页码联动调整）

---

## 一·八、v2.3.4 预览栏视觉微调（2026-08-22）

| # | 修复/改动 | 文件位置 | 可能影响的功能（风险点） |
|---|---|---|---|
| 2.3.4-1 | ▲▼ 按钮高 32→26（更矮） | ReaderWindow.axaml：▲▼ Button Height | 预览栏观感 |
| 2.3.4-2 | Grid 加 RowSpacing=6 → 按钮与卡片间距 = 卡片间距 6px，视觉一致（原为 0 + ItemsControl 居中留白） | ReaderWindow.axaml：PreviewStrip Grid | 预览栏间距观感 |
| 2.3.4-3 | 卡片高公式常数 58→64（按钮26×2 + RowSpacing6×2）同步，防溢出不变 | ReaderWindow.axaml.cs：RefreshThumbStrip | 卡片高度计算 |

---

## 一·九、v2.3.5 双页拼接无缝（2026-08-22）

> 用户反馈：双页模式两图中间有时出现很窄黑边。

| # | 修复/改动 | 文件位置 | 可能影响的功能（风险点） |
|---|---|---|---|
| 2.3.5-1 | **双页无缝拼接**：PagePanel Spacing 0→-1，两图重叠 1px 盖住接缝黑线（GPU 纹理采样/亚像素对齐缝隙） | ReaderWindow.axaml：PagePanel | 双页贴合观感 |

### 分析备忘
- 布局本身无缝隙（Spacing=0、Ratio 来自 JPEG 头真实解析、无 letterbox）
- 黑边为**渲染缝隙**（相邻纹理采样 + 非整数像素对齐），缩放/特定窗口尺寸下偶发 → 负间距重叠是游戏/图片拼接通用解法
- 若用户实测黑边仍在且不随缩放变化 → 是图片内容边缘（日漫扫描页黑出血线），备选方案：RenderTargetBitmap 合成单图（物理无接缝）或裁剪图片边缘

---

## 一·十、v2.3.6 主界面修复（2026-08-22）

> 用户反馈：右上角搜索框没反应；展示模式（小图封面超框/大图封面太小/详情空白）；纯图片文件夹统计错误。

| # | 修复/改动 | 文件位置 | 可能影响的功能（风险点） |
|---|---|---|---|
| 2.3.6-1 | **搜索框修复**：ApplyFilter 原过滤 Books（未绑定 UI 的集合）→ 改为**全库搜索**（遍历当前库 FolderTree，命中文件夹 + 漫画含纯图片 FolderBook/Zip），结果按类型+名称排序；清空恢复目录浏览；搜索态点文件夹条目 → 退出搜索并进入 | MainWindowViewModel.cs：ApplyFilter/CollectSearchHits/OpenAction | 搜索、目录浏览切换 |
| 2.3.6-2 | **展示模式数据化**：封面/字体随模式注入（FolderEntryItemViewModel 加 CoverWidth/CoverHeight/TitleFontSize/SubtitleFontSize/IsDetail；WireEntry 按 GalleryMode 设置）→ 小图封面不再超框、大图封面匹配卡片；**详情模式行式布局**（图标📁/📖 + 名称 + 副标题，无图）；GalleryItemWidth/Height Detail 0→560/40（修复 WrapPanel 项高 0 一片空白） | MainWindowViewModel.cs：WireEntry/GalleryItemWidth/Height；FolderEntryItemViewModel.cs；MainWindow.axaml 模板双布局 | 四种展示模式 |
| 2.3.6-3 | **文件夹统计修复**：FolderTreeScanner 补 gif/bmp 扩展名（与 LibraryScanner/FolderComicSource 一致）+ FolderBook PageCount = 本目录直接图片数（原硬编码 0） | FolderTree.cs | 纯图片文件夹识别、页数显示 |
| 2.3.6-4 | **目录浏览默认排序**：文件夹在前 → 名称（OrdinalIgnoreCase） | MainWindowViewModel.cs：ShowFolderContents | 目录展示顺序 |
| 2.3.6-5 | 新增 3 测试：全库搜索（含 gif 识别/页数/排序/清空恢复/详情属性）、有子文件夹+图片的文件夹按文件夹显示并统计子文件夹数 | FolderBrowserTests.cs | 测试基线 38→40 |

### 语义备忘（用户确认）
- 纯图片文件夹（无子文件夹）= 漫画，统计页数
- 有子文件夹的文件夹 = 文件夹（可进入），统计子文件夹数；其内部纯图片子文件夹在进入后显示为漫画

---

## 一·十一、v2.3.7 卡片文本防溢出 + 递归统计（2026-08-22）

> 用户反馈：标题/统计文本超出卡片；统计"35 个子文件夹"错误（纯图片子文件夹被算作子文件夹）。

| # | 修复/改动 | 文件位置 | 可能影响的功能（风险点） |
|---|---|---|---|
| 2.3.7-1 | **统计规则**：M/N 只统计本层（M=纯图片子文件夹数+本目录 FolderBook+Zip；N=非纯图片直接子文件夹数）；X=递归全层级漫画总数。文件夹副标题两行：第 1 行 "M 本漫画 · N 个子文件夹"（M/N=0 省略对应项，双 0 不显示）；第 2 行 "目录下共 X 本漫画"（X=0 不显示） | FolderEntryItemViewModel.cs：Subtitle/StatsLine2 + 静态辅助 IsPureImageFolder/CountDirectBooks/CountDirectSubfolders/CountBooksRecursive | 目录浏览副标题、详情模式 |
| 2.3.7-2 | 纯图片漫画副标题 "N 页"（去掉 "· 文件夹" 后缀，与 CBZ "N 页 · CBZ" 区分） | FolderEntryItemViewModel.cs：Subtitle | 漫画条目副标题 |
| 2.3.7-3 | **卡片封面防溢出**：封面高 = 卡片高 − 文本区（大 280/中 178/小 108）；卡片高对齐 WrapPanel 项高（366/262/188）；ClipToBounds 兜底 | MainWindowViewModel.cs：WireEntry；MainWindow.axaml | 三种卡片模式布局 |
| 2.3.7-4 | **详情模式**：副标题贴右两行小字（MaxWidth 280 省略 + ToolTip），标题列伸缩（窗口变窄标题优先省略 + ToolTip 全名） | MainWindow.axaml | 详情模式 |
| 2.3.7-5 | 新增递归统计测试（X=3 递归、M/N 分支、纯图片无后缀、M=0 仅子文件夹） | FolderBrowserTests.cs | 测试基线 40→41 |

### 统计语义（用户确认）
- 例：1. 同人志 = "34 本漫画 · 1 个子文件夹" + 第 2 行 "目录下共 44 本漫画"（子文件夹内 10 本递归计入）

---

## 一·十二、v2.3.8 封面统一比例 + 卡片间距 + 详情单行（2026-08-22）

> 用户反馈：详情模式要单行可伸缩；卡片上下贴合无空隙；三模式封面比例不一致。

| # | 修复/改动 | 文件位置 | 可能影响的功能（风险点） |
|---|---|---|---|
| 2.3.8-1 | **封面统一 0.7 比例**（单行本：新书判 148×210≈0.705 / B6 182×257≈0.708），三模式等比 ×1.5：大 216×309 / 中 144×206 / 小 96×137；封面填充展示（UniformToFill） | MainWindowViewModel.cs：WireEntry | 三模式封面观感 |
| 2.3.8-2 | **卡片尺寸重构**：卡片 = 封面 + 文本区（大 240×380 / 中 168×272 / 小 120×196）；**卡片 Margin 2→8**（卡片间 16px 空隙，不再贴合）；WrapPanel 单元格 = 卡片 + 16（256×396 / 184×288 / 136×212） | MainWindowViewModel.cs：WireEntry/GalleryItemWidth/Height；MainWindow.axaml | 封面墙布局 |
| 2.3.8-3 | **详情模式改单行**：标题列 `*` 伸缩（窗口宽窄自适应，省略号 + ToolTip 全名）+ 副标题单行贴右（合并格式 "M 本漫画 · N 个子文件夹 · 共 X 本漫画"，各段为 0 省略）；漫画条目单行页数 | FolderEntryItemViewModel.cs：SubtitleOneLine；MainWindow.axaml | 详情模式 |
| 2.3.8-4 | 测试：三模式封面比例 0.7 断言 + SubtitleOneLine 合并格式 | FolderBrowserTests.cs | 测试基线 41 |

---

## 一·十三、v2.3.9 主界面交互细化（2026-08-22）

> 用户反馈：卡片间隔要小；详情模式要仿 Windows 文件管理器（单行占满、副标题盖标题、过窄裁剪）；搜索框层级（按钮盖搜索框）。

| # | 修复/改动 | 文件位置 | 可能影响的功能（风险点） |
|---|---|---|---|
| 2.3.9-1 | **搜索框层级**：TextBox 移到顶栏 DockPanel 最前声明（Dock=Right 仍最右，渲染最底层）→ 窗口变窄重叠时按钮盖住搜索框 | MainWindow.axaml：顶栏 | 搜索框位置不变、层级修正 |
| 2.3.9-2 | **卡片间距减小**：Margin 8→4（卡片间 8px）；WrapPanel 单元格 -8（大 248×388 / 中 176×280 / 小 128×204） | MainWindow.axaml + MainWindowViewModel.cs | 封面墙间距 |
| 2.3.9-3 | **详情模式重构（仿文件管理器"详细信息"）**：取消卡片——ScrollViewer(卡片 ListBox) + DetailList 平级 Grid 按 IsDetailView 切换；详情行高 32、行间 6px、行占满内容区宽随窗口伸缩；**层级：标题先声明(NoWrap 延伸, 下层) → 副标题后声明(贴右带背景色盖住标题)**；行 MinWidth=440，窗口过窄不再变短由应用窗口右缘裁剪 | MainWindow.axaml：内容区双 ListBox + DetailList 模板；MainWindowViewModel.cs：IsDetailView | 详情模式、卡片模式互斥显示 |
| 2.3.9-4 | 测试：IsDetailView 切换断言 | FolderBrowserTests.cs | 测试基线 41 |

---

## 一·十四、v2.3.10 顶栏层级 + 详情行紧贴（2026-08-22）

> 用户反馈：搜索框仍盖按钮（要求按钮/标题最上、搜索框第二、状态文本最底）；详情行间隔过大。

| # | 修复/改动 | 文件位置 | 可能影响的功能（风险点） |
|---|---|---|---|
| 2.3.10-1 | **顶栏显式层级**：ZIndex 附加属性（Avalonia 12 属 Visual 非 Panel）——标题/按钮 ZIndex=10（最上）、搜索框 5（第二）、状态文本 1（最底最先被盖住）；声明顺序不变 → 位置不变 | MainWindow.axaml：顶栏 | 窗口变窄重叠层级 |
| 2.3.10-2 | **详情行紧贴**：ListBox 加 ItemContainerTheme（Avalonia 12 用 ItemContainerTheme 非 WPF 的 ItemContainerStyle）Padding=0 + Margin=0,1 → 去掉默认 padding，行框与视觉项目高度一致，行间 ~4px | MainWindow.axaml：DetailList | 详情模式行间距 |

### Avalonia 12 API 备忘
- ZIndex：直接写 ZIndex（Visual 属性），不用 Panel.ZIndex 前缀
- ItemContainerTheme（ControlTheme TargetType=ListBoxItem）替代 ItemContainerStyle

---

## 一·十五、v2.3.11 详情行高亮 + 间距 + 右缘留白（2026-08-22）

> 用户反馈：详情行间隔再小、hover 要高亮、右缘贴边被滚动条遮挡。

| # | 修复/改动 | 文件位置 | 可能影响的功能（风险点） |
|---|---|---|---|
| 2.3.11-1 | 详情行间距：Border Margin 0,3→0,1（行间 ~2px） | MainWindow.axaml：DetailList 模板 | 详情行间距 |
| 2.3.11-2 | **hover 高亮**：ListBox.Styles —— Border.detailRow:pointerover（背景 #2E2E38 + 边框 #5A5A6A）；副标题盖层背景同步（TextBlock.subtitleCover）避免色差 | MainWindow.axaml：DetailList | 详情行悬停反馈 |
| 2.3.11-3 | 右缘留白：DetailList Padding 右 12px，防滚动条遮挡副标题 | MainWindow.axaml：DetailList | 详情行右缘 |

---

## 一·十六、v2.3.12 详情行 hover 高亮修复（2026-08-22）

> 用户反馈：v2.3.11 的详情行高亮没生效。

| # | 修复/改动 | 文件位置 | 可能影响的功能（风险点） |
|---|---|---|---|
| 2.3.12-1 | **hover 高亮根因**：DataTemplate 里 Border/副标题的本地 Background（LocalValue）优先级高于 Style Setter → pointerover 覆盖不了。移除本地 Background，改由样式控制（Border.detailRow 基础 #1E1E26 / :pointerover #2E2E38；TextBlock.subtitleCover 同步） | MainWindow.axaml：DetailList | 详情行悬停高亮 |

### Avalonia 优先级备忘
- 属性优先级：LocalValue（XAML 直接设）> Style Setter → 需要样式切换的属性不要在模板里直接赋值

---

## 一·十七、v2.3.13 顶栏改 Grid 叠加层（2026-08-22）

> 用户反馈：搜索框仍在按钮上方（ZIndex 未生效）。

| # | 修复/改动 | 文件位置 | 可能影响的功能（风险点） |
|---|---|---|---|
| 2.3.13-1 | **根因**：DockPanel 空间不足时压缩后声明元素（Math.Min(DesiredWidth, currentBounds.Width)）而非重叠，ZIndex 无处生效。**改 Grid 单格叠加**：声明顺序即层级——最底=状态文本（右对齐，Margin 右 304 避开搜索框）→ 中=搜索框（右 280）→ 最顶=标题+按钮（左 StackPanel 不压缩，溢出盖住搜索框再盖状态文本）；移除 ZIndex | MainWindow.axaml：顶栏 | 顶栏布局、窗口变窄层叠 |

### 设计备忘
- DockPanel 不产生重叠，层叠需求用 Grid 叠加层（声明顺序 = 渲染层级）
- 状态文本 Margin 右 304 = 搜索框 280 + 24 间距

---

## 一·十八、v2.3.14 焦点翻页修复 + 资料库切换（2026-08-22）

> 用户反馈：进入漫画第一次按 ← 不翻页（◀ 按钮白框）；库无法手动切换；翻页延迟。

| # | 修复/改动 | 文件位置 | 可能影响的功能（风险点） |
|---|---|---|---|
| 2.3.14-1 | **焦点翻页（A+B）**：Viewport 加 Focusable + OnOpened 时 Viewport.Focus()（焦点落中性区无白框）；隧道阶段 KeyDown 处理器（AddHandler Tunnel）优先处理翻页键（Left/Right/PageUp/PageDown/Space）→ 焦点导航/按钮不再吞第一次按键；bubble OnKeyDown 开头加 e.Handled 检查防重复 | ReaderWindow.axaml(.cs) | 翻页、键盘交互 |
| 2.3.14-2 | **资料库切换**：侧边栏顶部加资料库下拉框（Libraries/SelectedLibrary），选择库 → OnSelectedLibraryChanged → 重建该库文件夹树 + 目录浏览 | MainWindow.axaml + MainWindowViewModel.cs | 多库管理、目录浏览 |
| 2.3.14-3 | 新增首次按键翻页回归测试（ArrowKeyFocusDiagTests：无焦点时第一次 ← 即翻页） | 测试 | 42/42 |

### 翻页延迟分析（预解码方案待确认）
- 延迟点：翻页时新页不在 LRU 缓存（Cap=6）→ 现解码 1200~4096px 大图（100~500ms，线程池不卡 UI 但显示延迟）；ZIP 读流几十 ms
- 优化方案：翻页后异步预解码相邻页 ±1~2 入缓存 + 缓存 Cap 6→10 → 连续翻页接近零延迟

---

## 一·十九、v2.3.15 资料库 UI 重构 + 清空功能 + 翻页预解码（2026-08-22）

> 用户反馈：资料库标签占整列、下拉宽度随名称变、文件夹展开左挪；需要清空资料库；翻页延迟。

| # | 修复/改动 | 文件位置 | 可能影响的功能（风险点） |
|---|---|---|---|
| 2.3.15-1 | **资料库横排**：标签+下拉+清空按钮同一行（DockPanel）；下拉 Stretch 填满剩余 = 侧边栏宽（与文件夹宽度一致）；名称 TextTrimming 截断、MaxDropDownHeight 320 | MainWindow.axaml | 侧边栏布局 |
| 2.3.15-2 | **TreeView 展开不左挪**：垂直滚动条常驻（ScrollViewer.VerticalScrollBarVisibility=Visible）——根因：展开出现滚动条占布局宽导致内容左移 | MainWindow.axaml | 文件夹树 |
| 2.3.15-3 | **清空资料库**：侧边栏清空按钮 → ConfirmDialog 确认 → 清 Libraries/树/目录/面包屑 + libraries.txt 清空 | ConfirmDialog.cs（新）+ MainWindowViewModel.ClearLibrariesCommand | 库管理 |
| 2.3.15-4 | **翻页预解码**：LoadCurrentAsync 后 PrefetchNeighborsAsync（单页 ±1 / 双页下一逻辑页两张）解码入缓存不 Acquire（防引用计数泄漏）；_prefetchSeq 防堆积；_disposed 防护；缓存 Cap 6→10 | ReaderViewModel.cs | 翻页延迟（连续翻页命中缓存即时显示） |

---

## 一·二十、v2.3.16 清空资料库修复（2026-08-22）

> 用户反馈：清空按钮没效果（点了确定也不清空）。

| # | 修复/改动 | 文件位置 | 可能影响的功能（风险点） |
|---|---|---|---|
| 2.3.16-1 | **根因（日志定位）**：ConfirmDialog.ShowDialog 在本环境不阻塞——立即返回 False 但窗口仍显示；用户点确定时方法早已返回 → 清空被跳过。**改异步 tcs 模式**：ShowAsync = await ShowDialog + TaskCompletionSource 等待按钮点击（确定 true / 取消或 X 关闭 false）；ClearLibraries 改 async await | ConfirmDialog.cs + MainWindowViewModel.cs | 确认对话框、清空资料库 |

### 教训
- ShowDialog 阻塞行为依赖环境：遇到"对话框弹出但不阻塞"时改用 await ShowDialog + TCS 模式（对不阻塞环境也健壮）
- 诊断流程：SLog 分步记录（CLEAR_TRY → CONFIRM_SHOW → CONFIRM_RETURN → OK_CLICK）精确定位卡点

---

## 一·二十一、v2.3.17 文件夹树展开左移修复（2026-08-22）

> 用户反馈：1/2 级展开正常，3 级展开整体左移一点。

| # | 修复/改动 | 文件位置 | 可能影响的功能（风险点） |
|---|---|---|---|
| 2.3.17-1 | **根因**：3 级展开缩进+长名称使内容超宽 → 水平滚动条（Auto）出现 → 布局变化左移。**修复**：TreeView 水平滚动条 Hidden（永不出现）+ 树项目名称 MaxWidth=196/TextTrimming 截断（内容不超宽）；垂直滚动条已常驻（v2.3.15）→ 任何层级展开布局完全稳定 | MainWindow.axaml：TreeView | 文件夹树布局 |

---

## 一·二十二、v2.3.18 树选中左移 + 拉宽居中修复（2026-08-22）

> 用户反馈：展开后点击 2/3 级名称选中会左移；拉宽侧边栏文本右移居中。

| # | 修复/改动 | 文件位置 | 可能影响的功能（风险点） |
|---|---|---|---|
| 2.3.18-1 | **选中左移**：名称 MaxWidth 200→180（3 级缩进 48+180=228 < 侧边栏 240 永不超宽）——根因：内容超宽时选中触发 BringIntoView 水平滚动 | MainWindow.axaml：树名称 | 文件夹树选中 |
| 2.3.18-2 | **拉宽居中**：树名称显式 HorizontalAlignment/TextAlignment=Left（文本固定左侧不随宽度居中） | MainWindow.axaml：树名称 | 侧边栏拉宽 |
| 2.3.18-3 | 注：Avalonia 12 TreeViewItem 无 Indent 属性（运行时枚举验证，只有 FontSize/Width/Height 等），缩进紧凑需自定义 TreeViewItem 模板，暂缓 | — | — |

---

## 一·二十三、v2.3.19 树选中左移根治 + 全名展示（2026-08-22）

> 用户反馈：选中 2/3 级仍左移；不要省略号截断要全部文本。

| # | 修复/改动 | 文件位置 | 可能影响的功能（风险点） |
|---|---|---|---|
| 2.3.19-1 | **左移根治**：Hidden 仅隐藏滚动条、水平滚动仍发生（offset 变化）→ 内容超宽时选中 BringIntoView 仍滚动；改 HorizontalScrollBarVisibility=**Disabled**（完全禁用水平滚动）→ 永不左移 | MainWindow.axaml：TreeView | 文件夹树选中 |
| 2.3.19-2 | **全名展示**：树名称移除 MaxWidth/TextTrimming → 全部文本，超宽由窗口自然裁剪（拉宽导航栏看全名），左对齐保留 | MainWindow.axaml：树名称 | 长名称显示 |

---

## 一·二十五、滚动模式修复（2026-08，用户反馈三问题：黑屏/惯性回弹/复位语义）

> 方案与根因分析见 [scroll-mode-fix-plan.md](scroll-mode-fix-plan.md)（用户已决策：默认视图=适应屏幕+居中、复位同语义、纯动量惯性、拖拽甩动、一次做完）。

| # | 修复/改动 | 文件位置 | 可能影响的功能（风险点） |
|---|---|---|---|
| 2.4-1 | **黑屏根治①（解码链跳过）**：`LoadCurrentAsync` 在 Webtoon 模式直接返回 —— 分页解码的 Image1/Image2 不显示，反而触发 Relayout→EnterWebtoon→seq++ 作废在途 Webtoon 解码 → 画布空黑屏 | ReaderViewModel.cs：LoadCurrentAsync | 滚动模式页码/续读/跳页状态（PageText 由 UpdateWebtoonPageText 维护，已验证） |
| 2.4-2 | **黑屏根治②（EnterWebtoon 幂等）**：`_webtoonSeq++` 只在首次进入/视口尺寸变化时执行；重复 Relayout 不再作废在途任务。尺寸变化检测改 `_webtoonBaseViewportW`（原 `_webtoonWidth*2` 在默认宽度非 50% 后恒误判 → 每次 Relayout 重建+作废） | ReaderWindow.axaml.cs：EnterWebtoon | 模式切换、窗口缩放 |
| 2.4-3 | **黑屏根治③（作废补偿）**：`LoadWebtoonImageAsync` 因 seq 过期丢弃位图后，若该页仍在可见范围 → 让出 loading 标记重新请求，杜绝"请求被丢→画布空" | ReaderWindow.axaml.cs：LoadWebtoonImageAsync | 快速跳页/切模式并发 |
| 2.4-4 | **跳页修复**：跳页对话框 Webtoon 分支补 `ScrollWebtoonToPhysical`（原只改状态不动画面→"跳转无效"）；"当前页"计算改 `PhysicalAt((-ty)/scale)`（原用分页 BaseH 算错页） | ReaderWindow.axaml.cs：ShowJumpDialog | 输入页码跳转 |
| 2.4-5 | **工具栏/键盘翻页动画面**：Webtoon 模式 ◀▶ 按钮改按方向滚动一屏（原只改 CurrentIndex 画面不动）；Space/PageDown/PageUp 方向修正（原 PageDown 实际向上滚） | ReaderWindow.axaml.cs：OnNavPrev/NextClick、OnKeyDown(Tunnel) | 滚动模式翻页方向 |
| 2.4-6 | **惯性重写（纯动量）**：删"目标-位置"双轨（`_scrollTargetTy` 失同步→惯性反冲的根因）→ 滚轮每格施加速度脉冲，帧循环指数摩擦衰减滑行停住（方向恒与滚动一致）；每帧 dt 重置（原 elapsed 自启动累积→速度项放大+衰减加速→过冲后反向回弹） | ReaderWindow.axaml.cs：ScrollWebtoonByEvent/SmoothScrollTick/ScrollWebtoonPages | 滚动手感、速度按钮、键盘翻页 |
| 2.4-7 | **拖拽防互搏+甩动**：拖拽开始 CancelSmoothScroll（残余惯性不再往回拉）；拖拽中估速，释放超阈值按拖拽方向甩动滑行（fling） | ReaderWindow.axaml.cs：OnViewportPointerPressed/Moved/Released | 拖拽手感 |
| 2.4-8 | **边界即停**：ClampWebtoonScroll 到边界清速度（不反弹）；内容窄于视口水平居中（与分页规则一致） | ReaderWindow.axaml.cs：ClampWebtoonScroll | 边界滚动、水平居中 |
| 2.4-9 | **默认视图/复位统一**：新 `FitWebtoonToPage` —— 当前页完全可见（宽=min(视口宽, 页高×ratio)）+ 水平垂直居中 + scale=1；`EnterWebtoon` 首入与 `ResetWebtoonView`（复位=回到进入默认视图）共用；封面页（第 0 页）自动满足 | ReaderWindow.axaml.cs：FitWebtoonToPage/EnterWebtoon/ResetWebtoonView | 滚动模式初始显示、复位按钮、双击进入封面 |
| 2.4-10 | **DecodeAsync 引用计数下溢修复（连带发现）**：缓存命中且位图仍被 UI 引用时复用**不 Acquire** → 调用方 Release 使计数下溢到 0 → 缓存淘汰 Dispose 掉仍被 UI 引用的位图 → 渲染 `Bitmap.get_Size()` 抛 ObjectDisposedException（快速切模式时复现）；补 Acquire 配平 | ReaderViewModel.cs：DecodeAsync | 分页/滚动所有位图生命周期（**高危**，防崩溃） |
| 2.4-11 | 新增滚动模式回归测试 6 项：进入即非空（黑屏）/默认视图居中/跳页定位+竞态/惯性单调不回弹/复位居中/快速切模式不崩 | ScrollModeFixesTests.cs | 测试基线 42→48 |

### 设计备忘
- 纯动量参数：摩擦 6.0、停止阈值 8px/s、速度上限 6000px/s、脉冲换算 7.2（单档滑行 ≈1.2×步长、连滚累积速度）—— 手感接近手机长页面
- 拖拽甩动阈值 120px/s（低于则松手即停）

---

## 一·二十六、滚动模式 v2 修复（2026-08，用户实测三问题：默认视图贴左放大/滚轮回弹/预览跳页黑屏）

> 诊断：用用户真实书（38 页 4299×6071 JPEG）headless 逐帧实测定位根因（详见 [scroll-mode-fix-plan.md](scroll-mode-fix-plan.md) 〇章）。v2 已获用户决策后实施。

| # | 修复/改动 | 文件位置 | 可能影响的功能（风险点） |
|---|---|---|---|
| 2.5-1 | **滚轮输入改"累积器+阈值分步"**（回弹根治）：删"每事件按符号打满档脉冲"→ `_wheelAccum += Delta.Y`，每满 120 触发一步（方向取累积符号，余数保留）。鼠标/触摸板 Delta 抖动（Avalonia #9858 间歇性错误、触摸板惯性尾流 ±小值）永远达不到阈值 → 不再反向整档滚动（实测：±1 交替事件画面纹丝不动）；一档 120=一步；触控板连续小 Delta=平滑滚动 | ReaderWindow.axaml.cs：ScrollWebtoonByEvent + _wheelAccum/WheelStepThreshold | **滚动手感（核心）**、速度按钮 |
| 2.5-2 | **步长调优**：step = max(60, 视口高×0.35)（一档≈半屏，实测 764 视口→267px/步→滑行 ~320px 停住） | ReaderWindow.axaml.cs：ScrollWebtoonByEvent | 滚动手感 |
| 2.5-3 | **进入/复位必重设默认视图**（贴左放大根治）：新增 `_webtoonActive` 标志——`EnterWebtoon` 在"非活动→活动"（任何进入，含再次进入/切回）一律 `FitWebtoonToPage(当前页)`（适应屏幕+水平垂直居中+scale=1）；会话中仅响应尺寸变化；`ExitWebtoon` 复位标志并清累积器（实测：分页放大 250% 后重进 → scale=1、tx=280.2 居中） | ReaderWindow.axaml.cs：EnterWebtoon/ExitWebtoon | 模式切换、窗口缩放 |
| 2.5-4 | **拖拽整视口可拖**（死区消除）：滚动模式 `PointerPressed` 不再要求 `e.Source is Image`——视口任意处可拖（两侧空白有效，预览栏覆盖层除外）；分页模式保持"仅图片可拖"；提取 `StartDrag` 共用 | ReaderWindow.axaml.cs：OnViewportPointerPressed/StartDrag | 拖拽手感、预览栏点击 |
| 2.5-5 | **甩动限幅收敛**：触发阈值 120→150 px/s、速度限幅 ±3000（原 ±6000）；慢拖=松手即停，快甩=自然滑行 | ReaderWindow.axaml.cs：OnViewportPointerReleased + DragFlingThreshold/Max | 拖拽释放手感 |
| 2.5-6 | **跳页即时化**（预览跳页黑屏根治）：`ScrollWebtoonToPhysical` 补 `_webtoonSeq++`（作废旧范围在途解码，新范围立即解码不排队） | ReaderWindow.axaml.cs：ScrollWebtoonToPhysical | 预览栏/输入页码跳页 |
| 2.5-7 | **占位块渲染**（任何时刻不黑屏）：`UpdateWebtoonPages` 为"可见范围内未解码"的页放置灰底 `#2A2A34`+居中页码 Border（位置=前缀和），解码完成即撤换；范围移出/结构重建（进入/复位/尺寸变化）时清理 | ReaderWindow.axaml.cs：EnsureWebtoonPlaceholder/RemoveWebtoonPlaceholder/ClearWebtoonPlaceholders + UpdateWebtoonPages/LoadWebtoonImageAsync/RemoveWebtoonImage/ExitWebtoon/OnClosed | 虚拟化渲染、画布子元素 |
| 2.5-8 | **解码按显示宽度**：`GetPageBitmapAsync(physical, wantWidth)` 重载 + `DecodeAsync(physical, requestedWidth)` —— Webtoon 解码目标 = `_webtoonWidth×scale×dpr`（clamp 400..2048），不再用分页 2048+；真实书 4299px 页 fit 状态只解 ~540px → 解码提速 4-8 倍、内存同比例下降；放大按需重解码（复用既有机制） | ReaderViewModel.cs：DecodeAsync/GetPageBitmapAsync；ReaderWindow.axaml.cs：LoadWebtoonImageAsync | 解码缓存宽度语义、翻页/滚动性能 |
| 2.5-9 | 新增 v2 回归测试 4 项：重进必重设（防贴左放大）/Delta 抖动免疫（±小值不动、一档正常滚）/跳页即时定位且画面有图或占位（防黑屏）/空白区可拖 | ScrollModeFixesTests.cs | 测试基线 48→52 |

### 设计备忘（v2）
- 滚轮一档（120 delta）≈ 半屏滑动 + 缓慢停住（用户确认）；触摸板连续小 Delta 自然累积为平滑滚动
- 拖拽保持"内容跟随光标"（直接操纵，与移动端/看图器一致，用户确认）；甩动方向随拖拽
- 真实书诊断验证：重进 scale=1 居中、Delta 抖动画面不动、拖拽+甩动单调无回弹、跳页立即定位且有实例/占位

---

## 一·二十七、滚动模式 v3 修复（2026-08，发布版实测 6 问题）

> v2.5 发布后用户实测：滚轮无作用 / 键盘左右非上下 / 复位错位 / 适应按钮+百分比 / 黑页不恢复 / 放大模糊。
> 诊断关键实证：`Bitmap.DecodeToWidth` 对垃圾数据从不抛异常（返回占位图）→ 真正的解码失败只来自**流读取**（损坏 ZIP 条目）；
> 黑页根因为"解码失败创建空 Image 登记 → 永不重请求"；模糊根因为缓存宽度记录缺失 + 重解码被 uiRefs 守卫拒绝。

| # | 修复/改动 | 文件位置 | 可能影响的功能（风险点） |
|---|---|---|---|
| 2.6-1 | **滚轮改比例脉冲+死区**（滚轮无作用根治）：删"累积满 120 分步"→ `v += clamp(Delta×16, ±6000)`，`\|Delta\|<2` 死区，最小脉冲 150 兜底 —— 任何 Delta 大小立即滚动（弱 Delta 设备不再无效）、±1 抖动过滤不反弹、一档 120≈半屏滑行 | ReaderWindow.axaml.cs：ScrollWebtoonByEvent + WheelVelocityK/Deadband/MinImpulse | **滚动手感（核心）**、速度按钮 |
| 2.6-2 | **键盘 ↑↓**（问题 2）：滚动模式 ↑=上滚一屏/↓=下滚一屏；分页模式 ↑=上一页/↓=下一页（←→ 保留，看图器惯例） | ReaderWindow.axaml.cs：OnKeyDownTunnel/OnKeyDown | 键盘翻页 |
| 2.6-3 | **复位/适应屏幕统一 + 宽度变化清空重载**（问题 3 根治）：`FitWebtoonToPage` 重算条宽后先 `ClearWebtoonImages()`（含重置范围守卫）再重建 —— 旧尺寸图不再残留错位；`CycleFit`（适应屏幕按钮）在滚动模式 → 当前页适应+居中 | ReaderWindow.axaml.cs：FitWebtoonToPage/ClearWebtoonImages/PropertyChanged 处理器 | 复位/适应按钮、模式切换 |
| 2.6-4 | **百分比 fit=100%**（问题 4）：滚动模式 `ZoomPercent = scale×100`；`ZoomWebtoon`/`FitWebtoonToPage`/复位后均更新 | ReaderWindow.axaml.cs：UpdateZoomPercent/ZoomWebtoon | 缩放显示 |
| 2.6-5 | **解码失败不建空图+占位+限次重试**（问题 5 根治，实证）：`bmp==null`（流读取失败）→ 不创建空 Image（防登记后永不重请求 → 永久黑），保留占位块 + 限次重试（≤3 次、800ms 间隔，翻远重来重置计数）；`LoadSizes` 单页失败容错（损坏 CBZ 不再打不开） | ReaderWindow.axaml.cs：LoadWebtoonImageAsync/RetryWebtoonImageAsync；ReaderViewModel.cs：LoadSizes | 损坏书/坏条目健壮性 |
| 2.6-6 | **放大按需重解码（移除重建）**（问题 6 根治）：删 `DecodeAsync` 的"uiRefs 则拒绝重解码"守卫；`UpdateWebtoonPages` 对可见页检查"缓存宽度 < 显示需求×0.85"→ 移除旧图+占位+按新宽度重请求（移除重建而非原位替换 Source —— 原位替换会因渲染管线持旧引用崩溃）；分页模式缩放后当前页不足 → `RefreshCurrentPage` 重解码 | ReaderWindow.axaml.cs：TryRefreshWebtoonImage；ReaderViewModel.cs：DecodeAsync/HasCachedWidth/CurrentPageNeedsRedecode/RefreshCurrentPage | 位图生命周期、翻页/缩放性能 |
| 2.6-7 | **DecodeAsync 缓存宽度记录补齐**（连带发现）：原只在 Prefetch 记录宽度 → 放大清晰度/重解码判定失效，补齐 `_pageCacheWidth[physical]=want` | ReaderViewModel.cs：DecodeAsync | 缓存宽度语义 |
| 2.6-8 | **缓存淘汰保护显示中条目**（连带发现，崩溃级）：`EvictCache` 跳过仍被 UI 引用的条目（移到队尾，整轮显示中则停止）—— 否则显示中页的缓存条目被淘汰 → 放大刷新风暴 → "先淘汰后 Acquire"Dispose 刚解码位图 → 渲染崩溃；淘汰同步清理宽度记录 | ReaderViewModel.cs：EvictCache | **内存/崩溃安全** |
| 2.6-9 | **DecodeAsync 先 Acquire 再淘汰**（连带发现）：防"刚解码未引用即被淘汰 Dispose" | ReaderViewModel.cs：DecodeAsync | 位图生命周期 |
| 2.6-10 | **RemoveWebtoonImage 先移出画布再释放**（连带发现）：缓存条目已被淘汰时，先 Release 会 Dispose 仍被渲染树引用的位图 → 下一布局崩溃；改为先 `Children.Remove` 再 Release | ReaderWindow.axaml.cs：RemoveWebtoonImage | 位图生命周期 |
| 2.6-11 | **优化**：`UpdateWebtoonPages` 范围未变跳过重活（滚动 16ms/帧减负）；`_ctrlDown` 窗口失活复位（防滚轮误判缩放）；占位块提亮（`#3A3A44`+`#CCC` 更易辨识为加载中） | ReaderWindow.axaml.cs | 性能/健壮性/观感 |
| 2.6-12 | 新增 v3 回归测试 5 项：弱 Delta 滚轮有效+死区免疫 / 坏条目页占位不黑（真实 CRC 损坏 CBZ）/ 复位无旧宽度残留 / 放大后缓存宽度提升+百分比对应 / 键盘 ↑↓；更新滚轮测试为真实档位 Delta | ScrollModeFixesTests.cs | 测试基线 52→57 |

### 设计备忘（v3）
- 滚轮比例脉冲：任何 Delta 大小都有效（弱 Delta 设备根治）；死区 2 过滤 ±1 抖动；最小脉冲保证任一档位有可见滚动
- 放大清晰度用"移除重建"：复用既有加载/占位/作废机制，避免原位替换的渲染管线风险
- 黑页/模糊/崩溃全部溯源到位图生命周期：解码失败登记空图、缓存宽度缺失、淘汰风暴、释放顺序 —— 本批一并修复

---

## 一·二十八、v3.1 修复（2026-08-23，用户实测：滚轮无效 / 适应宽度语义）

| # | 修复/改动 | 文件位置 | 可能影响的功能（风险点） |
|---|---|---|---|
| 2.7-1 | **滚轮纯比例脉冲（无死区无下限）**：`v = Delta×16`，任何 Delta 大小都产生成比例滚动 —— 弱 Delta 设备（高分辨率滚轮/触摸板，每格 1~40）不再被死区过滤或压成不可见；±1 抖动只产生 ±16px/s 微小脉冲、正负相抵不反弹 | ReaderWindow.axaml.cs：ScrollWebtoonByEvent（删 WheelDeadband/WheelMinImpulse） | **滚轮（核心）** |
| 2.7-2 | **滚轮事件全量进 F12 日志**：`WHEEL_EVT mode/dy/ctrl`（事件是否到达）+ `WHEEL dy/v`（脉冲计算）+ `SCROLL ty/v`（周期 500ms，运动是否执行）—— 排查"事件未达/输入被滤/运动未动"三种可能 | ReaderWindow.axaml.cs：OnPointerWheelChanged/ScrollWebtoonByEvent/SmoothScrollTick | 调试可观测性 |
| 2.7-3 | **滚动模式"适应宽度"**（问题 1）：FitMode=FitWidth 时条宽 = 视口宽（铺满，与单页 FitWidth 一致）、当前可见页顶对齐视口顶、tx=0、scale=1；FitHeight/FitScreen 仍为当前页适应屏幕+居中 | ReaderWindow.axaml.cs：FitWebtoonWidth + PropertyChanged 处理器按 FitMode 分流 | 适应按钮循环、条宽语义 |
| 2.7-4 | 新增回归测试：适应宽度铺满（条宽=视口宽、tx=0、fit=100%）；更新滚轮抖动测试（±1 仅微小脉冲） | ScrollModeFixesTests.cs | 测试基线 56→57 |

### 设计备忘（v3.1）
- 死区/下限是"滚轮无效"的元凶：弱 Delta 设备每格 1~9 被死区全滤或压成 25px/事件不可见 —— 比例模型天然抗抖（±1 → ±2.6px/事件），无需死区
- F12 事件流现在能看到滚轮全链路（事件→脉冲→运动），后续滚轮问题可直接凭日志定位

---

## 二、v2.2c + v2.2b（2026-08-22，热区与按钮配色修复）

| # | 修复 | 文件位置 | 可能影响的功能 |
|---|---|---|---|
| 2.2c-1 | **右边缘黑边根因**：右侧热区 DockPanel.Dock="Right" 占 48px 布局挤压 Viewport 且露出深色背景 → 删除布局热区，改 Viewport PointerMoved 右缘 48px 检测（不占布局，画面不遮挡） | ReaderWindow.axaml.cs：OnViewportPointerMoved 右缘检测 + 删 DockPanel 热区 | 预览栏唤出、Viewport 布局 |
| 2.2c-2 | **白天按钮仍黑根因**：OnIsDarkChanged 未通知 ToolbarButton* 4 个配色属性 → 补通知 | ReaderViewModel.cs：OnIsDarkChanged | 主题切换后按钮配色 |
| 2.2b-1 | **热区透明 48px**（不占视觉、面积大）+ 鼠标一挪到立即唤出（去 400ms 等待） | ReaderWindow.axaml(.cs) | 预览栏唤出时机 |
| 2.2b-2 | **白天模式按钮文字黑色根因**：ContentPresenter 缺 `Foreground="{TemplateBinding Foreground}"` → 文字未继承按钮前景色 | ReaderWindow.axaml：Window.Styles 自定义按钮模板 | 所有工具栏按钮文字颜色 |

---

## 三、v2.2 预览栏移到右侧竖排（2026-08-22）

| # | 改动 | 文件位置 | 可能影响的功能 |
|---|---|---|---|
| 2.2-1 | 预览栏从底部移到**右侧竖排**（删底部热区，画面最大化） | ReaderWindow.axaml：PreviewStrip 改 DockPanel.Dock="Right"（v2.3 再改覆盖层） | 预览栏位置、底部布局 |
| 2.2-2 | 右侧 24px 热区悬停触发（v2.2b 改 48px 透明 + 立即） | ReaderWindow.axaml.cs | 唤出交互 |
| 2.2-3 | 竖排 7 张卡片（宽 96，高 = min(窗口高/7−间距, 130)，下限 80） | ReaderWindow.axaml.cs：ThumbStripItem/RefreshThumbStrip | 预览栏卡片尺寸 |
| 2.2-4 | ▲▼ 整排跳 ±7 页；滚轮一格一页（中心页 ±1 刷新） | ReaderWindow.axaml.cs：OnPreviewStripPrev/Next/Wheel | 预览栏翻页 |
| 2.2-5 | 占位图方案 A（灰块 + 居中页码，Image 加载成功后覆盖）；每张底部页码始终显示 | ReaderWindow.axaml：ThumbStrip DataTemplate | 加载中/失败显示 |
| 2.2-6 | 点击缩略图跳页（三模式映射：物理页 / 逻辑页 / 滚动定位 + 隐藏预览栏） | ReaderWindow.axaml.cs：OnThumbStripItemClick | 预览栏跳页 |

---

## 四、v2.1 预览栏重构（2026-08-22，性能与刷新修复）

> 根因：v2.0 缩略图复用正文大图解码（1200–4096px）→ 7 张同时解码风暴 → CPU/内存拉满 + 崩溃。

| # | 修复 | 文件位置 | 可能影响的功能 |
|---|---|---|---|
| 2.1-1 | **独立 150px 小图解码**（GetThumbnailAsync + _thumbCache，与正文大图缓存分离）→ 根治解码风暴 | ReaderViewModel.cs：GetThumbnailAsync/ThumbWidth | 预览栏缩略图、内存 |
| 2.1-2 | **ObservableCollection 自动刷新**（List 不刷新 → 修复"只有数字没图"） | ReaderWindow.axaml.cs：_thumbItems | 缩略图显示 |
| 2.1-3 | 热区 PointerEntered 触发（400ms 立即唤出，不再随移动重置） | ReaderWindow.axaml.cs | 唤出时机 |
| 2.1-4 | seq 防并发刷新（_thumbSeq 只认最新一次） | ReaderWindow.axaml.cs：RefreshThumbStrip | 快速操作时缩略图不花屏 |
| 2.1-5 | 滚轮/按钮事件修复 | ReaderWindow.axaml(.cs) | 预览栏翻页 |

---

## 五、v2.0 底部悬浮预览栏（2026-08-22，新功能）

| # | 改动 | 文件位置 | 可能影响的功能 |
|---|---|---|---|
| 2.0-1 | **进度条 + 缩略图融合**（删旧 Slider / Popup / 🖼 按钮），鼠标移到底部热区 400ms 唤出 | ReaderWindow.axaml(.cs) | 底部布局、跳页方式 |
| 2.0-2 | 7 张缩略图当前页居中 + 页码占位；◀▶ 整排换 ±7；滚轮一格一页；点击跳转（三模式映射）；滚动模式双向联动（正文滚动 ↔ 预览栏高亮） | ReaderWindow.axaml(.cs)：ThumbStrip/OnThumbStripItemClick/SyncPreviewStripToScroll | 预览栏全部交互 |
| 2.0-3 | 缩略图只加载可见 7 张（虚拟化） | ReaderWindow.axaml.cs：RefreshThumbStrip | 性能 |
| 2.0-4 | 预览栏方案文档（eb3c4d9 / a2bcd3e / 1a018d9） | docs/03-dev/preview-strip-*.md | — |

---

## 六、已知潜在风险点（用户检查时重点关注）

1. **覆盖层（2.3-1/2.3-5）影响面最大**：预览栏在 Viewport 内部叠加，所有 Viewport 指针交互需经守卫过滤。若发现：
   - 预览栏滚轮同时缩放 → 查 IsOverPreviewStrip 守卫（OnPointerWheelChanged）
   - 预览栏双击放大 → 查 OnViewportDoubleTapped 守卫
   - 预览栏左半悬停闪烁消失 → 查 OnViewportPointerMoved 隐藏分支（Bounds.Left - 10）
   - 预览栏唤出后图片位置跳动 → 查覆盖层 Grid 结构（PageLayer 不应受 PreviewStrip 影响）
2. **双页高度匹配（2.3-4）**：若发现双页两图高度仍不一致 → 查 LoadCurrentAsync 顺序 + Relayout 里 Ratio 使用
3. **主题色（2.3-2）**：切换主题后预览栏配色未变 → 查 OnIsDarkChanged 通知（PreviewStripBg/ItemBg/Label）
4. **书签隐藏（2.3-3）**：快捷键 B 仍可加书签（仅 UI 隐藏），后续恢复时删 IsVisible="False" 即可
5. **右侧竖排卡片尺寸（2.2-3）**：窗口高度极小时卡片下限 80px，极宽窗口卡片上限 130px

---

## 七、验证状态

- **v3.1（2.7）：57/57 自动化测试通过**（新增适应宽度铺满测试；滚轮纯比例脉冲）
- **v3（2.6）：56/56 自动化测试通过**（v3 新增 5 项：弱 Delta 滚轮/坏条目占位/复位无残留/放大刷新+百分比/键盘↑↓）；真实书诊断复验
- **v2.5：52/52 自动化测试通过**（v2 新增 4 项滚动回归：重进重设/Delta 抖动免疫/跳页即时不黑屏/空白区可拖）；真实书（38 页 4299×6071）headless 诊断复验：重进 scale=1 居中、Delta 抖动画面不动、拖拽+甩动单调无回弹、跳页立即定位且有实例/占位
- **v2.3：36/36 自动化测试通过**（新增 PreviewStripOverlayTests 覆盖层回归）
- v2.2/v2.2b/v2.2c：35/35 测试通过；真实书压力 4/4 零崩溃，内存稳定 ~300–420MB
- v2.1：35/35 测试通过；压力 4/4 零崩溃，内存稳定 ~400MB
- v2.0：35/35 测试通过；压力 3/3 零崩溃
- **发布版已更新：app/publish/MangaAgent.App.exe（v3，self-contained，冒烟启动验证通过）**

> 请用户真实应用验证预览栏交互（鼠标悬停/滚轮/点击/主题切换）与滚动模式手感（滚轮/拖拽/跳页/复位/放大清晰度），反馈问题现象，我会对照上表定位并修复。
