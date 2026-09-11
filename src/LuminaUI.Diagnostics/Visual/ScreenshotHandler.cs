using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using LuminaUI.Diagnostics.Controls;
using LuminaUI.Diagnostics.Dispatch;
using LuminaUI.Diagnostics.Inspection;
using LuminaUI.Diagnostics.Threading;
using LuminaUI.Diagnostics.Abstractions;
using SkiaSharp;

namespace LuminaUI.Diagnostics.Visual;

public sealed class ScreenshotHandler : IDiagnosticToolHandler
{
    private readonly IUiThreadInvoker _invoker;
    private readonly IControlResolver _controlResolver;
    private readonly NodeRegistry _nodeRegistry;
    private readonly Func<IReadOnlyList<Control>> _getRoots;

    public ScreenshotHandler(
        IUiThreadInvoker invoker,
        IControlResolver? controlResolver = null,
        NodeRegistry? nodeRegistry = null,
        Func<IReadOnlyList<Control>>? getRoots = null)
    {
        _invoker = invoker ?? throw new ArgumentNullException(nameof(invoker));
        _controlResolver = controlResolver ?? new AvaloniaControlResolver();
        _nodeRegistry = nodeRegistry ?? new NodeRegistry();
        _getRoots = getRoots ?? InspectionRequestHelpers.GetCurrentWindowRoots;
    }

    public string Method => LuminaUIDiagnosticsToolNames.TakeScreenshot;

    public Task<DiagnosticResponse> HandleAsync(
        DiagnosticRequest request,
        CancellationToken cancellationToken = default) =>
        _invoker.InvokeAsync(
            request,
            _ => Task.FromResult(HandleOnUiThread(request)),
            cancellationToken);

    private DiagnosticResponse HandleOnUiThread(DiagnosticRequest request)
    {
        var roots = _getRoots();
        var windowIndex = InspectionRequestHelpers.GetInt(request.Parameters, "windowIndex", 0);
        if (windowIndex < 0 || windowIndex >= roots.Count)
        {
            return DiagnosticResponse.Fail(
                request.Id,
                DiagnosticErrorCode.TargetNotFound,
                $"Window index {windowIndex} was not found.",
                new JsonObject { ["windowIndex"] = windowIndex });
        }

        var target = ResolveTarget(request, roots[windowIndex]);
        if (!target.Success)
            return target.Response!;

        if (!TryCreateOptions(request, out var options, out var saveToFile, out var optionsError))
        {
            return DiagnosticResponse.Fail(
                request.Id,
                DiagnosticErrorCode.InvalidRequest,
                optionsError!);
        }

        var pixelSize = GetPixelSize(target.Control!);
        if (pixelSize.Width <= 0 || pixelSize.Height <= 0)
        {
            return DiagnosticResponse.Fail(
                request.Id,
                DiagnosticErrorCode.InvalidRequest,
                "Cannot capture a screenshot for a zero-size target.",
                new JsonObject
                {
                    ["width"] = pixelSize.Width,
                    ["height"] = pixelSize.Height
                });
        }

        try
        {
            using var bitmap = new RenderTargetBitmap(pixelSize);
            bitmap.Render(target.Control!);

            using var pngStream = new MemoryStream();
            bitmap.Save(pngStream);
            pngStream.Position = 0;

            using var source = SKBitmap.Decode(pngStream);
            if (source is null)
            {
                return DiagnosticResponse.Fail(
                    request.Id,
                    DiagnosticErrorCode.InternalError,
                    "Screenshot capture failed: could not decode the rendered frame.");
            }

            var processed = ScreenshotImageProcessor.Process(source, options);

            if (!string.IsNullOrWhiteSpace(saveToFile))
                return SaveToFile(request, processed, saveToFile);

            return DiagnosticResponse.Ok(
                request.Id,
                new JsonObject
                {
                    ["format"] = processed.Format,
                    ["mimeType"] = processed.MimeType,
                    ["width"] = processed.Width,
                    ["height"] = processed.Height,
                    ["bytes"] = processed.Bytes.Length,
                    ["frameHash"] = processed.FrameHash,
                    ["base64"] = Convert.ToBase64String(processed.Bytes)
                });
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return DiagnosticResponse.Fail(
                request.Id,
                DiagnosticErrorCode.InvalidRequest,
                ex.Message);
        }
        catch (Exception ex)
        {
            return DiagnosticResponse.Fail(
                request.Id,
                DiagnosticErrorCode.InternalError,
                $"Screenshot capture failed: {ex.Message}");
        }
    }

