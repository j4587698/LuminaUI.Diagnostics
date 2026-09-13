using System.Globalization;
using LuminaUI.Diagnostics.Localization;
using LuminaUI.Localization;

namespace LuminaUI.Diagnostics.Tests.UI;

public sealed class DevToolsLocalizationTests
{
    [Fact]
    public void Register_ResolvesDefaultAndZhCnStrings()
    {
        DiagnosticsLocalization.Register();

        LuminaLocalization.SetCulture(CultureInfo.InvariantCulture);
        Assert.True(LuminaLocalization.TryGet("DevTools.Title", out var enTitle));
        Assert.Equal("Lumina DevTools", enTitle);

        LuminaLocalization.SetCulture(new CultureInfo("zh-CN"));
        Assert.True(LuminaLocalization.TryGet("DevTools.Title", out var zhTitle));
        Assert.Equal("Lumina 开发工具", zhTitle);
    }
}
