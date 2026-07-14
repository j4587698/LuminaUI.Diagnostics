using System.Diagnostics;
using System.IO.Pipes;
using LuminaUI.Diagnostics.Abstractions;

namespace LuminaUI.Diagnostics.UI;

internal static class ExternalDevToolsLauncher
{
    public static bool TryOpen()
    {
        var host = LuminaUIDiagnosticsExtensions.GetLuminaUIDiagnosticsHost();
        if (host is null || !host.IsRunning)
            return false;

        var targetProcessId = Environment.ProcessId;
        if (TryActivate(targetProcessId))
            return true;

        var executable = ResolveExecutable();
        if (executable is null)
            return false;

        var arguments = $"--target-pid {targetProcessId} --pipe \"{host.DiagnosticsPipeName}\"";
        var startInfo = executable.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
            ? new ProcessStartInfo("dotnet", $"\"{executable}\" {arguments}")
            : new ProcessStartInfo(executable, arguments);
        startInfo.UseShellExecute = false;
        startInfo.CreateNoWindow = true;
        startInfo.WorkingDirectory = AppContext.BaseDirectory;

        try
        {
            return Process.Start(startInfo) is not null;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool TryActivate(int targetProcessId)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(
                ".",
                ActivationPipeName.ForProcess(targetProcessId),
                PipeDirection.Out,
                PipeOptions.Asynchronous);
            pipe.Connect(75);
            pipe.WriteByte(1);
            pipe.Flush();
            return true;
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string? ResolveExecutable()
    {
        var configuredPath = Environment.GetEnvironmentVariable("LUMINA_DEVTOOLS_PATH");
        if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath))
            return Path.GetFullPath(configuredPath);

        var directory = Path.Combine(AppContext.BaseDirectory, "lumina-devtools");
        var executableName = OperatingSystem.IsWindows()
            ? "LuminaUI.Diagnostics.Desktop.exe"
            : "LuminaUI.Diagnostics.Desktop";
        var executable = Path.Combine(directory, executableName);
        if (File.Exists(executable))
            return executable;

        var assembly = Path.Combine(directory, "LuminaUI.Diagnostics.Desktop.dll");
        return File.Exists(assembly) ? assembly : null;
    }
}
