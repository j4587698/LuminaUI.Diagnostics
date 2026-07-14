using System.Collections;
using System.Reflection;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using LuminaUI.Diagnostics.Abstractions;
using LuminaUI.Diagnostics.Controls;
using LuminaUI.Diagnostics.Dispatch;
using LuminaUI.Diagnostics.Inspection;
using LuminaUI.Diagnostics.Threading;

namespace LuminaUI.Diagnostics.Binding;

public sealed class BindingExpressionInspectionHandler : IDiagnosticToolHandler
{
    private readonly IUiThreadInvoker _invoker;
    private readonly IControlResolver _controlResolver;
    private readonly NodeRegistry _nodeRegistry;
    private readonly Func<IReadOnlyList<Control>> _getRoots;

    public BindingExpressionInspectionHandler(
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

    public string Method => LuminaUIDiagnosticsToolNames.GetBindingExpressions;

    public Task<DiagnosticResponse> HandleAsync(
        DiagnosticRequest request,
        CancellationToken cancellationToken = default) =>
        _invoker.InvokeAsync(
            request,
            _ => Task.FromResult(HandleOnUiThread(request)),
            cancellationToken);

    private DiagnosticResponse HandleOnUiThread(DiagnosticRequest request)
    {
        var lookup = InspectionRequestHelpers.ResolveControl(request, _getRoots(), _controlResolver, _nodeRegistry);
        if (!lookup.Success)
            return lookup.Response!;

        var expressions = BindingExpressionInspector.Inspect(lookup.Control!);
        return DiagnosticResponse.Ok(
            request.Id,
            new JsonObject
            {
                ["count"] = expressions.Count,
                ["expressions"] = new JsonArray(expressions.Select(expression => Serialize(expression)).ToArray())
            });
    }

    private static JsonNode Serialize(BindingExpressionSnapshot expression) => new JsonObject
    {
        ["property"] = expression.Property,
        ["expressionType"] = expression.ExpressionType,
        ["description"] = expression.Description,
        ["value"] = expression.Value,
        ["error"] = expression.Error,
        ["priority"] = expression.Priority,
        ["details"] = new JsonArray(expression.Details
            .Select(detail => (JsonNode?)JsonValue.Create(detail))
            .ToArray())
    };
}

internal static class BindingExpressionInspector
{
    private static readonly string[] DetailPropertyNames =
    [
        "Source", "Path", "Mode", "Converter", "ConverterParameter", "FallbackValue",
        "TargetNullValue", "UpdateSourceTrigger", "Priority", "ErrorType", "IsRunning"
    ];

    public static IReadOnlyList<BindingExpressionSnapshot> Inspect(AvaloniaObject target)
    {
        var result = new List<BindingExpressionSnapshot>();
        foreach (var property in RegisteredPropertyCatalog.GetUnique(target))
        {
            BindingExpressionBase? expression;
            try { expression = BindingOperations.GetBindingExpressionBase(target, property); }
            catch (Exception) { continue; }
            if (expression is null)
                continue;

            result.Add(ReadExpression(property.Name, expression));
        }

        return result;
    }

    private static BindingExpressionSnapshot ReadExpression(string property, BindingExpressionBase expression)
    {
        var type = expression.GetType();
        var details = new List<string>();
        foreach (var name in DetailPropertyNames)
        {
            if (TryReadProperty(expression, type, name, out var detailValue))
                details.Add($"{name}: {Format(detailValue)}");
        }

        if (TryReadProperty(expression, type, "Bindings", out var bindings) && bindings is IEnumerable enumerable)
        {
            var index = 0;
            foreach (var binding in enumerable)
                details.Add($"Binding[{index++}]: {binding}");
        }

        var description = TryReadProperty(expression, type, "Description", out var descriptionValue)
            ? Format(descriptionValue)
            : expression.ToString() ?? type.Name;
        var error = TryReadProperty(expression, type, "ErrorType", out var errorValue)
            && !string.Equals(Format(errorValue), "None", StringComparison.OrdinalIgnoreCase)
                ? Format(errorValue)
                : string.Empty;
        var priority = TryReadProperty(expression, type, "Priority", out var priorityValue)
            ? Format(priorityValue)
            : string.Empty;

        var isRunning = !TryReadProperty(expression, type, "IsRunning", out var isRunningValue)
            || isRunningValue is not bool running
            || running;
        string currentValue;
        if (!isRunning)
        {
            currentValue = "(inactive)";
        }
        else try
        {
            currentValue = expression.GetType().GetMethod("GetValue", BindingFlags.Instance | BindingFlags.Public)
                ?.Invoke(expression, null)?.ToString() ?? "(null)";
        }
        catch (Exception ex)
        {
            currentValue = "(unavailable)";
            error = ex.InnerException?.Message ?? ex.Message;
        }

        return new BindingExpressionSnapshot(
            property,
            type.FullName ?? type.Name,
            description,
            currentValue,
            error,
            priority,
            details);
    }

    private static bool TryReadProperty(object instance, Type type, string name, out object? value)
    {
        value = null;
        try
        {
            var property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
            if (property is null || property.GetIndexParameters().Length > 0)
                return false;
            value = property.GetValue(instance);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static string Format(object? value) => value switch
    {
        null => "(null)",
        string text => text,
        _ => value.ToString() ?? value.GetType().Name
    };
}

internal sealed record BindingExpressionSnapshot(
    string Property,
    string ExpressionType,
    string Description,
    string Value,
    string Error,
    string Priority,
    IReadOnlyList<string> Details);
