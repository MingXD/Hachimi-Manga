# BUG-007：单页模式跳页（物理页导航修复，2026-08-21）

- **状态**：已修复（v1.9）
- **优先级**：P0（基础阅读功能）

## 现象
单页模式下翻页：第1页 → 直接到第3页（跳过第2页）。

## 根因（历史溯源确认）
- PageLayouter 无论模式都按双页配对 (1,2)(3,4)… 生成逻辑页（M1 时代就有）。
- 单页模式 CurrentIndex 走逻辑页索引 → index1=(1,2) 显示物理第2页、index2=(3,4) 显示物理第4页 → 跳过物理第3页。
- 早期未暴露：测试与使用以双页为主；v1.8 默认 FitScreen 后单页翻页变频繁才被发现。

## 修复方案：物理页导航
- 引入 _currentPhysical（物理页为唯一导航基准）
- 单页模式：Next/Prev = 物理页 ±1（逐物理页）
- 双页模式：跳到下一/上一逻辑页起点（配对不变）
- 状态持久化：改存物理页（跨模式稳定）；书签也存物理页
- SetCurrentPhysical：物理页 → 同步 CurrentIndex（找包含它的逻辑页）
- PageText：单页显示物理页号（1/300）、双页显示逻辑页号（1/151）

## 涉及文件
- ReaderViewModel.cs：导航/状态/书签/PageText/LoadCurrentAsync
- ReaderWindow.axaml.cs：进度条按物理页映射、SyncJumpSlider
- 测试：ReaderE2eTests/QuickJumpTests 断言更新

## 验证
- 32/32 测试通过；真实书压力 4/4 零崩溃
