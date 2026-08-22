# 滚动模式修复方案（黑屏 / 惯性回弹 / 复位与默认视图）

> 状态：**已实施（2026-08），48/48 自动化测试通过**。
> 决策记录：①默认宽度=当前页完全可见+居中（与单页"适应屏幕"一致）②复位=当前页适应屏幕居中+回默认缩放 ③滚动模型=纯动量滑行（连滚累积速度）④拖拽释放要甩动惯性 ⑤A+B+C+D 一次做完。
> 实施变更记录见 [v2-changes-record.md](v2-changes-record.md) 一·二十五（2.4-1 ~ 2.4-11，含连带修复的 DecodeAsync 引用计数下溢 bug）。
> 对应文件：`app/src/MangaAgent.App/Views/ReaderWindow.axaml.cs`（画布/Webtoon/滚动）、`app/src/MangaAgent.App/ViewModels/ReaderViewModel.cs`（跳页/解码）。
> 相关历史：BUG-006（v1.8 Webtoon 重构）、v1.9 功能-2/功能-3（平滑滚动+惯性、默认 50% 宽）。

---

## 一、问题整理（用户反馈，2026-08）

| # | 问题 | 现象 |
|---|---|---|
| 1 | **滚动模式黑屏** | 进入滚动模式（含页面跳转后、预览栏跳转后）画面全黑，鼠标点击无反应；**滚轮滚动一下画面才出现** |
| 2 | **页码跳转无效** | 输入页码回车后，指定页没有跳过去（画面不动） |
| 3 | **惯性方向错误/往回弹** | 往下滚，画面惯性往**上**弹回；拖拽也把画面往回拉；期望像手机长页面：滚下去后缓慢减速停住，画面连续滑动不反弹 |
| 4 | **复位/默认视图不明确** | 期望像单页一样：**当前页适应屏幕并居中**作为默认位置；双击进入漫画后的封面页也应如此 |

---

## 二、根因分析

### 问题 1a：黑屏 —— 解码任务被"作废+去重"双重机制丢掉，画布永远为空

**代码路径（进入滚动模式时）：**

1. 切到 Webtoon → `ReaderViewModel.OnViewModeChanged` → `LoadCurrentAsync()`（分页解码流程，Webtoon 模式下**仍然运行**）→ 设置 `Image1/Image2/IsDoublePageShown`。
2. 这些属性变化触发窗口 `PropertyChanged` 处理器 → `Relayout()` → `EnterWebtoon()`（ReaderWindow.axaml.cs:326）。
3. `EnterWebtoon()` 末尾**无条件 `_webtoonSeq++`**（:545）→ 使所有在途的 `LoadWebtoonImageAsync` 任务"过期"。
4. `LoadWebtoonImageAsync` 解码完成后检查 `mySeq != _webtoonSeq`（:669）→ **位图直接 Release 丢弃，不加入画布**。
5. 同时 `_webtoonLoading` 去重集合（:660 `if (!_webtoonLoading.Add(physical)) return`）保证**同一页在途期间不会再次请求**；任务作废后**没有任何机制重新发起请求**。
6. 结果：`EnterWebtoon` 第一次 `UpdateWebtoonPages` 发起的请求（含当前页），在途期间被后续 `Image1/Image2` 属性触发的第二次、第三次 `Relayout→EnterWebtoon→seq++` 连续作废；作废后无人补请求 → 画布（Canvas）**空** → 全黑。
7. 滚轮滚动 → `SmoothScrollTick → UpdateWebtoonPages`（:769）→ 此时在途任务已全部结束（`_webtoonLoading` 已清空）→ 以**最新 seq** 重新请求 → 画面出现。这就是"滚轮滚一下才出现"。

**预览栏跳转/翻页后的黑屏同理**：`JumpToPhysicalCommand`（改 VM 状态）→ `LoadCurrentAsync` 异步解码 → 完成后 `Image1` 属性变化 → `Relayout → EnterWebtoon → seq++`；而 `ScrollWebtoonToPhysical`（视图滚动）在命令后**同步**执行、其 `UpdateWebtoonPages` 刚发起的请求仍在途 → 被作废 → 跳到的位置黑屏。

