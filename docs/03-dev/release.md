# 发布流程（Release 打包）

> 目标：把当前代码打成**可分发、双击即用**的 Windows 版（self-contained，目标机无需安装 .NET）。
> 阅读对象：新会话/新开发者。改完代码并验证后，按本流程发布。

---

## 一、发布产物与流程总览

```
源码 → build(Debug 验证) → test(全量) → publish(Release+自包含) → robocopy 到 app\publish → 冒烟验证
```

最终产物：
- **app\publish\HachimiManga.exe**（self-contained，~208MB / ~230 文件，双击运行）
- **app\dist\HachimiManga-<ver>-win-x64.zip**（绿色版 zip，含 guide\，可直接分发）
- 两者均为**可再生成产物，不入库**（`.gitignore` 排除 `publish/`、`dist/`、`bin/`、`obj/`）

## 二、完整流程（按顺序执行）

### 0. 前置：清理残留进程（防文件锁导致 publish 失败）
```powershell
taskkill /F /IM HachimiManga.exe /T 2>$null
taskkill /F /IM testhost.exe /T 2>$null
taskkill /F /IM dotnet.exe /T 2>$null
```
> 原理：Windows 下被占用的文件无法覆盖。上次构建/测试/运行的进程不退出，publish 会报"文件正由另一进程使用"。

### 1. 构建验证（Debug）
```powershell
cd D:\MangaAgent\app
$env:DOTNET_ROOT = "$env:LOCALAPPDATA\Microsoft\dotnet"
$env:DOTNET_CLI_USE_MSBUILD_SERVER = "0"
& "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe" build src\MangaAgent.App -m:1 -p:UseSharedCompilation=false -v:q
```
期望：0 错误（允许 1 个既有警告 CS0108 ReaderWindow.IsPointerOver）。

### 2. 全量测试
```powershell
& "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe" test src\MangaAgent.Tests -m:1 -p:UseSharedCompilation=false
```
期望：**67/67 通过**（基线随版本变化；改完代码后新增测试会 +N，见 v2-changes-record.md 验证状态）。

### 3. Release 发布
```powershell
& "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe" publish src\MangaAgent.App -c Release -r win-x64 --self-contained -p:PublishSingleFile=false -m:1 -p:UseSharedCompilation=false
```
输出目录：`app\src\MangaAgent.App\bin\Release\net9.0\win-x64\publish`

### 4. 镜像同步到 app\publish（分发目录）
```powershell
robocopy D:\MangaAgent\app\src\MangaAgent.App\bin\Release\net9.0\win-x64\publish D:\MangaAgent\app\publish /MIR /NFL /NDL /NJH /NP
```
- `/MIR`：镜像模式——目标与源完全一致，**删除目标多余文件**（防止旧文件残留）
- 退出码：0=无变化，1=成功复制（正常），≥8=失败

### 5. 冒烟验证（发布版能启动）
```powershell
$p = Start-Process 'D:\MangaAgent\app\publish\HachimiManga.exe' -PassThru
Start-Sleep -Seconds 5
Get-Process -Id $p.Id   # 存活 = OK；退出 = 崩溃
Stop-Process -Id $p.Id -Force
```

### 5.5 一键打包绿色版 zip（可选，替代手工 3+4+5）
```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File D:\MangaAgent\tools\pack-release.ps1
```
脚本 = publish → robocopy 到 app\publish → 组装 app\dist\HachimiManga\<ver>\（含 guide\）→ 打 zip 到 app\dist\。
发版前在脚本头部 `$ver` 递增版本号。输出 zip 可上传 GitHub Releases 分发。

### 6. 记录
- commit 提交信息注明功能 + 测试数（如 `67/67通过`）
- 更新 docs/03-dev/v2-changes-record.md 验证状态 + docs/03-dev/README.md 测试基线 + README 版本历史

---

## 三、参数原理（为什么这样发布）

| 参数 | 作用 | 为什么需要 |
|---|---|---|
| `-c Release` | Release 配置（优化、无调试信息） | 发布版性能与体积 |
| `-r win-x64` | 目标 RID（Runtime Identifier） | 收集平台特定原生库（Avalonia 的 Win32/Skia 等） |
| `--self-contained` | 捆绑 .NET 运行时 | 目标机**无需安装 .NET**，双击即用（~208MB） |
| `-p:PublishSingleFile=false` | 保留多文件布局 | 单文件打包对 Avalonia 原生库兼容性差；多文件更稳 |
| `-m:1` | 单 MSBuild 节点 | 本机内存受限，多节点并行易 OOM |
| `-p:UseSharedCompilation=false` | 禁用 Roslyn 共享编译器 | 避免残留编译服务器进程占内存/锁文件 |
| `DOTNET_CLI_USE_MSBUILD_SERVER=0` | 禁用 MSBuild Server | 同上，防后台进程残留 |

### 为什么不用"直接把 Debug exe 拷过去"
Debug 构建是框架依赖 + 未优化，且不含运行时；目标机无 .NET 9 则无法运行。**正式分发必须 publish**。

## 四、环境坑位（本机特有，新会话必读）

1. **dotnet 不在 PATH**：SDK 装在用户目录 `%LOCALAPPDATA%\Microsoft\dotnet`（非 Program Files）。
   必须用完整路径 `"$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe"`，或先设 `$env:DOTNET_ROOT`。
   （Node/run_code 环境缺用户环境变量——LOCALAPPDATA 为 undefined，**build/publish 一律走 pwsh**）
2. **项目根**：代码在 `D:\MangaAgent\app`（csproj 在 `app\src\*`），不是 `D:\MangaAgent` 下。
   build/test/publish 的 workdir = `D:\MangaAgent\app`。
3. **行尾 CRLF**：axaml/cs 文件为 CRLF。脚本批量替换多行文本时用 `\r\n`，否则匹配失败
   （可能破坏文件结构——替换前先 split 计数验证唯一匹配）。
4. **Avalonia 12 API**：`Visual.VisualParent` 为 internal、`IsVisualDescendantOf` 不存在，
   可视树向上遍历用扩展方法 `GetVisualParent()`（using Avalonia.VisualTree）。
5. **测试输出**：Console.WriteLine 在 headless 被吞，诊断用 Assert 消息；测试输出重定向到 UTF-16 文件再读。

## 五、验证清单（发布前确认）

- [ ] build 0 错误
- [ ] 全量测试通过（67/67 基线）
- [ ] publish 输出目录存在且含 HachimiManga.exe
- [ ] robocopy 后 app\publish 时间戳为最新
- [ ] （可选）pack-release.ps1 生成的 zip 在 app\dist\ 且可解压运行
- [ ] 发布版启动存活 ≥5 秒
- [ ] git 无未提交改动（或已按需提交）

## 六、相关文档

- 环境搭建：[setup.md](setup.md)（DOTNET_ROOT 修复记录）
- 代码地图：[CODE-MAP.md](CODE-MAP.md)（`## 发布` 简版命令）
- 变更记录：[v2-changes-record.md](v2-changes-record.md)（每次发布后更新验证状态）
