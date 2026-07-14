using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace LuminaUI.Diagnostics.UI;

internal class HighlightAdorner : Control
{
    private Avalonia.Visual? _target;
    private static readonly IBrush s_fillBrush = new SolidColorBrush(Color.FromArgb(100, 100, 149, 237));
    private static readonly IPen s_borderPen = new Pen(new SolidColorBrush(Color.FromRgb(100, 149, 237)), 1);
    private static readonly IBrush s_marginBrush = new SolidColorBrush(Color.FromArgb(70, 246, 173, 85));
    private static readonly IBrush s_paddingBrush = new SolidColorBrush(Color.FromArgb(70, 76, 175, 80));
    private static readonly IPen s_guidePen = new Pen(new SolidColorBrush(Color.FromArgb(170, 79, 107, 237)), 1, dashStyle: DashStyle.Dash);
    private static readonly IBrush s_infoBackground = new SolidColorBrush(Color.FromArgb(225, 32, 38, 52));
    private static readonly IBrush s_infoForeground = Brushes.White;

    public HighlightAdorner()
    {
        IsHitTestVisible = false;
        ClipToBounds = false;
    }

    public Avalonia.Visual? Target
    {
        get => _target;
        set
        {
            if (_target != value)
            {
                _target = value;
                InvalidateVisual();
            }
        }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        if (_target == null)
            return;

        if (!_target.IsVisible || _target.Opacity == 0)
            return;

        // Ensure target is still attached to visual tree
        if (!this.IsAttachedToVisualTree() || !_target.IsAttachedToVisualTree())
            return;

        try
        {
            // Transform bounds to AdornerLayer coordinates
            var transform = _target.TransformToVisual(this);
            if (transform.HasValue)
            {
                var bounds = new Rect(new Point(0, 0), _target.Bounds.Size);
                var transformedBounds = bounds.TransformToAABB(transform.Value);
                var settings = DevToolsSettingsStore.Current;

                if (settings.OverlayVisualizeMarginPadding)
                {
                    var margin = ReadThickness(_target, "Margin");
                    var marginBounds = new Rect(
                        transformedBounds.X - margin.Left,
                        transformedBounds.Y - margin.Top,
                        transformedBounds.Width + margin.Left + margin.Right,
                        transformedBounds.Height + margin.Top + margin.Bottom);
                    context.DrawRectangle(s_marginBrush, null, marginBounds);

                    var padding = ReadThickness(_target, "Padding");
                    var paddingBounds = new Rect(
                        transformedBounds.X + padding.Left,
                        transformedBounds.Y + padding.Top,
                        Math.Max(0, transformedBounds.Width - padding.Left - padding.Right),
                        Math.Max(0, transformedBounds.Height - padding.Top - padding.Bottom));
                    context.DrawRectangle(s_paddingBrush, null, paddingBounds);
                }

                context.DrawRectangle(s_fillBrush, s_borderPen, transformedBounds);

                if (settings.OverlayShowRulers || settings.OverlayShowExtensionLines)
                    DrawGuides(context, transformedBounds, settings);

                if (settings.OverlayShowInfo)
                    DrawInfo(context, transformedBounds);
            }
        }
        catch (Exception)
        {
            // Transforms might fail if tree is changing
        }
    }

    private void DrawGuides(DrawingContext context, Rect targetBounds, DevToolsSettings settings)
    {
        if (settings.OverlayShowExtensionLines)
        {
            context.DrawLine(s_guidePen, new Point(0, targetBounds.Top), new Point(Bounds.Width, targetBounds.Top));
            context.DrawLine(s_guidePen, new Point(0, targetBounds.Bottom), new Point(Bounds.Width, targetBounds.Bottom));
            context.DrawLine(s_guidePen, new Point(targetBounds.Left, 0), new Point(targetBounds.Left, Bounds.Height));
            context.DrawLine(s_guidePen, new Point(targetBounds.Right, 0), new Point(targetBounds.Right, Bounds.Height));
        }

        if (settings.OverlayShowRulers)
        {
            context.DrawLine(s_borderPen, new Point(0, 8), new Point(Bounds.Width, 8));
            context.DrawLine(s_borderPen, new Point(8, 0), new Point(8, Bounds.Height));
            for (var x = 0d; x < Bounds.Width; x += 50)
                context.DrawLine(s_borderPen, new Point(x, 4), new Point(x, 12));
            for (var y = 0d; y < Bounds.Height; y += 50)
                context.DrawLine(s_borderPen, new Point(4, y), new Point(12, y));
        }
    }

    private void DrawInfo(DrawingContext context, Rect targetBounds)
    {
        var name = _target is StyledElement { Name: { Length: > 0 } elementName }
            ? $" #{elementName}"
            : string.Empty;
        var text = $"{_target!.GetType().Name}{name}  {targetBounds.Width:0.#} × {targetBounds.Height:0.#}";
        var formatted = new FormattedText(
            text,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            new Typeface("Inter"),
            11,
            s_infoForeground);
        var origin = new Point(
            Math.Clamp(targetBounds.Left, 0, Math.Max(0, Bounds.Width - formatted.Width - 12)),
            Math.Max(0, targetBounds.Top - formatted.Height - 8));
        context.DrawRectangle(
            s_infoBackground,
            null,
            new Rect(origin.X - 5, origin.Y - 3, formatted.Width + 10, formatted.Height + 6),
            3);
        context.DrawText(formatted, origin);
    }

    private static Thickness ReadThickness(object target, string propertyName)
    {
        try
        {
            return target.GetType().GetProperty(propertyName)?.GetValue(target) is Thickness value
                ? value
                : default;
        }
        catch (Exception)
        {
            return default;
        }
    }
}
