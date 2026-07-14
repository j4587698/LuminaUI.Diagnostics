using System.Collections;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using LuminaUI.Diagnostics.Controls;
using LuminaUI.Diagnostics.Dispatch;
using LuminaUI.Diagnostics.Serialization;
using LuminaUI.Diagnostics.Threading;
using LuminaUI.Diagnostics.Abstractions;

namespace LuminaUI.Diagnostics.Inspection;

public sealed class ResourceInspectionHandler : IDiagnosticToolHandler
{
    private readonly IUiThreadInvoker _invoker;
    private readonly IControlResolver _controlResolver;
    private readonly ValueFormatter _formatter;
    private readonly NodeRegistry _nodeRegistry;
    private readonly ResourceEntryRegistry _resourceRegistry;
    private readonly Func<IReadOnlyList<Control>> _getRoots;

    public ResourceInspectionHandler(
        IUiThreadInvoker invoker,
        IControlResolver? controlResolver = null,
        ValueFormatter? formatter = null,
        NodeRegistry? nodeRegistry = null,
        Func<IReadOnlyList<Control>>? getRoots = null,
        ResourceEntryRegistry? resourceRegistry = null)
    {
        _invoker = invoker ?? throw new ArgumentNullException(nameof(invoker));
        _controlResolver = controlResolver ?? new AvaloniaControlResolver();
        _formatter = formatter ?? new ValueFormatter();
        _nodeRegistry = nodeRegistry ?? new NodeRegistry();
        _resourceRegistry = resourceRegistry ?? new ResourceEntryRegistry();
        _getRoots = getRoots ?? InspectionRequestHelpers.GetCurrentWindowRoots;
    }

    public string Method => LuminaUIDiagnosticsToolNames.GetResources;

    public Task<DiagnosticResponse> HandleAsync(
        DiagnosticRequest request,
        CancellationToken cancellationToken = default) =>
        _invoker.InvokeAsync(
            request,
            _ => Task.FromResult(HandleOnUiThread(request)),
            cancellationToken);

    private DiagnosticResponse HandleOnUiThread(DiagnosticRequest request)
    {
        var controlId = InspectionRequestHelpers.GetString(request.Parameters, "controlId");
        var nodeId = InspectionRequestHelpers.GetString(request.Parameters, "nodeId");
        if (string.IsNullOrWhiteSpace(controlId) && string.IsNullOrWhiteSpace(nodeId))
        {
            return DiagnosticResponse.Ok(
                request.Id,
                CreateApplicationResponse(request));
        }

        var lookup = InspectionRequestHelpers.ResolveControl(request, _getRoots(), _controlResolver, _nodeRegistry);
        if (!lookup.Success)
            return lookup.Response!;

        return DiagnosticResponse.Ok(
            request.Id,
            CreateResponse(
                "control",
                lookup.Control!.Resources,
                InspectionRequestHelpers.GetString(request.Parameters, "source")));
    }

    private JsonObject CreateApplicationResponse(DiagnosticRequest request)
    {
        _resourceRegistry.Clear();
        var items = new JsonArray();
        var parameters = request.Parameters ?? new JsonObject();
        var providersOnly = parameters["providersOnly"]?.GetValue<bool>() == true;
        var providers = providersOnly ? new JsonArray() : null;
        var sourceFilter = InspectionRequestHelpers.GetString(parameters, "source");
        CollectDictionary(
            Application.Current?.Resources,
            "application/resources",
            ["application", "resources"],
            items,
            providers,
            sourceFilter,
            new HashSet<object>(ReferenceEqualityComparer.Instance));

        var styles = Application.Current?.Styles;
        if (styles is not null)
            CollectStyleResources(
                styles,
                "application/styles",
                ["application", "styles"],
                items,
                providers,
                sourceFilter,
                new HashSet<object>(ReferenceEqualityComparer.Instance));

        return providersOnly
            ? new JsonObject { ["source"] = "application", ["providers"] = providers }
            : new JsonObject { ["source"] = "application", ["count"] = items.Count, ["resources"] = items };
    }

