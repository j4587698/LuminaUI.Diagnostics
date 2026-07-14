using System.ComponentModel;
using System.Globalization;
using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;

namespace LuminaUI.Diagnostics.Interaction;

public sealed class PropertyValueConverter
{
    public bool TryConvert(
        string? value,
        Type targetType,
        out object? converted,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(targetType);

        converted = null;
        error = null;

        var nonNullableType = Nullable.GetUnderlyingType(targetType) ?? targetType;

        if (value is null)
        {
            if (Nullable.GetUnderlyingType(targetType) is not null || !targetType.IsValueType)
                return true;

            error = $"Cannot assign null to {targetType.Name}.";
            return false;
        }

        try
        {
            converted = ConvertCore(value, nonNullableType);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public bool TryClearValue(
        AvaloniaProperty property,
        AvaloniaObject target,
        out string? error)
    {
        error = null;

        if (property.IsReadOnly)
        {
            error = $"Property '{property.Name}' is read-only and cannot be cleared.";
            return false;
        }

        try
        {
            target.ClearValue(property);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static object? ConvertCore(
        string value,
        Type targetType)
    {
        if (targetType == typeof(string))
            return value;

        if (targetType == typeof(object))
            return value;

        if (targetType == typeof(bool))
            return bool.Parse(value);

        if (targetType.IsEnum)
            return Enum.Parse(targetType, value, ignoreCase: true);

        if (targetType == typeof(Thickness))
            return Thickness.Parse(value);

        if (targetType == typeof(CornerRadius))
            return CornerRadius.Parse(value);

        if (targetType == typeof(Color))
            return Color.Parse(value);

        if (targetType == typeof(Guid))
            return Guid.Parse(value);

        if (targetType == typeof(TimeSpan))
            return TimeSpan.Parse(value, CultureInfo.InvariantCulture);

        if (typeof(IBrush).IsAssignableFrom(targetType))
            return Brush.Parse(value);

        if (typeof(Geometry).IsAssignableFrom(targetType))
            return Geometry.Parse(value);

        if (targetType == typeof(FontFamily))
            return new FontFamily(value);

        if (targetType == typeof(BoxShadows))
            return BoxShadows.Parse(value);

        if (targetType == typeof(ThemeVariant))
        {
            return value.ToUpperInvariant() switch
            {
                "DEFAULT" => ThemeVariant.Default,
                "LIGHT" => ThemeVariant.Light,
                "DARK" => ThemeVariant.Dark,
                _ => throw new FormatException($"Unknown theme variant '{value}'.")
            };
        }

        if (targetType == typeof(Cursor))
            return new Cursor(Enum.Parse<StandardCursorType>(value, ignoreCase: true));

        var converter = TypeDescriptor.GetConverter(targetType);
        if (converter.CanConvertFrom(typeof(string)))
            return converter.ConvertFromInvariantString(value);

        return Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture);
    }

    internal static bool CanConvertFromString(Type? type)
    {
        if (type is null)
            return false;

        var targetType = Nullable.GetUnderlyingType(type) ?? type;
        if (targetType == typeof(string)
            || targetType == typeof(bool)
            || targetType.IsEnum
            || IsValidNumeric(targetType)
            || targetType == typeof(Thickness)
            || targetType == typeof(CornerRadius)
            || targetType == typeof(Color)
            || targetType == typeof(Guid)
            || targetType == typeof(TimeSpan)
            || typeof(IBrush).IsAssignableFrom(targetType)
            || typeof(Geometry).IsAssignableFrom(targetType)
            || targetType == typeof(FontFamily)
            || targetType == typeof(BoxShadows)
            || targetType == typeof(ThemeVariant)
            || targetType == typeof(Cursor))
        {
            return true;
        }

        return TypeDescriptor.GetConverter(targetType).CanConvertFrom(typeof(string));
    }

    internal static string GetPropertyCategory(string name)
    {
        return name switch
        {
            "Width" or "Height" or "MinWidth" or "MinHeight" or "MaxWidth" or "MaxHeight"
                or "Margin" or "Padding" or "HorizontalAlignment" or "VerticalAlignment"
                or "HorizontalContentAlignment" or "VerticalContentAlignment"
                or "ScrollBarVisibility" or "ScrollViewer" => "Layout",
            "Background" or "Foreground" or "BorderBrush" or "BorderThickness"
                or "CornerRadius" or "Opacity" or "FontFamily" or "FontSize"
                or "FontWeight" or "FontStyle" or "RequestedThemeVariant"
                or "TextAlignment" or "TextWrapping" or "TextTrimming"
                or "ForegroundColor" or "BackgroundColor" => "Appearance",
            "IsEnabled" or "IsVisible" or "Focusable" or "IsHitTestVisible"
                or "ClipToBounds" or "FocusAdorner" or "Cursor"
                or "IsTabStop" or "TabIndex" or "TabNavigation" => "Behavior",
            "Name" or "Tag" or "DataContext" or "FlowDirection" or "Classes"
                or "Resources" or "Style" or "StyleKey" => "Common",
            "Command" or "CommandParameter" or "HotKey" or "ContextMenu" => "Automation",
            _ => "Misc",
        };
    }

    internal static bool IsValidNumeric(Type? type)
    {
        if (type == null || type.IsEnum)
            return false;

        var typeCode = Type.GetTypeCode(type);

        if (typeCode == TypeCode.Object)
        {
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>))
                typeCode = Type.GetTypeCode(Nullable.GetUnderlyingType(type));
            else
                return false;
        }

        return typeCode switch
        {
            TypeCode.Byte or TypeCode.SByte or TypeCode.Int16 or TypeCode.UInt16
                or TypeCode.Int32 or TypeCode.UInt32 or TypeCode.Int64 or TypeCode.UInt64
                or TypeCode.Single or TypeCode.Double => true,
            _ => false,
        };
    }
}
