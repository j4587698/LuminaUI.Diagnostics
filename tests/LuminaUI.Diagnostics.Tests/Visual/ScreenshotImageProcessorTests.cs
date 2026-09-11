using LuminaUI.Diagnostics.Visual;
using SkiaSharp;

namespace LuminaUI.Diagnostics.Tests.Visual;

public sealed class ScreenshotImageProcessorTests
{
    [Fact]
    public void Process_DefaultOptions_KeepsSizeAndPngFormat()
    {
        using var source = CreateTestBitmap(400, 300);

        var result = ScreenshotImageProcessor.Process(source, ScreenshotImageOptions.Default);

        Assert.Equal(400, result.Width);
        Assert.Equal(300, result.Height);
        Assert.Equal("png", result.Format);
        Assert.Equal("image/png", result.MimeType);
        AssertPng(result.Bytes);
        AssertFrameHash(result.FrameHash);
    }

    [Fact]
    public void Process_MaxWidth_ScalesDownProportionally()
    {
        using var source = CreateTestBitmap(400, 300);

        var result = ScreenshotImageProcessor.Process(
            source,
            new ScreenshotImageOptions(MaxWidth: 200, "png", 80, null));

        Assert.Equal(200, result.Width);
        Assert.Equal(150, result.Height);
    }

    [Fact]
    public void Process_MaxWidthAboveSource_DoesNotUpscale()
    {
        using var source = CreateTestBitmap(400, 300);

        var result = ScreenshotImageProcessor.Process(
            source,
            new ScreenshotImageOptions(MaxWidth: 800, "png", 80, null));

        Assert.Equal(400, result.Width);
        Assert.Equal(300, result.Height);
    }

    [Fact]
    public void Process_Crop_CutsOutRegion()
    {
        using var source = CreateTestBitmap(400, 300);

        var result = ScreenshotImageProcessor.Process(
            source,
            new ScreenshotImageOptions(null, "png", 80, new PixelCrop(100, 50, 200, 150)));

        Assert.Equal(200, result.Width);
        Assert.Equal(150, result.Height);

        using var decoded = SKBitmap.Decode(result.Bytes);
        Assert.NotNull(decoded);
        // (0,0) of the crop corresponds to (100,50) of the source: top-left quadrant.
        Assert.Equal(SKColors.Red, decoded!.GetPixel(10, 10));
    }

    [Fact]
    public void Process_CropThenResize_AppliesInOrder()
    {
        using var source = CreateTestBitmap(400, 300);

        var result = ScreenshotImageProcessor.Process(
            source,
            new ScreenshotImageOptions(MaxWidth: 100, "png", 80, new PixelCrop(0, 0, 400, 200)));

        Assert.Equal(100, result.Width);
        Assert.Equal(50, result.Height);
    }

    [Fact]
    public void Process_CropOutsideBounds_IsClamped()
    {
        using var source = CreateTestBitmap(400, 300);

        var result = ScreenshotImageProcessor.Process(
            source,
            new ScreenshotImageOptions(null, "png", 80, new PixelCrop(-50, -50, 200, 200)));

        Assert.Equal(150, result.Width);
        Assert.Equal(150, result.Height);
    }

