using Avalonia;
using Avalonia.Media;
using Avalonia.Input;
using Avalonia.Styling;
using LuminaUI.Diagnostics.Interaction;

namespace LuminaUI.Diagnostics.Tests.Interaction;

public sealed class PropertyValueConverterTests
{
    private readonly PropertyValueConverter _converter = new();

    [Theory]
    [InlineData("hello", typeof(string), "hello")]
    [InlineData("true", typeof(bool), true)]
    [InlineData("false", typeof(bool), false)]
    [InlineData("42", typeof(int), 42)]
    [InlineData("3.14", typeof(double), 3.14)]
    [InlineData("123", typeof(long), 123L)]
    public void TryConvert_BasicTypes_ReturnsExpected(string input, Type targetType, object expected)
    {
        var result = _converter.TryConvert(input, targetType, out var converted, out var error);

        Assert.True(result, $"Conversion failed: {error}");
        Assert.Equal(expected, converted);
        Assert.Null(error);
    }

    [Fact]
    public void TryConvert_Enum_ReturnsParsed()
    {
        var result = _converter.TryConvert("TargetNotFound", typeof(DiagnosticErrorCode), out var converted, out var error);

        Assert.True(result);
        Assert.Equal(DiagnosticErrorCode.TargetNotFound, converted);
        Assert.Null(error);
    }

    [Fact]
    public void TryConvert_EnumCaseInsensitive_ReturnsParsed()
    {
        var result = _converter.TryConvert("targetnotfound", typeof(DiagnosticErrorCode), out var converted, out var error);

        Assert.True(result);
        Assert.Equal(DiagnosticErrorCode.TargetNotFound, converted);
    }

    [Fact]
    public void TryConvert_Thickness_ReturnsParsed()
    {
        var result = _converter.TryConvert("1,2,3,4", typeof(Thickness), out var converted, out var error);

        Assert.True(result);
        var thickness = Assert.IsType<Thickness>(converted);
        Assert.Equal(1, thickness.Left);
        Assert.Equal(2, thickness.Top);
        Assert.Equal(3, thickness.Right);
        Assert.Equal(4, thickness.Bottom);
    }

    [Fact]
    public void TryConvert_CornerRadius_ReturnsParsed()
    {
        var result = _converter.TryConvert("5", typeof(CornerRadius), out var converted, out var error);

        Assert.True(result);
        var cornerRadius = Assert.IsType<CornerRadius>(converted);
        Assert.Equal(5, cornerRadius.TopLeft);
    }

    [Fact]
    public void TryConvert_Color_ReturnsParsed()
    {
        var result = _converter.TryConvert("#FF0000", typeof(Color), out var converted, out var error);

        Assert.True(result);
        var color = Assert.IsType<Color>(converted);
        Assert.Equal(255, color.R);
        Assert.Equal(0, color.G);
        Assert.Equal(0, color.B);
    }

    [Fact]
    public void TryConvert_Brush_ReturnsParsed()
    {
        var result = _converter.TryConvert("red", typeof(IBrush), out var converted, out var error);

        Assert.True(result);
        Assert.IsAssignableFrom<IBrush>(converted);
        var solidColorBrush = Assert.IsAssignableFrom<ISolidColorBrush>(converted);
        Assert.Equal(Colors.Red, solidColorBrush.Color);
    }

    [Fact]
    public void TryConvert_NullToNullableType_ReturnsNull()
    {
        var result = _converter.TryConvert(null, typeof(int?), out var converted, out var error);

        Assert.True(result);
        Assert.Null(converted);
    }

    [Fact]
    public void TryConvert_NullToValueType_ReturnsError()
    {
        var result = _converter.TryConvert(null, typeof(int), out var converted, out var error);

        Assert.False(result);
        Assert.NotNull(error);
        Assert.Null(converted);
    }

    [Fact]
    public void TryConvert_InvalidInput_ReturnsError()
    {
        var result = _converter.TryConvert("not-a-number", typeof(int), out var converted, out var error);

        Assert.False(result);
        Assert.NotNull(error);
        Assert.Null(converted);
    }

