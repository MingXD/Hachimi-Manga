# 2026-12-24 PDF 格式支持（v4-PDF）

## 任务概述
- 目标：为 Hachimi Manga（MangaAgent）适配 PDF 格式 —— 调研与方案设计先行，用户确认后干净实现
- 结果：PDFium 渲染集成完成，测试 100 → **112/112 全绿**，已提交 `542875a` 并推送

## 过程简述
1. 方案设计阶段（纯调研+文档，零代码）：摸清 ComicSource 抽象/解码管线/扫描链路；核实**离线约束**（nuget.org 不可达、缓存零 PDF 库、SDK 无 WinRT 投影包、本机无 pdfium.dll）→ 方案锁定"手写 FPDF P/Invoke + 自备 dll"路线，产出 `docs/02-design/pdf-format-support.md`
2. 用户下载 pdfium.dll（154.0.8035 单文件 6.93MB）→ 隔离冒烟工具（`vendor/pdfium/smoke/`，PS5.1 + Add-Type）验证：加载/打开/渲染/像素级颜色正确性/性能实测（1200px 页 36-63ms）
3. 实施：Core（接口扩展→PdfiumNative→PngEncoder→PdfComicSource→扫描/目录树/BookKind）→ App（WriteableBitmap 直通→解码三处分支→质量档映射→文案/标识/重命名/--open/设置提示/guide）→ 测试（程序化 PDF 生成器 + 单测 + Headless E2E）
4. 提交推送，112/112

## 遇到的问题
### 问题 1：沙箱 runner 崩溃（临时目录缺失）
- **现象**：任何 pwsh 命令报 `windows-acl-run: --temp is not an existing directory`，含只读命令
- **原因**：沙箱后端（ACL restricted-token runner）的临时目录 `C:\Users\YMK\AppData\Local\Temp\dsh-9FjMj8` 不存在/不可用；非权限问题
- **解决**：请求升 danger-full-access 执行（当时策略 ask 放行）——之后用户把会话策略切到 danger-full-access，全流程无沙箱

### 问题 2：代理端口在监听但命令沙箱连不出去
- **现象**：探测到 Clash 端口 7897 开放；`Invoke-RestMethod -Proxy` 全部握手失败；curl 报 `SEC_E_NO_CREDENTIALS`
- **原因**：DSH 的 pwsh 是 **Windows PowerShell 5.1**（默认 TLS1.0/1.1，无 -SkipCertificateCheck），且沙箱进程证书/网络环境受限；试了 TLS1.2 强制仍失败
- **解决**：放弃自动下载，指导用户在浏览器（有翻墙）手动下载 pdfium-win-x64.tgz 放到 vendor 目录——比在沙箱里折腾代理省时

### 问题 3：冒烟渲染 AccessViolation / 全灰
- **现象**：① 渲染后 Marshal.Copy 崩 `AccessViolationException`；② 修好后渲染输出全灰（RGB 三通道相等=Rec.601 亮度，像素级校验 red=0）
- **原因**：① **`FPDFBitmap_BGRA` 常量写错**：枚举 UNKNOWN=0 GRAY=1 BGR=2 BGRx=3 **BGRA=4**，写了 1（灰度格式）→ 按 4 倍长度越界读；② **渲染 flags 含 `FPDF_LCD_TEXT`(0x08)**：实测开启后 pdfium 整页输出灰度化（flags=0/1 正常彩色，flags=8/9 灰度）
- **解决**：format 常量改 4、flags 只用 `FPDF_ANNOT`(0x01)。用"渲染 4 种 format × 4 种 flags 对照 dump 字节"定位，一次实测定案（非猜）

### 问题 4：PS 5.1 Add-Type 连环坑（C#5 编译器 + 缺引用 + out 参数）
- **现象**：本地函数报"应输入 }"（C#7 特性）；System.Drawing.Imaging 找不到（缺 -ReferencedAssemblies）；C# out 参数经 PS [ref] 回传失效（SavePng 收到空宽高）
- **原因**：PS5.1 Add-Type 用 .NET Framework 编译器（最高 C#5），默认只引 mscorlib/System
- **解决**：冒烟代码降到 C#5（委托替代本地函数）、加 `-ReferencedAssemblies "System.Drawing.dll"`、把需要回传的逻辑挪进 C# 内部方法。**教训：冒烟验证用独立 PS 脚本摩擦大，正式代码走 .NET 项目编译才是正路**（smoke 只为 P0 验证，刻意未入主工程）

