using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace LuminaUI.Diagnostics.UI;

public sealed class DevToolsSettings : INotifyPropertyChanged
{
    private string _theme = "System";
    private bool _compactMode;
    private string _defaultTreeKind = "Merged";
    private int _defaultExpandDepth = 3;
    private int _maxTreeDepth = 20;
    private bool _aggregateTemplateSubtrees = true;
    private bool _inlinePseudoclasses;
    private bool _contextualProperties = true;
    private bool _includeClrProperties;
    private bool _overlayShowInfo = true;
    private bool _overlayVisualizeMarginPadding = true;
    private bool _overlayShowRulers;
    private bool _overlayShowExtensionLines;
    private bool _topMost;
    private int _metricsRefreshIntervalMs = 1000;
    private string _pickElementShortcut = "Ctrl+Shift+C";
    private string _refreshShortcut = "F5";
    private string _toggleTopMostShortcut = "Ctrl+Shift+T";

    public string Theme { get => _theme; set => Set(ref _theme, NormalizeTheme(value)); }
    public bool CompactMode { get => _compactMode; set => Set(ref _compactMode, value); }
    public string DefaultTreeKind { get => _defaultTreeKind; set => Set(ref _defaultTreeKind, NormalizeTreeKind(value)); }
    public int DefaultExpandDepth { get => _defaultExpandDepth; set => Set(ref _defaultExpandDepth, Math.Clamp(value, 0, 20)); }
    public int MaxTreeDepth { get => _maxTreeDepth; set => Set(ref _maxTreeDepth, Math.Clamp(value, 1, 50)); }
    public bool AggregateTemplateSubtrees { get => _aggregateTemplateSubtrees; set => Set(ref _aggregateTemplateSubtrees, value); }
    public bool InlinePseudoclasses { get => _inlinePseudoclasses; set => Set(ref _inlinePseudoclasses, value); }
    public bool ContextualProperties { get => _contextualProperties; set => Set(ref _contextualProperties, value); }
    public bool IncludeClrProperties { get => _includeClrProperties; set => Set(ref _includeClrProperties, value); }
    public bool OverlayShowInfo { get => _overlayShowInfo; set => Set(ref _overlayShowInfo, value); }
    public bool OverlayVisualizeMarginPadding { get => _overlayVisualizeMarginPadding; set => Set(ref _overlayVisualizeMarginPadding, value); }
    public bool OverlayShowRulers { get => _overlayShowRulers; set => Set(ref _overlayShowRulers, value); }
    public bool OverlayShowExtensionLines { get => _overlayShowExtensionLines; set => Set(ref _overlayShowExtensionLines, value); }
    public bool TopMost { get => _topMost; set => Set(ref _topMost, value); }
    public int MetricsRefreshIntervalMs { get => _metricsRefreshIntervalMs; set => Set(ref _metricsRefreshIntervalMs, Math.Clamp(value, 250, 10000)); }
    public string PickElementShortcut { get => _pickElementShortcut; set => Set(ref _pickElementShortcut, value?.Trim() ?? string.Empty); }
    public string RefreshShortcut { get => _refreshShortcut; set => Set(ref _refreshShortcut, value?.Trim() ?? string.Empty); }
    public string ToggleTopMostShortcut { get => _toggleTopMostShortcut; set => Set(ref _toggleTopMostShortcut, value?.Trim() ?? string.Empty); }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return;

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private static string NormalizeTheme(string? value) => value switch
    {
        "Light" => "Light",
        "Dark" => "Dark",
        _ => "System"
    };

    private static string NormalizeTreeKind(string? value) => value switch
    {
        "Visual" => "Visual",
        "Logical" => "Logical",
        _ => "Merged"
    };
}

internal static class DevToolsSettingsStore
{
    private static readonly object Gate = new();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    static DevToolsSettingsStore()
    {
        Current = Load();
        Current.PropertyChanged += (_, _) => Save(Current);
    }

    public static DevToolsSettings Current { get; }

    private static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LuminaUI",
        "DevTools",
        "settings.json");

    private static DevToolsSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                return JsonSerializer.Deserialize<DevToolsSettings>(json, JsonOptions) ?? new DevToolsSettings();
            }
        }
        catch (Exception)
        {
            // Settings must never prevent diagnostics from opening.
        }

        return new DevToolsSettings();
    }

    private static void Save(DevToolsSettings settings)
    {
        try
        {
            lock (Gate)
            {
                var directory = Path.GetDirectoryName(SettingsPath)!;
                Directory.CreateDirectory(directory);
                var temporaryPath = SettingsPath + ".tmp";
                File.WriteAllText(temporaryPath, JsonSerializer.Serialize(settings, JsonOptions));
                File.Move(temporaryPath, SettingsPath, overwrite: true);
            }
        }
        catch (Exception)
        {
            // Diagnostics settings are best-effort and should not affect the target application.
        }
    }
}
