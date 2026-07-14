using System.Reflection;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using LuminaUI.Diagnostics.Controls;
using LuminaUI.Diagnostics.Dispatch;
using LuminaUI.Diagnostics.Serialization;
using LuminaUI.Diagnostics.Threading;
using LuminaUI.Diagnostics.Abstractions;

namespace LuminaUI.Diagnostics.Inspection;

public sealed class PropertyInspectionHandler : IDiagnosticToolHandler
{
    private readonly IUiThreadInvoker _invoker;
    private readonly IControlResolver _controlResolver;
    private readonly ValueFormatter _formatter;
    private readonly NodeRegistry _nodeRegistry;
    private readonly Func<IReadOnlyList<Control>> _getRoots;

    public PropertyInspectionHandler(
        IUiThreadInvoker invoker,
        IControlResolver? controlResolver = null,
        ValueFormatter? formatter = null,
        NodeRegistry? nodeRegistry = null,
        Func<IReadOnlyList<Control>>? getRoots = null)
    {
        _invoker = invoker ?? throw new ArgumentNullException(nameof(invoker));
        _controlResolver = controlResolver ?? new AvaloniaControlResolver();
        _formatter = formatter ?? new ValueFormatter();
        _nodeRegistry = nodeRegistry ?? new NodeRegistry();
        _getRoots = getRoots ?? InspectionRequestHelpers.GetCurrentWindowRoots;
    }

    public string Method => LuminaUIDiagnosticsToolNames.GetControlProperties;

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

        var propertyNames = InspectionRequestHelpers.GetStringArray(request.Parameters, "propertyNames");
        var filters = propertyNames.Length == 0
            ? null
            : new HashSet<string>(propertyNames, StringComparer.Ordinal);
        var includeClrProperties = InspectionRequestHelpers.GetBool(request.Parameters, "includeClrProperties", defaultValue: false);
        var contextualProperties = InspectionRequestHelpers.GetBool(request.Parameters, "contextualProperties", defaultValue: false);

        return DiagnosticResponse.Ok(
            request.Id,
            new JsonObject
            {
                ["rootIndex"] = lookup.RootIndex,
                ["nodeId"] = _nodeRegistry.RegisterNode(lookup.Control!),
                ["type"] = lookup.Control!.GetType().FullName,
                ["name"] = lookup.Control.Name,
                ["avaloniaProperties"] = ReadAvaloniaProperties(lookup.Control, filters, contextualProperties),
                ["clrProperties"] = includeClrProperties || filters is not null
                    ? ReadClrProperties(lookup.Control, filters)
                    : new JsonArray()
            });
    }

    private JsonArray ReadAvaloniaProperties(
        Control control,
        HashSet<string>? filters,
        bool contextualProperties)
    {
        var properties = RegisteredPropertyCatalog
            .GetUnique(control)
            .Where(property => filters is null || filters.Contains(property.Name))
            .Where(property => !contextualProperties || ShouldIncludeProperty(control, property))
            .ToArray();

        var result = new JsonArray();
        foreach (var property in properties)
            result.Add(ReadAvaloniaProperty(control, property));

        return result;
    }

    private JsonObject ReadAvaloniaProperty(
        Control control,
        AvaloniaProperty property)
    {
        var json = new JsonObject
        {
            ["name"] = property.Name,
            ["ownerType"] = property.OwnerType.FullName,
            ["propertyType"] = property.PropertyType.FullName,
            ["isAttached"] = property.IsAttached,
            ["isDirect"] = property.IsDirect,
            ["isReadOnly"] = IsReadOnly(property),
            ["isSet"] = control.IsSet(property)
        };

        try
        {
            json["value"] = _formatter.Format(
                control.GetValue(property),
                new ValueFormatOptions { MaxStringLength = 160, MaxEnumerableItems = 10, MaxDepth = 1 });
        }
        catch (Exception ex)
        {
            json["error"] = ex.Message;
        }

        return json;
    }

    private JsonArray ReadClrProperties(
        Control control,
        HashSet<string>? filters)
    {
        var result = new JsonArray();
        var type = control.GetType();

        var properties = filters is null
            ? type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(property => property.CanRead && property.GetIndexParameters().Length == 0)
            : filters.Select(propertyName => type.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public))
                .OfType<PropertyInfo>();

        foreach (var property in properties.OrderBy(property => property.Name, StringComparer.Ordinal))
        {
            if (RegisteredPropertyCatalog.Find(control, property.Name) is not null)
                continue;

            var json = new JsonObject
            {
                ["name"] = property.Name,
                ["declaringType"] = property.DeclaringType?.FullName,
                ["propertyType"] = property.PropertyType.FullName
            };

            try
            {
                json["value"] = _formatter.Format(
                    property.GetValue(control),
                    new ValueFormatOptions { MaxStringLength = 160, MaxEnumerableItems = 10, MaxDepth = 1 });
            }
            catch (Exception ex)
            {
                json["error"] = ex.InnerException?.Message ?? ex.Message;
            }

            result.Add(json);
        }

        return result;
    }

    private static bool ShouldIncludeProperty(Control control, AvaloniaProperty property)
    {
        if (!property.IsAttached || control.IsSet(property))
            return true;

        var parentType = control.Parent?.GetType();
        return parentType is not null && property.OwnerType.IsAssignableFrom(parentType);
    }

    private static bool IsReadOnly(AvaloniaProperty property)
    {
        try
        {
            return property.IsReadOnly;
        }
        catch
        {
            return false;
        }
    }
}
