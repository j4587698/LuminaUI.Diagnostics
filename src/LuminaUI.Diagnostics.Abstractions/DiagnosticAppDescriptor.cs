using System.Text.Json.Serialization;

namespace LuminaUI.Diagnostics.Abstractions;

public sealed record DiagnosticAppDescriptor
{
    [JsonPropertyName("processId")]
    public int ProcessId { get; init; }

    [JsonPropertyName("processName")]
    public string ProcessName { get; init; } = "";

    [JsonPropertyName("pipeName")]
    public string PipeName { get; init; } = "";

    [JsonPropertyName("sessionId")]
    public string SessionId { get; init; } = "";

    [JsonPropertyName("applicationSessionId")]
    public string ApplicationSessionId { get; init; } = "";

    [JsonPropertyName("protocolName")]
    public string ProtocolName { get; init; } = LuminaUIDiagnosticsProtocol.Name;

    [JsonPropertyName("protocolVersion")]
    public string ProtocolVersion { get; init; } = LuminaUIDiagnosticsProtocol.Version;

    [JsonPropertyName("startedAtUtc")]
    public DateTimeOffset StartedAtUtc { get; init; }
}

public static class DiagnosticsDiscoveryPath
{
    public static string DirectoryPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Lumina",
        "Diagnostics",
        "apps");

    public static string ForProcess(int processId)
    {
        if (processId <= 0)
            throw new ArgumentOutOfRangeException(nameof(processId));

        return Path.Combine(DirectoryPath, $"{processId}.json");
    }
}
