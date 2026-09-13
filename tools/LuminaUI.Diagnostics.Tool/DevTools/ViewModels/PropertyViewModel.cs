using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using LuminaUI.Localization;

namespace LuminaUI.Diagnostics.UI.ViewModels;

public class PropertyViewModel : INotifyPropertyChanged
{
    private object? _value;
    private string? _stringValue;
    private bool _isSet;
    private string? _error;
    private bool _canEdit;
    private Action<object?>? _setterAction;

    public PropertyViewModel(string name, string propertyType, string ownerType, bool isAttached, bool isDirect)
    {
        Name = name;
        PropertyType = propertyType;
        OwnerType = ownerType;
        IsAttached = isAttached;
        IsDirect = isDirect;
        ChoiceValues = [];
        ClearLocalValueCommand = new PropertyCommand(ClearLocalValue, () => CanClearLocalValue);
    }

    public string Name { get; }
    public string PropertyType { get; }
    public string OwnerType { get; }
    public bool IsAttached { get; }
    public bool IsDirect { get; }
    public string Category { get; set; } = "Misc";

    public bool IsReadOnly { get; set; }
    public bool IsEnum { get; set; }
    public IReadOnlyList<string> EnumValues
    {
        get => ChoiceValues;
        set => ChoiceValues = value;
    }
    public IReadOnlyList<string> ChoiceValues { get; set; }
    public bool IsNumeric { get; set; }
    public bool IsBoolean { get; set; }
    public PropertyEditorKind EditorKind { get; set; } = PropertyEditorKind.ReadOnly;
    public bool CanEdit
    {
        get => _canEdit;
        set
        {
            if (!SetProperty(ref _canEdit, value))
                return;

            OnPropertyChanged(nameof(IsEditorDisabled));
            OnPropertyChanged(nameof(CanClearLocalValue));
            OnPropertyChanged(nameof(ValueSourceDisplay));
            OnPropertyChanged(nameof(ShowValueSource));
            ((PropertyCommand)ClearLocalValueCommand).RaiseCanExecuteChanged();
        }
    }
    public bool IsEditorDisabled => !CanEdit;
    public ICommand ClearLocalValueCommand { get; }
    public bool HasError => !string.IsNullOrWhiteSpace(Error);
    public bool ShowValueSource => IsSet || IsEditorDisabled;
    public bool CanClearLocalValue => IsSet && CanEdit && SetterAction is not null;
    public string ValueSourceDisplay => IsReadOnly
        ? L("DevTools.Property.Source.ReadOnly", "Read only")
        : !CanEdit
            ? L("DevTools.Property.Source.InspectOnly", "Inspect only")
        : IsSet
            ? L("DevTools.Property.Source.Local", "Local")
            : L("DevTools.Property.Source.Effective", "Effective");
    public string DisplayValue => FormatDisplayValue(Value, PropertyType);

    public object? Value
    {
        get => _value;
        set
        {
            if (SetProperty(ref _value, value))
            {
                StringValue = _value?.ToString();
                OnPropertyChanged(nameof(DisplayValue));
            }
        }
    }

    public string? StringValue
    {
        get => _stringValue;
        set
        {
            if (SetProperty(ref _stringValue, value))
            {
                UpdateValue(value);
            }
        }
    }

    public bool IsSet
    {
        get => _isSet;
        set
        {
            if (!SetProperty(ref _isSet, value))
                return;

            OnPropertyChanged(nameof(CanClearLocalValue));
            OnPropertyChanged(nameof(ValueSourceDisplay));
            OnPropertyChanged(nameof(ShowValueSource));
            ((PropertyCommand)ClearLocalValueCommand).RaiseCanExecuteChanged();
        }
    }

    public string? Error
    {
        get => _error;
        set
        {
            if (SetProperty(ref _error, value))
                OnPropertyChanged(nameof(HasError));
        }
    }
    
    public Action<object?>? SetterAction
    {
        get => _setterAction;
        set
        {
            if (ReferenceEquals(_setterAction, value))
                return;

            _setterAction = value;
            OnPropertyChanged(nameof(CanClearLocalValue));
            ((PropertyCommand)ClearLocalValueCommand).RaiseCanExecuteChanged();
        }
    }

    public void UpdateValue(object? newValue)
    {
        SetterAction?.Invoke(newValue);
    }

    private void ClearLocalValue() => SetterAction?.Invoke(null);

    internal static string FormatFontFamilyName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var names = value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(part =>
            {
                var marker = part.LastIndexOf('#');
                return marker >= 0 ? part[(marker + 1)..] : part;
            })
            .Where(part => !part.StartsWith('$'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return names.Length == 0 ? value : string.Join(", ", names);
    }

    private static string FormatDisplayValue(object? value, string propertyType)
    {
        if (value is null)
            return L("DevTools.Fallback.None", "(none)");

        var text = value.ToString() ?? string.Empty;
        if (propertyType == nameof(Avalonia.Media.FontFamily))
            return FormatFontFamilyName(text);

        var type = value.GetType();
        return text == type.FullName ? type.Name : text;
    }

    private static string L(string key, string fallback) =>
        LuminaLocalization.TryGet(key, out var value) && !string.IsNullOrEmpty(value) ? value : fallback;

    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (System.Collections.Generic.EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }

    private void OnPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

internal sealed class PropertyCommand : ICommand
{
    private readonly Action _execute;
    private readonly Func<bool> _canExecute;

    public PropertyCommand(Action execute, Func<bool> canExecute)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => _canExecute();
    public void Execute(object? parameter) => _execute();
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
