using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using LuminaUI.Diagnostics.HotReload;
using LuminaUI.Localization;

namespace LuminaUI.Diagnostics.UI.ViewModels;

public class LuminaDevToolsViewModel : INotifyPropertyChanged, IDisposable
{
    private const string ElementsSection = "Elements";
    private const string ResourcesSection = "Resources";
    private const string AssetsSection = "Assets";
    private const string BindingsSection = "Bindings";
    private const string MetricsSection = "Metrics";
    private const string SettingsSection = "Settings";
    private const string StylesInspector = "Styles";
    private const string BindingsInspector = "Bindings";

    private readonly IDiagnosticsClient _client;
    private CancellationTokenSource? _operationCts;
    private readonly object _ctsGate = new();
    private readonly Avalonia.Threading.DispatcherTimer _metricsTimer;
    private CancellationTokenSource? _hoverCts;
    private bool _suppressSelectedNodeLoad;
    private int _selectionVersion;
    private bool _disposed;
    private string _statusMessage = string.Empty;
    private bool _isStatusError;
    private bool _isBusy;

    public void HoverNode(VisualTreeNodeViewModel node)
    {
        QueueHighlight(node);
    }

    public void ClearHover()
    {
        if (!IsPicking)
            QueueHighlight(null);
    }

    private void QueueHighlight(VisualTreeNodeViewModel? node)
    {
        _hoverCts?.Cancel();
        _hoverCts?.Dispose();
        _hoverCts = new CancellationTokenSource();
        _ = HighlightAfterDelayAsync(node, _hoverCts.Token);
    }

    private async Task HighlightAfterDelayAsync(VisualTreeNodeViewModel? node, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(20, cancellationToken).ConfigureAwait(true);
            await _client.HighlightElementAsync(node, cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            SetStatus($"Highlight element failed: {ex.Message}", isError: true);
        }
    }

    private CancellationTokenSource BeginOperation()
    {
        lock (_ctsGate)
        {
            if (_operationCts is { IsCancellationRequested: false, Token.IsCancellationRequested: false })
                _operationCts.Cancel();

            _operationCts?.Dispose();
            _operationCts = new CancellationTokenSource();
            return _operationCts;
        }
    }

    private void CancelPendingOperations()
    {
        lock (_ctsGate)
        {
            if (_operationCts is { IsCancellationRequested: false })
            {
                try { _operationCts.Cancel(); }
                catch (ObjectDisposedException) { }
            }
        }
    }

    private VisualTreeNodeViewModel? _selectedNode;