    private JsonObject CreateResponse(
        string source,
        IResourceDictionary? resources,
        string? sourceFilter)
    {
        var items = new JsonArray();
        CollectDictionary(
            resources,
            source,
            [source],
            items,
            null,
            sourceFilter,
            new HashSet<object>(ReferenceEqualityComparer.Instance));

        return new JsonObject
        {
            ["source"] = source,
            ["count"] = items.Count,
            ["resources"] = items
        };
    }

    private void CollectDictionary(
        IResourceDictionary? resources,
        string source,
        IReadOnlyList<string> sourceSegments,
        JsonArray items,
        JsonArray? providers,
        string? sourceFilter,
        ISet<object> visited)
    {
        if (resources is null || !visited.Add(resources))
            return;

        if (providers is not null)
            providers.Add(SerializeProvider(source, sourceSegments, resources.Count));

        if (providers is null
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
                CollectDictionary(dictionary, mergedSource, mergedSegments, items, providers, sourceFilter, visited);
            else
                CollectStyleResources(merged, mergedSource, mergedSegments, items, providers, sourceFilter, visited);
        }

        foreach (var theme in resources.ThemeDictionaries)
        {
            var themeSource = $"{source}/theme[{theme.Key}]";
            var themeSegments = ResourceProviderDescriptor.Append(sourceSegments, $"theme:{theme.Key}");
            if (theme.Value is IResourceDictionary dictionary)
                CollectDictionary(dictionary, themeSource, themeSegments, items, providers, sourceFilter, visited);
            else
                CollectStyleResources(theme.Value, themeSource, themeSegments, items, providers, sourceFilter, visited);
        }
    }

    private void CollectStyleResources(
        object styleProvider,
        string source,
        IReadOnlyList<string> sourceSegments,
        JsonArray items,
        JsonArray? providers,
        string? sourceFilter,
        ISet<object> visited)
    {
        if (!visited.Add(styleProvider))
            return;

        if (styleProvider.GetType().GetProperty("Resources")?.GetValue(styleProvider) is IResourceDictionary resources)
            CollectDictionary(resources, $"{source}/resources", sourceSegments, items, providers, sourceFilter, visited);

        if (styleProvider is IEnumerable children)
        {
            var index = 0;
            foreach (var child in children)
            {
                if (child is not null)
                {
                    var childIndex = index++;
                    CollectStyleResources(
                        child,
                        $"{source}/style[{childIndex}]",
                        ResourceProviderDescriptor.Append(
                            sourceSegments,
                            ResourceProviderDescriptor.CreateProviderSegment(child, childIndex)),
                        items,
                        providers,
                        sourceFilter,
                        visited);
                }
            }
        }
    }

    private static JsonObject SerializeProvider(
        string source,
        IReadOnlyList<string> sourceSegments,
        int count)
    {
        var sourcePath = new JsonArray();
        foreach (var segment in sourceSegments)
            sourcePath.Add(segment);

        return new JsonObject
        {
            ["source"] = source,
            ["sourceSegments"] = sourcePath,
            ["count"] = count
        };
    }

    private JsonObject SerializeResource(
        string source,
        IReadOnlyList<string> sourceSegments,
        IResourceDictionary dictionary,
        object entry)
    {
        var key = GetEntryMember(entry, "Key");
        var value = GetEntryMember(entry, "Value");
        var isDeferred = ResourceValuePolicy.IsDeferred(value);
        var canEdit = key is not null && ResourceValuePolicy.IsEditable(value);
        var sourcePath = new JsonArray();
        foreach (var segment in sourceSegments)
            sourcePath.Add(segment);

        var formattedValue = isDeferred
            ? new JsonObject
            {
                ["kind"] = "deferred",
                ["type"] = "AXAML",
                ["value"] = null
            }
            : _formatter.Format(
                value,
                new ValueFormatOptions { MaxStringLength = 160, MaxEnumerableItems = 5, MaxDepth = 0 });

        return new JsonObject
        {
            ["entryId"] = canEdit ? _resourceRegistry.Register(dictionary, key!) : null,
            ["source"] = source,
            ["sourceSegments"] = sourcePath,
            ["key"] = key?.ToString(),
            ["keyType"] = key?.GetType().FullName,
            ["isDeferred"] = isDeferred,
            ["value"] = formattedValue
        };
    }

    private static object? GetEntryMember(
        object entry,
        string name) =>
        entry.GetType().GetProperty(name)?.GetValue(entry);
}
