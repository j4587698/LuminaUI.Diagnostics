using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using LuminaUI.Diagnostics.Abstractions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace LuminaUI.Diagnostics.Mcp.Tools;

[McpServerToolType]
public sealed class VisualTools
{
    [McpServerTool(Name = LuminaUIDiagnosticsToolNames.TakeScreenshot, ReadOnly = true, Destructive = false),
     Description("Capture a screenshot of a target window or control. Returns an image by default; pass maxWidth/format/quality/crop to shrink it, or saveToFile to write it to disk and get back only metadata (path, size, bytes, frameHash).")]
    public static async Task<ContentBlock> TakeScreenshot(
        ToolForwarder forwarder,
        [Description("Optional window index from list_windows.")] int windowIndex = 0,
        [Description("Node identifier from get_visual_tree/get_logical_tree/list_windows responses. If omitted, captures the whole window.")] string? nodeId = null,
        [Description("Window identifier from list_windows response.")] string? windowId = null,
        [Description("Scale the image down proportionally to fit within this pixel width.")] int? maxWidth = null,
        [Description("Image format: 'png' (default) or 'jpeg'.")] string? format = null,
        [Description("JPEG quality (1-100, default 80). Only used when format is 'jpeg'.")] int? quality = null,
        [Description("Crop rectangle 'x,y,width,height' in source pixels, applied before scaling.")] string? crop = null,
        [Description("Optional file path. When set, the image is written to this path (overwritten on repeat calls) and the response contains only metadata without image data.")] string? saveToFile = null,
        [Description("Target process ID.")] int? pid = null,
        [Description("Explicit LuminaUI.Diagnostics pipe name. Takes precedence over pid.")] string? pipe = null,
        [Description("Maximum time in milliseconds to wait for the operation to complete.")] int timeoutMs = LuminaUIDiagnosticsProtocol.DefaultTimeoutMs,
        CancellationToken cancellationToken = default)
    {
        var response = await forwarder.ForwardAsync(
            LuminaUIDiagnosticsToolNames.TakeScreenshot,
            ToolForwarder.Parameters(("windowIndex", windowIndex),
                ("nodeId", nodeId), ("windowId", windowId),
                ("maxWidth", maxWidth), ("format", format), ("quality", quality),
                ("crop", crop), ("saveToFile", saveToFile)),
            pid, pipe, timeoutMs, cancellationToken);

        if (!response.Success)
        {
            var message = response.Error?.Message ?? "Screenshot capture failed.";
            throw new InvalidOperationException(message);
        }

        if (response.Data is JsonObject json)
        {
            if (json.TryGetPropertyValue("base64", out var base64Node)
                && base64Node is JsonValue base64Val
                && base64Val.TryGetValue<string>(out var base64)
                && !string.IsNullOrWhiteSpace(base64))
            {
                var mimeType = json["mimeType"]?.GetValue<string>() ?? "image/png";
                return ImageContentBlock.FromBytes(Convert.FromBase64String(base64), mimeType);
            }

            // saveToFile responses carry metadata only (path/width/height/bytes/frameHash).
            return new TextContentBlock { Text = json.ToJsonString() };
        }

        throw new InvalidOperationException("Screenshot response did not contain valid image data.");
    }
}
