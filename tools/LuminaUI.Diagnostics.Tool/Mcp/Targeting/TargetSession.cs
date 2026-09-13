using LuminaUI.Diagnostics.Abstractions;

namespace LuminaUI.Diagnostics.Mcp.Targeting;

public sealed class TargetSession
{
    private readonly object _gate = new();
    private TargetSessionSnapshot? _current;

    public TargetSessionSnapshot? GetCurrent()
    {
        lock (_gate)
        {
            return _current;
        }
    }

    public TargetSessionSnapshot Select(DiagnosticAppDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        var snapshot = new TargetSessionSnapshot(
            descriptor.ProcessId,
            descriptor.ProcessName,
            descriptor.PipeName,
            descriptor.SessionId,
            descriptor.ApplicationSessionId,
            descriptor.ProtocolVersion,
            descriptor.StartedAtUtc);

        lock (_gate)
        {
            _current = snapshot;
        }

        return snapshot;
    }

    public TargetSessionSnapshot? Clear()
    {
        lock (_gate)
        {
            var previous = _current;
            _current = null;
            return previous;
        }
    }
}

public sealed record TargetSessionSnapshot(
    int ProcessId,
    string ProcessName,
    string PipeName,
    string SessionId,
    string ApplicationSessionId,
    string ProtocolVersion,
    DateTimeOffset StartedAtUtc);
