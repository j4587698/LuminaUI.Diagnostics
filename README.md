# LuminaUI.Diagnostics

[中文说明](README.zh-CN.md)

Real-time diagnostics infrastructure for [LuminaUI](https://github.com/anomalyco/LuminaUI) — an Avalonia component library. Exposes the visual/logical tree, properties, data context, styles, resources, and binding errors of a running Avalonia application over a named-pipe protocol, plus an [MCP](https://modelcontextprotocol.io) stdio server for AI tooling (e.g. opencode).

---

## Packages

| Package | Type | Description |
| --- | --- | --- |
| `LuminaUI.Diagnostics.Abstractions` | NuGet | Shared named-pipe protocol contracts for host, client, and MCP tooling. |
| `LuminaUI.Diagnostics` | NuGet | Application-side diagnostics host that exposes the control tree, properties, data context, styles, resources, and binding errors over a named pipe. |
| `LuminaUI.Diagnostics.Mcp` | dotnet tool | MCP stdio server that connects to a running diagnostics host and exposes its capabilities to MCP clients. |

---

## Quick Start

### 1. Enable the diagnostics host in your app

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

`UseLuminaUIDiagnostics()` starts the named-pipe server and registers F12 to open the DevTools window by default.

Disable the DevTools window if you only need MCP:

```csharp
.UseLuminaUIDiagnostics(o => o.EnableDevTools = false);
```

Custom shortcut:

```csharp
.UseLuminaUIDiagnostics(o => {
    o.DevToolsGesture = new KeyGesture(Key.F12, KeyModifiers.Ctrl);
});
```

### 2. Use the MCP tool

Install the global tool (traditional):

```bash
dotnet tool install -g LuminaUI.Diagnostics.Mcp
lumina-mcp
```

.NET 10+ users can run on-the-fly with `dnx` (like `npx`):

```bash
dnx lumina-mcp
```

Then configure your MCP client (opencode, VS Code, etc.):

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

For the `dnx` one-shot approach:

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

### 3. Standalone DevTools window

`tools/LuminaUI.Diagnostics.Desktop` is a standalone desktop process that connects to the app over a named pipe and provides a DevTools window (the host must be started with `options.StartImmediately = true` or `UseLuminaUIDiagnostics()`).

---

## Features

- **Tree inspection** — Visual/logical tree traversal, search by name, type, or text
- **Property editing** — Read and set Avalonia properties (StyledProperty / DirectProperty / CLR)
- **Data context** — View DataContext type and property values, expand collections
- **Binding errors** — Capture and display Avalonia binding errors
- **Style inspection** — View applied style classes, pseudo-classes, and style setter summaries
- **Resource inspection** — View application-level and control-level resources
- **Interaction** — Click controls, input text, invoke commands, wait for property changes
- **Scroll diagnostics** — ScrollViewer state and virtualization info
- **Screenshots** — Capture window or control screenshots
- **Hot reload** — AXAML hot reload support

---

## Build

```bash
dotnet build
```

Test:

```bash
dotnet test
```

---

## License

MIT
