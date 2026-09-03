# PDF 格式适配：调研与方案设计（已实施 v4-PDF）

> 状态：**✅ 已实施（v4-PDF，2026-12-24）**——测试基线 112/112 全绿。实施清单见 `docs/03-dev/v4-pdf-changes-record.md`；本文档保留调研/决策/P0 实测全过程供追溯。
> 决策进度：① 路线已定 **R1 = PDFium 原生**；② `pdfium.dll` 已获取（**154.0.8035**，单文件 6.93MB，BSD-3）并完成加载/渲染/像素级验证。本机无外网约束不再阻塞本方案。

---

## 0. 结论摘要（TL;DR）

| 项 | 结论 |
|---|---|
| 难度评级 | **中（推荐路线：PDFium 原生封装）**；**中-高（零依赖纯托管路线）**。架构承接难度低（V1 预留的 ComicSource 扩展点直接兑现），难点在渲染后端及其原生供应链 |
| 工作量 | 单人约 **2.5–3.5 周**（PDFium 路线，含测试）；纯托管路线同量级但覆盖面打折、长尾维护成本高 |
| 外部依赖 | ✅ 已解决：`vendor/pdfium/win-x64/pdfium.dll`（154.0.8035，单文件无附加依赖，实测加载+渲染通过）；`vendor/` 已入 .gitignore |
| 推荐路线 | **R1 = PDFium 原生（手写 FPDF P/Invoke 薄封装，不引 NuGet 包）** 为主后端；阅读器解码管线新增「按宽度光栅化」入口，矢量页缩放不糊 |
| 阅读器改造面 | 小：现有 ComicSource 抽象、位图 LRU 按宽缓存、v3 清晰度档位全部复用，约 10 个文件的小改动 + 1 个新源类 |
| 明确不支持（V1） | 加密 PDF（密码输入 UI 后置）；若走纯托管路线另加：矢量/文字型 PDF、ObjectStream 变体等长尾（报错占位不崩溃） |

---

## 1. 需求与范围定义

### 1.1 目标场景

用户把**漫画/单行本 PDF**（扫描本、汉化组合集、ePub 转 PDF 等）加入库，能像 CBZ/文件夹书一样：扫描入库 → 封面墙/目录树展示 → 打开阅读（分页/双页/RTL/Webtoon/缩放/续读/书签/预览栏）→ 缩略图正常。

漫画 PDF 的普遍形态：**每页 = 一张全页内嵌图（DCTDecode/JPEG 或 FlateDecode 原始像素）**，页面级为矢量内容极少。这决定了「图片提取」能覆盖绝大多数真实文件，也决定了「完整矢量渲染」是工程上更稳的通用解。

### 1.2 非目标（V1）

- 加密/密码 PDF（PDFium 可探到 `FPDF_ERR_PASSWORD`，V1 显示明确提示页，密码输入对话框后置）
- PDF 批注/表单/文本选中复制/搜索（阅读器不做文本层）
- 打开即整档解析索引等重型优化（随机访问即可，与 CBZ 一致）

### 1.3 验收口径（建议）

1. 扫描含 PDF 的库 → PDF 以书出现（页数正确、标题/卷号推断与 CBZ 同规则：`/Info` Title 优先、否则文件名；Series=父目录名）
2. 打开 PDF：分页显示、翻页/双页配对/RTL/Webtoon/缩放均正常；**缩放放大后文字/线稿保持清晰（按显示宽度重渲染）**
3. 缩略图（封面墙/预览栏）可显示；续读/书签与 CBZ 行为一致
4. 加密/损坏 PDF：扫描不中断、打开给明确提示（沿用坏 CBZ「跳过不崩」哲学）
5. 回归：原 67+（现 100）测试全绿；新增 PDF 源/扫描/布局测试

---

## 2. 现状调研：本项目如何天然承接 PDF

### 2.1 代码事实（已逐文件核实）

