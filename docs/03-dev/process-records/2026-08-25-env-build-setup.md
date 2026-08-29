# 2026-08-25 环境构建搭建（DSH 沙箱 + 离线 NuGet 适配）

## 任务概述
- 目标：按 README 文档把项目构建起来（Debug 构建 + 全量测试 67/67），为后续"主开发 v2"的修 Bug / 加功能做准备
- 结果：✅ Debug 构建 0 错误；✅ 测试 **67/67 通过**（45s）；✅ 唯一仓库改动 `MangaAgent.Tests.csproj` 钉死 `xunit.runner.visualstudio [4.0.0]`

## 过程简述
1. 检查环境（git 干净、dotnet SDK 9.0.317 可用）→ 按 README 执行 Debug 构建
2. 构建失败：`AvaloniaStatsTask`（Avalonia 遥测）写 `%LOCALAPPDATA%\AvaloniaUI\BuildServices\buildtasks.log` 被拒（UnauthorizedAccessException）
3. 排查：确认该目录在工作区之外 → 探测 TEMP 可写、AppData 不可写 → 判定为 DSH 文件沙箱（当时 workspace-write 模式）拦截 → 尝试 CI 环境变量无效 → 构建命令升级 danger-full-access 成功
4. 跑测试：还原失败（NU1301：`xunit.runner.visualstudio` 浮动版本 `3.*` 需联网解析，环境无外网）
5. 修复：csproj 把 `3.*` 改为 `[4.0.0]`（本地缓存中实际可用版本）、删除被污染的 Tests `obj/`、离线 restore 成功
6. 验证期间发现 `dotnet test` 控制台输出被管道吞掉（TRX 也不生成）→ 用 `*> 文件` 重定向 + 读文件尾部确认结果
7. 最终 67/67 通过，清理临时文件，记录本文档

## 遇到的问题

### 问题 1：AvaloniaStatsTask 写 AppData 日志被拒绝导致构建失败
- **现象**：`dotnet build` 报 `MSB4018: AvaloniaStatsTask 任务意外失败 … Access to the path 'C:\Users\YMK\AppData\Local\AvaloniaUI\BuildServices\buildtasks.log' is denied`；Core 项目已编译成功，App 项目挂在这里
- **原因**：该任务（Avalonia.BuildServices 11.3.2 的匿名遥测）无条件写 `%LOCALAPPDATA%\AvaloniaUI\BuildServices\buildtasks.log`；当时文件沙箱为 workspace-write 模式，只允许写工作区 D:\MangaAgent 和平台临时区，AppData 在工作区外 → 被拦。探测证实：TEMP 可写、该 AppData 目录不可写。CI=true 环境变量无效（任务仍执行到写日志才崩）
- **解决**：构建命令一次性升级 `sandbox_permissions: danger-full-access`（用户批准）；之后会话策略整体切到 danger-full-access，此问题消失
- **教训**：环境性失败先确认**沙箱边界**（探测可写性）再深挖，不要先去做 DLL 反编译/查源码找遥测开关（走了弯路，见"最耗 token 项"）

### 问题 2：Tests 项目还原失败（离线 + 浮动版本 + obj 污染）
- **现象**：`dotnet test` 报 `NU1301: 未能从远程源检索 xunit.runner.visualstudio 的信息 … SSL/凭证错误`；且之后 `--no-restore` 静默通过（exit 0）但不跑测试
- **原因**：① 环境**无外网**（nuget.org SSL 握手失败）；② csproj 里 `xunit.runner.visualstudio` 是**浮动版本 `3.*`**，浮动版本必须查源解析，即使包已在本地缓存；③ 失败的还原把 `obj/project.assets.json` 覆盖成**空壳**（targets/libraries 为空 + NU1301 日志），后续 `--no-restore` 用空 assets 编译 → 行为诡异
- **解决**：csproj 钉死 `Version="[4.0.0]"`（本地缓存中与 xunit.v3 3.2.2 验证兼容的版本，`[...]` 表示精确版本）；删除 Tests `obj/`；离线 restore 成功（329ms）；67/67 通过
- **教训**：离线环境禁用浮动版本；还原失败后检查 `obj/project.assets.json` 是否被污染（`targets` 是否为空）

### 问题 3：`dotnet test` 控制台输出与 TRX 文件"消失"
- **现象**：后台任务显示 exit 0 但无任何输出；TRX logger 也没生成文件；`*>` 重定向文件为空
- **原因**：沙箱下子进程 stdout 经管道捕获受限（文档所述 EPERM 边界的表现之一）；TRX 不生成可能与同样的 I/O 限制或静默路径有关
- **解决**：用 `& dotnet test … *> test-output.txt`（PowerShell 自家管道不受限）后读文件尾部拿到"已通过 67"；最终以文件内容为准
- **教训**：此环境下**验证命令输出一律 `*> 文件` 再读文件**，不要依赖控制台回显或后台 job 输出

## 最耗时项
- **测试验证循环（约 5~6 分钟）**：还原失败 2 轮（每次 ~10s 失败 + 排查）、输出被吞导致 3~4 次重复跑测试（每次 45s+）。教训：先确认输出通道可用、先修好还原，再开始跑测试
- 首次构建失败到提权成功：约 3 分钟（含探测与一次提权审批）

## 最耗 token 项
- **Avalonia.BuildServices.dll 字符串提取 + 两次 web_search 溯源遥测开关（约 2~3k token，且结果无效）**：方向错误——这是纯环境沙箱问题，不是 Avalonia 配置问题。教训：环境性失败先探测沙箱边界，再决定是否深挖源码
- **大目录列表输出**（node_modules 全量 200+ 行、.dsh 递归列表）：一次输出即数百行。教训：列表类输出先 `-First N` 限制
- 反复读取 MSB4018/NU1301 的堆栈与还原日志：各 ~1k token

## 规避与完善方案（给后续会话）
- [ ] **沙箱边界第一**：环境性失败（路径拒绝/网络失败）先探测（写 TEMP vs 目标路径、试连网络），确认后再深挖；本会话后策略已是 danger-full-access，若回退需注意
- [ ] **命令输出用 `*> 文件` 重定向**再读文件，不依赖控制台回显
- [ ] **离线构建纪律**：改 csproj 版本时避免浮动版本（`*`）；新包依赖需先确认本地缓存有（`~/.nuget/packages/<id>/<ver>`），否则无法还原
- [ ] 还原/构建失败后检查 `obj/project.assets.json` 是否被污染（targets 为空 = 坏），必要时删除 obj 重来
- [ ] `xunit.runner.visualstudio` 已钉 `[4.0.0]`；若未来联网升级包，需重新验证与 xunit.v3 3.2.2 的兼容性
- [ ] 本次会话已把上述要点写入根目录 `AGENTS.md`（DSH 自动加载），新会话无需重读本文即可知晓

## 相关文件 / 提交
- 提交：见 `git log`（本任务与"过程记录约定"同批提交）
- 涉及文件：
  - `app/src/MangaAgent.Tests/MangaAgent.Tests.csproj`（`3.*` → `[4.0.0]`，去 BOM）
  - `docs/03-dev/process-recording.md`、`docs/03-dev/process-records/`（本文及后续记录）
  - `AGENTS.md`、`.dsh/skills/process-record/SKILL.md`（DSH 记忆与技能）
