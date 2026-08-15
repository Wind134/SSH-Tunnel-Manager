# TinyTools

一款为 Windows 11 重新设计的轻量桌面工具箱：在同一个原生应用里管理 SSH 隧道、查看端口进程，以及定位占用文件或文件夹的程序。

[下载最新版本](https://github.com/Wind134/SSH-Tunnel-Manager/releases/latest) · [迁移进度](docs/winui3-migration.md) · [发布检查清单](docs/发布检查清单.md)

## 一个应用，处理三类日常问题

### SSH 隧道

- 创建、编辑、删除并批量启动或停止 SSH 反向隧道
- 支持密码与 OpenSSH 私钥认证
- 首次连接显示 Host Key 指纹，已信任密钥发生变化时阻止连接
- 断线检测与自动重连，状态和日志在应用内持续更新
- 主机地址与密码由 Windows DPAPI 加密，绑定当前 Windows 用户

### 端口与进程

- 查看 IPv4/IPv6 TCP 监听端口与已建立连接
- 按连接类型、协议、端口、PID、进程名或地址即时筛选
- 支持按设置自动刷新，并可隐藏系统内核记录
- 右键复制详情、打开进程所在目录或强制终止进程树
- PID 0/4 禁止终止；关键 Windows 进程会显示明确的高风险确认

### 文件和文件夹占用

- 直接拖入文件或文件夹，也可以使用原生选择器或输入路径
- 递归查询文件夹内被占用的文件，显示进程、应用名、启动时间和路径
- 大目录扫描可随时取消；无法访问的目录和扫描上限会明确提示
- 占用结果同样支持打开目录、复制详情和安全的进程终止流程

## Windows 11 原生体验

新的应用层使用 WinUI 3 / Windows App SDK 构建，采用原生标题栏、NavigationView、Mica、Fluent 控件和克制的页面切换动画。Windows 10 会自动使用普通主题背景，不依赖 Mica 也能正常使用。

- 浅色、深色和跟随系统主题
- 单实例运行、系统托盘、关闭到托盘和通知
- 记住上次页面，也可指定 SSH、端口或文件占用为启动页
- DPI 感知的默认/最小窗口尺寸，并记住最后一次正常窗口大小
- unpackaged、self-contained、单文件发布，目标电脑无需预装 .NET
- 配置备份、原子保存以及旧版本配置自动迁移

## 下载与更新

GitHub Release 提供便携 ZIP；正式切换前仍保留 WPF 安装包作为回退。WinUI 包使用以下独立命名，避免与旧应用混淆：

```text
TinyTools-WinUI-v<版本>-win-x64.zip
TinyTools-WinUI-v<版本>-win-x64.zip.sha256
```

WinUI 应用可在“设置 → 关于与更新”中检查 GitHub 最新版本。下载器只接受 WinUI 命名的资产，并在交付前验证 SHA-256。ZIP 下载完成后会引导退出应用并覆盖程序文件；配置位于独立 `data` 目录，不包含在更新包中。未来 WinUI 安装包完成验收后，应用可以在用户确认后启动安装程序完成升级。

## 系统要求

- Windows 10 x64 或 Windows 11 x64；项目最低目标为 Windows 10 build 17763
- SSH 功能需要能够访问 SSH Server；本地代理需由 Clash、v2ray 等程序提供
- 查看或终止受保护进程时可能需要管理员权限

## 隐私与安全

TinyTools 不需要账户，也不会上传隧道配置、进程列表或文件路径。更新检查只访问公开的 GitHub Releases API。主机地址与密码使用 DPAPI 加密；私钥文件只保存本地路径，不会被复制进配置。

运行时数据默认位于可执行文件旁：

```text
TinyTools.WinUI.exe
data/
├── config.json
├── config.json.bak
└── crash.log
```

请将便携版放在当前用户可写的位置。旧版 `%APPDATA%\SSHTunnelManager\config.json` 会在首次启动时自动迁移。

## 开发与验证

运行自动化测试和 WinUI Release 构建：

```powershell
dotnet test .\tests\TinyTools.Tests\TinyTools.Tests.csproj -c Release
dotnet build .\src\TinyTools.WinUI\TinyTools.WinUI.csproj -c Release
```

生成 unpackaged、self-contained、单文件 WinUI 版本：

```powershell
dotnet publish .\src\TinyTools.WinUI\TinyTools.WinUI.csproj `
  -c Release -r win-x64 --self-contained true `
  -p:PublishProfile=win-x64 -o .\artifacts\winui
```

现有 `build.ps1` 和 Inno Setup 脚本继续生成 WPF 稳定版，直至 WinUI 完成功能、安装升级和真机验收。推送 `v*` 标签后，GitHub Actions 会运行测试，生成 WPF 回退产物以及带 SHA-256 的 WinUI ZIP，并创建 GitHub Release。

## 迁移状态

WinUI 应用已覆盖 SSH 管理、Host Key 校验、自动重连、端口/进程查看、文件/文件夹占用、设置、主题、托盘、单实例、通知、配置迁移和 GitHub 更新下载。WPF 源码与发布链仍然保留，待 Windows 10/11 真机矩阵、WinUI Inno Setup 安装/升级以及完整可访问性验收通过后再进行发布切换。

详细的阶段记录、技术取舍和剩余验收项见 [WinUI 3 迁移文档](docs/winui3-migration.md)。