- `Comics/ComicSource.cs`：抽象即「随机访问 + 懒解码」，注释明写 *"未来 CBR、PDF"*；工厂 `Open(path)` 按扩展名分发——**PDF 只差一个实现类与一行分发**
- `ReaderViewModel.Decode.cs` 解码管线：`OpenPageAsync(index)` → 流 → `Bitmap.DecodeToWidth(ms, want)`；LRU 缓存按「物理页 + 解码宽度」双键（`_pageCache` + `_pageCacheWidth`），放大时 `CurrentPageNeedsRedecode` 触发按新宽度重解码——**按宽度重渲的机制现成**
- 页面尺寸表 `_sizes`（ConcurrentDictionary）只用于宽高比/布局与 Auto 档上限——尺寸语义需为 PDF 单独定义（见 §5.3）
- 扫描：`LibraryScanner.ScanAsync`（递归收集 CBZ/ZIP + 含图目录）、`FolderTree.FillAsync`（目录浏览器）——各有一处文件型书收集点
- 打开入口：库内双击（MainWindowViewModel → ReaderViewModel，path 驱动）与 `--open`（`App.axaml.cs`，文件一律按 `BookKind.Zip` 构造——**PDF 会被错误地当 CBZ 打开**，需修）
- 封面缩略图 `ThumbnailService`、预览栏、Webtoon 都经同一 `OpenPageAsync`/`DecodeAsync`——**统一受益，无需各自适配**
- 进度/书签/设置全部按 `Book.Path` 键存 SQLite/JSON——PDF 零改动

### 2.2 结论

阅读器侧改造是「兑现预留设计」，**低难度**；PDF 的真正成本在渲染后端（见 §3）。

---

## 3. 技术路线调研与选型

### 3.0 离线约束实测（本次复核）

| 探测 | 结果 |
|---|---|
| `api.nuget.org` / 公网 HEAD | 连接失败（无外网成立） |
| `~/.nuget/packages` 全部 70+ 包 | **零 PDF 相关**；有 `skiasharp 3.119.4`（Avalonia 依赖，含 win-x64 native）、`sharpcompress`、BCL `ZLibStream` 可用 |
| SDK 9.0.317 | 无 `Microsoft.Windows.SDK.NET.Ref`（WinRT 投影包，属 NuGet 需还原）→ **Windows.Data.Pdf 离线不可行** |
| 本机已装 Foxit PDF Editor / WPS / lenovoPdf / Chrome 残留目录 | 均未找到可复用的 `pdfium*.dll`（Foxit 为自有引擎，Chrome 未安装/无独立 pdfium.dll） |

**结论：任何「引 NuGet 包」或「依赖系统组件」的路线当前离线均不可行；可行路线只有「手写封装 + 自备原生 dll」与「BCL 纯托管」。**

### 3.1 R1：PDFium 原生（推荐）