    public LuminaDevToolsViewModel(IDiagnosticsClient client)
    {
        _client = client;
        Settings = DevToolsSettingsStore.Current;
        Settings.PropertyChanged += OnSettingsChanged;
        _client.ElementPicked += OnElementPicked;
        MergedTree = new ObservableCollection<VisualTreeNodeViewModel>();
        VisualTree = new ObservableCollection<VisualTreeNodeViewModel>();
        LogicalTree = new ObservableCollection<VisualTreeNodeViewModel>();
        Properties = new ObservableCollection<PropertyViewModel>();
        Resources = new ObservableCollection<ResourceEntryViewModel>();
        Assets = new ObservableCollection<AssetEntryViewModel>();
        BindingErrors = new ObservableCollection<BindingErrorEntryViewModel>();
        BindingExpressions = new ObservableCollection<BindingExpressionEntryViewModel>();
        RefreshCommand = new DevToolsCommand(_ => _ = SafeFireAsync(RefreshCurrentAsync));
        ResetPropertyFiltersCommand = new DevToolsCommand(_ => ResetPropertyFilters());
        SelectSectionCommand = new DevToolsCommand(SelectSection);
        SelectInspectorTabCommand = new DevToolsCommand(SelectInspectorTab);
        RefreshResourcesCommand = new DevToolsCommand(_ => _ = SafeFireAsync(LoadResourcesAsync));
        RefreshAssetsCommand = new DevToolsCommand(_ => _ = SafeFireAsync(LoadAssetsAsync));
        RefreshBindingsCommand = new DevToolsCommand(_ => _ = SafeFireAsync(LoadBindingErrorsAsync));

        HotReloadManager.OnTypesUpdated += OnHotReloaded;
        HotReloadManager.StatusChanged += OnHotReloadStatusChanged;
        ApplyHotReloadStatus(HotReloadManager.Status);
        _metricsTimer = new Avalonia.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(Settings.MetricsRefreshIntervalMs)
        };
        _metricsTimer.Tick += OnMetricsTimerTick;
        _metricsTimer.Start();
    }

    public ICommand RefreshCommand { get; }
    public ICommand ResetPropertyFiltersCommand { get; }
    public ICommand SelectSectionCommand { get; }
    public ICommand SelectInspectorTabCommand { get; }
    public ICommand RefreshResourcesCommand { get; }
    public ICommand RefreshAssetsCommand { get; }
    public ICommand RefreshBindingsCommand { get; }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public bool IsStatusError
    {
        get => _isStatusError;
        private set => SetProperty(ref _isStatusError, value);
    }

    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetProperty(ref _isBusy, value))
                return;

            OnPropertyChanged(nameof(ShowNoSelection));
            OnPropertyChanged(nameof(ShowNoProperties));
            OnPropertyChanged(nameof(ShowResourcesEmpty));
            OnPropertyChanged(nameof(ShowSelectedResourceEmpty));
            OnPropertyChanged(nameof(ShowBindingsEmpty));
            OnPropertyChanged(nameof(ShowVisualTreeEmpty));
            OnPropertyChanged(nameof(ShowLogicalTreeEmpty));
            OnPropertyChanged(nameof(ShowMergedTreeEmpty));
        }
    }

    public string ConnectionStatusText => L("DevTools.Status.Connected", "Connected");

    public DevToolsSettings Settings { get; }

    public IReadOnlyList<string> ThemeOptions { get; } = ["System", "Light", "Dark"];
    public IReadOnlyList<string> TreeKindOptions { get; } = ["Merged", "Visual", "Logical"];

    public int SelectedTreeTabIndex
    {
        get => Settings.DefaultTreeKind switch
        {
            "Visual" => 1,
            "Logical" => 2,
            _ => 0
        };
        set
        {
            Settings.DefaultTreeKind = value switch
            {
                1 => "Visual",
                2 => "Logical",
                _ => "Merged"
            };
            OnPropertyChanged(nameof(SelectedTreeTabIndex));
        }
    }

    public Task InitializeAsync() => SafeFireAsync(RefreshTreesAsync);

    private void OnHotReloaded(Type[] updatedTypes)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            _ = SafeFireAsync(RefreshTreesAsync);
        });
    }

    private void OnHotReloadStatusChanged(HotReloadStatus status)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() => ApplyHotReloadStatus(status));
    }

    private void ApplyHotReloadStatus(HotReloadStatus status) =>
        SetStatus(status.Error ?? status.Message, status.Error is not null);

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DevToolsSettings.DefaultTreeKind))
            OnPropertyChanged(nameof(SelectedTreeTabIndex));

        if (e.PropertyName == nameof(DevToolsSettings.MetricsRefreshIntervalMs))
            _metricsTimer.Interval = TimeSpan.FromMilliseconds(Settings.MetricsRefreshIntervalMs);

        if (e.PropertyName is nameof(DevToolsSettings.DefaultExpandDepth)
            or nameof(DevToolsSettings.MaxTreeDepth)
            or nameof(DevToolsSettings.AggregateTemplateSubtrees))
        {
            _ = SafeFireAsync(RefreshTreesAsync);
        }
    }

    private void OnMetricsTimerTick(object? sender, EventArgs e)
    {
        if (IsMetricsSection)
            _ = SafeFireAsync(LoadMetricsAsync);
    }

    private void OnElementPicked(object? sender, object element)
    {
        _isPicking = false;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsPicking)));

        var (node, treeTabIndex) = FindPickedNode(element);
        if (node is null)
            return;

        if (SelectedTreeTabIndex != treeTabIndex)
            SelectedTreeTabIndex = treeTabIndex;

        _suppressSelectedNodeLoad = true;
        try
        {
            SelectNodeInTree(treeTabIndex, node);
            node.IsSelected = true;
        }
        finally
        {
            _suppressSelectedNodeLoad = false;
        }

        PickedNodeLocated?.Invoke(this, node);
        QueuePickedNodeLoad(node, _selectionVersion);
    }

    private void QueuePickedNodeLoad(VisualTreeNodeViewModel node, int selectionVersion)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (_disposed
                || selectionVersion != _selectionVersion
                || !ReferenceEquals(SelectedNode, node))
            {
                return;
            }

            _ = SafeFireAsync(ct => LoadSelectedNodeAsync(node, ct));
        }, Avalonia.Threading.DispatcherPriority.Background);
    }

    private void SelectNodeInTree(int treeTabIndex, VisualTreeNodeViewModel node)
    {
        switch (treeTabIndex)
        {
            case 1:
                SelectedVisualNode = node;
                break;
            case 2:
                SelectedLogicalNode = node;
                break;
            default:
                SelectedMergedNode = node;
                break;
        }
    }

    private (VisualTreeNodeViewModel? Node, int TreeTabIndex) FindPickedNode(object element)
    {
        var currentTabIndex = SelectedTreeTabIndex;
        var searchOrder = currentTabIndex switch
        {
            1 => new[] { 1, 0, 2 },
            2 => new[] { 2, 0, 1 },
            _ => new[] { 0, 1, 2 }
        };

        foreach (var treeTabIndex in searchOrder)
        {
            var nodes = treeTabIndex switch
            {
                1 => VisualTree,
                2 => LogicalTree,
                _ => MergedTree
            };
            var node = FindNodeAndExpand(nodes, element);
            if (node is not null)
                return (node, treeTabIndex);
        }

        return (null, currentTabIndex);
    }

    private static VisualTreeNodeViewModel? FindNodeAndExpand(IEnumerable<VisualTreeNodeViewModel> nodes, object target)
    {
        foreach (var node in nodes)
        {
            if (MatchesTarget(node, target))
                return node;

            var foundChild = FindNodeAndExpand(node.Children, target);
            if (foundChild is not null)
            {
                node.IsExpanded = true;
                return foundChild;
            }
        }
        return null;
    }

    private static bool MatchesTarget(VisualTreeNodeViewModel node, object target)
    {
        if (Equals(node.Identity, target))
            return true;

        if (target is string targetNodeId && !string.IsNullOrWhiteSpace(targetNodeId))
            return string.Equals(node.NodeId, targetNodeId, StringComparison.Ordinal);

        if (node.Control is not null && ReferenceEquals(node.Control, target))
            return true;

        return false;
    }

    private bool _isPicking;
    public bool IsPicking
    {
        get => _isPicking;
        set
        {
            if (SetProperty(ref _isPicking, value))
                _ = ObserveClientOperationAsync(_client.TogglePickerModeAsync(value), "Toggle element picker");
        }
    }

    public ObservableCollection<VisualTreeNodeViewModel> MergedTree { get; }
    public ObservableCollection<VisualTreeNodeViewModel> VisualTree { get; }
    public ObservableCollection<VisualTreeNodeViewModel> LogicalTree { get; }
    private VisualTreeNodeViewModel? _selectedMergedNode;
    private VisualTreeNodeViewModel? _selectedVisualNode;
    private VisualTreeNodeViewModel? _selectedLogicalNode;

    public VisualTreeNodeViewModel? SelectedMergedNode
    {
        get => _selectedMergedNode;
        set => SetTreeSelection(ref _selectedMergedNode, value);
    }

    public VisualTreeNodeViewModel? SelectedVisualNode
    {
        get => _selectedVisualNode;
        set => SetTreeSelection(ref _selectedVisualNode, value);
    }

    public VisualTreeNodeViewModel? SelectedLogicalNode
    {
        get => _selectedLogicalNode;
        set => SetTreeSelection(ref _selectedLogicalNode, value);
    }

    private void SetTreeSelection(
        ref VisualTreeNodeViewModel? field,
        VisualTreeNodeViewModel? value,
        [CallerMemberName] string? propertyName = null)
    {
        if (SetProperty(ref field, value, propertyName) && value is not null)
            SelectedNode = value;
    }

    public ObservableCollection<PropertyViewModel> Properties { get; } = [];
    public ObservableCollection<ResourceEntryViewModel> Resources { get; }
    public ObservableCollection<AssetEntryViewModel> Assets { get; }
    public ObservableCollection<BindingErrorEntryViewModel> BindingErrors { get; }
    public ObservableCollection<BindingExpressionEntryViewModel> BindingExpressions { get; }

    private string _selectedSection = ElementsSection;
    public string SelectedSection
    {
        get => _selectedSection;
        private set
        {
            if (!SetProperty(ref _selectedSection, value))
                return;

            OnPropertyChanged(nameof(IsElementsSection));
            OnPropertyChanged(nameof(IsResourcesSection));
            OnPropertyChanged(nameof(IsAssetsSection));
            OnPropertyChanged(nameof(IsBindingsSection));
            OnPropertyChanged(nameof(IsMetricsSection));
            OnPropertyChanged(nameof(IsSettingsSection));
            OnPropertyChanged(nameof(SectionTitle));
        }
    }

    public bool IsElementsSection => SelectedSection == ElementsSection;
    public bool IsResourcesSection => SelectedSection == ResourcesSection;
    public bool IsAssetsSection => SelectedSection == AssetsSection;
    public bool IsBindingsSection => SelectedSection == BindingsSection;
    public bool IsMetricsSection => SelectedSection == MetricsSection;
    public bool IsSettingsSection => SelectedSection == SettingsSection;

    public string SectionTitle => SelectedSection switch
    {
        ResourcesSection => L("DevTools.Section.Resources", "RESOURCES"),
        AssetsSection => L("DevTools.Section.Assets", "ASSETS"),
        BindingsSection => L("DevTools.Section.Bindings", "BINDINGS"),
        MetricsSection => L("DevTools.Section.Metrics", "METRICS"),
        SettingsSection => L("DevTools.Section.Settings", "SETTINGS"),
        _ => L("DevTools.Section.Elements", "ELEMENTS"),
    };

    private string _selectedInspectorTab = StylesInspector;
    public string SelectedInspectorTab
    {
        get => _selectedInspectorTab;
        private set
        {
            if (!SetProperty(ref _selectedInspectorTab, value))
                return;

            OnPropertyChanged(nameof(IsStylesInspector));
            OnPropertyChanged(nameof(IsBindingsInspector));
            OnPropertyChanged(nameof(ShowStylesInspector));
            OnPropertyChanged(nameof(ShowInspectorNoSelection));
        }
    }

    public bool IsStylesInspector => SelectedInspectorTab == StylesInspector;
    public bool IsBindingsInspector => SelectedInspectorTab == BindingsInspector;
    public bool ShowStylesInspector => IsStylesInspector && HasSelection;
    public bool ShowInspectorNoSelection => IsStylesInspector && !HasSelection;

    private string _searchText = string.Empty;
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
                UpdateGroupedProperties();
        }
    }

    public ObservableCollection<CategoryItem> AvailableCategories { get; } =
    [
        new("All Properties", L("DevTools.Property.AllProperties", "All Properties")),
        new("Common", L("DevTools.Category.Common", "Common")),
        new("Layout", L("DevTools.Category.Layout", "Layout")),
        new("Appearance", L("DevTools.Category.Appearance", "Appearance")),
        new("Behavior", L("DevTools.Category.Behavior", "Behavior")),
        new("Automation", L("DevTools.Category.Automation", "Automation")),
        new("Misc", L("DevTools.Category.Misc", "Misc")),
    ];

    private CategoryItem? _selectedCategoryItem;
    public CategoryItem? SelectedCategoryItem
    {
        get => _selectedCategoryItem ?? AvailableCategories[0];
        set
        {
            if (SetProperty(ref _selectedCategoryItem, value))
                UpdateGroupedProperties();
        }
    }

    private static string L(string key, string fallback) =>
        LuminaLocalization.TryGet(key, out var v) && !string.IsNullOrEmpty(v) ? v : fallback;

    private IReadOnlyList<PropertyGroupViewModel> _groupedProperties = [];
    public IReadOnlyList<PropertyGroupViewModel> GroupedProperties
    {
        get => _groupedProperties;
        private set => SetProperty(ref _groupedProperties, value);
    }

    private IReadOnlyList<ResourceTreeNodeViewModel> _resourceTreeRoots = [];
    public IReadOnlyList<ResourceTreeNodeViewModel> ResourceTreeRoots
    {
        get => _resourceTreeRoots;
        private set => SetProperty(ref _resourceTreeRoots, value);
    }

    private ResourceTreeNodeViewModel? _selectedResourceNode;
    private bool _suppressResourceSelectionLoad;
    public ResourceTreeNodeViewModel? SelectedResourceNode
    {
        get => _selectedResourceNode;
        set
        {
            if (!SetProperty(ref _selectedResourceNode, value))
                return;

            OnPropertyChanged(nameof(SelectedResourceEntries));
            OnPropertyChanged(nameof(SelectedResourcePath));
            OnPropertyChanged(nameof(SelectedResourceCount));
            OnPropertyChanged(nameof(HasSelectedResources));
            OnPropertyChanged(nameof(ShowSelectedResourceEmpty));

            if (!_suppressResourceSelectionLoad && value?.Source is not null)
                _ = SafeFireAsync(ct => LoadResourceEntriesAsync(value, ct));
        }
    }

    public IReadOnlyList<ResourceEntryViewModel> SelectedResourceEntries => Resources;
    public string SelectedResourcePath => SelectedResourceNode?.DisplayPath ?? string.Empty;
    public int SelectedResourceCount => SelectedResourceEntries.Count;
    public bool HasSelectedResources => SelectedResourceCount > 0;

    private StyleInspectionViewModel _selectedNodeStyles = StyleInspectionViewModel.Empty;
    public StyleInspectionViewModel SelectedNodeStyles
    {
        get => _selectedNodeStyles;
        private set
        {
            if (SetProperty(ref _selectedNodeStyles, value))
            {
                OnPropertyChanged(nameof(LocalStyleCount));
                OnPropertyChanged(nameof(HasLocalStyles));
                OnPropertyChanged(nameof(HasNoLocalStyles));
                OnPropertyChanged(nameof(HierarchyStyleCount));
                OnPropertyChanged(nameof(HasHierarchyStyles));
                OnPropertyChanged(nameof(HasNoHierarchyStyles));
                OnPropertyChanged(nameof(EffectiveSetterCount));
                OnPropertyChanged(nameof(HasEffectiveSetters));
                OnPropertyChanged(nameof(HasNoEffectiveSetters));
            }
        }
    }

    private IReadOnlyList<MetricItemViewModel> _metrics = [];
    public IReadOnlyList<MetricItemViewModel> Metrics
    {
        get => _metrics;
        private set => SetProperty(ref _metrics, value);
    }

    public string SelectedNodeTitle => SelectedNode?.Type ?? L("DevTools.Fallback.NoSelection", "No Selection");
    public string SelectedNodeName =>
        string.IsNullOrWhiteSpace(SelectedNode?.Name)
            ? L("DevTools.Fallback.Unnamed", "(unnamed)")
            : $"#{SelectedNode.Name}";
    public string SelectedNodeClasses =>
        string.IsNullOrWhiteSpace(SelectedNode?.Classes)
            ? L("DevTools.Fallback.NoClasses", "(no classes)")
            : SelectedNode.Classes;

    public int PropertiesCount => Properties.Count;
    public int VisiblePropertiesCount => GroupedProperties.Sum(group => group.Count);
    private int _resourceCount;
    public int ResourceCount => _resourceCount;
    public int AssetCount => Assets.Count;
    public int BindingErrorCount => BindingErrors.Count;
    public int BindingExpressionCount => BindingExpressions.Count;
    public int LocalStyleCount => SelectedNodeStyles.LocalStyles.Count;

    public bool HasResources => ResourceCount > 0;
    public bool HasNoResources => ResourceCount == 0;
    public bool HasAssets => AssetCount > 0;
    public bool HasNoAssets => AssetCount == 0;
    public bool HasBindingErrors => BindingErrorCount > 0;
    public bool HasNoBindingErrors => BindingErrorCount == 0;
    public bool HasBindingExpressions => BindingExpressionCount > 0;
    public bool HasNoBindingExpressions => BindingExpressionCount == 0;
    public bool HasLocalStyles => LocalStyleCount > 0;
    public bool HasNoLocalStyles => LocalStyleCount == 0;
    public int HierarchyStyleCount => SelectedNodeStyles.HierarchyStyles.Count;
    public bool HasHierarchyStyles => HierarchyStyleCount > 0;
    public bool HasNoHierarchyStyles => HierarchyStyleCount == 0;
    public int EffectiveSetterCount => SelectedNodeStyles.EffectiveSetters.Count;
    public bool HasEffectiveSetters => EffectiveSetterCount > 0;
    public bool HasNoEffectiveSetters => EffectiveSetterCount == 0;
    public bool HasSelection => SelectedNode is not null;
    public bool ShowNoSelection => !IsBusy && !HasSelection;
    public bool ShowNoProperties => !IsBusy && HasSelection && VisiblePropertiesCount == 0;
    public bool ShowResourcesEmpty => !IsBusy && !HasResources;
    public bool ShowSelectedResourceEmpty => !IsBusy && HasResources && !HasSelectedResources;
    public bool ShowBindingsEmpty => !IsBusy && !HasBindingErrors;
    public bool HasVisualTree => VisualTree.Count > 0;
    public bool HasLogicalTree => LogicalTree.Count > 0;
    public bool HasMergedTree => MergedTree.Count > 0;
    public bool ShowVisualTreeEmpty => !IsBusy && !HasVisualTree;
    public bool ShowLogicalTreeEmpty => !IsBusy && !HasLogicalTree;
    public bool ShowMergedTreeEmpty => !IsBusy && !HasMergedTree;

    public VisualTreeNodeViewModel? SelectedNode
    {
        get => _selectedNode;
        set
        {
            if (SetProperty(ref _selectedNode, value))
            {
                _selectionVersion++;
                OnPropertyChanged(nameof(SelectedNodeTitle));
                OnPropertyChanged(nameof(SelectedNodeName));
                OnPropertyChanged(nameof(SelectedNodeClasses));
                OnPropertyChanged(nameof(HasSelection));
                OnPropertyChanged(nameof(ShowNoSelection));
                OnPropertyChanged(nameof(ShowNoProperties));
                OnPropertyChanged(nameof(ShowStylesInspector));
                OnPropertyChanged(nameof(ShowInspectorNoSelection));
                if (!_suppressSelectedNodeLoad)
                    _ = SafeFireAsync(ct => LoadSelectedNodeAsync(value, ct));
            }
        }
    }

    public PropertyViewModel? LayoutMarginProperty => Properties.FirstOrDefault(p => p.Name == "Margin");
    public PropertyViewModel? LayoutPaddingProperty => Properties.FirstOrDefault(p => p.Name == "Padding");
    public PropertyViewModel? LayoutBorderProperty => Properties.FirstOrDefault(p => p.Name == "BorderThickness");
    public PropertyViewModel? LayoutWidthProperty => Properties.FirstOrDefault(p => p.Name == "Width");
    public PropertyViewModel? LayoutHeightProperty => Properties.FirstOrDefault(p => p.Name == "Height");
    public PropertyViewModel? LayoutBoundsProperty => Properties.FirstOrDefault(p => p.Name == "Bounds");
    public string LayoutMarginDisplay => LayoutMarginProperty?.StringValue ?? "0";
    public string LayoutPaddingDisplay => LayoutPaddingProperty?.StringValue ?? "0";
    public string LayoutBorderDisplay => LayoutBorderProperty?.StringValue ?? "0";
    public string LayoutBoundsDisplay => LayoutBoundsProperty?.StringValue ?? "—";

    private void ResetPropertyFilters()
    {
        SearchText = string.Empty;
        SelectedCategoryItem = AvailableCategories[0];
    }

    private void SelectSection(object? parameter)
    {
        var section = parameter?.ToString();
        if (string.IsNullOrWhiteSpace(section))
            return;

        SelectedSection = section;
        _ = SafeFireAsync(ct => LoadSectionAsync(section, ct));
    }

    private void SelectInspectorTab(object? parameter)
    {
        var tab = parameter?.ToString();
        if (string.IsNullOrWhiteSpace(tab))
            return;

        SelectedInspectorTab = tab;
        if (tab == BindingsInspector)
            _ = SafeFireAsync(ct => LoadBindingExpressionsAsync(SelectedNode, ct));
    }

    private async Task SafeFireAsync(Func<CancellationToken, Task> operation)
    {
        CancellationTokenSource? operationCts = null;
        try
        {
            operationCts = BeginOperation();
            IsBusy = true;
            await operation(operationCts.Token).ConfigureAwait(true);
            ApplyHotReloadStatus(HotReloadManager.Status);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LuminaDevTools] Operation failed: {ex.Message}");
            SetStatus(ex.Message, isError: true);
        }
        finally
        {
            lock (_ctsGate)
            {
                if (ReferenceEquals(_operationCts, operationCts))
                    IsBusy = false;
            }
        }
    }

    private async Task ObserveClientOperationAsync(Task operation, string operationName)
    {
        try
        {
            await operation.ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            SetStatus($"{operationName} failed: {ex.Message}", isError: true);
        }
    }

    private async Task RefreshCurrentAsync(CancellationToken ct)
    {
        await RefreshTreesAsync(ct);
        await LoadSectionAsync(SelectedSection, ct);
    }

    private Task LoadSectionAsync(string section, CancellationToken ct) =>
        section switch
        {
            ResourcesSection => LoadResourcesAsync(ct),
            AssetsSection => LoadAssetsAsync(ct),
            BindingsSection => LoadBindingErrorsAsync(ct),
            MetricsSection => LoadMetricsAsync(ct),
            _ => Task.CompletedTask,
        };

    private async Task LoadMetricsAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _runtimeMetrics = await _client.GetRuntimeMetricsAsync(ct).ConfigureAwait(true);
        ct.ThrowIfCancellationRequested();
        UpdateMetrics();
    }

    private async Task LoadSelectedNodeAsync(VisualTreeNodeViewModel? node, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        await LoadPropertiesAsync(node, ct);
        ct.ThrowIfCancellationRequested();

        await LoadStylesAsync(node, ct);

        if (IsBindingsInspector)
            await LoadBindingExpressionsAsync(node, ct);
        if (IsBindingsSection)
            await LoadBindingErrorsAsync(ct);

        UpdateMetrics();
    }

    private async Task LoadPropertiesAsync(VisualTreeNodeViewModel? node, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Properties.Clear();

        if (node is null)
        {
            UpdateGroupedProperties();
            NotifyPropertySummaries();
            return;
        }

        var props = await _client.GetPropertiesAsync(node, ct).ConfigureAwait(true);
        ct.ThrowIfCancellationRequested();

        foreach (var p in props)
            Properties.Add(p);

        UpdateGroupedProperties();
        NotifyPropertySummaries();
    }

    private async Task LoadStylesAsync(VisualTreeNodeViewModel? node, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var styles = await _client.GetAppliedStylesAsync(node, ct).ConfigureAwait(true);
        ct.ThrowIfCancellationRequested();
        SelectedNodeStyles = styles;
        UpdateMetrics();
    }

    private async Task LoadResourcesAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Resources.Clear();
        ResourceTreeRoots = [];
        _resourceCount = 0;
        _suppressResourceSelectionLoad = true;
        SelectedResourceNode = null;

        try
        {
            var providers = await _client.GetResourceProvidersAsync(ct).ConfigureAwait(true);
            ct.ThrowIfCancellationRequested();
            ResourceTreeRoots = BuildResourceTree(providers);
            _resourceCount = providers.Sum(provider => provider.Count);
            SelectedResourceNode = FindFirstResourceNode(ResourceTreeRoots, "application/resources")
                ?? FindFirstResourceNode(ResourceTreeRoots);
        }
        finally
        {
            _suppressResourceSelectionLoad = false;
        }

        if (SelectedResourceNode is { } selected)
            await LoadResourceEntriesAsync(selected, ct);

        NotifyResourceSummaries();
        UpdateMetrics();
    }

    private async Task LoadResourceEntriesAsync(ResourceTreeNodeViewModel node, CancellationToken ct)
    {
        Resources.Clear();
        NotifySelectedResourceSummaries();
        if (string.IsNullOrWhiteSpace(node.Source))
            return;

        var resources = await _client.GetResourcesAsync(node.Source, cancellationToken: ct).ConfigureAwait(true);
        ct.ThrowIfCancellationRequested();
        if (!ReferenceEquals(node, SelectedResourceNode))
            return;

        foreach (var resource in resources)
            Resources.Add(resource);
        NotifySelectedResourceSummaries();
    }

    internal static IReadOnlyList<ResourceTreeNodeViewModel> BuildResourceTree(
        IEnumerable<ResourceEntryViewModel> resources) =>
        BuildResourceTree(resources
            .GroupBy(resource => resource.Source, StringComparer.Ordinal)
            .Select(group => new ResourceProviderViewModel(
                group.Key,
                group.First().SourceSegments,
                group.Count())));

    internal static IReadOnlyList<ResourceTreeNodeViewModel> BuildResourceTree(
        IEnumerable<ResourceProviderViewModel> providers)
    {
        var roots = new List<ResourceTreeNodeViewModel>();
        foreach (var provider in providers.OrderBy(provider => provider.Source, StringComparer.Ordinal))
        {
            var segments = provider.SourceSegments.Count > 0
                ? provider.SourceSegments
                : provider.Source.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            var siblings = roots;
            ResourceTreeNodeViewModel? current = null;
            var displayPath = new List<string>();
            for (var depth = 0; depth < segments.Count; depth++)
            {
                var segment = segments[depth];
                var displayName = FormatResourceSegment(segment);
                displayPath.Add(displayName);
                current = siblings.FirstOrDefault(node => string.Equals(node.SegmentKey, segment, StringComparison.Ordinal));
                if (current is null)
                {
                    current = new ResourceTreeNodeViewModel(
                        segment,
                        displayName,
                        string.Join(" / ", displayPath),
                        depth < 2);
                    siblings.Add(current);
                }

                siblings = current.MutableChildren;
            }

            current?.SetProvider(provider.Source, provider.Count);
        }

        return roots;
    }

    private static ResourceTreeNodeViewModel? FindFirstResourceNode(
        IEnumerable<ResourceTreeNodeViewModel> nodes,
        string? source = null)
    {
        foreach (var node in nodes)
        {
            if (node.Count > 0
                && (source is null || string.Equals(node.Source, source, StringComparison.Ordinal)))
            {
                return node;
            }

            var child = FindFirstResourceNode(node.Children, source);
            if (child is not null)
                return child;
        }

        return null;
    }

    private static string FormatResourceSegment(string segment)
    {
        if (string.Equals(segment, "application", StringComparison.OrdinalIgnoreCase))
            return L("DevTools.Resource.ApplicationNode", "Application");
        if (string.Equals(segment, "resources", StringComparison.OrdinalIgnoreCase))
            return L("DevTools.Resource.ResourcesNode", "Resources");
        if (string.Equals(segment, "styles", StringComparison.OrdinalIgnoreCase))
            return L("DevTools.Resource.StylesNode", "Styles");
        if (segment.StartsWith("merged:", StringComparison.Ordinal))
            return $"{L("DevTools.Resource.MergedNode", "Merged")}: {GetProviderLabel(segment)}";
        if (segment.StartsWith("theme:", StringComparison.Ordinal))
            return $"{L("DevTools.Resource.ThemeNode", "Theme")}: {segment[6..]}";
        if (segment.StartsWith("provider:", StringComparison.Ordinal))
            return GetProviderLabel(segment);
        if (segment.StartsWith("style[", StringComparison.Ordinal))
        {
            var end = segment.IndexOf(']');
            if (end > 6 && int.TryParse(segment.AsSpan(6, end - 6), out var index))
                return $"{L("DevTools.Resource.StyleNode", "Style")} {index + 1}";
        }

        return segment;
    }

    private static string GetProviderLabel(string segment)
    {
        var labelStart = segment.IndexOf(':', segment.IndexOf(':') + 1);
        return labelStart >= 0 && labelStart < segment.Length - 1
            ? segment[(labelStart + 1)..]
            : segment[(segment.IndexOf(':') + 1)..];
    }

    private async Task LoadAssetsAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Assets.Clear();
        var assets = await _client.GetAssetsAsync(ct).ConfigureAwait(true);
        ct.ThrowIfCancellationRequested();
        foreach (var asset in assets)
            Assets.Add(asset);

        OnPropertyChanged(nameof(AssetCount));
        OnPropertyChanged(nameof(HasAssets));
        OnPropertyChanged(nameof(HasNoAssets));
        UpdateMetrics();
    }

    private async Task LoadBindingErrorsAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        BindingErrors.Clear();

        var entries = await _client.GetBindingErrorsAsync(cancellationToken: ct).ConfigureAwait(true);
        ct.ThrowIfCancellationRequested();

        foreach (var entry in entries)
            BindingErrors.Add(entry);

        NotifyBindingSummaries();
        UpdateMetrics();
    }

    private async Task LoadBindingExpressionsAsync(VisualTreeNodeViewModel? node, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        BindingExpressions.Clear();
        var entries = await _client.GetBindingExpressionsAsync(node, ct).ConfigureAwait(true);
        ct.ThrowIfCancellationRequested();
        foreach (var entry in entries)
            BindingExpressions.Add(entry);

        OnPropertyChanged(nameof(BindingExpressionCount));
        OnPropertyChanged(nameof(HasBindingExpressions));
        OnPropertyChanged(nameof(HasNoBindingExpressions));
    }

    public async Task RefreshTreesAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        VisualTree.Clear();
        LogicalTree.Clear();
        MergedTree.Clear();

        var visualNodes = await _client.GetVisualTreeAsync(ct).ConfigureAwait(true);
        ct.ThrowIfCancellationRequested();

        foreach (var node in visualNodes)
        {
            ExpandInitialTreeLevels(node, 0, Settings.DefaultExpandDepth);
            VisualTree.Add(node);
        }

        var logicalNodes = await _client.GetLogicalTreeAsync(ct).ConfigureAwait(true);
        ct.ThrowIfCancellationRequested();

        foreach (var node in logicalNodes)
        {
            ExpandInitialTreeLevels(node, 0, Settings.DefaultExpandDepth);
            LogicalTree.Add(node);
        }

        foreach (var node in BuildMergedTree(LogicalTree, VisualTree, Settings.AggregateTemplateSubtrees))
        {
            ExpandInitialTreeLevels(node, 0, Settings.DefaultExpandDepth);
            MergedTree.Add(node);
        }

        OnPropertyChanged(nameof(HasVisualTree));
        OnPropertyChanged(nameof(HasLogicalTree));
        OnPropertyChanged(nameof(HasMergedTree));
        OnPropertyChanged(nameof(ShowVisualTreeEmpty));
        OnPropertyChanged(nameof(ShowLogicalTreeEmpty));
        OnPropertyChanged(nameof(ShowMergedTreeEmpty));

        if (SelectedNode is null && MergedTree.FirstOrDefault() is { } firstNode)
        {
            SelectedNode = firstNode;
        }
        else if (SelectedNode is not null)
        {
            var reSelected = SelectedNode.Identity is not null
                ? FindNodeAndExpand(MergedTree, SelectedNode.Identity)
                : null;
            if (reSelected is not null)
            {
                _selectedNode = reSelected;
                OnPropertyChanged(nameof(SelectedNode));
                OnPropertyChanged(nameof(SelectedNodeTitle));
                OnPropertyChanged(nameof(SelectedNodeName));
                OnPropertyChanged(nameof(SelectedNodeClasses));
                await LoadSelectedNodeAsync(reSelected, ct);
            }
        }

        UpdateMetrics();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    internal event EventHandler<VisualTreeNodeViewModel>? PickedNodeLocated;

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _client.ElementPicked -= OnElementPicked;
        Settings.PropertyChanged -= OnSettingsChanged;
        _metricsTimer.Stop();
        _metricsTimer.Tick -= OnMetricsTimerTick;
        _hoverCts?.Cancel();
        _hoverCts?.Dispose();
        _hoverCts = null;
        HotReloadManager.OnTypesUpdated -= OnHotReloaded;
        HotReloadManager.StatusChanged -= OnHotReloadStatusChanged;
        CancelPendingOperations();
        lock (_ctsGate)
        {
            _operationCts?.Dispose();
            _operationCts = null;
        }

        _selectedNode = null;
        _selectedMergedNode = null;
        _selectedVisualNode = null;
        _selectedLogicalNode = null;
        _selectedResourceNode = null;
        MergedTree.Clear();
        VisualTree.Clear();
        LogicalTree.Clear();
        Properties.Clear();
        Resources.Clear();
        _resourceCount = 0;
        Assets.Clear();
        BindingErrors.Clear();
        BindingExpressions.Clear();
        GroupedProperties = [];
        ResourceTreeRoots = [];
        Metrics = [];
        SelectedNodeStyles = StyleInspectionViewModel.Empty;
    }

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged(string? propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private void SetStatus(string message, bool isError)
    {
        StatusMessage = message;
        IsStatusError = isError;
        OnPropertyChanged(nameof(HasStatusMessage));
    }

    private void NotifyPropertySummaries()
    {
        OnPropertyChanged(nameof(PropertiesCount));
        OnPropertyChanged(nameof(VisiblePropertiesCount));
        OnPropertyChanged(nameof(LayoutMarginProperty));
        OnPropertyChanged(nameof(LayoutPaddingProperty));
        OnPropertyChanged(nameof(LayoutBorderProperty));
        OnPropertyChanged(nameof(LayoutWidthProperty));
        OnPropertyChanged(nameof(LayoutHeightProperty));
        OnPropertyChanged(nameof(LayoutBoundsProperty));
        OnPropertyChanged(nameof(LayoutMarginDisplay));
        OnPropertyChanged(nameof(LayoutPaddingDisplay));
        OnPropertyChanged(nameof(LayoutBorderDisplay));
        OnPropertyChanged(nameof(LayoutBoundsDisplay));
        UpdateMetrics();
    }

    private void NotifyResourceSummaries()
    {
        OnPropertyChanged(nameof(ResourceCount));
        OnPropertyChanged(nameof(HasResources));
        OnPropertyChanged(nameof(HasNoResources));
        OnPropertyChanged(nameof(ShowResourcesEmpty));
        NotifySelectedResourceSummaries();
    }

    private void NotifySelectedResourceSummaries()
    {
        OnPropertyChanged(nameof(SelectedResourceEntries));
        OnPropertyChanged(nameof(SelectedResourceCount));
        OnPropertyChanged(nameof(HasSelectedResources));
        OnPropertyChanged(nameof(ShowSelectedResourceEmpty));
    }

    private void NotifyBindingSummaries()
    {
        OnPropertyChanged(nameof(BindingErrorCount));
        OnPropertyChanged(nameof(HasBindingErrors));
        OnPropertyChanged(nameof(HasNoBindingErrors));
        OnPropertyChanged(nameof(ShowBindingsEmpty));
    }

    private RuntimeMetricsViewModel _runtimeMetrics = RuntimeMetricsViewModel.Empty;

    private void UpdateMetrics()
    {
        Metrics =
        [
            new MetricItemViewModel(L("DevTools.Metrics.VisualNodes", "Visual nodes"), CountNodes(VisualTree).ToString(), L("DevTools.Metrics.Tree", "Tree")),
            new MetricItemViewModel(L("DevTools.Metrics.LogicalNodes", "Logical nodes"), CountNodes(LogicalTree).ToString(), L("DevTools.Metrics.Tree", "Tree")),
            new MetricItemViewModel(L("DevTools.Metrics.Properties", "Properties"), PropertiesCount.ToString(), L("DevTools.Metrics.Selection", "Selection")),
            new MetricItemViewModel(L("DevTools.Metrics.VisibleProperties", "Visible properties"), VisiblePropertiesCount.ToString(), L("DevTools.Metrics.Selection", "Selection")),
            new MetricItemViewModel(L("DevTools.Metrics.Resources", "Resources"), ResourceCount.ToString(), L("DevTools.Metrics.Application", "Application")),
            new MetricItemViewModel(L("DevTools.Metrics.Assets", "Assets"), AssetCount.ToString(), L("DevTools.Metrics.Application", "Application")),
            new MetricItemViewModel(L("DevTools.Metrics.BindingIssues", "Binding issues"), BindingErrorCount.ToString(), L("DevTools.Metrics.Runtime", "Runtime"))
            ,new MetricItemViewModel(L("DevTools.Metrics.ManagedHeap", "Managed heap"), $"{_runtimeMetrics.ManagedHeapBytes / 1024d / 1024d:0.0} MB", L("DevTools.Metrics.Runtime", "Runtime"))
            ,new MetricItemViewModel(L("DevTools.Metrics.WorkingSet", "Working set"), $"{_runtimeMetrics.WorkingSetBytes / 1024d / 1024d:0.0} MB", L("DevTools.Metrics.Runtime", "Runtime"))
            ,new MetricItemViewModel(L("DevTools.Metrics.Threads", "Threads"), _runtimeMetrics.ThreadCount.ToString(), L("DevTools.Metrics.Runtime", "Runtime"))
            ,new MetricItemViewModel(L("DevTools.Metrics.CpuTime", "CPU time"), _runtimeMetrics.CpuTime.ToString(@"hh\:mm\:ss"), L("DevTools.Metrics.Runtime", "Runtime"))
            ,new MetricItemViewModel(L("DevTools.Metrics.GcCollections", "GC collections"), $"{_runtimeMetrics.Generation0Collections} / {_runtimeMetrics.Generation1Collections} / {_runtimeMetrics.Generation2Collections}", L("DevTools.Metrics.Runtime", "Runtime"))
        ];
    }

    private static int CountNodes(IEnumerable<VisualTreeNodeViewModel> nodes)
    {
        var count = 0;
        foreach (var node in nodes)
        {
            count++;
            count += CountNodes(node.Children);
        }
        return count;
    }

    private static void ExpandInitialTreeLevels(VisualTreeNodeViewModel node, int depth, int expandedDepth)
    {
        node.IsExpanded = depth < expandedDepth;
        foreach (var child in node.Children)
            ExpandInitialTreeLevels(child, depth + 1, expandedDepth);
    }

    internal static IReadOnlyList<VisualTreeNodeViewModel> BuildMergedTree(
        IEnumerable<VisualTreeNodeViewModel> logicalRoots,
        IEnumerable<VisualTreeNodeViewModel> visualRoots,
        bool aggregateTemplateSubtrees)
    {
        var visualByControl = new Dictionary<object, VisualTreeNodeViewModel>();
        foreach (var root in visualRoots)
            IndexByControl(root, visualByControl);

        var logicalControls = new HashSet<object>();
        foreach (var root in logicalRoots)
            CollectControls(root, logicalControls);

        return logicalRoots
            .Select(root => CloneLogical(root, visualByControl, logicalControls, aggregateTemplateSubtrees))
            .ToArray();
    }

    private static VisualTreeNodeViewModel CloneLogical(
        VisualTreeNodeViewModel logical,
        IReadOnlyDictionary<object, VisualTreeNodeViewModel> visualByControl,
        IReadOnlySet<object> logicalControls,
        bool aggregateTemplateSubtrees)
    {
        var clone = logical.CloneShallow();

        if (logical.Identity is not null
            && visualByControl.TryGetValue(logical.Identity, out var visual))
        {
            var visualOnly = visual.Children
                .SelectMany(child => CloneVisualOnly(child, logicalControls))
                .ToArray();

            if (visualOnly.Length > 0)
            {
                if (aggregateTemplateSubtrees)
                {
                    var template = new VisualTreeNodeViewModel("/template/", null, null, null)
                    {
                        IsExpanded = false
                    };
                    foreach (var item in visualOnly)
                        template.Children.Add(item);
                    clone.Children.Add(template);
                }
                else
                {
                    foreach (var item in visualOnly)
                        clone.Children.Add(item);
                }
            }
        }

        foreach (var child in logical.Children)
            clone.Children.Add(CloneLogical(child, visualByControl, logicalControls, aggregateTemplateSubtrees));

        return clone;
    }

    private static IEnumerable<VisualTreeNodeViewModel> CloneVisualOnly(
        VisualTreeNodeViewModel visual,
        IReadOnlySet<object> logicalControls)
    {
        if (visual.Identity is not null && logicalControls.Contains(visual.Identity))
            yield break;

        var clone = visual.CloneShallow();
        foreach (var child in visual.Children)
        {
            foreach (var visualOnlyChild in CloneVisualOnly(child, logicalControls))
                clone.Children.Add(visualOnlyChild);
        }

        yield return clone;
    }

    private static void IndexByControl(
        VisualTreeNodeViewModel node,
        IDictionary<object, VisualTreeNodeViewModel> index)
    {
        if (node.Identity is not null)
            index[node.Identity] = node;
        foreach (var child in node.Children)
            IndexByControl(child, index);
    }

    private static void CollectControls(VisualTreeNodeViewModel node, ISet<object> controls)
    {
        if (node.Identity is not null)
            controls.Add(node.Identity);
        foreach (var child in node.Children)
            CollectControls(child, controls);
    }

    private void UpdateGroupedProperties()
    {
        var filtered = Properties.AsEnumerable();

        if (SelectedCategoryItem?.Key != "All Properties")
            filtered = filtered.Where(p => p.Category == SelectedCategoryItem?.Key);

        if (!string.IsNullOrWhiteSpace(SearchText))
            filtered = filtered.Where(p => p.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase));

        var order = new[] { "Common", "Appearance", "Layout", "Behavior", "Automation", "Misc" };
        GroupedProperties = filtered
            .GroupBy(p => p.Category)
            .OrderBy(g => Array.IndexOf(order, g.Key))
            .Select(g => new PropertyGroupViewModel(g.Key, LocalizeCategory(g.Key), g.ToList()))
            .ToList();

        OnPropertyChanged(nameof(VisiblePropertiesCount));
        OnPropertyChanged(nameof(ShowNoProperties));
        UpdateMetrics();
    }

    private static string LocalizeCategory(string category) => category switch
    {
        "Common" => L("DevTools.Category.Common", category),
        "Layout" => L("DevTools.Category.Layout", category),
        "Appearance" => L("DevTools.Category.Appearance", category),
        "Behavior" => L("DevTools.Category.Behavior", category),
        "Automation" => L("DevTools.Category.Automation", category),
        _ => L("DevTools.Category.Misc", category),
    };
}

