using System.Threading.Tasks;

namespace LuminaUI.Diagnostics.Mcp;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        return await LuminaUI.Diagnostics.Tool.Program.Main(args);
    }
}
