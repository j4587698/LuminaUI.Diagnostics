using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using LuminaUI.Diagnostics.Localization;

namespace LuminaUI.Diagnostics.UI;

public static class LuminaDevTools
{
    private static LuminaDevToolsWindow? s_window;
    private static KeyGesture s_gesture = new KeyGesture(Key.F12);
    private static IDiagnosticsClient? s_sharedClient;
    private static DiagnosticsServices? s_standaloneServices;

    private static bool _registered;

    internal static void Register(KeyGesture? gesture = null)
    {
        if (_registered)
            return;
        _registered = true;

        if (gesture != null)
            s_gesture = gesture;

        DiagnosticsLocalization.Register();
        InputElement.KeyDownEvent.AddClassHandler<TopLevel>(OnKeyDown, RoutingStrategies.Tunnel);
        HotReload.HotReloadManager.StartWatcher();
    }

    public static AppBuilder AttachLuminaDevTools(this AppBuilder builder, KeyGesture? gesture = null)
    {
        return builder.AfterSetup(_ => Register(gesture));
    }

    public static void SetSharedClient(IDiagnosticsClient client)
    {
        s_sharedClient = client ?? throw new ArgumentNullException(nameof(client));
    }

    public static void Initialize() =>
        DiagnosticsLocalization.Register();

    private static void OnKeyDown(TopLevel topLevel, KeyEventArgs e)
    {
        if (s_gesture.Matches(e))
        {
            if (!ExternalDevToolsLauncher.TryOpen())
                Open(topLevel);
            e.Handled = true;
        }
        else if (s_window?.HandleShortcut(e) == true)
        {
            e.Handled = true;
        }
    }

    public static void Open(TopLevel? owner = null)
    {
        DiagnosticsLocalization.Register();
        if (s_window == null)
        {
            var client = s_sharedClient;
            if (client is null)
            {
                var host = LuminaUIDiagnosticsExtensions.GetLuminaUIDiagnosticsHost();
                if (host is not null && host.Dispatcher is not null && host.TryGetNodeRegistry(out var nodeRegistry))
                {
                    client = new DispatcherDiagnosticsClient(host.Dispatcher, nodeRegistry);
                }
                else
                {
                    s_standaloneServices = DiagnosticsServiceFactory.CreateDefault();
                    client = new DispatcherDiagnosticsClient(
                        s_standaloneServices.Dispatcher,
                        s_standaloneServices.NodeRegistry);
                }
            }

            var window = new LuminaDevToolsWindow(client);
            s_window = window;
            void ReleaseWindow()
            {
                if (!ReferenceEquals(s_window, window))
                    return;

                s_window = null;
                s_standaloneServices?.Dispose();
                s_standaloneServices = null;
            }

            window.DiagnosticsDisposed += (_, _) => ReleaseWindow();
            window.Closing += (_, _) => ReleaseWindow();
            window.Closed += (_, _) => ReleaseWindow();
            window.Show();
        }
        else
        {
            s_window.Activate();
        }
    }
}
