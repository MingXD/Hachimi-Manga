# 2026-10-25 自动更新机制（P1/P2/P3）+ 幽灵书修复（v4.19）过程记录

## 任务概述

用户提出两件事：
1. 软件没有好的更新机制——把文件夹移动到 A 目录、新下载漫画、新建文件夹后，界面里"什么都看不到"，希望"够用就行、不占性能"的改进；
2. `F:\额外储存\bz\zhongzi` 下莫名多出一本叫 `zhongzi` 的漫画（指向目录自身），且"移动走一个文件后只剩一本漫画"。
决策：按方案 A（幽灵书）实施，新机制 P1/P2/P3 全部干净实现。

## 过程简述

1. **幽灵书取证**：列目录发现 zhongzi 根上真有 49 张散图（`001.jpg…049.jpg`，含 `005_.jpg`，约 400MB）；
   写临时探针测试直接跑软件自身的扫描/渲染逻辑，输出确认 zhongzi 节点 `books=3`，其中 `kind=Folder title=zhongzi pages=49 path=…\zhongzi`
   ——即"目录本身被当成一本书列在自己里面"。用户可见的 10 项里就有这张卡。
2. **刷新机制取证**：读代码确认无 FileSystemWatcher；`RefreshCurrentViewAsync` 每次重建**整棵库树**且
   `FolderTreeScanner`/`LibraryScanner` 都会**逐个打开压缩包**读页数。实测用户库 2610 目录 / 67286 zip-cbz
   （纯枚举 5 s，开包则分钟级）→ 这就是"刷新只能省着用"的根因。
3. **实施**（全部经用户批准）：
   - 幽灵书：`FolderNode.IsSingleVolumeFolder` 收紧 + 扫描端只在"无子文件夹、无压缩包、有散图"时生成目录书本；
   - P1：`FolderTreeScanner` 改为**浅结构扫描**（不打开压缩包）+ 新增 `RefreshShallowAsync` 原地浅刷新；
     `LibraryScanner` 增加 `readPageCounts=false` 轻量模式并合并每层重复枚举；页数改由界面后台懒补（`EnrichPageCountsAsync`）；
   - P2：进入目录/返回上级/面包屑/前进后退浅刷新；窗口 `Activated` → `RefreshIfStaleAsync`（比较目录 `LastWriteTime`）；
     `FolderNode.Children` 改 `ObservableCollection` 使侧边栏树原地更新且保留展开状态；
   - P3：新增 `FolderWatcher`（只监听当前目录、切目录重挂、去抖 800ms、溢出兜底、关窗释放、丢弃过期目录事件）。
4. **验证**：新增 `AutoRefreshTests`（6 例）+ 更新受影响用例；全量 **165/165 通过**；
   真实库实测：建树 **0.9~1.2 s**（原分钟级）、库扫描 **533 ms**（原 2314 ms）、zhongzi 幽灵书消失。

## 遇到的问题（现象 → 原因 → 解决）

| 现象 | 原因 | 解决 |
|---|---|---|
| 修改后多个既有用例随机失败（条目数不对/找不到新条目） | `MainWindowViewModel` 构造时异步加载真实 `libraries.txt`；v4.19 起加载变快，会在测试中途完成并覆盖测试自己的树 | 测试进程启动时用 `ModuleInitializer` 把 `MainWindowViewModel.AppData` 切到独立临时目录（测试永不触碰用户库） |
| 粘贴/移动后的刷新偶尔"没生效" | 旧去重逻辑"正在刷新就 return"把请求静默丢弃 | 改为 `_refreshRequest` 排队：本轮回合结束立刻补跑一次 |
| watcher 切目录后仍被旧目录事件触发 | 事件在 UI 线程异步执行，执行时已换目标 | 投递时记录当时的监听目标，执行时不匹配即丢弃 |
| 删除目录偶发 `IOException`（测试） | headless 测试没有窗口却挂了 watcher，多次刷新拉长缩略图解码窗口 | watcher 只在 `GetOwnerWindow != null` 时启用；watcher 本身另行单独测试 |
| 首屏出现"… 页" | 浅扫描不读页数（有意） | `ShowFolderContents` 渲染后后台懒补并回填（可观察属性，`Subtitle` 自动刷新） |

## 最耗时项

改造前先做**性能取证**（枚举 67286 个 zip 与目录的耗时、深扫描代价）与**测试污染的定位**——后者花了整轮时间：
失败信息都指向"条目数量不对"，真正原因是异步加载真实库覆盖了测试状态。

## 最耗 token 项

集中阅读与改动 `MainWindowViewModel`（915 行，导航/刷新/搜索/文件操作都在里面），以及把四处重复的"单行本/纯图片"判定收敛到 `FolderNode.IsSingleVolumeFolder`。

## 规避与完善方案

- 与文件系统相关的判定统一收敛到 Core（`IsSingleVolumeFolder` / `RefreshShallowAsync`），APP 只做触发与渲染；
- 设计文档 `docs/02-design/auto-refresh.md` 记录触发矩阵与实测数据，后续调整触发器只需改表；
- 仍未做（有意）：库根级全量 watcher、深层递归自动刷新、ComicInfo 标题的浅读取——都等有真实需求再加。
