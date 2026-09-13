using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using LuminaUI.Diagnostics.Interaction;

namespace LuminaUI.Diagnostics.UI.ViewModels;

internal static class PropertyEditorMetadata
{
    public static void Configure(PropertyViewModel property, Type? valueType)
    {
        var type = valueType is null
            ? null
            : Nullable.GetUnderlyingType(valueType) ?? valueType;

        property.IsBoolean = type == typeof(bool);
        property.IsEnum = type?.IsEnum == true;
        property.IsNumeric = PropertyValueConverter.IsValidNumeric(valueType);
        property.EditorKind = ResolveEditorKind(property.Name, type);
        property.CanEdit = !property.IsReadOnly && property.EditorKind != PropertyEditorKind.ReadOnly;

        if (property.IsEnum && type is not null)
            property.ChoiceValues = Enum.GetNames(type);
        else if (type == typeof(ThemeVariant))
            property.ChoiceValues = ["Default", "Light", "Dark"];
        else if (type == typeof(Cursor))
            property.ChoiceValues = Enum.GetNames<StandardCursorType>();
    }

    private static PropertyEditorKind ResolveEditorKind(string propertyName, Type? type)
    {
        if (type == typeof(object) && propertyName is "Tag" or "CommandParameter")
            return PropertyEditorKind.Text;

        if (type is null || !PropertyValueConverter.CanConvertFromString(type))
            return PropertyEditorKind.ReadOnly;

        if (type == typeof(bool))
            return PropertyEditorKind.Boolean;
        if (type.IsEnum || type == typeof(ThemeVariant) || type == typeof(Cursor))
            return PropertyEditorKind.Choice;
        if (PropertyValueConverter.IsValidNumeric(type))
            return PropertyEditorKind.Numeric;
        if (type == typeof(Thickness) || type == typeof(CornerRadius))
            return PropertyEditorKind.Compound;
        if (type == typeof(Color) || typeof(IBrush).IsAssignableFrom(type))
            return PropertyEditorKind.Color;
        if (type == typeof(FontFamily))
            return PropertyEditorKind.FontFamily;

        return PropertyEditorKind.Text;
    }
}

public enum PropertyEditorKind
{
    ReadOnly,
    Boolean,
    Choice,
    Numeric,
    Compound,
    Color,
    FontFamily,
    Text
}
