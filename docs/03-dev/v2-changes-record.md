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

- **v2.3：36/36 自动化测试通过**（新增 PreviewStripOverlayTests 覆盖层回归）
- v2.2/v2.2b/v2.2c：35/35 测试通过；真实书压力 4/4 零崩溃，内存稳定 ~300–420MB
- v2.1：35/35 测试通过；压力 4/4 零崩溃，内存稳定 ~400MB
- v2.0：35/35 测试通过；压力 3/3 零崩溃
- 发布版已更新：app/publish/MangaAgent.App.exe（v2.3）

> 请用户真实应用验证预览栏交互（鼠标悬停/滚轮/点击/主题切换），反馈问题现象，我会对照上表定位并修复。
