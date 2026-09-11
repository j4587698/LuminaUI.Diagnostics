using System;
using System.Globalization;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LuminaUI.Diagnostics.Controls;
using LuminaUI.Diagnostics.Dispatch;
using LuminaUI.Diagnostics.Inspection;
using LuminaUI.Diagnostics.Threading;
using LuminaUI.Diagnostics.Abstractions;

namespace LuminaUI.Diagnostics.Interaction;

public sealed class InteractionHandler : IDiagnosticToolHandler
{
    private readonly InteractionKind _kind;
    private readonly IUiThreadInvoker _invoker;
    private readonly IControlResolver _controlResolver;
    private readonly PropertyValueConverter _valueConverter;
    private readonly NodeRegistry _nodeRegistry;
    private readonly Func<IReadOnlyList<Control>> _getRoots;

    private InteractionHandler(
        InteractionKind kind,
        IUiThreadInvoker invoker,
        IControlResolver? controlResolver = null,
        PropertyValueConverter? valueConverter = null,
        NodeRegistry? nodeRegistry = null,
        Func<IReadOnlyList<Control>>? getRoots = null)
    {
        _kind = kind;
        _invoker = invoker ?? throw new ArgumentNullException(nameof(invoker));
        _controlResolver = controlResolver ?? new AvaloniaControlResolver();
        _valueConverter = valueConverter ?? new PropertyValueConverter();
        _nodeRegistry = nodeRegistry ?? new NodeRegistry();
        _getRoots = getRoots ?? InspectionRequestHelpers.GetCurrentWindowRoots;
    }

    public string Method =>
        _kind switch
        {
            InteractionKind.Click => LuminaUIDiagnosticsToolNames.ClickControl,
            InteractionKind.SetProperty => LuminaUIDiagnosticsToolNames.SetProperty,
            InteractionKind.InputText => LuminaUIDiagnosticsToolNames.InputText,
            InteractionKind.SendKeys => LuminaUIDiagnosticsToolNames.SendKeys,
            InteractionKind.InvokeCommand => LuminaUIDiagnosticsToolNames.InvokeCommand,
            InteractionKind.WaitForProperty => LuminaUIDiagnosticsToolNames.WaitForProperty,
            _ => throw new InvalidOperationException("Unknown interaction kind.")
        };

    public static InteractionHandler ClickControl(
        IUiThreadInvoker invoker,
        IControlResolver? controlResolver = null,
        NodeRegistry? nodeRegistry = null,
        Func<IReadOnlyList<Control>>? getRoots = null) =>
        new(InteractionKind.Click, invoker, controlResolver, nodeRegistry: nodeRegistry, getRoots: getRoots);

    public static InteractionHandler SetProperty(
        IUiThreadInvoker invoker,
        IControlResolver? controlResolver = null,
        PropertyValueConverter? valueConverter = null,
        NodeRegistry? nodeRegistry = null,
        Func<IReadOnlyList<Control>>? getRoots = null) =>
        new(InteractionKind.SetProperty, invoker, controlResolver, valueConverter, nodeRegistry, getRoots);

    public static InteractionHandler InputText(
        IUiThreadInvoker invoker,
        IControlResolver? controlResolver = null,
        NodeRegistry? nodeRegistry = null,
        Func<IReadOnlyList<Control>>? getRoots = null) =>
        new(InteractionKind.InputText, invoker, controlResolver, nodeRegistry: nodeRegistry, getRoots: getRoots);

    public static InteractionHandler SendKeys(
        IUiThreadInvoker invoker,
        IControlResolver? controlResolver = null,
        NodeRegistry? nodeRegistry = null,
        Func<IReadOnlyList<Control>>? getRoots = null) =>
        new(InteractionKind.SendKeys, invoker, controlResolver, nodeRegistry: nodeRegistry, getRoots: getRoots);

    public static InteractionHandler InvokeCommand(
        IUiThreadInvoker invoker,
        IControlResolver? controlResolver = null,
        NodeRegistry? nodeRegistry = null,
        Func<IReadOnlyList<Control>>? getRoots = null) =>
        new(InteractionKind.InvokeCommand, invoker, controlResolver, nodeRegistry: nodeRegistry, getRoots: getRoots);

