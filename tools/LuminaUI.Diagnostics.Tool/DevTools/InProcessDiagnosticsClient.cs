using System.Collections;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Styling;
using Avalonia.VisualTree;
using LuminaUI.Diagnostics.Binding;
using LuminaUI.Diagnostics.Inspection;
using LuminaUI.Diagnostics.Interaction;
using LuminaUI.Diagnostics.Serialization;
using LuminaUI.Diagnostics.UI.ViewModels;

namespace LuminaUI.Diagnostics.UI;

public sealed class InProcessDiagnosticsClient : IDiagnosticsClient, IDisposable
{
    private readonly IDisposable _bindingErrorRegistration;
    private readonly ValueFormatter _formatter = new();
    private readonly NodeRegistry _nodeRegistry;
    private readonly ElementPickerService _pickerService;

    public InProcessDiagnosticsClient(NodeRegistry? nodeRegistry = null)
    {
        _nodeRegistry = nodeRegistry ?? throw new ArgumentNullException(nameof(nodeRegistry));
        _bindingErrorRegistration = BindingErrorDiagnostics.Install();
        _pickerService = new ElementPickerService(nodeRegistry);
        _pickerService.ElementPicked += OnPickerElementPicked;
    }

    public event EventHandler<object>? ElementPicked;

    public Task<IReadOnlyList<VisualTreeNodeViewModel>> GetVisualTreeAsync(CancellationToken cancellationToken = default)
    {
        var roots = InspectionRequestHelpers.GetCurrentWindowRoots();
        var viewModels = new List<VisualTreeNodeViewModel>();

        foreach (var root in roots)
        {
            if (root.GetType().Name == "LuminaDevToolsWindow") continue;
            viewModels.Add(BuildVisualTree(root, 0, DevToolsSettingsStore.Current.MaxTreeDepth));
        }

        return Task.FromResult<IReadOnlyList<VisualTreeNodeViewModel>>(viewModels);
    }

    public Task<IReadOnlyList<VisualTreeNodeViewModel>> GetLogicalTreeAsync(CancellationToken cancellationToken = default)
    {
        var roots = InspectionRequestHelpers.GetCurrentWindowRoots();
        var viewModels = new List<VisualTreeNodeViewModel>();

        foreach (var root in roots)
        {
            if (root.GetType().Name == "LuminaDevToolsWindow") continue;
            viewModels.Add(BuildLogicalTree(root, 0, DevToolsSettingsStore.Current.MaxTreeDepth));
        }

        return Task.FromResult<IReadOnlyList<VisualTreeNodeViewModel>>(viewModels);
    }

