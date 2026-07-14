using System.Text.Json.Nodes;
using Avalonia.Controls;
using LuminaUI.Diagnostics.Abstractions;
using LuminaUI.Diagnostics.Dispatch;
using LuminaUI.Diagnostics.Inspection;
using LuminaUI.Diagnostics.Threading;

namespace LuminaUI.Diagnostics.UI;

internal sealed class RemoteElementPickerSession : IDisposable
{
    private readonly NodeRegistry _nodeRegistry;
    private readonly ElementPickerService _picker;

    public RemoteElementPickerSession(NodeRegistry nodeRegistry)
    {
        _nodeRegistry = nodeRegistry;
        _picker = new ElementPickerService(nodeRegistry);
        _picker.ElementPicked += OnElementPicked;
    }

    public string? PickedNodeId { get; private set; }
    public long Generation { get; private set; }

    public void SetEnabled(bool enabled)
    {
        if (enabled)
        {
            PickedNodeId = null;
            _picker.Enable();
        }
        else
        {
            _picker.Disable();
        }
    }

    public void Highlight(string? nodeId)
    {
        Control? control = null;
        if (!string.IsNullOrWhiteSpace(nodeId))
            _nodeRegistry.TryResolveNode(nodeId, out control);
        _picker.Highlight(control);
    }

    public void Dispose()
    {
        _picker.ElementPicked -= OnElementPicked;
        _picker.Dispose();
    }

    private void OnElementPicked(object? sender, object element)
    {
        if (element is not Control control)
            return;

        PickedNodeId = _nodeRegistry.RegisterNode(control);
        Generation++;
    }
}

internal sealed class RemoteElementPickerHandler : IDiagnosticToolHandler
{
    private readonly IUiThreadInvoker _invoker;
    private readonly RemoteElementPickerSession _session;
    private readonly Operation _operation;

    private RemoteElementPickerHandler(
        IUiThreadInvoker invoker,
        RemoteElementPickerSession session,
        Operation operation)
    {
        _invoker = invoker;
        _session = session;
        _operation = operation;
    }

    public string Method => _operation switch
    {
        Operation.Highlight => LuminaUIDiagnosticsToolNames.HighlightElement,
        Operation.SetEnabled => LuminaUIDiagnosticsToolNames.SetElementPicker,
        _ => LuminaUIDiagnosticsToolNames.GetElementPickerState
    };

    public static RemoteElementPickerHandler Highlight(IUiThreadInvoker invoker, RemoteElementPickerSession session) =>
        new(invoker, session, Operation.Highlight);

    public static RemoteElementPickerHandler SetEnabled(IUiThreadInvoker invoker, RemoteElementPickerSession session) =>
        new(invoker, session, Operation.SetEnabled);

    public static RemoteElementPickerHandler GetState(IUiThreadInvoker invoker, RemoteElementPickerSession session) =>
        new(invoker, session, Operation.GetState);

    public Task<DiagnosticResponse> HandleAsync(
        DiagnosticRequest request,
        CancellationToken cancellationToken = default) =>
        _invoker.InvokeAsync(
            request,
            _ => Task.FromResult(HandleOnUiThread(request)),
            cancellationToken);

    private DiagnosticResponse HandleOnUiThread(DiagnosticRequest request)
    {
        switch (_operation)
        {
            case Operation.Highlight:
                _session.Highlight(request.Parameters?["nodeId"]?.GetValue<string>());
                break;
            case Operation.SetEnabled:
                _session.SetEnabled(request.Parameters?["enabled"]?.GetValue<bool>() == true);
                break;
        }

        return DiagnosticResponse.Ok(
            request.Id,
            new JsonObject
            {
                ["nodeId"] = _session.PickedNodeId,
                ["generation"] = _session.Generation
            });
    }

    private enum Operation
    {
        Highlight,
        SetEnabled,
        GetState
    }
}
