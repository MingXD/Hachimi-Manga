# 开发环境搭建

> 2026-02 记录：本机没有 winget；Program Files 下只有 .NET 6 运行时（无 SDK）。已通过官方 dotnet-install.ps1 安装 .NET 9 SDK 到用户目录。

## .NET SDK（已装：%LOCALAPPDATA%\Microsoft\dotnet，版本 9.0.317 / 运行时 9.0.19）

### ⚠️ 环境修复记录（2026-02，重要）
**问题**：直接双击 exe 会弹"需要安装 .NET"——SDK 装在用户目录但系统不认识（DOTNET_ROOT 为空，
Program Files\dotnet 只有 6.0.2 运行时，找不到 9.x）。

**已修复**（用户级，无需管理员）：
```powershell
[Environment]::SetEnvironmentVariable("DOTNET_ROOT", "$env:LOCALAPPDATA\Microsoft\dotnet", "User")
# 并把 %LOCALAPPDATA%\Microsoft\dotnet 加入 User PATH
```
验证：应用窗口正常启动并渲染封面墙（无弹窗）。

**注意**：
- 新开的终端/进程会自动获得新环境变量；Explorer 可能需重启或注销一次才刷新。
- **根治方案**：正式打包用 **self-contained 发布**（自带运行时，见 todo：M1 打包），
  任何机器双击即可运行，无需安装 .NET。

### 常用命令
```powershell
$dotnet = "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe"
& $dotnet --version
```

## 构建
```powershell
cd D:\MangaAgent\app
dotnet restore
dotnet build
dotnet test
```

## 测试夹具
- 样本 CBZ：用 tools/make-fixtures 脚本生成（画有页码的 PNG 打包为 CBZ）
- 样本库目录：`reference/fixtures/library/`
