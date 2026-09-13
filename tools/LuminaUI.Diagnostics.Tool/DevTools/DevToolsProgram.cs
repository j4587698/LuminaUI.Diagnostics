using Avalonia;
using Avalonia.Fonts.Inter;
using LuminaUI.Diagnostics.Desktop;
using LuminaUI.Diagnostics.Mcp.Discovery;
using LuminaUI.Diagnostics.Mcp.Transport;

namespace LuminaUI.Diagnostics.Tool.DevTools;

internal static class DevToolsProgram
{
    public static int Run(string[] args)
    {
        var options = ResolveOptions(args);
        if (options is null)
        {
            Console.Error.WriteLine("[LuminaUI.Diagnostics] No active Avalonia application found.");
            Console.Error.WriteLine("Please start an application with .UseLuminaUIDiagnostics(), or specify --target-pid <pid> --pipe <name>.");
            return 1;
        }

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

    private static DevToolsArguments? ResolveOptions(string[] args)
    {
        if (DevToolsArguments.TryParse(args, out var parsed))
            return parsed;

        try
        {
            var discovery = new AppDiscoveryService(new PipeDiagnosticClient());
            var apps = discovery.DiscoverAsync(timeoutMs: 1000).GetAwaiter().GetResult();
            if (apps.Count > 0)
            {
                var target = apps[0];
                return new DevToolsArguments(target.ProcessId, target.PipeName);
            }
        }
        catch
        {
        }

        return null;
    }

    private static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<DevToolsApplication>()
            .UsePlatformDetect()
            .UseSkia()
            .UseHarfBuzz()
            .WithInterFont()
            .LogToTrace();
}

public sealed record DevToolsArguments(int TargetProcessId, string PipeName)
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
