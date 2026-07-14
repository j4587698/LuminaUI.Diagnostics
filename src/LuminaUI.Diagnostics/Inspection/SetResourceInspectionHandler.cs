using System.Text.Json.Nodes;
using LuminaUI.Diagnostics.Abstractions;
using LuminaUI.Diagnostics.Dispatch;
using LuminaUI.Diagnostics.Interaction;
using LuminaUI.Diagnostics.Threading;

namespace LuminaUI.Diagnostics.Inspection;

public sealed class SetResourceInspectionHandler : IDiagnosticToolHandler
{
    private readonly IUiThreadInvoker _invoker;
    private readonly ResourceEntryRegistry _registry;
    private readonly PropertyValueConverter _converter;

    public SetResourceInspectionHandler(
        IUiThreadInvoker invoker,
        ResourceEntryRegistry registry,
        PropertyValueConverter converter)
    {
        _invoker = invoker;
        _registry = registry;
        _converter = converter;
    }

    public string Method => LuminaUIDiagnosticsToolNames.SetResource;

    public Task<DiagnosticResponse> HandleAsync(
        DiagnosticRequest request,
        CancellationToken cancellationToken = default) =>
        _invoker.InvokeAsync(request, _ => Task.FromResult(HandleOnUiThread(request)), cancellationToken);

    private DiagnosticResponse HandleOnUiThread(DiagnosticRequest request)
    {
        var entryId = InspectionRequestHelpers.GetString(request.Parameters, "entryId");
        var valueText = InspectionRequestHelpers.GetString(request.Parameters, "value") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(entryId) || !_registry.TryGet(entryId, out var entry))
        {
            return DiagnosticResponse.Fail(
                request.Id,
                DiagnosticErrorCode.TargetNotFound,
                "The resource entry is no longer available. Refresh resources and try again.");
        }

        var currentValue = entry.Dictionary[entry.Key];
        var targetType = currentValue?.GetType() ?? typeof(string);
        if (!_converter.TryConvert(valueText, targetType, out var converted, out var error))
        {
            return DiagnosticResponse.Fail(
                request.Id,
                DiagnosticErrorCode.InvalidRequest,
                error ?? $"Unable to convert the value to {targetType.Name}.");
        }

        entry.Dictionary[entry.Key] = converted;
        return DiagnosticResponse.Ok(
            request.Id,
            new JsonObject { ["entryId"] = entryId, ["value"] = converted?.ToString() });
    }
}
