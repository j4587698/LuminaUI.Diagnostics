using System.Text.Json.Nodes;
using LuminaUI.Diagnostics.Visual;

namespace LuminaUI.Diagnostics.Tests.Visual;

public sealed class ScreenshotSaveToFileTests
{
    [Fact]
    public void SaveToFile_WritesBytesAndReturnsMetadataWithoutBase64()
    {
        var directory = Path.Combine(Path.GetTempPath(), "lumina-screenshot-tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "nested", "frame.jpg");
        var payload = new ProcessedScreenshot(
            [1, 2, 3, 4, 5],
            "jpeg",
            "image/jpeg",
            800,
            600,
            "deadbeef");
        var request = new DiagnosticRequest("req", LuminaUIDiagnosticsToolNames.TakeScreenshot, new JsonObject(), 5_000);

        try
        {
            // Repeated calls overwrite the same file.
            for (var i = 0; i < 3; i++)
            {
                var response = ScreenshotHandler.SaveToFile(request, payload, path);

                Assert.True(response.Success, response.Error?.Message);
                var data = Assert.IsType<JsonObject>(response.Data);
                Assert.Equal(path, data["path"]?.GetValue<string>());
                Assert.Equal(800, data["width"]?.GetValue<int>());
                Assert.Equal(600, data["height"]?.GetValue<int>());
                Assert.Equal(5, data["bytes"]?.GetValue<int>());
                Assert.Equal("deadbeef", data["frameHash"]?.GetValue<string>());
                Assert.False(data.ContainsKey("base64"));
            }

            Assert.Equal([1, 2, 3, 4, 5], File.ReadAllBytes(path));
            Assert.Single(Directory.GetFiles(Path.GetDirectoryName(path)!));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
