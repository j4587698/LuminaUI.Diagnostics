using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using LuminaUI.Diagnostics.Binding;
using LuminaUI.Diagnostics.Controls;
using LuminaUI.Diagnostics.Dispatch;
using LuminaUI.Diagnostics.Inspection;
using LuminaUI.Diagnostics.Serialization;
using LuminaUI.Diagnostics.Threading;
using LuminaUI.Diagnostics.UI;
using LuminaUI.Diagnostics.UI.ViewModels;

namespace LuminaUI.Diagnostics.Tests.Interaction;

public sealed class DispatcherDiagnosticsClientTests
{
    [Fact]
    public async Task GetProperties_RestoresEditorValueTypes()
    {
        var button = new Button
        {
            IsEnabled = false,
            Width = 42,
            Margin = new Thickness(1, 2, 3, 4),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        using var registry = new NodeRegistry();
        using var client = CreateClient(button, registry);

        var properties = await client.GetPropertiesAsync(new VisualTreeNodeViewModel(button), TestContext.Current.CancellationToken);

        var isEnabled = Assert.Single(properties, property => property.Name == "IsEnabled");
        Assert.True(isEnabled.IsBoolean);
        Assert.IsType<bool>(isEnabled.Value);

        var width = Assert.Single(properties, property => property.Name == "Width");
        Assert.True(width.IsNumeric);
        Assert.IsType<double>(width.Value);

        var margin = Assert.Single(properties, property => property.Name == "Margin");
        Assert.Equal("Thickness", margin.PropertyType);
        Assert.IsType<Thickness>(margin.Value);

        var alignment = Assert.Single(properties, property => property.Name == "HorizontalAlignment");
        Assert.True(alignment.IsEnum);
        Assert.Equal(HorizontalAlignment.Center, alignment.Value);
    }

    [Fact]
    public void RegisteredPropertyCatalog_DeduplicatesByProtocolPropertyName()
    {
        var properties = RegisteredPropertyCatalog.GetUnique(
            [Button.WidthProperty, Button.WidthProperty, Button.HeightProperty]);

        Assert.Equal(["Height", "Width"], properties.Select(property => property.Name));
    }

    [Fact]
    public async Task GetResources_FormatsNumericAndBooleanValues()
    {
        var button = new Button();
        button.Resources["count"] = 42;
        button.Resources["enabled"] = true;
        using var registry = new NodeRegistry();
        using var client = CreateClient(button, registry);

        var resources = await client.GetResourcesAsync(node: new VisualTreeNodeViewModel(button), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("42", Assert.Single(resources, resource => resource.Key == "count").Value);
        Assert.Equal("True", Assert.Single(resources, resource => resource.Key == "enabled").Value);
    }

    [Fact]
    public async Task GetBindingErrors_UsesDispatcherStore()
    {
        var button = new Button();
        using var registry = new NodeRegistry();
        var store = new BindingErrorStore();
        store.Add(new BindingErrorEntry(DateTimeOffset.UtcNow, "Error", "Binding", "Button", "Failed binding"));
        using var client = CreateClient(button, registry, store);

        var errors = await client.GetBindingErrorsAsync(cancellationToken: TestContext.Current.CancellationToken);

        var error = Assert.Single(errors);
        Assert.Equal("Failed binding", error.Message);
    }

    [Fact]
    public async Task PropertySetter_ReportsDispatcherFailureOnViewModel()
    {
        var button = new Button();
        using var registry = new NodeRegistry();
        var invoker = new ImmediateUiThreadInvoker();
        var roots = new Func<IReadOnlyList<Control>>(() => [button]);
        var handlers = new IDiagnosticToolHandler[]
        {
            new PropertyInspectionHandler(invoker, new AvaloniaControlResolver(), new ValueFormatter(), registry, roots),
            new FailingSetPropertyHandler()
        };
        using var client = new DispatcherDiagnosticsClient(new DiagnosticDispatcher(handlers), registry);
        var properties = await client.GetPropertiesAsync(new VisualTreeNodeViewModel(button), TestContext.Current.CancellationToken);
        var width = Assert.Single(properties, property => property.Name == "Width");

        width.UpdateValue(123d);
        await WaitUntilAsync(() => width.Error is not null);

        Assert.Equal("Rejected by test handler.", width.Error);
    }

    private static DispatcherDiagnosticsClient CreateClient(
        Button root,
        NodeRegistry registry,
        BindingErrorStore? bindingErrorStore = null)
    {
        var invoker = new ImmediateUiThreadInvoker();
        var roots = new Func<IReadOnlyList<Control>>(() => [root]);
        var handlers = new IDiagnosticToolHandler[]
        {
            new PropertyInspectionHandler(invoker, new AvaloniaControlResolver(), new ValueFormatter(), registry, roots),
            new ResourceInspectionHandler(invoker, new AvaloniaControlResolver(), new ValueFormatter(), registry, roots),
            new BindingErrorInspectionHandler(bindingErrorStore ?? new BindingErrorStore())
        };
        return new DispatcherDiagnosticsClient(new DiagnosticDispatcher(handlers), registry);
    }

    private sealed class ImmediateUiThreadInvoker : IUiThreadInvoker
    {
        public Task<DiagnosticResponse> InvokeAsync(
            DiagnosticRequest request,
            Func<CancellationToken, Task<DiagnosticResponse>> operation,
            CancellationToken cancellationToken = default) =>
            operation(cancellationToken);
    }

    private sealed class FailingSetPropertyHandler : IDiagnosticToolHandler
    {
        public string Method => LuminaUIDiagnosticsToolNames.SetProperty;

        public Task<DiagnosticResponse> HandleAsync(
            DiagnosticRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(DiagnosticResponse.Fail(
                request.Id,
                DiagnosticErrorCode.UnsupportedOperation,
                "Rejected by test handler."));
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (!condition() && DateTime.UtcNow < deadline)
            await Task.Delay(10);
        Assert.True(condition());
    }
}
