using System.Globalization;
using SkiaSharp;

namespace LuminaUI.Diagnostics.Visual;

/// <summary>
/// Applies crop/resize/encode post-processing to a captured screenshot and
/// computes a frame hash over the final encoded bytes.
/// </summary>
internal static class ScreenshotImageProcessor
{
    public const string PngFormat = "png";
    public const string JpegFormat = "jpeg";
    public const string PngMimeType = "image/png";
    public const string JpegMimeType = "image/jpeg";

    public static bool TryParseFormat(string? text, out string format)
    {
        format = string.IsNullOrWhiteSpace(text)
            ? PngFormat
            : text.Trim().ToLowerInvariant();

        return format is PngFormat or JpegFormat;
    }

    public static string GetMimeType(string format) =>
        format == JpegFormat ? JpegMimeType : PngMimeType;

    public static bool TryParseCrop(
        string? text,
        out PixelCrop crop,
        out string? error)
    {
        crop = default;
        error = null;

        if (string.IsNullOrWhiteSpace(text))
            return true;

        var parts = text.Split(',');
        if (parts.Length != 4)
        {
            error = $"Crop '{text}' is invalid. Expected 'x,y,width,height'.";
            return false;
        }

        var values = new int[4];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out values[i]))
            {
                error = $"Crop '{text}' is invalid. Expected 'x,y,width,height' with integer components.";
                return false;
            }
        }

        if (values[2] <= 0 || values[3] <= 0)
        {
            error = $"Crop '{text}' is invalid. Width and height must be positive.";
            return false;
        }

        crop = new PixelCrop(values[0], values[1], values[2], values[3]);
        return true;
    }

    public static ProcessedScreenshot Process(
        SKBitmap source,
        ScreenshotImageOptions options)
    {
        ArgumentNullException.ThrowIfNull(source);

        var working = source;
        SKBitmap? cropped = null;
        SKBitmap? resized = null;

        try
        {
            if (options.Crop is { } crop)
            {
                var rect = ClampCrop(crop, source.Width, source.Height);
                if (rect.Width <= 0 || rect.Height <= 0)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(options),
                        $"Crop rectangle ({crop.X},{crop.Y},{crop.Width},{crop.Height}) does not intersect the captured image ({source.Width}x{source.Height}).");
                }

                cropped = new SKBitmap(rect.Width, rect.Height);
                using (var canvas = new SKCanvas(cropped))
                {
                    canvas.DrawBitmap(
                        working,
                        new SKRectI(rect.X, rect.Y, rect.Right, rect.Bottom),
                        new SKRect(0, 0, rect.Width, rect.Height));
                }

                working = cropped;
            }

            if (options.MaxWidth is > 0 && working.Width > options.MaxWidth.Value)
            {
                var targetWidth = options.MaxWidth.Value;
                var targetHeight = Math.Max(1, (int)Math.Round(working.Height * (double)targetWidth / working.Width));
                resized = working.Resize(
                    new SKImageInfo(targetWidth, targetHeight),
                    new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None));
                working = resized;
            }

            using var image = SKImage.FromBitmap(working);
            using var data = image.Encode(
                options.Format == JpegFormat ? SKEncodedImageFormat.Jpeg : SKEncodedImageFormat.Png,
                Math.Clamp(options.Quality, 1, 100));
            var bytes = data.ToArray();

            return new ProcessedScreenshot(
                bytes,
                options.Format,
                GetMimeType(options.Format),
                working.Width,
                working.Height,
                ComputeFrameHash(bytes));
        }
        finally
        {
            cropped?.Dispose();
            resized?.Dispose();
        }
    }

    public static string ComputeFrameHash(byte[] bytes) => Crc32.ComputeHex(bytes);

    private static PixelCrop ClampCrop(
        PixelCrop crop,
        int imageWidth,
        int imageHeight)
    {
        var left = Math.Clamp(crop.X, 0, imageWidth);
        var top = Math.Clamp(crop.Y, 0, imageHeight);
        var right = Math.Clamp(crop.X + crop.Width, 0, imageWidth);
        var bottom = Math.Clamp(crop.Y + crop.Height, 0, imageHeight);
        return new PixelCrop(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
    }

    private static class Crc32
    {
        private static readonly uint[] Table = BuildTable();

        public static string ComputeHex(byte[] bytes)
        {
            var crc = 0xFFFFFFFFu;
            foreach (var b in bytes)
                crc = (crc >> 8) ^ Table[(crc ^ b) & 0xFF];

            return (~crc).ToString("x8", CultureInfo.InvariantCulture);
        }

        private static uint[] BuildTable()
        {
            var table = new uint[256];
            for (var i = 0; i < table.Length; i++)
            {
                var entry = (uint)i;
                for (var bit = 0; bit < 8; bit++)
                    entry = (entry & 1) != 0 ? (entry >> 1) ^ 0xEDB88320u : entry >> 1;
                table[i] = entry;
            }

            return table;
        }
    }
}

internal readonly record struct PixelCrop(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;
}

internal sealed record ScreenshotImageOptions(
    int? MaxWidth,
    string Format,
    int Quality,
    PixelCrop? Crop)
{
    public static ScreenshotImageOptions Default { get; } =
        new(MaxWidth: null, ScreenshotImageProcessor.PngFormat, Quality: 80, Crop: null);
}

internal sealed record ProcessedScreenshot(
    byte[] Bytes,
    string Format,
    string MimeType,
    int Width,
    int Height,
    string FrameHash);
