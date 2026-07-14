using System.Diagnostics;
using System.Reflection;
using System.Reflection.Metadata;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;

[assembly: MetadataUpdateHandler(typeof(LuminaUI.Diagnostics.HotReload.HotReloadManager))]

namespace LuminaUI.Diagnostics.HotReload;

public static class HotReloadManager
{
    private static FileSystemWatcher? _watcher;
    private static string? _sourceRoot;
    private static Timer? _debounceTimer;
    private static string? _lastChangedFile;
    private static HotReloadConfig _config = new();
    private static HotReloadStatus _status = new("Disabled", null);

    public static event Action<Type[]>? OnTypesUpdated;
    public static event Action<HotReloadStatus>? StatusChanged;

    public static HotReloadConfig Config
    {
        get => _config;
        set => _config = value ?? new HotReloadConfig();
    }

    public static bool IsRunning => _watcher is not null;
    public static string? WatchedDirectory => _sourceRoot;
    public static int PendingChanges => _lastChangedFile is not null ? 1 : 0;
    public static HotReloadStatus Status => _status;

    public static void SetSourceRoot(string? path)
    {
        if (string.IsNullOrEmpty(path)) return;

        var dir = new DirectoryInfo(path);
        while (dir is not null && !dir.GetFiles("*.sln").Any() && dir.Name != "LuminaUI")
            dir = dir.Parent;

        if (dir is not null)
            _sourceRoot = dir.FullName;
    }

    public static void StartWatcher()
    {
        if (_watcher is not null) return;
        if (!_config.EnableXamlFileWatcher)
        {
            ReportStatus("XAML watcher disabled");
            return;
        }
        if (string.IsNullOrEmpty(_sourceRoot))
        {
            ReportError("XAML watcher source directory is unavailable.");
            return;
        }

        _watcher = new FileSystemWatcher(_sourceRoot, "*.axaml")
        {
            IncludeSubdirectories = true,
            EnableRaisingEvents = true,
            NotifyFilter = NotifyFilters.LastWrite
        };

        _watcher.Changed += OnXamlFileChanged;
        ReportStatus($"Watching {_sourceRoot}");
    }

    public static void StopWatcher()
    {
        if (_watcher is null) return;

        _watcher.Changed -= OnXamlFileChanged;
        _watcher.Dispose();
        _watcher = null;
        _debounceTimer?.Dispose();
        _debounceTimer = null;
        _lastChangedFile = null;
        ReportStatus("Stopped");
    }

    public static void ClearCache(Type[]? updatedTypes)
    {
        Debug.WriteLine($"[LuminaUI.Diagnostics] HotReload ClearCache triggered for {updatedTypes?.Length ?? 0} types.");
    }

    public static void UpdateApplication(Type[]? updatedTypes)
    {
        if (updatedTypes is null || updatedTypes.Length == 0) return;

        if (!_config.EnableInstanceReplacement)
        {
            Debug.WriteLine($"[LuminaUI.Diagnostics] HotReload instance replacement is disabled. Notifying listeners only.");
            ReportStatus($"Observed {updatedTypes.Length} updated type(s); replacement disabled");
            OnTypesUpdated?.Invoke(updatedTypes);
            return;
        }

        Debug.WriteLine($"[LuminaUI.Diagnostics] HotReload UpdateApplication for {string.Join(", ", updatedTypes.Select(t => t.Name))}.");

        OnTypesUpdated?.Invoke(updatedTypes);

        Avalonia.Threading.Dispatcher.UIThread.Post(() => ReplaceInstances(updatedTypes));
        ReportStatus($"Queued replacement for {updatedTypes.Length} updated type(s)");
    }

    public static void NotifyTypesUpdated(Type[] updatedTypes)
    {
        OnTypesUpdated?.Invoke(updatedTypes);
    }

    private static void OnXamlFileChanged(object sender, FileSystemEventArgs e)
    {
        _lastChangedFile = e.FullPath;

        if (_debounceTimer is null)
            _debounceTimer = new Timer(OnDebounceTimer, null, 100, Timeout.Infinite);
        else
            _debounceTimer.Change(100, Timeout.Infinite);
    }

    private static void OnDebounceTimer(object? state)
    {
        var path = _lastChangedFile;
        if (path is null) return;
        Avalonia.Threading.Dispatcher.UIThread.Post(() => ProcessXamlChange(path));
    }

    private static async void ProcessXamlChange(string fullPath)
    {
        _lastChangedFile = null;

        if (!_config.EnableXamlFileWatcher)
        {
            Debug.WriteLine("[LuminaUI.Diagnostics] XAML file watcher is disabled.");
            return;
        }

        try
        {
            var xaml = await ReadFileWithRetryAsync(fullPath, _config.FileReadRetries, _config.FileReadRetryDelayMs);
            if (string.IsNullOrWhiteSpace(xaml)) return;

            var match = Regex.Match(xaml, @"x:Class=""([^""]+)""");
            if (!match.Success) return;

            var className = match.Groups[1].Value;

            Type? type = null;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    type = assembly.GetType(className);
                    if (type is not null) break;
                }
                catch
                {
                }
            }

