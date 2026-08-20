# 体验重构实施计划与进度（2026-08）

> 依据：reader-experience-v3.md + library-hierarchy-files.md（设计稿）
> 流程：每阶段 → 构建+测试 → 真实验证 → commit → 更新本文档进度

## 阶段与状态
| 阶段 | 内容 | 状态 |
|---|---|---|
| A0 | 画布模型重构：滚轮精细缩放(鼠标焦点锚定)、拖拽平移(可出界/空白不拖)、←→翻页、双页贴合 | [x] |
| A1 | 测试适配（滚轮=缩放、画布居中/出界、拖拽） | [x] |
| B | Webtoon 上下无缝拼接 + 滚轮滚动 + 懒加载；双击放大/恢复 | [x] |
| C | 按需高清重解码 | [x] |
| D | 文件夹树视图（层级库）+ 内容区联动 | [x] |
| E | 右键文件操作：打开/重命名/删除(回收站)/在资源管理器中显示/属性 | [x] |
| F | 最终验证 + 发布 + 文档归档 | 进行中 |

## 实施记录（2026-08-18/19）
- A0：ReaderWindow 重构为画布模型（Viewport + PageLayer + Scale/Translate 变换，RenderTransform 管理），
  移除 ScrollViewer；滚轮 ZoomTo（1.0015^delta 精细、鼠标焦点锚定）；拖拽仅在 Image 上生效（空白不拖）、
  无 clamp 可出界；缩放后内容<视口自动居中（看图器规则）；双击 Fit↔200%；工具栏 +/- 改 Click。
- B：ViewMode 枚举（单页/双页/Webtoon），M 键循环；Webtoon 用独立垂直面板懒加载可见页、滚轮滚动（Ctrl+滚轮缩放）。
- C：DecodeAsync 按需重解码（缓存记录解码宽度，放大超宽时重新解码，上限 4096）。
- D：FolderTreeScanner 目录树扫描；侧边栏 TreeView（库根→目录），选中目录内容区显示该目录的书。
- E：FileOps（回收站删除 SHFileOperation/重命名/资源管理器显示）；封面右键菜单
  （打开/资源管理器/重命名/删除/属性）；RenameDialog/PropertiesDialog。
- 过程问题：Avalonia 对 Transform 不生成命名字段（改用 code-behind 字段管理 RenderTransform）；
  事件双触发（窗口 AddHandler + XAML 绑定）导致缩放重复——只保留单入口。
- 测试：23/23 通过；真实验证：阅读器打开真实书渲染正常（light 62.5%）、主窗口含文件夹树。

## 关键决策记录
（实施中追加）

## v1.1 交互修正（2026-08-19）
- 缩放速度：factor = 1.1^(delta/120)（每格~10%，修复 Delta 单位导致的过慢）
- 焦点缩放：标准锚定公式 + 焦点 clamp 到内容区域（指针在画面外取最近边界点）；内容<视口时缩放后居中
- RTL 双页顺序：右=前页、左=后页（D 键切换方向时翻页与双页排列同步反转）
- 测试 24/24（严格锚定 <1px、RTL 双页顺序）

## v1.2 缩放修复（2026-08-19）
- 根因：Avalonia Issue #9858（Delta 值间歇性不正确/单位不一）→ 依赖单次 Delta 不可靠
- 修复：累积阈值触发（每 60 累积 → 5% 缩放）+ 灵敏度可调（工具栏按钮 ×0.5/×1/×2 循环）
- 焦点：headless 严格锚定（<1px）验证通过；速度修复后锚定效果应明显
- 注：真实应用无法注入 wheel（Avalonia 过滤），速度最终由用户实测 + 灵敏度按钮调节

## v1.3 滚轮失效修复（2026-08-19）
- 根因：v1.2 累积阈值依赖 Delta 数值，用户环境 Delta 极小/异常（#9858）→ 永远达不到阈值 → 完全失效
- 修复：**按事件计数缩放**（每次滚轮事件 = 一次 5% 缩放，方向由 Delta 符号决定，完全不依赖数值）
- 按钮缩放：焦点从"视口中心"改为"**内容中心**"（ContentScreenRect().Center）——修复"焦点在右下角"
- 24/24 测试通过

## v1.4 焦点右下角根因修复（2026-08-19）
- 根因：TransformGroup 子变换复合顺序与锚定公式假设不一致（translate 被 scale 放大）→ 缩放时画面向右下偏移
- 修复：改用 **MatrixTransform（p' = scale·p + t）**，消除顺序歧义，与锚定公式精确匹配
- 验证：数学验证（按钮缩放以内容中心为焦点，缩放后中心保持 <1px）+ 25/25 测试
- 说明：headless RenderTargetBitmap.Save 输出不可解码（已知限制），像素级验证改用 ContentToScreen 数学验证

