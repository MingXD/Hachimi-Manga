# BUG-005：资料库添加/扫描显示问题 + 配对/切模式无反应（2026-08-20）

- **状态**：已修复（v1.8）
- **优先级**：高

## 子问题 A：添加资料库"没反应" / 扫描出书不显示 / 重启左侧空白
### 根因（3 个独立问题）
1. 扫描全量递归且无进度反馈（每个 CBZ 开压缩包读元数据）→ 深层目录数十秒 → 像卡死
   （LibraryScanner.ScanDirectoryAsync + MakeZipBookAsync）
2. RescanAsync 只更新 Libraries 不重建 FolderTree → 扫描后左侧树旧/空
3. LoadSavedLibrariesAsync（启动）不建树 → 重启空白
### 修复
- ScanAsync 加 progress 回调；Add/Rescan 用 Task.Run 离线扫描 + Dispatcher 更新状态
- Rescan/LoadSaved 补 FolderTreeScanner 重建 + ShowFolderContents

## 子问题 B：双页点击无法切回单页 / 配对点击无反应
### 根因
1. 模式切换是循环（单页→双页→滚动→单页），按钮显示当前模式名 → 双页点按钮实际切滚动
2. **真 bug**：TogglePairOffset → RebuildLayout 重建 _logical（配对起点变 → 同 CurrentIndex 的
   FirstIndex 变）→ LoadCurrentAsync 竞态防护对比 FirstIndex 误判为竞态 → 丢弃结果 → 画面不更新
### 修复
- 模式切换改下拉菜单三选一（SetViewMode 直接设置）
- LoadCurrentAsync 引入 _layoutVersion（RebuildLayout/ToggleMode 自增），防护只对比版本号

## 验证
- PairOffsetSwitchTests（配对切换后画面更新）、RescanBuildsTreeTests（扫描后树非空）
- 31/31 通过；真实书压力 4/4 零崩溃
