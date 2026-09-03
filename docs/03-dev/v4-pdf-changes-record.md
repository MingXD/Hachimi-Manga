# v4-PDF：PDF 格式支持（变更记录）

> 对应设计文档 `docs/02-design/pdf-format-support.md`（含 P0 实测与决策记录）。测试基线：**100 → 112/112 全绿**（新增 12 项 PDF 相关测试）。

## 功能落地清单

### Core（MangaAgent.Core）
- `Comics/ComicSource.cs`：新增 `RenderedPage`（BGRA8888 像素页）与 `virtual RenderPageAsync(index, targetWidthPx)` —— 矢量源按目标宽光栅化；图片源默认 null 走原流解码路径（**零回归**）。工厂 `.pdf` → `PdfComicSource`
- `Comics/Pdf/PdfiumNative.cs`：PDFium（154.0.8035，BSD-3）FPDF P/Invoke 薄封装 + `PdfiumLibrary` dll 定位（exe 旁 → `%LOCALAPPDATA%\HachimiManga\pdfium\` 兜底）+ `PdfException`（NativeMissing/Encrypted/Corrupt/Other）
- `Comics/Pdf/PdfComicSource.cs`：页数/尺寸/元数据/按宽渲染/PNG 兜底流；`_gate` 串行化（沿用 Zip 源纪律）；GetPageSize 返回 **canonical 300dpi 像素**（A4 ≈ 2480×3508，比例驱动布局、语义=“扫描原图级”）
- `Comics/Pdf/PngEncoder.cs`：纯 BCL PNG 编码（zlib+CRC32）——OpenPageAsync 兜底（通用解码调用方零改动可消费 PDF）
- `Library/BookInfo.cs`：`BookKind.Pdf`；`KindLabel`（CBZ/PDF/文件夹 统一标识）；`IsFileKind`；`KindForPath(path)`（--open/未知入口统一类型推断）
- `Library/LibraryScanner.cs` / `FolderTree.cs`：收集 `*.pdf`（单文件坏/加密/dll 缺失跳过，不中断扫描/建树）

### App（MangaAgent.App）
- `Imaging/BgraBitmap.cs`：BGRA → `WriteableBitmap` 直通（无编码往返，Headless 尺寸保真已测）
- `ReaderViewModel.Decode.cs`：DecodeAsync / 预取 / 预览缩略图三处先走 `RenderPageAsync`，命中即直通；**EffectiveDecodeLimit PDF 档位映射**（Auto ≤ canonical 300dpi、Limit=DecodeLimit、Original=预算上限）——v3 清晰度档位对 PDF 语义化生效，设置页无需新增控件
- `App.axaml.cs`：`--open` 按扩展名推断类型（PDF 不再被误当 CBZ）
- 文案/标识联动：封面墙副标题、目录树条目副标题、属性对话框、重命名保留扩展名（文件型书）、搜索命中、目录统计（M/N/X 计数）全含 PDF
- 设置页“解码质量”提示补 PDF 语义；使用介绍（guide + DefaultGuide 模板）补“支持的格式”小节
- `app/Directory.Build.targets`：vendor pdfium.dll 自动复制到各项目输出（bin/publish；dll 缺失构建照常）

### 顺手修复
- `ParseVolumeFromName` 正则：C# 字符串里的 `\b` 实为**退格字符**（非词边界）+ `.?s*` 意图不明 → 文件名卷号 fallback 从未生效（CBZ 靠 ComicInfo 掩盖，PDF 无元数据即暴露）。改为 `(?i)(?<![a-z0-9])(?:vol|v)\.?\s*(\d+)`，`TestSeriesA_vol4` → Volume “4”

### 测试（+12，全部真实走 PDFium 渲染）
- `Tests/Pdf/PdfTestWriter.cs`：纯 BCL 程序化 PDF 生成器（N 页矢量、每页主色块、经典 xref）
- `Tests/Pdf/PdfComicSourceTests.cs`（8）：页数/尺寸(2500×3333@300dpi)/渲染宽高成比例/彩色内容像素断言/OpenPageAsync PNG 签名/损坏文件 PdfException(Corrupt)/中文路径/Dispose 幂等
- `Tests/Pdf/PdfReaderE2eTests.cs`（3，Headless）：WriteableBitmap 尺寸保真冒烟；打开 PDF → 单页/翻页/双页/滚动(按宽)/缩略图全链路；canonical 尺寸喂布局与 Webtoon 比例
- `LibraryScannerTests.Scans_Pdf_Books`：fixture `reference/fixtures/library/TestSeriesA/TestSeriesA_vol4.pdf`（2 页真实 PDF：矢量页+内嵌 JPEG 页）→ 系列/卷/页数正确

## 资源与分发
- `vendor/pdfium/win-x64/pdfium.dll`（154.0.8035，单文件 6.93MB，BSD-3）+ LICENSE；`vendor/` 已 gitignore（不入库）
- 发布包经 Directory.Build.targets 自动携带 dll；运行期缺失 → PDF 不入库/打开给明确提示（`%LOCALAPPDATA%\HachimiManga\pdfium\` 可兜底）

## 已知限制（V1 边界，有意为之）
- 加密 PDF：扫描跳过；`--open`/阅读器给出“已加密暂不支持”提示（无密码输入 UI，P2 再议）
- pdfium.dll 缺失：PDF 功能整体不可用（有可读提示），其余格式不受影响

## 验证
- `dotnet build`（sln）：0 错误
- `dotnet test`：112/112 通过（57s）
