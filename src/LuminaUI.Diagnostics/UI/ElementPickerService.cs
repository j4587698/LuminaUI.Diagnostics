using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using LuminaUI.Diagnostics.Inspection;

namespace LuminaUI.Diagnostics.UI;

public sealed class ElementPickerService : IDisposable
{
    private readonly NodeRegistry _nodeRegistry;
    private static HighlightAdorner? _currentAdorner;
    private static AdornerLayer? _currentAdornerLayer;
    private static Avalonia.Visual? _currentTarget;
    private bool _isEnabled;
    private bool _disposed;

    public ElementPickerService(NodeRegistry nodeRegistry)
    {
        _nodeRegistry = nodeRegistry ?? throw new ArgumentNullException(nameof(nodeRegistry));
    }

    public event EventHandler<object>? ElementPicked;

    public void Enable()
    {
        if (_isEnabled)
            return;

        _isEnabled = true;
        var roots = InspectionRequestHelpers.GetCurrentWindowRoots();
        foreach (var root in roots)
        {
            if (root.GetType().Name == "LuminaDevToolsWindow")
                continue;

            root.AddHandler(InputElement.PointerMovedEvent, OnPointerMoved, RoutingStrategies.Tunnel);
            root.AddHandler(InputElement.PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
        }
    }

    public void Disable()
    {
        if (!_isEnabled)
            return;

        _isEnabled = false;
        var roots = InspectionRequestHelpers.GetCurrentWindowRoots();
        foreach (var root in roots)
        {
            root.RemoveHandler(InputElement.PointerMovedEvent, OnPointerMoved);
            root.RemoveHandler(InputElement.PointerPressedEvent, OnPointerPressed);
        }

        Highlight(null);
    }

    public void Highlight(Avalonia.Visual? target)
    {
        if (ReferenceEquals(target, _currentTarget) && _currentAdorner is not null)
            return;

        RemoveAdorner();

        if (target is null)
            return;

        var adornerLayer = AdornerLayer.GetAdornerLayer(target);
        if (adornerLayer is null)
            return;

        _currentAdorner = new HighlightAdorner { Target = target };
        adornerLayer.Children.Add(_currentAdorner);
        _currentAdornerLayer = adornerLayer;
        _currentTarget = target;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        if (_isEnabled)
            Disable();

        RemoveAdorner();
    }

    public static Control? ResolveTargetControl(Avalonia.Visual? visual, bool deep = false)
    {
        if (visual is null)
            return null;

        var control = visual as Control
            ?? visual.GetVisualAncestors().OfType<Control>().FirstOrDefault();
        if (control is null)
            return null;

        if (deep)
            return control;

        // Walk up TemplatedParent chain so clicking inside a control template (like a TextBox's TextPresenter,
        // ScrollViewer, Border, or a Button's ContentPresenter/TextBlock) targets the authored control itself.
        while (control is Avalonia.StyledElement { TemplatedParent: Control templatedParent })
        {
            control = templatedParent;
        }

        return control;
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isEnabled)
            return;

        if (sender is Window win && win.GetType().Name == "LuminaDevToolsWindow")
            return;

        if (e.Source is Avalonia.Visual visual)
        {
            var deep = e.KeyModifiers.HasFlag(KeyModifiers.Alt) || e.KeyModifiers.HasFlag(KeyModifiers.Shift);
            var target = ResolveTargetControl(visual, deep) ?? visual;
            Highlight(target);
        }

        e.Handled = true;
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!_isEnabled)
            return;

        if (sender is Window win && win.GetType().Name == "LuminaDevToolsWindow")
            return;

        if (e.Source is Avalonia.Visual visual)
        {
            var deep = e.KeyModifiers.HasFlag(KeyModifiers.Alt) || e.KeyModifiers.HasFlag(KeyModifiers.Shift);
            var target = ResolveTargetControl(visual, deep) ?? visual;
            ElementPicked?.Invoke(this, target);
        }

        e.Handled = true;
        Disable();
    }

    private static void RemoveAdorner()
    {
        if (_currentAdornerLayer is not null && _currentAdorner is not null)
        {
            _currentAdornerLayer.Children.Remove(_currentAdorner);
            _currentAdorner = null;
            _currentAdornerLayer = null;
        }

        _currentTarget = null;
    }
}
