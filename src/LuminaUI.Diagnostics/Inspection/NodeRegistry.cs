using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using LuminaUI.Diagnostics.Abstractions;
using LuminaUI.Diagnostics.Controls;

namespace LuminaUI.Diagnostics.Inspection;

public sealed class NodeRegistry : IDisposable
{
    private readonly ConcurrentDictionary<string, WeakReference<Control>> _nodes = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, WeakReference<Window>> _windows = new(StringComparer.Ordinal);
    private readonly ConditionalWeakTable<Control, string> _nodeMap = new();
    private readonly ConditionalWeakTable<Window, string> _windowMap = new();
    private readonly object _gate = new();
    private string _applicationSessionId = "";
    private bool _initialized;
    private bool _disposed;

    public string ApplicationSessionId
    {
        get
        {
            EnsureInitialized();
            return _applicationSessionId;
        }
    }

    public string RegisterWindow(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        if (_windowMap.TryGetValue(window, out var existing))
            return existing;

        var windowId = $"w{Guid.NewGuid():N}";
        _windows[windowId] = new WeakReference<Window>(window);
        _windowMap.Add(window, windowId);
        return windowId;
    }

    public string RegisterNode(Control control)
    {
        ArgumentNullException.ThrowIfNull(control);

        if (_nodeMap.TryGetValue(control, out var existing))
            return existing;

        var nodeId = $"n{Guid.NewGuid():N}";
        _nodes[nodeId] = new WeakReference<Control>(control);
        _nodeMap.Add(control, nodeId);
        return nodeId;
    }

    public bool TryResolveWindow(string windowId, out Window? window)
    {
        window = null;
        if (string.IsNullOrWhiteSpace(windowId))
            return false;

        if (!_windows.TryGetValue(windowId, out var weakRef))
            return false;

        if (weakRef.TryGetTarget(out var target) && IsWindowAlive(target))
        {
            window = target;
            return true;
        }

        _windows.TryRemove(windowId, out _);
        return false;
    }

    public bool TryResolveNode(string nodeId, out Control? control)
    {
        control = null;
        if (string.IsNullOrWhiteSpace(nodeId))
            return false;

        if (!_nodes.TryGetValue(nodeId, out var weakRef))
            return false;

        if (weakRef.TryGetTarget(out var target) && IsControlAlive(target))
        {
            control = target;
            return true;
        }

        _nodes.TryRemove(nodeId, out _);
        return false;
    }

    public NodeResolution ResolveFromRequest(
        DiagnosticRequest request,
        IReadOnlyList<Control> roots,
        IControlResolver resolver)
    {
        var nodeId = InspectionRequestHelpers.GetString(request.Parameters, "nodeId");
        if (!string.IsNullOrWhiteSpace(nodeId))
        {
            if (TryResolveNode(nodeId, out var nodeControl))
            {
                for (var i = 0; i < roots.Count; i++)
                {
                    if (IsDescendantOf(nodeControl!, roots[i]))
                        return NodeResolution.Found(nodeControl!, i, nodeId);
                }
            }

            return NodeResolution.Failed(
                DiagnosticResponse.Fail(request.Id, DiagnosticErrorCode.TargetNotFound,
                    $"Node '{nodeId}' is stale or was destroyed. Re-resolve the control.",
                    new JsonObject { ["nodeId"] = nodeId }));
        }

        var windowId = InspectionRequestHelpers.GetString(request.Parameters, "windowId");
        if (!string.IsNullOrWhiteSpace(windowId))
        {
            if (TryResolveWindow(windowId, out var window))
            {
                for (var i = 0; i < roots.Count; i++)
                {
                    if (ReferenceEquals(roots[i], window))
                        return NodeResolution.Found(window, i, windowId);
                }
            }

            return NodeResolution.Failed(
                DiagnosticResponse.Fail(request.Id, DiagnosticErrorCode.TargetNotFound,
                    $"Window '{windowId}' is stale or was closed. Re-list windows.",
                    new JsonObject { ["windowId"] = windowId }));
        }

        return ResolveLegacy(request, roots, resolver);
    }

