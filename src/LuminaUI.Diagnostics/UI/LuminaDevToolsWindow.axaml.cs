using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LuminaUI.Diagnostics.UI.ViewModels;

namespace LuminaUI.Diagnostics.UI;

public partial class LuminaDevToolsWindow : LuminaUI.Controls.LuminaWindow
{
    private readonly IDiagnosticsClient _client;
    private readonly LuminaDevToolsViewModel _viewModel;
    private bool _diagnosticsDisposed;

    internal event EventHandler? DiagnosticsDisposed;

    public LuminaDevToolsWindow()
        : this(null)
    {
    }

    public LuminaDevToolsWindow(IDiagnosticsClient? client)
    {
        InitializeComponent();
        _client = client ?? new InProcessDiagnosticsClient();
        _viewModel = new LuminaDevToolsViewModel(_client);
        DataContext = _viewModel;
        _viewModel.PickedNodeLocated += OnPickedNodeLocated;
        _viewModel.Settings.PropertyChanged += OnSettingsPropertyChanged;
        ApplySettings();
    }

    private async void OnPickedNodeLocated(object? sender, VisualTreeNodeViewModel node)
    {
        await BringNodeIntoViewAsync(node);
    }

    private async Task BringNodeIntoViewAsync(VisualTreeNodeViewModel node)
    {
        var (tree, roots) = _viewModel.SelectedTreeTabIndex switch
        {
            1 => (VisualTreeView, _viewModel.VisualTree.AsEnumerable()),
            2 => (LogicalTreeView, _viewModel.LogicalTree.AsEnumerable()),
            _ => (MergedTreeView, _viewModel.MergedTree.AsEnumerable())
        };
        var path = new List<VisualTreeNodeViewModel>();
        if (!TryBuildNodePath(roots, node, path))
            return;

        tree.SelectedItem = node;
        var realizedContainer = await Dispatcher.UIThread.InvokeAsync(
            () => tree.GetVisualDescendants()
                .OfType<TreeViewItem>()
                .FirstOrDefault(item => ReferenceEquals(item.DataContext, node)),
            DispatcherPriority.Loaded);
        if (realizedContainer is not null)
        {
            realizedContainer.IsSelected = true;
            realizedContainer.BringIntoView();
            return;
        }

        ItemsControl itemsControl = tree;
        TreeViewItem? targetContainer = null;
        for (var index = 0; index < path.Count; index++)
        {
            var pathNode = path[index];
            targetContainer = await GetTreeItemContainerAsync(itemsControl, pathNode);
            if (targetContainer is null)
                return;

            if (index < path.Count - 1)
                targetContainer.IsExpanded = true;

            itemsControl = targetContainer;
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            tree.SelectedItem = node;
            if (targetContainer is not null)
            {
                targetContainer.IsSelected = true;
                targetContainer.BringIntoView();
            }
        }, DispatcherPriority.Loaded);
    }

    private static async Task<TreeViewItem?> GetTreeItemContainerAsync(
        ItemsControl itemsControl,
        VisualTreeNodeViewModel node)
    {
        const int maxAttempts = 2;
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            TreeViewItem? container = null;
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                itemsControl.ScrollIntoView(node);
                container = itemsControl.ContainerFromItem(node) as TreeViewItem;
            }, DispatcherPriority.Loaded);

            if (container is not null)
                return container;
        }

        return null;
    }

    private static bool TryBuildNodePath(
        IEnumerable<VisualTreeNodeViewModel> nodes,
        VisualTreeNodeViewModel target,
        ICollection<VisualTreeNodeViewModel> path)
    {
        foreach (var node in nodes)
        {
            path.Add(node);
            if (ReferenceEquals(node, target)
                || TryBuildNodePath(node.Children, target, path))
            {
                return true;
            }

            path.Remove(node);
        }

        return false;
    }

    private void OnNodePointerEntered(object? sender, PointerEventArgs e)
    {
        if (sender is Control control && control.DataContext is VisualTreeNodeViewModel node)
        {
            _viewModel.HoverNode(node);
        }
    }

    private void OnNodePointerExited(object? sender, PointerEventArgs e)
    {
        if (sender is Control control && control.DataContext is VisualTreeNodeViewModel node)
        {
            _viewModel.ClearHover();
        }
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        DisposeDiagnostics();
        base.OnClosing(e);
    }

    protected override void OnClosed(System.EventArgs e)
    {
        DisposeDiagnostics();
        base.OnClosed(e);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        DisposeDiagnostics();
        base.OnDetachedFromVisualTree(e);
    }

    private void DisposeDiagnostics()
    {
        if (_diagnosticsDisposed)
            return;

        _diagnosticsDisposed = true;
        _ = _client.TogglePickerModeAsync(false);
        _ = _client.HighlightElementAsync(null);
        _viewModel.Dispose();
        _viewModel.PickedNodeLocated -= OnPickedNodeLocated;
        _viewModel.Settings.PropertyChanged -= OnSettingsPropertyChanged;
        DataContext = null;
        if (_client is IDisposable disposable)
            disposable.Dispose();
        DiagnosticsDisposed?.Invoke(this, EventArgs.Empty);
    }

    private void OnSettingsPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) =>
        ApplySettings();

    private void ApplySettings()
    {
        var settings = _viewModel.Settings;
        RequestedThemeVariant = settings.Theme switch
        {
            "Light" => ThemeVariant.Light,
            "Dark" => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };
        Topmost = settings.TopMost;
        FontSize = settings.CompactMode ? 12 : 14;

        var dark = settings.Theme == "Dark"
            || settings.Theme == "System" && ActualThemeVariant == ThemeVariant.Dark;
        if (dark)
        {
            SetBrush("DevToolsWindowBackgroundBrush", "#181B20");
            SetBrush("DevToolsSurfaceBrush", "#20242B");
            SetBrush("DevToolsSurfaceMutedBrush", "#262B33");
            SetBrush("DevToolsBorderBrush", "#343B46");
            SetBrush("DevToolsBorderStrongBrush", "#4B5565");
            SetBrush("DevToolsTextBrush", "#F1F4F8");
            SetBrush("DevToolsTextMutedBrush", "#AAB3C0");
            SetBrush("DevToolsTextFaintBrush", "#7E8897");
            SetBrush("DevToolsInputBrush", "#252A31");
            SetBrush("DevToolsHoverBrush", "#2C333D");
        }
        else
        {
            SetBrush("DevToolsWindowBackgroundBrush", "#F3F5F8");
            SetBrush("DevToolsSurfaceBrush", "#FFFFFF");
            SetBrush("DevToolsSurfaceMutedBrush", "#F7F8FA");
            SetBrush("DevToolsBorderBrush", "#DDE2EA");
            SetBrush("DevToolsBorderStrongBrush", "#BCC6D5");
            SetBrush("DevToolsTextBrush", "#202634");
            SetBrush("DevToolsTextMutedBrush", "#687386");
            SetBrush("DevToolsTextFaintBrush", "#9099A9");
            SetBrush("DevToolsInputBrush", "#FAFBFC");
            SetBrush("DevToolsHoverBrush", "#EEF1F6");
        }

        Background = (IBrush?)Resources["DevToolsWindowBackgroundBrush"];
        ContentBackground = Background;
    }

    private void SetBrush(string key, string color)
    {
        if (Resources[key] is SolidColorBrush brush)
            brush.Color = Color.Parse(color);
    }

    internal bool HandleShortcut(KeyEventArgs e)
    {
        var settings = _viewModel.Settings;
        if (MatchesShortcut(settings.PickElementShortcut, e))
        {
            _viewModel.IsPicking = !_viewModel.IsPicking;
            return true;
        }

        if (MatchesShortcut(settings.RefreshShortcut, e))
        {
            _viewModel.RefreshCommand.Execute(null);
            return true;
        }

        if (MatchesShortcut(settings.ToggleTopMostShortcut, e))
        {
            settings.TopMost = !settings.TopMost;
            return true;
        }

        return false;
    }

    private static bool MatchesShortcut(string shortcut, KeyEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(shortcut))
            return false;

        var parts = shortcut.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0 || !Enum.TryParse<Key>(parts[^1], ignoreCase: true, out var key) || e.Key != key)
            return false;

        var required = KeyModifiers.None;
        foreach (var modifier in parts[..^1])
        {
            required |= modifier.ToUpperInvariant() switch
            {
                "CTRL" or "CONTROL" => KeyModifiers.Control,
                "SHIFT" => KeyModifiers.Shift,
                "ALT" => KeyModifiers.Alt,
                "META" or "CMD" or "COMMAND" => KeyModifiers.Meta,
                _ => KeyModifiers.None
            };
        }

        return e.KeyModifiers == required;
    }

    protected override async void OnOpened(System.EventArgs e)
    {
        base.OnOpened(e);
        await _viewModel.InitializeAsync();
    }
}
