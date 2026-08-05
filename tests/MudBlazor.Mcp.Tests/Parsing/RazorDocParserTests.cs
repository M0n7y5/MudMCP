// Copyright (c) 2026 Mud MCP Contributors
// Licensed under the GNU General Public License v2.0. See LICENSE file in the project root for full license information.

using Microsoft.Extensions.Logging;
using MudBlazor.Mcp.Services.Parsing;

namespace MudBlazor.Mcp.Tests.Parsing;

public class RazorDocParserTests
{
    private readonly RazorDocParser _parser = new(Mock.Of<ILogger<RazorDocParser>>());

    [Fact]
    public void ParseDocumentation_WithComponentLinks_ExtractsRouteSlugs()
    {
        // Arrange - real MudBlazor docs link related components via route slugs
        var content = """
            @page "/components/alert"
            <DocsPage>
                <DocsPageHeader Title="Alert" Component="@nameof(MudAlert)" />
                <MudText>Check out <MudLink Href="/components/snackbar">Snackbar</MudLink>.</MudText>
                <MudText>See also <MudLink Href="/components/togglegroup">Toggle Group</MudLink>.</MudText>
            </DocsPage>
            """;

        // Act
        var result = _parser.ParseDocumentation(content, "AlertPage.razor");

        // Assert
        Assert.NotNull(result);
        Assert.Contains("snackbar", result.RelatedComponentSlugs);
        Assert.Contains("togglegroup", result.RelatedComponentSlugs);
    }

    [Fact]
    public void ParseDocumentation_WithFragmentsAndQueries_StripsToRoute()
    {
        // Arrange - links may target page fragments; nested aliases stay whole
        var content = """
            <MudLink Href="/components/datagrid#advanced-data-grid">Advanced</MudLink>
            <MudLink Href="/components/dialog#configuration">Dialog config</MudLink>
            <MudLink Href="/components/table?tab=sorting">Table</MudLink>
            <MudLink Href="/components/chart-types/barchart">Bar chart</MudLink>
            """;

        // Act
        var result = _parser.ParseDocumentation(content, "GridPage.razor");

        // Assert
        Assert.NotNull(result);
        Assert.Contains("datagrid", result.RelatedComponentSlugs);
        Assert.Contains("dialog", result.RelatedComponentSlugs);
        Assert.Contains("table", result.RelatedComponentSlugs);
        Assert.Contains("chart-types/barchart", result.RelatedComponentSlugs);
        Assert.DoesNotContain(result.RelatedComponentSlugs, s => s.Contains('#') || s.Contains('?'));
    }

    [Fact]
    public void ParseDocumentation_WithPageDirectives_ExtractsAllRoutes()
    {
        // Arrange - chart pages expose multiple route aliases
        var content = """
            @page "/components/barchart"
            @page "/components/chart-types/barchart"
            @page "/components/bar"
            <DocsPage>
                <DocsPageHeader Title="Bar Chart" Component="@nameof(MudBlazor.Charts.Bar<T>)" />
            </DocsPage>
            """;

        // Act
        var result = _parser.ParseDocumentation(content, "BarChartPage.razor");

        // Assert - every alias is captured and the header wins over the file name
        Assert.NotNull(result);
        Assert.Equal(["barchart", "chart-types/barchart", "bar"], result.Routes);
        Assert.Equal("Bar", result.ComponentName);
    }

    [Fact]
    public void ParseDocumentation_HeaderComponent_OverridesFileNameDerivation()
    {
        // Arrange - ButtonFAB/FabPage.razor documents MudFab under route "buttonfab"
        var content = """
            @page "/components/buttonfab"
            <DocsPage>
                <DocsPageHeader Title="Floating Action Button" Component="@nameof(MudFab)" />
            </DocsPage>
            """;

        // Act
        var result = _parser.ParseDocumentation(content, "FabPage.razor");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("MudFab", result.ComponentName);
        Assert.Equal(["buttonfab"], result.Routes);
    }

    [Fact]
    public void ParseDocumentation_WithApiTypeLink_ExtractsTypeName()
    {
        // Arrange - <ApiTypeLink TypeName="MudButton" /> is an explicit reference
        var content = """
            <Description>
                Similar to <ApiTypeLink TypeName="MudButton" /> but with icons.
            </Description>
            """;

        // Act
        var result = _parser.ParseDocumentation(content, "IconButtonPage.razor");

        // Assert
        Assert.NotNull(result);
        Assert.Contains("MudButton", result.RelatedComponents);
    }

    [Fact]
    public void ParseDocumentation_WithCodeInlineMentions_ExtractsComponentNames()
    {
        // Arrange - prose references like <CodeInline>MudIconButton</CodeInline>
        var content = """
            <Description>
                <CodeInline>MudButtonGroup</CodeInline> can also make use of
                <CodeInline>MudIconButton</CodeInline> and <CodeInline>MudToggleIconButton</CodeInline>.
                A <CodeInline>MudMenu</CodeInline> can be used to create a split button.
                Set <CodeInline>Vertical="true"</CodeInline> for vertical layout.
            </Description>
            """;

        // Act
        var result = _parser.ParseDocumentation(content, "ButtonGroupPage.razor");

        // Assert - component mentions extracted; attribute snippets are not
        Assert.NotNull(result);
        Assert.Contains("MudIconButton", result.RelatedComponents);
        Assert.Contains("MudToggleIconButton", result.RelatedComponents);
        Assert.Contains("MudMenu", result.RelatedComponents);
        Assert.DoesNotContain(result.RelatedComponents, n => n.Contains('='));
    }

    [Fact]
    public void ParseDocumentation_LayoutMarkupTags_AreNotTreatedAsRelated()
    {
        // Arrange - a page built from MudText/MudLink/MudAlert layout tags with no real references
        var content = """
            <DocsPage>
                <MudText Typo="Typo.h6">Note</MudText>
                <MudAlert Severity="Severity.Info">Just a note.</MudAlert>
                <MudLink Href="https://example.com">External</MudLink>
            </DocsPage>
            """;

        // Act
        var result = _parser.ParseDocumentation(content, "AlertPage.razor");

        // Assert - markup usage must not fabricate relationships
        Assert.NotNull(result);
        Assert.Empty(result.RelatedComponents);
        Assert.Empty(result.RelatedComponentSlugs);
    }

    [Fact]
    public void ParseDocumentation_ComponentName_FallsBackToFileName()
    {
        // Act - no DocsPageHeader Component attribute present
        var result = _parser.ParseDocumentation("<DocsPage />", "ButtonPage.razor");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("MudButton", result.ComponentName);
    }
}
