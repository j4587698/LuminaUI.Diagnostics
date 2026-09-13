using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using LuminaUI.Diagnostics.Localization;
using LuminaUI.Diagnostics.Tool.DevTools;
using LuminaUI.Diagnostics.Transport;
using LuminaUI.Diagnostics.UI;

namespace LuminaUI.Diagnostics.Desktop;

internal sealed class DevToolsApplication : Application
{
    private readonly CancellationTokenSource _lifetime = new();

    public static DevToolsArguments Options { get; set; } = null!;
    public static DevToolsSingleInstance SingleInstance { get; set; } = null!;

    public override void Initialize()
    {
        DiagnosticsLocalization.Register();
        RequestedThemeVariant = ThemeVariant.Default;
        Styles.Add(new FluentTheme());
        Styles.Add(new LuminaTheme());
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var transport = new PipeDiagnosticClient(Options.PipeName);
            var client = new DispatcherDiagnosticsClient(transport.SendAsync);
            var window = new LuminaDevToolsWindow(client);
            desktop.MainWindow = window;
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;

            SingleInstance.StartActivationListener(
                () => Dispatcher.UIThread.Post(() =>
                {
                    if (!window.IsVisible)
                        window.Show();
                    window.Activate();
                }),
                _lifetime.Token);
            _ = MonitorTargetProcessAsync(desktop, _lifetime.Token);
            desktop.Exit += (_, _) => _lifetime.Cancel();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static async Task MonitorTargetProcessAsync(
        IClassicDesktopStyleApplicationLifetime desktop,
        CancellationToken cancellationToken)
    {
        try
        {
            using var target = Process.GetProcessById(Options.TargetProcessId);
            while (!target.HasExited)
            {
                await Task.Delay(500, cancellationToken).ConfigureAwait(false);
                target.Refresh();
            }
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
        }
        catch (OperationCanceledException)
        {
            return;
        }

        Dispatcher.UIThread.Post(() => desktop.Shutdown());
    }
}
