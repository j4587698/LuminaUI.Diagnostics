using System.Reflection;
using System.Text.Json.Nodes;
using Avalonia.Platform;
using LuminaUI.Diagnostics.Abstractions;
using LuminaUI.Diagnostics.Dispatch;

namespace LuminaUI.Diagnostics.Inspection;

public sealed class AssetInspectionHandler : IDiagnosticToolHandler
{
    public string Method => LuminaUIDiagnosticsToolNames.GetAssets;

    public Task<DiagnosticResponse> HandleAsync(
        DiagnosticRequest request,
        CancellationToken cancellationToken = default)
    {
        var assets = new JsonArray();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies()
            .Where(assembly => !assembly.IsDynamic)
            .OrderBy(assembly => assembly.GetName().Name, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var assemblyName = assembly.GetName().Name;
            if (string.IsNullOrWhiteSpace(assemblyName))
                continue;

            try
            {
                foreach (var uri in AssetLoader.GetAssets(new Uri($"avares://{assemblyName}/"), null))
                {
                    if (uri.AbsolutePath.Contains("!AvaloniaResourceXamlInfo", StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (!seen.Add(uri.AbsoluteUri))
                        continue;

                    assets.Add(new JsonObject
                    {
                        ["uri"] = uri.AbsoluteUri,
                        ["fileName"] = Path.GetFileName(uri.LocalPath),
                        ["relativePath"] = uri.AbsolutePath.TrimStart('/'),
                        ["assembly"] = assemblyName,
                        ["size"] = TryGetLength(uri)
                    });
                }
            }
            catch (Exception)
            {
                // Not every loaded assembly contains an Avalonia resource manifest.
            }
        }

        return Task.FromResult(DiagnosticResponse.Ok(
            request.Id,
            new JsonObject { ["count"] = assets.Count, ["assets"] = assets }));
    }

    private static long? TryGetLength(Uri uri)
    {
        try
        {
            using var stream = AssetLoader.Open(uri);
            return stream.CanSeek ? stream.Length : null;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
