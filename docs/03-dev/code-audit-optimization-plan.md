# 代码整体优化审查报告（Code Audit & Optimization Plan）

> 状态：**审查完成，方案待用户决策**（未实施）。
> 审查范围：app/src/MangaAgent.App（UI/VM/服务）、MangaAgent.Core（领域层）、MangaAgent.Tests（65 测试）。
> 方法：文件规模盘点、静态扫描（TODO/async void/未用成员/警告/订阅）、关键路径人工审读（位图生命周期/滚动/解码/打开书）。

---

## 一、审查结论速览

| 层 | 结论 |
|---|---|
| 正确性 | 1 个真实资源泄漏 bug（DisposeAsync no-op）；无其他崩溃级隐患（历轮已修复） |
| 整洁度 | 死代码/过时注释少量；警告 2+~15 个 |
| 可维护性 | 2 个大文件（1827/740 行）+ 1 个 856 行测试文件 —— 最大痛点 |
| 性能 | 解码/滚动路径已多轮优化；剩余 1 个启动性能点（LoadSizes 同步全扫）+ 日志降噪 + 压力验证 |
| 工程 | 无 .editorconfig；Nullable 已启用；文档需小幅更新 |

---

## 二、问题清单（问题 → 影响 → 方案）

### P0 · 正确性 + 整洁

| # | 问题 | 影响 | 方案 |
|---|---|---|---|
| P0-1 | **`ReaderViewModel.DisposeAsync` 守卫写反**：`_disposed = true; if (_disposed) return;` 恒真 → **DisposeAsync 从不执行任何清理**（SaveState/源释放/SQLite 释放/位图/缩略图全跳过） | **每打开/关闭一本书泄漏**：ZIP 文件句柄、SQLite 连接、页面位图、缩略图位图 —— 长会话（开几十本书）句柄/内存持续增长 | 守卫改为 `Interlocked.Exchange` 幂等；OnClosed 顺序审查（先置空 Source 再释放，已符合）；双调用（OnClosed fire-and-forget + 测试显式调用）幂等安全 |
| P0-2 | 死代码/过时引用：`WebtoonVisibleRange` 访问器无引用；`ProgressValue` 只写不读；ReaderWindow:144 注释仍提"SyncJumpSlider"（已删除的功能） | 维护噪音、误导 | 删除/更新 |
| P0-3 | 编译警告：CS0108 `IsPointerOver`（2 处，`new` 关键字即可）；测试项目 xUnit1051 ~15 处（应传 `TestContext.Current.CancellationToken`） | 噪音，掩盖新警告 | 清零 |
| P0-4 | `async void` ×4（RunZoomDiagnostics/OnSettingsClick/RefreshThumbStrip/ShowJumpDialog）—— 事件处理器惯例 | 异常若逃逸会崩 UI 线程 | 审查结论：均有 try-catch 或轻量体，**保留**；RefreshThumbStrip 内 await 路径已含 catch（GetThumbnailAsync）—— 仅登记不处理 |

### P1 · 可维护性（为持续开发做准备）

| # | 问题 | 影响 | 方案 |
|---|---|---|---|
| P1-1 | **`ReaderWindow.axaml.cs` 1827 行**：画布/缩放/滚动/预览栏/键盘/跳页/设置入口全在一文件 | 定位/修改困难，易误伤 | **partial class 分文件**（纯搬移，行为零变化）：`ReaderWindow.Canvas.cs`（画布/缩放/Fit）、`ReaderWindow.Webtoon.cs`（滚动虚拟化/占位/惯性）、`ReaderWindow.Preview.cs`（预览栏/缩略图）、`ReaderWindow.Input.cs`（键盘/鼠标/快捷键分派）、`ReaderWindow.Dialog.cs`（跳页/设置/调试） |
| P1-2 | `ReaderViewModel.cs` 740 行 | 同上 | partial 分 3 文件：`ReaderViewModel.Decode.cs`（缓存/解码/引用计数）、`ReaderViewModel.Nav.cs`（翻页/书签/跳转）、`ReaderViewModel.State.cs`（保存/恢复/构造） |
| P1-3 | `ScrollModeFixesTests.cs` 856 行 | 测试难维护 | 按主题拆：`SettingsTests.cs`（设置存取/默认模式/续读）、`WebtoonNavigationTests.cs`（整页跳/方向组/组合键）、`ScrollInertiaTests.cs`（滚轮/惯性/复位）、`ReaderSettingsIntegrationTests.cs` |
| P1-4 | `vm.PropertyChanged +=`（ReaderWindow:141）从不退订 | 窗口与 VM 同生命周期成环，GC 可回收（非泄漏），但不够干净 | OnClosed 退订（或保留并注明：同生命周期安全）—— 建议退订，洁癖级 |
| P1-5 | CODE-MAP/文档未含新文件（SettingsStore/SettingsDialog/guide 机制） | 新会话定位慢 | 更新 CODE-MAP + 文档导航 |

