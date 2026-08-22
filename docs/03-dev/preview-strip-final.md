# 底部悬浮预览栏——最终方案（2026-08-22 v2）

> 用户已确认方向，本版为实施前最终定稿。

## 一、布局总览

平时底部无任何东西；鼠标移到底部热区(24px)停留400ms → 半透明预览栏滑入；移出300ms滑出。

   [◀]  [P12][P13][P14][★P15][P16][P17][P18]  [▶]
        (每张: 缩略图 + 下方页码, 间距8px)

## 二、缩略图尺寸设计（自适应窗口/分辨率/模式）

### 核心公式
- 每排固定 7 张，间距固定 8px
- 可用宽度 = 窗口宽 - 边距(左16+右16) - 左右按钮区(各40) - 间距(7张x8=56)
- 单张宽度 = min(可用宽度/7, 150px)，下限 80px
- 高度 = 宽度 / 页平均比例(0.7)，上限 214px(150/0.7)，下限 114px

### 三模式差异
| 模式 | 缩略图内容 | 尺寸 |
|---|---|---|
| 单页 | 物理页 ±3 (7张) | 标准: w x (w/0.7) |
| 双页 | 逻辑页 ±3 (7张, 每张=一对) | 宽图: w x (w/1.4) 即两页并排, 更宽更扁 |
| 滚动 | 物理页 ±3 (7张) | 标准: 同单页 |

### 分辨率适配
- 解码目标宽度 = 单张显示宽 x 2 (适配高DPI) + 余量 → ~200-320px
- 窗口拉大 → 单张变宽 → 解码目标变高（按需重新解码更大）
- 窗口缩小 → 单张变窄 → 直接缩放显示已有缩略图（不重新解码）

## 三、交互

- 点击缩略图 → 跳转到该页（单页:物理页 / 双页:逻辑页 / 滚动:滚动定位）
- 左右 ◀ ▶ 按钮 → 整排换（±7 页）
- 滚轮在预览栏上 → **一格前进一页**（细粒度）
- 滚动模式双向联动：正文滚动→高亮跟手；预览操作→正文跳转
- 拖动扫描：**暂不做**（后续版本）

## 四、性能

- 只解码可见 7 张（虚拟化：整排换时增量加载）
- 小图解码（~200-320px）快，缓存复用
- 加载中/失败 → 显示页码占位（不白框）

## 五、三模式跳转映射

| 模式 | 点击跳转实现 |
|---|---|
| 单页 | vm.JumpToPhysicalCommand(物理页) |
| 双页 | vm.JumpToPageCommand(逻辑页) |
| 滚动 | vm.JumpToPhysicalCommand + ScrollWebtoonToPhysical |

## 六、实施范围

1. ReaderWindow.axaml：删 JumpSlider + ThumbnailPopup + 底部进度条；加底部热区 + 悬浮预览栏（Grid: ◀ 7缩略图 ▶）
2. ReaderWindow.axaml.cs：热区悬停检测(Timer)、预览栏显示/隐藏、缩略图加载(7张虚拟化)、翻页(按钮/滚轮)、点击跳转、模式切换刷新
3. ReaderWindow：新增 ThumbStripItem 模型(页码+位图+状态)
4. ThumbnailService：修缩略图解码(排查只有框没图) + 按需尺寸解码 API
5. 三模式跳转映射 + 滚动联动
6. 测试：悬停唤出/隐藏、缩略图加载、点击跳转、按钮/滚轮翻页、三模式

## 七、删除项

- 底部常驻进度条(Slider) — 由预览栏取代
- ThumbnailPopup — 由预览栏取代
- 工具栏"🖼 缩略图"按钮 — 由悬停唤出取代
- 进度条相关的 OnJumpSliderValueChanged/SyncJumpSlider 逻辑

---

## 八、实施后现状（v2.3，2026-08-22）—— 与方案的差异

> 本文档为 v2 底部方案定稿；实际实施历经 v2.0（底部）→ v2.1（重构）→ v2.2（右侧竖排）→ v2.3（覆盖层），现按 v2.3 为准。

### 实际布局（覆盖层）
- 预览栏为 **Viewport 内部覆盖层**（不再占任何布局）：Viewport 内容包一层 Grid，PageLayer 与 PreviewStrip 叠加；PreviewStrip 宽 120、HorizontalAlignment=Right
- **不推挤图片**：显示时图片不被左移/压缩，仅遮挡右侧 120px（用户已确认可接受）
- 唤出：鼠标移到 Viewport 右缘 **48px** 内立即唤出（v2.2c 删布局热区，改 PointerMoved 检测）
- 隐藏：鼠标离开预览区（左移越过 PreviewStrip.Bounds.Left−10）或移出 → 300ms 延迟隐藏

### 实际内容与交互（对照原方案差异）
| 原方案（本文档） | 实际（v2.3） |
|---|---|
| 底部横排 7 张 | **右侧竖排 7 张**（宽 96，高 = min(窗口高/7−间距, 130)，下限 80） |
| 卡片含缩略图+下方页码 | 占位灰块+居中页码，150px 小图加载后覆盖；**每张底部页码常显** |
| 左右 ◀ ▶ 整排换 ±7 | 上下 ▲▼ 整排换 ±7（竖排） |
| 滚轮一格一页 | 同（中心页 ±1 刷新） |
| 点击跳转（三模式映射） | 同：物理页 / 逻辑页 / 滚动定位（Webtoon 附加 ScrollWebtoonToPhysical），点击后隐藏预览栏 |
| 滚动模式双向联动 | 同（SyncPreviewStripToScroll） |
| 半透明背景 | 绑定主题色：夜 #CC1C1C24 / 昼 #CCFFFFFF（均半透明）；条目底色/页码同主题 |
| 拖动扫描 | 仍暂不做 |

### 覆盖层方案的技术要点（v2.3 新增）
- PreviewStrip 移入 Viewport 后，其上的指针事件会冒泡到 Viewport 处理器（Avalonia XAML 处理器默认忽略 Handled）→ 需守卫：
  - 滚轮：OnPointerWheelChanged 开头 `if (IsOverPreviewStrip(e.Source)) return`（防"翻页+缩放"同时）
  - 双击：OnViewportDoubleTapped 同守卫（防预览栏内双击缩放）
  - 左半悬停：隐藏条件用 `edgePos.X < PreviewStrip.Bounds.Left - 10`（防闪烁）
- `IsOverPreviewStrip`：沿 `GetVisualParent()` 遍历可视树判断事件源是否在预览栏内（Avalonia 12：`VisualParent` 为 internal，须用扩展方法）

### 验证
- 36/36 测试通过（v2.3 新增 PreviewStripOverlayTests：覆盖层结构/不占 DockPanel/显隐不改 Viewport 尺寸/主题色切换）
- 完整演进与风险点见 [v2-changes-record.md](v2-changes-record.md)
