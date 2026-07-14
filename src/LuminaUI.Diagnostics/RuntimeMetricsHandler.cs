using System.Diagnostics;
using System.Text.Json.Nodes;
using LuminaUI.Diagnostics.Abstractions;
using LuminaUI.Diagnostics.Dispatch;

namespace LuminaUI.Diagnostics;

internal sealed class RuntimeMetricsHandler : IDiagnosticToolHandler
{
    public string Method => LuminaUIDiagnosticsToolNames.GetRuntimeMetrics;

    public Task<DiagnosticResponse> HandleAsync(
        DiagnosticRequest request,
        CancellationToken cancellationToken = default)
    {
        using var process = Process.GetCurrentProcess();
        return Task.FromResult(DiagnosticResponse.Ok(
            request.Id,
            new JsonObject
            {
                ["managedHeapBytes"] = GC.GetTotalMemory(false),
                ["workingSetBytes"] = process.WorkingSet64,
                ["threadCount"] = process.Threads.Count,
                ["cpuTimeTicks"] = process.TotalProcessorTime.Ticks,
                ["generation0Collections"] = GC.CollectionCount(0),
                ["generation1Collections"] = GC.CollectionCount(1),
                ["generation2Collections"] = GC.CollectionCount(2)
            }));
    }
}