## v1.5 BUG-003 真根因修复：RenderTransformOrigin（2026-08-19）
- 现象：v1.4 数学验证通过（<1px）但用户实测"症状完全没有改变"（仍右下角）
- 深究方法（bug 修复方法论）：先在真实应用加 --diag 实证，再广泛搜索资料
  1. TranslatePoint 诊断：中心漂移 280/362px，随 scale 变化 → 怀疑渲染语义 ≠ 数学模型
  2. **决定性实验**：应用内 RenderTargetBitmap 渲染到 PNG（背景临时设品红），像素级测白色页面矩形
     → 页面尺寸正确但位置偏移 (1-scale)·(baseW/2, ...) → **缩放围绕元素中心发生**
  3. 官方文档证实：[Avalonia: RenderTransformOrigin 默认 RelativePoint.Center]（WPF 默认 TopLeft）——
     https://v11.docs.avaloniaui.net/docs/get-started/wpf/rendertransforms-and-rendertransformorigin/
- 根因：**Avalonia 的 RenderTransform 默认围绕元素中心缩放**（p' = scale·p + t + (1-scale)·center），
  与全程假设的 WPF 语义 p' = scale·p + t（原点左上角）不符 → 锚定公式缺 (1-scale)·center 项 →
  放大时内容围绕中心向外扩张，焦点处内容向右下漂移 scale·center·(r-1)。
  v1.0→v1.4 换任何变换实现都无效，因为 RenderTransformOrigin 默认值从未被设置。
- 修复（ReaderWindow.axaml.cs）：
  1. `PageLayer.RenderTransformOrigin = Avalonia.RelativePoint.TopLeft;`（根因）
  2. 残余 y 偏移（-30×scale）：PageLayer/PagePanel 显式 Height = BaseH
     （StackPanel 被视口压缩到 738 高 → 800 高 Image 垂直居中偏移 30px）
  3. Image 加 VerticalAlignment="Top"
  4. 清理遗留 _layerScale/_layerTranslate 字段
- 验证（像素级，品红背景白页矩形）：内容中心固定 (550, 368±1) vs 数学 (550,369)；
  x 偏移从 +178px → +0.2px；三档缩放中心不漂移；25/25 测试通过
- 归档：docs/03-dev/bugs/BUG-003-zoom-focus.md（完整排查史 + 教训）

## v1.6 BUG-004 修复：双页→单页切换闪退（2026-08-19）
- 现象：双页浏览时点击模式切换按钮 → 应用闪退
- 根因：页面位图 LRU 缓存（上限 6 页）淘汰/重解码时直接 Dispose，但同一 Bitmap 对象被 UI Image
  控件 Source 引用（缓存共享返回）→ 布局 Image.MeasureOverride → Bitmap.get_Size() 抛
  ObjectDisposedException。切到 Webtoon 时 UpdateWebtoonPages 逐页解码大量触发淘汰 → 闪退。
  二次压力测试还发现 await 竞态：await GetPageBitmapAsync 恢复前位图已被淘汰 Dispose。
- 修复（位图 UI 引用计数）：
  1. VM 增加 _uiRefs（Acquire/Release/TryDispose）；EvictCache/重解码走 TryDispose（UI 引用中或
     在缓存 → 不 Dispose）
  2. Acquire 提前到 DecodeAsync 返回前（消除 await 后 Source 赋值前的淘汰竞态）
  3. LoadCurrentAsync 替换 Image1/2 前 Release 旧位图；竞态丢弃路径 Release 防泄漏
  4. View UpdateWebtoonPages/ExitWebtoon 释放面板位图引用 + _webtoonSeq 序列保护
  5. DisposeAsync 关闭时 Dispose 全部
- 验证：压力复现 4/4 零崩溃；新增回归测试 ModeSwitchCrashTests（26/26 通过）
- 归档：docs/03-dev/bugs/BUG-004-mode-switch-crash.md
- 附带：App 增加全局异常记录（AppDomain + Dispatcher.UnhandledException → LocalAppData/MangaAgent/app-start.log）

## v1.7 体验重构实施（2026-08-20，按 ux-v2-plan.md）
### 日志增强（需求四）
- App.SLog 改按日期分文件（logs/app-YYYY-MM-DD.log）+ 保留 7 天轮转
- 阅读器记录操作日志：翻页(PAGE)/模式切换(MODE)/缩放(ZOOM)/跳页(JUMP)/滚轮采样(WHEEL 已精简)/Webtoon 虚拟化范围(WEBTOON)
- 后续排查流程：用户操作 → 回传 %LOCALAPPDATA%\MangaAgent\logs\app-*.log + 截图 → 按日志轨迹定位