internal sealed class DevToolsCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Predicate<object?>? _canExecute;

    public DevToolsCommand(Action<object?> execute, Predicate<object?>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;

    public void Execute(object? parameter) => _execute(parameter);

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

public sealed class PropertyGroupViewModel
{
    public PropertyGroupViewModel(string key, string display, IReadOnlyList<PropertyViewModel> properties)
    {
        Key = key;
        Display = display;
        Properties = properties;
    }

    public string Key { get; }
    public string Display { get; }
    public IReadOnlyList<PropertyViewModel> Properties { get; }
    public int Count => Properties.Count;
}

public sealed class ResourceEntryViewModel : INotifyPropertyChanged
{
    private string _editValue;
    private string _error = string.Empty;

    public ResourceEntryViewModel(
        string source,
        string key,
        string keyType,
        string kind,
        string valueType,
        string value,
        Func<string, Task>? setter = null,
        IReadOnlyList<string>? sourceSegments = null,
        bool isDeferred = false)
    {
        Source = source;
        SourceSegments = sourceSegments ?? [];
        Key = key;
        KeyType = keyType;
        Kind = kind;
        IsDeferred = isDeferred;
        ValueType = isDeferred ? "AXAML" : valueType;
        Value = isDeferred ? LocalizeDeferredValue() : value;
        _editValue = Value;
        Setter = setter;
        ApplyCommand = new DevToolsCommand(_ => _ = ApplyAsync(), _ => CanEdit);
    }

    public string Source { get; }
    public IReadOnlyList<string> SourceSegments { get; }
    public string Key { get; }
    public string KeyType { get; }
    public string Kind { get; }
    public string ValueType { get; }
    public string Value { get; }
    public bool IsDeferred { get; }
    public bool CanEdit => Setter is not null;
    public ICommand ApplyCommand { get; }
    public Func<string, Task>? Setter { get; }
    public string EditValue
    {
        get => _editValue;
        set
        {
            if (_editValue == value) return;
            _editValue = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EditValue)));
        }
    }
    public string Error
    {
        get => _error;
        private set
        {
            if (_error == value) return;
            _error = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Error)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasError)));
        }
    }
    public bool HasError => !string.IsNullOrWhiteSpace(Error);
    public event PropertyChangedEventHandler? PropertyChanged;

    private async Task ApplyAsync()
    {
        if (Setter is null)
            return;
        try
        {
            await Setter(EditValue);
            Error = string.Empty;
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
    }

    private static string LocalizeDeferredValue() =>
        LuminaLocalization.TryGet("DevTools.Resource.DeferredValue", out var value)
        && !string.IsNullOrWhiteSpace(value)
            ? value
            : "Deferred AXAML resource";
}

