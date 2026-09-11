using LuminaUI.Diagnostics.Binding;
using LuminaUI.Diagnostics.Controls;
using LuminaUI.Diagnostics.Data;
using LuminaUI.Diagnostics.Dispatch;
using LuminaUI.Diagnostics.Inspection;
using LuminaUI.Diagnostics.Interaction;
using LuminaUI.Diagnostics.Scroll;
using LuminaUI.Diagnostics.Serialization;
using LuminaUI.Diagnostics.Threading;
using LuminaUI.Diagnostics.UI;
using LuminaUI.Diagnostics.Visual;

namespace LuminaUI.Diagnostics;

public static class DiagnosticsServiceFactory
{
    public static DiagnosticsServices CreateDefault()
    {
        var invoker = new AvaloniaUiThreadInvoker();
        var resolver = new AvaloniaControlResolver();
        var formatter = new ValueFormatter();
        var propertyValueConverter = new PropertyValueConverter();
        var scrollStateSerializer = new ScrollStateSerializer();
        var bindingErrorStore = new BindingErrorStore();
        var bindingErrorSink = BindingErrorLogSink.Install(bindingErrorStore);
        var nodeRegistry = new NodeRegistry();
        var treeSerializer = new VisualNodeSerializer(formatter, nodeRegistry);
        var resourceEntryRegistry = new ResourceEntryRegistry();
        var remoteElementPicker = new RemoteElementPickerSession(nodeRegistry);

        var registry = new DiagnosticHandlerRegistry()
            .Add(new DiagnosticsPingHandler())
            .Add(new RuntimeMetricsHandler())
            .Add(new WindowInspectionHandler(invoker, nodeRegistry))
            .Add(TreeInspectionHandler.VisualTree(invoker, resolver, treeSerializer))
            .Add(TreeInspectionHandler.LogicalTree(invoker, resolver, treeSerializer))
            .Add(new ControlSearchHandler(invoker, nodeRegistry))
            .Add(new FocusedElementInspectionHandler(invoker, nodeRegistry))
            .Add(RemoteElementPickerHandler.Highlight(invoker, remoteElementPicker))
            .Add(RemoteElementPickerHandler.SetEnabled(invoker, remoteElementPicker))
            .Add(RemoteElementPickerHandler.GetState(invoker, remoteElementPicker))
            .Add(new PropertyInspectionHandler(invoker, resolver, formatter, nodeRegistry))
            .Add(new DataContextInspectionHandler(invoker, resolver, formatter, nodeRegistry))
            .Add(new BindingErrorInspectionHandler(bindingErrorStore))
            .Add(new BindingExpressionInspectionHandler(invoker, resolver, nodeRegistry))
            .Add(new StyleInspectionHandler(invoker, resolver, nodeRegistry))
            .Add(new ResourceInspectionHandler(invoker, resolver, formatter, nodeRegistry, resourceRegistry: resourceEntryRegistry))
            .Add(new SetResourceInspectionHandler(invoker, resourceEntryRegistry, propertyValueConverter))
            .Add(new AssetInspectionHandler())
            .Add(InteractionHandler.ClickControl(invoker, resolver, nodeRegistry))
            .Add(InteractionHandler.SetProperty(invoker, resolver, propertyValueConverter, nodeRegistry))
            .Add(InteractionHandler.InputText(invoker, resolver, nodeRegistry))
            .Add(InteractionHandler.SendKeys(invoker, resolver, nodeRegistry))
            .Add(InteractionHandler.InvokeCommand(invoker, resolver, nodeRegistry))
            .Add(InteractionHandler.WaitForProperty(invoker, resolver, nodeRegistry))
            .Add(new ScreenshotHandler(invoker, resolver, nodeRegistry))
            .Add(ScrollDiagnosticsHandler.Scroll(invoker, resolver, scrollStateSerializer, nodeRegistry))
            .Add(ScrollDiagnosticsHandler.ScrollableItems(invoker, resolver, scrollStateSerializer, nodeRegistry));

        return new DiagnosticsServices(
            registry.Handlers,
            registry.CreateDispatcher(),
            [bindingErrorSink, remoteElementPicker],
            nodeRegistry);
    }
}

public sealed class DiagnosticsServices : IDisposable
{
    private readonly IReadOnlyList<IDisposable> _ownedDisposables;
    private bool _disposed;

    public DiagnosticsServices(
        IReadOnlyList<IDiagnosticToolHandler> handlers,
        DiagnosticDispatcher dispatcher,
        IReadOnlyList<IDisposable>? ownedDisposables = null,
        NodeRegistry? nodeRegistry = null)
    {
        Handlers = handlers ?? throw new ArgumentNullException(nameof(handlers));
        Dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        NodeRegistry = nodeRegistry ?? new NodeRegistry();
        _ownedDisposables = ownedDisposables ?? [];
    }

    public IReadOnlyList<IDiagnosticToolHandler> Handlers { get; }

    public DiagnosticDispatcher Dispatcher { get; }

    public NodeRegistry NodeRegistry { get; }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        NodeRegistry.Dispose();
        for (var index = _ownedDisposables.Count - 1; index >= 0; index--)
            _ownedDisposables[index].Dispose();
    }
}
