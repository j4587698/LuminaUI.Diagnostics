using Avalonia.Controls;
using LuminaUI.Diagnostics.Inspection;
using LuminaUI.Diagnostics.UI;
using LuminaUI.Diagnostics.UI.ViewModels;

namespace LuminaUI.Diagnostics.Tests.UI;

public sealed class LuminaDevToolsViewModelTests
{
    [Fact]
    public async Task SelectedNode_DoesNotApplyStalePropertyResults()
    {
        var firstControl = new Button();
        var secondControl = new Button();
        var client = new DelayedDiagnosticsClient(firstControl, secondControl);
        using var viewModel = new LuminaDevToolsViewModel(client);

        viewModel.SelectedNode = new VisualTreeNodeViewModel(firstControl);
        await client.FirstRequestStarted.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        viewModel.SelectedNode = new VisualTreeNodeViewModel(secondControl);
        await WaitUntilAsync(() => viewModel.Properties.SingleOrDefault()?.Name == "Second");

        client.ReleaseFirstRequest.TrySetResult();
        await Task.Delay(50, TestContext.Current.CancellationToken);

        Assert.Equal("Second", Assert.Single(viewModel.Properties).Name);
    }

    [Fact]
    public async Task InitializeAsync_ReportsClientFailure()
    {
        using var viewModel = new LuminaDevToolsViewModel(new FailingDiagnosticsClient());

        await viewModel.InitializeAsync();

        Assert.True(viewModel.IsStatusError);
        Assert.Contains("tree failed", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ElementPicked_SelectsAndExpandsNodeInCurrentTree()
    {
        var settings = DevToolsSettingsStore.Current;
        var originalTreeKind = settings.DefaultTreeKind;
        settings.DefaultTreeKind = "Visual";

        try
        {
            var rootControl = new object();
            var childControl = new object();
            var visualRoot = new VisualTreeNodeViewModel(rootControl);
            var visualChild = new VisualTreeNodeViewModel(childControl);
            visualRoot.Children.Add(visualChild);
            var client = new PickerDiagnosticsClient(
                [visualRoot],
                [new VisualTreeNodeViewModel(rootControl)]);

            using var viewModel = new LuminaDevToolsViewModel(client);
            await viewModel.InitializeAsync();
            var propertyRequestsBeforePick = client.PropertyRequestCount;
            visualRoot.IsExpanded = false;
            VisualTreeNodeViewModel? locatedNode = null;
            var locatedCount = 0;
            viewModel.PickedNodeLocated += (_, node) =>
            {
                locatedNode = node;
                locatedCount++;
            };

            client.Pick(childControl);

            Assert.Equal(1, viewModel.SelectedTreeTabIndex);
            Assert.Same(visualChild, viewModel.SelectedNode);
            Assert.Same(visualChild, viewModel.SelectedVisualNode);
            Assert.Same(visualChild, locatedNode);
            Assert.True(visualRoot.IsExpanded);
            Assert.True(visualChild.IsSelected);
            Assert.Equal(propertyRequestsBeforePick, client.PropertyRequestCount);

            client.Pick(childControl);

            Assert.Equal(2, locatedCount);
        }
        finally
        {
            settings.DefaultTreeKind = originalTreeKind;
        }
    }

    [Fact]
    public void PropertyEditorMetadata_UsesChoicesAndDisablesComplexObjects()
    {
        var alignment = new PropertyViewModel("HorizontalAlignment", "HorizontalAlignment", "Layoutable", false, false);
        PropertyEditorMetadata.Configure(alignment, typeof(Avalonia.Layout.HorizontalAlignment));

        var content = new PropertyViewModel("Content", "Object", "ContentControl", false, false);
        PropertyEditorMetadata.Configure(content, typeof(object));

        Assert.Equal(PropertyEditorKind.Choice, alignment.EditorKind);
        Assert.Contains("Stretch", alignment.ChoiceValues);
        Assert.True(alignment.CanEdit);
        Assert.Equal(PropertyEditorKind.ReadOnly, content.EditorKind);
        Assert.False(content.CanEdit);
    }

    [Fact]
    public void FontFamilyDisplay_RemovesCompositeFontInternals()
    {
        var display = PropertyViewModel.FormatFontFamilyName(
            "compositefont:fonts:Inter#Inter, $Default#Inter, $Default");

        Assert.Equal("Inter", display);
    }

    [Fact]
    public void BuildMergedTree_UsesLogicalBackboneAndAggregatesVisualOnlyNodes()
    {
        var rootControl = new Button();
        var templateBorder = new Border();
        var logicalChild = new TextBox();

        var visualRoot = new VisualTreeNodeViewModel(rootControl);
        var visualTemplate = new VisualTreeNodeViewModel(templateBorder);
        visualTemplate.Children.Add(new VisualTreeNodeViewModel(logicalChild));
        visualRoot.Children.Add(visualTemplate);

        var logicalRoot = new VisualTreeNodeViewModel(rootControl);
        logicalRoot.Children.Add(new VisualTreeNodeViewModel(logicalChild));

        var merged = LuminaDevToolsViewModel.BuildMergedTree([logicalRoot], [visualRoot], aggregateTemplateSubtrees: true);

        var mergedRoot = Assert.Single(merged);
        Assert.Equal(2, mergedRoot.Children.Count);
        var template = mergedRoot.Children[0];
        Assert.Equal("/template/", template.Type);
        Assert.Same(templateBorder, Assert.Single(template.Children).Control);
        Assert.Empty(template.Children[0].Children);
        Assert.Same(logicalChild, mergedRoot.Children[1].Control);
    }

    [Fact]
    public void BuildMergedTree_UsesNodeIdsAcrossProcessBoundary()
    {
        var visualRoot = new VisualTreeNodeViewModel("Window", null, null, null, "root");
        visualRoot.Children.Add(new VisualTreeNodeViewModel("Border", null, null, null, "template-border"));
        visualRoot.Children.Add(new VisualTreeNodeViewModel("TextBox", "Search", null, null, "search"));

        var logicalRoot = new VisualTreeNodeViewModel("Window", null, null, null, "root");
        logicalRoot.Children.Add(new VisualTreeNodeViewModel("TextBox", "Search", null, null, "search"));

        var merged = LuminaDevToolsViewModel.BuildMergedTree(
            [logicalRoot],
            [visualRoot],
            aggregateTemplateSubtrees: true);

        var mergedRoot = Assert.Single(merged);
        Assert.Equal("root", mergedRoot.NodeId);
        Assert.Equal("template-border", Assert.Single(mergedRoot.Children[0].Children).NodeId);
        Assert.Equal("search", mergedRoot.Children[1].NodeId);
    }

    [Fact]
    public void BuildResourceTree_UsesProviderHierarchyAndHidesTechnicalIndexes()
    {
        var applicationResource = new ResourceEntryViewModel(
            "application/resources",
            "PrimaryColor",
            "String",
            "scalar",
            "String",
            "Blue",
            sourceSegments: ["application", "resources"]);
        var styleResource = new ResourceEntryViewModel(
            "application/styles/style[0]/resources",
            "ButtonPadding",
            "String",
            "scalar",
            "String",
            "8",
            sourceSegments: ["application", "styles", "provider:0:Button.primary"]);

        var roots = LuminaDevToolsViewModel.BuildResourceTree([applicationResource, styleResource]);

        var application = Assert.Single(roots);
        Assert.Equal(2, application.Children.Count);
        var styleProvider = Assert.Single(application.Children.Single(node => node.SegmentKey == "styles").Children);
        Assert.Equal("Button.primary", styleProvider.DisplayName);
        Assert.DoesNotContain("style[0]", styleProvider.DisplayPath, StringComparison.Ordinal);
        Assert.Equal(styleResource.Source, styleProvider.Source);
        Assert.Equal(1, styleProvider.Count);
        Assert.Equal(2, application.TotalCount);
    }

    [Fact]
    public void ResourceValuePolicy_RejectsDeferredAxamlFactories()
    {
        var deferred = new DeferredTransformationFactory();
        var pointerDeferred = new PointerDeferredContent<object>();

        Assert.True(ResourceValuePolicy.IsDeferred(deferred));
        Assert.False(ResourceValuePolicy.IsEditable(deferred));
        Assert.True(ResourceValuePolicy.IsDeferred(pointerDeferred));
        Assert.False(ResourceValuePolicy.IsEditable(pointerDeferred));
        Assert.True(ResourceValuePolicy.IsEditable(Avalonia.Media.Color.Parse("#2563EB")));
    }

    [Fact]
    public async Task Resources_LoadProvidersFirstAndEntriesForSelectedNodeOnly()
    {
        var client = new LazyResourceDiagnosticsClient();
        using var viewModel = new LuminaDevToolsViewModel(client);

        viewModel.SelectSectionCommand.Execute("Resources");
        await WaitUntilAsync(() => viewModel.SelectedResourceCount == 1);

        Assert.Equal(716, viewModel.ResourceCount);
        Assert.Equal(["application/resources"], client.RequestedSources);

        var dark = viewModel.ResourceTreeRoots
            .Single()
            .Children.Single(node => node.SegmentKey == "styles")
            .Children.Single(node => node.Source == "application/styles/dark");
        viewModel.SelectedResourceNode = dark;
        await WaitUntilAsync(() => client.RequestedSources.Count == 2);

        Assert.Equal("application/styles/dark", client.RequestedSources[1]);
        viewModel.Dispose();
        Assert.Equal(0, viewModel.ResourceCount);
        Assert.Empty(viewModel.Resources);
        Assert.Empty(viewModel.ResourceTreeRoots);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (!condition() && DateTime.UtcNow < deadline)
            await Task.Delay(10);
        Assert.True(condition());
    }

    private sealed class DelayedDiagnosticsClient(object firstControl, object secondControl) : DiagnosticsClientStub
    {
        public TaskCompletionSource FirstRequestStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseFirstRequest { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async Task<IReadOnlyList<PropertyViewModel>> GetPropertiesAsync(
            VisualTreeNodeViewModel node,
            CancellationToken cancellationToken = default)
        {
            if (ReferenceEquals(node.Control, firstControl))
            {
                FirstRequestStarted.TrySetResult();
                await ReleaseFirstRequest.Task;
                return [CreateProperty("First")];
            }

            Assert.Same(secondControl, node.Control);
            return [CreateProperty("Second")];
        }

        private static PropertyViewModel CreateProperty(string name) =>
            new(name, typeof(string).Name, typeof(Button).Name, false, false);
    }

    private sealed class DeferredTransformationFactory;
    private sealed class PointerDeferredContent<T>;

    private sealed class LazyResourceDiagnosticsClient : DiagnosticsClientStub
    {
        public List<string> RequestedSources { get; } = [];

        public override Task<IReadOnlyList<ResourceProviderViewModel>> GetResourceProvidersAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ResourceProviderViewModel>>(
            [
                new("application/resources", ["application", "resources"], 1),
                new("application/styles/dark", ["application", "styles", "theme:Dark"], 715)
            ]);

        public override Task<IReadOnlyList<ResourceEntryViewModel>> GetResourcesAsync(
            string? source = null,
            VisualTreeNodeViewModel? node = null,
            CancellationToken cancellationToken = default)
        {
            RequestedSources.Add(source ?? string.Empty);
            return Task.FromResult<IReadOnlyList<ResourceEntryViewModel>>(
            [
                new(source ?? string.Empty, "key", "String", "scalar", "String", "value")
            ]);
        }
    }

    private sealed class FailingDiagnosticsClient : DiagnosticsClientStub
    {
        public override Task<IReadOnlyList<VisualTreeNodeViewModel>> GetVisualTreeAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Tree failed");
    }

    private sealed class PickerDiagnosticsClient(
        IReadOnlyList<VisualTreeNodeViewModel> visualTree,
        IReadOnlyList<VisualTreeNodeViewModel> logicalTree) : DiagnosticsClientStub
    {
        public int PropertyRequestCount { get; private set; }

        public override Task<IReadOnlyList<VisualTreeNodeViewModel>> GetVisualTreeAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(visualTree);

        public override Task<IReadOnlyList<VisualTreeNodeViewModel>> GetLogicalTreeAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(logicalTree);

        public override Task<IReadOnlyList<PropertyViewModel>> GetPropertiesAsync(
            VisualTreeNodeViewModel node,
            CancellationToken cancellationToken = default)
        {
            PropertyRequestCount++;
            return Task.FromResult<IReadOnlyList<PropertyViewModel>>([]);
        }

        public void Pick(object element) => RaiseElementPicked(element);
    }

    private abstract class DiagnosticsClientStub : IDiagnosticsClient
    {
        public event EventHandler<object>? ElementPicked;

        public virtual Task<IReadOnlyList<VisualTreeNodeViewModel>> GetVisualTreeAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<VisualTreeNodeViewModel>>([]);

        public virtual Task<IReadOnlyList<VisualTreeNodeViewModel>> GetLogicalTreeAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<VisualTreeNodeViewModel>>([]);

        public virtual Task HighlightElementAsync(VisualTreeNodeViewModel? node, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public virtual Task<IReadOnlyList<PropertyViewModel>> GetPropertiesAsync(VisualTreeNodeViewModel node, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PropertyViewModel>>([]);

        public virtual Task<IReadOnlyList<ResourceProviderViewModel>> GetResourceProvidersAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ResourceProviderViewModel>>([]);

        public virtual Task<IReadOnlyList<ResourceEntryViewModel>> GetResourcesAsync(string? source = null, VisualTreeNodeViewModel? node = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ResourceEntryViewModel>>([]);

        public virtual Task<StyleInspectionViewModel> GetAppliedStylesAsync(VisualTreeNodeViewModel? node, CancellationToken cancellationToken = default) =>
            Task.FromResult(StyleInspectionViewModel.Empty);

        public virtual Task<IReadOnlyList<BindingErrorEntryViewModel>> GetBindingErrorsAsync(int maxItems = 100, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<BindingErrorEntryViewModel>>([]);

        public virtual Task<IReadOnlyList<BindingExpressionEntryViewModel>> GetBindingExpressionsAsync(VisualTreeNodeViewModel? node, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<BindingExpressionEntryViewModel>>([]);

        public virtual Task<IReadOnlyList<AssetEntryViewModel>> GetAssetsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AssetEntryViewModel>>([]);

        public virtual Task TogglePickerModeAsync(bool isEnabled, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        protected void RaiseElementPicked(object element) => ElementPicked?.Invoke(this, element);
    }
}