    public static InteractionHandler WaitForProperty(
        IUiThreadInvoker invoker,
        IControlResolver? controlResolver = null,
        NodeRegistry? nodeRegistry = null,
        Func<IReadOnlyList<Control>>? getRoots = null) =>
        new(InteractionKind.WaitForProperty, invoker, controlResolver, nodeRegistry: nodeRegistry, getRoots: getRoots);

    public Task<DiagnosticResponse> HandleAsync(
        DiagnosticRequest request,
        CancellationToken cancellationToken = default) =>
        _invoker.InvokeAsync(
            request,
            token => HandleOnUiThreadAsync(request, token),
            cancellationToken);

    private Task<DiagnosticResponse> HandleOnUiThreadAsync(
        DiagnosticRequest request,
        CancellationToken cancellationToken) =>
        _kind switch
        {
            InteractionKind.Click => Task.FromResult(Click(request)),
            InteractionKind.SetProperty => Task.FromResult(SetProperty(request)),
            InteractionKind.InputText => InputTextAsync(request, cancellationToken),
            InteractionKind.SendKeys => Task.FromResult(SendKeys(request)),
            InteractionKind.InvokeCommand => Task.FromResult(InvokeCommand(request)),
            InteractionKind.WaitForProperty => WaitForPropertyAsync(request, cancellationToken),
            _ => Task.FromResult(
                DiagnosticResponse.Fail(
                    request.Id,
                    DiagnosticErrorCode.UnsupportedOperation,
                    "Unsupported interaction kind."))
        };

    private DiagnosticResponse Click(DiagnosticRequest request)
    {
        var lookup = ResolveRequiredControl(request);
        if (!lookup.Success)
            return lookup.Response!;

        var control = lookup.Control!;
        control.Focus();

        if (TryPointerClick(control))
        {
            return Ok(request, "pointerClicked");
        }

        // Fallback for controls that cannot receive synthesized pointer input
        // (not attached to a visual root, hit-test invisible, ...).
        if (control is Button button)
        {
            if (button.Command is null)
            {
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                return Ok(request, "clickRaised");
            }

            var command = button.Command;
            var parameter = button.CommandParameter;
            if (!command.CanExecute(parameter))
            {
                return DiagnosticResponse.Fail(
                    request.Id,
                    DiagnosticErrorCode.UnsupportedOperation,
                    "Button command cannot execute.");
            }

            command.Execute(parameter);
            return Ok(request, "commandExecuted");
        }

        if (TryToggleExpandableControl(control, out var expanded))
        {
            return Ok(request, expanded ? "expanded" : "collapsed");
        }

        var commandResponse = TryExecuteCommand(request, control);
        if (commandResponse is not null)
        {
            return commandResponse;
        }

        if (TryRaiseStaticRoutedEvent(control, "InvokedEvent"))
        {
            return Ok(request, "invokedRaised");
        }

        return Ok(request, "focused");
    }