**为何必然触发**：解码大图需要几百 ms（1200–4096px），而属性变化链（IsDoublePageShown→Image1→Image2）在解码完成前就跑完 → 竞态窗口大。测试未发现是因为 `WebtoonVirtualizationTests` 只断言"滚动后画布非空"（滚动本身会重新请求），没覆盖"进入即非空"。

**次要因素**：Webtoon 模式下 `LoadCurrentAsync` 纯属浪费（解码的 `Image1/Image2` 根本不在 Webtoon 画布显示，只用于触发上述作废链）。

### 问题 1b：页码跳转无效 —— 跳页对话框只改状态、不动视图

- `ShowJumpDialog`（:1259）：Webtoon 模式只执行 `_vm.JumpToPhysicalCommand.Execute(p-1)`（改 `CurrentIndex/CurrentPhysical` + `LoadCurrentAsync`），**没有调用 `ScrollWebtoonToPhysical`**（移动 `_ty`）。
- 对比预览栏点击（:1131-1132）是"命令 + `ScrollWebtoonToPhysical`"两步都做。
- 所以对话框跳页后：页码文本变了、VM 状态变了，**画面纹丝不动** → "跳转无效"。
- 附带问题：对话框里"当前页"计算用 `(-_ty) / (BaseH*_scale)`（:1239）——`BaseH=800` 是分页基准高，Webtoon 每页高度各不相同（`宽/ratio`），显示的第几页是错的。

### 问题 2：惯性方向错误 / 往回弹 —— 两处数学错误叠加

**根因 2a：`_scrollTargetTy`（滚动目标）与 `_ty`（实际位置）失同步**

`_scrollTargetTy` 只在滚轮（:725）和键盘（:1352）里累加；而以下入口**直接改 `_ty` 但从不更新目标**：
- `EnterWebtoon` 首入定位（:531 `_ty = -(PageOffset(firstPhys) * _scale)`）
- `ScrollWebtoonToPhysical`（:704）
- `ResetWebtoonView`（:968）
- `ZoomWebtoon`（:801-802）
- 鼠标拖拽（:862）

首入时 `_scrollTargetTy` 仍是初始值 0。若书从中间页进入（续读恢复/预览栏跳页后），`_ty` 在书中部（如 -78000），目标却是 0 → 之后第一次滚轮：`remaining = target - ty` 为**巨大正值** → 平滑分支 `move = remaining*0.25` 每帧把画面往**顶部回拉 ~19000px**（数值模拟已复现）→ "往下滚，惯性往上弹"。

**根因 2b：帧循环的 `elapsed` 自启动起累积、不逐帧重置**

`SmoothScrollTick`（:758）用 `_scrollWatch.Elapsed.TotalSeconds` 作为本帧时间差，但 `_scrollWatch` 只在 `StartSmoothScroll` 时 Restart 一次（:738）→ `elapsed` 随帧数递增：
- 速度项 `_scrollVelocity * elapsed * 10`（:763）随帧数**放大**；
- 衰减项 `Math.Pow(0.90, elapsed*60)`（:765）随帧数**加速衰减**。

两者叠加 → 速度项在速度已快衰减完时反而最大 → **过冲**。数值模拟（目标 -150，初速 -90，60fps）：冲至 **-205** 再反向滑回 -150 → "滚下去又弹回来"的橡皮筋感；最后分支还把 `_ty` 硬拉到 `_scrollTargetTy`（:783，snap）再跳一次。

**根因 2c：拖拽与残余平滑滚动互搏 + 无甩动惯性**

