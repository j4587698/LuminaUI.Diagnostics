# LuminaUI Diagnostics MCP 工具

`LuminaUI.Diagnostics.Tool` 是统一的 dotnet tool 包（全局命令 `lumina`），内置 stdio MCP server 把 MCP 客户端连接到运行中的 Avalonia 应用。

它只负责 live diagnostics，不索引文档。组件知识、示例、设计令牌、API 和包安装信息由独立的 `DotNetCatalog.Mcp` 服务提供。

## 应用侧接入

应用需要引用 `LuminaUI.Diagnostics`，并在 `AppBuilder` 上启用 diagnostics：

```csharp
using LuminaUI.Diagnostics;

public static AppBuilder BuildAvaloniaApp()
    => AppBuilder.Configure<App>()
        .UsePlatformDetect()
#if DEBUG
        .UseLuminaUIDiagnostics()
#endif
        ;
```

建议默认把 diagnostics 限制在 `#if DEBUG` 下，除非这是明确的内部诊断构建。同时把包引用也限制为 Debug 条件，避免 Release publish 输出包含 diagnostics 包：

```xml
<ItemGroup Condition="'$(Configuration)' == 'Debug'">
  <PackageReference Include="LuminaUI.Diagnostics" Version="<resolved-version>" />
</ItemGroup>
```

如果先执行了 `dotnet add package LuminaUI.Diagnostics`，保留它生成的版本号，把这条 `PackageReference` 移到带 `Condition` 的 `ItemGroup` 里。

默认命名管道格式：

```text
lumina-ui-diagnostics-{pid}
```

## 运行工具

```powershell
dotnet tool install --global LuminaUI.Diagnostics.Tool
lumina mcp
```

本仓库开发时也可以直接运行项目：

```powershell
dotnet run --project tools/LuminaUI.Diagnostics.Tool/LuminaUI.Diagnostics.Tool.csproj -- mcp
```

## 目标选择

推荐先调用 `discover_apps` 获取运行中的应用，再调用 `connect_app` 选择当前目标。选择后，后续 tools 不需要重复传递 `pid`：

```text
discover_apps -> connect_app(pid) -> list_windows -> ...
```

- `get_current_app`：查看当前默认目标。
- `disconnect_app`：清除当前默认目标，但不会关闭应用。

大多数面向应用的 tools 仍支持 `pid` 或 `pipe` 参数，用于临时覆盖当前目标：

- `pid`：根据进程 ID 自动拼出默认命名管道。
- `pipe`：直接指定完整命名管道名。
- `timeoutMs`：单次请求超时时间，默认 30000 毫秒。

## 常用 Tools

- `discover_apps`：发现启用 LuminaUI diagnostics 的运行中应用。
- `connect_app` / `get_current_app` / `disconnect_app`：管理默认目标会话。
- `list_windows`：列出 Avalonia 窗口。
- `get_visual_tree` / `get_logical_tree`：读取控件树。
- `find_control`：按名称、类型或文本搜索控件。
- `get_control_properties`：读取控件属性。
- `get_data_context`：读取 DataContext。
- `get_binding_errors`：读取绑定错误。
- `take_screenshot`：截取窗口或控件截图。支持 `maxWidth`（等比缩放）、`format`/`quality`（jpeg 压缩）、`crop`（"x,y,w,h" 裁剪）缩小回传体积；传 `saveToFile` 时写入文件（重复调用覆盖），只回传元数据（path/width/height/bytes/frameHash）不回传图像。
- `click_control`：在控件中心合成指针按下/抬起事件（走 Avalonia 输入管线，可翻转 ToggleButton/RadioButton）；无法接收指针输入时回退为直接触发 Click/Command。
- `input_text`：向 TextBox 或其子 TextBox 写入文本；目标不是 TextBox 时回退为逐字符按键注入。
- `send_keys`：聚焦目标后逐字符发送 KeyDown/KeyUp 与 TextInput 事件，自绘控件（终端、画布）也能接收文本。
- `set_property` / `invoke_command`：设置属性、调用命令。
