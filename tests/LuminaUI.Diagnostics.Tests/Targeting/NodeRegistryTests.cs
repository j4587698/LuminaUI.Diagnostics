using Avalonia;
using Avalonia.Controls;
using LuminaUI.Diagnostics.Inspection;
using System.Runtime.CompilerServices;

namespace LuminaUI.Diagnostics.Tests.Targeting;

public sealed class NodeRegistryTests
{
    [Fact]
    public void NodeRegistry_RegisterNode_ReturnsValidId()
    {
        var registry = new NodeRegistry();
        var control = new Button();

        var nodeId = registry.RegisterNode(control);

        Assert.StartsWith("n", nodeId);
        Assert.True(registry.TryResolveNode(nodeId, out var resolved));
        Assert.Same(control, resolved);
    }

    [Fact]
    public void NodeRegistry_RegisterWindow_ReturnsStableId()
    {
        var registry = new NodeRegistry();
        var window = (Window)RuntimeHelpers.GetUninitializedObject(typeof(Window));

        var id1 = registry.RegisterWindow(window);
        var id2 = registry.RegisterWindow(window);

        Assert.StartsWith("w", id1);
        Assert.Equal(id1, id2);
    }

    [Fact]
    public void NodeRegistry_StaleNode_ReturnsFalse()
    {
        var registry = new NodeRegistry();
        var nodeId = registry.RegisterNode(new Button());

        // Force GC to collect the WeakReference target
        GC.Collect();
        GC.WaitForPendingFinalizers();

        // The WeakReference may still hold a reference since the Button
        // is rooted in the RegisterNode call's local variable scope.
        // Let's test with a truly unrooted scenario:
        var staleId = RegisterAndForget(registry);

        GC.Collect();
        GC.WaitForPendingFinalizers();

        Assert.False(registry.TryResolveNode(staleId, out Control? unusedNode));
    }

    [Fact]
    public void NodeRegistry_ApplicationSessionId_IsConsistent()
    {
        var registry = new NodeRegistry();

        var id1 = registry.ApplicationSessionId;
        var id2 = registry.ApplicationSessionId;

        Assert.Equal(id1, id2);
        Assert.StartsWith("s", id1);
    }

    [Fact]
    public void NodeRegistry_CleanupStaleEntries_RemovesDeadReferences()
    {
        var registry = new NodeRegistry();
        var _ = registry.RegisterNode(new Button());

        var staleId = RegisterAndForget(registry);
        GC.Collect();
        GC.WaitForPendingFinalizers();

        registry.CleanupStaleEntries();

        Assert.False(registry.TryResolveNode(staleId, out Control? unusedNode));
    }

    [Fact]
    public void NodeRegistry_TryResolve_InvalidId_ReturnsFalse()
    {
        var registry = new NodeRegistry();
        Assert.False(registry.TryResolveNode("nonexistent", out Control? unusedNode));
        Assert.False(registry.TryResolveWindow("nonexistent", out Window? unusedWindow));
    }

    [Fact]
    public void NodeRegistry_Dispose_ClearsNodes()
    {
        var registry = new NodeRegistry();
        var nodeId = registry.RegisterNode(new Button());

        registry.Dispose();
        Assert.False(registry.TryResolveNode(nodeId, out Control? _));
    }

    [Fact]
    public void NodeRegistry_Dedup_SameControl_ReturnsSameId()
    {
        var registry = new NodeRegistry();
        var control = new Button();

        var id1 = registry.RegisterNode(control);
        var id2 = registry.RegisterNode(control);

        Assert.Equal(id1, id2);
    }

    [Fact]
    public void NodeRegistry_Dedup_DifferentControls_DifferentIds()
    {
        var registry = new NodeRegistry();
        var control1 = new Button();
        var control2 = new Button();

        var id1 = registry.RegisterNode(control1);
        var id2 = registry.RegisterNode(control2);

        Assert.NotEqual(id1, id2);
    }

    [Fact]
    public void NodeRegistry_ResolveFromRequest_WithNodeId_FindsControl()
    {
        var registry = new NodeRegistry();
        var control = new Button { Name = "testButton" };
        var nodeId = registry.RegisterNode(control);

        var request = new DiagnosticRequest("req1", "test", new() { ["nodeId"] = nodeId }, 5000);
        var resolution = registry.ResolveFromRequest(request, [control], new Controls.AvaloniaControlResolver());

        Assert.True(resolution.Success);
        Assert.Same(control, resolution.Control);
        Assert.Equal(nodeId, resolution.NodeId);
    }

    [Fact]
    public void NodeRegistry_ResolveFromRequest_StaleNodeId_ReturnsFail()
    {
        var registry = new NodeRegistry();
        var staleId = "n" + Guid.NewGuid().ToString("N");

        var request = new DiagnosticRequest("req1", "test", new() { ["nodeId"] = staleId }, 5000);
        var resolution = registry.ResolveFromRequest(request, [], new Controls.AvaloniaControlResolver());

        Assert.False(resolution.Success);
        Assert.NotNull(resolution.Response);
        Assert.Equal(DiagnosticErrorCode.TargetNotFound, resolution.Response.Error?.Code);
    }

    [Fact]
    public void NodeRegistry_ResolveFromRequest_WithControlId_FallbackWorks()
    {
        var registry = new NodeRegistry();
        var control = new Button { Name = "fallbackBtn" };
        registry.RegisterNode(control);

        var request = new DiagnosticRequest("req1", "test", new() { ["controlId"] = "#fallbackBtn" }, 5000);
        var resolution = registry.ResolveFromRequest(request, [control], new Controls.AvaloniaControlResolver());

        Assert.True(resolution.Success);
        Assert.Same(control, resolution.Control);
    }

    [Fact]
    public void NodeRegistry_ResolveFromRequest_NoId_ReturnsFail()
    {
        var registry = new NodeRegistry();
        var request = new DiagnosticRequest("req1", "test", new System.Text.Json.Nodes.JsonObject(), 5000);
        var resolution = registry.ResolveFromRequest(request, [new Button()], new Controls.AvaloniaControlResolver());

        Assert.False(resolution.Success);
        Assert.NotNull(resolution.Response);
        Assert.Equal(DiagnosticErrorCode.InvalidRequest, resolution.Response.Error?.Code);
    }

    private static string RegisterAndForget(NodeRegistry registry)
    {
        var temp = new Button { Name = "temp" };
        return registry.RegisterNode(temp);
    }
}