public sealed class ResourceTreeNodeViewModel
{
    internal ResourceTreeNodeViewModel(
        string segmentKey,
        string displayName,
        string displayPath,
        bool isExpanded)
    {
        SegmentKey = segmentKey;
        DisplayName = displayName;
        DisplayPath = displayPath;
        IsExpanded = isExpanded;
    }

    internal string SegmentKey { get; }
    internal List<ResourceTreeNodeViewModel> MutableChildren { get; } = [];
    public string? Source { get; private set; }
    public string DisplayName { get; }
    public string DisplayPath { get; }
    public bool IsExpanded { get; set; }
    public IReadOnlyList<ResourceTreeNodeViewModel> Children => MutableChildren;
    public int Count { get; private set; }
    public int TotalCount => Count + Children.Sum(child => child.TotalCount);

    internal void SetProvider(string source, int count)
    {
        Source = source;
        Count = count;
    }
}

public sealed record ResourceProviderViewModel(
    string Source,
    IReadOnlyList<string> SourceSegments,
    int Count);

public sealed class AssetEntryViewModel
{
    public AssetEntryViewModel(string fileName, string relativePath, string assembly, string uri, long? size)
    {
        FileName = fileName;
        RelativePath = relativePath;
        Assembly = assembly;
        Uri = uri;
        Size = size;
    }

