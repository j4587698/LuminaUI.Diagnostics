using System.Diagnostics;
using LuminaUI.Diagnostics.Abstractions;

namespace LuminaUI.Diagnostics.Discovery;

internal sealed class DiagnosticsAppRegistration : IDisposable
{
    private readonly string _descriptorPath;
    private bool _disposed;

    private DiagnosticsAppRegistration(string descriptorPath, DiagnosticAppDescriptor descriptor)
    {
        _descriptorPath = descriptorPath;
        Descriptor = descriptor;
    }

    public DiagnosticAppDescriptor Descriptor { get; }

    public static DiagnosticsAppRegistration? TryCreate(
        string pipeName,
        string? applicationSessionId = null)
    {
        var processId = Environment.ProcessId;
        var descriptor = new DiagnosticAppDescriptor
        {
            ProcessId = processId,
            ProcessName = Process.GetCurrentProcess().ProcessName,
            PipeName = pipeName,
            SessionId = Guid.NewGuid().ToString("N"),
            ApplicationSessionId = applicationSessionId ?? string.Empty,
            ProtocolName = LuminaUIDiagnosticsProtocol.Name,
            ProtocolVersion = LuminaUIDiagnosticsProtocol.Version,
            StartedAtUtc = DateTimeOffset.UtcNow
        };

        var descriptorPath = DiagnosticsDiscoveryPath.ForProcess(processId);
        var temporaryPath = $"{descriptorPath}.{descriptor.SessionId}.tmp";

        try
        {
            Directory.CreateDirectory(DiagnosticsDiscoveryPath.DirectoryPath);
            File.WriteAllText(temporaryPath, DiagnosticJson.SerializeAppDescriptor(descriptor));
            File.Move(temporaryPath, descriptorPath, overwrite: true);
            return new DiagnosticsAppRegistration(descriptorPath, descriptor);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            TryDelete(temporaryPath);
            return null;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        TryDelete(_descriptorPath);
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
