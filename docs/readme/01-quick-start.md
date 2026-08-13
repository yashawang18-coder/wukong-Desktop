# 01 Quick Start

## 环境要求

| 项 | 要求 |
|---|---|
| OS | Windows 10/11，用于真实 WPF 透明窗口验证 |
| SDK | .NET 8 SDK |
| UI | WPF，项目入口为 `src/Wukong.Desktop` |
| Python | 用于 `tools/validate_contracts.py` 和 `tests/` 下的素材/契约测试 |

## 常用命令

```powershell
dotnet --list-sdks
dotnet --version
dotnet build Wukong.sln --no-restore -v minimal
dotnet run --project src\Wukong.Desktop\Wukong.Desktop.csproj --no-build
```

发布便携目录：

```powershell
dotnet publish src\Wukong.Desktop\Wukong.Desktop.csproj `
  -c Release `
  --self-contained false `
  -o .publish-check\runtime-assets-integration-20260812 `
  -v minimal
```

发布后的 EXE 示例：

```text
.publish-check/runtime-assets-integration-20260812/Wukong.Desktop.exe
```

## 启动链路

```mermaid
sequenceDiagram
    participant OS as Windows
    participant Program as Program.Main
    participant App as App.xaml / App.xaml.cs
    participant Startup as DesktopStartup
    participant Main as MainWindow
    participant Runtime as DesktopRuntimeHost

    OS->>Program: start Wukong.Desktop.exe
    Program->>App: new App()
    Program->>App: InitializeComponent()
    Program->>App: Run()
    App->>Startup: EnsureMainWindow(this)
    Startup->>Main: new MainWindow()
    Main->>Runtime: load runtime host
    Runtime->>Runtime: load WukongAssets
    App->>Main: Show()
```

## 日志位置

Bootstrap 诊断日志：

```text
%LOCALAPPDATA%\Wukong\logs\bootstrap\yyyyMMdd.log
```

运行日志：

```text
%LOCALAPPDATA%\Wukong\logs\<runtime-mode>\yyyyMMdd.log
```

日志约束：

- 默认脱敏。
- 保留 30 天。
- 总量上限 50MB。
- 写入或清理失败不能阻塞桌宠主流程。

## 第一次运行时看什么

```mermaid
flowchart TD
    A[启动 EXE] --> B{窗口可见?}
    B -- 否 --> C[看 bootstrap log]
    B -- 是 --> D[双击打开控制面板]
    D --> E[开发者页]
    E --> F[查看素材库和 DeveloperTrace]
    F --> G{候选动作?}
    G -- failed/candidate --> H[只能开发者强制预览]
    G -- runtime-approved + runtime_use --> I[可进入正式运行注册表]
```

