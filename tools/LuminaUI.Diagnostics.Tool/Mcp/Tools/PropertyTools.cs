using System.ComponentModel;
using System.Text.Json.Nodes;
using LuminaUI.Diagnostics.Abstractions;
using ModelContextProtocol.Server;

namespace LuminaUI.Diagnostics.Mcp.Tools;

[McpServerToolType]
public sealed class PropertyTools
{
    [McpServerTool(Name = LuminaUIDiagnosticsToolNames.GetControlProperties, ReadOnly = true, Destructive = false, UseStructuredContent = true),
     Description("Get Avalonia and selected CLR properties for a target control.")]
    public static async Task<JsonObject> GetControlProperties(
        ToolForwarder forwarder,
        [Description("Node identifier from get_visual_tree/get_logical_tree responses.")] string nodeId,
        [Description("Optional property names to filter the response.")] string[]? propertyNames = null,
        [Description("Include public CLR properties in addition to Avalonia properties.")] bool includeClrProperties = false,
        [Description("Only include attached properties relevant to the current parent context.")] bool contextualProperties = false,
        [Description("Optional window index from list_windows.")] int? windowIndex = null,
        [Description("Window identifier from list_windows response.")] string? windowId = null,
        [Description("Target process ID.")] int? pid = null,
        [Description("Explicit LuminaUI.Diagnostics pipe name. Takes precedence over pid.")] string? pipe = null,
        [Description("Maximum time in milliseconds to wait for the operation to complete.")] int timeoutMs = LuminaUIDiagnosticsProtocol.DefaultTimeoutMs,
        CancellationToken cancellationToken = default)
    {
        var response = await forwarder.ForwardAsync(
            LuminaUIDiagnosticsToolNames.GetControlProperties,
            ToolForwarder.Parameters(
                ("nodeId", nodeId),
                ("propertyNames", propertyNames is { Length: > 0 } ? propertyNames : null),
                ("includeClrProperties", includeClrProperties),
                ("contextualProperties", contextualProperties),
                ("windowIndex", windowIndex), ("windowId", windowId)),
            pid, pipe, timeoutMs, cancellationToken);
        return response.Data as JsonObject ?? new JsonObject();
    }

    [McpServerTool(Name = LuminaUIDiagnosticsToolNames.GetDataContext, ReadOnly = true, Destructive = false, UseStructuredContent = true),
     Description("Get DataContext type and public property summaries for a target control.")]
    public static async Task<JsonObject> GetDataContext(
        ToolForwarder forwarder,
        [Description("Node identifier from get_visual_tree/get_logical_tree responses. If omitted, uses main window.")] string? nodeId = null,
        [Description("Optional collection property to expand.")] string? expandProperty = null,
        [Description("Maximum collection items to include when expanding a property.")] int maxItems = 50,
        [Description("Optional window index from list_windows.")] int? windowIndex = null,
        [Description("Window identifier from list_windows response.")] string? windowId = null,
        [Description("Target process ID.")] int? pid = null,
        [Description("Explicit LuminaUI.Diagnostics pipe name. Takes precedence over pid.")] string? pipe = null,
        [Description("Maximum time in milliseconds to wait for the operation to complete.")] int timeoutMs = LuminaUIDiagnosticsProtocol.DefaultTimeoutMs,
        CancellationToken cancellationToken = default)
    {
        var response = await forwarder.ForwardAsync(
            LuminaUIDiagnosticsToolNames.GetDataContext,
            ToolForwarder.Parameters(
                ("nodeId", nodeId),
                ("expandProperty", expandProperty),
                ("maxItems", maxItems), ("windowIndex", windowIndex), ("windowId", windowId)),
            pid, pipe, timeoutMs, cancellationToken);
        return response.Data as JsonObject ?? new JsonObject();
    }

    [McpServerTool(Name = LuminaUIDiagnosticsToolNames.GetBindingErrors, ReadOnly = true, Destructive = false, UseStructuredContent = true),
     Description("Get binding errors captured by the target diagnostics host.")]
    public static async Task<JsonObject> GetBindingErrors(
        ToolForwarder forwarder,
        [Description("Target process ID.")] int? pid = null,
        [Description("Explicit LuminaUI.Diagnostics pipe name. Takes precedence over pid.")] string? pipe = null,
        [Description("Maximum time in milliseconds to wait for the operation to complete.")] int timeoutMs = LuminaUIDiagnosticsProtocol.DefaultTimeoutMs,
        CancellationToken cancellationToken = default)
    {
        var response = await forwarder.ForwardAsync(
            LuminaUIDiagnosticsToolNames.GetBindingErrors, [], pid, pipe, timeoutMs, cancellationToken);
        return response.Data as JsonObject ?? new JsonObject();
    }

