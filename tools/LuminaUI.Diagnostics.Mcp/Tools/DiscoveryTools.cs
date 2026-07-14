using System.ComponentModel;
using LuminaUI.Diagnostics.Abstractions;
using ModelContextProtocol.Server;

namespace LuminaUI.Diagnostics.Mcp.Tools;

[McpServerToolType]
public sealed class DiscoveryTools
{
    [McpServerTool(Name = LuminaUIDiagnosticsToolNames.DiscoverApps, ReadOnly = true, Destructive = false, UseStructuredContent = true),
     Description("Discover running LuminaUI.Diagnostics-enabled Avalonia applications for the current user.")]
    public static async Task<DiscoveryResult> DiscoverApps(
        LuminaUI.Diagnostics.Mcp.Discovery.AppDiscoveryService discovery,
        [Description("Maximum probe time per application in milliseconds.")] int timeoutMs = 1_000,
        CancellationToken cancellationToken = default)
    {
        var apps = await discovery.DiscoverAsync(timeoutMs, cancellationToken).ConfigureAwait(false);
        return new DiscoveryResult(
            apps.Count,
            apps,
            LuminaUIDiagnosticsProtocol.Name,
            LuminaUIDiagnosticsProtocol.Version);
    }

    [McpServerTool(Name = LuminaUIDiagnosticsToolNames.ConnectApp, ReadOnly = false, Destructive = false, UseStructuredContent = true),
     Description("Select a discovered LuminaUI.Diagnostics application as the default target for later tool calls.")]
    public static async Task<ConnectAppResult> ConnectApp(
        LuminaUI.Diagnostics.Mcp.Discovery.AppDiscoveryService discovery,
        LuminaUI.Diagnostics.Mcp.Targeting.TargetSession session,
        [Description("Process ID returned by discover_apps.")] int pid,
        [Description("Maximum time in milliseconds to verify the application.")] int timeoutMs = 1_000,
        CancellationToken cancellationToken = default)
    {
        if (pid <= 0)
            return new ConnectAppResult(false, null, "Process ID must be a positive integer.");

        var descriptor = await discovery
            .FindByProcessIdAsync(pid, timeoutMs, cancellationToken)
            .ConfigureAwait(false);
        if (descriptor is null)
        {
            return new ConnectAppResult(
                false,
                null,
                $"No reachable LuminaUI.Diagnostics application was found for process {pid}.");
        }

        return new ConnectAppResult(true, session.Select(descriptor), "Target application selected.");
    }

    [McpServerTool(Name = LuminaUIDiagnosticsToolNames.GetCurrentApp, ReadOnly = true, Destructive = false, UseStructuredContent = true),
     Description("Get the application currently selected as the default diagnostics target.")]
    public static CurrentAppResult GetCurrentApp(LuminaUI.Diagnostics.Mcp.Targeting.TargetSession session)
    {
        var current = session.GetCurrent();
        return new CurrentAppResult(current is not null, current);
    }

    [McpServerTool(Name = LuminaUIDiagnosticsToolNames.DisconnectApp, ReadOnly = false, Destructive = false, UseStructuredContent = true),
     Description("Clear the default diagnostics target without stopping the application.")]
    public static CurrentAppResult DisconnectApp(LuminaUI.Diagnostics.Mcp.Targeting.TargetSession session)
    {
        var previous = session.Clear();
        return new CurrentAppResult(false, previous);
    }
}

public sealed record DiscoveryResult(
    int Count,
    IReadOnlyList<DiagnosticAppDescriptor> Apps,
    string ProtocolName,
    string ProtocolVersion);

public sealed record ConnectAppResult(
    bool Success,
    LuminaUI.Diagnostics.Mcp.Targeting.TargetSessionSnapshot? Target,
    string Message);

public sealed record CurrentAppResult(
    bool Connected,
    LuminaUI.Diagnostics.Mcp.Targeting.TargetSessionSnapshot? Target);