    private DiagnosticResponse SetProperty(DiagnosticRequest request)
    {
        var lookup = ResolveRequiredControl(request);
        if (!lookup.Success)
            return lookup.Response!;

        var propertyName = InspectionRequestHelpers.GetString(request.Parameters, "propertyName");
        var rawValue = InspectionRequestHelpers.GetString(request.Parameters, "value");
        var clearLocalValue = InspectionRequestHelpers.GetBool(request.Parameters, "clearLocalValue");
        if (string.IsNullOrWhiteSpace(propertyName))
        {
            return DiagnosticResponse.Fail(
                request.Id,
                DiagnosticErrorCode.InvalidRequest,
                "Parameter 'propertyName' is required.");
        }

        var property = FindAvaloniaProperty(lookup.Control!, propertyName);
        if (property is null)
        {
            return DiagnosticResponse.Fail(
                request.Id,
                DiagnosticErrorCode.UnsupportedOperation,
                $"Avalonia property '{propertyName}' was not found.");
        }

        if (clearLocalValue)
        {
            if (_valueConverter.TryClearValue(property, lookup.Control!, out _))
                return Ok(request, "propertyCleared");

            return DiagnosticResponse.Fail(
                request.Id,
                DiagnosticErrorCode.ConversionFailed,
                $"Could not clear value for property '{propertyName}'.");
        }

        if (rawValue is null)
        {
            return DiagnosticResponse.Fail(
                request.Id,
                DiagnosticErrorCode.InvalidRequest,
                "Parameter 'value' is required unless 'clearLocalValue' is true.");
        }

        if (!_valueConverter.TryConvert(rawValue, property.PropertyType, out var converted, out var error))
        {
            return DiagnosticResponse.Fail(
                request.Id,
                DiagnosticErrorCode.ConversionFailed,
                error ?? $"Could not convert value to {property.PropertyType.Name}.");
        }

        lookup.Control!.SetValue(property, converted);
        return Ok(request, "propertySet");
    }

    private async Task<DiagnosticResponse> InputTextAsync(
        DiagnosticRequest request,
        CancellationToken cancellationToken)
    {
        var lookup = ResolveRequiredControl(request);
        if (!lookup.Success)
            return lookup.Response!;

        var text = InspectionRequestHelpers.GetString(request.Parameters, "text") ?? "";
        var pressEnter = InspectionRequestHelpers.GetBool(request.Parameters, "pressEnter");
        var textBox = lookup.Control as TextBox;
        if (textBox is not null && !IsEditableTextBox(textBox))
        {
            return DiagnosticResponse.Fail(
                request.Id,
                DiagnosticErrorCode.UnsupportedOperation,
                "Target TextBox is not editable.");
        }

        textBox ??= AvaloniaControlResolver.EnumerateControls(lookup.Control!)
            .OfType<TextBox>()
            .FirstOrDefault(IsEditableTextBox);
        textBox ??= FindEditableTextBoxInMatchingType(request, lookup);

        if (textBox is null)
        {
            // No editable TextBox: fall back to key/text-input injection so
            // custom-drawn controls (terminals, canvases) can receive text.
            return SendKeysCore(request, lookup.Control!, text, pressEnter, cancellationToken);
        }

        textBox.Focus();
        textBox.Text = text;

        var enterPressed = false;
        if (pressEnter)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Dispatcher.UIThread.InvokeAsync(() => RaiseReturnKey(textBox), DispatcherPriority.Background);
            cancellationToken.ThrowIfCancellationRequested();
            enterPressed = true;
        }