    [McpServerTool(Name = LuminaUIDiagnosticsToolNames.GetBindingExpressions, ReadOnly = true, Destructive = false, UseStructuredContent = true),
     Description("Get active binding expressions for a target control, including current values, priority, and errors.")]
    public static async Task<JsonObject> GetBindingExpressions(
        ToolForwarder forwarder,
        [Description("Node identifier from a tree response.")] string nodeId,
        [Description("Target process ID.")] int? pid = null,
        [Description("Explicit LuminaUI.Diagnostics pipe name. Takes precedence over pid.")] string? pipe = null,
        [Description("Maximum time in milliseconds to wait for the operation to complete.")] int timeoutMs = LuminaUIDiagnosticsProtocol.DefaultTimeoutMs,
        CancellationToken cancellationToken = default)
    {
        var response = await forwarder.ForwardAsync(
            LuminaUIDiagnosticsToolNames.GetBindingExpressions,
            ToolForwarder.Parameters(("nodeId", nodeId)),
            pid, pipe, timeoutMs, cancellationToken);
        return response.Data as JsonObject ?? new JsonObject();
    }

    [McpServerTool(Name = LuminaUIDiagnosticsToolNames.GetAppliedStyles, ReadOnly = true, Destructive = false, UseStructuredContent = true),
     Description("Get style classes, pseudo-classes, and style setter summaries for a target control.")]
    public static async Task<JsonObject> GetAppliedStyles(
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
            LuminaUIDiagnosticsToolNames.GetAppliedStyles,
            ToolForwarder.Parameters(
                ("nodeId", nodeId),
                ("windowIndex", windowIndex), ("windowId", windowId)),
            pid, pipe, timeoutMs, cancellationToken);
        return response.Data as JsonObject ?? new JsonObject();
    }

    [McpServerTool(Name = LuminaUIDiagnosticsToolNames.GetResources, ReadOnly = true, Destructive = false, UseStructuredContent = true),
     Description("Get application or control resource summaries from the target diagnostics host.")]
    public static async Task<JsonObject> GetResources(
        ToolForwarder forwarder,
        [Description("Node identifier from get_visual_tree/get_logical_tree responses. If omitted, returns application resources.")] string? nodeId = null,
        [Description("Optional window index from list_windows.")] int? windowIndex = null,
        [Description("Window identifier from list_windows response.")] string? windowId = null,
        [Description("Target process ID.")] int? pid = null,
        [Description("Explicit LuminaUI.Diagnostics pipe name. Takes precedence over pid.")] string? pipe = null,
        [Description("Maximum time in milliseconds to wait for the operation to complete.")] int timeoutMs = LuminaUIDiagnosticsProtocol.DefaultTimeoutMs,
        CancellationToken cancellationToken = default)
    {
        var response = await forwarder.ForwardAsync(
            LuminaUIDiagnosticsToolNames.GetResources,
            ToolForwarder.Parameters(
                ("nodeId", nodeId),
                ("windowIndex", windowIndex), ("windowId", windowId)),
            pid, pipe, timeoutMs, cancellationToken);
        return response.Data as JsonObject ?? new JsonObject();
    }

    [McpServerTool(Name = LuminaUIDiagnosticsToolNames.SetResource, ReadOnly = false, Destructive = false, UseStructuredContent = true),
     Description("Update an existing runtime resource value using an entryId returned by get_resources.")]
    public static async Task<JsonObject> SetResource(
        ToolForwarder forwarder,
        [Description("Resource entry identifier returned by get_resources.")] string entryId,
        [Description("New value converted to the resource's current runtime type.")] string value,
        [Description("Target process ID.")] int? pid = null,
        [Description("Explicit LuminaUI.Diagnostics pipe name. Takes precedence over pid.")] string? pipe = null,
        [Description("Maximum time in milliseconds to wait for the operation to complete.")] int timeoutMs = LuminaUIDiagnosticsProtocol.DefaultTimeoutMs,
        CancellationToken cancellationToken = default)
    {
        var response = await forwarder.ForwardAsync(
            LuminaUIDiagnosticsToolNames.SetResource,
            ToolForwarder.Parameters(("entryId", entryId), ("value", value)),
            pid, pipe, timeoutMs, cancellationToken);
        return response.Data as JsonObject ?? new JsonObject();
    }

    [McpServerTool(Name = LuminaUIDiagnosticsToolNames.GetAssets, ReadOnly = true, Destructive = false, UseStructuredContent = true),
     Description("List Avalonia embedded assets from loaded application and dependency assemblies.")]
    public static async Task<JsonObject> GetAssets(
        ToolForwarder forwarder,
        [Description("Target process ID.")] int? pid = null,
        [Description("Explicit LuminaUI.Diagnostics pipe name. Takes precedence over pid.")] string? pipe = null,
        [Description("Maximum time in milliseconds to wait for the operation to complete.")] int timeoutMs = 15000,
        CancellationToken cancellationToken = default)
    {
        var response = await forwarder.ForwardAsync(
            LuminaUIDiagnosticsToolNames.GetAssets, [], pid, pipe, timeoutMs, cancellationToken);
        return response.Data as JsonObject ?? new JsonObject();
    }
}
