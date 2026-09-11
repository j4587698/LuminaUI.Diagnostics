using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Themes.Simple;
using Avalonia.Threading;
using LuminaUI.Diagnostics.Controls;
using LuminaUI.Diagnostics.Inspection;
using LuminaUI.Diagnostics.Interaction;
using LuminaUI.Diagnostics.Threading;

[assembly: AvaloniaTestApplication(typeof(LuminaUI.Diagnostics.Tests.Interaction.HeadlessAppBuilder))]

namespace LuminaUI.Diagnostics.Tests.Interaction;

public static class HeadlessAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<TestApplication>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());

    private sealed class TestApplication : Application
    {
        public override void Initialize()
        {
            Styles.Add(new SimpleTheme());
        }
    }
}

/// <summary>
/// Exercises click_control through the synthesized pointer pipeline against a
/// real (headless) Avalonia window.
/// </summary>
public sealed class PointerClickHandlerTests
{
    [AvaloniaFact]
    public async Task Click_RadioButton_TogglesIsChecked()
    {
        var radio = new RadioButton { Content = "Option A" };
        var target = ClickTarget.Show(radio);

        var response = await target.ClickAsync(radio);

        Assert.True(response.Success, response.Error?.Message);
        Assert.Equal("pointerClicked", response.Data?["status"]?.GetValue<string>());
        Assert.True(radio.IsChecked);
    }

    [AvaloniaFact]
    public async Task Click_ToggleButton_FlipsIsCheckedBothWays()
    {
        var toggle = new ToggleButton { Content = "Toggle" };
        var target = ClickTarget.Show(toggle);

        await target.ClickAsync(toggle);
        Assert.True(toggle.IsChecked);

        await target.ClickAsync(toggle);
        Assert.False(toggle.IsChecked);
    }

    [AvaloniaFact]
    public async Task Click_PlainButton_RaisesClickThroughPointerPipeline()
    {
        var button = new Button { Content = "Click me" };
        var clicked = 0;
        button.Click += (_, _) => clicked++;
        var target = ClickTarget.Show(button);

        var response = await target.ClickAsync(button);

        Assert.True(response.Success, response.Error?.Message);
        Assert.Equal("pointerClicked", response.Data?["status"]?.GetValue<string>());
        Assert.Equal(1, clicked);
    }

    private sealed class ClickTarget
    {
        // Windows are intentionally kept alive for the whole test run: the headless
        // platform hit-tests against the last rendered frame, and closing a window
        // leaves a stale scene behind for subsequently shown windows.
        private static readonly List<Window> OpenWindows = [];

        private readonly NodeRegistry _registry;
        private readonly InteractionHandler _handler;

        private ClickTarget(NodeRegistry registry, InteractionHandler handler)
        {
            _registry = registry;
            _handler = handler;
        }

        public static ClickTarget Show(Control control)
        {
            var window = new Window
            {
                Width = 200,
                Height = 100,
                Content = control
            };
            window.Show();
            OpenWindows.Add(window);
            Dispatcher.UIThread.RunJobs();
            // Hit testing reads the composition scene, which is only updated
            // after a rendered frame.
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();

            var registry = new NodeRegistry();
            var handler = InteractionHandler.ClickControl(
                new ImmediateUiThreadInvoker(),
                new AvaloniaControlResolver(),
                registry,
                () => [window]);
            return new ClickTarget(registry, handler);
        }

        public async Task<DiagnosticResponse> ClickAsync(Control control) =>
            await _handler.HandleAsync(new DiagnosticRequest(
                "request",
                LuminaUIDiagnosticsToolNames.ClickControl,
                new JsonObject { ["nodeId"] = _registry.RegisterNode(control) },
                5_000));
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
