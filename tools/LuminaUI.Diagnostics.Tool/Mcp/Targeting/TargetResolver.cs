using LuminaUI.Diagnostics.Abstractions;

namespace LuminaUI.Diagnostics.Mcp.Targeting;

public static class TargetResolver
{
    public static TargetResolution Resolve(
        TargetOptions options,
        TargetSession? session = null)
    {
        var timeoutMs = options.TimeoutMs > 0
            ? options.TimeoutMs
            : LuminaUIDiagnosticsProtocol.DefaultTimeoutMs;

        if (!string.IsNullOrWhiteSpace(options.DiagnosticsPipeName))
            return TargetResolution.ForPipe(options.DiagnosticsPipeName.Trim(), timeoutMs);

        if (options.ProcessId.HasValue)
        {
            if (options.ProcessId.Value <= 0)
            {
                return TargetResolution.Fail(
                    DiagnosticErrorCode.InvalidRequest,
                    "Process ID must be a positive integer.");
            }

            return TargetResolution.ForPipe(DiagnosticsPipeName.ForProcess(options.ProcessId.Value), timeoutMs);
        }

        if (session?.GetCurrent() is { } current)
            return TargetResolution.ForPipe(current.PipeName, timeoutMs);

        return TargetResolution.Fail(
            DiagnosticErrorCode.InvalidRequest,
            "No LuminaUI.Diagnostics target was selected. Call connect_app, or provide a pid or pipe parameter.");
    }
}