### P2 · 性能（体验优先：阅读/画质/切换/滚动流畅）

| # | 问题 | 影响 | 方案 |
|---|---|---|---|
| P2-1 | **`LoadSizes` 构造时同步读全书页尺寸头**（大书几百页逐个开条目读头） | 打开大书有可感知延迟（数百次文件读） | 异步化/懒加载：构造只读必要页（前 2 页）→ 后台线程补全其余 → 双页/滚动用到时再取（缺省 0.7 比例）。需保证 Ratio 到位时序（双页拼接依赖真实比例） |
| P2-2 | **滚轮事件每事件 SLog**（WHEEL/SCROLL，弱 Delta 设备事件密集） | 高频日志写文件 + 事件环，滚动时拖累 | 加日志级别开关：`SettingsStore.DebugLog`（默认关）；仅开启时记 WHEEL/SCROLL 高频项，关键事件（模式/跳页/异常）始终记 |
| P2-3 | 无**压力验证基线** | 内存/流畅性回归无据 | 新增压力测试：长时连续滚动 + 快速切模式 + 缩放循环，断言位图引用计数归零、缓存不增长、无 ObjectDisposed（headless 可覆盖） |
| P2-4 | （可选）封面墙缩略图：已懒加载前 24，长列表滚动加载策略 | 大库滚动首屏外卡片 | 评估可见区 + 缓冲的懒加载（复用 Webtoon 可见区思路）；当前影响小，列为可选 |

### P3 · 工程实践 / 测试补强（轻量）

| # | 问题 | 影响 | 方案 |
|---|---|---|---|
| P3-1 | **无 .editorconfig**；未跑过 dotnet format | 风格不统一（缩进/换行已有约定但无强制） | 添加 .editorconfig（对齐现有风格：4 空格/CRLF/UTF-8）+ `dotnet format` 一次格式化（确认 diff 仅格式） |
| P3-2 | Nullable 已启用（三个项目均 enable）✓ | — | 无需改动；抽查 `ReaderViewModel`/`SettingsStore` 的 `!` 使用是否都有依据 |
| P3-3 | 测试覆盖缺口 | 主窗口（库/目录/搜索）已有 FolderBrowserTests ✓；设置对话框 UI 交互（headless 难）—— 底层 store/分派已测 ✓ | 补：压力测试（P2-3）+ 若 P1-3 拆文件顺带整理；不新增 UI 交互测试（headless 收益低） |

---

## 三、实施顺序建议

1. **P0**（1 轮）：修 DisposeAsync → 死代码/注释清理 → 警告清零 → 全量测试回归
2. **P1**（1-2 轮）：ReaderWindow/ReaderViewModel partial 分文件 → 测试拆文件 → 退订 + 文档更新 → 回归
3. **P2**（1 轮）：LoadSizes 异步化 → 日志级别 → 压力测试
4. **P3**（收尾）：.editorconfig + format → 最终回归 + 发布

> 每步：build + 全量测试（65）+ 提交；最后统一发布。

## 四、风险与注意事项
- P1 分文件为纯搬移，回归风险低；但需确认 partial 文件成员访问（private 字段跨文件 OK ✓）
- P0-1 修复后，测试中 `win.Close() + vm.DisposeAsync()` 双调用的幂等性需验证（现有测试大量走此路径）
- P2-1 改 LoadSizes 时序需保证双页拼接/Ratio 依赖不回归（有双页高度匹配测试）
- 全程不改功能行为，仅结构/性能/健壮性；功能验收基线 = 65/65 测试 + 发布冒烟
