# 开发约定（M1 草稿 — 待技术栈决策后定稿）

> 状态：定稿（2026-02，决策记录 D1：Avalonia 11 + .NET）。

## 1. 仓库结构
```
D:\MangaAgent\
├── app\                    # 主代码库
│   ├── MangaAgent.sln
│   ├── src\
│   │   ├── MangaAgent.Core\      # 领域层：归档/分页/元数据/存储（纯 .NET，不引用 UI）
│   │   ├── MangaAgent.App\       # Avalonia UI（库/阅读器/设置）
│   │   └── MangaAgent.Tests\     # 单元测试（核心逻辑）
│   ├── assets\             # 图标、设计资源
│   └── README.md
├── docs\                   # 设计与维护文档（本仓库）
├── reference\              # 参考资料
└── tools\                  # 脚本（构建、文档生成）
```

## 2. 命名与代码风格
- C#：.editorconfig + dotnet format；类 PascalCase、局部 snake_case 不适用（C# 用 camelCase）
- 核心库禁止引用 Avalonia/WPF 类型；UI 层禁止业务逻辑
- 注释用中文或英文皆可，但**公共 API 必须有文档注释**

## 3. 测试策略
- Core 层：xUnit；PageLayouter/方向/跨页识别为纯函数重点覆盖边界
- 归档：用假 ComicSource + 少量真实样本（reference/ 放 2-3 个测试用 CBZ）
- UI 层：手动 + 截图回归（Avalonia.Headless 可选）

## 4. Git 约定
- 尚未初始化 git；M1 开工时 `git init`，主分支 main
- 提交信息：`类型(范围): 简述`（feat/fix/docs/refactor/test/chore）
- docs/ 与 app/ 同仓库提交（文档随代码演进）

## 5. 文档约定
- 设计变更先改 docs/02-design/ 再改代码；决策记录见 decision-log.md
- CHANGELOG.md 记录里程碑（roadmap.md 对应）

## 6. 依赖纪律（许可证）
- 只引入 MIT/Apache-2.0/BSD 依赖；RAR 相关注意 RARLAB/unRAR 许可；记录于 docs/03-dev/dependencies.md