- 拖拽直接改 `_ty`（:861-862），若此前平滑滚动未停，`_scrollTargetTy`/`_scrollVelocity` 残余继续在 Tick 里推 `_ty` → "拖着，画面被往回拉"。
- 拖拽释放不产生惯性（从未记录拖拽速度）—— 与用户期望的"拖一下自然滑停"不符。
- 边缘 `ClampWebtoonScroll`（:810）硬 clamp → 到边界弹簧式回弹。

**期望行为（用户描述=手机长页面逻辑）**：滚轮向下 → 内容向下连续滑动 → 速度指数衰减 → 缓慢停住（惯性方向与滚动方向一致）；拖拽释放后也自然滑行减速；到边界即停、不反弹。

### 问题 3：复位/默认视图 —— 与单页语义不一致

| 场景 | 现状 | 用户期望 |
|---|---|---|
| 进入滚动模式 | 宽度 = 视口 50%（v1.9 功能-3），当前页**顶部**对齐视口顶（:531） | 当前页**适应屏幕 + 居中**（像单页 FitScreen） |
| 双击进入（封面页） | 同上（首入第 0 页即封面） | 封面页适应屏幕 + 居中 |
| 复位按钮 | `ResetWebtoonView`：宽度 50% + 当前可见页垂直居中（:955） | 与进入默认一致：当前页适应屏幕 + 居中 |

即：滚动模式缺一个"与单页 FitScreen 对齐"的默认视图定义。

---

## 三、修复方案（按工作流拆分，均含回归测试）

### 工作流 A：黑屏根治 + 跳页修复（问题 1）

1. **Webtoon 模式跳过分页解码链**：`LoadCurrentAsync` 开头在 `ViewMode == Webtoon` 时直接返回（Webtoon 的页由 `UpdateWebtoonPages` 管理，`PageText` 由 `UpdateWebtoonPageText` 维护）→ 消除 `Image1/Image2` 属性链触发的 `Relayout→EnterWebtoon→seq++` 作废风暴（顺带省掉无谓解码）。
2. **`EnterWebtoon` 幂等化**：`_webtoonSeq++` 只保留在真正结构变化的两次（首次进入 / 视口尺寸重建）分支里；无变化的重复 `Relayout` 调用不再作废在途任务。
3. **作废补偿（兜底）**：`LoadWebtoonImageAsync` 因 seq 过期丢弃位图后，若该页仍处于当前可见范围 → 从 `_webtoonLoading` 移除并重新发起请求（或直接再调一次 `UpdateWebtoonPages`）。保证任何情况下"可见页必有请求落地"。
4. **（可选加固）占位渲染**：请求期间在 Canvas 放灰块 + 页码占位（复用预览栏占位方案），解码完成覆盖 → 加载慢也不黑屏。
5. **跳页对话框补视图滚动**：`ShowJumpDialog` Webtoon 分支 = `JumpToPhysicalCommand` + `ScrollWebtoonToPhysical`（与预览栏一致）；"当前页"计算改用 `PhysicalAt((-_ty)/_scale)` 与滚动状态一致。

### 工作流 B：惯性/平滑滚动重写（问题 2）

6. **统一状态**：删除"目标-位置"双轨模型，改为**纯动量模型**（手机式）：
   - 每帧 `dt` = 本帧距上帧时间（每帧重置计时，clamp ≤ 50ms 防卡顿跳变）；
   - 滚轮每格：施加速度脉冲 `v += dir × step`（step 由 WebtoonSpeed 缩放），不设硬目标；
   - 每帧：`ty += v × dt`；`v ×= exp(-c × dt)`（固定摩擦系数，自然滑停）；边界 `ClampWebtoonScroll`（到边即停，速度清零，不反弹）；
   - 停止条件：`|v| < 阈值` 且已到达目标/边界。
   - 键盘翻页（PageUp/Down/←→）同样施加速度脉冲（一屏高）。
7. **拖拽**：拖拽开始**取消**残余平滑滚动（清速度/停 timer）；拖拽中直接改 `_ty`（现状）；释放时若拖拽速度超阈值 → 以该速度启动动量滑行（甩动惯性，方向与拖拽一致）。
8. 消除 `_scrollTargetTy` 字段（或任何直接改 `_ty` 的入口同步重置目标）→ 根因 2a 从设计上消失。

