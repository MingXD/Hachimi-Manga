# 路线图（v1 — 结合调研与技术选型）

> 前置决策点见 requirements.md §7；技术路径见 tech-stack-eval.md。

## M0 调研与决策（当前阶段）
- [x] 竞品调研（开源 / 商业/UX / 技术选型）→ docs/01-research/
- [x] 需求与设计文档 v1 → docs/02-design/
- [x] **用户评审与决策**（D1–D6 全部确认：Avalonia 11、文件夹+CBZ、纯本地元数据、干跑+可撤销、进阶后置 M3、范围确认）
- 产出：本仓库全套文档；决策记录见 decision-log.md；下一步初始化 git 与项目脚手架

## M0.5 技术 PoC（✅ 已完成，详见 docs/03-dev/poc-report.md）
- [x] SharpCompress/ZipArchive：300 页 CBZ 索引 29ms、随机翻页 0.1ms（内置 ZipArchive 胜出）
- [x] Avalonia 12.1.1：封面墙虚拟化（1 万项）+ 大图渲染 Headless 冒烟通过
- [x] SQLite：1 万条书目插入 72ms、查询 3.8ms
- [ ] MSIX 打包 + 自动更新链路（随 M1 打包一并验证）
## M1 Windows MVP（可读一本日漫）—— 预计 4–6 周（单人）
目标验收：打开 5000 页 CBZ，RTL 双页正确、黑夜模式可用、续读生效
- 项目脚手架：Avalonia 11 + .NET 核心类库 + 测试框架
- ComicSource：FolderSource + ZipSource（CBZ）；SharpCompress 接入
- 库扫描 v1：全量扫描 + 封面墙（虚拟化）
- 阅读器 v1：Pager + DoublePage（PageLayouter 核心规则）+ RTL/LTR + 缩放 + 翻页（点击/键盘/滚轮）+ 动画（无/淡入/滑动）
- 主题：白天/黑夜/跟随系统；阅读区亮度滤镜
- 进度：续读 + 书签 + 完成标记（reading.db）
- 设置：全局 + 单本两层 + 预设管理器骨架
- 打包：MSIX 安装包

## M2 库与文件管理 —— 预计 3–4 周
验收：扫描 1 万本书不卡 UI；整理操作可预览可撤销
- Komga 式四层扫描（增量 + 分析 + 元数据刷新）
- ComicInfo.xml 读写；系列聚合；文件名推断
- 搜索 / 排序 / 筛选；收藏视图
- 文件管理：拖放导入、批量重命名、自动整理（干跑+撤销）、回收站删除
- 库/进度备份导出 JSON

## M3 打磨与进阶 —— 持续迭代
- Webtoon 连续滚动 + 长条带间隔；点击区预设；E-ink/低刷屏适配
- 放大镜；跨页图拆分/旋转；offset；手动分组
- 设置搜索、每项"生效层"显示、主题编辑器
- 性能：预解码、三档缓存、全库缩略图后台、崩溃恢复
- 格式扩展：CBR/RAR、7z、PDF（按用户决策）
- 去重（文件/页 hash）；格式转换（CBR→CBZ、WebP 重压缩）【待定】

## M4 移动端探索（未来，决策窗口 12–24 个月）
- 路径 A：领域层纯 .NET 类库 → MAUI 复用（UI 重写、逻辑保留）
- 路径 B：LocalServerService（HTTP + OPDS）→ 手机端薄客户端（Komga/Kavita 模式）
- 路径 C（若移动端提前至 12 个月内）：切换 Flutter 主方案重写 UI
- 推荐先做 B 的服务端化改造（低成本），视用户反馈再决定 A/C

## 里程碑验收清单（M1）
- [ ] 5000 页 CBZ 索引 <1s、随机翻页 <100ms
- [ ] RTL 双页：封面单页、横向页单页、首尾页单页正确
- [ ] 黑夜模式 + 阅读区滤镜可用
- [ ] 中断后重开恢复续读
- [ ] 全局/单本设置覆盖生效