### Webtoon 虚拟化重构（需求二，参考 mihon/Tachiyomi WebtoonViewer）
- Canvas 绝对定位 + 增量渲染：只实例化可见页 ±2 缓冲（300 页书只 ~6 页实例化），滚动增量增删，无全量 Clear 重建
- 滚轮按事件计数滚动（步长 = 视口高 40%，修复 #9858 Delta 极小导致滚动缓慢——与缩放慢同根因）
- Ctrl+滚轮缩放（复用 ZoomToFocus 锚定）、鼠标拖拽平移、边界 Clamp、可见页号状态显示
- 键盘 ←→/Space/PageUp/PageDown = 按屏滚动
- 测试：WebtoonVirtualizationTests（虚拟化数量 + 滚动 + 缩放不崩溃）
- 竞态修复 2 项（见 BUG-004 v1.7）：LoadWebtoonImageAsync 并发去重；DecodeAsync 重解码保护 UI 引用位图

### 快速翻页（需求三，参考 KOReader 进度条 + 跳页对话框）
- 底部进度条（Slider）：拖动实时显页码、松手跳转（不逐页解码）；翻页/跳页后自动同步
- 跳页对话框：G 键或点击页码 → 输入 1..N 回车跳转（Webtoon 按物理页、分页按逻辑页）
- VM：JumpToPage（逻辑页）/ JumpToPhysical（物理页→逻辑页映射，修复单页 SecondIndex=-1 短路 bug）
- 测试：QuickJumpTests

### 目录浏览器混合展示（需求一）
- FolderNode 加 Parent + PathChain（面包屑）；FolderTreeScanner 保留所有子文件夹（文件系统导航语义）
- 新增 FolderEntryItemViewModel（IsFolder/Book 混合）；MainWindowViewModel：CurrentFolder + FolderEntries +
  EnterFolder/GoUp/NavigateBreadcrumb
- 右侧内容区：面包屑栏（⬆ 上级 + 路径）+ 混合条目（📁 文件夹样式 / 漫画封面样式）；
  双击文件夹进入下一层、双击漫画打开；侧边栏树选中同步
- 测试：FolderBrowserTests（A/B/C/D 场景：B 子文件夹、C/D 漫画，导航/面包屑/上级）

### 其他
- 修复 DockPanel LastChildFill 布局 bug：Slider 声明在 Viewport 之后导致 Viewport 失去 Fill（宽度收缩到内容宽）→ 调整声明顺序
- 全量测试 29/29 通过；真实书压力 5/5 零崩溃

## v1.8 问题修复（2026-08-20，按 v1.8-plan / v1.8-final-plan）
### Bug 修复
- BUG-3 竞态防护：_layoutVersion 替代 FirstIndex 对比 → 配对/切模式画面必然更新（此前被误杀丢弃）
- BUG-1 资料库：扫描进度回调（Task.Run 离线 + Dispatcher 更新）；Rescan/LoadSaved 补建 FolderTree
- BUG-4 进度条：去掉外层 Border IsHitTestVisible=False → 可拖动
### 滚动模式重构（BUG-006）
- 等宽：所有页宽度一致（基准=进入时视口宽），高度=宽/ratio，页位置前缀和+二分定位
- 卡顿：解码限流（SemaphoreSlim 2）；黑屏：先加后删；缩放焦点：ZoomWebtoon 鼠标原始锚定
- 滚动速度按钮（工具栏，Webtoon 模式显示）
### UI 调整
- 模式切换：下拉菜单（单页/双页/滚动 三选一）
- 配对改名"配对起点1/2"、RTL 改名"右→左/左→右"（仅单页/双页显示）
- 左右翻页按钮按方向映射 + ToolTip"下一页/上一页"
- 默认 FitScreen（打开/切模式/复位）
- 悬停 ToolTip 全名；侧栏 GridSplitter 可拖宽
### 右键菜单
- 恢复：打开/资源管理器/重命名/删除(回收站)/属性
- 新增：复制到…/移动到…（任意目录，文件夹选择器）
### 测试/验证
- 新增 PairOffsetSwitchTests、RescanBuildsTreeTests；更新 WebtoonVirtualization/ZoomCentering/CtrlWheel/焦点锚定测试
- 31/31 通过；真实书压力 4/4 零崩溃
