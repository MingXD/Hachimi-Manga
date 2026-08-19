# BUG-003：缩放焦点偏移（右下角）—— 排查史与根因深究

- **状态**：已修复（2026-08-19，修复验证通过，待用户实测确认）
- **优先级**：高（核心交互不可用）
- **现象**：缩放（滚轮/按钮）时画面向**右下角**偏移，焦点未保持在指针/中心处

## 现象演变（用户反馈时间线）
1. v1.0 画布模型（TransformGroup Scale+Translate + 锚定公式）→ 用户："焦点在右下角"
2. v1.1 ZoomToFocus（焦点 clamp 到内容）+ 缩放速度 → 用户："还是右下角"
3. v1.3 按钮改内容中心焦点 → 用户："滚轮和按钮都右下角"
4. v1.4 改 MatrixTransform（数学验证通过 <1px）→ 用户："症状完全没有改变"

## 已做改动回顾
| 版本 | 改动 | 验证 |
|---|---|---|
| v1.0 | 画布模型：ScaleTransform+TranslateTransform（TransformGroup）+ 锚定 tx'=f-(f-tx)·r | headless 数学自洽测试通过（盲区：未验证真实屏幕行为） |
| v1.1 | ZoomToFocus + EffectiveFocus(clamp 内容) + 缩放速度 1.1^(delta/120) | 同上 |
| v1.3 | 滚轮按事件缩放（修复失效）；按钮焦点=内容中心 | 24/24 |
| v1.4 | TransformGroup → **MatrixTransform(p'=scale·p+t)**；ContentToScreen 数学验证 | 25/25（数学通过） |

## 关键矛盾
- **数学验证通过**（ContentToScreen 缩放前后中心保持 <1px）
- **用户实测完全没变**（右下角现象从 v1.0 到 v1.4 一直存在）

## 深究过程（实证为主）

### 1. 排除法：真实渲染 vs 数学模型
headless 测试只验证了**自洽的数学函数**（ContentToScreen 用同一套 scale/tx/ty 计算），
没有验证 **Avalonia 真实渲染**是否按 p'=scale·p+t 执行。因此先在真实应用加自动诊断
（--diag：缩放前后输出 Viewport/PageLayer 的 TranslatePoint 与内容中心），发现：

- TranslatePoint 结果与数学不符（中心漂移 280/362px），且随 scale 变化
- 但 TranslatePoint 是否反映 RenderTransform 本身存疑（Avalonia 坐标转换 API 语义）

### 2. 决定性实验：真实渲染像素级验证
在应用内把 Viewport 背景临时设为**纯品红**，RenderTargetBitmap 渲染到 PNG，
用像素分析找**白色页面矩形**，与数学预期对比：

| STEP | 数学左上角 | 实际像素左上角 | 偏移 |
|---|---|---|---|
| 0 (s=0.369) | (445.8, 221.4) | (624, 444) | **+178, +223** |
| 1 (s=0.5535) | (393.6, 147.6) | (520, 296) | **+126, +148** |
| 2 (s=0.8302) | (315.5, 36.9) | (364, 76) | **+49, +39** |

页面**尺寸完全正确**（scale 生效），但**位置偏移**。用公式 p' = t + (1-scale)·origin 反推：
- x 偏移 = (1-scale)·282.5，而 282.5 = baseW/2 → **origin = 元素中心 (0.5, 0.5)**
- 三组数据全部吻合 → **Avalonia 的 RenderTransform 默认围绕元素中心缩放**

### 3. 官方文档证实
[Avalonia Docs: RenderTransforms and RenderTransformOrigin](https://v11.docs.avaloniaui.net/docs/get-started/wpf/rendertransforms-and-rendertransformorigin/)：
> "**default value for the RenderTransformOrigin in Avalonia is RelativePoint.Center**.
> In WPF the default value is RelativePoint.TopLeft (0, 0)."

**这就是根因**：全程按 WPF 语义假设 p'=scale·p+t（原点左上角），但 Avalonia 实际执行
p' = scale·p + t + (1-scale)·center。锚定公式里缺 (1-scale)·center 项 →
放大时内容围绕中心向外扩张，焦点处内容向右下漂移 scale·center·(r-1)。
v1.0→v1.4 换变换实现（ScaleTransform/TransformGroup/MatrixTransform）都改不了——
因为 **RenderTransformOrigin 默认值从未被设置**。

### 4. 修复
**ReaderWindow.axaml.cs 构造函数**：
```csharp
PageLayer.RenderTransformOrigin = Avalonia.RelativePoint.TopLeft;
```
同时清理了遗留的未使用 _layerScale/_layerTranslate 字段。

### 5. 残余 y 偏移（第二次像素验证发现）
修复 RenderTransformOrigin 后 x 完全一致（+0.2px），但 y 仍有 -30×scale 偏移。
追加诊断（打印 Bounds）：
```
PageLayer.Bounds = 0,0,565.3,738      ← Grid 被视口压缩到 738 高（非 800）
PageImage1.Bounds = 0,-30,565.3,798.3 ← StackPanel 把超高子元素垂直居中 → 上移 30px
```
→ StackPanel（PagePanel）在 Grid 中被压缩到视口高 738，800 高的 Image 垂直居中偏移 30px。
**修复**：Relayout 中显式 `PageLayer.Height = BaseH; PagePanel.Height = BaseH;`
XAML 中 Image 加 `VerticalAlignment="Top"`。

## 修复后验证（像素级）
| STEP | 数学左上角 | 实际渲染左上角 | 内容中心 |
|---|---|---|---|
| 0 (s=0.369) | (445.8, 221.4) | (446, 222) | (550, 368) |
| 1 (s=0.5535) | (393.6, 147.6) | (394, 148) | (550, 368) |
| 2 (s=0.8302) | (315.5, 36.9) | (316, 36) | (550, 367) |

**内容中心固定在 (550, 368±1)，与数学 (550,369) 一致，缩放不再漂移。**

## 教训（方法论）
1. **headless 数学测试的盲区**：只验证自洽函数，不验证框架真实渲染。
   对 RenderTransform 类行为，必须用**真实渲染像素验证**（RenderTargetBitmap→PNG→像素分析）。
2. **跨框架假设是高风险**：WPF 经验（RenderTransformOrigin 默认 TopLeft）不适用于 Avalonia
   （默认 Center）。动手前先查目标框架文档。
3. **症状"完全没变"≠ 没修**：v1.0→v1.4 每次改动都在修数学，但根因在框架默认值，
   数学无论如何改都是对的——所以用户实测无变化。要敢于怀疑"数学本身没问题，是渲染语义不同"。

## 残留事项
- [ ] 用户实测确认（滚轮/按钮缩放不再右下漂移）
- [ ] 重新发布 exe（app\publish）
