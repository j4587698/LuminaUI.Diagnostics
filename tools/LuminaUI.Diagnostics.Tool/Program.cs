using LuminaUI.Diagnostics.Mcp;
using LuminaUI.Diagnostics.Tool.DevTools;
using Microsoft.Extensions.Hosting;

namespace LuminaUI.Diagnostics.Tool;

public static class Program
{
    [STAThread]
    public static async Task<int> Main(string[] args)
    {
        var commandName = GetInvokedCommandName();
        var isMcpShim = commandName.Contains("lumina-mcp", StringComparison.OrdinalIgnoreCase);

        // 1. Help
        if (args.Any(a => a is "--help" or "-h" or "help"))
        {
            PrintHelp();
            return 0;
        }

        // 2. If invoked as `lumina-mcp` (direct MCP shim compatibility)
        if (isMcpShim)
        {
            if (args.Length > 0 && (string.Equals(args[0], "devtools", StringComparison.OrdinalIgnoreCase) ||
                                    string.Equals(args[0], "gui", StringComparison.OrdinalIgnoreCase)))
            {
                return DevToolsProgram.Run(args.Skip(1).ToArray());
            }

            var mcpArgs = (args.Length > 0 && string.Equals(args[0], "mcp", StringComparison.OrdinalIgnoreCase))
                ? args.Skip(1).ToArray()
                : args;
            await ServerHostBuilder.Build(mcpArgs).RunAsync();
            return 0;
        }

        // 3. If invoked with "mcp" subcommand (e.g. `lumina mcp`)
        if (args.Length > 0 && string.Equals(args[0], "mcp", StringComparison.OrdinalIgnoreCase))
        {
            var mcpArgs = args.Skip(1).ToArray();
            await ServerHostBuilder.Build(mcpArgs).RunAsync();
            return 0;
        }

        // 4. DevTools mode (or default)
        var devToolsArgs = args;
        if (args.Length > 0 && (string.Equals(args[0], "devtools", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(args[0], "gui", StringComparison.OrdinalIgnoreCase)))
        {
            devToolsArgs = args.Skip(1).ToArray();
        }

        return DevToolsProgram.Run(devToolsArgs);
    }

    private static string GetInvokedCommandName()
    {
        try
        {
            var processPath = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(processPath))
            {
                var fileName = Path.GetFileNameWithoutExtension(processPath);
                if (fileName.Contains("lumina-mcp", StringComparison.OrdinalIgnoreCase) ||
                    fileName.Contains("LuminaUI.Diagnostics.Mcp", StringComparison.OrdinalIgnoreCase))
                    return fileName;
            }

            var cmdArgs = Environment.GetCommandLineArgs();
            if (cmdArgs.Length > 0 && !string.IsNullOrWhiteSpace(cmdArgs[0]))
            {
                var cmdName = Path.GetFileNameWithoutExtension(cmdArgs[0]);
                if (cmdName.Contains("lumina-mcp", StringComparison.OrdinalIgnoreCase) ||
                    cmdName.Contains("LuminaUI.Diagnostics.Mcp", StringComparison.OrdinalIgnoreCase))
                    return cmdName;
            }
        }
        catch
        {
        }

        return string.Empty;
    }

    private static void PrintHelp()
    {
        Console.WriteLine(@"LuminaUI Diagnostics Tool (lumina / lumina-mcp)

Usage:
  lumina [command] [options]
  lumina-mcp [options]

Commands:
  mcp             Run as an MCP stdio server for AI tools (opencode, Cursor, VS Code).
  devtools        Launch the DevTools desktop window.
  (none)          Launch DevTools desktop window (auto-discovering active Avalonia applications).

Compatibility:
  Executing as 'lumina-mcp' automatically runs the MCP stdio server without requiring the 'mcp' subcommand.

DevTools Options:
  --target-pid <pid>    Target application process ID to connect to.
  --pipe <name>         Named pipe to connect to.

Examples:
  lumina mcp
  lumina-mcp
  lumina devtools --target-pid 12345 --pipe lumina-diag-12345
  lumina
");
    }
}
