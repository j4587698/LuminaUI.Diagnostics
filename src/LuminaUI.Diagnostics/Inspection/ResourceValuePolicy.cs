using LuminaUI.Diagnostics.Interaction;

namespace LuminaUI.Diagnostics.Inspection;

internal static class ResourceValuePolicy
{
    public static bool IsDeferred(object? value)
    {
        if (value is null)
            return false;

        var type = value.GetType();
        var fullName = type.FullName ?? type.Name;
        return type.Name.Contains("Deferred", StringComparison.Ordinal)
            || fullName.Contains("XamlIlRuntimeHelpers+", StringComparison.Ordinal)
            && fullName.Contains("Deferred", StringComparison.Ordinal);
    }

    public static bool IsEditable(object? value) =>
        value is not null
        && !IsDeferred(value)
        && PropertyValueConverter.CanConvertFromString(value.GetType());
}
