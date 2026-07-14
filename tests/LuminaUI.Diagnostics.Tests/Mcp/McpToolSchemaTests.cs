using System.Reflection;
using System.Text.Json.Nodes;
using LuminaUI.Diagnostics.Abstractions;
using LuminaUI.Diagnostics.Mcp.Tools;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace LuminaUI.Diagnostics.Tests.Mcp;

public sealed class McpToolSchemaTests
{
    [Fact]
    public void AllToolNames_AreRegistered()
    {
        var names = typeof(LuminaUIDiagnosticsToolNames)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(f => f.FieldType == typeof(string))
            .Select(f => (string)f.GetValue(null)!)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains(LuminaUIDiagnosticsToolNames.DiscoverApps, names);
        Assert.Contains(LuminaUIDiagnosticsToolNames.ConnectApp, names);
        Assert.Contains(LuminaUIDiagnosticsToolNames.ListWindows, names);
        Assert.Contains(LuminaUIDiagnosticsToolNames.GetVisualTree, names);
        Assert.Contains(LuminaUIDiagnosticsToolNames.GetControlProperties, names);
        Assert.Contains(LuminaUIDiagnosticsToolNames.TakeScreenshot, names);
        Assert.Contains(LuminaUIDiagnosticsToolNames.SetProperty, names);
        Assert.Contains(LuminaUIDiagnosticsToolNames.ClickControl, names);
        Assert.Contains(LuminaUIDiagnosticsToolNames.InvokeCommand, names);
        Assert.NotEmpty(names);
    }

    [Fact]
    public void ToolNames_UseExpectedConvention()
    {
        var names = typeof(LuminaUIDiagnosticsToolNames)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(f => f.FieldType == typeof(string))
            .Select(f => (string)f.GetValue(null)!)
            .ToList();

        foreach (var name in names)
        {
            Assert.False(string.IsNullOrWhiteSpace(name), "Tool name must not be empty.");
            Assert.True(name.Length >= 3, $"Tool name '{name}' is too short.");
        }
    }

    [Fact]
    public void ToolForwarder_Parameters_BuildsCorrectJson()
    {
        var parameters = ToolForwarder.Parameters(
            ("name", "test"),
            ("count", 42),
            ("enabled", true),
            ("optional", null));

        Assert.Equal("test", parameters["name"]?.GetValue<string>());
        Assert.Equal(42, parameters["count"]?.GetValue<int>());
        Assert.True(parameters["enabled"]?.GetValue<bool>());
        Assert.False(parameters.ContainsKey("optional"));
    }

    [Fact]
    public void ToolForwarder_Parameters_ArrayValues()
    {
        var parameters = ToolForwarder.Parameters(
            ("items", new[] { "a", "b", "c" }));

        var array = parameters["items"] as JsonArray;
        Assert.NotNull(array);
        Assert.Equal(3, array!.Count);
    }

    [Fact]
    public void DiagnosticRequest_DefaultTimeout_IsValid()
    {
        Assert.True(LuminaUIDiagnosticsProtocol.DefaultTimeoutMs > 0);
        Assert.True(LuminaUIDiagnosticsProtocol.DefaultTimeoutMs <= 60000);
    }

    [Fact]
    public void JsonTools_EnableStructuredContent()
    {
        var toolTypes = new[]
        {
            typeof(DiscoveryTools),
            typeof(InspectionTools),
            typeof(PropertyTools),
            typeof(InteractionTools),
            typeof(ScrollTools)
        };

        var methods = toolTypes.SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Static));
        foreach (var method in methods)
        {
            var attribute = method.GetCustomAttribute<McpServerToolAttribute>();
            if (attribute is null)
                continue;

            var resultType = method.ReturnType.IsGenericType
                ? method.ReturnType.GetGenericArguments()[0]
                : method.ReturnType;
            if (typeof(ContentBlock).IsAssignableFrom(resultType))
                continue;

            Assert.True(attribute.UseStructuredContent, $"Tool '{attribute.Name ?? method.Name}' must enable structured content.");
        }
    }

    [Fact]
    public void SetProperty_ExposesExplicitClearLocalValueParameter()
    {
        var method = typeof(InteractionTools).GetMethod(nameof(InteractionTools.SetProperty));

        Assert.NotNull(method);
        var parameter = Assert.Single(method!.GetParameters(), item => item.Name == "clearLocalValue");
        Assert.Equal(typeof(bool), parameter.ParameterType);
        Assert.True(parameter.HasDefaultValue);
        Assert.Equal(false, parameter.DefaultValue);
    }
}