            if (type is not null)
            {
                Debug.WriteLine($"[LuminaUI.Diagnostics] XAML changed for {className}.");
                var reloaded = ReplaceInstancesWithXaml(type, xaml);
                OnTypesUpdated?.Invoke([type]);
                if (reloaded)
                    ReportStatus($"Reloaded {className}");
            }
            else
            {
                ReportError($"Could not resolve XAML type '{className}'.");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LuminaUI.Diagnostics] Error processing XAML change: {ex.Message}");
            ReportError(ex.Message);
        }
    }

    private static async Task<string> ReadFileWithRetryAsync(string path, int maxRetries, int retryDelayMs)
    {
        for (var i = 0; i < maxRetries; i++)
        {
            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var sr = new StreamReader(fs);
                var content = await sr.ReadToEndAsync().ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(content))
                    return content;
            }
            catch (IOException) when (i < maxRetries - 1)
            {
                if (retryDelayMs > 0)
                    await Task.Delay(retryDelayMs).ConfigureAwait(false);
            }
        }

        return "";
    }

    private static void ReplaceInstances(Type[] updatedTypes)
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            return;

        var controlsToReplace = updatedTypes.Where(t => typeof(Control).IsAssignableFrom(t)).ToList();
        if (controlsToReplace.Count == 0) return;

        foreach (var window in desktop.Windows)
            ReplaceInstancesInTree(window, controlsToReplace);
    }

    private static void ReplaceInstancesInTree(Control root, List<Type> updatedTypes)
    {
        var children = root.GetVisualDescendants().OfType<Control>().ToList();

        foreach (var oldControl in children)
        {
            var type = oldControl.GetType();
            if (updatedTypes.Contains(type))
            {
                try
                {
                    Debug.WriteLine($"[LuminaUI.Diagnostics] Replacing instance of {type.Name}");
                    var newControl = (Control)Activator.CreateInstance(type)!;
                    newControl.DataContext = oldControl.DataContext;
                    ReplaceInParent(oldControl, newControl);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[LuminaUI.Diagnostics] Failed to replace instance of {type.Name}: {ex.Message}");
                    ReportError($"Failed to replace {type.Name}: {ex.Message}");
                }
            }
        }
    }

    private static bool ReplaceInstancesWithXaml(Type type, string xamlText)
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            return false;

        var succeeded = true;

        foreach (var window in desktop.Windows)
        {
            var children = window.GetVisualDescendants().OfType<Control>().ToList();
            if (window.GetType() == type) children.Add(window);

            foreach (var oldControl in children)
            {
                if (oldControl.GetType() != type) continue;

                try
                {
                    Debug.WriteLine($"[LuminaUI.Diagnostics] Parsing XAML for instance of {type.Name}");

                    if (oldControl is StyledElement se)
                    {
                        se.Styles?.Clear();
                        se.Resources?.Clear();
                        var field = typeof(StyledElement).GetField("_stylesApplied",
                            BindingFlags.Instance | BindingFlags.NonPublic);
                        field?.SetValue(oldControl, false);
                    }

                    if (oldControl is ContentControl cc) cc.Content = null;
                    else if (oldControl is Panel p) p.Children.Clear();
                    else if (oldControl is Decorator dec) dec.Child = null;

                    NameScope.SetNameScope((StyledElement)oldControl, new NameScope());
                    AvaloniaRuntimeXamlLoader.Load(xamlText, type.Assembly, oldControl);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[LuminaUI.Diagnostics] Failed to parse XAML for {type.Name}: {ex.Message}");
                    ReportError($"Failed to reload {type.Name}: {ex.Message}");
                    succeeded = false;
                }
            }
        }

        return succeeded;
    }

    private static void ReplaceInParent(Control oldControl, Control newControl)
    {
        var parent = oldControl.Parent;
        if (parent is null) return;

        if (parent is ContentControl cc && cc.Content == oldControl)
            cc.Content = newControl;
        else if (parent is Panel panel)
        {
            var index = panel.Children.IndexOf(oldControl);
            if (index >= 0)
                panel.Children[index] = newControl;
        }
        else if (parent is Decorator dec && dec.Child == oldControl)
            dec.Child = newControl;
        else if (parent is ItemsControl ic && ic.Items.Contains(oldControl))
        {
            if (ic.ItemsSource is System.Collections.IList list)
            {
                var idx = list.IndexOf(oldControl);
                if (idx >= 0) list[idx] = newControl;
            }
        }
    }

    private static void ReportStatus(string message)
    {
        _status = new HotReloadStatus(message, null);
        StatusChanged?.Invoke(_status);
    }

    private static void ReportError(string error)
    {
        _status = new HotReloadStatus("Hot reload failed", error);
        StatusChanged?.Invoke(_status);
    }
}

public sealed record HotReloadStatus(string Message, string? Error);

public sealed class HotReloadConfig
{
    public bool EnableInstanceReplacement { get; set; }
    public bool EnableXamlFileWatcher { get; set; }
    public int FileReadRetries { get; set; } = 3;
    public int FileReadRetryDelayMs { get; set; } = 50;
}
