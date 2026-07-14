using System.ComponentModel;
using System.Text.Json.Nodes;
using LuminaUI.Diagnostics.Abstractions;
using ModelContextProtocol.Server;

namespace LuminaUI.Diagnostics.Mcp.Tools;

[McpServerToolType]
public sealed class InspectionTools
{
    [McpServerTool(Name = LuminaUIDiagnosticsToolNames.ListWindows, ReadOnly = true, Destructive = false, UseStructuredContent = true),
     Description("List windows in the target LuminaUI.Diagnostics-enabled Avalonia application.")]
    public static async Task<JsonObject> ListWindows(
        ToolForwarder forwarder,
        [Description("Target process ID. When provided, LuminaUI.Diagnostics uses the default pipe name lumina-ui-diagnostics-{pid}.")] int? pid = null,
        [Description("Explicit LuminaUI.Diagnostics pipe name. Takes precedence over pid.")] string? pipe = null,
        [Description("Maximum time in milliseconds to wait for the operation to complete.")] int timeoutMs = LuminaUIDiagnosticsProtocol.DefaultTimeoutMs,
        CancellationToken cancellationToken = default)
    {
        var response = await forwarder.ForwardAsync(
            LuminaUIDiagnosticsToolNames.ListWindows, [], pid, pipe, timeoutMs, cancellationToken);
        return response.Data as JsonObject ?? new JsonObject();
    }

    [McpServerTool(Name = LuminaUIDiagnosticsToolNames.GetVisualTree, ReadOnly = true, Destructive = false, UseStructuredContent = true),
     Description("Get a bounded visual tree for a target window or control.")]
    public static async Task<JsonObject> GetVisualTree(
        ToolForwarder forwarder,
        [Description("Optional window index from list_windows.")] int windowIndex = 0,
        [Description("Node identifier from list_windows or get_visual_tree response.")] string? nodeId = null,
        [Description("Window identifier from list_windows response.")] string? windowId = null,
        [Description("Maximum traversal depth.")] int maxDepth = 10,
        [Description("Target process ID.")] int? pid = null,
        [Description("Explicit LuminaUI.Diagnostics pipe name. Takes precedence over pid.")] string? pipe = null,
        [Description("Maximum time in milliseconds to wait for the operation to complete.")] int timeoutMs = LuminaUIDiagnosticsProtocol.DefaultTimeoutMs,
        CancellationToken cancellationToken = default)
    {
        var response = await forwarder.ForwardAsync(
            LuminaUIDiagnosticsToolNames.GetVisualTree,
            Parameters(("windowIndex", windowIndex),
                ("nodeId", nodeId), ("windowId", windowId), ("maxDepth", maxDepth)),
            pid, pipe, timeoutMs, cancellationToken);
        return response.Data as JsonObject ?? new JsonObject();
    }

    [McpServerTool(Name = LuminaUIDiagnosticsToolNames.GetLogicalTree, ReadOnly = true, Destructive = false, UseStructuredContent = true),
     Description("Get a bounded logical tree for a target window or control.")]
    public static async Task<JsonObject> GetLogicalTree(
        ToolForwarder forwarder,
        [Description("Optional window index from list_windows.")] int windowIndex = 0,
        [Description("Node identifier from list_windows or get_logical_tree response.")] string? nodeId = null,
        [Description("Window identifier from list_windows response.")] string? windowId = null,
        [Description("Maximum traversal depth.")] int maxDepth = 10,
        [Description("Target process ID.")] int? pid = null,
        [Description("Explicit LuminaUI.Diagnostics pipe name. Takes precedence over pid.")] string? pipe = null,
        [Description("Maximum time in milliseconds to wait for the operation to complete.")] int timeoutMs = LuminaUIDiagnosticsProtocol.DefaultTimeoutMs,
        CancellationToken cancellationToken = default)
    {
        var response = await forwarder.ForwardAsync(
            LuminaUIDiagnosticsToolNames.GetLogicalTree,
            Parameters(("windowIndex", windowIndex),
                ("nodeId", nodeId), ("windowId", windowId), ("maxDepth", maxDepth)),
            pid, pipe, timeoutMs, cancellationToken);
        return response.Data as JsonObject ?? new JsonObject();
    }

    [McpServerTool(Name = LuminaUIDiagnosticsToolNames.FindControl, ReadOnly = true, Destructive = false, UseStructuredContent = true),
     Description("Search controls by name, type, or displayed text across target windows.")]
    public static async Task<JsonObject> FindControl(
        ToolForwarder forwarder,
        [Description("Name filter.")] string? name = null,
        [Description("Type name filter, such as Button or TextBox.")] string? typeName = null,
        [Description("Displayed text filter.")] string? text = null,
        [Description("Maximum number of matches to return.")] int maxResults = 20,
        [Description("Optional window index from list_windows. If omitted, searches all windows.")] int? windowIndex = null,
        [Description("Window identifier from list_windows response.")] string? windowId = null,
        [Description("Target process ID.")] int? pid = null,
        [Description("Explicit LuminaUI.Diagnostics pipe name. Takes precedence over pid.")] string? pipe = null,
        [Description("Maximum time in milliseconds to wait for the operation to complete.")] int timeoutMs = LuminaUIDiagnosticsProtocol.DefaultTimeoutMs,
        CancellationToken cancellationToken = default)
    {
        var response = await forwarder.ForwardAsync(
            LuminaUIDiagnosticsToolNames.FindControl,
            Parameters(("name", name), ("typeName", typeName), ("text", text),
                ("maxResults", maxResults), ("windowIndex", windowIndex), ("windowId", windowId)),
            pid, pipe, timeoutMs, cancellationToken);
        return response.Data as JsonObject ?? new JsonObject();
    }

    [McpServerTool(Name = LuminaUIDiagnosticsToolNames.GetFocusedElement, ReadOnly = true, Destructive = false, UseStructuredContent = true),
     Description("Get the currently focused element in the target application.")]
    public static async Task<JsonObject> GetFocusedElement(
        ToolForwarder forwarder,
        [Description("Optional window index from list_windows. If omitted, checks all windows.")] int? windowIndex = null,
        [Description("Window identifier from list_windows response.")] string? windowId = null,
        [Description("Target process ID.")] int? pid = null,
        [Description("Explicit LuminaUI.Diagnostics pipe name. Takes precedence over pid.")] string? pipe = null,
        [Description("Maximum time in milliseconds to wait for the operation to complete.")] int timeoutMs = LuminaUIDiagnosticsProtocol.DefaultTimeoutMs,
        CancellationToken cancellationToken = default)
    {
        var response = await forwarder.ForwardAsync(
            LuminaUIDiagnosticsToolNames.GetFocusedElement,
            Parameters(("windowIndex", windowIndex), ("windowId", windowId)),
            pid, pipe, timeoutMs, cancellationToken);
        return response.Data as JsonObject ?? new JsonObject();
    }

    private static JsonObject Parameters(params (string Name, object? Value)[] values) =>
        ToolForwarder.Parameters(values);
}
