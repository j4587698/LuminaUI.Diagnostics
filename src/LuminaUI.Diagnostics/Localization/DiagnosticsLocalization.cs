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

        LuminaLocalization.RegisterResourceManager(
            new ResourceManager(
                "LuminaUI.Diagnostics.Localization.Resources.DevToolsStrings",
                typeof(DiagnosticsLocalization).Assembly),
            priority: 20);

        _registered = true;
    }
}