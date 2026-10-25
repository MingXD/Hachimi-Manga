# BUG-011：分类目录里的散图被当成"一本漫画"（幽灵书 + 家目录自指）

- 发现：2026-10-25，用户实测
- 状态：已修复（v4.19）
- 影响面：目录浏览器（网格/详情）、库扫描计数、搜索命中
- 严重度：中（数据无损；但用户会看到"明明不存在的一本漫画"，且误操作"移动到该目录"必然失败）

## 现象

用户报告：`F:\额外储存\bz\zhongzi` 下多出一本叫 **`zhongzi`** 的漫画，点开资源管理器指向的是 zhongzi 目录本身；
该目录里其实有大量图片、文件夹、压缩包。另一症状是"移动走一个文件之后，软件里就只剩下一本漫画"。

## 排查证据

- 目录实际内容：7 个子文件夹 + **49 张散图**（`001.jpg`…`049.jpg`，含 `005_.jpg`，约 400MB）+ 2 个 zip + mp4/torrent。
- 用探针跑软件自身的扫描/渲染逻辑（临时测试，验证后删除）：

```
NODE zhongzi children=7 books=3
  BOOK kind=Zip    [FANBOX] Honashi... 724 页
  BOOK kind=Folder title=zhongzi pages=49 path=F:\额外储存\bz\zhongzi   ← 幽灵
  BOOK kind=Zip    [とまほーく...] 238 页
--- 界面实际渲染 10 项（含一张标题为 zhongzi、49 页的"漫画"卡）---
```

- 代码：`FolderTreeScanner.FillAsync` 对所有"直接含图片"的目录都追加一条 `BookKind.Folder` 的 `BookInfo`
  （路径=目录自身），本意是支持"不压缩、直接放散图的单行本文件夹"。分类目录也被这样处理 → 目录把自己当一本书列在自己里面。

## 根因

"目录本身算一本书"的判定缺少类型约束：只要目录里**直接有图片**就成立，没有排除
"该目录同时还有子文件夹或压缩包"（即分类/容器目录）的情况。
分类目录里的散图（多半是下载/解压时漏在根上的残留）因此被渲染成一本以目录名命名的漫画；
并且这张卡指向目录自身，用户对它"移动到该目录"会得到"源与目标目录相同"。

## 修复（v4.19）

- `FolderNode.IsSingleVolumeFolder` 判定收紧为：`Children.Count == 0 && Books.Count > 0 && Books.All(b => b.Kind == BookKind.Folder)`；
- 扫描端（`FolderTreeScanner.FillBooksShallow`、`LibraryScanner.ScanDirectoryAsync`）只在
  **无子文件夹 + 无 CBZ/ZIP/PDF + 直接有图片**时才生成"目录本身这本书"；
- 因此：单行本文件夹（散图）行为不变（仍折叠成一本漫画、可正常阅读）；
  分类目录（有子文件夹或压缩包）一律保持文件夹身份，散图不再自成一本。
- 回归测试：`AutoRefreshTests.Ghost_Book_Only_For_SingleVolume_Folder`、
  更新 `FolderBrowserTests.Folder_With_Subfolder_And_Images_Is_Folder_With_Stats`。

## 用户侧后续（可选，软件不动文件）

`F:\额外储存\bz\zhongzi` 根上的 49 张 `001.jpg…049.jpg` 是真实文件，v4.19 后不再在软件里显示为"漫画"。
若确定是残留，可自行移进子文件夹或删除；若想保留阅读，建一个子文件夹放进去即可（单行本文件夹规则会把它当一本书）。

## 关于"移动走一个文件后只剩一本漫画"

该现象未能复现：v4.18/v4.19 对 zhongzi 的实际渲染均为 10 项（见上面探针）；当日日志中真实库只有一条
`NEWFOLDER ...\zhongzi\NTR`，无任何 MOVE/COPY 失败记录。推测是刷新前看到的旧视图，或当时位于某个子目录。
v4.19 的自动刷新（见 `docs/02-design/auto-refresh.md`）已让"移动后视图不更新"不再可能。
