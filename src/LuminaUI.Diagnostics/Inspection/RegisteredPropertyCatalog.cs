using Avalonia;

namespace LuminaUI.Diagnostics.Inspection;

internal static class RegisteredPropertyCatalog
{
    public static IReadOnlyList<AvaloniaProperty> GetUnique(AvaloniaObject target) =>
        GetUnique(AvaloniaPropertyRegistry.Instance.GetRegistered(target));

    internal static IReadOnlyList<AvaloniaProperty> GetUnique(IEnumerable<AvaloniaProperty> properties) =>
        properties
            .GroupBy(property => property.Name, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(property => property.Name, StringComparer.Ordinal)
            .ToArray();

    public static AvaloniaProperty? Find(AvaloniaObject target, string propertyName) =>
        GetUnique(target)
            .FirstOrDefault(property => string.Equals(property.Name, propertyName, StringComparison.Ordinal));
}