    [Fact]
    public void Process_CropWithoutIntersection_Throws()
    {
        using var source = CreateTestBitmap(400, 300);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ScreenshotImageProcessor.Process(
                source,
                new ScreenshotImageOptions(null, "png", 80, new PixelCrop(500, 500, 100, 100))));
    }

    [Fact]
    public void Process_Jpeg_EncodesWithJpegMagic()
    {
        using var source = CreateTestBitmap(400, 300);

        var result = ScreenshotImageProcessor.Process(
            source,
            new ScreenshotImageOptions(null, "jpeg", 65, null));

        Assert.Equal("jpeg", result.Format);
        Assert.Equal("image/jpeg", result.MimeType);
        Assert.True(result.Bytes.Length > 2);
        Assert.Equal(0xFF, result.Bytes[0]);
        Assert.Equal(0xD8, result.Bytes[1]);
    }

    [Fact]
    public void Process_JpegScaled_IsMuchSmallerThanFullPng()
    {
        using var source = CreateNoiseBitmap(800, 600);

        var png = ScreenshotImageProcessor.Process(source, ScreenshotImageOptions.Default);
        var jpeg = ScreenshotImageProcessor.Process(
            source,
            new ScreenshotImageOptions(MaxWidth: 800, "jpeg", 65, null));

        Assert.True(
            jpeg.Bytes.Length < png.Bytes.Length * 0.4,
            $"Expected jpeg ({jpeg.Bytes.Length}) to be >60% smaller than png ({png.Bytes.Length}).");
    }

    [Fact]
    public void Process_SameInput_ProducesSameFrameHash()
    {
        using var source = CreateTestBitmap(200, 120);

        var first = ScreenshotImageProcessor.Process(source, ScreenshotImageOptions.Default);
        var second = ScreenshotImageProcessor.Process(source, ScreenshotImageOptions.Default);

        Assert.Equal(first.FrameHash, second.FrameHash);
    }

    [Fact]
    public void Process_ChangedInput_ProducesDifferentFrameHash()
    {
        using var source = CreateTestBitmap(200, 120);

        var before = ScreenshotImageProcessor.Process(source, ScreenshotImageOptions.Default);
        source.SetPixel(0, 0, SKColors.White);
        var after = ScreenshotImageProcessor.Process(source, ScreenshotImageOptions.Default);

        Assert.NotEqual(before.FrameHash, after.FrameHash);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("10,20,30,40", true)]
    [InlineData("10,20,30", false)]
    [InlineData("10,20,30,40,50", false)]
    [InlineData("a,b,c,d", false)]
    [InlineData("10,20,0,40", false)]
    [InlineData("10,20,30,-40", false)]
    public void TryParseCrop_ValidatesInput(
        string? text,
        bool expected)
    {
        var result = ScreenshotImageProcessor.TryParseCrop(text, out var crop, out var error);

        Assert.Equal(expected, result);
        if (expected && !string.IsNullOrWhiteSpace(text))
        {
            Assert.Equal(new PixelCrop(10, 20, 30, 40), crop);
        }
        else if (!expected)
        {
            Assert.NotNull(error);
        }
    }

    [Theory]
    [InlineData(null, "png")]
    [InlineData("png", "png")]
    [InlineData("PNG", "png")]
    [InlineData("jpeg", "jpeg")]
    [InlineData("Jpeg", "jpeg")]
    public void TryParseFormat_AcceptsKnownFormats(
        string? text,
        string expected)
    {
        Assert.True(ScreenshotImageProcessor.TryParseFormat(text, out var format));
        Assert.Equal(expected, format);
    }

    [Fact]
    public void TryParseFormat_RejectsUnknownFormat()
    {
        Assert.False(ScreenshotImageProcessor.TryParseFormat("gif", out _));
    }

    private static void AssertPng(byte[] bytes)
    {
        Assert.True(bytes.Length > 8);
        Assert.Equal(0x89, bytes[0]);
        Assert.Equal((byte)'P', bytes[1]);
        Assert.Equal((byte)'N', bytes[2]);
        Assert.Equal((byte)'G', bytes[3]);
    }

    private static void AssertFrameHash(string frameHash)
    {
        Assert.Equal(8, frameHash.Length);
        Assert.All(frameHash, c => Assert.Contains(c, "0123456789abcdef"));
    }

    /// <summary>
    /// Creates a bitmap whose quadrants are filled with distinct colors:
    /// red (top-left), green (bottom-left), blue (top-right), yellow (bottom-right).
    /// </summary>
    private static SKBitmap CreateTestBitmap(int width, int height)
    {
        var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Black);
        var halfWidth = width / 2;
        var halfHeight = height / 2;
        canvas.DrawRect(0, 0, halfWidth, halfHeight, new SKPaint { Color = SKColors.Red });
        canvas.DrawRect(halfWidth, 0, halfWidth, halfHeight, new SKPaint { Color = SKColors.Blue });
        canvas.DrawRect(0, halfHeight, halfWidth, halfHeight, new SKPaint { Color = SKColors.Green });
        canvas.DrawRect(halfWidth, halfHeight, halfWidth, halfHeight, new SKPaint { Color = SKColors.Yellow });
        return bitmap;
    }

    private static SKBitmap CreateNoiseBitmap(int width, int height)
    {
        var bitmap = new SKBitmap(width, height);
        var random = new Random(42);
        var buffer = new byte[3];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                random.NextBytes(buffer);
                bitmap.SetPixel(x, y, new SKColor(buffer[0], buffer[1], buffer[2]));
            }
        }

        return bitmap;
    }
}
