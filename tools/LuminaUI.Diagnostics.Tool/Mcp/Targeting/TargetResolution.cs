using LuminaUI.Diagnostics.Abstractions;

namespace LuminaUI.Diagnostics.Mcp.Targeting;

public sealed record TargetResolution(
    bool Success,
    string? DiagnosticsPipeName,
    int TimeoutMs,
    DiagnosticError? Error)
{
    public static TargetResolution ForPipe(string pipeName, int timeoutMs) =>
        new(true, pipeName, timeoutMs, null);

    public static TargetResolution Fail(DiagnosticErrorCode code, string message) =>
        new(false, null, LuminaUIDiagnosticsProtocol.DefaultTimeoutMs, new DiagnosticError(code, message));
}

