# 开发环境搭建

> 2026-02 记录：本机没有 winget；Program Files 下只有 .NET 6 运行时（无 SDK）。已通过官方 dotnet-install.ps1 安装 .NET 9 SDK 到用户目录。

## .NET SDK（已装：%LOCALAPPDATA%\Microsoft\dotnet）
```powershell
# 全路径调用（PATH 未持久化）
$dotnet = "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe"
& $dotnet --version
# 或为当前会话临时加入 PATH
$env:PATH = "$env:LOCALAPPDATA\Microsoft\dotnet;" + $env:PATH
dotnet --list-sdks
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
