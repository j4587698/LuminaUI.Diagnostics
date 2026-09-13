# LuminaUI.Diagnostics

[中文说明](README.zh-CN.md)

Real-time diagnostics infrastructure for Avalonia applications. Exposes the visual/logical tree, properties, data context, styles, resources, and binding errors of a running Avalonia application over a named-pipe protocol. Features a unified CLI and desktop tool `lumina`, providing both an [MCP](https://modelcontextprotocol.io) stdio server (for AI tools like opencode, Cursor, VS Code) and an out-of-process DevTools desktop UI.

> **Zero UI Pollution**: The host package `LuminaUI.Diagnostics` depends purely on Avalonia with **zero third-party UI dependencies**. The DevTools UI is entirely hosted in an isolated `lumina` external process.

---

## Packages

| Package | Type | Description |
| --- | --- | --- |
| `LuminaUI.Diagnostics.Abstractions` | NuGet | Shared named-pipe protocol contracts for host, client, and tools. |
| `LuminaUI.Diagnostics` | NuGet | Application-side lightweight diagnostics host that exposes the control tree, properties, data context, styles, resources, and binding errors over a named pipe (pure Avalonia). |
| `LuminaUI.Diagnostics.Tool` | dotnet tool | Unified diagnostics tool (command `lumina`), including MCP stdio server and standalone process-level DevTools. |

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

`UseLuminaUIDiagnostics()` starts the named-pipe server and launches the external `lumina` DevTools when F12 is pressed.

Disable DevTools launcher if you only need MCP:

```csharp
.UseLuminaUIDiagnostics(o => o.EnableDevTools = false);
```

Custom shortcut:

```csharp
.UseLuminaUIDiagnostics(o => {
    o.DevToolsGesture = new KeyGesture(Key.F12, KeyModifiers.Ctrl);
});
```

---

### 2. Install and use the unified tool (`lumina`)

Install the global tool once:

```bash
dotnet tool install -g LuminaUI.Diagnostics.Tool
```

.NET 10+ users can also run on-the-fly with `dnx`:

```bash
dnx lumina --help
```

#### A. Run as MCP Server (for AI clients)

Configure your MCP client (opencode, Cursor, VS Code, Claude Desktop):

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

For the `dnx` on-the-fly approach:

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

#### B. Run as standalone DevTools window

```bash
# Launch DevTools (auto-discovers and connects to running Avalonia apps)
lumina

# Or specify the subcommand
lumina devtools

# Or connect to a specific process and pipe
lumina devtools --target-pid 12345 --pipe lumina-diag-12345
```

---

## Features

- **Process-level isolation** — DevTools runs in its own process, avoiding GC, memory pressure, and theme conflicts in target apps
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

## Build & Test

Build the solution:

```bash
dotnet build
```

Run unit tests:

```bash
dotnet test
```

Pack global tool:

```bash
dotnet pack tools/LuminaUI.Diagnostics.Tool
```

---

## License

MIT