        return DiagnosticResponse.Ok(
            request.Id,
            new JsonObject
            {
                ["status"] = "textSet",
                ["pressEnterRequested"] = pressEnter,
                ["enterPressed"] = enterPressed
            });
    }

    private DiagnosticResponse SendKeys(DiagnosticRequest request)
    {
        var lookup = ResolveRequiredControl(request);
        if (!lookup.Success)
            return lookup.Response!;

        var text = InspectionRequestHelpers.GetString(request.Parameters, "text");
        if (text is null)
        {
            return DiagnosticResponse.Fail(
                request.Id,
                DiagnosticErrorCode.InvalidRequest,
                "Parameter 'text' is required.");
        }

        return SendKeysCore(request, lookup.Control!, text, pressEnter: false, CancellationToken.None);
    }

    private static DiagnosticResponse SendKeysCore(
        DiagnosticRequest request,
        Control target,
        string text,
        bool pressEnter,
        CancellationToken cancellationToken)
    {
        target.Focus();

        cancellationToken.ThrowIfCancellationRequested();
        var characters = SendText(target, text);

        var enterPressed = false;
        if (pressEnter)
        {
            RaiseReturnKey(target);
            enterPressed = true;
        }

        return DiagnosticResponse.Ok(
            request.Id,
            new JsonObject
            {
                ["status"] = "keysSent",
                ["characters"] = characters,
                ["pressEnterRequested"] = pressEnter,
                ["enterPressed"] = enterPressed
            });
    }

    private static int SendText(
        IInputElement target,
        string text)
    {
        var count = 0;
        var index = 0;
        while (index < text.Length)
        {
            var length = char.IsHighSurrogate(text[index]) && index + 1 < text.Length ? 2 : 1;
            var chunk = text.Substring(index, length);
            index += length;

            var key = MapCharToKey(chunk[0]);
            if (key != Key.None)
            {
                target.RaiseEvent(new KeyEventArgs
                {
                    RoutedEvent = InputElement.KeyDownEvent,
                    Source = target,
                    Key = key,
                    KeyModifiers = KeyModifiers.None
                });
            }

            target.RaiseEvent(new TextInputEventArgs
            {
                RoutedEvent = InputElement.TextInputEvent,
                Source = target,
                Text = chunk
            });

            if (key != Key.None)
            {
                target.RaiseEvent(new KeyEventArgs
                {
                    RoutedEvent = InputElement.KeyUpEvent,
                    Source = target,
                    Key = key,
                    KeyModifiers = KeyModifiers.None
                });
            }

            count++;
        }

        return count;
    }

    private static Key MapCharToKey(char c) =>
        c switch
        {
            >= 'a' and <= 'z' => Key.A + (c - 'a'),
            >= 'A' and <= 'Z' => Key.A + (c - 'A'),
            >= '0' and <= '9' => Key.D0 + (c - '0'),
            ' ' => Key.Space,
            '\r' or '\n' => Key.Return,
            '\t' => Key.Tab,
            _ => Key.None
        };

    private static bool TryPointerClick(Control control)
    {
        if (!control.IsEffectivelyEnabled
            || !control.IsEffectivelyVisible
            || !control.IsHitTestVisible)
        {
            return false;
        }

        if (TopLevel.GetTopLevel(control) is not { } rootVisual)
            return false;

        var bounds = control.Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return false;

        var rootPosition = control.TranslatePoint(
            new Point(bounds.Width / 2, bounds.Height / 2),
            rootVisual);
        if (rootPosition is null)
            return false;

        var pointer = new Avalonia.Input.Pointer(
            Avalonia.Input.Pointer.GetNextFreeId(),
            PointerType.Mouse,
            isPrimary: true);
        var timestamp = (ulong)Math.Max(0, Environment.TickCount64);

        control.RaiseEvent(new PointerPressedEventArgs(
            control,
            pointer,
            rootVisual,
            rootPosition.Value,
            timestamp,
            new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed),
            KeyModifiers.None));

        control.RaiseEvent(new PointerReleasedEventArgs(
            control,
            pointer,
            rootVisual,
            rootPosition.Value,
            timestamp + 1,
            new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased),
            KeyModifiers.None,
            MouseButton.Left));

        return true;
    }

    private DiagnosticResponse InvokeCommand(DiagnosticRequest request)
    {
        var lookup = ResolveRequiredControl(request);
        if (!lookup.Success)
            return lookup.Response!;

        var commandName = InspectionRequestHelpers.GetString(request.Parameters, "commandName");
        var parameter = InspectionRequestHelpers.GetString(request.Parameters, "parameter");
        if (string.IsNullOrWhiteSpace(commandName))
        {
            return DiagnosticResponse.Fail(
                request.Id,
                DiagnosticErrorCode.InvalidRequest,
                "Parameter 'commandName' is required.");
        }

        var dataContext = lookup.Control!.DataContext;
        var command = dataContext?.GetType()
            .GetProperty(commandName, BindingFlags.Instance | BindingFlags.Public)
            ?.GetValue(dataContext) as ICommand;

        if (command is null)
        {
            return DiagnosticResponse.Fail(
                request.Id,
                DiagnosticErrorCode.UnsupportedOperation,
                $"Command '{commandName}' was not found.");
        }

        if (!command.CanExecute(parameter))
        {
            return DiagnosticResponse.Fail(
                request.Id,
                DiagnosticErrorCode.UnsupportedOperation,
                $"Command '{commandName}' cannot execute.");
        }

        command.Execute(parameter);
        return Ok(request, "commandExecuted");
    }

    private async Task<DiagnosticResponse> WaitForPropertyAsync(
        DiagnosticRequest request,
        CancellationToken cancellationToken)
    {
        var lookup = ResolveOptionalControl(request);
        if (!lookup.Success)
            return lookup.Response!;

        var propertyName = InspectionRequestHelpers.GetString(request.Parameters, "propertyName");
        var expectedValue = InspectionRequestHelpers.GetString(request.Parameters, "expectedValue");
        if (string.IsNullOrWhiteSpace(propertyName))
            return DiagnosticResponse.Fail(request.Id, DiagnosticErrorCode.InvalidRequest, "Parameter 'propertyName' is required.");

        var timeoutMs = Math.Clamp(
            InspectionRequestHelpers.GetInt(request.Parameters, "timeoutMs", request.TimeoutMs),
            1,
            600_000);
        var pollIntervalMs = Math.Clamp(
            InspectionRequestHelpers.GetInt(request.Parameters, "pollIntervalMs", 500),
            10,
            10_000);
        var deadline = DateTimeOffset.UtcNow.AddMilliseconds(timeoutMs);

        while (DateTimeOffset.UtcNow <= deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = ReadPropertyValue(lookup.Control!, propertyName);
            var text = Convert.ToString(current, CultureInfo.InvariantCulture);

            if (string.Equals(text, expectedValue, StringComparison.OrdinalIgnoreCase))
            {
                return DiagnosticResponse.Ok(
                    request.Id,
                    new JsonObject
                    {
                        ["status"] = "matched",
                        ["propertyName"] = propertyName,
                        ["value"] = text
                    });
            }

            await Task.Delay(pollIntervalMs, cancellationToken);
        }

        return DiagnosticResponse.Fail(
            request.Id,
            DiagnosticErrorCode.UiThreadTimeout,
            $"Property '{propertyName}' did not reach expected value within {timeoutMs} ms.");
    }

    private ControlLookup ResolveRequiredControl(DiagnosticRequest request) =>
        InspectionRequestHelpers.ResolveControl(request, _getRoots(), _controlResolver, _nodeRegistry);

    private ControlLookup ResolveOptionalControl(DiagnosticRequest request)
    {
        if (!string.IsNullOrWhiteSpace(InspectionRequestHelpers.GetString(request.Parameters, "controlId")))
            return ResolveRequiredControl(request);

        if (!string.IsNullOrWhiteSpace(InspectionRequestHelpers.GetString(request.Parameters, "nodeId")))
            return ResolveRequiredControl(request);

        var roots = _getRoots();
        return roots.Count == 0
            ? ControlLookup.Failed(
                DiagnosticResponse.Fail(
                    request.Id,
                    DiagnosticErrorCode.TargetNotFound,
                    "No root controls are available."))
            : ControlLookup.Found(roots[0], rootIndex: 0, controlId: "");
    }

    private static AvaloniaProperty? FindAvaloniaProperty(
        Control control,
        string propertyName) =>
        RegisteredPropertyCatalog.Find(control, propertyName);

    private static object? ReadPropertyValue(
        Control control,
        string propertyName)
    {
        var avaloniaProperty = FindAvaloniaProperty(control, propertyName);
        if (avaloniaProperty is not null)
            return control.GetValue(avaloniaProperty);

        var clrProperty = control.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
        if (clrProperty is { } && clrProperty.GetIndexParameters().Length == 0)
            return clrProperty.GetValue(control);

        var dataContext = control.DataContext;
        var dataContextProperty = dataContext?.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
        return dataContextProperty?.GetIndexParameters().Length == 0
            ? dataContextProperty.GetValue(dataContext)
            : null;
    }

    private static DiagnosticResponse? TryExecuteCommand(
        DiagnosticRequest request,
        Control control)
    {
        var command = control.GetType()
            .GetProperty("Command", BindingFlags.Instance | BindingFlags.Public)
            ?.GetValue(control) as ICommand;
        if (command is null)
        {
            return null;
        }

        var parameter = control.GetType()
            .GetProperty("CommandParameter", BindingFlags.Instance | BindingFlags.Public)
            ?.GetValue(control);
        if (!command.CanExecute(parameter))
        {
            return DiagnosticResponse.Fail(
                request.Id,
                DiagnosticErrorCode.UnsupportedOperation,
                "Control command cannot execute.");
        }

        command.Execute(parameter);
        return Ok(request, "commandExecuted");
    }

    private static bool TryToggleExpandableControl(
        Control control,
        out bool expanded)
    {
        expanded = false;

        var isExpandedProperty = control.GetType()
            .GetProperty("IsExpanded", BindingFlags.Instance | BindingFlags.Public);
        if (isExpandedProperty is not { PropertyType: { } propertyType } || propertyType != typeof(bool) || !isExpandedProperty.CanWrite)
        {
            return false;
        }

        var hasNavigationChildren = control.GetType()
            .GetMethod("HasNavigationChildren", BindingFlags.Instance | BindingFlags.Public)
            ?.Invoke(control, null) as bool?;
        if (hasNavigationChildren != true)
        {
            return false;
        }

        expanded = !((bool?)isExpandedProperty.GetValue(control) ?? false);
        isExpandedProperty.SetValue(control, expanded);
        return true;
    }

    private static bool TryRaiseStaticRoutedEvent(
        Control control,
        string fieldName)
    {
        var routedEvent = control.GetType()
            .GetField(fieldName, BindingFlags.Static | BindingFlags.Public | BindingFlags.FlattenHierarchy)
            ?.GetValue(null) as RoutedEvent;
        if (routedEvent is null)
        {
            return false;
        }

        control.RaiseEvent(new RoutedEventArgs(routedEvent, control));
        return true;
    }

    private static bool IsEditableTextBox(TextBox textBox) =>
        textBox.IsVisible && textBox.IsEnabled && !textBox.IsReadOnly;

    private TextBox? FindEditableTextBoxInMatchingType(
        DiagnosticRequest request,
        ControlLookup lookup)
    {
        var controlId = InspectionRequestHelpers.GetString(request.Parameters, "controlId");
        if (!ControlIdentifierParser.TryParse(controlId, out var identifier, out _)
            || identifier.Kind != ControlIdentifierKind.Type)
        {
            return null;
        }

        var roots = _getRoots();
        if (lookup.RootIndex < 0 || lookup.RootIndex >= roots.Count)
        {
            return null;
        }

        foreach (var control in AvaloniaControlResolver.EnumerateControls(roots[lookup.RootIndex]))
        {
            if (!IsTypeMatch(control, identifier.Value))
                continue;

            var textBox = control as TextBox
                ?? AvaloniaControlResolver.EnumerateControls(control)
                    .OfType<TextBox>()
                    .FirstOrDefault(IsEditableTextBox);
            if (textBox is not null && IsEditableTextBox(textBox))
            {
                return textBox;
            }
        }

        return null;
    }

    private static bool IsTypeMatch(
        Control control,
        string typeName)
    {
        var type = control.GetType();
        return string.Equals(type.Name, typeName, StringComparison.Ordinal)
            || string.Equals(type.FullName, typeName, StringComparison.Ordinal);
    }

    private static void RaiseReturnKey(IInputElement target)
    {
        target.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Source = target,
            Key = Key.Return,
            KeyModifiers = KeyModifiers.None
        });
        target.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyUpEvent,
            Source = target,
            Key = Key.Return,
            KeyModifiers = KeyModifiers.None
        });
    }

    private static DiagnosticResponse Ok(
        DiagnosticRequest request,
        string status) =>
        DiagnosticResponse.Ok(
            request.Id,
            new JsonObject { ["status"] = status });

    private enum InteractionKind
    {
        Click,
        SetProperty,
        InputText,
        SendKeys,
        InvokeCommand,
        WaitForProperty
    }
}
