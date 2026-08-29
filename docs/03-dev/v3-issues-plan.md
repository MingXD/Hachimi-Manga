# v3 问题排查与解决方案（14 项）— 2026-08-25（v2）

> 状态：**只排查 + 出方案，未实施**。等用户决策后按优先级分批实施。
> v2 更新（用户已答复部分决策 + 新增 #14 排序问题）：
> - 新增 **#14 封面错乱（排序 bug）**，根因已定位
> - #13/#12 配对语义按用户新定义重写：起点1=(0,1)起对、起点2=第1页单页后(1,2)起对
> - #9 中键行为确认：按一次进入滑动模式 → 移鼠标控速 → 再按一次退出（toggle，可入快捷键）
> - #11 清晰度策略确认：写入设置（工作量评估见 §11）
> - #5 其他文件确认：仅目录浏览器显示、不参与搜索、但有完整基本操作

## 0. 结论总表

| # | 问题 | 代码确认结果 | 根因/现状 | 优先级 |
|---|---|---|---|---|
| 1 | 删除/移动及时刷新 + 进度条 | 已有刷新但不同步目标库；复制/移动**同步阻塞无进度** | `FileOps.CopyTo/MoveTo` 同步执行；`RefreshAction` 只刷源目录 | **P1** |
| 2 | 开启时默认阅读顺序 | **无默认方向设置**；另有续读 Webtoon 丢失 | `State.cs` 无状态时 `Direction=Rtl` 硬编码；`SaveState` 把 Webtoon 存成 Single | P1 |
| 3 | 跨资料库移动失败 | **确认 bug（静默失败）** | `File.Move/Directory.Move` **不支持跨卷**（IOException 被 catch 吞掉 → 无提示） | **P0** |
| 4 | 资料库页资源管理器式快捷键 | **完全没有**（仅 F12） | `MainWindow.axaml.cs` 无 Ctrl+C/V/X/侧键处理 | P2 |
| 5 | 其他文件类别 | **其他文件被忽略**（已确认范围：目录浏览器显示、不参与搜索、完整基本操作） | `FolderTreeScanner` 只收 cbz/zip/图片目录 | P2 |
| 6 | 新建文件夹 | **无此功能** | 无 NewFolder 命令/入口 | P2 |
| 7 | 阅读中查看图片信息 | **无**（库属性框不含分辨率） | `_sizes`(PageSize) 已有，未暴露到阅读器 UI | P2 |
| 8 | 一键首页 | **无**（需跳页框输入 1） | `ReaderAction` 枚举无 Home；工具栏无按钮 | P2 |
| 9 | 中键滑动模式 + 滚动帧率 | **无中键滑动**；动画 16ms DispatcherTimer | 行为已确认（toggle）；帧率优化点已定位 | P1 |
| 10 | 翻页先模糊后清晰 | **无渐进加载**（解码完成才换图） | `LoadCurrentAsync` 单次解码 | P1 |
| 11 | 图片是否最清晰 | **默认上限 2048px，多数情况降采样**；已确认策略写入设置 | `_maxDecodeWidth=2048`；want 基于 BaseH=800 固定基准 | **P0** |
| 12 | 封面参与配对 | 封面恒单页 → **并入 #13 新语义** | `CoverSingle=true` | （并入13） |
| 13 | 配对起点逻辑 | 语义按用户重定义；"从第6页起配对"= 横向页单页打断配对 + 切换不保持物理页 + 不持久化 | 见 §13 | **P0** |
| 14 | **后记成为封面（排序 bug）** | **确认 bug** | `ImageInfo.CompareNatural` **不对称**（两个方向结果矛盾）+ 数字/字母优先级反了 | **P0（改动最小，最先做）** |

---

## 14. 后记成为封面 — 页面排序 bug（P0，先做）

**现象**：漫画页命名 `_1`、`_2`…（或 `0001`、`0002`…），后记命名如 `xy`，结果后记成为第 1 页（封面）。

