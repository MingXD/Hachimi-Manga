# BUG-010：装 zip 的分类文件夹被折叠成一本漫画（文件夹"消失" + 反复移动失败）

- 发现：2026-10-24，用户实测
- 状态：已修复（v4.18）
- 影响面：目录浏览器（网格/详情）、全库搜索、文件夹右键菜单语义
- 严重度：中（数据无损，但文件夹不可见、移动操作反复报错）

## 现象

1. 用户在 `F:\额外储存\bz\zhongzi\` 下新建（资源管理器）文件夹 `nemuke？ (似せ)`，把 9 个 zip 移进去。
2. 软件里**找不到这个文件夹**，且这些漫画**仍显示在 `zhongzi` 下**。
3. 对那本漫画右键"移动到…"，在系统选择器里选中 `nemuke？ (似せ)`，**反复失败**：

```
10-22 04:18  MOVE FAIL ...\zhongzi\nemuke？ (似せ)\[nemuke？ (似せ)] アナキメ1 [中国翻訳] [5DK个人汉化].zip: 源与目标目录相同
10-24 22:01  MOVE FAIL （同上）
```

## 排查证据

- 文件系统：`nemuke？ (似せ)` 存在，含 9 个 zip（无子文件夹、zip 内无 ComicInfo → 书名=文件名）。移动其实早已成功。
- 日志：唯一失败原因是"源与目标目录相同"，且**源路径已包含 `nemuke？ (似せ)`** → 软件知道该文件夹，不是"没扫描到"。
- 代码：`MainWindowViewModel.ShowFolderContents` 的 BUG-D v1.9 折叠规则
  `if (child.Children.Count == 0 && child.Books.Count > 0) → 显示 child.Books[0]（漫画条目）`，
  本意是"直接装图片的单行本文件夹 = 一本漫画"，但对**装 zip/pdf 的分类文件夹同样生效**。
- 书名取 `Books[0]`：`ImageInfo.CompareNatural` 下 `[nemuke？ (似せ)] アナキメ1` 排在 `アナキメ 2.X` 前
  （数字块前的前缀 `""` < `" "`），因此那张"文件夹卡"标题恰是 `アナキメ1` —— 与用户描述的"这本漫画还在 zhongzi 下"完全吻合。

## 根因

折叠规则的判定条件过宽：只检查"无子文件夹 + 有书"，未区分书的类型。
分类文件夹（装压缩包/PDF）被降级成一本漫画 → ①文件夹本身在网格里不可见（侧边栏树里其实有节点）；
②该卡片 `SourcePath` = `Books[0].Path`，右键"移动到"只作用于第一本书；③目标选成该文件夹自身时
`FileOps` 判定源目录=目标目录 → 拒绝（防御正确），用户侧表现为"反复移动失败"。

## 修复（v4.18）

- 新增 `FolderNode.IsSingleVolumeFolder`：`Children.Count == 0 && Books.Count > 0 && Books.All(b => b.Kind == BookKind.Folder)`
  —— 只有"直接装图片"的单行本文件夹才折叠成漫画；含任何 zip/pdf 的文件夹（含图片+压缩包混合）一律保持文件夹身份。
- `ShowFolderContents`、`CollectSearchHits`（两处）、`FolderEntryItemViewModel.IsPureImageFolder` 统一使用该属性，消除四处重复判定。
- 回归测试：`FolderBrowserTests.Zip_Folder_Shows_As_Folder_Not_Book`（装 zip 的文件夹 + 图片/zip 混合文件夹都必须显示为文件夹，进入后才看到 zip 漫画）。
- 测试基线 158 → 159，全绿。

## 后续可选（未做）

- `源与目标目录相同` 文案改为"已在目标文件夹中，无需移动"。
- 无 FileSystemWatcher：资源管理器侧的新建/移动需 Rescan/重启才反映（本次非主因，但仍是体验缺口）。
