using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace LuminaUI.Diagnostics.UI;

public static class LuminaDevTools
{
    private static KeyGesture s_gesture = new KeyGesture(Key.F12);
    private static bool _registered;

    internal static void Register(KeyGesture? gesture = null)
    {
        if (_registered)
            return;
        _registered = true;

        if (gesture != null)
            s_gesture = gesture;

        InputElement.KeyDownEvent.AddClassHandler<TopLevel>(OnKeyDown, RoutingStrategies.Tunnel);
        HotReload.HotReloadManager.StartWatcher();
    }

    public static bool Open()
    {
        return ExternalDevToolsLauncher.TryOpen();
    }

    public static AppBuilder AttachLuminaDevTools(this AppBuilder builder, KeyGesture? gesture = null)
    {
        return builder.AfterSetup(_ => Register(gesture));
    }

    private static void OnKeyDown(TopLevel topLevel, KeyEventArgs e)
    {
        if (s_gesture.Matches(e))
        {
            if (!ExternalDevToolsLauncher.TryOpen())
            {
                Trace.WriteLine("[LuminaUI.Diagnostics] Could not launch external DevTools. Please install the tool: dotnet tool install -g LuminaUI.Diagnostics.Tool");
            }
            e.Handled = true;
        }
    }
}
