using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace LuminaUI.Diagnostics.UI.ViewModels;

public class VisualTreeNodeViewModel : INotifyPropertyChanged
{
    private bool _isExpanded;
    private bool _isSelected;

    public VisualTreeNodeViewModel(object control)
    {
        Control = control;
        Children = new ObservableCollection<VisualTreeNodeViewModel>();
        
        var type = control.GetType();
        Type = type.Name;
        
        if (control is Avalonia.StyledElement styled)
        {
            Name = styled.Name;
            Classes = string.Join(" ", styled.Classes);
        }
    }

    public VisualTreeNodeViewModel(string type, string? name, string? classes, object? control, string? nodeId = null)
    {
        Type = type;
        Name = name;
        Classes = classes ?? "";
        Control = control;
        NodeId = nodeId;
        Children = new ObservableCollection<VisualTreeNodeViewModel>();
    }

    public object? Control { get; }
    public string? NodeId { get; }
    public object? Identity => Control ?? NodeId;
    public string Type { get; }
    public string? Name { get; }
    public string? Classes { get; }
    public bool IsReadOnly => Identity is null;
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? string.Empty : $"#{Name}";
    public string DisplayClasses
    {
        get
        {
            var classes = (Classes ?? string.Empty)
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(value => DevToolsSettingsStore.Current.InlinePseudoclasses || !value.StartsWith(':'));
            var display = string.Join(" .", classes);
            return display.Length == 0 ? string.Empty : $".{display}";
        }
    }

    public ObservableCollection<VisualTreeNodeViewModel> Children { get; }

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public string DisplayText
    {
        get
        {
            var text = Type;
            if (!string.IsNullOrEmpty(Name))
                text += $" '{Name}'";
            if (!string.IsNullOrEmpty(Classes))
                text += $" .{Classes.Replace(" ", " .")}";
            return text;
        }
    }

    public VisualTreeNodeViewModel CloneShallow() => new(Type, Name, Classes, Control, NodeId)
    {
        IsExpanded = IsExpanded,
        IsSelected = IsSelected
    };

    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}
