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

## 4. 验收标准（P0）
- [ ] 打开书默认 FitWidth+居中，恢复上次页/模式
- [ ] FitWidth/Height/Screen 切换正确；Ctrl+滚轮以光标为中心缩放
- [ ] 拖拽：未放大时水平拖翻页（RTL 方向正确）；放大时拖拽平移
- [ ] 双页配对 1-2/2-3 切换，跨页图配对正确
- [ ] 测试通过 + 真实应用截屏验证