- 技术：Google PDFium（BSD-3）。获取预编译 `pdfium.dll`：官方无 Windows 二进制分发，社区标准渠道为 [bblanchon/pdfium-binaries](https://github.com/bblanchon/pdfium-binaries)（GitHub Releases，win-x64 tag，附 v8/icu 依赖集需实测）或 NuGet 上的原生包（如 [bblanchon.PDFium.Win32](https://libraries.io/nuget/bblanchon.PDFium.Win32/120.0.6084)，仅装一次）。
- 集成方式：**不引 .NET 包**，手写 FPDF P/Invoke 薄封装（约 300–500 行）：`FPDF_InitLibrary / FPDF_LoadDocument / FPDF_GetPageCount / FPDF_LoadPage / FPDF_GetPageWidthF·HeightF / FPDFBitmap_CreateEx(BGRA) / FPDF_RenderPageBitmap / FPDF_ClosePage·CloseDocument / FPDF_GetLastError / FPDF_GetMetaText`；文件访问用 `FPDF_FILEACCESS` 回调直读磁盘（大 PDF 不整读入内存）。API 极稳定（近十年无破坏性变化），封装一次长期受益。
- 质量：矢量+文字+图片全覆盖；**可按任意目标宽度光栅化** → 与现有「按宽解码/放大重解」管线严丝合缝，缩放无损。
- 成本与风险：dll 体积 ~10–20MB；依赖集（个别 build 需要 v8/icu 附加文件）**已实测**：154.0.8035 win-x64 为单文件 6.93MB、零附加依赖；PDFium 渲染建议按源串行化（沿用 Zip 源 `_gate` 模式，避免并发坑）。
- 同行参照：竞品 YACReader（QtPdf=PDFium）、Perfect Viewer、CDisplayEX 均走 PDFium；[PdfiumCore](https://relatedrepos.com/gh/Dtronix/PDFiumCore)、Docnet.Core、PDFtoImage 均为同思路 .NET 封装（本项目手写以规避离线还原 + 依赖最小化）。

### 3.1a P0 冒烟实测结果（2026-12-24，已通过）

隔离冒烟工具位于 `vendor/pdfium/smoke/`（SmokeLib.cs + run-smoke.ps1，**不入主代码库**）。测试 PDF 为程序化生成的 2 页 A4（第 1 页纯矢量：色块/文字/灰阶棋盘网格；第 2 页内嵌 JPEG 模拟扫描漫画页：黑框/面板/对话气泡/网点区），手工写 xref 的合法 PDF。

| 验证项 | 结果 |
|---|---|
| dll 加载（FPDF_InitLibrary） | ✅ 单文件无依赖缺失 |
| 打开 PDF / 页数 / 页尺寸 | ✅ 2 页；595.0 × 842.0 pt 正确 |
| 按目标宽渲染彩色 BGRA | ✅ 像素级验证：红/蓝/绿块与文字精确命中（如红块 BGRA=64,64,235） |
| PNG 输出（System.Drawing 直写） | ✅ |

**性能实测（本机 CPU，含首次渲染冷启动，单位 ms）**

| 渲染目标宽 | 第 1 页（纯矢量） | 第 2 页（内嵌 JPEG 扫描页） |
|---|---|---|
| 400px（≈缩略图） | 12 | 26 |
| 1200px（≈分页显示宽） | 36 | 63 |
| 2400px（≈2K 放大档） | 145 | 215 |

> 对照：项目现有大 JPEG 扫描页解码（Bitmap.DecodeToWidth 到 1200 宽）量级为几十 ms，PDFium 矢量/图片页渲染与之同量级；2400px 档 150–220ms 属放大操作，命中现有「预取 + 按宽缓存 + 放大重解」机制后翻页路径（~1200px）体感与 CBZ 一致。内存峰值 = 单页渲染缓冲（2400×3396×4 ≈ 33MB 瞬态），与现有 4K JPEG 解码峰值同量级。

**踩坑记录（实施时避免）**
1. `FPDFBitmap_BGRA = 4`（枚举 UNKNOWN=0 GRAY=1 BGR=2 BGRx=3 BGRA=4）；写 1（GRAY）会按 4 倍长度越界读 → AccessViolation。
2. **渲染 flags 禁用 `FPDF_LCD_TEXT`(0x08)**：实测开启后整页输出被灰度化（RGB 三通道相等=Rec.601 亮度），改用 `FPDF_ANNOT`(0x01) 即真彩色；速度无差异。
3. `FPDF_LoadDocument` 路径参数为 UTF-8（C# 侧 `CharSet.Ansi` 在纯 ASCII 路径下安全；中文路径需手动 UTF-8 编码字节，实施时处理）。
4. 宽度→像素换算：`h = round(pageH_pt * targetW / pageW_pt)`；BGRA stride = width×4（天然 4 对齐）。

### 3.2 R2：Windows.Data.Pdf（系统自带渲染，被离线挡路）

- 技术：WinRT `Windows.Data.Pdf`（Win10 1809+ 系统组件），`PdfDocument.LoadFromFileAsync + PdfPage.RenderToStreamAsync(PdfPageRenderOptions.DestinationWidth)` 输出 PNG 流（[MS Learn](https://learn.microsoft.com/zh-cn/uwp/api/windows.data.pdf.pdfpage.rendertostreamasync)）。
- 优点：零分发物、系统维护；缺点：需 TFM 改 `net9.0-windows10.0.19041.0` + 还原 `Microsoft.Windows.SDK.NET.Ref` 投影包（**当前缓存无 → 离线不可行**）；只支持 Windows（与项目 Windows-first 定位吻合但锁死未来多平台）；渲染质量/速度不受控。
- 定位：**当网络恢复且认可锁 Windows 时，可作为免 dll 的备选实现**（渲染后端接口化后可插拔）。本次不采纳。

### 3.3 R3：托管解析/渲染库（离线同样不可行 + 能力不匹配）

- [UglyToad/PdfPig](https://github.com/UglyToad/PdfPig)：纯托管解析（文本/内嵌图提取），**官方无光栅化渲染**（[Issue #647](https://github.com/UglyToad/PdfPig/issues/647)）；社区有 [wieslawsoltes/PdfPig.Rendering.Skia](https://codeberg.org/CalyPdf/PdfPig.Rendering.Skia) 实验渲染器，成熟度/许可需评估——无论哪种都要引 NuGet（离线挡路）。
- PDFsharp/MigraDoc：面向 PDF **生成**，解析不做渲染 → 不适用。
- Aspose/Syncfusion/Apryse：商业授权，排除。
- 结论：留档作「未来联网后」的候选（PdfPig.Rendering.Skia 全托管、跨平台潜力好），当前不阻塞也不采纳。

### 3.4 R4：纯托管「图片型 PDF 提取」（BCL only，当前离线唯一能立即开工的完整实现）

- 思路：自写 PDF 结构解析（tokenizer + xref 表 + 间接对象 + 流对象），按页解析 `/Resources /XObject` 找到 `/Subtype /Image`，滤镜解码：`DCTDecode` → JPEG 直通（Avalonia 原生可解）；`FlateDecode` → BCL `ZLibStream` 解出原始像素 → 自写 PNG 编码器（IHDR/IDAT/IEND + zlib + CRC32，约 120 行）成流；`ASCIIHex/ASCII85/RunLength` 少量行数补齐；`LZW/CCITT/JBIG2/JPX` 与加密、xref-stream/ObjectStream 变体不支持 → 明确占位。
- 覆盖面：**扫描型漫画 PDF（现实绝大多数）可读**；矢量/文字页 PDF、新版工具生成的 ObjectStream PDF、加密 PDF 不可读（V1 明确报错）。类比 YACReader 对 EPUB 的「仅图片型」支持。
- 成本：解析器+滤镜+PNG ≈ 1500–2500 行，核心风险在 PDF 方言长尾与**异常输入健壮性**（本项目测试纪律要求高，长尾修补不可控）；维护成本高于 PDFium 封装。
- 价值：不依赖任何外部资源、可先落地验证「PDF 阅读需求真实存在 + 交互体验」；**可作 R1 的 dll 缺失降级后端**（同一 `PdfComicSource` 门面下按探测选择）。

### 3.5 路线对照表

| | R1 PDFium 原生 | R2 Windows.Data.Pdf | R3 PdfPig(+Skia 渲染) | R4 纯托管图片提取 |
|---|---|---|---|---|
| 许可 | BSD-3 ✅ | 系统组件 ✅ | Apache-2.0/MIT 系 ✅ | 自有 ✅ |
| 当前离线可行 | ✅ 已获取（vendor/pdfium/win-x64，154.0.8035 单文件） | ❌ 投影包需还原 | ❌ 需还原 | ✅ 立即可行 |
| 矢量/文字渲染 | ✅ | ✅ | ✅（实验性） | ❌ |
| 任意宽度光栅化（缩放无损） | ✅ | ✅（DestinationWidth） | ✅ | ⚠️ 内嵌图固有分辨率 |
| 依赖体积 | +10–20MB native | 0 | ~1–2MB | 0 |
| 一次性开发量 | ~0.5–1 周（封装） | 小（被挡） | 小（被挡） | ~1.5–2 周（解析器） |
| 长期维护 | 低 | 低 | 中 | **高（长尾）** |
| 多平台潜力 | ✅ | ❌ | ✅ | ✅ |

**建议：主推 R1；R4 视用户对「立即可用 vs 等 dll」的偏好，可作先行或降级实现；R2/R3 记档。**

---

## 4. 推荐架构设计

### 4.1 总体思路

「PDF 是一本书」在项目里 = **一个新的 ComicSource 实现 + BookKind.Pdf + 扫描/文案几处收口**；阅读器唯一需要新增的能力是「把原生像素缓冲变成页位图」（PDF 矢量页不能走『解一张 JPEG』，要走『按目标宽度光栅化』）。

```
库扫描/目录树/--open（识别 .pdf）
   │ BookKind.Pdf / BookInfo
   ▼
ComicSourceFactory.Open → PdfComicSource
   │ （门面：构造时探测渲染后端）
   ├─ 渲染后端 A：PdfiumNative（pdfium.dll 在 exe 旁 / 数据目录，FPDF P/Invoke）   ← 推荐主路径
   └─ 渲染后端 B：EmbeddedImageExtractor（纯托管，扫描型 PDF 降级/先行）
   ▼
ComicSource 既有接口 + 新增 RenderPageAsync(index, targetWidthPx)
   ▼
ReaderViewModel 解码管线（DecodeAsync / 预取 / 缩略图 / Webtoon 虚拟化）
   ├─ 命中 RenderPageAsync → WriteableBitmap(BGRA) 直通（按宽渲染，缩放不糊）
   └─ 未命中（CBZ/文件夹）→ 原 DecodeToWidth 流路径（零回归）
   ▼
既有 LRU 按宽缓存 / 双页 / RTL / 滚动 / 续读 / 书签（全部复用）
```

### 4.2 接口扩展（Core）

```csharp
// Comics/ComicSource.cs 新增（默认 null → 现有实现零影响）
public readonly record struct RenderedPage(int Width, int Height, byte[] Bgra8888);

public abstract class ComicSource
{
    /// <summary>按目标像素宽光栅化页面（矢量源 PDF 用；图片源返回 null 走流解码路径）。</summary>
    public virtual Task<RenderedPage?> RenderPageAsync(int index, int targetWidthPx,
        CancellationToken ct = default) => Task.FromResult<RenderedPage?>(null);
}

// 工厂新增一行：
if (ext is ".pdf") return new PdfComicSource(path);
```

`BookInfo.BookKind` 增 `Pdf`（`enum BookKind { Zip, Folder, Pdf }`）。

### 4.3 PdfComicSource（新，Core，~300–500 行 + 后端）

- 生命周期：`EnsureOpen` 惰性打开（沿用 Zip 源的 `_gate` 串行化纪律：持锁不 await；渲染在 `Task.Run` 执行）。
- 后端选择：构造时探测——`AppContext.BaseDirectory`/数据目录下有 `pdfium.dll` → A；否则按配置尝试 B（图片提取）；都不行 → `OpenPageAsync/RenderPageAsync` 抛带中文说明的 `NotSupportedException`（库扫描照旧跳过，打开时提示）。
- `PageCount`：A：`FPDF_GetPageCount`（毫秒级）；B：解析页面树计数。
- `GetPageSize(i)`（同步，UI 构造期调用）：返回**标准光栅密度（canonical 150→300 dpi 讨论见 §5.3）换算的像素尺寸**，仅用于宽高比/布局/质量档；A：`FPDF_GetPageWidthF×dpi/72`；B：页面 MediaBox。
- `GetPageSizeAsync`：同上（默认同步即可）。
- `RenderPageAsync(i, w)`：A：目标宽 → dpi = w·72/MediaBoxW → `FPDFBitmap_CreateEx` 渲染 BGRA，拷贝为 `RenderedPage`（渲染串行化 + 及时释放 FPDF 对象）；B：内嵌图按需缩放输出（无缩放=原分辨率，放大略糊属预期，明确写进 V1 边界）。实现中把「目标宽」钳到质量档上限（见 §5.3）。
- `OpenPageAsync(i)`：兼容兜底——以默认宽（如 2048）渲染后经 PNG 编码为流（ThumbnailService 等通用调用方若不迁移也能工作；仍建议迁移走 RenderPageAsync 以省编码）。
- `ReadMetadataAsync`：A：`FPDF_GetMetaText`（Title/Author）→ `ComicMetadata{Title}`；Series 由扫描器传（同 CBZ）。B：解析 `/Info`。
- `DisposeAsync`：幂等释放文档句柄（照抄 Zip 源 P0-1 教训）。

### 4.4 解码管线改动（App）

`ReaderViewModel.Decode.cs` 三处加分支（DecodeAsync / PrefetchDecodeAsync / GetThumbnailAsync）：

```csharp
var rp = await _source.RenderPageAsync(physical, want);   // 仅 PDF 非 null
Bitmap? decoded = rp is { } page
    ? BitmapFromBgra.ToBitmap(page)                        // WriteableBitmap 直通
    : await Task.Run(() => Bitmap.DecodeToWidth(ms, want)); // 原路径
```

- `BitmapFromBgra`（新小助手，App）：`new WriteableBitmap(new PixelSize(w,h), new Vector(96,96), PixelFormat.Bgra8888, AlphaFormat.Opaque)` → `Lock()` 拷贝 → 返回。已核实 **Avalonia 12.1.1 存在 `Avalonia.Media.Imaging.WriteableBitmap`**（含目标 ctor）；headless 后端可用性放 P0 冒烟验证，不行则回退「Core 出 PNG 流走原路径」（代价仅每次 +一次编码/解码，架构不变）。
- 缓存键语义不变：同页不同宽命中不同缓存条目 → 放大时既有 `CurrentPageNeedsRedecode`/`PrefetchDecodeAsync` 宽检查自动触发 PDF 按新宽重渲染（**v3 清晰度体系对 PDF 免费生效**）。
- 预取并发闸 `_prefetchGate`(2) 与 Webtoon 虚拟化按宽解码（`GetPageBitmapAsync(p, want)`）同样走 RenderPageAsync，PDF 滚动模式直接可用。

### 4.5 扫描 / 目录 / 打开收口（每处都是小改）

| 文件 | 改动 |
|---|---|
| `Core/Library/LibraryScanner.cs` | 仿 `AddZipBooksAsync` 增 `AddPdfBooksAsync`（`*.pdf` TopDirectoryOnly，坏文件跳过）；`MakePdfBookAsync` 读页数/Title（`/Info` 或文件名）→ `BookKind.Pdf` |
| `Core/Library/FolderTree.cs` | 文件型书收集谓词含 `.pdf`（第 26–30 行处）；`MakeBookAsync` 按扩展名分发 |
| `Core/Library/BookInfo.cs` | `BookKind.Pdf` |
| `App/App.axaml.cs`（`--open` 分支） | 文件型 Kind 由扩展名推断（现写死 Zip → PDF 会被当 CBZ 打开）；抽小助手 `BookKind.ForPath` |
| `App/ViewModels/MainWindowViewModel.cs`（~L597-607） | 文件型书集合谓词由 `Zip` 泛化为 `Zip or Pdf`（目录浏览器混合展示） |
| `App/ViewModels/BookItemViewModel.cs` | Subtitle 文案 + 重命名保留扩展名逻辑泛化（现特判 `BookKind.Zip`） |
| `App/ViewModels/FolderEntryItemViewModel.cs` / `Views/PropertiesDialog.cs` | 类型文案「PDF」（现仅 CBZ/文件夹） |
| `App/Services/ThumbnailService.cs` | 优先走宽感知路径（或保留 OpenPageAsync 兜底也可用） |
| `Storage/`（进度/书签/设置） | 零改动（path 键） |

### 4.6 元数据与阅读方向（P2 可选项）

- PDF `/Info Title` 进 `ComicMetadata.Title`；Series/Volume 沿用「父目录名 + 文件名卷号」规则，与 CBZ 完全一致 → 系列聚合/排序免费获得。
- 阅读方向：PDF 无 ComicInfo 的 Manga 字段；`/ViewerPreferences /Direction /R2L` 部分日漫 PDF 有，PDFium 无 API 读取 → 走文件头轻解析（B 后端顺带）或 P2 再议。V1 按设置默认方向。

---

## 5. 关键设计点与取舍

### 5.1 为什么走「RenderPageAsync 按宽渲染」而非「高 DPI 光栅一次」

图片书质量档的语义是「上限 = 原图固有像素宽」；PDF 矢量页**没有固有像素**，只有渲染预算。若固定 300dpi 光栅一次，4K 屏 fit-width 下仍清晰，但再放大即糊；按宽渲染（渲染目标宽 = 显示需求宽，钳到质量上限）让 v3 的「放大重解」语义对 PDF 成立：**缩放任何倍数矢量内容都锐利，CPU 只花在当前显示需求**。这也是唯一需要动的解码管线点。

### 5.2 线程/内存纪律（沿用本仓既有模式）

- 渲染放线程池 + 每源 `_gate` 串行化（PDFium 文档级并发渲染是雷区，串行最省心，毫秒级影响可接受；后续如需并发再按文档分实例）。
- FPDF 对象（document/page/bitmap）全部 try/finally 显式释放；每页 BGRA 为一次性缓冲，出 RenderPageAsync 即拷走并释放 FPDFBitmap，峰值 ≈ 单页 × 渲染宽（2048 宽 A4 页 ≈ 16MB 瞬态，与现有 4K JPEG 解码峰值同量级）。
- PdfComicSource.Dispose 幂等 + 关闭后不重开（P0-1/P2-3 教训直接套用）。

### 5.3 尺寸与质量档语义（PDF 专用映射）

- `GetPageSize` 返回 canonical 像素：`pts × Dpi/72`，**Dpi 取 150（P0 冒烟后定 150/200/300 三档实测内存与观感）**；宽高比与 Dpi 无关，布局不受影响。
- Auto 档 = 「按显示宽渲染，上限 = canonical 宽（≈150dpi 扫描级）」——语义上对齐「原图分辨率」，放大超过 canonical 会重渲染到上限后封顶（与图片书「放大到原图上限」体验一致）；Limit/Original 档 = 上限 4096/更高（Original 对 PDF 建议上限 `max(canonical, 2×canonical)` 并受内存预算约束，P0 实测后落常量或设置项）。
- 设置项：V1 复用既有三档预设与 DecodeLimit 滑块，不新增 UI；PDF 专属项（默认渲染密度）P2 再进设置。

### 5.4 错误与边界（沿用「跳过不崩」哲学）

- 扫描：单 PDF 打开失败（加密/损坏/后端缺失）→ skip + 日志（同 AddZipBooksAsync catch 模式）。
- 打开阅读：页级渲染失败 → 既有占位/StatusText 机制（坏页占位已在滚动模式验证过）；整书失败（如加密）→ 第一页渲染返回明确错误文案「PDF 已加密或不受支持，请提供 pdfium.dll / 使用无加密版本」。

---

## 6. 测试计划

**测试夹具（不依赖任何外部文件，测试内程序化生成）**：`PoC/PdfFixtureWriter.cs` —— 手写最小合法 PDF（xref 表 + 页树 + 内嵌 DCTDecode JPEG 小图 与 FlateDecode RGB 各一），支持 1/3/N 页、含横向页（双页判定用）、坏文件、带 Encrypt 字典的加密形态。生成逻辑本身可单测（`%PDF` 头/xref 偏移校验）。

| 层 | 用例 |
|---|---|
| Core 单测 | PdfComicSource：PageCount / GetPageSize（比例正确）/ RenderPageAsync 输出宽=请求宽且像素非空 / OpenPageAsync 兜底流可解 / 加密与坏文件抛受控异常 / Dispose 幂等 |
| 扫描 | LibraryScannerTests 扩：pdf 收集（含坏 pdf 跳过）、Title/卷号规则、与 Folder/Zip 混合排序 |
| 布局 | 横向 PDF 页走「单页」规则（PageLayouter 复用，无需改，回归即可） |
| App headless E2E | 打开 fixture PDF → 翻页/双页/主题/续读/书签（复用 ReaderE2eTests 基建，把书换 PDF）；WriteableBitmap 直通冒烟（P0） |
| 降级后端（若做 R4） | 结构解析器：DCTDecode 直通、Flate+PNG 编码往返像素一致、加密/缺失滤镜占位不崩 |
| native 缺失策略 | pdfium 相关用例在 dll 不存在时 `Skip`（沿用 5000p 压力书跳过先例），CI/本地无 dll 也能全绿其余 |

回归基线：当前全量测试（最近提交 100/100）须保持全绿；渲染/解码改动均走既有缓存/竞态用例（BUG-004/scroll-mode-fix 系列）防回归。

---

## 7. 风险与对策

| 风险 | 等级 | 对策 |
|---|---|---|
| pdfium.dll 获取 | ✅ 已消解 | 用户已下载 154.0.8035 win-x64，P0 实测加载/渲染/像素全通过，存放 `vendor/pdfium/win-x64/` |
| pdfium.dll 依赖集（v8/icu 附加 dll） | ✅ 已消解 | 实测为单文件 6.93MB、零附加依赖 |
| WriteableBitmap 在 Avalonia 12 headless/实际渲染的兼容 | 低-中 | P1 首步冒烟；回退方案 = Core PNG 流路径（架构不变，仅多一次编解码） |
| PDF 渲染性能（高 DPI 矢量页 CPU） | 低-中 | 实测 1200px ≈ 36–63ms（与现有大图解码同量级）；按宽渲染 + 钳上限 + 既有 LRU/预取闸；密度档 P1 实测后定 |
| PDFium 非托管句柄泄漏/崩溃 | 中 | 显式释放 + 每源串行化 + crash 日志兜底（App 已有全局异常记录）；正式实施前用真实漫画 PDF 样本复测 |
| 大 PDF 扫描耗时（读页数） | 低 | FPDF_GetPageCount 毫秒级；文件访问回调直读不整载 |
| 中文路径（FPDF_LoadDocument 需 UTF-8） | 低 | P/Invoke 手动 UTF-8 编码路径字节（P0 已识别，见 3.1a） |

---

## 8. 工作量与阶段划分（单人，含测试与文档）

| 阶段 | 内容 | 估时 |
|---|---|---|
| P0 风险消解 | ✅ dll 获取 + 加载/渲染/像素验证已通过（见 §3.1a）；剩余：WriteableBitmap 冒烟、PdfFixtureWriter 正式化、密度档位实测（并入 P1 首步） | 0.5–1 周（已基本完成） |
| P1 主链路 | PdfComicSource + 渲染后端封装/解析器 + RenderPageAsync 管线接线 + BookKind.Pdf + 扫描/目录树/--open/文案收口 + 全部测试 | 1–1.5 周 |
| P2 打磨 | 质量档/密度调优、缩放重渲性能、错误体验（加密提示）、文件管理细节（重命名/属性）、可选阅读方向探测 | 0.5–1 周 |
| **合计（R1）** | | **~2.5–3.5 周** |
| R4 替代（若选纯托管先行） | P1 中的后端替换为解析器（~1.5–2 周），总时长相当但覆盖打折，长尾另计 | |

**难度评级小结**：架构承接 ★（低）；渲染后端集成 ★★☆（中，难点=原生供应链而非代码）；阅读器管线改造 ★★（中）；纯托管实现 ★★★（中-高，长尾）。综合 **中**；若坚持零外部依赖 → **中-高**。

---

## 9. 待用户决策项

1. ✅ **主后端路线已定**：R1 = PDFium 原生（用户已拍板）
2. ✅ **pdfium.dll 已获取**（154.0.8035，win-x64 单文件），暂存 `vendor/pdfium/win-x64/`（已 gitignore）；正式集成时的分发位置建议：**exe 旁主目录**（随绿色版分发）+ **数据目录 `%LOCALAPPDATA%\HachimiManga\pdfium\` 兜底**（升级不覆盖）——实施时定稿
3. **V1 范围确认**：加密 PDF 仅提示不支持（不做密码输入 UI）——可否？（PDFium 可探到 `FPDF_ERR_PASSWORD` 并明确提示）
4. **是否顺带把「文件型书」判定（Zip/Pdf）抽成 `BookKind.ForPath` 统一三处入口**（低风险重构，顺手消除 --open 把 PDF 当 CBZ 的隐患）——默认建议做
5. （新增）**实施验收样本**：实施后建议用 1–2 本真实漫画 PDF（用户自备）做端到端验收——用户可否提供样本路径？

---

## 参考链接

- 二进制渠道：[bblanchon/pdfium-binaries](https://github.com/bblanchon/pdfium-binaries)（BSD-3 预编译 pdfium.dll）；原生包示例 [bblanchon.PDFium.Win32 on NuGet](https://libraries.io/nuget/bblanchon.PDFium.Win32/120.0.6084)
- 同思路 .NET 封装（参考 API 设计，本项目不引包）：[PdfiumCore](https://relatedrepos.com/gh/Dtronix/PDFiumCore)、PDFiumViewer、Docnet.Core（[介绍](https://cloud.tencent.cn/developer/article/2542445)）
- [PdfPig：无渲染器声明 Issue #647](https://github.com/UglyToad/PdfPig/issues/647)；社区渲染器 [PdfPig.Rendering.Skia](https://codeberg.org/CalyPdf/PdfPig.Rendering.Skia)；[PdfPig 2026 综述](https://hackernoon.com/pdfpig-c-review-a-focused-open-source-pdf-library-in-2026)
- 系统备选：[Windows.Data.Pdf PdfPage.RenderToStreamAsync](https://learn.microsoft.com/zh-cn/uwp/api/windows.data.pdf.pdfpage.rendertostreamasync)（离线被投影包挡路，记档）
- 项目内依据：`docs/01-research/tech-stack-eval.md`（PDFium 生态表）、`docs/02-design/decision-log.md`（PDF 预留 V2 扩展点）、`docs/02-design/roadmap.md` M3 格式扩展
