# 2026-11-21 资料库"重新关联"（v4.20）过程记录

## 任务概述

用户连续两次因"库目录改名/移动 → 旧路径失效 → 只能清空库再重新添加"而丢失使用状态（库顺序、当前目录、封面是否可见）。
要求实现"失效库一键重新关联"，干净实现，并说明用法与验证方式。

## 过程简述

1. **暴露根因**：`LoadSavedLibrariesAsync` 对失效路径直接 `continue` 跳过 → 失效库**根本不在列表里**，无法操作；
   `libraries.txt` 采用"读取旧内容 + 追加"写入，改名/删除没有统一清理入口。
2. **改造**：
   - `LibraryItemViewModel` 改为 `ObservableObject`：`Path`/`Name`/`IsValid` 可观察，`DisplayName` 在失效时显示"（路径失效）"，新增 `UpdatePath()`；
   - `LoadSavedLibrariesAsync`：失效库**保留并标记**，只自动选中第一个有效库；
   - `OnSelectedLibraryChanged`/`ShowLibraryAsync`：失效库不扫描、给明确提示；
   - `RelinkLibraryAsync(lib, path)`：校验 → 更新条目 → 同步库根 → 整体重写 `libraries.txt` → 重建树并显示；
   - `[RelayCommand] RelinkLibrary()`：弹目录选择器（选中失效库后可用）；
   - `RewriteLibrariesFileAsync()`：库文件唯一写入口（添加/关联/清空都走它，去掉追加式写入）；
   - `MainWindow.axaml`：库下拉旁新增"关联"按钮，下拉项改绑 `DisplayName`。
3. **验证**：新增 `LibraryRelinkTests` 2 例（改名后关联→路径/文件/库根/封面全部恢复；非法目标拒绝），全量 **172/172 通过**。

## 遇到的问题（现象 → 原因 → 解决）

| 现象 | 原因 | 解决 |
|---|---|---|
| 失效库在 UI 里找不到，无法操作 | 加载时直接跳过失效路径 | 保留条目并标记 `IsValid=false` |
| 改名后"重新添加"即可，但顺序/选中状态丢失 | 没有"改路径"能力，只能清空重建 | 新增重新关联（原地替换路径） |
| `libraries.txt` 可能出现重复/残留行 | 追加式写入，且清空/改名没有统一入口 | 统一为"用库列表整体重写" |
| 关联后封面不显示 | 若只改条目不改内存库根，渲染仍按旧根解析 | 关联后立即同步 `LibraryRoots` + 清封面缓存 |

## 最耗时项

`OnSelectedLibraryChanged` / `ShowLibraryAsync` / `RelinkLibraryAsync` 三者的调用时序（选中触发建树、关联触发选中、避免重复扫描与循环），以及异步加载（构造时 fire-and-forget）对测试的同步要求。

## 最耗 token 项

`MainWindowViewModel` 中库加载/选中/添加三处流程的重排与统一；以及补充"失效库"相关的断言（测试需要等待异步加载完成）。

## 规避与完善方案

- 库文件只有一个写入口（`RewriteLibrariesFileAsync`），后续任何库增删改都复用它；
- 同类"路径漂移"问题（封面键、进度键）仍按既有规则处理：改名后封面需重设、进度第二步入库时可用同样的重新关联机制恢复（因为键相对库根）；
- 待办：若以后做"进库"（v4.20b `progress.json`），重新关联可直接复用本机制。