    public string FileName { get; }
    public string RelativePath { get; }
    public string Assembly { get; }
    public string Uri { get; }
    public long? Size { get; }
    public string DisplaySize => Size switch
    {
        null => "—",
        < 1024 => $"{Size} B",
        < 1024 * 1024 => $"{Size / 1024d:0.#} KB",
        _ => $"{Size / 1024d / 1024d:0.#} MB"
    };
}

public sealed class StyleInspectionViewModel
{
    public static StyleInspectionViewModel Empty { get; } = new(
        "No Selection", "", "", "(default)", "(none)", [], [], []);

    public StyleInspectionViewModel(
        string type, string name, string classes, string styleKey,
        string templatedParentType, IReadOnlyList<StyleEntryViewModel> localStyles,
        IReadOnlyList<StyleEntryViewModel>? hierarchyStyles = null,
        IReadOnlyList<string>? effectiveSetters = null)
    {
        Type = type;
        Name = name;
        Classes = classes;
        StyleKey = styleKey;
        TemplatedParentType = templatedParentType;
        LocalStyles = localStyles;
        HierarchyStyles = hierarchyStyles ?? [];
        EffectiveSetters = effectiveSetters ?? [];
    }

    public string Type { get; }
    public string Name { get; }
    public string Classes { get; }
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? "(unnamed)" : $"#{Name}";
    public string DisplayClasses => string.IsNullOrWhiteSpace(Classes) ? "(no classes)" : Classes;
    public string StyleKey { get; }
    public string TemplatedParentType { get; }
    public IReadOnlyList<StyleEntryViewModel> LocalStyles { get; }
    public IReadOnlyList<StyleEntryViewModel> HierarchyStyles { get; }
    public IReadOnlyList<string> EffectiveSetters { get; }
}