    public void EnsureInitialized()
    {
        if (_initialized) return;
        lock (_gate)
        {
            if (_initialized) return;
            _applicationSessionId = $"s{Guid.NewGuid():N}";
            _initialized = true;
        }
    }

    public void CleanupStaleEntries()
    {
        foreach (var (key, weakRef) in _nodes)
        {
            if (!weakRef.TryGetTarget(out var target) || !IsControlAlive(target))
                _nodes.TryRemove(key, out _);
        }

        foreach (var (key, weakRef) in _windows)
        {
            if (!weakRef.TryGetTarget(out var target) || !IsWindowAlive(target))
                _windows.TryRemove(key, out _);
        }
    }

    public void SetApplicationSessionId(string sessionId)
    {
        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            lock (_gate)
            {
                _applicationSessionId = sessionId;
                _initialized = true;
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _nodes.Clear();
        _windows.Clear();
    }

    private static bool IsControlAlive(Control control)
    {
        try
        {
            _ = control.Parent;
            _ = control.IsVisible;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsWindowAlive(Window? window)
    {
        if (window is null) return false;
        try { return window.IsVisible; }
        catch { return false; }
    }

    private static bool IsDescendantOf(Control control, Control root)
    {
        var current = control.Parent;
        while (current is not null)
        {
            if (ReferenceEquals(current, root)) return true;
            current = current.Parent;
        }
        return ReferenceEquals(control, root);
    }

    private NodeResolution ResolveLegacy(
        DiagnosticRequest request,
        IReadOnlyList<Control> roots,
        IControlResolver resolver)
    {
        var controlId = InspectionRequestHelpers.GetString(request.Parameters, "controlId");
        if (string.IsNullOrWhiteSpace(controlId))
        {
            return NodeResolution.Failed(
                DiagnosticResponse.Fail(request.Id, DiagnosticErrorCode.InvalidRequest,
                    "Parameter 'controlId' or 'nodeId' is required."));
        }

        // Accept node identifiers issued by find_control/get_visual_tree/get_logical_tree
        // in the 'controlId' parameter as well.
        if (TryResolveNode(controlId, out var nodeControl))
        {
            for (var i = 0; i < roots.Count; i++)
            {
                if (IsDescendantOf(nodeControl!, roots[i]))
                    return NodeResolution.Found(nodeControl!, i, controlId);
            }

            return NodeResolution.Failed(
                DiagnosticResponse.Fail(request.Id, DiagnosticErrorCode.TargetNotFound,
                    $"Node '{controlId}' is stale or was destroyed. Re-resolve the control.",
                    new JsonObject { ["controlId"] = controlId }));
        }

        if (!ControlIdentifierParser.TryParse(controlId, out var identifier, out var error))
        {
            return NodeResolution.Failed(
                DiagnosticResponse.Fail(request.Id, DiagnosticErrorCode.InvalidRequest, error!,
                    new JsonObject { ["controlId"] = controlId }));
        }

        for (var rootIndex = 0; rootIndex < roots.Count; rootIndex++)
        {
            var resolution = resolver.Resolve(roots[rootIndex], identifier);
            if (resolution.Found)
                return NodeResolution.Found(resolution.Control!, rootIndex, controlId);
        }

        return NodeResolution.Failed(
            DiagnosticResponse.Fail(request.Id, DiagnosticErrorCode.TargetNotFound,
                $"Control '{controlId}' was not found.",
                new JsonObject { ["controlId"] = controlId }));
    }
}

public sealed record NodeResolution(
    bool Success,
    Control? Control,
    int RootIndex,
    string? NodeId,
    DiagnosticResponse? Response)
{
    public static NodeResolution Found(Control control, int rootIndex, string nodeId) =>
        new(true, control, rootIndex, nodeId, null);

    public static NodeResolution Failed(DiagnosticResponse response) =>
        new(false, null, -1, null, response);
}
