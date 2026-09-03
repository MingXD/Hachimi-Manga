# BUG-008：目录页 PDF 书名空白（/Info Title 解析出 NUL）

- **状态**：✅ 已修复（2026-12-24，实施含净化下沉 + 中文卷号解析）
- **引入**：v4-PDF（`542875a`）
- **用户反馈**：目录页（目录浏览器）里 PDF 漫画的"名称不见了"（卡片只剩封面/页数，标题空白）

## 现象
- 用户真实库 `G:\影视\晚安，布布\WA,BB 第N卷.pdf`（12 卷）：目录卡片**书名为空**，其他信息（页数 228-275）正常
- 复现（Core 扫描层实测）：`BookInfo.DisplayName = ''`；`Title` 为**单个 NUL 字符 U+0000**（LEN=1, CODES=[U+0000]）

## 根因（三层链）
1. **PDF 文件侧**：这 12 卷的 `/Info` 里没有有效 Title（转换器写入了空/无效 Title）→ PDFium `FPDF_GetMetaText(doc,"Title")` 返回**空值**（仅有 NUL 终止）
2. **P/Invoke 层 `Fpdf.GetMetaText`**：第一遍拿长度后，第二遍把内容按 `PtrToStringUni(buf, len-1)` 读出——空标题场景把**终止 NUL 当作 1 个真实字符**返回（得到 `"\0"` 而非 `null`）
3. **`PdfComicSource.ReadMetadataAsync`**：`string.IsNullOrWhiteSpace(title)` 对 `'\0'` 返回 **false**（NUL 不是空白字符）→ NUL 标题通过净化 → 扫描器 `meta.Title is { Length: > 0 }` 也放行（长度 1）→ `DisplayName`（Title）渲染为空白

> CBZ 没这问题：ComicInfo.xml 缺 Title 时解析器给 null → fallback 文件名。PDF 的元数据接口把"空"编码成 NUL 字符，等于隐性 bug 触发条件。

## 方案（改动点 / 影响 / 测试）
**核心：元数据字符串净化下沉到 P/Invoke 层（一次修复，所有调用方受益）**

| 改动 | 内容 |
|---|---|
| `Core/Comics/Pdf/PdfiumNative.cs` `Fpdf.GetMetaText` | 返回前净化：剔除 `U+0000` 与全部 C0 控制字符/Unicode 格式符（Cf，含零宽 U+200B 等）→ `Trim()` → 为空返回 `null` |
| `Core/Comics/Pdf/PdfComicSource.cs` `ReadMetadataAsync` | 不变量不变（净化为 null 后现有 `IsNullOrWhiteSpace` 判断自然命中；保险起见直接以净化结果为准） |
| 效果 | 空/脏 Title → null → LibraryScanner/FolderTree 自动 fallback **文件名**（"WA,BB 第10卷"）→ 卡片正常显示，与同目录 CBZ 行为一致 |

- **影响面**：仅 PDF 元数据读取路径；无 UI/管线改动
- **测试**：
  - 单测（`PdfTestWriter` 增加可写 `/Info << /Title (…空/含 NUL…) >>` 的形态）+ `ReadMetadataAsync` 断言 null
  - 回归用例：净化函数对 `"\0"` / `"\0ABC\0"` / 零宽混入 / 首尾空格 的判定
  - 真实样本复验：扫描用户 12 卷目录 → 全部书名 = 文件名
- **风险**：低。净化只影响"无有效标题"场景；正常中文/英文标题不受影响（控制字符/格式符在正常标题中不存在）

## 关联
- 顺带发现（不属本 bug）：`ParseVolumeFromName` 不解析中文卷号（"第10卷"）→ 同系列 PDF 卷排序为字典序（第10卷 排在 第1卷 前）。属"各格式体验一致"盘点项，方案见 BUG-008 关联回复/体验盘点，待用户决定是否一并修。
