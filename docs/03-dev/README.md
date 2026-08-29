# 开发与维护文档

> 维护约定：变更先记入对应版本的 changes-record，Bug 记入 bugs/ 目录，验证后归档。

## 文档索引
| 文档 | 内容 |
|---|---|
| [CODE-MAP.md](CODE-MAP.md) | 代码地图（App/Core/Tests 模块与关键文件定位） |
| [conventions.md](conventions.md) | 代码规范与约定 |
| [setup.md](setup.md) | 环境搭建（.NET、构建、测试） |
| [m1-acceptance.md](m1-acceptance.md) | M1 里程碑验收 |
| [poc-report.md](poc-report.md) | PoC 报告（画布模型验证等） |
| [experience-impl-plan.md](experience-impl-plan.md) | 阅读器体验实施计划 |
| [v1.8-plan.md](v1.8-plan.md) / [v1.8-final-plan.md](v1.8-final-plan.md) | v1.8 计划与定稿 |
| [v1.9-plan.md](v1.9-plan.md) / [v1.9-changes-record.md](v1.9-changes-record.md) | v1.9 计划 / 修复全记录 |
| [v2-changes-record.md](v2-changes-record.md) | **v2.0→v5.2 全记录（预览栏/滚动/设置/命名发布，最新）** |
| [release.md](release.md) | **发布流程（Release 打包/原理/环境坑位，发布前必读）** |
| [preview-strip-design.md](preview-strip-design.md) / [preview-strip-final.md](preview-strip-final.md) | 预览栏方案设计 / 最终方案（含实施后现状） |
| [code-audit-optimization-plan.md](code-audit-optimization-plan.md) | 代码审计与优化计划（P0–P3） |
| [process-recording.md](process-recording.md) | **长程任务过程记录约定（完成长程任务必写：问题/原因/解决/耗时/耗token/规避）** |

## 过程记录（长程任务）
→ [process-records/](process-records/)（按 `YYYY-MM-DD-<简述>.md` 命名；首个记录：2026-08-25-env-build-setup.md）

## Bug 跟踪
→ [bugs/README.md](bugs/README.md)（BUG-001~007，均 ✅ 已修复）

## 构建与测试
```powershell
# 构建（在 app/ 目录）
& "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe" build src\MangaAgent.App -m:1 -p:UseSharedCompilation=false
# 测试
& "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe" test src\MangaAgent.Tests -m:1 -p:UseSharedCompilation=false
# 发布（绿色版 zip 一键打包，输出到 app\dist）
& "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe" publish src\MangaAgent.App -c Release -r win-x64 --self-contained -p:PublishSingleFile=false -m:1 -p:UseSharedCompilation=false
# 或直接跑打包脚本（publish + robocopy + zip 一步完成）：
powershell -NoProfile -ExecutionPolicy Bypass -File D:\MangaAgent\tools\pack-release.ps1
```

## 测试状态
- 当前基线：**67/67 通过**（v5.2 + code-audit 后；每次发布在 v2-changes-record.md 更新）
- 测试目录：app/src/MangaAgent.Tests/PoC/（ReaderInput / ZoomCentering / ZoomWheel / ReaderE2e / ModeSwitchCrash / WebtoonVirtualization / QuickJump / FolderBrowser / PairOffsetSwitch / RescanBuildsTree / ResetAndDirection / ZoomButtonDiag / PreviewStripOverlay / ScrollModeFit / SettingsSystem / StressRegression 等）
