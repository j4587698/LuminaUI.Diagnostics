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

        var arguments = $"devtools --target-pid {targetProcessId} --pipe \"{host.DiagnosticsPipeName}\"";
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

        var toolName = OperatingSystem.IsWindows() ? "lumina.exe" : "lumina";

        // 1. Check global dotnet tools directory (~/.dotnet/tools/lumina)
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var globalTool = Path.Combine(userProfile, ".dotnet", "tools", toolName);
        if (File.Exists(globalTool))
            return globalTool;

        // 2. Check local application base directory
        var localTool = Path.Combine(AppContext.BaseDirectory, toolName);
        if (File.Exists(localTool))
            return localTool;

        // 3. Check subdirectory lumina-devtools
        var directory = Path.Combine(AppContext.BaseDirectory, "lumina-devtools");
        var directoryTool = Path.Combine(directory, toolName);
        if (File.Exists(directoryTool))
            return directoryTool;

        var legacyDesktopName = OperatingSystem.IsWindows()
            ? "LuminaUI.Diagnostics.Desktop.exe"
            : "LuminaUI.Diagnostics.Desktop";
        var legacyExecutable = Path.Combine(directory, legacyDesktopName);
        if (File.Exists(legacyExecutable))
            return legacyExecutable;

        var assembly = Path.Combine(directory, "LuminaUI.Diagnostics.Tool.dll");
        if (File.Exists(assembly))
            return assembly;

        // 4. Check system PATH
        return FindInPath(toolName);
    }

    private static string? FindInPath(string fileName)
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathEnv))
            return null;

        var paths = pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var p in paths)
        {
            try
            {
                var full = Path.Combine(p, fileName);
                if (File.Exists(full))
                    return full;
            }
            catch
            {
            }
        }

        return null;
    }
}
