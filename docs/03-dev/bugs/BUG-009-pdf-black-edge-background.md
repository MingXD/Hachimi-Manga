# BUG-009：PDF 双页阅读中间出现固定黑带（渲染背景未预填白 → 白边输出为黑）

- **状态**：✅ 已修复（2026-12-24：FillRect 白底 + 裁切模式功能）
- **引入**：v4-PDF（`542875a`）
- **用户反馈**：PDF 双页模式两页"不像图片一样连在一起"，中间黑色非常固定；用户指出漫画没有任何黑边、Chrome 打开直接是位图本身，要求对标 Chrome

## 现象（像素级实测，用户真实 PDF `G:\影视\晚安，布布`）
- 当前渲染：页面四周 29 列/42 行（≈3.6%）输出 **`(0,0,0,0)`（纯黑 + alpha 0）**，向内第 30 列起才出现内容
- 对照渲染（渲染前把位图整体填充白色）：同位置全部 `(255,255,255,255)` 纯白，内容不变
- 双页并排时：两页的黑色边缘相邻 → 固定宽度黑色竖带（用户所见）

## 根因（文件解剖 v3 定案：渲染背景未预填白，非页面内容问题）
- 页面字典：MediaBox = CropBox = A4（595.2744 × 841.8888 pt），Rotate 0，无异常裁剪框
- 每页内容 = QuickPDF 转换工具生成的 1 张 **DCTDecode 位图（1041 × 1500 px，比例 0.694）** + 文本层；A4 纸面比例 0.707
- **位图按比例居中绘制在纸面中央（约占 91–94%），纸面四周 ≈3.7% 是页面从未绘制任何内容的空区**（普通纸面留白；QuickPDF 未铺背景矩形）
- `FPDFBitmap_CreateEx` 分配的缓冲**不初始化**（0 = 黑 + alpha0）；PDFium 只绘制内容覆盖区 → 我们的渲染把空区输出为黑
- **Chrome 对照（同为 PDFium）**：Chrome 同样画出这圈空区，但标准渲染先有白色页面背景 → 空区为白，与纸面融为一体 → "直接就是位图本身，没有边"
- 参考封装（PdfiumViewer/Docnet）均在渲染前 `FPDFBitmap_FillRect` 填白 —— 属标准做法
- 自造测试 PDF 未暴露：测试页带显式全页背景矩形，无空区

## 方案（小改动，低风险）
**渲染前把位图背景填白**：`Fpdf` 增加 `FPDFBitmap_FillRect` P/Invoke；`PdfComicSource.RenderPageAsync` 在 `CreateEx` 后、`RenderPageBitmap` 前调用
`FPDFBitmap_FillRect(bmp, 0, 0, w, h, 0xFFFFFFFF)`（BGRA 白 = alpha 255）。

- **改动点**：`Core/Comics/Pdf/PdfiumNative.cs`（+1 P/Invoke）、`Core/Comics/Pdf/PdfComicSource.cs`（+1 行调用）——**仅此**
- **影响**：所有 PDF 渲染（正文/预取/缩略图/滚动/封面）页边白正确显示；双页中间为白边相接（与图片书观感一致）；alpha 恒 255 更贴合 `WriteableBitmap AlphaFormat.Opaque`；无性能影响（FillRect 一次整幅，微秒级）
- **不需要**：不做任何裁边（页面白边是正常书页特征，图片书同样如此；用户要求的是格式间观感一致，不是切边）
- **测试**：
  - `PdfTestWriter` 增加"位图不满 MediaBox、无背景矩形"页形态（页中央 80% 画内容，四周留白）→ 断言渲染结果四边为纯白、alpha=255、中央内容存在
  - 回归：既有 PDF 渲染测试（自造全幅背景页）不变
  - 真实样本复验：用户 12 卷任意页四边 = 白色；双页视图无黑带
- **风险**：极低（标准参考实现均如此）；页面本身画了黑色背景的（罕见）不受影响——FillRect 先填后被页面内容覆盖

## 建议
与 BUG-008（书名 NUL）、中文卷号解析同批实施（均 PDF/扫描/元数据小改，一次提交 + 回归）。

## 参考
- PdfiumViewer 渲染前填白背景的既有实践（`FPDFBitmap_FillRect(bitmap, 0, 0, width, height, 0xFFFFFFFF)`）