public sealed class StyleEntryViewModel
{
    public StyleEntryViewModel(string type, string summary, string? selector = null, IReadOnlyList<string>? setters = null)
    {
        Type = type;
        Summary = summary;
        Selector = selector ?? "";
        Setters = setters ?? [];
    }

    public string Type { get; }
    public string DisplayType => Type.Split('.').LastOrDefault() ?? Type;
    public string Summary { get; }
    public string Selector { get; }
    public IReadOnlyList<string> Setters { get; }
    public bool HasSetters => Setters.Count > 0;
}

public sealed class BindingErrorEntryViewModel
{
    public BindingErrorEntryViewModel(string time, string level, string area, string sourceType, string message)
    {
        Time = time;
        Level = level;
        Area = area;
        SourceType = sourceType;
        Message = message;
    }

    public string Time { get; }
    public string Level { get; }
    public string Area { get; }
    public string SourceType { get; }
    public string Message { get; }
    public string DisplayTime => DateTimeOffset.TryParse(Time, out var timestamp)
        ? timestamp.ToLocalTime().ToString("HH:mm:ss")
        : Time;
    public bool IsError => Level.Contains("error", StringComparison.OrdinalIgnoreCase);
    public bool IsWarning => Level.Contains("warning", StringComparison.OrdinalIgnoreCase);
}

public sealed class BindingExpressionEntryViewModel
{
    public BindingExpressionEntryViewModel(
        string property,
        string expressionType,
        string description,
        string value,
        string error,
        string priority,
        IReadOnlyList<string> details)
    {
        Property = property;
        ExpressionType = expressionType;
        Description = description;
        Value = value;
        Error = error;
        Priority = priority;
        Details = details;
    }

    public string Property { get; }
    public string ExpressionType { get; }
    public string Description { get; }
    public string Value { get; }
    public string Error { get; }
    public string Priority { get; }
    public IReadOnlyList<string> Details { get; }
    public bool HasError => !string.IsNullOrWhiteSpace(Error);
    public bool HasDetails => Details.Count > 0;
    public string DisplayType => ExpressionType.Split('.').LastOrDefault() ?? ExpressionType;
}

public sealed record CategoryItem(string Key, string Display);

public sealed class MetricItemViewModel
{
    public MetricItemViewModel(string name, string value, string group)
    {
        Name = name;
        Value = value;
        Group = group;
    }

    public string Name { get; }
    public string Value { get; }
    public string Group { get; }
}
