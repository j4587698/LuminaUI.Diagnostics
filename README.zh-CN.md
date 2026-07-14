# LuminaUI.Diagnostics

[English README](README.md)

[LuminaUI](https://github.com/anomalyco/LuminaUI) 的实时诊断基础设施。通过命名管道协议暴露 Avalonia 应用的控件树、属性、数据上下文、样式、资源和绑定错误，并提供 [MCP](https://modelcontextprotocol.io) stdio server 方便 AI 工具（如 opencode）直接交互。

> LuminaUI.Diagnostics 是 LuminaUI 生态的独立仓库。如果你在使用 LuminaUI 组件库，可以直接引用对应的 NuGet 包来启用诊断能力。

---

## 包结构

| 包名 | 类型 | 用途 |
| --- | --- | --- |
| `LuminaUI.Diagnostics.Abstractions` | NuGet | 共享的命名管道协议契约，供 Host、Client 和 MCP 工具使用。 |
| `LuminaUI.Diagnostics` | NuGet | 应用侧诊断 Host，通过命名管道暴露控件树、属性、数据上下文、样式、资源、绑定错误等。 |
| `LuminaUI.Diagnostics.Mcp` | dotnet tool | MCP stdio server，连接运行中应用的诊断 Host，将诊断能力暴露给 MCP 客户端（如 opencode）。 |

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

`UseLuminaUIDiagnostics()` 默认启动命名管道服务器并注册 F12 打开 DevTools。

仅需 MCP 不要 DevTools？关闭即可：

```csharp
.UseLuminaUIDiagnostics(o => o.EnableDevTools = false);
```

自定义快捷键：

```csharp
.UseLuminaUIDiagnostics(o => {
    o.DevToolsGesture = new KeyGesture(Key.F12, KeyModifiers.Ctrl);
});
```

### 2. 使用 MCP 工具

安装全局 tool（传统方式）：

```bash
dotnet tool install -g LuminaUI.Diagnostics.Mcp
lumina-mcp
```

.NET 10+ 可用 `dnx` 免安装运行（类似 `npx`）：

```bash
dnx lumina-mcp
```

然后在支持 MCP 的客户端（如 opencode、VS Code）中配置：

```json
{
  "servers": {
    "LuminaUI.Diagnostics": {
      "type": "stdio",
      "command": "lumina-mcp"
    }
  }
}
```

如果用的是 `dnx` 免安装方式，command 改成：

```json
{
  "servers": {
    "LuminaUI.Diagnostics": {
      "type": "stdio",
      "command": "dnx",
      "args": ["lumina-mcp"]
    }
  }
}
```

### 3. 独立 DevTools 窗口

`tools/LuminaUI.Diagnostics.Desktop` 是一个独立的桌面进程，通过命名管道连接到应用，提供 DevTools 窗口（需在应用侧设置 `options.StartImmediately = true` 或调用 `UseLuminaUIDiagnostics()`）。

---

## 功能特性

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

## 构建

```bash
dotnet build
```

测试：

```bash
dotnet test
```

---

## 开源协议

MIT
