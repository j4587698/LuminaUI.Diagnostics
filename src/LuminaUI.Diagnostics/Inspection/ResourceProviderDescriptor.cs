namespace LuminaUI.Diagnostics.Inspection;

internal static class ResourceProviderDescriptor
{
    public static IReadOnlyList<string> Append(IReadOnlyList<string> segments, string segment) =>
        [.. segments, segment];

    public static string CreateProviderSegment(object provider, int index) =>
        $"provider:{index}:{Describe(provider)}";

    public static string CreateMergedSegment(object provider, int index) =>
        $"merged:{index}:{Describe(provider)}";

    private static string Describe(object provider)
    {
        var type = provider.GetType();
        var selector = type.GetProperty("Selector")?.GetValue(provider)?.ToString();
        if (!string.IsNullOrWhiteSpace(selector))
            return selector;

        if (type.GetProperty("TargetType")?.GetValue(provider) is Type targetType)
            return $"{targetType.Name} ({type.Name})";

        var source = type.GetProperty("Source")?.GetValue(provider)?.ToString();
        if (!string.IsNullOrWhiteSpace(source))
        {
            var normalized = source.Replace('\\', '/');
            return normalized[(normalized.LastIndexOf('/') + 1)..];
        }

        return type.Name;
    }
}
