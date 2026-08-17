# 阅读引擎设计（v1 — 结合竞品调研）

> 规则与算法参考：Mihon（RTL/双页三档/宽页拆分/缩放六档）、Komga DIVINA（首尾页单页/横向页单页/元数据推断方向）、Houdoku/Kavita（offset）、Panels（封面恒单页/手动分组）、YACReader（放大镜/插值）、Cover（RTL 对齐）。详见 ../01-research/commercial-ux.md 与 open-source-readers.md。

## 1. 阅读方向
- 默认 **RTL**；LTR/竖排【待定】可全局与单本覆盖
- 方向判定优先级：单本设置 → ComicInfo.xml `<Manga>` 字段 → 文件名/目录启发式 → 全局默认（Komga 模式）
- **方向与缩放/其他选项正交**（KOReader 锁定 bug 的教训）

## 2. 双页模式（PageLayouter 规则集）
输入：物理页序列；输出：逻辑页序列（每项 = 单页或双页合成）
规则（全部可配置）：
1. **封面单页**：默认第 1 页不参与双页（ComicInfo `PageType=FrontCover` 优先）
2. **成对分组**：封面后 2-3、4-5…
3. **横向页单页**：宽高比 ≥ 阈值（默认 1.6，可配置）的页面保持单页（Komga/Mihon Wide 档）
4. **首尾页单页**：双页模式下，整本书第一/最后一张逻辑页单页（Komga）
5. **中缝**：宽度 0/固定 px/百分比，可独立调节（Cover 用户痛点）
6. **offset**：模拟实体书左右页错位（Houdoku/Kavita），RTL 时保证左右配对正确【可配置】
7. **RTL 对齐**：RTL 书首单页靠左、末单页靠右；LTR 反之（Cover 规则，被 Komga 引用为标准）
8. **手动分组**：长按封面/缩略图"从此页开始成对"重排起点（Panels）【V2】
9. **跨页图**：可选"拆分宽页为左右两半翻页"（Mihon split wide pages，带反转开关）与"旋转宽页适配"（Paged/Webtoon 独立开关）

## 3. 阅读模式（Viewer）
| Viewer | 说明 |
|---|---|
| Pager（单页） | 一次一页；点击区/键盘/滚轮/滑动翻页 |
| DoublePage（双页） | 由 PageLayouter 合成；可"横屏/宽窗口自动切换"（Panels 预设） |
| Webtoon（连续滚动） | 垂直卷动、懒加载、滚动到章节边界预加载上下章、点按翻页距离可配置（Mihon WebtoonViewer） |
| ContinuousVertical（长条带间隔） | Mihon 的 Long strip with gaps【V2】 |
| PageCurl（卷页） | 实体书手感【V2，高成本】 |

## 4. 翻页交互
- 点击区预设：默认 / 左右 / L形 / Kindle式 / 边缘 / 禁用 + 水平/垂直反相（Mihon）
- 键盘：←→、PageUp/Down、空格、可自定义全部快捷键（CDisplayEx）
- 滚轮 / 触摸板滑动 / 触屏手势（Windows 触屏）
- 音量键翻页【仅移动端，预留】
- 长按快捷操作（设封面/复制/分享）【V2】

## 5. 缩放与平移
- 六档：适应屏幕 / 拉伸 / 适应宽度 / 适应高度 / 原始尺寸 / 智能适应（Mihon scale types）
- 缩放起始焦点：自动/左/右/中（RTL 默认关注右上角）【可配置】
- 滚轮以光标为中心缩放；范围 25%–400%+；双击缩放动画速度可调
- 平移时禁用翻页防误触；放大镜（矩形/圆形+描边，YACReader 式）【V2】

## 6. 渲染管线
```
ComicSource → 按需解压单页 → 解码 → 三档缓存（原始/屏幕级/缩略图）
→ 滤镜（亮度/对比度/灰度/反色/RGB 混合，Mihon/OpenComic）
→ 插值（Lanczos3/Lanczos2/Mitchell/Cubic/Linear/Nearest 可选，OpenComic/YACReader）
→ PageLayouter 合成 → 呈现
```
- 翻页动画：无 / 淡入 / 滑动（可关，Foliate reduce-animation 思路）
- E-ink/低刷屏：翻页闪烁刷新（黑/白闪、间隔可调）【V2】
- 阅读背景色：黑/灰/白/自动（Mihon/Komga）

## 7. 进度与书签
- 自动续读（页/双页偏移/缩放/方向/模式）；防抖落盘 + 退出强写
- 书签（逻辑页 + 备注）；已读完标记；系列流"下一卷"提示【V2】
- 数据按 路径+修改时间 定位，文件移动后可重定位

## 8. 性能目标
- 5000 页 CBZ：索引 <1s、随机翻页 <100ms
- 内存：缓存上限可配置（默认 ≤512MB）
- 预解码 ±N 页（默认 2）；全库缩略图后台生成
- 超大图（8K 扫描页）降采样显示

## 9. 可测试性
- PageLayouter/方向/跨页识别为纯函数，单元测试覆盖边界（奇数页、全横向页、RTL 对齐）
- ComicSource 可注入假源做引擎级测试；UI 层薄，逻辑层厚
