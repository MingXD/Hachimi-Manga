# app — MangaAgent 主代码库（实施中）

> 技术栈（决策 D1）：**Avalonia 11 + .NET**（详见 docs/03-dev/conventions.md）

## 解决方案结构（规划）
```
MangaAgent.sln
src/
  MangaAgent.Core/      # 领域层：ComicSource 抽象、分页、元数据、存储（纯 .NET，不引用 UI）
  MangaAgent.App/       # Avalonia UI：库界面 / 阅读器 / 设置
  MangaAgent.Tests/     # xUnit 单元测试（PageLayouter/方向/跨页识别）
tests/fixtures/         # 测试用 CBZ 样本（见 reference/fixtures）
```

## 当前状态
- [ ] M0.5 技术 PoC（SharpCompress 基准 / Avalonia 虚拟化 / SQLite）
- [ ] M1 库扫描 + 封面墙
- [ ] M1 阅读器（RTL 单页/双页、缩放、翻页、主题）
- [ ] M1 续读/书签/进度
- [ ] M1 打包验收
