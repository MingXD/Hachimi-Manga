# 阅读体验优化设计 v1（Reader Experience v2）

> 状态：设计定稿，分步实施中（每步 commit 留痕）。
> 参考开源核心逻辑：YACReader（缩放 fit 模式/以光标为中心缩放/拖拽平移/Ctrl+滚轮缩放）、
> CDisplayEx（滚轮翻页/键盘导航）、Perfect Viewer（滑动翻页/点击区）、Mihon（RTL 方向/缩放焦点）、
> Panels（双页分组手动调整 Group from this page on、Webtoon 连续滚动）。

## 0. 修复历程回顾与改动评估（2026-02）

| 提交 | 改动 | 评估 | 结论 |
|---|---|---|---|
| 3042466 | M0.5 PoC 领域层 | 基础设施 | ✅ 保留 |
| b6c1b11 | 同步 GetPageSize（UI 死锁修复） | 真 bug 修复 | ✅ 保留 |
| b6c1b11 | xunit v3 + Avalonia 12 API 适配 | 依赖要求 | ✅ 保留 |
| f57cece | LRU 页面缓存 + 解码失败提示 | 内存控制/可诊断性 | ✅ 保留 |
| f57cece | 封面懒加载（滚动可见区） | BUG-002 修复 | ✅ 保留 |
| 421da3f | DecodeToWidth 降采样 + 竞态防护 + JPEG 头解析 64KB | 4K 性能/正确性 | ✅ 保留；**副作用：放大超解码宽度会糊 → P1 按需高清重解码解决** |
| 7fc088a | Image Source 绑定（最终根因） | BUG-001 修复 | ✅ 保留 |
| 其余 | 诊断测试/夹具/文档 | 测试资产 | ✅ 保留 |

**结论：无需还原任何改动**；唯一待处理副作用（放大模糊）纳入本次迭代 P1。

## 1. 功能清单与优先级排期

### P0 — 基础阅读体验正确性（本次迭代，最高优先）
| # | 功能 | 现状 | 目标 |
|---|---|---|---|
| P0-1 | **居中显示** | 页面左对齐 | 单页/双页水平垂直居中 |
| P0-2 | **缩放模式** | 单一倍率 | Fit Width / Fit Height / Fit Screen / 自定义，**以光标为中心缩放** |
| P0-3 | **双页配对起点可设置** | 固定封面单页后 (1,2) | 可切换 (1,2) 相接 ↔ (2,3) 相接（跨页图配对） |
| P0-4 | **鼠标拖拽** | 无 | 分页模式：拖拽翻页（RTL 感知）；缩放时：拖拽平移 |
| P0-5 | **默认显示正确** | Fit Width | 打开书即 Fit Width + 居中 + 恢复上次位置 |

### P1 — 紧随（下一次迭代）
| # | 功能 | 说明 |
|---|---|---|
| P1-1 | 连续滚动（Webtoon）模式 | 垂直连续、懒加载、滚轮滚动 |
| P1-2 | 按需高清重解码 | 缩放超过解码宽度时重新解码更高分辨率（解决降采样糊） |

### P2 — 后续（M2/M3）
| # | 功能 | 说明 |
|---|---|---|
| P2-1 | 点击区翻页预设 | 左/右 1/3 点击翻页（Mihon tap zones） |
| P2-2 | 每书设置记忆增强 | 缩放/配对偏移/模式持久化 |
| P2-3 | 翻页动画 | 淡入/滑动（可关） |

## 1.1 基础阅读操作（参考开源 Windows 阅读器 —— 用户反馈修正 2026-02）

调研结论（CDisplayEx / YACReader / MComix 等 Windows 桌面阅读器共识）：
- **滚轮 = 翻页**（分页模式）：向下滚=下一页，向上滚=上一页；不是滚动页面
- **Ctrl+滚轮 = 缩放**（YACReader 约定）
- **鼠标拖拽 = 画面平移**（按住左键拖动移动画面；内容溢出方向生效）
- 键盘翻页：←/→、PageUp/PageDown、空格
- 翻页与垂直查看的关系：FitWidth 时页面下部超出视口 → 用**拖拽平移**查看；
  也可切 FitScreen/FitHeight 整页显示。滚轮始终翻页（CDisplayEx 经典组合）

**修正（相对上一版实现）—— 2026-02 已实施**：
1. 滚轮：分页模式非 Ctrl 滚轮 → 翻页（headless 输入模拟验证通过）；Ctrl+滚轮 → 缩放
2. 拖拽：改为**纯平移**（PanOffset 纯函数，用 ScrollViewer.Extent 计算可滚动范围；
   修复了 Panel 被 Stretch 到视口宽导致无法水平滚动的坑）；移除"未放大拖拽翻页"
3. 翻页方式：滚轮、键盘、按钮（P2 加点击区）
4. 验证：21/21 测试通过（含 MouseWheel 翻页 headless 模拟 + PanOffset 单测）；
   真实拖拽交互请用户实测（headless ScrollViewer 交互模拟不可靠，已记录）