### 工作流 C：默认视图 / 复位统一（问题 3）

9. **新默认视图函数 `FitWebtoonToPage()`**（进入与复位共用）：
   - `_webtoonWidth = min(视口宽, 当前页高 × ratio_当前页)` → 当前页**完全可见**（= 单页 FitScreen 语义）；
   - 水平居中：`_tx = (视口宽 − _webtoonWidth) / 2`；
   - 垂直居中：`_ty = −(页顶 + 页高/2 − 视口高/2)`，再 `ClampWebtoonScroll`；
   - `EnterWebtoon` 首入调用它（当前页 = `CurrentFirstPhysical`，封面即第 0 页）→ 双击进入自动满足；
   - `ResetWebtoonView` 改为调用它（"复位 = 回到进入时的默认视图"，语义明确）。
10. 缩放（Ctrl+滚轮/按钮）行为不变（锚定鼠标/视口中心）；缩放后不再自动回居中。

### 工作流 D：回归测试

| 测试 | 断言 |
|---|---|
| 进入 Webtoon 画布非空 | 切到 Webtoon 后等待解码，`WebtoonInstancedCount ≥ 1` 且覆盖当前可见页（防黑屏回归） |
| 快速切模式+跳页竞态 | 切 Webtoon → 立刻预览栏跳页 → 画布最终非空且目标页在实例化范围内 |
| 跳页对话框滚动 | 模拟跳页命令+滚动，`_ty` 定位到目标页（`CanvasY ≈ -offset×scale`） |
| 惯性方向 | 单次滚轮向下后，`CanvasY` 单调递减（不回弹）；停止时速度归零 |
| 复位居中 | `ResetWebtoonView` 后当前页中心 ≈ 视口中心、宽度 = min(视口宽, 页高×ratio) |
| 目标-位置同步 | 预览栏跳页后 `_scrollTargetTy == _ty`（若保留目标字段）或直接断言无回弹滑动 |

---

## 四、待用户决策问题

1. **滚动默认宽度语义**（问题 3 的细节）：
   - **方案 A（推荐，符合你"像单页适应屏幕"的描述）**：当前页完全可见 + 水平垂直居中（页宽可能窄于视口，如 1100×760 视口下 0.7 比例页 ≈ 532px 宽）；
   - **方案 B**：铺满视口宽度（页宽 = 视口宽，标准 webtoon 观感，竖长页上下超出需滚动查看），当前页仅垂直居中。
2. **复位按钮语义**：复位 = "当前页适应屏幕并居中"（回到进入时默认视图）——确认？是否同时复位缩放倍率（回默认宽度）？
3. **滚动模型**：纯动量（滚一档 → 滑行缓慢停住，多档累积速度）vs 固定步长缓动（每格固定距离+缓动，无速度累积）。推荐纯动量（更接近手机手感，也符合"往下翻后缓慢暂停"）。
4. **拖拽释放惯性（甩动）**：是否要？推荐要（拖一下自然滑停，方向随拖拽）。
5. **实施范围**：A+B+C+D 一次做完（涉及滚动模式全部交互 + 测试，改动集中在 ReaderWindow.axaml.cs / ReaderViewModel.cs），确认后一次性实施验证。

---

## 五、风险点

- 工作流 A 的"LoadCurrentAsync 跳过"会影响 Webtoon 模式下 `CurrentIndex/PageText` 的既有刷新路径（目前由 `UpdateWebtoonPageText` 负责，需核对 `ProgressValue`/续读保存是否仍正确）。
- 工作流 B 重写后，`WebtoonSpeed` 按钮、键盘翻页、预览栏联动（`SyncPreviewStripToScroll`）需重新验证。
- 工作流 C 改变进入默认宽度（50% → 适应屏幕），属用户明确要求的行为变更；复位按钮 ToolTip 文案需同步更新。
