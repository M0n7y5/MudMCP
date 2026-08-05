// Copyright (c) 2025 Mud MCP Contributors
// Licensed under the GNU General Public License v2.0. See LICENSE file in the project root for full license information.

using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using MudBlazor.Mcp.Models;

namespace MudBlazor.Mcp.Services.Parsing;

/// <summary>
/// Parses Razor documentation files to extract component descriptions and sections.
/// </summary>
public sealed partial class RazorDocParser
{
    private readonly ILogger<RazorDocParser> _logger;

    public RazorDocParser(ILogger<RazorDocParser> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Parses a Razor documentation file for component documentation.
    /// </summary>
    /// <param name="filePath">The path to the Razor documentation file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The parse result, or null if the file doesn't exist.</returns>
    /// <exception cref="IOException">The file could not be read.</exception>
    /// <exception cref="UnauthorizedAccessException">Access to the file was denied.</exception>
    public async Task<RazorDocResult?> ParseDocumentationFileAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        if (!File.Exists(filePath))
        {
            _logger.LogWarning("Documentation file not found: {FilePath}", filePath);
            return null;
        }

        var content = await File.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);
        return ParseDocumentation(content, filePath);
    }

    /// <summary>
    /// Parses Razor documentation content.
    /// </summary>
    /// <param name="content">The Razor file content.</param>
    /// <param name="filePath">The file path for reference.</param>
    /// <returns>The parsed documentation result.</returns>
    public RazorDocResult? ParseDocumentation(string content, string filePath)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var componentName = ExtractComponentFromHeader(content) ?? ExtractComponentName(filePath);
        var title = ExtractPageTitle(content);
        var description = ExtractSubTitle(content);
        var sections = ExtractSections(content);
        var (relatedNames, relatedSlugs) = ExtractRelatedComponents(content);
        var usageNotes = ExtractUsageNotes(content);
        var routes = ExtractRoutes(content);

        return new RazorDocResult
        {
            FilePath = filePath,
            ComponentName = componentName,
            Title = title,
            Description = description,
            Sections = sections,
            RelatedComponents = relatedNames,
            RelatedComponentSlugs = relatedSlugs,
            Routes = routes,
            UsageNotes = usageNotes
        };
    }

    private static string? ExtractComponentName(string filePath)
    {
        var fileName = Path.GetFileNameWithoutExtension(filePath);
        
        // Handle pattern like "ButtonPage.razor" -> "MudButton"
        if (fileName.EndsWith("Page"))
        {
            var name = fileName[..^4]; // Remove "Page"
            return $"Mud{name}";
        }

        return null;
    }

    /// <summary>
    /// Extracts the canonical component name from the DocsPageHeader
    /// (e.g. Component="@nameof(MudAlert)" or Component="@nameof(MudBlazor.Charts.Bar&lt;T&gt;)").
    /// The header is authoritative; file-name derivation is only a fallback because
    /// pages like ButtonFAB/FabPage.razor document MudFab.
    /// </summary>
    private static string? ExtractComponentFromHeader(string content)
    {
        var match = ComponentAttributeRegex().Match(content);
        if (!match.Success)
            return null;

        var typeName = match.Groups[1].Value;

        // Strip generic arguments: "MudAutocomplete<T>" -> "MudAutocomplete"
        var genericStart = typeName.IndexOf('<');
        if (genericStart >= 0)
            typeName = typeName[..genericStart];

        // Use the simple name: "MudBlazor.Charts.Bar" -> "Bar"
        var lastDot = typeName.LastIndexOf('.');
        return lastDot >= 0 ? typeName[(lastDot + 1)..] : typeName;
    }

    /// <summary>
    /// Extracts every route this page is served under
    /// (e.g. @page "/components/barchart" and the alias @page "/components/bar").
    /// </summary>
    private static List<string> ExtractRoutes(string content)
    {
        var routes = new List<string>();
        foreach (Match match in PageRouteRegex().Matches(content))
        {
            routes.Add(match.Groups[1].Value.ToLowerInvariant());
        }
        return routes;
    }

    private static string? ExtractPageTitle(string content)
    {
        // Match: <DocsPage title="Button">
        var match = TitleAttributeRegex().Match(content);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static string? ExtractSubTitle(string content)
    {
        // Match: <DocsPageHeader Title="..." SubTitle="...">
        var match = SubTitleAttributeRegex().Match(content);
        if (match.Success)
            return match.Groups[1].Value;

        // Alternative: <MudText> in header section
        match = HeaderTextRegex().Match(content);
        return match.Success ? match.Groups[1].Value : null;
    }

    private List<DocumentationSection> ExtractSections(string content)
    {
        var sections = new List<DocumentationSection>();

        // Match DocsPageSection components
        var sectionMatches = DocsSectionRegex().Matches(content);
        
        foreach (Match match in sectionMatches)
        {
            var title = match.Groups[1].Value;
            var sectionContent = match.Groups[2].Value;

            sections.Add(new DocumentationSection
            {
                Title = title,
                Content = ExtractTextContent(sectionContent),
                HasExample = sectionContent.Contains("SectionSource") || 
                            sectionContent.Contains("Example")
            });
        }

        return sections;
    }

    private static string ExtractTextContent(string razorContent)
    {
        // Remove Razor markup and extract readable text
        var text = razorContent;
        
        // Remove component tags
        text = ComponentTagRegex().Replace(text, "");
        
        // Remove code blocks
        text = CodeBlockRegex().Replace(text, "");
        
        // Remove @ directives
        text = DirectiveRegex().Replace(text, "");
        
        // Clean up whitespace
        text = WhitespaceRegex().Replace(text, " ");
        
        return text.Trim();
    }

    /// <summary>
    /// Extracts related-component references from a documentation page.
    /// Names come from explicit references (<c>ApiTypeLink</c>, <c>CodeInline</c> prose mentions);
    /// slugs come from <c>href="/components/{slug}"</c> links and must be resolved against the
    /// component index by the caller. Layout markup tags are deliberately NOT treated as
    /// relationships - a page using MudText/MudLink for its prose is not "related" to them.
    /// </summary>
    private static (List<string> Names, List<string> Slugs) ExtractRelatedComponents(string content)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var slugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Links to other component pages: href="/components/{route}[#fragment]".
        // Nested route aliases (e.g. "chart-types/barchart") are kept whole so they
        // can be resolved against the route map built from @page directives.
        foreach (Match match in ComponentLinkRegex().Matches(content))
        {
            var slug = match.Groups[1].Value;

            // Strip fragments and query strings
            var end = slug.IndexOfAny(['#', '?']);
            if (end >= 0)
            {
                slug = slug[..end];
            }

            slug = slug.Trim('/');

            if (slug.Length > 0)
            {
                slugs.Add(slug.ToLowerInvariant());
            }
        }

        // Explicit API type references: <ApiTypeLink TypeName="MudXxx" />
        foreach (Match match in ApiTypeLinkRegex().Matches(content))
        {
            names.Add(match.Groups[1].Value);
        }

        // Prose references to other components: <CodeInline>MudXxx</CodeInline>
        foreach (Match match in CodeInlineComponentRegex().Matches(content))
        {
            names.Add(match.Groups[1].Value);
        }

        return (names.ToList(), slugs.ToList());
    }

    private static List<string> ExtractUsageNotes(string content)
    {
        var notes = new List<string>();

        // Extract MudAlert content (often used for important notes)
        var alertMatches = AlertContentRegex().Matches(content);
        foreach (Match match in alertMatches)
        {
            var alertText = ExtractTextContent(match.Groups[1].Value);
            if (!string.IsNullOrWhiteSpace(alertText))
            {
                notes.Add(alertText);
            }
        }

        return notes;
    }

    [GeneratedRegex(@"Title\s*=\s*""([^""]+)""", RegexOptions.IgnoreCase)]
    private static partial Regex TitleAttributeRegex();

    [GeneratedRegex(@"SubTitle\s*=\s*""([^""]+)""", RegexOptions.IgnoreCase)]
    private static partial Regex SubTitleAttributeRegex();

    [GeneratedRegex(@"<MudText[^>]*>\s*([^<]+)\s*</MudText>")]
    private static partial Regex HeaderTextRegex();

    [GeneratedRegex(@"<DocsPageSection\s+Title\s*=\s*""([^""]+)""[^>]*>(.*?)</DocsPageSection>", RegexOptions.Singleline)]
    private static partial Regex DocsSectionRegex();

    [GeneratedRegex(@"<[A-Z][^>]*>.*?</[A-Z][^>]*>|<[A-Z][^/]*/>", RegexOptions.Singleline)]
    private static partial Regex ComponentTagRegex();

    [GeneratedRegex(@"<code>.*?</code>|@\{.*?\}", RegexOptions.Singleline)]
    private static partial Regex CodeBlockRegex();

    [GeneratedRegex(@"@[a-zA-Z]+(\.[a-zA-Z]+)*")]
    private static partial Regex DirectiveRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"href\s*=\s*""/components/([^""]+)""", RegexOptions.IgnoreCase)]
    private static partial Regex ComponentLinkRegex();

    [GeneratedRegex(@"<ApiTypeLink\s[^>]*TypeName\s*=\s*""(Mud\w+)""", RegexOptions.IgnoreCase)]
    private static partial Regex ApiTypeLinkRegex();

    [GeneratedRegex(@"<CodeInline[^>]*>\s*(Mud[A-Z]\w*)\s*</CodeInline>")]
    private static partial Regex CodeInlineComponentRegex();

    [GeneratedRegex(@"Component\s*=\s*""@nameof\(([^)]+)\)""", RegexOptions.IgnoreCase)]
    private static partial Regex ComponentAttributeRegex();

    [GeneratedRegex(@"@page\s+""/components/([^""]+)""", RegexOptions.IgnoreCase)]
    private static partial Regex PageRouteRegex();

    [GeneratedRegex(@"<MudAlert[^>]*>(.*?)</MudAlert>", RegexOptions.Singleline)]
    private static partial Regex AlertContentRegex();
}

/// <summary>
/// Result of parsing a Razor documentation file.
/// </summary>
public record RazorDocResult
{
    public required string FilePath { get; init; }
    public string? ComponentName { get; init; }
    public string? Title { get; init; }
    public string? Description { get; init; }
    public List<DocumentationSection> Sections { get; init; } = [];
    public List<string> RelatedComponents { get; init; } = [];

    /// <summary>
    /// Route paths from href="/components/{route}" links (e.g. "togglegroup" or
    /// "chart-types/barchart"). Resolved against the docs route map by the caller.
    /// </summary>
    public List<string> RelatedComponentSlugs { get; init; } = [];

    /// <summary>
    /// Route paths this page is served under, from @page directives
    /// (e.g. "barchart" and its alias "bar").
    /// </summary>
    public List<string> Routes { get; init; } = [];
    public List<string> UsageNotes { get; init; } = [];
}

/// <summary>
/// A section in the documentation.
/// </summary>
public record DocumentationSection
{
    public required string Title { get; init; }
    public string? Content { get; init; }
    public bool HasExample { get; init; }
}
