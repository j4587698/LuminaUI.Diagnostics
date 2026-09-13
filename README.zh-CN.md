# LuminaUI.Diagnostics

[English README](README.md)

针对 Avalonia 应用的实时诊断基础设施。通过命名管道协议暴露运行中 Avalonia 应用的控件树、属性、数据上下文、样式、资源和绑定错误，并提供统一的全局 CLI / 桌面工具 `lumina`，同时支持 [MCP](https://modelcontextprotocol.io) stdio server（方便 AI 工具如 opencode、Cursor 直接交互）以及独立进程级 DevTools 可视化调试面板。

> **轻量零污染**：宿主诊断包 `LuminaUI.Diagnostics` 仅依赖原生 Avalonia，**不包含任何第三方组件库依赖**，任何 Avalonia 项目均可放心接入；所有 DevTools 窗口界面与 UI 组件均隔离在独立的 `lumina` 进程中运行。

---

## 包结构

| 包名 | 类型 | 用途 |
| --- | --- | --- |
| `LuminaUI.Diagnostics.Abstractions` | NuGet | 共享的命名管道协议契约，供 Host、Client 和 MCP 工具使用。 |
| `LuminaUI.Diagnostics` | NuGet | 应用侧轻量诊断 Host，通过命名管道暴露控件树、属性、数据上下文、样式、资源、绑定错误等（纯 Avalonia，零外部 UI 依赖）。 |
| `LuminaUI.Diagnostics.Tool` | dotnet tool | 统一诊断工具（CLI 命令 `lumina`），包含 MCP stdio 服务与独立进程级 DevTools 调试面板。 |

---

## 快速开始

### 1. 在 Avalonia 应用中启用诊断 Host

```bash
dotnet add package LuminaUI.Diagnostics
```

```csharp
// Program.cs
using LuminaUI.Diagnostics;

public static AppBuilder BuildAvaloniaApp()
    => AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .UseSkia()
        .UseHarfBuzz()
        .WithInterFont()
        .LogToTrace()
#if DEBUG
        .UseLuminaUIDiagnostics();
#endif
```

`UseLuminaUIDiagnostics()` 默认启动命名管道服务器，并在用户按下 F12 时自动唤起外部 `lumina` 调试器窗口。

仅需 MCP 不要 DevTools 唤起？关闭即可：

```csharp
.UseLuminaUIDiagnostics(o => o.EnableDevTools = false);
```

自定义快捷键：

```csharp
.UseLuminaUIDiagnostics(o => {
    o.DevToolsGesture = new KeyGesture(Key.F12, KeyModifiers.Ctrl);
});
```

---

### 2. 安装与使用统一诊断工具 (`lumina`)

安装全局工具（一次安装，AI 调试与可视化面板全齐）：

```bash
dotnet tool install -g LuminaUI.Diagnostics.Tool
```

.NET 10+ 也可以用 `dnx` 免安装即开即用（类似 `npx`）：

```bash
dnx lumina --help
```

#### A. 作为 MCP Server 运行（供 AI 客户端使用）

在支持 MCP 的客户端（如 opencode、Cursor、VS Code、Claude Desktop）中配置：

```json
{
  "servers": {
    "LuminaUI.Diagnostics": {
      "type": "stdio",
      "command": "lumina",
      "args": ["mcp"]
    }
  }
}
```

如果使用 `dnx` 免安装方式：

```json
{
  "servers": {
    "LuminaUI.Diagnostics": {
      "type": "stdio",
      "command": "dnx",
      "args": ["lumina", "mcp"]
    }
  }
}
```

#### B. 作为独立 DevTools 调试面板运行

```bash
# 启动 DevTools 窗口（自动扫描并连接本机运行中的 Avalonia 应用）
lumina

# 或明确指定子命令
lumina devtools

# 或连接指定进程与管道
lumina devtools --target-pid 12345 --pipe lumina-diag-12345
```

---

## 功能特性

- **进程级解耦** — 调试面板在独立进程中运行，不占用目标应用内存与 GC，不污染用户依赖
- **控件树检查** — 可视化/逻辑树遍历，支持按名称、类型或文本搜索
- **属性编辑** — 读取和设置 Avalonia 属性（StyledProperty / DirectProperty / CLR 属性）
- **数据上下文** — 查看 DataContext 类型和属性值，支持展开集合
- **绑定错误** — 捕获并展示 Avalonia 绑定错误
- **样式检查** — 查看应用的样式类和伪类、样式 Setter 摘要
- **资源检查** — 查看应用级和控件级资源
- **交互操作** — 点击控件、输入文本、调用命令、等待属性变化
- **滚动诊断** — 查看 ScrollViewer 状态和虚拟化信息
- **截图** — 捕获窗口或控件截图
- **热重载** — AXAML 热重载支持

---

## 构建与测试

构建整套方案：

```bash
dotnet build
```

运行全部单元测试：

```bash
dotnet test
```

打包全局工具：

```bash
dotnet pack tools/LuminaUI.Diagnostics.Tool
```

---

## 开源协议

MIT
