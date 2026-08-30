# AGENTS.md — Hachimi Manga（工程代号 MangaAgent）项目指令

> 本文件由 DSH（DeepSeek Harness）自动加载为项目级工作区指令。维护：修改本项目工作流/约定时同步更新；完整约定见 `docs/03-dev/`，过程记录见 `docs/03-dev/process-records/`。

## 项目速览
- Windows 优先的本地漫画阅读器（C#/.NET 9 + Avalonia 12 + CommunityToolkit.Mvvm + SQLite + xUnit），不联网
- 三层：`app/src/MangaAgent.Core`（领域，无 UI）→ `MangaAgent.App`（Avalonia UI）→ `MangaAgent.Tests`（headless xUnit）
- 对外名 HachimiManga；内部命名空间仍为 `MangaAgent.*`；数据目录 `%LOCALAPPDATA%\HachimiManga`
- 测试基线 **67/67 通过**；改阅读器/库/设置逻辑必须带回归测试
- 文档入口：`docs/00-overview.md`；代码地图：`docs/03-dev/CODE-MAP.md`；发布前必读 `docs/03-dev/release.md`

## 常用命令（本机 dotnet 不在 PATH，用完整路径）
```powershell
$dotnet = "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe"
# 构建
& $dotnet build app\src\MangaAgent.App -m:1 -p:UseSharedCompilation=false
# 测试（包已还原后可直接 --no-restore 省时间）
& $dotnet test app\src\MangaAgent.Tests -m:1 -p:UseSharedCompilation=false --no-restore
# 运行 dev 构建
powershell -ExecutionPolicy Bypass -File tools\run.ps1
# 发布绿色版 zip
powershell -NoProfile -ExecutionPolicy Bypass -File tools\pack-release.ps1
```

## 工作流规范
- **★ 变更流程（用户明确要求，2026-08-25）**：**先排查 → 给方案（根因/改动点/影响/测试）→ 用户决策后 → 才实施**。用户说"先给方案/不要先实施"时必须遵守，即使改动很小；方案放回复或 docs/03-dev/ 下，等用户确认再动代码
- Bug 处理：**先查原因 → 给方案 → 用户认可后修复 → 验证 → 归档**；新建 `docs/03-dev/bugs/BUG-xxx.md` 跟踪
- git 三连：`git add .` → `git commit -m "..."` → `git push`；提交风格 `类型(范围): 中文描述；测试数`
- 代码规范：`docs/03-dev/conventions.md`（dotnet format：4 空格/CRLF/UTF-8 去 BOM）
- 隐私红线：`test-file/`、`reference/screenshots/`、`dist/`、`publish/`、`bin/`、`obj/` 等已 gitignore，不得提交个人/版权数据

## ★ 长程任务过程记录（必做）
**所有长程任务（多轮/踩坑/用户要求记录）完成后，除结果外必须写过程记录**，包含：过程简述、遇到的问题（现象→原因→解决）、最耗时项、最耗 token 项、规避与完善方案。
- 保存到 `docs/03-dev/process-records/YYYY-MM-DD-<简述>.md`（模板见 `docs/03-dev/process-recording.md`，可加载 `process-record` 技能）
- 最终回复里附记录文件链接

## 本机环境坑位速记（2026-08-25 实测）
1. **沙箱/权限**：本 DSH 会话文件策略为 danger-full-access（2026-08-25 起），一般命令可直接跑；若策略回退，构建会因 Avalonia 遥测写 `%LOCALAPPDATA%\AvaloniaUI\BuildServices` 失败（工作区外）
2. **无外网**：nuget.org 不可达 → 还原只能用本地缓存；**不得引入浮动版本**（`*`），新包先确认 `~/.nuget/packages/<id>/<ver>` 存在
3. **验证命令输出用 `*> 文件` 重定向**再读文件；控制台/后台 job 回显可能为空（管道受限）
4. **还原失败会污染 `obj/project.assets.json`**（targets 空 = 坏壳），删除 obj 重来；`xunit.runner.visualstudio` 已钉 `[4.0.0]`
5. 行尾 CRLF：axaml/cs 为 CRLF，脚本批量替换用 `\r\n`；Avalonia 12 可视树遍历用 `GetVisualParent()` 扩展
6. **Avalonia 对话框坑（2026-08-25 新建/重命名"完全无效"根因）**：`Window.ShowDialog()` 是**异步**的（返回 Task，不阻塞调用线程）——同步写法 `ShowDialog(); return result;` 会**调用后立即返回 null**，对话框照常弹出但取值永远拿不到。需要返回值必须 `await ShowDialog` 或 tcs 模式（参考 ConfirmDialog/NewFolderDialog）；仅展示不需要返回值的窗口（如 PropertiesDialog）可 fire-and-forget
