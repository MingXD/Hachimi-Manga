# BUG-002：封面图只加载前面少数 — 已修复 ✅

- **状态**：已修复并验证（2026-02）
- **优先级**：中

## 现象
封面墙只有前 ~24 本显示封面，后面大部分空白。

## 原因（确认）
`MainWindowViewModel` 只对 `Books.Take(24)` 调用缩略图加载。

## 修复
滚动按需加载：`MainWindow` 的 `ScrollChanged` 事件 → 用固定卡片尺寸（184×262）按索引
估算可见行范围（±300px 缓冲）→ 对可见区的 `BookItemViewModel.LoadThumbnailAsync()`。
缩略图本身有 WeakReference 缓存 + DecodeToWidth(260) 降采样，重复滚动零成本。

## 验证（实测）
- 生成 60 本测试库 → 真实应用启动：第一屏 light%=39.7%（封面渲染）
- `SendInput` 模拟真实滚轮滚动到底：视图变化（截图不同），新区域 light%=34.5%（新封面按需加载成功）
- 测试 18/18 通过

## 说明
- WrapPanel 非虚拟化（容器全建、缩略图懒加载）；大库（数千本）性能优化留待 M3
  （ItemsRepeater + UniformGridLayout 虚拟化）。
