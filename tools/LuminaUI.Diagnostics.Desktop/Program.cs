using Avalonia;
using Avalonia.Fonts.Inter;

namespace LuminaUI.Diagnostics.Desktop;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (!DevToolsArguments.TryParse(args, out var options))
            return 2;

        using var singleInstance = new DevToolsSingleInstance(options.TargetProcessId);
        if (!singleInstance.IsPrimary)
        {
            singleInstance.ActivatePrimary();
            return 0;
        }

        DevToolsApplication.Options = options;
        DevToolsApplication.SingleInstance = singleInstance;
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        return 0;
    }

    private static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<DevToolsApplication>()
            .UsePlatformDetect()
            .UseSkia()
            .UseHarfBuzz()
            .WithInterFont()
            .LogToTrace();
}

internal sealed record DevToolsArguments(int TargetProcessId, string PipeName)
{
    public static bool TryParse(string[] args, out DevToolsArguments options)
    {
        var targetProcessId = 0;
        string? pipeName = null;
        for (var index = 0; index < args.Length - 1; index++)
        {
            if (args[index] == "--target-pid")
                int.TryParse(args[++index], out targetProcessId);
            else if (args[index] == "--pipe")
                pipeName = args[++index];
        }

        if (targetProcessId <= 0 || string.IsNullOrWhiteSpace(pipeName))
        {
            options = null!;
            return false;
        }

        options = new DevToolsArguments(targetProcessId, pipeName);
        return true;
    }
}
