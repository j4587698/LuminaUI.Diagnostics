using System.Text.Json.Nodes;
using Avalonia.Controls;
using LuminaUI.Diagnostics.Controls;
using LuminaUI.Diagnostics.Inspection;
using LuminaUI.Diagnostics.Interaction;
using LuminaUI.Diagnostics.Threading;

namespace LuminaUI.Diagnostics.Tests.Interaction;

public sealed class InteractionHandlerTests
{
    [Fact]
    public async Task SetProperty_LiteralClear_AssignsStringValue()
    {
        var textBox = new TextBox { Text = "initial" };
        using var registry = new NodeRegistry();
        var handler = CreateHandler(textBox, registry);

        var response = await handler.HandleAsync(CreateRequest(
            registry.RegisterNode(textBox),
            new JsonObject { ["value"] = "clear" }));

        Assert.True(response.Success);
        Assert.Equal("clear", textBox.Text);
        Assert.True(textBox.IsSet(TextBox.TextProperty));
    }

    [Fact]
    public async Task SetProperty_EmptyString_AssignsEmptyString()
    {
        var textBox = new TextBox { Text = "initial" };
        using var registry = new NodeRegistry();
        var handler = CreateHandler(textBox, registry);

        var response = await handler.HandleAsync(CreateRequest(
            registry.RegisterNode(textBox),
            new JsonObject { ["value"] = "" }));

        Assert.True(response.Success);
        Assert.Equal("", textBox.Text);
        Assert.True(textBox.IsSet(TextBox.TextProperty));
    }

    [Fact]
    public async Task SetProperty_ClearLocalValue_ClearsInsteadOfAssigning()
    {
        var textBox = new TextBox { Text = "initial" };
        using var registry = new NodeRegistry();
        var handler = CreateHandler(textBox, registry);

        var response = await handler.HandleAsync(CreateRequest(
            registry.RegisterNode(textBox),
            new JsonObject { ["clearLocalValue"] = true }));

        Assert.True(response.Success);
        Assert.False(textBox.IsSet(TextBox.TextProperty));
        Assert.Equal("propertyCleared", response.Data?["status"]?.GetValue<string>());
    }

    private static InteractionHandler CreateHandler(TextBox textBox, NodeRegistry registry) =>
        InteractionHandler.SetProperty(
            new ImmediateUiThreadInvoker(),
            new AvaloniaControlResolver(),
            new PropertyValueConverter(),
            registry,
            () => [textBox]);

    private static DiagnosticRequest CreateRequest(string nodeId, JsonObject extraParameters)
    {
        var parameters = new JsonObject
        {
            ["nodeId"] = nodeId,
            ["propertyName"] = "Text"
        };
        foreach (var (key, value) in extraParameters)
            parameters[key] = value?.DeepClone();

        return new DiagnosticRequest("request", LuminaUIDiagnosticsToolNames.SetProperty, parameters, 5_000);
    }

    private sealed class ImmediateUiThreadInvoker : IUiThreadInvoker
    {
        public Task<DiagnosticResponse> InvokeAsync(
            DiagnosticRequest request,
            Func<CancellationToken, Task<DiagnosticResponse>> operation,
            CancellationToken cancellationToken = default) =>
            operation(cancellationToken);
    }
}
