// Copyright (c) 2026 Mud MCP Contributors
// Licensed under the GNU General Public License v2.0. See LICENSE file in the project root for full license information.

using System.ComponentModel;
using System.Text;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using MudBlazor.Mcp.Configuration;
using MudBlazor.Mcp.Services;

namespace MudBlazor.Mcp.Tools;

/// <summary>
/// MCP tools for API reference documentation.
/// </summary>
[McpServerToolType]
public sealed class ApiReferenceTools
{
    private static readonly string[] ValidMemberTypes = ["all", "properties", "methods", "events"];

    /// <summary>
    /// Gets the API reference for a MudBlazor type.
    /// </summary>
    [McpServerTool(Name = "get_api_reference")]
    [Description("Gets the full API reference for a MudBlazor component or type, including all properties, methods, and events. Results are for the configured MudBlazor version. If a component seems missing, verify the --version matches your project's MudBlazor PackageReference in the .csproj file.")]
    public static async Task<string> GetApiReferenceAsync(
        IComponentIndexer indexer,
        ILogger<ApiReferenceTools> logger,
        VersionContext versionContext,
        [Description("The type name (e.g., 'MudButton', 'Color', 'Size')")]
        string typeName,
        [Description("Filter to specific member type: 'all', 'properties', 'methods', 'events' (default: 'all')")]
        string? memberType = null,
        CancellationToken cancellationToken = default)
    {
        ToolValidation.RequireNonEmpty(typeName, nameof(typeName));

        // Apply default value if not provided (MCP clients may send null for optional parameters)
        var effectiveMemberType = memberType ?? "all";
        ToolValidation.RequireValidOption(effectiveMemberType, ValidMemberTypes, nameof(memberType));

        logger.LogDebug("Getting API reference for type: {TypeName}, memberType: {MemberType}",
            typeName, effectiveMemberType);

        var apiRef = await indexer.GetApiReferenceAsync(typeName, cancellationToken);
        
        if (apiRef is null)
        {
            logger.LogWarning("Type not found: {TypeName}", typeName);
            ToolValidation.ThrowTypeNotFound(typeName);
        }

        logger.LogDebug("Found API reference for {TypeName} with {MemberCount} members",
            typeName, apiRef.Members?.Count ?? 0);

        var sb = new StringBuilder();
        sb.AppendLine($"# {apiRef.TypeName} API Reference (v{versionContext.Version})");
        sb.AppendLine();
        sb.AppendLine($"**Namespace:** `{apiRef.Namespace}`");
        
        if (!string.IsNullOrEmpty(apiRef.BaseType))
        {
            sb.AppendLine($"**Base Type:** `{apiRef.BaseType}`");
        }
        sb.AppendLine();

        if (!string.IsNullOrEmpty(apiRef.Summary))
        {
            sb.AppendLine("## Summary");
            sb.AppendLine();
            sb.AppendLine(apiRef.Summary);
            sb.AppendLine();
        }

        // Enums have values instead of members
        if (apiRef.Kind.Equals("enum", StringComparison.OrdinalIgnoreCase))
        {
            var values = apiRef.EnumValues ?? [];

            sb.AppendLine("## Values");
            sb.AppendLine();
            sb.AppendLine("| Value | Description |");
            sb.AppendLine("|-------|-------------|");

            foreach (var value in values)
            {
                sb.AppendLine($"| `{value.Name}` | {TruncateText(value.Description, 100)} |");
            }
            sb.AppendLine();
            sb.AppendLine($"Use `get_enum_values` for usage examples. **Values:** {values.Count}");

            return sb.ToString();
        }

        // Filter members
        var members = apiRef.Members ?? [];
        if (!effectiveMemberType.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            var filterType = effectiveMemberType.ToLowerInvariant() switch
            {
                "properties" => "Property",
                "methods" => "Method",
                "events" => "Event",
                _ => effectiveMemberType
            };
            
            members = members.Where(m => 
                m.MemberType.Equals(filterType, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        // Group by member type
        var properties = members.Where(m => m.MemberType == "Property").ToList();
        var events = members.Where(m => m.MemberType == "Event").ToList();
        var methods = members.Where(m => m.MemberType == "Method").ToList();

        // Properties
        if (properties.Count > 0)
        {
            sb.AppendLine("## Properties");
            sb.AppendLine();
            sb.AppendLine("| Name | Type | Description |");
            sb.AppendLine("|------|------|-------------|");
            
            foreach (var prop in properties.OrderBy(p => p.Name))
            {
                var desc = TruncateText(prop.Description, 60);
                sb.AppendLine($"| `{prop.Name}` | `{prop.ReturnType}` | {desc} |");
            }
            sb.AppendLine();
        }

        // Events
        if (events.Count > 0)
        {
            sb.AppendLine("## Events");
            sb.AppendLine();
            sb.AppendLine("| Name | Type | Description |");
            sb.AppendLine("|------|------|-------------|");
            
            foreach (var evt in events.OrderBy(e => e.Name))
            {
                var desc = TruncateText(evt.Description, 60);
                sb.AppendLine($"| `{evt.Name}` | `{evt.ReturnType}` | {desc} |");
            }
            sb.AppendLine();
        }

        // Methods
        if (methods.Count > 0)
        {
            sb.AppendLine("## Methods");
            sb.AppendLine();
            
            foreach (var method in methods.OrderBy(m => m.Name))
            {
                var parameters = method.ParameterSignature ?? "";
                
                sb.AppendLine($"### `{method.ReturnType} {method.Name}({parameters})`");
                
                if (!string.IsNullOrEmpty(method.Description))
                {
                    sb.AppendLine();
                    sb.AppendLine(method.Description);
                }
                sb.AppendLine();
            }
        }

        // Summary statistics
        sb.AppendLine("## Summary Statistics");
        sb.AppendLine();
        sb.AppendLine($"- **Properties:** {properties.Count}");
        sb.AppendLine($"- **Events:** {events.Count}");
        sb.AppendLine($"- **Methods:** {methods.Count}");

        return sb.ToString();
    }

    /// <summary>
    /// Gets enum values for a MudBlazor enum type.
    /// </summary>
    [McpServerTool(Name = "get_enum_values")]
    [Description("Gets all values for a MudBlazor enum type (e.g., Color, Size, Variant). Results are parsed from the configured MudBlazor version's source. If an enum seems missing, verify the --version matches your project's MudBlazor PackageReference in the .csproj file.")]
    public static async Task<string> GetEnumValuesAsync(
        IComponentIndexer indexer,
        ILogger<ApiReferenceTools> logger,
        VersionContext versionContext,
        [Description("The enum name (e.g., 'Color', 'Size', 'Variant', 'Align')")]
        string enumName,
        CancellationToken cancellationToken = default)
    {
        ToolValidation.RequireNonEmpty(enumName, nameof(enumName));

        logger.LogDebug("Getting enum values for: {EnumName}", enumName);

        var apiRef = await indexer.GetApiReferenceAsync(enumName, cancellationToken);

        if (apiRef is null
            || !apiRef.Kind.Equals("enum", StringComparison.OrdinalIgnoreCase)
            || apiRef.EnumValues is null
            || apiRef.EnumValues.Count == 0)
        {
            logger.LogWarning("Enum not found: {EnumName}", enumName);
            ToolValidation.ThrowTypeNotFound(enumName);
        }

        var enumValues = apiRef.EnumValues;
        logger.LogDebug("Found {Count} values for enum {EnumName}", enumValues.Count, enumName);

        var sb = new StringBuilder();
        sb.AppendLine($"# {apiRef.TypeName} Enum Values (v{versionContext.Version})");
        sb.AppendLine();

        if (!string.IsNullOrEmpty(apiRef.Summary))
        {
            sb.AppendLine(apiRef.Summary);
            sb.AppendLine();
        }

        sb.AppendLine("| Value | Description |");
        sb.AppendLine("|-------|-------------|");

        foreach (var value in enumValues)
        {
            sb.AppendLine($"| `{value.Name}` | {TruncateText(value.Description, 100)} |");
        }
        sb.AppendLine();

        sb.AppendLine("## Usage Example");
        sb.AppendLine();
        sb.AppendLine($"```razor");
        sb.AppendLine($"<MudComponent {apiRef.TypeName}=\"{apiRef.TypeName}.{enumValues[0].Name}\" />");
        sb.AppendLine($"```");

        return sb.ToString();
    }

    private static string TruncateText(string? text, int maxLength)
    {
        if (string.IsNullOrEmpty(text))
            return "-";

        text = text.Replace("\n", " ").Replace("\r", "");
        
        if (text.Length <= maxLength)
            return text;

        return text[..(maxLength - 3)] + "...";
    }
}
