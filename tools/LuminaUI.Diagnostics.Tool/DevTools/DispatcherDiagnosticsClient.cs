using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using LuminaUI.Diagnostics.Abstractions;
using LuminaUI.Diagnostics.Dispatch;
using LuminaUI.Diagnostics.Inspection;
using LuminaUI.Diagnostics.Interaction;
using LuminaUI.Diagnostics.UI.ViewModels;

namespace LuminaUI.Diagnostics.UI;

public sealed class DispatcherDiagnosticsClient : IDiagnosticsClient, IDisposable
{
    private static readonly TimeSpan PickerPollInterval = TimeSpan.FromMilliseconds(33);
    private readonly Func<DiagnosticRequest, CancellationToken, Task<DiagnosticResponse>> _sendAsync;
    private readonly NodeRegistry? _nodeRegistry;
    private readonly ElementPickerService? _pickerService;
    private CancellationTokenSource? _pickerPolling;

    public DispatcherDiagnosticsClient(DiagnosticDispatcher dispatcher, NodeRegistry nodeRegistry)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        _sendAsync = dispatcher.DispatchAsync;
        _nodeRegistry = nodeRegistry ?? throw new ArgumentNullException(nameof(nodeRegistry));
        _pickerService = new ElementPickerService(nodeRegistry);
        _pickerService.ElementPicked += OnPickerElementPicked;
    }

    public DispatcherDiagnosticsClient(
        Func<DiagnosticRequest, CancellationToken, Task<DiagnosticResponse>> sendAsync)
    {
        _sendAsync = sendAsync ?? throw new ArgumentNullException(nameof(sendAsync));
    }

    public event EventHandler<object>? ElementPicked;

    public async Task<IReadOnlyList<VisualTreeNodeViewModel>> GetVisualTreeAsync(CancellationToken cancellationToken = default)
    {
        var windows = await ListWindowsAsync(cancellationToken);
        var roots = new List<VisualTreeNodeViewModel>();

        for (var i = 0; i < windows.Count; i++)
        {
            var request = new DiagnosticRequest(
                id: Guid.NewGuid().ToString("N"),
                method: LuminaUIDiagnosticsToolNames.GetVisualTree,
                parameters: new JsonObject { ["windowIndex"] = i, ["maxDepth"] = DevToolsSettingsStore.Current.MaxTreeDepth },
                timeoutMs: 10000);
            var response = await _sendAsync(request, cancellationToken);
            if (response.Success && response.Data is JsonObject json && json["root"] is JsonObject root)
            {
                if (root["type"]?.GetValue<string>() != "LuminaDevToolsWindow")
                    roots.Add(BuildNodeFromJson(root));
            }
        }

        return roots;
    }

    public async Task<IReadOnlyList<VisualTreeNodeViewModel>> GetLogicalTreeAsync(CancellationToken cancellationToken = default)
    {
        var windows = await ListWindowsAsync(cancellationToken);
        var roots = new List<VisualTreeNodeViewModel>();

        for (var i = 0; i < windows.Count; i++)
        {
            var request = new DiagnosticRequest(
                id: Guid.NewGuid().ToString("N"),
                method: LuminaUIDiagnosticsToolNames.GetLogicalTree,
                parameters: new JsonObject { ["windowIndex"] = i, ["maxDepth"] = DevToolsSettingsStore.Current.MaxTreeDepth },
                timeoutMs: 10000);
            var response = await _sendAsync(request, cancellationToken);
            if (response.Success && response.Data is JsonObject json && json["root"] is JsonObject root)
            {
                if (root["type"]?.GetValue<string>() != "LuminaDevToolsWindow")
                    roots.Add(BuildNodeFromJson(root));
            }
        }

        return roots;
    }

    private async Task<IReadOnlyList<JsonObject>> ListWindowsAsync(CancellationToken cancellationToken)
    {
        var request = new DiagnosticRequest(
            id: Guid.NewGuid().ToString("N"),
            method: LuminaUIDiagnosticsToolNames.ListWindows,
            timeoutMs: 5000);
        var response = await _sendAsync(request, cancellationToken);
        if (response.Success && response.Data is JsonObject json && json["windows"] is JsonArray arr)
            return arr.OfType<JsonObject>().ToList();
        return [];
    }

    public async Task HighlightElementAsync(VisualTreeNodeViewModel? node, CancellationToken cancellationToken = default)
    {
        if (_pickerService is not null)
        {
            _pickerService.Highlight(node?.Control as Avalonia.Visual);
            return;
        }

        var request = new DiagnosticRequest(
            id: Guid.NewGuid().ToString("N"),
            method: LuminaUIDiagnosticsToolNames.HighlightElement,
            parameters: new JsonObject { ["nodeId"] = node?.NodeId },
            timeoutMs: 5000);
        await _sendAsync(request, cancellationToken);
    }

    public async Task<IReadOnlyList<PropertyViewModel>> GetPropertiesAsync(VisualTreeNodeViewModel node, CancellationToken cancellationToken = default)
    {
        var nodeId = GetNodeId(node);
        if (nodeId is null)
            return [];

        var request = new DiagnosticRequest(
            id: Guid.NewGuid().ToString("N"),
            method: LuminaUIDiagnosticsToolNames.GetControlProperties,
            parameters: new JsonObject
            {
                ["nodeId"] = nodeId,
                ["includeClrProperties"] = DevToolsSettingsStore.Current.IncludeClrProperties,
                ["contextualProperties"] = DevToolsSettingsStore.Current.ContextualProperties
            },
            timeoutMs: 5000);
        var response = await _sendAsync(request, cancellationToken);
        if (!response.Success || response.Data is not JsonObject json)
            return [];

        var properties = new List<PropertyViewModel>();
        if (json["avaloniaProperties"] is JsonArray ap)
        {
            foreach (var prop in ap.OfType<JsonObject>())
                properties.Add(BuildPropertyViewModel(node.Control as Control, nodeId, prop));
        }
        if (json["clrProperties"] is JsonArray cp)
        {
            foreach (var prop in cp.OfType<JsonObject>())
                properties.Add(BuildPropertyViewModel(node.Control as Control, nodeId, prop));
        }
        return properties;
    }

    public async Task<IReadOnlyList<ResourceProviderViewModel>> GetResourceProvidersAsync(
        CancellationToken cancellationToken = default)
    {
        var request = new DiagnosticRequest(
            id: Guid.NewGuid().ToString("N"),
            method: LuminaUIDiagnosticsToolNames.GetResources,
            parameters: new JsonObject { ["providersOnly"] = true },
            timeoutMs: 15000);
        var response = await _sendAsync(request, cancellationToken);
        if (!response.Success || response.Data is not JsonObject json || json["providers"] is not JsonArray providers)
            return [];

        return providers
            .OfType<JsonObject>()
            .Select(provider => new ResourceProviderViewModel(
                provider["source"]?.GetValue<string>() ?? string.Empty,
                ParseSourceSegments(provider["sourceSegments"]),
                provider["count"]?.GetValue<int>() ?? 0))
            .ToArray();
    }

    public async Task<IReadOnlyList<ResourceEntryViewModel>> GetResourcesAsync(
        string? source = null,
        VisualTreeNodeViewModel? node = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new JsonObject();
        if (!string.IsNullOrWhiteSpace(source))
            parameters["source"] = source;
        if (node is not null && GetNodeId(node) is { } nodeId)
            parameters["nodeId"] = nodeId;

        var request = new DiagnosticRequest(
            id: Guid.NewGuid().ToString("N"),
            method: LuminaUIDiagnosticsToolNames.GetResources,
            parameters: parameters,
            timeoutMs: 5000);
        var response = await _sendAsync(request, cancellationToken);
        if (!response.Success || response.Data is not JsonObject json)
            return [];

        var responseSource = json["source"]?.GetValue<string>() ?? "";
        var items = new List<ResourceEntryViewModel>();
        if (json["resources"] is JsonArray arr)
        {
            foreach (var entry in arr.OfType<JsonObject>())
            {
                var entryId = entry["entryId"]?.GetValue<string>();
                var sourceSegments = ParseSourceSegments(entry["sourceSegments"]);
                items.Add(new ResourceEntryViewModel(
                    entry["source"]?.GetValue<string>() ?? responseSource,
                    entry["key"]?.GetValue<string>() ?? "(null)",
                    entry["keyType"]?.GetValue<string>() ?? "",
                    entry["value"] is JsonObject v ? v["kind"]?.GetValue<string>() ?? "" : "",
                    entry["value"] is JsonObject vt ? vt["type"]?.GetValue<string>() ?? "" : "",
                    entry["value"] is JsonObject vv ? FormatValueForDisplay(vv) : "(null)",
                    string.IsNullOrWhiteSpace(entryId)
                        ? null
                        : value => SetResourceAsync(entryId, value),
                    sourceSegments,
                    entry["isDeferred"]?.GetValue<bool>() == true));
            }
        }
        return items.OrderBy(r => r.Key).ToArray();
    }

    private static IReadOnlyList<string> ParseSourceSegments(JsonNode? node) =>
        node is JsonArray sourcePath
            ? sourcePath.OfType<JsonValue>().Select(value => value.GetValue<string>()).ToArray()
            : [];

    public async Task<IReadOnlyList<AssetEntryViewModel>> GetAssetsAsync(CancellationToken cancellationToken = default)
    {
        var request = new DiagnosticRequest(
            id: Guid.NewGuid().ToString("N"),
            method: LuminaUIDiagnosticsToolNames.GetAssets,
            timeoutMs: 15000);
        var response = await _sendAsync(request, cancellationToken);
        if (!response.Success || response.Data is not JsonObject json || json["assets"] is not JsonArray assets)
            return [];

        return assets.OfType<JsonObject>()
            .Select(asset => new AssetEntryViewModel(
                asset["fileName"]?.GetValue<string>() ?? "",
                asset["relativePath"]?.GetValue<string>() ?? "",
                asset["assembly"]?.GetValue<string>() ?? "",
                asset["uri"]?.GetValue<string>() ?? "",
                asset["size"]?.GetValue<long?>()))
            .OrderBy(asset => asset.Assembly, StringComparer.OrdinalIgnoreCase)
            .ThenBy(asset => asset.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private async Task SetResourceAsync(string entryId, string value)
    {
        var request = new DiagnosticRequest(
            id: Guid.NewGuid().ToString("N"),
            method: LuminaUIDiagnosticsToolNames.SetResource,
            parameters: new JsonObject { ["entryId"] = entryId, ["value"] = value },
            timeoutMs: 5000);
        var response = await _sendAsync(request, CancellationToken.None);
        if (!response.Success)
            throw new InvalidOperationException(response.Error?.Message ?? "Failed to update resource.");
    }

    public async Task<StyleInspectionViewModel> GetAppliedStylesAsync(VisualTreeNodeViewModel? node, CancellationToken cancellationToken = default)
    {
        if (node is null || GetNodeId(node) is not { } nodeId)
            return StyleInspectionViewModel.Empty;

        var request = new DiagnosticRequest(
            id: Guid.NewGuid().ToString("N"),
            method: LuminaUIDiagnosticsToolNames.GetAppliedStyles,
            parameters: new JsonObject { ["nodeId"] = nodeId },
            timeoutMs: 5000);
        var response = await _sendAsync(request, cancellationToken);
        if (!response.Success || response.Data is not JsonObject json)
            return StyleInspectionViewModel.Empty;

        var classes = json["classes"] is JsonArray c ? string.Join(" ", c.OfType<JsonValue>().Select(v => v.GetValue<string>())) : "";
        return new StyleInspectionViewModel(
            json["type"]?.GetValue<string>() ?? node.Type,
            json["name"]?.GetValue<string>() ?? node.Name ?? "",
            classes,
            json["styleKey"]?.GetValue<string>() ?? "(default)",
            json["templatedParentType"]?.GetValue<string>() ?? "(none)",
            ParseStyleEntries(json["localStyles"]),
            [],
            []);
    }

    public async Task<IReadOnlyList<BindingErrorEntryViewModel>> GetBindingErrorsAsync(int maxItems = 100, CancellationToken cancellationToken = default)
    {
        var request = new DiagnosticRequest(
            id: Guid.NewGuid().ToString("N"),
            method: LuminaUIDiagnosticsToolNames.GetBindingErrors,
            parameters: new JsonObject { ["maxItems"] = maxItems },
            timeoutMs: 5000);
        var response = await _sendAsync(request, cancellationToken);
        if (!response.Success || response.Data is not JsonObject json)
            return [];

        var entries = new List<BindingErrorEntryViewModel>();
        foreach (var e in json["entries"] is JsonArray arr ? arr.OfType<JsonObject>() : [])
        {
            entries.Add(new BindingErrorEntryViewModel(
                e["timestamp"]?.GetValue<string>() ?? "",
                e["level"]?.GetValue<string>() ?? "",
                e["area"]?.GetValue<string>() ?? "",
                e["sourceType"]?.GetValue<string>() ?? "",
                e["message"]?.GetValue<string>() ?? ""));
        }
        return entries;
    }

    public async Task<IReadOnlyList<BindingExpressionEntryViewModel>> GetBindingExpressionsAsync(
        VisualTreeNodeViewModel? node,
        CancellationToken cancellationToken = default)
    {
        if (node is null || GetNodeId(node) is not { } nodeId)
            return [];

        var request = new DiagnosticRequest(
            id: Guid.NewGuid().ToString("N"),
            method: LuminaUIDiagnosticsToolNames.GetBindingExpressions,
            parameters: new JsonObject { ["nodeId"] = nodeId },
            timeoutMs: 5000);
        var response = await _sendAsync(request, cancellationToken);
        if (!response.Success || response.Data is not JsonObject json || json["expressions"] is not JsonArray expressions)
            return [];

        return expressions.OfType<JsonObject>()
            .Select(expression => new BindingExpressionEntryViewModel(
                expression["property"]?.GetValue<string>() ?? "",
                expression["expressionType"]?.GetValue<string>() ?? "",
                expression["description"]?.GetValue<string>() ?? "",
                expression["value"]?.GetValue<string>() ?? "",
                expression["error"]?.GetValue<string>() ?? "",
                expression["priority"]?.GetValue<string>() ?? "",
                expression["details"] is JsonArray details
                    ? details.OfType<JsonValue>().Select(value => value.GetValue<string>()).ToArray()
                    : []))
            .ToArray();
    }

    public async Task<RuntimeMetricsViewModel> GetRuntimeMetricsAsync(CancellationToken cancellationToken = default)
    {
        var request = new DiagnosticRequest(
            id: Guid.NewGuid().ToString("N"),
            method: LuminaUIDiagnosticsToolNames.GetRuntimeMetrics,
            timeoutMs: 3000);
        var response = await _sendAsync(request, cancellationToken);
        if (!response.Success || response.Data is not JsonObject json)
            return RuntimeMetricsViewModel.Empty;

        return new RuntimeMetricsViewModel(
            json["managedHeapBytes"]?.GetValue<long>() ?? 0,
            json["workingSetBytes"]?.GetValue<long>() ?? 0,
            json["threadCount"]?.GetValue<int>() ?? 0,
            TimeSpan.FromTicks(json["cpuTimeTicks"]?.GetValue<long>() ?? 0),
            json["generation0Collections"]?.GetValue<int>() ?? 0,
            json["generation1Collections"]?.GetValue<int>() ?? 0,
            json["generation2Collections"]?.GetValue<int>() ?? 0);
    }

    public async Task TogglePickerModeAsync(bool isEnabled, CancellationToken cancellationToken = default)
    {
        if (_pickerService is not null)
        {
            if (isEnabled) _pickerService.Enable();
            else _pickerService.Disable();
            return;
        }

        _pickerPolling?.Cancel();
        _pickerPolling?.Dispose();
        _pickerPolling = null;

        var request = new DiagnosticRequest(
            id: Guid.NewGuid().ToString("N"),
            method: LuminaUIDiagnosticsToolNames.SetElementPicker,
            parameters: new JsonObject { ["enabled"] = isEnabled },
            timeoutMs: 5000);
        var response = await _sendAsync(request, cancellationToken);
        if (!response.Success || !isEnabled)
            return;

        _pickerPolling = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _ = PollPickerAsync(_pickerPolling.Token);
    }

    public void Dispose()
    {
        _pickerPolling?.Cancel();
        _pickerPolling?.Dispose();
        _pickerPolling = null;
        if (_pickerService is not null)
        {
            _pickerService.ElementPicked -= OnPickerElementPicked;
            _pickerService.Dispose();
        }
    }

    private void OnPickerElementPicked(object? sender, object element)
    {
        ElementPicked?.Invoke(this, element);
    }

    private async Task PollPickerAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var request = new DiagnosticRequest(
                    id: Guid.NewGuid().ToString("N"),
                    method: LuminaUIDiagnosticsToolNames.GetElementPickerState,
                    timeoutMs: 2000);
                var response = await _sendAsync(request, cancellationToken);
                if (response.Success
                    && response.Data is JsonObject json
                    && json["nodeId"]?.GetValue<string>() is { Length: > 0 } nodeId)
                {
                    if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
                    {
                        ElementPicked?.Invoke(this, nodeId);
                    }
                    else
                    {
                        Avalonia.Threading.Dispatcher.UIThread.Post(() => ElementPicked?.Invoke(this, nodeId));
                    }
                    _pickerPolling?.Cancel();
                    return;
                }

                await Task.Delay(PickerPollInterval, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception)
            {
                // A transient transport failure should not leave picker mode stuck in the UI.
                try
                {
                    await Task.Delay(250, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    private string? GetNodeId(VisualTreeNodeViewModel node)
    {
        if (!string.IsNullOrWhiteSpace(node.NodeId))
            return node.NodeId;
        return _nodeRegistry is not null && node.Control is Control control
            ? _nodeRegistry.RegisterNode(control)
            : null;
    }

    private VisualTreeNodeViewModel BuildNodeFromJson(JsonObject json)
    {
        var type = json["type"]?.GetValue<string>() ?? "Unknown";
        var name = json["name"]?.GetValue<string>();
        var classes = json["classes"] is JsonArray c
            ? string.Join(" ", c.OfType<JsonValue>().Select(v => v.GetValue<string>()))
            : "";
        var nodeId = json["nodeId"]?.GetValue<string>();
        Control? control = null;

        if (_nodeRegistry is not null && !string.IsNullOrWhiteSpace(nodeId))
            _nodeRegistry.TryResolveNode(nodeId, out control);

        var vm = new VisualTreeNodeViewModel(type, name, classes, control, nodeId);

        if (json["children"] is JsonArray children)
        {
            foreach (var child in children.OfType<JsonObject>())
                vm.Children.Add(BuildNodeFromJson(child));
        }

        vm.IsExpanded = false;
        return vm;
    }

    private PropertyViewModel BuildPropertyViewModel(Control? target, string nodeId, JsonObject prop)
    {
        var name = prop["name"]?.GetValue<string>() ?? "";
        var serializedPropertyType = prop["propertyType"]?.GetValue<string>() ?? "";
        var ownerType = prop["ownerType"]?.GetValue<string>() ?? "";
        var isAttached = prop["isAttached"]?.GetValue<bool>() ?? false;
        var isDirect = prop["isDirect"]?.GetValue<bool>() ?? false;

        var avaloniaProperty = target is null ? null : FindAvaloniaProperty(target, name);
        var clrProperty = target is not null && avaloniaProperty is null
            ? target.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public)
            : null;
        var valueType = avaloniaProperty?.PropertyType
            ?? clrProperty?.PropertyType
            ?? ResolveSerializedType(serializedPropertyType);
        var nonNullableType = valueType is null
            ? null
            : Nullable.GetUnderlyingType(valueType) ?? valueType;
        var propertyType = valueType?.Name ?? GetSimpleTypeName(serializedPropertyType);

        var vm = new PropertyViewModel(name, propertyType, ownerType, isAttached, isDirect)
        {
            IsSet = prop["isSet"]?.GetValue<bool>() ?? false,
            Category = PropertyValueConverter.GetPropertyCategory(name),
            IsReadOnly = prop["error"] is not null
                || prop["isReadOnly"]?.GetValue<bool>() == true
                || (target is not null
                    && (avaloniaProperty is null ? clrProperty?.CanWrite != true : IsReadOnlyProperty(avaloniaProperty)))
        };

        PropertyEditorMetadata.Configure(vm, valueType);

        if (prop["error"] is JsonValue errVal)
            vm.Error = errVal.GetValue<string>();

        if (prop["value"] is JsonObject valueObj)
            vm.Value = DeserializeFormattedValue(valueObj, valueType);

        if (vm.CanEdit && (avaloniaProperty is not null || target is null))
        {
            vm.SetterAction = val =>
            {
                _ = SetPropertyAsync(vm, nodeId, name, val);
            };
        }

        return vm;
    }

    private async Task SetPropertyAsync(PropertyViewModel property, string nodeId, string propertyName, object? value)
    {
        try
        {
            var clearLocalValue = value is null;
            var serializedValue = value is IFormattable formattable
                ? formattable.ToString(null, CultureInfo.InvariantCulture)
                : value?.ToString();
            var request = new DiagnosticRequest(
                id: Guid.NewGuid().ToString("N"),
                method: LuminaUIDiagnosticsToolNames.SetProperty,
                parameters: new JsonObject
                {
                    ["nodeId"] = nodeId,
                    ["propertyName"] = propertyName,
                    ["value"] = clearLocalValue ? null : serializedValue,
                    ["clearLocalValue"] = clearLocalValue
                },
                timeoutMs: 5000);
            var response = await _sendAsync(request, CancellationToken.None);

            property.Error = response.Success
                ? null
                : response.Error?.Message ?? $"Failed to update property '{propertyName}'.";
            if (response.Success)
                property.IsSet = !clearLocalValue;
        }
        catch (Exception ex)
        {
            property.Error = ex.Message;
        }
    }

    private static AvaloniaProperty? FindAvaloniaProperty(AvaloniaObject target, string name) =>
        RegisteredPropertyCatalog.Find(target, name);

    private static bool IsReadOnlyProperty(AvaloniaProperty property)
    {
        try { return property.IsReadOnly; }
        catch { return false; }
    }

    internal static object? DeserializeFormattedValue(JsonObject formattedValue, Type? targetType)
    {
        var valueNode = formattedValue["value"];
        if (valueNode is null)
            return null;

        if (targetType is not null)
        {
            try
            {
                return valueNode.Deserialize(targetType);
            }
            catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException)
            {
                var text = FormatJsonScalar(valueNode);
                var converter = new PropertyValueConverter();
                if (converter.TryConvert(text, targetType, out var converted, out _))
                    return converted;
            }
        }

        return ExtractJsonScalar(valueNode);
    }

    internal static string FormatValueForDisplay(JsonObject formattedValue)
    {
        var value = ExtractJsonScalar(formattedValue["value"]);
        if (value is null)
            return formattedValue["kind"]?.GetValue<string>() == "null" ? "(null)" : "";

        return value is IFormattable formattable
            ? formattable.ToString(null, CultureInfo.InvariantCulture) ?? ""
            : value.ToString() ?? "";
    }

    private static object? ExtractJsonScalar(JsonNode? node)
    {
        if (node is not JsonValue value)
            return node?.ToJsonString();

        if (value.TryGetValue<string>(out var text)) return text;
        if (value.TryGetValue<bool>(out var boolean)) return boolean;
        if (value.TryGetValue<int>(out var integer)) return integer;
        if (value.TryGetValue<long>(out var longInteger)) return longInteger;
        if (value.TryGetValue<decimal>(out var decimalNumber)) return decimalNumber;
        if (value.TryGetValue<double>(out var doubleNumber)) return doubleNumber;
        return value.ToJsonString();
    }

    private static string FormatJsonScalar(JsonNode node)
    {
        var value = ExtractJsonScalar(node);
        return value is IFormattable formattable
            ? formattable.ToString(null, CultureInfo.InvariantCulture) ?? ""
            : value?.ToString() ?? "";
    }

    private static string GetSimpleTypeName(string typeName)
    {
        var separator = typeName.LastIndexOf('.');
        return separator >= 0 ? typeName[(separator + 1)..] : typeName;
    }

    private static Type? ResolveSerializedType(string typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
            return null;

        var type = Type.GetType(typeName, throwOnError: false);
        if (type is not null)
            return type;

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            type = assembly.GetType(typeName, throwOnError: false);
            if (type is not null)
                return type;
        }

        return null;
    }

    private static IReadOnlyList<StyleEntryViewModel> ParseStyleEntries(JsonNode? node)
    {
        if (node is not JsonArray arr)
            return [];
        return arr.OfType<JsonObject>().Select(s => new StyleEntryViewModel(
            s["type"]?.GetValue<string>() ?? "",
            s["summary"]?.GetValue<string>() ?? "")).ToArray();
    }
}