**根因（代码确认）**：`Comics/ImageInfo.cs` 的 `CompareNatural`：
```csharp
if (ia < ma.Count) return 1;    // ← 反了：a 还有数字块，应排前（数字优先）
if (ib < mb.Count) return -1;   // ← 反了：b 还有数字块，应排前
```
- 比较 `"xy"` vs `"_1"`：`xy` 无数字、`_1` 有数字 → 命中第二分支返回 **-1 → xy 排前面**
- 反方向比较 `"_1"` vs `"xy"`：进不了数字循环 → 末尾 `string.Compare` → `_1` 排前面（-1）
- **同一对元素两个方向都返回 -1 → 比较器违反反对称性** → `List.Sort` 结果不可预测（.NET 排序要求比较器一致），字母页经常窜到数字页前
- 两个 source 都走此比较器：`ZipComicSource`（按 `entry.FullName`）、`FolderComicSource`（按文件名）→ 后记成封面、页面顺序错乱
- Windows 资源管理器排序没这问题（StrCmpLogicalW 自然排序，数字块按数值且数字优先）

**方案**：
1. 修复 CompareNatural 两个分支：
   ```csharp
   if (ia < ma.Count) return -1;   // a 还有数字块 → a 排前（数字优先，对称）
   if (ib < mb.Count) return 1;    // b 还有数字块 → b 排前
   ```
2. 加**对称性回归测试**：任意 (a,b) 断言 `Compare(a,b) == -Compare(b,a)`；`_1.._10 + xy` → xy 最后；`0001,0002,xy` → xy 最后；`a1 vs b` 等边界
3. 调用点检查：Zip/Folder 源（页序）→ 修复后封面即第一张数字页；`FolderTree`/`LibraryScanner` 的书名排序用的是 `OrderBy(OrdinalIgnoreCase)`（纯字符串，不受影响，但可顺手统一用自然排序提升一致性——低优先）

**涉及**：`Core/Comics/ImageInfo.cs`、`Tests/Paging/PageLayouterTests.cs` 或新建 `Comics/ImageInfoTests.cs`
**工作量**：极小（2 行修复 + 测试）

## 13. 双页配对逻辑（P0）— 语义按用户重定义

### 新语义（用户定义，覆盖原 #12/#13）
- **配对起点1**：第 1 页 + 第 2 页成对，即 `(0,1)(2,3)...`（封面参与配对，开头无单页）
- **配对起点2**：第 2 页 + 第 3 页成对，即第 1 页单页 + `(1,2)(3,4)...`（因为有的漫画没有封面，第 1 页当独立封面/扉页）
- 实现：`PageLayoutOptions` 去掉"封面恒单页"独立开关，`FirstPairOffset` 重定义为：
  - `0` → 直接 `(0,1)` 起对
  - `1` → 页 0 单页，`(1,2)` 起对
  - 当前实现 `CoverSingle=true + FirstPairOffset` 的"封面单页"行为 = 新起点2，**起点1 是新增行为**

### "从第 6 页开始配对"的处置（用户问：取消逻辑还是进设置？——我的判断）
- **根因**：`AspectRatio ≥ 1.6` 的页强制单页（横向跨页图规则）会打断配对序列——那本书前面有多张横图，配对从横图之后重新开始，看起来"从第 6 页开始配对"
- **建议：不取消，移入设置**（默认保留 1.6）：
  - 理由：横向跨页图被配对会拉变形/裁切，单页整张显示是漫画阅读正确语义；该规则本身不是 bug
  - 设置项：`SpreadThreshold` 可调（如 1.4 / 1.6 / 1.8 / 关闭"横向页单页"），关闭后全部参与配对
  - 配合：配对按钮 ToolTip/信息面板说明"横向页单页显示"，用户可理解并自行开关
- 若用户仍觉得困惑，可再加"配对从当前物理页重新开始"的局部重排——**不建议**（复杂度高收益低），先做设置项