    public Task HighlightElementAsync(VisualTreeNodeViewModel? node, CancellationToken cancellationToken = default)
    {
        var visual = node?.Control as Avalonia.Visual;
        _pickerService.Highlight(visual);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<PropertyViewModel>> GetPropertiesAsync(VisualTreeNodeViewModel node, CancellationToken cancellationToken = default)
    {
        var properties = new List<PropertyViewModel>();

        if (node?.Control is AvaloniaObject avaloniaObject)
        {
            var registeredProps = RegisteredPropertyCatalog.GetUnique(avaloniaObject);
            foreach (var property in registeredProps)
            {
                if (!ShouldIncludeProperty(avaloniaObject, property))
                    continue;

                var vm = new PropertyViewModel(
                    property.Name,
                    property.PropertyType.Name,
                    property.OwnerType.Name,
                    property.IsAttached,
                    property.IsDirect)
                {
                    IsReadOnly = property.IsReadOnly,
                    Category = PropertyValueConverter.GetPropertyCategory(property.Name)
                };

                PropertyEditorMetadata.Configure(vm, property.PropertyType);

                vm.IsSet = avaloniaObject.IsSet(property);

                try
                {
                    vm.Value = avaloniaObject.GetValue(property);

                    if (vm.CanEdit)
                        vm.SetterAction = CreateSetterAction(vm, avaloniaObject, property);
                }
                catch (Exception ex)
                {
                    vm.Error = ex.Message;
                    vm.CanEdit = false;
                }

                properties.Add(vm);
            }

            if (DevToolsSettingsStore.Current.IncludeClrProperties)
            {
                foreach (var clrProp in avaloniaObject.GetType()
                    .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                    .Where(property => property.CanRead && property.GetIndexParameters().Length == 0)
                    .OrderBy(property => property.Name, StringComparer.Ordinal))
                {
                    if (properties.Any(property => property.Name == clrProp.Name))
                        continue;

                    var vm = new PropertyViewModel(
                        clrProp.Name,
                        clrProp.PropertyType.Name,
                        clrProp.DeclaringType?.Name ?? "",
                        false,
                        false)
                    {
                        IsReadOnly = true,
                        Category = PropertyValueConverter.GetPropertyCategory(clrProp.Name)
                    };
                    PropertyEditorMetadata.Configure(vm, clrProp.PropertyType);

                    try { vm.Value = clrProp.GetValue(avaloniaObject); }
                    catch (Exception ex) { vm.Error = ex.InnerException?.Message ?? ex.Message; }

                    properties.Add(vm);
                }
            }
        }

        return Task.FromResult<IReadOnlyList<PropertyViewModel>>(properties);
    }

    public Task<IReadOnlyList<ResourceProviderViewModel>> GetResourceProvidersAsync(
        CancellationToken cancellationToken = default)
    {
        var providers = new List<ResourceProviderViewModel>();
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        CollectResources(
            "application/resources",
            ["application", "resources"],
            Application.Current?.Resources,
            null,
            providers,
            null,
            visited);
        if (Application.Current?.Styles is { } styles)
            CollectStyleResources(
                "application/styles",
                ["application", "styles"],
                styles,
                null,
                providers,
                null,
                visited);
        return Task.FromResult<IReadOnlyList<ResourceProviderViewModel>>(providers);
    }

    public Task<IReadOnlyList<ResourceEntryViewModel>> GetResourcesAsync(
        string? source = null,
        VisualTreeNodeViewModel? node = null,
        CancellationToken cancellationToken = default)
    {
        if (node?.Control is StyledElement styledElement)
            return Task.FromResult(ReadResources(node.DisplayText, styledElement.Resources));

        var items = new List<ResourceEntryViewModel>();
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        CollectResources(
            "application/resources",
            ["application", "resources"],
            Application.Current?.Resources,
            items,
            null,
            source,
            visited);
        if (Application.Current?.Styles is { } styles)
            CollectStyleResources(
                "application/styles",
                ["application", "styles"],
                styles,
                items,
                null,
                source,
                visited);
        return Task.FromResult<IReadOnlyList<ResourceEntryViewModel>>(items.OrderBy(item => item.Key).ToArray());
    }

    public async Task<IReadOnlyList<AssetEntryViewModel>> GetAssetsAsync(CancellationToken cancellationToken = default)
    {
        var handler = new AssetInspectionHandler();
        var response = await handler.HandleAsync(
            new LuminaUI.Diagnostics.Abstractions.DiagnosticRequest(
                Guid.NewGuid().ToString("N"),
                LuminaUI.Diagnostics.Abstractions.LuminaUIDiagnosticsToolNames.GetAssets),
            cancellationToken);
        return ParseAssets(response.Data as System.Text.Json.Nodes.JsonObject);
    }

    private static IReadOnlyList<AssetEntryViewModel> ParseAssets(System.Text.Json.Nodes.JsonObject? json) =>
        json?["assets"] is System.Text.Json.Nodes.JsonArray assets
            ? assets.OfType<System.Text.Json.Nodes.JsonObject>()
                .Select(asset => new AssetEntryViewModel(
                    asset["fileName"]?.GetValue<string>() ?? "",
                    asset["relativePath"]?.GetValue<string>() ?? "",
                    asset["assembly"]?.GetValue<string>() ?? "",
                    asset["uri"]?.GetValue<string>() ?? "",
                    asset["size"]?.GetValue<long?>()))
                .ToArray()
            : [];

    public Task<StyleInspectionViewModel> GetAppliedStylesAsync(
        VisualTreeNodeViewModel? node,
        CancellationToken cancellationToken = default)
    {
        if (node?.Control is not Control control)
            return Task.FromResult(StyleInspectionViewModel.Empty);

        var localStyles = new List<StyleEntryViewModel>();
        if (control is IStyleHost { IsStylesInitialized: true } styleHost)
        {
            foreach (var style in styleHost.Styles)
                localStyles.Add(BuildStyleEntry(style));
        }

        var hierarchyStyles = new List<StyleEntryViewModel>();
        var seen = new HashSet<string>();
        var parent = control.Parent as StyledElement;
        while (parent is IStyleHost { IsStylesInitialized: true } parentHost)
        {
            foreach (var style in parentHost.Styles)
            {
                var summary = style.ToString() ?? "";
                if (seen.Add(summary))
                    hierarchyStyles.Add(BuildStyleEntry(style));
            }
            parent = parent.Parent;
        }

        if (Application.Current is IStyleHost appHost && appHost.IsStylesInitialized)
        {
            foreach (var style in appHost.Styles)
            {
                var summary = style.ToString() ?? "";
                if (seen.Add(summary))
                    hierarchyStyles.Add(BuildStyleEntry(style));
            }
        }

        var effectiveSetters = new List<string>();
        var properties = RegisteredPropertyCatalog.GetUnique(control);
        foreach (var property in properties)
        {
            if (control.IsSet(property))
                effectiveSetters.Add($"{property.Name} = {control.GetValue(property)}");
        }

        return Task.FromResult(
            new StyleInspectionViewModel(
                control.GetType().FullName ?? control.GetType().Name,
                control.Name ?? "",
                string.Join(" ", control.Classes),
                control.StyleKey?.ToString() ?? "(default)",
                control.TemplatedParent?.GetType().FullName ?? "(none)",
                localStyles,
                hierarchyStyles,
                effectiveSetters));
    }

    public Task<IReadOnlyList<BindingErrorEntryViewModel>> GetBindingErrorsAsync(
        int maxItems = 100,
        CancellationToken cancellationToken = default)
    {
        var entries = BindingErrorDiagnostics.Store
            .Snapshot(maxItems)
            .Select(entry => new BindingErrorEntryViewModel(
                entry.Timestamp.ToLocalTime().ToString("HH:mm:ss"),
                entry.Level,
                entry.Area,
                entry.SourceType,
                entry.Message))
            .ToArray();

        return Task.FromResult<IReadOnlyList<BindingErrorEntryViewModel>>(entries);
    }

    public Task<IReadOnlyList<BindingExpressionEntryViewModel>> GetBindingExpressionsAsync(
        VisualTreeNodeViewModel? node,
        CancellationToken cancellationToken = default)
    {
        if (node?.Control is not AvaloniaObject target)
            return Task.FromResult<IReadOnlyList<BindingExpressionEntryViewModel>>([]);

        var entries = BindingExpressionInspector.Inspect(target)
            .Select(expression => new BindingExpressionEntryViewModel(
                expression.Property,
                expression.ExpressionType,
                expression.Description,
                expression.Value,
                expression.Error,
                expression.Priority,
                expression.Details))
            .ToArray();
        return Task.FromResult<IReadOnlyList<BindingExpressionEntryViewModel>>(entries);
    }

    public Task TogglePickerModeAsync(bool isEnabled, CancellationToken cancellationToken = default)
    {
        if (isEnabled)
            _pickerService.Enable();
        else
            _pickerService.Disable();
        return Task.CompletedTask;
    }

    public Task<RuntimeMetricsViewModel> GetRuntimeMetricsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(RuntimeMetricsViewModel.ReadCurrent());

    public void Dispose()
    {
        _pickerService.ElementPicked -= OnPickerElementPicked;
        _pickerService.Dispose();
        _bindingErrorRegistration.Dispose();
    }

    private void OnPickerElementPicked(object? sender, object element)
    {
        ElementPicked?.Invoke(this, element);
    }

    private VisualTreeNodeViewModel BuildVisualTree(Avalonia.Visual visual, int depth, int maxDepth)
    {
        var vm = new VisualTreeNodeViewModel(visual) { IsExpanded = depth < 3 };
        if (depth >= maxDepth) return vm;

        if (visual is Control control)
            _nodeRegistry.RegisterNode(control);

        foreach (var child in visual.GetVisualChildren())
        {
            if (child is Avalonia.Visual childVisual)
                vm.Children.Add(BuildVisualTree(childVisual, depth + 1, maxDepth));
        }

        return vm;
    }

    private VisualTreeNodeViewModel BuildLogicalTree(ILogical logical, int depth, int maxDepth)
    {
        var vm = new VisualTreeNodeViewModel(logical) { IsExpanded = depth < 3 };
        if (depth >= maxDepth) return vm;

        foreach (var child in logical.GetLogicalChildren())
        {
            vm.Children.Add(BuildLogicalTree(child, depth + 1, maxDepth));
        }

        return vm;
    }

    private static Action<object?> CreateSetterAction(
        PropertyViewModel vm,
        AvaloniaObject target,
        AvaloniaProperty property)
    {
        var converter = new PropertyValueConverter();
        return val =>
        {
            try
            {
                if (val is null)
                {
                    converter.TryClearValue(property, target, out _);
                    vm.IsSet = false;
                    return;
                }

                if (val is string str && converter.TryConvert(str, property.PropertyType, out var converted, out _))
                {
                    target.SetValue(property, converted);
                }
                else if (val is not string)
                {
                    target.SetValue(property, val);
                }

                vm.IsSet = true;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to set property '{property.Name}': {ex.Message}", ex);
            }
        };
    }

    private static StyleEntryViewModel BuildStyleEntry(IStyle style)
    {
        var setters = new List<string>();
        var selector = "";

        if (style is Style s)
        {
            selector = s.Selector?.ToString() ?? "";
            foreach (var setter in s.Setters)
            {
                if (setter is Setter setterValue)
                {
                    var valueStr = setterValue.Value?.ToString() ?? "(null)";
                    setters.Add($"{setterValue.Property?.Name} = {valueStr}");
                }
            }
        }

        return new StyleEntryViewModel(
            style.GetType().FullName ?? style.GetType().Name,
            style.ToString() ?? "",
            selector,
            setters);
    }

    private IReadOnlyList<ResourceEntryViewModel> ReadResources(
        string source,
        IResourceDictionary? resources)
    {
        var items = new List<ResourceEntryViewModel>();
        CollectResources(
            source,
            [source],
            resources,
            items,
            null,
            null,
            new HashSet<object>(ReferenceEqualityComparer.Instance));

        return items.OrderBy(item => item.Key).ToArray();
    }

    private void CollectResources(
        string source,
        IReadOnlyList<string> sourceSegments,
        IResourceDictionary? resources,
        ICollection<ResourceEntryViewModel>? items,
        ICollection<ResourceProviderViewModel>? providers,
        string? sourceFilter,
        ISet<object> visited)
    {
        if (resources is null || !visited.Add(resources))
            return;

        providers?.Add(new ResourceProviderViewModel(source, sourceSegments, resources.Count));

        if (items is not null
            && (string.IsNullOrWhiteSpace(sourceFilter)
                || string.Equals(source, sourceFilter, StringComparison.Ordinal))
            && resources is IEnumerable enumerable)
        {
            foreach (var entry in enumerable)
                items.Add(SerializeResource(source, sourceSegments, resources, entry));
        }

        var mergedIndex = 0;
        foreach (var merged in resources.MergedDictionaries)
        {
            var index = mergedIndex++;
            var mergedSource = $"{source}/merged[{index}]";
            var mergedSegments = ResourceProviderDescriptor.Append(
                sourceSegments,
                ResourceProviderDescriptor.CreateMergedSegment(merged, index));
            if (merged is IResourceDictionary dictionary)
                CollectResources(mergedSource, mergedSegments, dictionary, items, providers, sourceFilter, visited);
            else
                CollectStyleResources(mergedSource, mergedSegments, merged, items, providers, sourceFilter, visited);
        }

        foreach (var theme in resources.ThemeDictionaries)
        {
            var themeSource = $"{source}/theme[{theme.Key}]";
            var themeSegments = ResourceProviderDescriptor.Append(sourceSegments, $"theme:{theme.Key}");
            if (theme.Value is IResourceDictionary dictionary)
                CollectResources(themeSource, themeSegments, dictionary, items, providers, sourceFilter, visited);
            else
                CollectStyleResources(themeSource, themeSegments, theme.Value, items, providers, sourceFilter, visited);
        }
    }

    private void CollectStyleResources(
        string source,
        IReadOnlyList<string> sourceSegments,
        object provider,
        ICollection<ResourceEntryViewModel>? items,
        ICollection<ResourceProviderViewModel>? providers,
        string? sourceFilter,
        ISet<object> visited)
    {
        if (!visited.Add(provider))
            return;

        if (provider.GetType().GetProperty("Resources")?.GetValue(provider) is IResourceDictionary resources)
            CollectResources($"{source}/resources", sourceSegments, resources, items, providers, sourceFilter, visited);

        if (provider is IEnumerable children)
        {
            var index = 0;
            foreach (var child in children)
            {
                if (child is not null)
                {
                    var childIndex = index++;
                    CollectStyleResources(
                        $"{source}/style[{childIndex}]",
                        ResourceProviderDescriptor.Append(
                            sourceSegments,
                            ResourceProviderDescriptor.CreateProviderSegment(child, childIndex)),
                        child,
                        items,
                        providers,
                        sourceFilter,
                        visited);
                }
            }
        }
    }

    private ResourceEntryViewModel SerializeResource(
        string source,
        IReadOnlyList<string> sourceSegments,
        IResourceDictionary dictionary,
        object entry)
    {
        var key = entry.GetType().GetProperty("Key")?.GetValue(entry);
        var value = entry.GetType().GetProperty("Value")?.GetValue(entry);
        var isDeferred = ResourceValuePolicy.IsDeferred(value);
        var formatted = isDeferred
            ? new System.Text.Json.Nodes.JsonObject
            {
                ["kind"] = "deferred",
                ["type"] = "AXAML",
                ["value"] = null
            }
            : _formatter.Format(
                value,
                new ValueFormatOptions { MaxStringLength = 160, MaxEnumerableItems = 5, MaxDepth = 0 });
        Func<string, Task>? setter = ResourceValuePolicy.IsEditable(value) && key is not null
            ? text => SetResourceAsync(dictionary, key, value!.GetType(), text)
            : null;

        return new ResourceEntryViewModel(
            source,
            key?.ToString() ?? "(null)",
            key?.GetType().Name ?? "",
            formatted["kind"]?.ToString() ?? "",
            formatted["type"]?.ToString() ?? value?.GetType().FullName ?? "",
            formatted["value"]?.ToString() ?? "(null)",
            setter,
            sourceSegments,
            isDeferred);
    }

    private static Task SetResourceAsync(IResourceDictionary dictionary, object? key, Type targetType, string text)
    {
        if (key is null)
            throw new InvalidOperationException("The resource key is unavailable.");
        var converter = new PropertyValueConverter();
        if (!converter.TryConvert(text, targetType, out var converted, out var error))
            throw new InvalidOperationException(error ?? $"Unable to convert the value to {targetType.Name}.");
        dictionary[key] = converted;
        return Task.CompletedTask;
    }

    private static bool IsValidNumeric(Type? type) => PropertyValueConverter.IsValidNumeric(type);

    private static string GetPropertyCategory(string name) => PropertyValueConverter.GetPropertyCategory(name);

    private static bool ShouldIncludeProperty(AvaloniaObject target, AvaloniaProperty property)
    {
        if (!DevToolsSettingsStore.Current.ContextualProperties || !property.IsAttached || target.IsSet(property))
            return true;

        var parentType = (target as StyledElement)?.Parent?.GetType();
        return parentType is not null && property.OwnerType.IsAssignableFrom(parentType);
    }
}
