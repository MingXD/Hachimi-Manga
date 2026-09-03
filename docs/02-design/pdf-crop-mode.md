# PDF 裁切模式设计（"就放位图本身"，与图片书展示逻辑一致）

> 状态：**✅ 已实施（v4.1，2026-12-24）**——裁切按钮（仅 PDF）+ 设置默认（PdfCropDefault，默认关）+ 封面保持纸面。配套：BUG-009 渲染背景预填白（FillRect）。测试 119/119 全绿。
> 默认按 Chrome/PDFium 标准渲染（纸面视图，白背景）；"裁切"按钮切换：裁掉纸面空区、只显示位图本身，单页/双页/滚动/缩放展示逻辑与图片（CBZ）一致。

## 1. 调研结论：能做到，且无需"渲染后检测"

- 用 PDFium **对象枚举 API**（`FPDFPage_CountObjects / FPDFPage_GetObject / FPDFPageObj_GetType / FPDFPageObj_GetBounds`）直接读取页面内容边界，**不需要渲染**即可得到"位图本身"的区域（毫秒级，与 GetPageSize 同级开销）
- 用户真实样本实测（晚安布布第 10 卷）：
  - 每页对象 = **1 个 IMAGE（位图）+ 1 个 TEXT（OCR 文本层，占纸面 17% 宽，需忽略）**
  - 位图 bbox：`(21.6, 23.2)-(573.7, 818.7)pt`，宽 552.1pt = **纸面宽 92.7%**（与像素渲染实测内容区 92.5% 吻合）
  - **每页 bbox 独立**（上/下边距随扫描内容 23~31pt / 810~819pt 浮动）→ 必须逐页检测，不能统一常数
- 语义规则：**只统计 IMAGE 与 FORM（内嵌图像组）对象的边界并集**；TEXT/PATH（矢量文字线稿）不计 → 纯矢量 PDF 无 IMAGE → bbox=整页 → 裁切模式自动"不裁"（正确：裁切=显示位图本身，矢量页没有"位图"）

## 2. 功能设计

### 2.1 Core：PdfComicSource 增加裁切模式
| 改动 | 内容 |
|---|---|
| `PdfComicSource.CropToContent`（bool，volatile/加锁读写） | 模式开关；切换时源内部内容区缓存失效 |
| `GetContentBox(index)`（新增，pt 矩形） | LoadPage → 枚举对象 → IMAGE/FORM bbox 并集；无则整页；结果缓存（`ConcurrentDictionary<int, (float L,B,R,T)>`），懒加载 |
| `GetPageSize(index)` | Crop=true：返回**裁后** canonical 尺寸（用内容区比例）；false：整页（现状） |
| `RenderPageAsync(index, targetWidth)` | Crop=true：渲染宽 = `targetWidth × paperW/contentW`（保证裁后输出正好 targetWidth 宽）→ 渲染整页 → 从缓冲抠出内容子矩形（按比例换算像素坐标）返回；false：现状整页 |
| `OpenPageAsync`（PNG 兜底） | 内部走 RenderPageAsync → 自动跟随 |

- 输出不变量：**两种模式 RenderPageAsync 的输出宽都 = targetWidth**（解码管线/缓存键/LRU 全部零改动）
- 布局：`_sizes` 来自 GetPageSize → 裁切模式自动用"内容比例"→ 单页内容充满、双页两内容边相接（与图片书一致）、滚动模式按内容比例排布——**全链路自动一致，无需阅读器感知"裁切"**

### 2.2 App：工具栏"裁切"按钮
| 改动 | 内容 |
|---|---|
| `ReaderWindow.axaml` 工具栏 | 新增"裁切"切换按钮（按下态显示），**仅 `Book.Kind == Pdf` 时可见**（图片书无此概念） |
| `ReaderViewModel` 新 API `SetPdfCrop(bool)` | 若源是 PdfComicSource：置 `CropToContent` → 清空 `_sizes` 与位图/缩略图缓存 → 后台重算全部页尺寸（既有 LoadSizes 机制）→ 完成后 `RebuildLayout`（分页）/+`WebtoonLayoutChanged`（滚动）→ 重渲染当前页。切换为会话级（不持久化） |
| 状态提示 | 裁切开时工具栏按钮按下态 + 状态栏/日志记录一次 |

- 默认值：**关**（= Chrome 纸面视图）；按钮按一次进入"位图本身"模式
- 库封面（ThumbnailService）：保持纸面视图（信息全、实现零改动）——如需封面也裁，后续可加设置，见决策项

### 2.3 视觉对照（晚安布布样本）
- 纸面视图（默认）：A4 页面 + 四周白（修复后）；双页中间白边相接
- 裁切视图（按钮）：页面=位图本身（92.7%×93% 区域），画面放大 ~7% 满幅；双页两张图直接并排（Spacing=-1 重叠 1px 防缝）→ 与图片书双页观感完全一致

## 3. 测试计划
- `PdfComicSource`：Crop=false/true 的 GetPageSize 比例差异；Crop=true 渲染输出宽=targetWidth、**边缘像素不再是纸面白/黑而是位图内容**（首行含非白/内容色）；无 IMAGE 页（矢量 fixture）Crop=true 不裁（bbox=整页）；内容区缓存/切换后失效
- `PdfTestWriter` 增加"带偏移位图"页形态（图占中央 92% 且每页偏移不同——模拟真实样本）→ 裁切后各页宽高比正确
- E2E（Headless）：打开 PDF（默认纸面比例）→ 调 `SetPdfCrop(true)` → 等 `SizesLoaded` → 比例=裁后、双页 Image 宽高比正确、翻页/滚动不崩
- 真实样本复验（用户 12 卷）：裁切视图双页图相接无黑/白带

## 4. 风险与对策
| 风险 | 对策 |
|---|---|
| 内容 bbox 检测误判（页内多个图/装饰图拉到 bbox 外） | 取并集可能把边角小图算入 → 加过滤：忽略面积 < 页面 1% 的 IMAGE；FORM 递归同规则 |
| 位图带内白边（图内容本身白边）→ 裁切后仍有白边 | 属位图自身内容，与图片书同逻辑（CBZ 图自带白边也不裁），接受 |
| Crop 切换时的尺寸风暴（228 页重算） | 复用既有 LoadSizes 后台补全 + 每 64 页让出；切换仅一次 |
| 旋转页面（/Rotate） | GetBounds 返回已应用旋转的 bbox（pdfium 处理）→ 无额外处理 |

## 5. 实施批次建议
与 BUG-008（书名 NUL 净化）、BUG-009（渲染背景预填白）、中文卷号解析**同批一次提交**（均 PDF 相关小改 + 回归测试）：BUG-009 是裁切功能的前置（两种视图都需要正确背景）。

## 决策项（待用户）
1. 裁切按钮**默认关**（打开 PDF 先见 Chrome 纸面视图，按按钮切位图视图）——确认？
2. 库封面缩略图：**保持纸面**（推荐，改动最小）或跟随裁切？
3. 会话级切换即可，还是需要"设置→默认裁切开"记住偏好？
