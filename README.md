# TinyTools

TinyTools 是一个面向 Windows 10/11 的轻量桌面工具集。目前包含 SSH 隧道管理器和句柄查看器，使用 .NET 8 与 WPF 开发。

## 功能

### SSH 隧道管理器

- 创建、编辑、删除以及批量启动/停止 SSH 反向隧道
- 支持密码和 OpenSSH 私钥认证
- 首次连接确认 Host Key 指纹，后续连接自动校验
- 使用 Windows DPAPI 加密保存主机地址和密码
- SSH 断线检测与最多 5 次自动重连
- 隧道连通性检测和运行日志
- 浅色、深色及跟随系统主题

### 句柄查看器

- 查看 IPv4/IPv6 TCP 监听端口和连接对应的进程
- 按端口、PID、进程名或地址筛选
- 打开进程所在目录、复制详情或终止进程
- 查询占用指定文件的进程，支持文件选择与拖放
- 对关键系统进程提供终止保护

### 应用外壳

- 两个工具共用 TinyTools 导航入口
- 设置入口位于左下角，由 TinyTools 统一管理
- 单实例运行
- 可在启动时最小化到系统托盘
- 可配置关闭窗口时进入托盘或直接退出
- 可选择默认启动页面或记住上次使用的工具
- 可配置退出确认、托盘通知和句柄查看器自动刷新
- 自包含单文件发布，目标机器无需预装 .NET

## 系统要求

- Windows 10 20H1（版本 2004）或更高版本
- 构建需要 .NET 8 SDK 或更高版本
- SSH 隧道功能需要可访问的 SSH Server；本地代理需由 Clash、v2ray 等程序提供
- 查看部分系统进程或终止进程时可能需要管理员权限

## 构建与测试

在仓库根目录运行：

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

该脚本依次恢复依赖、运行测试，并将 win-x64 自包含单文件版本发布到 `src\TinyTools\publish`。仅发布、不运行测试：

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1 -SkipTests
```

也可以分别运行：

```powershell
dotnet test .\tests\TinyTools.Tests\TinyTools.Tests.csproj -c Release
dotnet build .\src\TinyTools\TinyTools.csproj -c Release
```

## 配置与日志

运行时数据位于可执行文件旁的 `data` 目录：

```text
TinyTools.exe
data/
├── config.json
├── config.json.bak
└── crash.log
```

保存配置时会先保留上一版备份，并通过临时文件原子替换主配置。旧版本位于 `%APPDATA%\SSHTunnelManager\config.json` 的配置会在首次启动时自动迁移。

主机地址和密码由 DPAPI 加密，绑定当前 Windows 用户。私钥文件只保存路径，不复制私钥内容。请将整个 TinyTools 目录放在当前用户可写的位置，否则配置和崩溃日志无法写入。

## 发布

推送 `v*` 标签后，[GitHub Actions](.github/workflows/release.yml) 会运行测试、生成 `TinyTools-<版本>-win-x64.zip` 并创建 GitHub Release。手动运行工作流只生成可下载的构建产物，不创建 Release。

## 当前状态

核心功能已经完成并可构建发布。自动化测试覆盖配置读写与备份恢复、DPAPI 加解密、模型复制、转换器和文件锁空结果等基础行为。SSH 连接、网络中断重连、Host Key 变更及需要管理员权限的操作仍应在真实 Windows 环境中进行发布前验收。

详细设计背景见 [技术方案 v2](docs/方案-v2.md)；该文档记录了最初的 SSH 模块设计，实际产品结构以本 README 和当前代码为准。
