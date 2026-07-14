using System.Diagnostics;
using System.Text.Json;
using LuminaUI.Diagnostics.Abstractions;
using LuminaUI.Diagnostics.Mcp.Transport;

namespace LuminaUI.Diagnostics.Mcp.Discovery;

public sealed class AppDiscoveryService
{
    private readonly PipeDiagnosticClient _client;

    public AppDiscoveryService(PipeDiagnosticClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public async Task<IReadOnlyList<DiagnosticAppDescriptor>> DiscoverAsync(
        int timeoutMs = 1_000,
        CancellationToken cancellationToken = default)
    {
        var descriptors = ReadDescriptors().ToArray();
        if (descriptors.Length == 0)
            return [];

        var probes = descriptors.Select(
            descriptor => ProbeAsync(descriptor, timeoutMs, cancellationToken));
        var results = await Task.WhenAll(probes).ConfigureAwait(false);

        return results
            .Where(static descriptor => descriptor is not null)
            .Select(static descriptor => descriptor!)
            .OrderBy(static descriptor => descriptor.ProcessName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static descriptor => descriptor.ProcessId)
            .ToArray();
    }

    public async Task<DiagnosticAppDescriptor?> FindByProcessIdAsync(
        int processId,
        int timeoutMs = 1_000,
        CancellationToken cancellationToken = default)
    {
        if (processId <= 0)
            return null;

        var descriptorPath = DiagnosticsDiscoveryPath.ForProcess(processId);
        var descriptor = ReadDescriptor(descriptorPath);
        if (descriptor is null)
            return null;

        if (!IsProcessActive(descriptor))
        {
            TryDelete(descriptorPath);
            return null;
        }

        return await ProbeAsync(descriptor, timeoutMs, cancellationToken).ConfigureAwait(false);
    }

    private async Task<DiagnosticAppDescriptor?> ProbeAsync(
        DiagnosticAppDescriptor descriptor,
        int timeoutMs,
        CancellationToken cancellationToken)
    {
        var normalizedTimeoutMs = Math.Clamp(timeoutMs, 100, 10_000);
        var request = new DiagnosticRequest(
            Guid.NewGuid().ToString("N"),
            LuminaUIDiagnosticsToolNames.Ping,
            timeoutMs: normalizedTimeoutMs);
        var response = await _client
            .SendAsync(descriptor.PipeName, request, cancellationToken)
            .ConfigureAwait(false);

        return response.Success ? descriptor : null;
    }

    private static IEnumerable<DiagnosticAppDescriptor> ReadDescriptors()
    {
        if (!Directory.Exists(DiagnosticsDiscoveryPath.DirectoryPath))
            yield break;

        string[] files;
        try
        {
            files = Directory.GetFiles(DiagnosticsDiscoveryPath.DirectoryPath, "*.json");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            yield break;
        }

        foreach (var file in files)
        {
            var descriptor = ReadDescriptor(file);
            if (descriptor is null)
                continue;

            if (!IsProcessActive(descriptor))
            {
                TryDelete(file);
                continue;
            }

            yield return descriptor;
        }
    }

    private static DiagnosticAppDescriptor? ReadDescriptor(string path)
    {
        try
        {
            var descriptor = DiagnosticJson.DeserializeAppDescriptor(File.ReadAllText(path));
            return descriptor.ProcessId > 0
                && !string.IsNullOrWhiteSpace(descriptor.PipeName)
                && string.Equals(
                    descriptor.ProtocolName,
                    LuminaUIDiagnosticsProtocol.Name,
                    StringComparison.Ordinal)
                ? descriptor
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private static bool IsProcessActive(DiagnosticAppDescriptor descriptor)
    {
        try
        {
            using var process = Process.GetProcessById(descriptor.ProcessId);
            return !process.HasExited;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
        }
    }
}
