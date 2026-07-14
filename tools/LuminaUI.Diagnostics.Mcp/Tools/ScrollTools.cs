using System.ComponentModel;
using System.Text.Json.Nodes;
using LuminaUI.Diagnostics.Abstractions;
using ModelContextProtocol.Server;

namespace LuminaUI.Diagnostics.Mcp.Tools;

[McpServerToolType]
public sealed class ScrollTools
{
    [McpServerTool(Name = LuminaUIDiagnosticsToolNames.Scroll, ReadOnly = false, Destructive = false, UseStructuredContent = true),
     Description("Read or change ScrollViewer state near a target control.")]
    public static async Task<JsonObject> Scroll(
        ToolForwarder forwarder,
        [Description("Node identifier from get_visual_tree/get_logical_tree responses.")] string? nodeId = null,
        [Description("Absolute horizontal offset.")] double? x = null,
        [Description("Absolute vertical offset.")] double? y = null,
        [Description("Relative horizontal delta.")] double? deltaX = null,
        [Description("Relative vertical delta.")] double? deltaY = null,
        [Description("Delta unit: pixels, lines, or pages.")] string? unit = null,
        [Description("Edge jump: top, bottom, left, right, home, or end.")] string? edge = null,
        [Description("Node identifier for the target control to bring into view.")] string? targetNodeId = null,
        [Description("ItemsControl item index to bring into view.")] int? itemIndex = null,
        [Description("Optional window index from list_windows.")] int? windowIndex = null,
        [Description("Window identifier from list_windows response.")] string? windowId = null,
        [Description("Target process ID.")] int? pid = null,
        [Description("Explicit LuminaUI.Diagnostics pipe name. Takes precedence over pid.")] string? pipe = null,
        [Description("Maximum time in milliseconds to wait for the operation to complete.")] int timeoutMs = LuminaUIDiagnosticsProtocol.DefaultTimeoutMs,
        CancellationToken cancellationToken = default)
    {
        var response = await forwarder.ForwardAsync(
            LuminaUIDiagnosticsToolNames.Scroll,
            ToolForwarder.Parameters(
                ("nodeId", nodeId),
                ("x", x), ("y", y),
                ("deltaX", deltaX), ("deltaY", deltaY),
                ("unit", unit), ("edge", edge),
                ("targetNodeId", targetNodeId),
                ("itemIndex", itemIndex),
                ("windowIndex", windowIndex), ("windowId", windowId)),
            pid, pipe, timeoutMs, cancellationToken);
        return response.Data as JsonObject ?? new JsonObject();
    }

    [McpServerTool(Name = LuminaUIDiagnosticsToolNames.GetScrollableItems, ReadOnly = true, Destructive = false, UseStructuredContent = true),
     Description("Get virtualization and scroll diagnostics for an ItemsControl.")]
    public static async Task<JsonObject> GetScrollableItems(
        ToolForwarder forwarder,
        [Description("Node identifier from get_visual_tree/get_logical_tree responses.")] string nodeId,
        [Description("Optional window index from list_windows.")] int? windowIndex = null,
        [Description("Window identifier from list_windows response.")] string? windowId = null,
        [Description("Target process ID.")] int? pid = null,
        [Description("Explicit LuminaUI.Diagnostics pipe name. Takes precedence over pid.")] string? pipe = null,
        [Description("Maximum time in milliseconds to wait for the operation to complete.")] int timeoutMs = LuminaUIDiagnosticsProtocol.DefaultTimeoutMs,
        CancellationToken cancellationToken = default)
    {
        var response = await forwarder.ForwardAsync(
            LuminaUIDiagnosticsToolNames.GetScrollableItems,
            ToolForwarder.Parameters(
                ("nodeId", nodeId),
                ("windowIndex", windowIndex), ("windowId", windowId)),
            pid, pipe, timeoutMs, cancellationToken);
        return response.Data as JsonObject ?? new JsonObject();
    }
}
