using System.Text.Json.Nodes;
using LuminaUI.Diagnostics.Dispatch;
using LuminaUI.Diagnostics.Abstractions;

namespace LuminaUI.Diagnostics;

public sealed class DiagnosticsPingHandler : IDiagnosticToolHandler
{
    public string Method => LuminaUIDiagnosticsToolNames.Ping;

    public Task<DiagnosticResponse> HandleAsync(
        DiagnosticRequest request,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(
            DiagnosticResponse.Ok(
                request.Id,
                new JsonObject
                {
                    ["name"] = LuminaUIDiagnosticsProtocol.Name,
                    ["version"] = LuminaUIDiagnosticsProtocol.Version,
                    ["processId"] = Environment.ProcessId,
                    ["timestampUtc"] = DateTimeOffset.UtcNow.ToString("O")
                }));
}
