using System.Text;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Input;
using LuminaUI.Diagnostics.Controls;
using LuminaUI.Diagnostics.Inspection;
using LuminaUI.Diagnostics.Interaction;
using LuminaUI.Diagnostics.Threading;

namespace LuminaUI.Diagnostics.Tests.Interaction;

public sealed class InteractionHandlerTests
{
    [Fact]
    public async Task SendKeys_CustomControl_ReceivesTextInput()
    {
        var target = new TextCaptureControl();
        using var registry = new NodeRegistry();
        var handler = InteractionHandler.SendKeys(
            new ImmediateUiThreadInvoker(),
            new AvaloniaControlResolver(),
            registry,
            () => [target]);

        var response = await handler.HandleAsync(new DiagnosticRequest(
            "request",
            LuminaUIDiagnosticsToolNames.SendKeys,
            new JsonObject
            {
                ["nodeId"] = registry.RegisterNode(target),
                ["text"] = "echo hi"
            },
            5_000), TestContext.Current.CancellationToken);

        Assert.True(response.Success, response.Error?.Message);
        Assert.Equal("echo hi", target.ReceivedText);
        Assert.Equal("keysSent", response.Data?["status"]?.GetValue<string>());
        Assert.Equal(7, response.Data?["characters"]?.GetValue<int>());
    }

    [Fact]
    public async Task SendKeys_MissingText_Fails()
    {
        var target = new TextCaptureControl();
        using var registry = new NodeRegistry();
        var handler = InteractionHandler.SendKeys(
            new ImmediateUiThreadInvoker(),
            new AvaloniaControlResolver(),
            registry,
            () => [target]);

        var response = await handler.HandleAsync(new DiagnosticRequest(
            "request",
            LuminaUIDiagnosticsToolNames.SendKeys,
            new JsonObject { ["nodeId"] = registry.RegisterNode(target) },
            5_000), TestContext.Current.CancellationToken);

        Assert.False(response.Success);
        Assert.Equal(DiagnosticErrorCode.InvalidRequest, response.Error?.Code);
    }

    [Fact]
    public async Task InputText_NonTextBoxControl_FallsBackToKeyInjection()
    {
        var target = new TextCaptureControl();
        using var registry = new NodeRegistry();
        var handler = InteractionHandler.InputText(
            new ImmediateUiThreadInvoker(),
            new AvaloniaControlResolver(),
            registry,
            () => [target]);

        var response = await handler.HandleAsync(new DiagnosticRequest(
            "request",
            LuminaUIDiagnosticsToolNames.InputText,
            new JsonObject
            {
                ["nodeId"] = registry.RegisterNode(target),
                ["text"] = "echo hi",
                ["pressEnter"] = true
            },
            5_000), TestContext.Current.CancellationToken);

        Assert.True(response.Success, response.Error?.Message);
        Assert.Equal("echo hi", target.ReceivedText);
        Assert.True(target.EnterReceived);
        Assert.Equal("keysSent", response.Data?["status"]?.GetValue<string>());
        Assert.True(response.Data?["enterPressed"]?.GetValue<bool>());
    }

    [Fact]
    public async Task InputText_TextBox_StillSetsTextDirectly()
    {
        var textBox = new TextBox { Text = "initial" };
        using var registry = new NodeRegistry();
        var handler = InteractionHandler.InputText(
            new ImmediateUiThreadInvoker(),
            new AvaloniaControlResolver(),
            registry,
            () => [textBox]);

        var response = await handler.HandleAsync(new DiagnosticRequest(
            "request",
            LuminaUIDiagnosticsToolNames.InputText,
            new JsonObject
            {
                ["nodeId"] = registry.RegisterNode(textBox),
                ["text"] = "replaced"
            },
            5_000), TestContext.Current.CancellationToken);

        Assert.True(response.Success, response.Error?.Message);
        Assert.Equal("replaced", textBox.Text);
        Assert.Equal("textSet", response.Data?["status"]?.GetValue<string>());
    }

    [Fact]
    public async Task Click_ButtonWithoutVisualRoot_FallsBackToRaisingClick()
    {
        var button = new Button();
        var clicked = false;
        button.Click += (_, _) => clicked = true;
        using var registry = new NodeRegistry();
        var handler = InteractionHandler.ClickControl(
            new ImmediateUiThreadInvoker(),
            new AvaloniaControlResolver(),
            registry,
            () => [button]);

        var response = await handler.HandleAsync(new DiagnosticRequest(
            "request",
            LuminaUIDiagnosticsToolNames.ClickControl,
            new JsonObject { ["nodeId"] = registry.RegisterNode(button) },
            5_000), TestContext.Current.CancellationToken);

        Assert.True(response.Success, response.Error?.Message);
        Assert.True(clicked);
        Assert.Equal("clickRaised", response.Data?["status"]?.GetValue<string>());
    }

    [Fact]
    public async Task Click_ControlIdCarryingNodeId_Resolves()
    {
        var button = new Button();
        var clicked = false;
        button.Click += (_, _) => clicked = true;
        using var registry = new NodeRegistry();
        var handler = InteractionHandler.ClickControl(
            new ImmediateUiThreadInvoker(),
            new AvaloniaControlResolver(),
            registry,
            () => [button]);

        var response = await handler.HandleAsync(new DiagnosticRequest(
            "request",
            LuminaUIDiagnosticsToolNames.ClickControl,
            new JsonObject { ["controlId"] = registry.RegisterNode(button) },
            5_000), TestContext.Current.CancellationToken);

        Assert.True(response.Success, response.Error?.Message);
        Assert.True(clicked);
    }

    private sealed class TextCaptureControl : Control
    {
        private readonly StringBuilder _received = new();

        public string ReceivedText => _received.ToString();

        public bool EnterReceived { get; private set; }

        protected override void OnTextInput(TextInputEventArgs e)
        {
            base.OnTextInput(e);
            _received.Append(e.Text);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.Key == Key.Return)
                EnterReceived = true;
        }
    }

    [Fact]
    public async Task SetProperty_LiteralClear_AssignsStringValue()
    {
        var textBox = new TextBox { Text = "initial" };
        using var registry = new NodeRegistry();
        var handler = CreateHandler(textBox, registry);

        var response = await handler.HandleAsync(CreateRequest(
            registry.RegisterNode(textBox),
            new JsonObject { ["value"] = "clear" }), TestContext.Current.CancellationToken);

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
            new JsonObject { ["value"] = "" }), TestContext.Current.CancellationToken);

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
            new JsonObject { ["clearLocalValue"] = true }), TestContext.Current.CancellationToken);

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
