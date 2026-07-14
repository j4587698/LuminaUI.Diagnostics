using System.Text.Json.Nodes;
using LuminaUI.Diagnostics.Abstractions;

namespace LuminaUI.Diagnostics.Tests.Protocol;

public sealed class ProtocolSerializationTests
{
    [Fact]
    public void DiagnosticRequest_Roundtrips_ThroughJson()
    {
        var original = new DiagnosticRequest(
            "test-id-123",
            "ping",
            new JsonObject { ["key"] = "value" },
            5000);

        var json = DiagnosticJson.SerializeRequest(original);
        var deserialized = DiagnosticJson.DeserializeRequest(json);

        Assert.NotNull(deserialized);
        Assert.Equal(original.Id, deserialized.Id);
        Assert.Equal(original.Method, deserialized.Method);
        Assert.Equal(original.TimeoutMs, deserialized.TimeoutMs);
        Assert.Equal("value", deserialized.Parameters["key"]?.GetValue<string>());
    }

    [Fact]
    public void DiagnosticResponse_Success_Roundtrips_ThroughJson()
    {
        var original = DiagnosticResponse.Ok(
            "resp-1",
            new JsonObject { ["result"] = "ok" });

        var json = DiagnosticJson.SerializeResponse(original);
        var deserialized = DiagnosticJson.DeserializeResponse(json);

        Assert.NotNull(deserialized);
        Assert.True(deserialized.Success);
        Assert.Equal(original.Id, deserialized.Id);
        Assert.Null(deserialized.Error);
        Assert.Equal("ok", deserialized.Data?["result"]?.GetValue<string>());
    }

    [Fact]
    public void DiagnosticResponse_Failure_Roundtrips_ThroughJson()
    {
        var original = DiagnosticResponse.Fail(
            "resp-2",
            DiagnosticErrorCode.TargetNotFound,
            "Control was not found.",
            new JsonObject { ["controlId"] = "Button[0]" });

        var json = DiagnosticJson.SerializeResponse(original);
        var deserialized = DiagnosticJson.DeserializeResponse(json);

        Assert.NotNull(deserialized);
        Assert.False(deserialized.Success);
        Assert.Equal(original.Id, deserialized.Id);
        Assert.NotNull(deserialized.Error);
        Assert.Equal(DiagnosticErrorCode.TargetNotFound, deserialized.Error.Code);
        Assert.Equal("Control was not found.", deserialized.Error.Message);
    }

    [Fact]
    public void TryDeserializeRequest_InvalidJson_ReturnsFalse()
    {
        var success = DiagnosticJson.TryDeserializeRequest(
            "not json at all",
            out var request,
            out var error);

        Assert.False(success);
        Assert.Null(request);
        Assert.NotNull(error);
        Assert.Equal(DiagnosticErrorCode.InvalidRequest, error.Code);
    }

    [Fact]
    public void DiagnosticAppDescriptor_Roundtrips_ThroughJson()
    {
        var original = new DiagnosticAppDescriptor
        {
            ProcessId = 12345,
            ProcessName = "test-app",
            PipeName = "lumina-ui-diagnostics-12345",
            SessionId = "session-1",
            ApplicationSessionId = "app-session-1",
            ProtocolName = LuminaUIDiagnosticsProtocol.Name,
            ProtocolVersion = "1.0.0",
            StartedAtUtc = DateTimeOffset.UtcNow
        };

        var json = DiagnosticJson.SerializeAppDescriptor(original);
        var deserialized = DiagnosticJson.DeserializeAppDescriptor(json);

        Assert.NotNull(deserialized);
        Assert.Equal(original.ProcessId, deserialized.ProcessId);
        Assert.Equal(original.ProcessName, deserialized.ProcessName);
        Assert.Equal(original.PipeName, deserialized.PipeName);
        Assert.Equal(original.SessionId, deserialized.SessionId);
        Assert.Equal(original.ApplicationSessionId, deserialized.ApplicationSessionId);
    }

    [Fact]
    public void DiagnosticsPipeName_ForProcess_GeneratesCorrectName()
    {
        var name = DiagnosticsPipeName.ForProcess(42);
        Assert.Equal("lumina-ui-diagnostics-42", name);
    }

    [Fact]
    public void DiagnosticsPipeName_ForZeroProcess_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DiagnosticsPipeName.ForProcess(0));
    }

    [Fact]
    public void DiagnosticRequest_DefaultConstructor_HasValidId()
    {
        var request = new DiagnosticRequest();
        Assert.False(string.IsNullOrWhiteSpace(request.Id));
        Assert.Empty(request.Method);
        Assert.NotNull(request.Parameters);
    }
}
