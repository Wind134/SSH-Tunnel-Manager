# SSH Tunnel Manager

A Windows 11 native GUI tool for managing SSH reverse tunnels. Built with WPF + ModernWpf + SSH.NET.

## Purpose

When you have a proxy (Clash/v2ray/etc.) running on your local Windows machine and need to share it with a remote server (e.g., Tencent Cloud) that can't reach GitHub directly, this tool creates an SSH reverse tunnel (`ssh -R`) so the remote machine can route traffic through your local proxy.

## Features

- One-click start/stop SSH reverse tunnels
- Password and private key SSH authentication
- Host key fingerprint verification (first-connection dialog)
- DPAPI-encrypted storage of IP and password
- IP masking in the UI (e.g., `123.45.***.***`)
- Auto-reconnect (up to 5 attempts)
- Connectivity test (verifies the tunnel actually works)
- Single-instance enforcement
- Light theme UI (dark / system-following theme not yet implemented)

## Build

### Prerequisites

- .NET SDK 8.0+ ([download](https://dotnet.microsoft.com/download/dotnet/8.0))

### Build

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1
```

Or manually:

```bash
cd src\SSHTunnelManager
dotnet restore
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableWindowsTargeting=true -o .\publish
```

The output is a single `SSHTunnelManager.exe` (~70MB, self-contained, no runtime needed).

## How It Works

The tool uses SSH.NET's `ForwardedPortRemote` to create a reverse port forward:

```
Local machine (proxy on :7890)
    |
    |  SSH reverse tunnel
    v
Remote machine (listens on :1080)
    -> forwards traffic back through SSH to local :7890
```

On the remote machine, you then configure git to use `http://127.0.0.1:1080` as proxy:

```bash
git config --global http.proxy http://127.0.0.1:1080
git config --global https.proxy http://127.0.0.1:1080
```

## Config Storage

Config file: `%APPDATA%\SSHTunnelManager\config.json`

Sensitive fields (IP, password) are encrypted with Windows DPAPI, bound to the current user account. Even if the config file is stolen, it cannot be decrypted on another machine or by another user.

## Tech Stack

- **.NET 8** (net8.0-windows10.0.19041.0)
- **WPF** with hand-written XAML styles (no third-party UI library such as ModernWpf)
- **SSH.NET** (Renci.SshNet) for SSH connections and port forwarding
- **System.Security.Cryptography.ProtectedData** for DPAPI encryption
