# 2026-10-24 分类文件夹被折叠成漫画（BUG-010 / v4.18）过程记录

## 任务概述

用户反馈：在 `F:\额外储存\bz\zhongzi\` 下新建的文件夹 `nemuke？ (似せ)`（内含 9 个 zip）在软件中**看不见**，且这些漫画**仍显示在 zhongzi 下**，对该书"移动到 nemuke？"反复失败。要求"查查为什么"，随后批准方案 A 实施。

## 过程简述

1. **取证（文件系统）**：确认 `F:\额外储存\bz\zhongzi\nemuke？ (似せ)` 存在（全角 `？` U+FF1F，合法），含 9 个 zip（含 `アナキメ1` 62MB），无子文件夹、zip 内无 ComicInfo → 书名=文件名。
2. **取证（日志）**：`%LOCALAPPDATA%\HachimiManga\logs\hachimi-2026-10-22/24.log` 里唯一相关失败是
   `MOVE FAIL ...zhongzi\nemuke？ (似せ)\[nemuke？ (似せ)] アナキメ1 ...zip: 源与目标目录相同`。
   关键点：日志中的**源路径已含 `nemuke？ (似せ)`** → 软件当时知道该文件夹，排除"没扫描到/无 FileSystemWatcher"作为主因。
3. **代码定位**：`MainWindowViewModel.ShowFolderContents` 的 BUG-D v1.9 折叠规则
   （`Children.Count == 0 && Books.Count > 0` → 显示 `Books[0]` 为漫画）把"装 zip 的分类文件夹"也折叠了。
   按 `ImageInfo.CompareNatural`，`[nemuke？ (似せ)] アナキメ1` 排在 `アナキメ 2.X` 前（数字块前前缀 `""` < `" "`），
   所以那张"文件夹卡"的标题恰好是 `アナキメ1` —— 与用户描述完全吻合。
4. **方案 A 实施**：新增 `FolderNode.IsSingleVolumeFolder`（`Books.All(Kind == Folder)`），
   显示/搜索/统计三处统一使用；含 zip/pdf 的文件夹（含图片+压缩包混合）一律保持文件夹身份。
5. **验证**：新增 `FolderBrowserTests.Zip_Folder_Shows_As_Folder_Not_Book`；目标测试 23/23、全量 **159/159** 通过。

## 遇到的问题（现象 → 原因 → 解决）

| 现象 | 原因 | 解决 |
|---|---|---|
| 软件网格里找不到新建文件夹 | 折叠规则把"只装 zip 的叶子文件夹"渲染成一本漫画 | 判定加类型约束（全部为图片型目录书才折叠） |
| 该漫画"还在 zhongzi 下" | 折叠卡的书名 = `Books[0]` = `アナキメ1`（自然排序结果），用户误以为是 zhongzi 下的散书 | 同上（文件夹恢复文件夹身份） |
| "移动到 nemuke？"反复失败 | 卡片的 `SourcePath` 是 `Books[0].Path`（已在 nemuke？ 内），目标又选同一文件夹 → `FileOps` 判"源与目标目录相同" | 文件夹身份恢复后语义正确；文案优化列入后续可选 |
| 控制台读日志乱码 | Windows 控制台按 GBK 显示 UTF-8 | 仅显示问题，不影响判断（按时间/结构解读） |

## 最耗时项

日志与文件系统的交叉取证（约一半时间）：需要证明"移动其实成功了"而非"移动失败导致书没搬走"——决定性证据是失败日志里的源路径已含目标文件夹名。

## 最耗 token 项

无法在无 GUI 的情况下直接观察界面行为，只能靠读源码复现排序/折叠/统计逻辑；为此通读了 `ImageInfo.CompareNatural`、`FolderEntryItemViewModel` 统计辅助、全库搜索命中逻辑（共 4 处同款判定）。

## 规避与完善方案

- 同类判定已收敛到 `FolderNode.IsSingleVolumeFolder`，后续只改一处。
- 可选后续：①`源与目标目录相同` 文案改"已在目标文件夹中，无需移动"；②对 Explorer 侧变更做更醒目的 Rescan 提示（或轻量 watcher）；③若日后需要"文件夹也能当一本书"（如装图片又装压缩包的混合目录），再引 `BookKind` 优先级展示，不恢复旧折叠。
