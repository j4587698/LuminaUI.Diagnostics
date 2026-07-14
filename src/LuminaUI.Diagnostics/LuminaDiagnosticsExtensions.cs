using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;

namespace LuminaUI.Diagnostics;

public static class LuminaUIDiagnosticsExtensions
{
    private static readonly object Sync = new();
    private static LuminaUIDiagnosticsHost? _host;
    private static bool _processCleanupRegistered;
    private static bool _applicationCleanupRegistered;

    public static AppBuilder UseLuminaUIDiagnostics(
        this AppBuilder builder,
        Action<LuminaUIDiagnosticsOptions>? configure = null,
        [System.Runtime.CompilerServices.CallerFilePath] string? callerFilePath = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var options = new LuminaUIDiagnosticsOptions();
        configure?.Invoke(options);

        // Store the caller path so HotReloadManager can reliably find the source code!
        if (!string.IsNullOrEmpty(callerFilePath))
        {
            HotReload.HotReloadManager.SetSourceRoot(System.IO.Path.GetDirectoryName(callerFilePath));
        }

        lock (Sync)
        {
            _host?.Dispose();
            _host = new LuminaUIDiagnosticsHost(options);

            if (options.StartImmediately)
                _host.Start();

            RegisterProcessCleanup();
        }

        return builder.AfterSetup(RegisterApplicationCleanup);
    }

    public static AppBuilder AttachLuminaDevTools(
        this AppBuilder builder,
        Avalonia.Input.KeyGesture? gesture = null)
    {
        return UI.LuminaDevTools.AttachLuminaDevTools(builder, gesture);
    }

    public static void ShutdownLuminaUIDiagnostics() =>
        DisposeHost();

    public static LuminaUIDiagnosticsHost? GetLuminaUIDiagnosticsHost()
    {
        lock (Sync)
        {
            return _host;
        }
    }

    private static void RegisterProcessCleanup()
    {
        if (_processCleanupRegistered)
            return;

        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
        _processCleanupRegistered = true;
    }

    private static void RegisterApplicationCleanup(AppBuilder _)
    {
        lock (Sync)
        {
            if (_applicationCleanupRegistered)
                return;

            if (Application.Current?.ApplicationLifetime is IControlledApplicationLifetime lifetime)
            {
                lifetime.Exit += OnApplicationExit;
                _applicationCleanupRegistered = true;
            }
        }
    }

    private static void OnApplicationExit(object? sender, ControlledApplicationLifetimeExitEventArgs args) =>
        DisposeHost();

    private static void OnProcessExit(object? sender, EventArgs args) =>
        DisposeHost();

    private static void DisposeHost()
    {
        LuminaUIDiagnosticsHost? host;
        lock (Sync)
        {
            host = _host;
            _host = null;
        }

        host?.Dispose();
    }
}


