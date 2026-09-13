using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LuminaUI.Diagnostics.UI.ViewModels;

namespace LuminaUI.Diagnostics.UI;

/// <summary>
/// Provides an abstraction for communicating with the target Avalonia application.
/// </summary>
public interface IDiagnosticsClient
{
    Task<IReadOnlyList<VisualTreeNodeViewModel>> GetVisualTreeAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<VisualTreeNodeViewModel>> GetLogicalTreeAsync(CancellationToken cancellationToken = default);
    Task HighlightElementAsync(VisualTreeNodeViewModel? node, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PropertyViewModel>> GetPropertiesAsync(VisualTreeNodeViewModel node, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ResourceProviderViewModel>> GetResourceProvidersAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ResourceEntryViewModel>> GetResourcesAsync(string? source = null, VisualTreeNodeViewModel? node = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AssetEntryViewModel>> GetAssetsAsync(CancellationToken cancellationToken = default);
    Task<StyleInspectionViewModel> GetAppliedStylesAsync(VisualTreeNodeViewModel? node, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<BindingErrorEntryViewModel>> GetBindingErrorsAsync(int maxItems = 100, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<BindingExpressionEntryViewModel>> GetBindingExpressionsAsync(VisualTreeNodeViewModel? node, CancellationToken cancellationToken = default);
    Task<RuntimeMetricsViewModel> GetRuntimeMetricsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(RuntimeMetricsViewModel.ReadCurrent());

    Task TogglePickerModeAsync(bool isEnabled, CancellationToken cancellationToken = default);
    event System.EventHandler<object>? ElementPicked;
}