    [Fact]
    public void TryConvert_Guid_ReturnsParsed()
    {
        var guid = Guid.NewGuid();
        var result = _converter.TryConvert(guid.ToString(), typeof(Guid), out var converted, out var error);

        Assert.True(result);
        Assert.Equal(guid, converted);
    }

    [Fact]
    public void TryConvert_ThemeVariant_ReturnsPreset()
    {
        var result = _converter.TryConvert("dark", typeof(ThemeVariant), out var converted, out var error);

        Assert.True(result, error);
        Assert.Same(ThemeVariant.Dark, converted);
    }

    [Fact]
    public void CanConvertFromString_CursorIsSupported()
    {
        Assert.True(PropertyValueConverter.CanConvertFromString(typeof(Cursor)));
    }

    [Fact]
    public void CanConvertFromString_RejectsArbitraryObjects()
    {
        Assert.False(PropertyValueConverter.CanConvertFromString(typeof(Avalonia.Controls.Control)));
        Assert.True(PropertyValueConverter.CanConvertFromString(typeof(FontFamily)));
    }

    [Fact]
    public void GetPropertyCategory_Layout_ReturnsCorrect()
    {
        Assert.Equal("Layout", PropertyValueConverter.GetPropertyCategory("Width"));
        Assert.Equal("Layout", PropertyValueConverter.GetPropertyCategory("Margin"));
        Assert.Equal("Layout", PropertyValueConverter.GetPropertyCategory("Padding"));
    }

    [Fact]
    public void GetPropertyCategory_Appearance_ReturnsCorrect()
    {
        Assert.Equal("Appearance", PropertyValueConverter.GetPropertyCategory("Background"));
        Assert.Equal("Appearance", PropertyValueConverter.GetPropertyCategory("Foreground"));
        Assert.Equal("Appearance", PropertyValueConverter.GetPropertyCategory("Opacity"));
    }

    [Fact]
    public void GetPropertyCategory_Behavior_ReturnsCorrect()
    {
        Assert.Equal("Behavior", PropertyValueConverter.GetPropertyCategory("IsEnabled"));
        Assert.Equal("Behavior", PropertyValueConverter.GetPropertyCategory("IsVisible"));
    }

    [Fact]
    public void GetPropertyCategory_Common_ReturnsCorrect()
    {
        Assert.Equal("Common", PropertyValueConverter.GetPropertyCategory("Name"));
        Assert.Equal("Common", PropertyValueConverter.GetPropertyCategory("DataContext"));
    }

    [Fact]
    public void IsValidNumeric_Int_ReturnsTrue()
    {
        Assert.True(PropertyValueConverter.IsValidNumeric(typeof(int)));
        Assert.True(PropertyValueConverter.IsValidNumeric(typeof(double)));
        Assert.True(PropertyValueConverter.IsValidNumeric(typeof(float)));
    }

    [Fact]
    public void IsValidNumeric_NonNumeric_ReturnsFalse()
    {
        Assert.False(PropertyValueConverter.IsValidNumeric(typeof(string)));
        Assert.False(PropertyValueConverter.IsValidNumeric(typeof(bool)));
        Assert.False(PropertyValueConverter.IsValidNumeric(typeof(Thickness)));
    }

    [Fact]
    public void TryClearValue_ResetsProperty()
    {
        var button = new Avalonia.Controls.Button();
        var property = Avalonia.Controls.Button.BackgroundProperty;

        var setResult = _converter.TryConvert("red", typeof(Avalonia.Media.IBrush), out var brush, out _);
        Assert.True(setResult);
        button.SetValue(property, brush);

        Assert.True(button.IsSet(property));

        var clearResult = _converter.TryClearValue(property, button, out _);
        Assert.True(clearResult);
        Assert.False(button.IsSet(property));
    }

    [Fact]
    public void TryClearValue_ReadOnlyProperty_ReturnsError()
    {
        var button = new Avalonia.Controls.Button();
        var readOnlyProperty = Avalonia.Controls.Button.IsPressedProperty;

        var result = _converter.TryClearValue(readOnlyProperty, button, out var error);

        Assert.False(result);
        Assert.NotNull(error);
        Assert.Contains("read-only", error, StringComparison.OrdinalIgnoreCase);
    }
}
