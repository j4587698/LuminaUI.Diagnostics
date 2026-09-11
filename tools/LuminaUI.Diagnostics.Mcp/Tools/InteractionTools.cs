using System.ComponentModel;
using System.Text.Json.Nodes;
using LuminaUI.Diagnostics.Abstractions;
using ModelContextProtocol.Server;

namespace LuminaUI.Diagnostics.Mcp.Tools;

[McpServerToolType]
public sealed class InteractionTools
{
    [McpServerTool(Name = LuminaUIDiagnosticsToolNames.ClickControl, ReadOnly = false, Destructive = false, UseStructuredContent = true),
     Description("Click a target control by synthesizing pointer press/release at its center through the Avalonia input pipeline (toggles ToggleButton/RadioButton); falls back to raising its click/command when pointer input is not possible.")]
    public static async Task<JsonObject> ClickControl(
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
            LuminaUIDiagnosticsToolNames.ClickControl,
            ToolForwarder.Parameters(
                ("nodeId", nodeId),
                ("windowIndex", windowIndex), ("windowId", windowId)),
            pid, pipe, timeoutMs, cancellationToken);
        return response.Data as JsonObject ?? new JsonObject();
    }

    [McpServerTool(Name = LuminaUIDiagnosticsToolNames.SetProperty, ReadOnly = false, Destructive = false, UseStructuredContent = true),
     Description("Set or clear a supported Avalonia property on a target control.")]
    public static async Task<JsonObject> SetProperty(
        ToolForwarder forwarder,
        [Description("Property name to set.")] string propertyName,
        [Description("Node identifier from get_visual_tree/get_logical_tree responses.")] string nodeId,
        [Description("String value converted by the diagnostics host. Required unless clearLocalValue is true.")] string? value = null,
        [Description("Clear the local Avalonia value and restore style/default resolution instead of assigning value.")] bool clearLocalValue = false,
        [Description("Optional window index from list_windows.")] int? windowIndex = null,
        [Description("Window identifier from list_windows response.")] string? windowId = null,
        [Description("Target process ID.")] int? pid = null,
        [Description("Explicit LuminaUI.Diagnostics pipe name. Takes precedence over pid.")] string? pipe = null,
        [Description("Maximum time in milliseconds to wait for the operation to complete.")] int timeoutMs = LuminaUIDiagnosticsProtocol.DefaultTimeoutMs,
        CancellationToken cancellationToken = default)
    {
        var response = await forwarder.ForwardAsync(
            LuminaUIDiagnosticsToolNames.SetProperty,
            ToolForwarder.Parameters(
                ("propertyName", propertyName), ("nodeId", nodeId), ("value", value),
                ("clearLocalValue", clearLocalValue),
                ("windowIndex", windowIndex), ("windowId", windowId)),
            pid, pipe, timeoutMs, cancellationToken);
        return response.Data as JsonObject ?? new JsonObject();
    }

    [McpServerTool(Name = LuminaUIDiagnosticsToolNames.InputText, ReadOnly = false, Destructive = false, UseStructuredContent = true),
     Description("Set text on a TextBox target or the first TextBox child inside a target control. Falls back to key/text-input injection for custom-drawn controls.")]
    public static async Task<JsonObject> InputText(
        ToolForwarder forwarder,
        [Description("Text to input.")] string text,
        [Description("Node identifier from get_visual_tree/get_logical_tree responses.")] string nodeId,
        [Description("Whether to press Enter after setting text.")] bool pressEnter = false,
        [Description("Optional window index from list_windows.")] int? windowIndex = null,
        [Description("Window identifier from list_windows response.")] string? windowId = null,
        [Description("Target process ID.")] int? pid = null,
        [Description("Explicit LuminaUI.Diagnostics pipe name. Takes precedence over pid.")] string? pipe = null,
        [Description("Maximum time in milliseconds to wait for the operation to complete.")] int timeoutMs = LuminaUIDiagnosticsProtocol.DefaultTimeoutMs,
        CancellationToken cancellationToken = default)
    {
        var response = await forwarder.ForwardAsync(
            LuminaUIDiagnosticsToolNames.InputText,
            ToolForwarder.Parameters(
                ("text", text), ("nodeId", nodeId), ("pressEnter", pressEnter),
                ("windowIndex", windowIndex), ("windowId", windowId)),
            pid, pipe, timeoutMs, cancellationToken);
        return response.Data as JsonObject ?? new JsonObject();
    }

    [McpServerTool(Name = LuminaUIDiagnosticsToolNames.SendKeys, ReadOnly = false, Destructive = false, UseStructuredContent = true),
     Description("Focus a target control and send text as individual key down/up and text-input events, so custom-drawn controls (terminals, canvases) receive input.")]
    public static async Task<JsonObject> SendKeys(
        ToolForwarder forwarder,
        [Description("Text to send to the focused control.")] string text,
        [Description("Node identifier from get_visual_tree/get_logical_tree responses.")] string nodeId,
        [Description("Optional window index from list_windows.")] int? windowIndex = null,
        [Description("Window identifier from list_windows response.")] string? windowId = null,
        [Description("Target process ID.")] int? pid = null,
        [Description("Explicit LuminaUI.Diagnostics pipe name. Takes precedence over pid.")] string? pipe = null,
        [Description("Maximum time in milliseconds to wait for the operation to complete.")] int timeoutMs = LuminaUIDiagnosticsProtocol.DefaultTimeoutMs,
        CancellationToken cancellationToken = default)
    {
        var response = await forwarder.ForwardAsync(
            LuminaUIDiagnosticsToolNames.SendKeys,
            ToolForwarder.Parameters(
                ("text", text), ("nodeId", nodeId),
                ("windowIndex", windowIndex), ("windowId", windowId)),
            pid, pipe, timeoutMs, cancellationToken);
        return response.Data as JsonObject ?? new JsonObject();
    }

    [McpServerTool(Name = LuminaUIDiagnosticsToolNames.InvokeCommand, ReadOnly = false, Destructive = false, UseStructuredContent = true),
     Description("Invoke an ICommand exposed by the target control DataContext.")]
    public static async Task<JsonObject> InvokeCommand(
        ToolForwarder forwarder,
        [Description("ICommand property name on the DataContext.")] string commandName,
        [Description("Node identifier from get_visual_tree/get_logical_tree responses.")] string nodeId,
        [Description("Optional command parameter.")] string? parameter = null,
        [Description("Optional window index from list_windows.")] int? windowIndex = null,
        [Description("Window identifier from list_windows response.")] string? windowId = null,
        [Description("Target process ID.")] int? pid = null,
        [Description("Explicit LuminaUI.Diagnostics pipe name. Takes precedence over pid.")] string? pipe = null,
        [Description("Maximum time in milliseconds to wait for the operation to complete.")] int timeoutMs = LuminaUIDiagnosticsProtocol.DefaultTimeoutMs,
        CancellationToken cancellationToken = default)
    {
        var response = await forwarder.ForwardAsync(
            LuminaUIDiagnosticsToolNames.InvokeCommand,
            ToolForwarder.Parameters(
                ("commandName", commandName), ("nodeId", nodeId), ("parameter", parameter),
                ("windowIndex", windowIndex), ("windowId", windowId)),
            pid, pipe, timeoutMs, cancellationToken);
        return response.Data as JsonObject ?? new JsonObject();
    }

    [McpServerTool(Name = LuminaUIDiagnosticsToolNames.WaitForProperty, ReadOnly = true, Destructive = false, UseStructuredContent = true),
     Description("Poll a control, Avalonia property, CLR property, or DataContext property until it reaches an expected value.")]
    public static async Task<JsonObject> WaitForProperty(
        ToolForwarder forwarder,
        [Description("Property name to watch.")] string propertyName,
        [Description("Expected value as a string.")] string expectedValue,
        [Description("Node identifier from get_visual_tree/get_logical_tree responses. If omitted, uses main window.")] string? nodeId = null,
        [Description("Polling interval in milliseconds.")] int pollIntervalMs = 500,
        [Description("Optional window index from list_windows.")] int? windowIndex = null,
        [Description("Window identifier from list_windows response.")] string? windowId = null,
        [Description("Target process ID.")] int? pid = null,
        [Description("Explicit LuminaUI.Diagnostics pipe name. Takes precedence over pid.")] string? pipe = null,
        [Description("Maximum time in milliseconds to wait for the property to match.")] int timeoutMs = LuminaUIDiagnosticsProtocol.DefaultTimeoutMs,
        CancellationToken cancellationToken = default)
    {
        var transportTimeoutMs = timeoutMs > 0 ? timeoutMs + 5_000 : timeoutMs;

        var response = await forwarder.ForwardAsync(
            LuminaUIDiagnosticsToolNames.WaitForProperty,
            ToolForwarder.Parameters(
                ("nodeId", nodeId),
                ("propertyName", propertyName), ("expectedValue", expectedValue),
                ("timeoutMs", timeoutMs), ("pollIntervalMs", pollIntervalMs),
                ("windowIndex", windowIndex), ("windowId", windowId)),
            pid, pipe, transportTimeoutMs, cancellationToken);
        return response.Data as JsonObject ?? new JsonObject();
    }
}
