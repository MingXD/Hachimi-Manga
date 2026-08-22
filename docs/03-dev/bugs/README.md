# Bug 跟踪

> 流程约定（用户要求）：**先查原因 → 给出方案 → 用户认可后修复 → 验证 → 记录归档**。
> 每个 bug 一个文件：`BUG-<编号>-<简述>.md`，记录现象/复现/原因分析/方案/修复/验证。

| 编号 | 状态 | 简述 |
|---|---|---|
| [BUG-001](BUG-001-reader-blank.md) | ✅ 已修复 | 阅读器画面全空（LRU 缓存+失败提示） |
| [BUG-002](BUG-002-cover-lazy-load.md) | ✅ 已修复 | 封面懒加载（滚动按需加载） |
| [BUG-003](BUG-003-zoom-focus.md) | ✅ 已修复 | 缩放焦点漂移（RenderTransformOrigin 默认 Center，v1.5） |
| [BUG-004](BUG-004-mode-switch-crash.md) | ✅ 已修复 | 双页→单页切换闪退（位图 Dispose 竞态 → UI 引用计数，v1.6） |
| [BUG-005](BUG-005-library-and-switch.md) | ✅ 已修复 | 资料库添加/切换问题（v1.8 扫描进度+容错） |
| [BUG-006](BUG-006-webtoon-rewrite.md) | ✅ 已修复 | Webtoon 虚拟化重构（并发竞态 2 处，v1.7） |
| [BUG-007](BUG-007-physical-navigation.md) | ✅ 已修复 | 单页跳页跳过物理页（物理页导航，v1.9） |

> v2.x 的修复（预览栏 CPU/内存拉满、白天按钮黑、双页高度不匹配等）记录在 [v2-changes-record.md](../v2-changes-record.md)。
