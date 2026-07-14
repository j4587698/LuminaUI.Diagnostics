using System.ComponentModel;
using System.Text.Json.Nodes;
using LuminaUI.Diagnostics.Abstractions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace LuminaUI.Diagnostics.Mcp.Tools;

[McpServerToolType]
public sealed class VisualTools
{
    [McpServerTool(Name = LuminaUIDiagnosticsToolNames.TakeScreenshot, ReadOnly = true, Destructive = false),
     Description("Capture a PNG screenshot of a target window or control.")]
    public static async Task<ImageContentBlock> TakeScreenshot(
        ToolForwarder forwarder,
        [Description("Optional window index from list_windows.")] int windowIndex = 0,
        [Description("Node identifier from get_visual_tree/get_logical_tree/list_windows responses. If omitted, captures the whole window.")] string? nodeId = null,
        [Description("Window identifier from list_windows response.")] string? windowId = null,
        [Description("Target process ID.")] int? pid = null,
        [Description("Explicit LuminaUI.Diagnostics pipe name. Takes precedence over pid.")] string? pipe = null,
        [Description("Maximum time in milliseconds to wait for the operation to complete.")] int timeoutMs = LuminaUIDiagnosticsProtocol.DefaultTimeoutMs,
        CancellationToken cancellationToken = default)
    {
        var response = await forwarder.ForwardAsync(
            LuminaUIDiagnosticsToolNames.TakeScreenshot,
            ToolForwarder.Parameters(("windowIndex", windowIndex),
                ("nodeId", nodeId), ("windowId", windowId)),
            pid, pipe, timeoutMs, cancellationToken);

        if (!response.Success)
        {
            var message = response.Error?.Message ?? "Screenshot capture failed.";
            throw new InvalidOperationException(message);
        }

        if (response.Data is JsonObject json
            && json.TryGetPropertyValue("base64", out var base64Node)
            && base64Node is JsonValue base64Val
            && base64Val.TryGetValue<string>(out var base64)
            && !string.IsNullOrWhiteSpace(base64))
        {
            var mimeType = json["mimeType"]?.GetValue<string>() ?? "image/png";
            return ImageContentBlock.FromBytes(Convert.FromBase64String(base64), mimeType);
        }

        throw new InvalidOperationException("Screenshot response did not contain valid image data.");
    }
}