### 其余修复（原 §13 保留）
1. **配对切换保持当前物理页**：`TogglePairOffset` 记录 `CurrentPhysical` → `RebuildLayout` 后 `SetCurrentPhysical(旧物理页)` 重定位（画面留在同一页，只变配对结构——根治"按钮没反应/画面跳走"）
2. **尺寸补全后再重建**：切换时若 `SizesLoaded==false`，等后台补全完成后的 RebuildLayout 一次性生效（页数多的书不再"改了晚生效/乱跳"）
3. **PairOffset 持久化**：写入 `settings.json`（全局默认配对起点），打开书时读入；书内切换即时生效+保存
4. 配对按钮 UI：改为菜单/下拉（起点1 / 起点2 / 横向页单页开关），ToolTip 说明规则

**涉及**：`Core/Paging/PageLayouter.cs`、`ViewModels/ReaderViewModel.cs`、`Views/ReaderWindow.axaml`、`Services/SettingsStore.cs`、`Tests/Paging/PageLayouterTests.cs`、`Tests/PoC/PairOffsetSwitchTests.cs`

## 11. 图片清晰度（P0）— 策略写入设置（用户确认）

**现状**：解码宽度 `want = ComputeBaseW() * _scale * dpr`，clamp `[1200, _maxDecodeWidth]`，默认上限 2048；布局基准 `BaseH=800` → 1:1 显示也远小于原图 → 普遍降采样；原图尺寸 `_sizes` 已有未参与决策

**方案（写入设置）**：
1. `AppSettings` 加 `DecodeQuality`（下拉）：
   - `自动`（默认）：`want = min(显示需求, 原图宽度)`，显示需求不足原图时按需；放大超过原图 → 用原图全尺寸（上限=原图宽，取代 2048 主观上限）
   - `限幅 4096`：上限 4096（内存友好）
   - `原图全尺寸`：永远解原图宽（内存峰值大，CacheCap 兜底）
2. `_maxDecodeWidth` 语义保留但默认逻辑改为上述三档；`MaxDecodeWidth` 数值设置项可保留为"限幅"档的参数
3. 配合 §7 信息面板：显示"原图宽 vs 当前解码宽 vs 显示需求"，让用户直接确认清晰度
4. 先做基准：4K 页全尺寸解码耗时/内存 vs 2048（用 fixtures 压力书）
5. **工作量评估**：设置项 + 设置 UI = 小（0.5d 内）；解码决策改造（want 计算贯穿 Canvas/Decode/Webtoon 三处）= 中；测试 = 小。总计约 **1~2 个工作日**量级（含验证）

**涉及**：`Views/ReaderWindow.Canvas.cs`、`Views/ReaderWindow.Webtoon.cs`、`ViewModels/ReaderViewModel.Decode.cs`、`Services/SettingsStore.cs`、`Views/SettingsDialog.cs`

## 9. 中键滑动模式 + 滚动帧率（P1）— 行为已确认

**确认行为（用户）**：按一次中键 → 进入滑动模式（autoscroll）→ 移动鼠标控制滚动速度/方向 → **再按一次中键退出**（toggle，非按住式）；可写入快捷键系统

**方案**：
1. 视口 `PointerPressed` 中键 → toggle autoscroll；激活时以"按下点 ↔ 当前鼠标位置"的 Y 偏移驱动目标速度（带 deadzone 防抖），每帧在 `SmoothScrollTick` 施加/维持目标速度；再按中键（或 Esc/切页）退出
2. `ReaderAction` 加 `Autoscroll`（键盘可绑，默认不绑或绑 `Insert` 等；中键是鼠标事件不走快捷键表，但动作入口统一）
3. 帧率优化（与 #9 原方案一致）：`DispatcherTimer` → 评估 `CompositionTarget.Rendering`；`TryRefreshWebtoonImage` 加 scale/范围守卫避免每帧遍历；先基准测量再改
4. autoscroll 与拖拽/惯性互斥（进入 autoscroll 取消惯性，退出恢复）