    private static bool TryCreateOptions(
        DiagnosticRequest request,
        out ScreenshotImageOptions options,
        out string? saveToFile,
        out string? error)
    {
        options = ScreenshotImageOptions.Default;
        saveToFile = InspectionRequestHelpers.GetString(request.Parameters, "saveToFile");
        error = null;

        if (!ScreenshotImageProcessor.TryParseFormat(
                InspectionRequestHelpers.GetString(request.Parameters, "format"),
                out var format))
        {
            error = "Parameter 'format' must be 'png' or 'jpeg'.";
            return false;
        }

        if (!ScreenshotImageProcessor.TryParseCrop(
                InspectionRequestHelpers.GetString(request.Parameters, "crop"),
                out var crop,
                out var cropError))
        {
            error = cropError;
            return false;
        }

        var maxWidth = InspectionRequestHelpers.GetInt(request.Parameters, "maxWidth", 0);
        var quality = Math.Clamp(
            InspectionRequestHelpers.GetInt(request.Parameters, "quality", 80),
            1,
            100);

        options = new ScreenshotImageOptions(
            maxWidth > 0 ? maxWidth : null,
            format,
            quality,
            crop.Width > 0 ? crop : null);
        return true;
    }

    internal static DiagnosticResponse SaveToFile(
        DiagnosticRequest request,
        ProcessedScreenshot processed,
        string saveToFile)
    {
        try
        {
            var fullPath = Path.GetFullPath(saveToFile);
            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllBytes(fullPath, processed.Bytes);

            return DiagnosticResponse.Ok(
                request.Id,
                new JsonObject
                {
                    ["path"] = fullPath,
                    ["format"] = processed.Format,
                    ["mimeType"] = processed.MimeType,
                    ["width"] = processed.Width,
                    ["height"] = processed.Height,
                    ["bytes"] = processed.Bytes.Length,
                    ["frameHash"] = processed.FrameHash
                });
        }
        catch (Exception ex)
        {
            return DiagnosticResponse.Fail(
                request.Id,
                DiagnosticErrorCode.InternalError,
                $"Saving the screenshot to '{saveToFile}' failed: {ex.Message}");
        }
    }

    private TargetResolution ResolveTarget(
        DiagnosticRequest request,
        Control root)
    {
        var controlId = InspectionRequestHelpers.GetString(request.Parameters, "controlId");
        var nodeId = InspectionRequestHelpers.GetString(request.Parameters, "nodeId");
        if (string.IsNullOrWhiteSpace(controlId) && string.IsNullOrWhiteSpace(nodeId))
            return TargetResolution.Found(root);

        var lookup = InspectionRequestHelpers.ResolveControl(
            request,
            [root],
            _controlResolver,
            _nodeRegistry);

        return lookup.Success
            ? TargetResolution.Found(lookup.Control!)
            : TargetResolution.Failed(lookup.Response!);
    }

    private static PixelSize GetPixelSize(Control control)
    {
        var width = (int)Math.Ceiling(control.Bounds.Width);
        var height = (int)Math.Ceiling(control.Bounds.Height);
        return new PixelSize(width, height);
    }

    private sealed record TargetResolution(
        bool Success,
        Control? Control,
        DiagnosticResponse? Response)
    {
        public static TargetResolution Found(Control control) =>
            new(Success: true, control, Response: null);

        public static TargetResolution Failed(DiagnosticResponse response) =>
            new(Success: false, Control: null, response);
    }
}
