using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Media;
using LuminaUI.Diagnostics.UI.ViewModels;
using LuminaUI.Localization;
using System;
using System.Globalization;

namespace LuminaUI.Diagnostics.UI.Controls;

public class PropertyValueEditor : ContentControl
{
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        Content = CreateEditor();
    }

    private Control? CreateEditor()
    {
        if (DataContext is not PropertyViewModel property)
            return null;

        if (!property.CanEdit)
            return CreateReadOnlyViewer(property);

        return property.EditorKind switch
        {
            PropertyEditorKind.Boolean => CreateBooleanEditor(property),
            PropertyEditorKind.Choice => CreateChoiceEditor(property),
            PropertyEditorKind.Numeric => CreateNumericEditor(property),
            PropertyEditorKind.Compound => CreateThicknessEditor(property),
            PropertyEditorKind.Color => CreateColorEditor(property),
            PropertyEditorKind.FontFamily => CreateFontFamilyEditor(property),
            _ => CreateTextEditor(property)
        };
    }

    private static Control CreateReadOnlyViewer(PropertyViewModel property)
    {
        var value = new TextBlock
        {
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            FontSize = 11
        };
        value.Classes.Add("devtool-readonly-text");
        value.Bind(TextBlock.TextProperty, new Avalonia.Data.Binding(nameof(PropertyViewModel.DisplayValue))
        {
            Source = property,
            Mode = BindingMode.OneWay
        });
        ToolTip.SetTip(value, property.DisplayValue);

        var border = new Border
        {
            Child = value,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
            MaxWidth = 360
        };
        border.Classes.Add("devtool-readonly-value");
        return border;
    }

    private static Control CreateBooleanEditor(PropertyViewModel property)
    {
        var toggle = new ToggleSwitch
        {
            Margin = new Thickness(0),
            OnContent = null,
            OffContent = null
        };
        toggle.Classes.Add("devtool-switch");
        toggle.Bind(ToggleSwitch.IsCheckedProperty, new Avalonia.Data.Binding(nameof(PropertyViewModel.Value))
        {
            Source = property,
            Mode = BindingMode.TwoWay
        });
        return toggle;
    }

    private static Control CreateChoiceEditor(PropertyViewModel property)
    {
        var choice = new ComboBox
        {
            ItemsSource = property.ChoiceValues,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch
        };
        choice.Classes.Add("devtool-value");
        choice.Bind(SelectingItemsControl.SelectedItemProperty, new Avalonia.Data.Binding(nameof(PropertyViewModel.Value))
        {
            Source = property,
            Mode = BindingMode.TwoWay,
            Converter = new ObjectToStringConverter()
        });
        return choice;
    }

    private static Control CreateNumericEditor(PropertyViewModel property)
    {
        var number = new NumericUpDown
        {
            ShowButtonSpinner = true,
            AllowSpin = true,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
            PlaceholderText = L("DevTools.Property.Auto", "Auto")
        };
        number.Classes.Add("devtool-value");
        number.Bind(NumericUpDown.ValueProperty, new Avalonia.Data.Binding(nameof(PropertyViewModel.Value))
        {
            Source = property,
            Mode = BindingMode.TwoWay,
            Converter = new ObjectToDecimalConverter()
        });
        return number;
    }

    private static Control CreateTextEditor(PropertyViewModel property)
    {
        var text = new TextBox
        {
            PlaceholderText = property.PropertyType,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
            TextWrapping = TextWrapping.NoWrap
        };
        text.Classes.Add("devtool-value");
        ToolTip.SetTip(text, property.StringValue);
        text.Bind(TextBox.TextProperty, new Avalonia.Data.Binding(nameof(PropertyViewModel.Value))
        {
            Source = property,
            Mode = BindingMode.TwoWay,
            Converter = new ObjectToStringConverter()
        });
        return text;
    }

    private Control CreateThicknessEditor(PropertyViewModel property)
    {
        var border = new Border
        {
            ClipToBounds = true
        };
        border.Classes.Add("devtool-compound");

        var sp = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,*,Auto,*,Auto,*") };
        border.Child = sp;

        TextBox CreateBox(string placeholder, int col) 
        {
            var t = new TextBox 
            { 
                PlaceholderText = placeholder, 
                Background = Brushes.Transparent,
                BorderBrush = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(2,4),
                TextAlignment = Avalonia.Media.TextAlignment.Center,
                FontSize = 12,
                MinWidth = 0,
                MinHeight = 28,
                InnerLeftContent = CreateAxisLabel(placeholder)
            };
            t.Classes.Add("devtool-inline-value");
            t.SetValue(Grid.ColumnProperty, col);
            sp.Children.Add(t);
            return t;
        }
        
        void AddDivider(int col)
        {
            var d = new Border { Width = 1, Margin = new Thickness(0,4) };
            d.Classes.Add("devtool-compound-divider");
            d.SetValue(Grid.ColumnProperty, col);
            sp.Children.Add(d);
        }

        var left = CreateBox(L("DevTools.Thickness.Left", "L"), 0); AddDivider(1);
        var top = CreateBox(L("DevTools.Thickness.Top", "T"), 2); AddDivider(3);
        var right = CreateBox(L("DevTools.Thickness.Right", "R"), 4); AddDivider(5);
        var bottom = CreateBox(L("DevTools.Thickness.Bottom", "B"), 6);
        
        var isUpdating = false;

        void UpdateFromProperty() 
        {
            isUpdating = true;
            if (property.Value is Thickness t) { left.Text = t.Left.ToString(); top.Text = t.Top.ToString(); right.Text = t.Right.ToString(); bottom.Text = t.Bottom.ToString(); }
            if (property.Value is CornerRadius c) { left.Text = c.TopLeft.ToString(); top.Text = c.TopRight.ToString(); right.Text = c.BottomRight.ToString(); bottom.Text = c.BottomLeft.ToString(); }
            isUpdating = false;
        }

        void UpdateToProperty(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (isUpdating) return;
            if (!double.TryParse(left.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var l)
                || !double.TryParse(top.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var t)
                || !double.TryParse(right.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var r)
                || !double.TryParse(bottom.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var b))
            {
                property.Error = L("DevTools.Property.InvalidNumber", "Enter a valid number.");
                return;
            }

            property.Error = null;
            if (property.PropertyType == "Thickness") property.Value = new Thickness(l, t, r, b);
            if (property.PropertyType == "CornerRadius") property.Value = new CornerRadius(l, t, r, b);
        }

        left.LostFocus += UpdateToProperty;
        top.LostFocus += UpdateToProperty;
        right.LostFocus += UpdateToProperty;
        bottom.LostFocus += UpdateToProperty;

        property.PropertyChanged += (s, e) => { if (e.PropertyName == nameof(PropertyViewModel.Value)) Avalonia.Threading.Dispatcher.UIThread.Post(UpdateFromProperty); };
        UpdateFromProperty();

        return border;
    }

    private static string L(string key, string fallback) =>
        LuminaLocalization.TryGet(key, out var v) && !string.IsNullOrEmpty(v) ? v : fallback;

    private static TextBlock CreateAxisLabel(string text)
    {
        var label = new TextBlock
        {
            Text = text,
            FontSize = 10,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 0, 0)
        };
        label.Classes.Add("devtool-axis-label");
        return label;
    }

    private Control CreateColorEditor(PropertyViewModel property)
    {
        var cp = new Avalonia.Controls.ColorPicker
        {
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
            MinWidth = 72
        };
        cp.Classes.Add("devtool-value");
        cp.Bind(Avalonia.Controls.ColorPicker.ColorProperty, new Avalonia.Data.Binding(nameof(PropertyViewModel.Value)) 
        { 
            Source = property, 
            Mode = BindingMode.TwoWay,
            Converter = new ObjectToColorConverter(property.PropertyType != nameof(Color))
        });

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 8 };
        grid.Children.Add(cp);
        var value = new TextBlock
        {
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            FontSize = 11
        };
        value.Classes.Add("devtool-color-value");
        value.SetValue(Grid.ColumnProperty, 1);
        value.Bind(TextBlock.TextProperty, new Avalonia.Data.Binding(nameof(PropertyViewModel.StringValue))
        {
            Source = property,
            Mode = BindingMode.OneWay
        });
        grid.Children.Add(value);
        return grid;
    }

    private static Control CreateFontFamilyEditor(PropertyViewModel property)
    {
        var current = PropertyViewModel.FormatFontFamilyName(property.Value?.ToString());
        IReadOnlyList<string> families;
        try
        {
            families = FontManager.Current.SystemFonts
                .Select(font => PropertyViewModel.FormatFontFamilyName(font.ToString()))
                .Append(current)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
        }
        catch
        {
            families = string.IsNullOrWhiteSpace(current) ? [] : [current];
        }

        var choice = new ComboBox
        {
            ItemsSource = families,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch
        };
        choice.Classes.Add("devtool-value");
        choice.Classes.Add("devtool-font-family");
        choice.Bind(SelectingItemsControl.SelectedItemProperty, new Avalonia.Data.Binding(nameof(PropertyViewModel.Value))
        {
            Source = property,
            Mode = BindingMode.TwoWay,
            Converter = new FontFamilyNameConverter()
        });
        return choice;
    }

    private class ObjectToStringConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return value?.ToString();
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return value;
        }
    }

    private class ObjectToDecimalConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            try
            {
                if (value == null) return null;
                return System.Convert.ToDecimal(value);
            }
            catch
            {
                return null;
            }
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return value;
        }
    }

    private class BrushConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is IBrush brush) return brush;
            if (value is Color color) return new SolidColorBrush(color);
            return Brushes.Transparent;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return value;
        }
    }

    private sealed class FontFamilyNameConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            PropertyViewModel.FormatFontFamilyName(value?.ToString());

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            value?.ToString();
    }

    private sealed class ObjectToColorConverter(bool returnBrush) : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is ISolidColorBrush solid) return solid.Color;
            if (value is Color c) return c;
            return Colors.Transparent;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return returnBrush && value is Color color ? new SolidColorBrush(color) : value;
        }
    }
}