**涉及**：`Views/ReaderWindow.Webtoon.cs`、`Views/ReaderWindow.Input.cs`、`Services/SettingsStore.cs`（ReaderAction）

## 5. 其他文件（P2）— 范围已确认

**确认范围（用户）**：仅目录浏览器显示、不参与搜索；**与文件夹/漫画一样有完整基本操作**

**方案**：
1. `FolderNode.OtherFiles`（路径/名称/扩展名/大小）；`FillAsync` 收集非图片、非 cbz/zip 的普通文件
2. `FolderEntryItemViewModel` 加 `IsOtherFile` 分支：统一图标（📄）+ 名称含后缀 + 大小
3. 右键菜单完整复用：打开（系统默认程序，可选）/复制/移动/删除（回收站）/重命名/在资源管理器中显示/属性（属性框扩展：扩展名/大小/修改时间）
4. 排序：文件夹 → 漫画 → 其他文件（或按名称混合，待定，建议文件夹在前、其余按名称）

**涉及**：`Core/Library/FolderNode.cs`、`Core/Library/FolderTree.cs`、`ViewModels/FolderEntryItemViewModel.cs`、`Views/MainWindow.axaml`、`Core/Library/FileOps.cs`

---

## 1. 删除/移动及时刷新 + 进度条（P1）— 不变
（同步改异步 + IProgress 进度 + 刷新源/目标库；与 #3 同批实施，原文 §1 保留）

## 2. 默认阅读顺序（P1）— 不变
（新增 `DefaultDirection` 设置 + 修 Webtoon 续读丢失，原文 §2 保留）

## 4. 资料库快捷键（P2）— 不变
（Ctrl+C/V/X/Delete/F2/Enter/Backspace/Alt+↑/鼠标侧键 + 导航历史栈，原文 §4 保留）

## 6. 新建文件夹（P2）— 不变（原文 §6 保留）

## 7. 图片信息（P2）— 不变，与 §11 联动（原文 §7 保留）

## 8. 一键首页（P2）— 不变，连带 GoEnd（原文 §8 保留）

## 10. 翻页先模糊后清晰（P1）— 不变（原文 §10 保留）

---

## 优先级与实施顺序（更新）

**P0（数据正确性 / 核心体验，先修）**：
1. **#14 排序 bug**（改动最小，立即可做，顺带回归 67/67）
2. **#3 跨库/跨盘移动失败**（数据操作正确性）
3. **#13 配对逻辑**（新语义 + 横向页入设置 + 保持物理页 + 持久化；含 #12）
4. **#11 清晰度**（三档设置 + 解码决策；需先基准）

**P1（体验增强）**：
5. **#1 进度+刷新**（与 #3 同批异步改造）
6. **#2 默认方向**（含 Webtoon 续读修复）
7. **#10 模糊优先加载**
8. **#9 中键滑动 + 帧率**（先基准）

**P2（新功能，按需）**：
9. **#8 首页**（成本最低可插队）
10. **#4 快捷键**、**#5 其他文件**、**#6 新建文件夹**、**#7 图片信息**

依赖：#14 独立无依赖（最先）；#3/#1 同改 FileOps；#13 独立（PageLayouter/设置）；#11/#7 共用解码宽度数据。

## 待用户决策的点（剩余）

1. ~~#13 配对语义~~ → **已确认**（起点1=(0,1)，起点2=第1页单页后(1,2)）
2. ~~#9 中键行为~~ → **已确认**（toggle 模式，入快捷键）
3. ~~#11 清晰度策略~~ → **已确认**（写入设置三档）
4. ~~#5 其他文件范围~~ → **已确认**（目录浏览器 + 完整操作，不参与搜索）
5. **#13 横向页处置**：我的建议是**默认保留单页规则（1.6）+ 移入设置可调/可关**，你认可吗？还是直接取消？
6. **#4 快捷键**：先单选版还是多选批量版？（未答复）
7. **#14**：修复方向（数字优先、对称比较器）认可即可开工
