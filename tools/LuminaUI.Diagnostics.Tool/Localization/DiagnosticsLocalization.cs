using System.Resources;
using LuminaUI.Localization;

namespace LuminaUI.Diagnostics.Localization;

internal static class DiagnosticsLocalization
{
    private static bool _registered;

    public static void Register()
    {
        if (_registered)
            return;

        var assembly = typeof(DiagnosticsLocalization).Assembly;
        LuminaLocalization.RegisterResourceManager(
            new ResourceManager(
                $"{assembly.GetName().Name}.Localization.Resources.DevToolsStrings",
                assembly),
            priority: 20);

        _registered = true;
    }
}