### 问题 5：文件名卷号 fallback 从未生效（现存 bug）
- **现象**：新增 `Scans_Pdf_Books` 断言 Volume="4" 失败（null）——CBZ 场景被 ComicInfo.xml 掩盖
- **原因**：`ParseVolumeFromName` 正则 `@"(?i)\b(?:vol|v).?s*(\d+)"` 中 `\b` 在 C# 字符串里是**退格字符 U+0008**（非正则词边界），模式永不可能匹配；且 `.?s*` 意图不明。第一次修复用 `\b` 仍失败：`_vol` 的下划线是 `\w`，词边界不成立
- **解决**：改 `(?i)(?<![a-z0-9])(?:vol|v)\.?\s*(\d+)`（前向负断言），`TestSeriesA_vol4` → "4" 通过。**教训：C# 字符串里的正则转义要写双反斜杠；匹配器写完后用真实文件名矩阵验证**

### 问题 6：行尾混用（新文件 LF vs 仓库 CRLF）
- **现象**：write 工具产出 LF 文件；仓库 cs 约定 CRLF；edit 对部分 LF 文件的多行匹配失败
- **原因**：工具默认 LF；项目无 .gitattributes，靠约定
- **解决**：收尾统一转换新增文件为 CRLF（UTF-8 无 BOM）。**后续会话：写完新文件立即转 CRLF，别拖到最后**

### 问题 7：离线 NU1900 警告噪音
- **现象**：每次构建刷 `NU1900 无法加载 nuget.org 漏洞数据`
- **原因**：无外网
- **解决**：忽略（无害）；不要尝试加源/联网

## 最耗时项
- **P0 冒烟踩坑（format/flags 两个坑 + PS 环境连环坑）**：约 1.5-2 小时。原因：native 互操作 bug 只能靠实测对照定位，而 PS5.1 环境又叠加了 C#5/引用/out 三坑。教训：P/Invoke 常量先核对官方头文件（fpdfview.h 枚举）再写代码；flags 不要想当然（LCD_TEXT 灰度坑只有实测能发现）；隔离验证优先 .NET 项目而非 PS Add-Type

## 最耗 token 项
- **代码实施 + 测试循环**（约占总 token 60%）：改动面广（Core/App/Tests 三工程 20+ 文件）且每步需读旧代码对齐风格（项目注释密集、bug 历史长）。教训：变更前先 grep 全部 kind 谓词/文案点建立清单，避免漏改（搜索命中/统计 M/N/X 这类隐蔽点就是靠清单抓到的）

## 规避与完善方案
- [ ] 新写 .cs/.axaml 文件后立即转 CRLF（UTF-8 无 BOM），避免收尾批量转换与 edit 匹配问题
- [ ] P/Invoke 枚举/常量写代码前先查官方头文件（vendor/pdfium/extracted/include/fpdfview.h 已随包保留）
- [ ] pdfium dll 升级时跑 `vendor/pdfium/smoke/run-smoke.ps1` + `Tests/Pdf` 全套做兼容回归（API 稳定但 format/flags 行为以实测为准）
- [ ] 本机有 Clash 代理（7897）时，需要联网的下载直接请用户浏览器操作，不在沙箱 pwsh 里折腾 TLS/代理
- [ ] PDF 真实样本验收（用户提供 1-2 本真实漫画 PDF）：加密 PDF 提示文案、超大 PDF 性能、矢量漫画 PDF 观感

## 相关文件 / 提交
- 提交：`542875a` feat(v4-PDF): PDF 格式支持——PDFium(P/Invoke)按宽光栅化渲染、canonical 300dpi 尺寸语义、清晰度档位 PDF 映射、扫描/目录树/标识/搜索/重命名全收口；修复文件名卷号解析正则(\b 退格字符)；112/112 通过
- 核心文件：`app/src/MangaAgent.Core/Comics/Pdf/{PdfComicSource,PdfiumNative,PngEncoder}.cs`、`app/src/MangaAgent.App/ViewModels/ReaderViewModel.Decode.cs`、`app/Directory.Build.targets`、`docs/02-design/pdf-format-support.md`、`docs/03-dev/v4-pdf-changes-record.md`