## 2. 技术方案

### P0-1 居中显示
- Relayout 中计算内容尺寸后：若内容宽 < 视口宽 → 左右居中（`StackPanel.HorizontalAlignment=Center` 或手动 Margin）；
  垂直同理（fit height 时）。
- 实现位置：`ReaderWindow.Relayout()` 统一计算内容矩形的 offset。

### P0-2 缩放模式 + 光标中心缩放
- `FitMode` 枚举（FitWidth/FitHeight/FitScreen/Custom）进 ReaderViewModel，默认 FitWidth。
- 基础缩放：fitW = vpW/imgW，fitH = vpH/imgH，fitScreen = min。
- **以光标为中心缩放**（参照 YACReader Ctrl+滚轮 + 光标锚定）：
  1. 记录缩放前光标在**内容坐标系**的位置 `c`；
  2. 缩放后滚动偏移补偿：`offset' = c - (c - offset) * (scale'/scale)`（ScrollViewer.Offset 调整）。
- 滚轮语义（分页模式）：滚轮=翻页，**Ctrl+滚轮=缩放**（YACReader 约定）；缩放中平移跟随。

### P0-3 双页配对起点（PageLayouter 扩展）
- `PageLayoutOptions.FirstPairOffset`（0 或 1，默认 0）：
  - 0：封面单页后 (1,2),(3,4)…（原作 1、2 相接）
  - 1：封面与第 2 页均单页，(2,3),(4,5)…（原作 2、3 相接，封面是独立单页）
- 纯函数修改 + 单测覆盖两种模式。
- UI：工具栏按钮循环 0↔1（显示 "配对:1-2 / 2-3"）。

### P0-4 鼠标拖拽
- 事件：PointerPressed / PointerMoved / PointerReleased（ReaderWindow）。
- 逻辑（参照 Perfect Viewer 滑动翻页 + 缩放平移两态）：
  - 内容 ≤ 视口（未放大）：水平拖拽超阈值（60px）→ 翻页（RTL：向左拖=下一页；LTR 反之）；短按/小位移 = 无操作。
  - 内容 > 视口（放大）：拖拽 = 平移（实时更新 ScrollViewer.Offset），松开不翻页。
- 与滚轮/键盘共存；翻页动画【P2】。

### P0-5 默认显示
- 打开书：FitWidth + 居中（Relayout 首帧）＋恢复 saved 页/模式/方向（已有）。

### P1-1 连续滚动（Webtoon）
- `ViewMode` 扩展为 Single/Double/Webtoon（M 键循环）。
- Webtoon：垂直 StackPanel + 可见页懒加载（复用封面墙可见性计算思路）+ 滚轮滚动 + 记忆 scroll offset。
- PageLayouter 不参与（物理页直排）。

### P1-2 按需高清重解码
- 解码宽度 = f(scale)：scale 超过当前解码宽度/原始宽度比时，按 `min(原图宽, decodeWidth×2)` 重新解码；
  LRU 缓存键 = (物理页, 尺寸档)。

## 3. 实施顺序与留痕
1. P0-5+P0-1 居中与默认显示（ReaderWindow.Relayout 重构）
2. P0-2 缩放模式 + 光标中心缩放
3. P0-4 鼠标拖拽
4. P0-3 双页配对起点（Core + UI）
5. 每步：dotnet test + 真实应用验证 + commit（`feat: reader-…`）+ 本文件更新进度
6. P1 另起迭代

## 3.1 实施进度（2026-02）
- [x] P0-5+P0-1 居中与默认显示（Relayout 重构：Fit 模式 + HorizontalContentAlignment=Center + 垂直居中）
- [x] P0-2 缩放模式（FitWidth/Height/Screen/Custom）+ Ctrl+滚轮以光标为中心缩放
- [x] P0-4 鼠标拖拽（未放大水平拖=翻页 RTL 感知；放大水平拖=平移）
- [x] P0-3 双页配对起点（PageLayouter FirstPairOffset + 工具栏切换 1-2/2-3）
- 测试 19/19 通过；真实应用实测：夹具书 light 82.8%、真实书 light 32.8%（渲染正常）
- ⚠️ 过程问题：ReaderViewModel 属性改名（ScaleText→FitText）未同步 XAML 导致 Avalonia XAML 编译失败
  → 应用启动崩溃（No precompiled XAML）。已修复；教训：改 VM 属性需同步 XAML，清理 obj 重建可暴露此类错误。

## 4. 验收标准（P0）
- [x] 打开书默认 FitWidth+居中，恢复上次页/模式
- [x] FitWidth/Height/Screen 切换正确；Ctrl+滚轮以光标为中心缩放（代码实现，交互待用户实测）
- [x] 拖拽：未放大时水平拖翻页（RTL 方向正确）；放大时拖拽平移（代码实现，交互待用户实测）
- [x] 双页配对 1-2/2-3 切换（PageLayouterTests 覆盖）
- [x] 测试 19/19 + 真实应用截屏验证
