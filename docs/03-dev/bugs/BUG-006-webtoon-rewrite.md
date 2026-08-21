# BUG-006：滚动模式专项问题（2026-08-20，v1.8 重构）

- **状态**：已修复（v1.8 滚动专项重构）
- **优先级**：高（滚动模式"完全不可用"）

## 问题清单与根因
1. **滚轮卡顿**（W-1）：每次滚轮事件触发虚拟化全量增删 + 异步解码风暴
   → 修复：解码限流（SemaphoreSlim 2）+ 范围不变不重建 + 滚动速度按钮
2. **Ctrl+滚轮不锚定焦点 + 同时滚动**（W-2）：ContentScreenRect 返回单页矩形，Webtoon 画布是
   总高拼接 → EffectiveFocus 把鼠标 clamp 到单页 → 焦点算错
   → 修复：ZoomWebtoon 用鼠标原始位置锚定；事件路由明确（Ctrl 只缩放）
3. **图片宽度不统一**（W-3）：每页 Width=ratio×BaseH → 拼接参差
   → 修复：等宽（基准=进入时视口宽），页高=宽/ratio，页位置用前缀和（_webtoonOffsets + 二分）
4. **滚动黑屏**（W-4）：先清后建异步空档 + 范围计算早退
   → 修复：先加后删（远离范围缓冲×2 才移除）+ 前缀和稳定定位

## 设计变更
- 等宽：所有页 Width=WebtoonWidth（进入时视口宽），Height=WebtoonWidth/ratio_i
- 前缀和：_webtoonOffsets[]（页 i 顶部 y = Σ前 i 页高），PhysicalAt 二分定位
- EnterWebtoon 幂等（视口尺寸变化重建基准、保持滚动位置）
- Relayout 在 Webtoon 模式不重算 scale（由 EnterWebtoon/ZoomWebtoon 管理）

## 验证
- WebtoonVirtualizationTests 更新（等宽语义）
- 31/31 通过；真实书压力 4/4 零崩溃

## v1.9 追加（2026-08-21）
- BUG-F：进度条跳页 → ScrollWebtoonToPhysical（视图滚动定位，漫画实际移动）
- BUG-G：缩放按钮以视口中心为焦点（ZoomWebtoon 统一入口，不再附带上下移动）
- BUG-H：复位 = 宽度 50% + 画面平移到当前页中央（不跳页）
- BUG-I：统一缩放入口（按钮/Ctrl 滚轮都走 ZoomWebtoon），修复缩放后黑屏
- 功能-2：平滑滚动 + 惯性（DispatcherTimer 帧循环 + 指数衰减，速度按钮联动）
- 功能-3：默认宽度 = 视口 50%
