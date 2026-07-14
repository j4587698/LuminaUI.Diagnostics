using System.Text.Json.Nodes;
using LuminaUI.Diagnostics.Abstractions;
using LuminaUI.Diagnostics.Mcp.Discovery;
using LuminaUI.Diagnostics.Mcp.Targeting;
using LuminaUI.Diagnostics.Mcp.Transport;

namespace LuminaUI.Diagnostics.Tests.Discovery;

public sealed class AppDiscoveryTests
{
    [Fact]
    public void TargetSession_InitialState_IsNull()
    {
        var session = new TargetSession();
        Assert.Null(session.GetCurrent());
    }

    [Fact]
    public void TargetSession_Select_SetsCurrent()
    {
        var session = new TargetSession();
        var descriptor = new DiagnosticAppDescriptor
        {
            ProcessId = 42,
            ProcessName = "test",
            PipeName = "lumina-ui-diagnostics-42",
            SessionId = "s1",
            ApplicationSessionId = "as1",
            ProtocolVersion = "1.0",
            StartedAtUtc = DateTimeOffset.UtcNow
        };

        var snapshot = session.Select(descriptor);

        Assert.Equal(42, snapshot.ProcessId);
        Assert.Equal("test", snapshot.ProcessName);
        Assert.Equal("as1", snapshot.ApplicationSessionId);

        var current = session.GetCurrent();
        Assert.NotNull(current);
        Assert.Equal(42, current!.ProcessId);
    }

    [Fact]
    public void TargetSession_Clear_ReturnsPrevious()
    {
        var session = new TargetSession();
        session.Select(new DiagnosticAppDescriptor
        {
            ProcessId = 1, ProcessName = "a", PipeName = "p-1",
            SessionId = "s1", ApplicationSessionId = "as1",
            ProtocolVersion = "1.0", StartedAtUtc = DateTimeOffset.UtcNow
        });

        var previous = session.Clear();
        Assert.NotNull(previous);
        Assert.Equal(1, previous.ProcessId);
        Assert.Null(session.GetCurrent());
    }

    [Fact]
    public void TargetResolver_ExplicitPipe_Wins()
    {
        var resolution = TargetResolver.Resolve(
            new TargetOptions(null, "custom-pipe", 5000));

        Assert.True(resolution.Success);
        Assert.Equal("custom-pipe", resolution.DiagnosticsPipeName);
        Assert.Equal(5000, resolution.TimeoutMs);
    }

    [Fact]
    public void TargetResolver_Pid_GeneratesPipeName()
    {
        var resolution = TargetResolver.Resolve(
            new TargetOptions(99, null, 3000));

        Assert.True(resolution.Success);
        Assert.Equal("lumina-ui-diagnostics-99", resolution.DiagnosticsPipeName);
    }

    [Fact]
    public void TargetResolver_NoArgs_ReturnsError()
    {
        var resolution = TargetResolver.Resolve(
            new TargetOptions(null, null, 1000));

        Assert.False(resolution.Success);
        Assert.NotNull(resolution.Error);
    }

    [Fact]
    public void TargetResolver_NegativePid_ReturnsError()
    {
        var resolution = TargetResolver.Resolve(
            new TargetOptions(-1, null, 1000));

        Assert.False(resolution.Success);
    }

    [Fact]
    public void TargetResolver_SessionFallback_Works()
    {
        var session = new TargetSession();
        session.Select(new DiagnosticAppDescriptor
        {
            ProcessId = 42, ProcessName = "app",
            PipeName = "lumina-ui-diagnostics-42",
            SessionId = "s1", ApplicationSessionId = "as1",
            ProtocolVersion = "1.0", StartedAtUtc = DateTimeOffset.UtcNow
        });

        var resolution = TargetResolver.Resolve(
            new TargetOptions(null, null, 1000), session);

        Assert.True(resolution.Success);
        Assert.Equal("lumina-ui-diagnostics-42", resolution.DiagnosticsPipeName);
    }

    [Fact]
    public void TargetSessionSnapshot_RecordsAllFields()
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = new TargetSessionSnapshot(
            42, "app", "pipe-42", "s1", "as1", "1.0", now);

        Assert.Equal(42, snapshot.ProcessId);
        Assert.Equal("app", snapshot.ProcessName);
        Assert.Equal("pipe-42", snapshot.PipeName);
        Assert.Equal("s1", snapshot.SessionId);
        Assert.Equal("as1", snapshot.ApplicationSessionId);
        Assert.Equal("1.0", snapshot.ProtocolVersion);
        Assert.Equal(now, snapshot.StartedAtUtc);
    }
}
