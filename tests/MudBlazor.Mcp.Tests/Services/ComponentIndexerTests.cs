// Copyright (c) 2025 Mud MCP Contributors
// Licensed under the GNU General Public License v2.0. See LICENSE file in the project root for full license information.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MudBlazor.Mcp.Configuration;
using MudBlazor.Mcp.Models;
using MudBlazor.Mcp.Services;
using MudBlazor.Mcp.Services.Parsing;

namespace MudBlazor.Mcp.Tests.Services;

public class ComponentIndexerTests : IDisposable
{
    private readonly List<string> _tempDirs = [];

    public void Dispose()
    {
        foreach (var dir in _tempDirs)
        {
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch (DirectoryNotFoundException)
            {
                // Directory may already have been deleted; ignore during cleanup.
            }
            catch (IOException)
            {
                // Best-effort cleanup: ignore transient IO issues when deleting temp directories.
            }
            catch (UnauthorizedAccessException)
            {
                // Ignore permission issues during cleanup of test temp directories.
            }
        }
    }

    private string CreateTempRepoDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"mudmcp-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        _tempDirs.Add(dir);
        return dir;
    }

    private ComponentIndexer CreateIndexer(
        IGitRepositoryService? gitService = null,
        XmlDocParser? xmlParser = null,
        RazorDocParser? razorParser = null,
        ExampleExtractor? exampleExtractor = null,
        CategoryMapper? categoryMapper = null,
        string? dataPath = null)
    {
        var (indexer, _) = CreateIndexerWithContext(gitService, xmlParser, razorParser, exampleExtractor, categoryMapper, dataPath);
        return indexer;
    }

    private (ComponentIndexer Indexer, VersionContext Context) CreateIndexerWithContext(
        IGitRepositoryService? gitService = null,
        XmlDocParser? xmlParser = null,
        RazorDocParser? razorParser = null,
        ExampleExtractor? exampleExtractor = null,
        CategoryMapper? categoryMapper = null,
        string? dataPath = null,
        MudBlazorOptions? mudBlazorOptions = null)
    {
        gitService ??= Mock.Of<IGitRepositoryService>(s => 
            s.IsAvailable == true && 
            s.RepositoryPath == "/fake/repo");
        
        xmlParser ??= new XmlDocParser(Mock.Of<ILogger<XmlDocParser>>());
        razorParser ??= new RazorDocParser(Mock.Of<ILogger<RazorDocParser>>());
        exampleExtractor ??= new ExampleExtractor(Mock.Of<ILogger<ExampleExtractor>>());
        categoryMapper ??= new CategoryMapper(Mock.Of<ILogger<CategoryMapper>>());
        
        var options = Options.Create(mudBlazorOptions ?? new MudBlazorOptions());
        var logger = Mock.Of<ILogger<ComponentIndexer>>();

        // Use a temp data path so cached index files don't leak onto the real file system.
        var basePath = dataPath ?? Path.Combine(Path.GetTempPath(), $"mudmcp-test-{Guid.NewGuid():N}");
        _tempDirs.Add(basePath);
        var versionContext = new VersionContext($"0.0.0-test-{Guid.NewGuid():N}", basePath);

        var indexer = new ComponentIndexer(
            gitService,
            xmlParser,
            razorParser,
            exampleExtractor,
            categoryMapper,
            versionContext,
            options,
            logger);

        return (indexer, versionContext);
    }

    [Fact]
    public void IsIndexed_WhenNotBuilt_ReturnsFalse()
    {
        // Arrange
        var indexer = CreateIndexer();

        // Act & Assert
        Assert.False(indexer.IsIndexed);
    }

    [Fact]
    public void LastIndexed_WhenNotBuilt_ReturnsNull()
    {
        // Arrange
        var indexer = CreateIndexer();

        // Act & Assert
        Assert.Null(indexer.LastIndexed);
    }

    [Fact]
    public async Task GetAllComponentsAsync_WhenNotIndexed_ThrowsInvalidOperationException()
    {
        // Arrange
        var indexer = CreateIndexer();

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => 
            indexer.GetAllComponentsAsync());
    }

    [Fact]
    public async Task GetComponentAsync_WhenNotIndexed_ThrowsInvalidOperationException()
    {
        // Arrange
        var indexer = CreateIndexer();

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => 
            indexer.GetComponentAsync("MudButton"));
    }

    [Fact]
    public async Task GetCategoriesAsync_WhenNotIndexed_ThrowsInvalidOperationException()
    {
        // Arrange
        var indexer = CreateIndexer();

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => 
            indexer.GetCategoriesAsync());
    }

    [Fact]
    public async Task SearchComponentsAsync_WhenNotIndexed_ThrowsInvalidOperationException()
    {
        // Arrange
        var indexer = CreateIndexer();

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => 
            indexer.SearchComponentsAsync("button"));
    }

    [Fact]
    public async Task BuildIndexAsync_WhenRepositoryNotAvailable_ThrowsInvalidOperationException()
    {
        // Arrange
        var gitService = new Mock<IGitRepositoryService>();
        gitService.Setup(g => g.IsAvailable).Returns(false);
        gitService.Setup(g => g.EnsureRepositoryAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        
        var indexer = CreateIndexer(gitService: gitService.Object);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => 
            indexer.BuildIndexAsync());
    }

    [Fact]
    public async Task BuildIndexAsync_CancellationToken_IsPropagated()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        cts.Cancel();
        
        var gitService = new Mock<IGitRepositoryService>();
        gitService.Setup(g => g.EnsureRepositoryAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());
        
        var indexer = CreateIndexer(gitService: gitService.Object);

        // Act & Assert - TaskCanceledException derives from OperationCanceledException
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => 
            indexer.BuildIndexAsync(cts.Token));
    }

    [Fact]
    public async Task BuildIndexAsync_WithMultipleRazorCsFiles_IndexesAllComponents()
    {
        // Arrange - create a temp repo with multiple .razor.cs files in one directory
        var repoPath = CreateTempRepoDir();
        var buttonDir = Path.Combine(repoPath, "src", "MudBlazor", "Components", "Button");
        Directory.CreateDirectory(buttonDir);

        await File.WriteAllTextAsync(Path.Combine(buttonDir, "MudButton.razor.cs"), """
            namespace MudBlazor;
            public partial class MudButton : MudBaseButton
            {
                [Parameter] public string? Label { get; set; }
            }
            """);

        await File.WriteAllTextAsync(Path.Combine(buttonDir, "MudIconButton.razor.cs"), """
            namespace MudBlazor;
            public partial class MudIconButton : MudBaseButton
            {
                [Parameter] public string? Icon { get; set; }
            }
            """);

        await File.WriteAllTextAsync(Path.Combine(buttonDir, "MudFab.razor.cs"), """
            namespace MudBlazor;
            public partial class MudFab : MudBaseButton
            {
                [Parameter] public string? StartIcon { get; set; }
            }
            """);

        var gitService = new Mock<IGitRepositoryService>();
        gitService.Setup(g => g.IsAvailable).Returns(true);
        gitService.Setup(g => g.RepositoryPath).Returns(repoPath);
        gitService.Setup(g => g.EnsureRepositoryAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var indexer = CreateIndexer(gitService: gitService.Object);

        // Act
        await indexer.BuildIndexAsync();

        // Assert - all three components should be indexed
        var all = await indexer.GetAllComponentsAsync();
        Assert.Equal(3, all.Count);
        Assert.Contains(all, c => c.Name == "MudButton");
        Assert.Contains(all, c => c.Name == "MudIconButton");
        Assert.Contains(all, c => c.Name == "MudFab");
    }

    [Fact]
    public async Task BuildIndexAsync_WithMixedRazorCsAndCsFiles_IndexesBothTypes()
    {
        // Arrange - .razor.cs takes precedence over .cs for same component,
        // but different components in .cs-only form should still be indexed
        var repoPath = CreateTempRepoDir();
        var gridDir = Path.Combine(repoPath, "src", "MudBlazor", "Components", "Grid");
        Directory.CreateDirectory(gridDir);

        await File.WriteAllTextAsync(Path.Combine(gridDir, "MudGrid.razor.cs"), """
            namespace MudBlazor;
            public partial class MudGrid : MudComponentBase
            {
                [Parameter] public int Spacing { get; set; }
            }
            """);

        // This .cs file is for a DIFFERENT component (no corresponding .razor.cs)
        await File.WriteAllTextAsync(Path.Combine(gridDir, "MudItem.cs"), """
            namespace MudBlazor;
            public partial class MudItem : MudComponentBase
            {
                [Parameter] public int Xs { get; set; }
            }
            """);

        // This .cs file has a corresponding .razor.cs above, so it should NOT be indexed separately
        await File.WriteAllTextAsync(Path.Combine(gridDir, "MudGrid.cs"), """
            namespace MudBlazor;
            public partial class MudGrid
            {
                public void SomeMethod() { }
            }
            """);

        var gitService = new Mock<IGitRepositoryService>();
        gitService.Setup(g => g.IsAvailable).Returns(true);
        gitService.Setup(g => g.RepositoryPath).Returns(repoPath);
        gitService.Setup(g => g.EnsureRepositoryAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var indexer = CreateIndexer(gitService: gitService.Object);

        // Act
        await indexer.BuildIndexAsync();

        // Assert - MudGrid from .razor.cs and MudItem from .cs
        var all = await indexer.GetAllComponentsAsync();
        Assert.Equal(2, all.Count);
        Assert.Contains(all, c => c.Name == "MudGrid");
        Assert.Contains(all, c => c.Name == "MudItem");
    }

    [Fact]
    public async Task BuildIndexAsync_WithStaleCacheSchemaVersion_DeletesAndRebuilds()
    {
        // Arrange — create a repo with one component, and a stale index.json with wrong SchemaVersion
        var repoPath = CreateTempRepoDir();
        var buttonDir = Path.Combine(repoPath, "src", "MudBlazor", "Components", "Button");
        Directory.CreateDirectory(buttonDir);

        await File.WriteAllTextAsync(Path.Combine(buttonDir, "MudButton.razor.cs"), """
            namespace MudBlazor;
            public partial class MudButton : MudBaseButton
            {
                [Parameter] public string? Label { get; set; }
            }
            """);

        var gitService = new Mock<IGitRepositoryService>();
        gitService.Setup(g => g.IsAvailable).Returns(true);
        gitService.Setup(g => g.RepositoryPath).Returns(repoPath);
        gitService.Setup(g => g.EnsureRepositoryAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var (indexer, context) = CreateIndexerWithContext(gitService: gitService.Object);

        // Write a stale index.json with a wrong SchemaVersion (999)
        Directory.CreateDirectory(Path.GetDirectoryName(context.IndexPath)!);
        var staleJson = """
            {
                "SchemaVersion": 999,
                "IncludeInternalComponents": false,
                "IncludeDeprecatedComponents": true,
                "MaxExamplesPerComponent": 20,
                "Components": [],
                "ApiReferences": []
            }
            """;
        await File.WriteAllTextAsync(context.IndexPath, staleJson);

        // Act — should detect stale schema, delete the file, and rebuild from repo
        await indexer.BuildIndexAsync();

        // Assert — component was indexed from the repo (not the empty stale cache)
        var all = await indexer.GetAllComponentsAsync();
        Assert.Single(all);
        Assert.Contains(all, c => c.Name == "MudButton");
    }

    [Fact]
    public async Task BuildIndexAsync_WithStaleParsingOptions_DeletesAndRebuilds()
    {
        // Arrange — create a repo with one component, and a stale index.json with mismatched options
        var repoPath = CreateTempRepoDir();
        var buttonDir = Path.Combine(repoPath, "src", "MudBlazor", "Components", "Button");
        Directory.CreateDirectory(buttonDir);

        await File.WriteAllTextAsync(Path.Combine(buttonDir, "MudButton.razor.cs"), """
            namespace MudBlazor;
            public partial class MudButton : MudBaseButton
            {
                [Parameter] public string? Label { get; set; }
            }
            """);

        var gitService = new Mock<IGitRepositoryService>();
        gitService.Setup(g => g.IsAvailable).Returns(true);
        gitService.Setup(g => g.RepositoryPath).Returns(repoPath);
        gitService.Setup(g => g.EnsureRepositoryAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Default options: IncludeInternalComponents=false, IncludeDeprecatedComponents=true, MaxExamplesPerComponent=20
        var (indexer, context) = CreateIndexerWithContext(gitService: gitService.Object);

        // Write a stale index.json with correct SchemaVersion but mismatched IncludeInternalComponents
        Directory.CreateDirectory(Path.GetDirectoryName(context.IndexPath)!);
        var staleJson = """
            {
                "SchemaVersion": 2,
                "IncludeInternalComponents": true,
                "IncludeDeprecatedComponents": true,
                "MaxExamplesPerComponent": 20,
                "Components": [],
                "ApiReferences": []
            }
            """;
        await File.WriteAllTextAsync(context.IndexPath, staleJson);

        // Act — should detect options mismatch, delete the stale file, and rebuild
        await indexer.BuildIndexAsync();

        // Assert — component was indexed from the repo (not the empty stale cache)
        var all = await indexer.GetAllComponentsAsync();
        Assert.Single(all);
        Assert.Contains(all, c => c.Name == "MudButton");
        // The stale file should have been deleted and replaced with a new one
        Assert.True(File.Exists(context.IndexPath));
    }

    [Fact]
    public async Task BuildIndexAsync_WithCorruptedCacheFile_DeletesAndRebuilds()
    {
        // Arrange — create a repo with one component, and a corrupted (non-JSON) index.json
        var repoPath = CreateTempRepoDir();
        var buttonDir = Path.Combine(repoPath, "src", "MudBlazor", "Components", "Button");
        Directory.CreateDirectory(buttonDir);

        await File.WriteAllTextAsync(Path.Combine(buttonDir, "MudButton.razor.cs"), """
            namespace MudBlazor;
            public partial class MudButton : MudBaseButton
            {
                [Parameter] public string? Label { get; set; }
            }
            """);

        var gitService = new Mock<IGitRepositoryService>();
        gitService.Setup(g => g.IsAvailable).Returns(true);
        gitService.Setup(g => g.RepositoryPath).Returns(repoPath);
        gitService.Setup(g => g.EnsureRepositoryAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var (indexer, context) = CreateIndexerWithContext(gitService: gitService.Object);

        // Write corrupt data to the index.json path
        Directory.CreateDirectory(Path.GetDirectoryName(context.IndexPath)!);
        await File.WriteAllTextAsync(context.IndexPath, "{{not valid json!!");

        // Act — should detect corruption, delete the bad file, and rebuild from repo
        await indexer.BuildIndexAsync();

        // Assert — component was indexed from the repo (not the corrupted cache)
        var all = await indexer.GetAllComponentsAsync();
        Assert.Single(all);
        Assert.Contains(all, c => c.Name == "MudButton");
        // The corrupted file should have been deleted and replaced with a valid one
        Assert.True(File.Exists(context.IndexPath));
    }

    [Fact]
    public async Task BuildIndexAsync_SourceUrl_UsesVersionContextTag()
    {
        // Arrange — the SourceUrl should reference the version tag, not the hard-coded "dev" branch
        var repoPath = CreateTempRepoDir();
        var buttonDir = Path.Combine(repoPath, "src", "MudBlazor", "Components", "Button");
        Directory.CreateDirectory(buttonDir);

        await File.WriteAllTextAsync(Path.Combine(buttonDir, "MudButton.razor.cs"), """
            namespace MudBlazor;
            public partial class MudButton : MudBaseButton
            {
                [Parameter] public string? Label { get; set; }
            }
            """);

        var gitService = new Mock<IGitRepositoryService>();
        gitService.Setup(g => g.IsAvailable).Returns(true);
        gitService.Setup(g => g.RepositoryPath).Returns(repoPath);
        gitService.Setup(g => g.EnsureRepositoryAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var (indexer, context) = CreateIndexerWithContext(gitService: gitService.Object);

        // Act
        await indexer.BuildIndexAsync();

        // Assert — SourceUrl must contain the version tag, not "tree/dev"
        var component = await indexer.GetComponentAsync("MudButton");
        Assert.NotNull(component);
        Assert.NotNull(component.SourceUrl);
        Assert.Contains($"/tree/{context.Tag}/", component.SourceUrl);
        Assert.DoesNotContain("/tree/dev/", component.SourceUrl);
        Assert.EndsWith("/Components/Button", component.SourceUrl);
    }

    private Mock<IGitRepositoryService> CreateGitService(string repoPath)
    {
        var gitService = new Mock<IGitRepositoryService>();
        gitService.Setup(g => g.IsAvailable).Returns(true);
        gitService.Setup(g => g.RepositoryPath).Returns(repoPath);
        gitService.Setup(g => g.EnsureRepositoryAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        return gitService;
    }

    private async Task<string> CreateRepoWithButtonAsync()
    {
        var repoPath = CreateTempRepoDir();
        var buttonDir = Path.Combine(repoPath, "src", "MudBlazor", "Components", "Button");
        Directory.CreateDirectory(buttonDir);

        await File.WriteAllTextAsync(Path.Combine(buttonDir, "MudButton.razor.cs"), """
            namespace MudBlazor;
            public partial class MudButton : MudComponentBase
            {
                [Parameter] public string? Label { get; set; }
            }
            """);

        return repoPath;
    }

    [Fact]
    public async Task BuildIndexAsync_ExcludesInternalComponents_ByDefault()
    {
        // Arrange - one public and one internal component
        var repoPath = await CreateRepoWithButtonAsync();
        var buttonDir = Path.Combine(repoPath, "src", "MudBlazor", "Components", "Button");

        await File.WriteAllTextAsync(Path.Combine(buttonDir, "MudButtonHelper.cs"), """
            namespace MudBlazor;
            internal class MudButtonHelper
            {
                [Parameter] public string? Hidden { get; set; }
            }
            """);

        var (indexer, _) = CreateIndexerWithContext(gitService: CreateGitService(repoPath).Object);

        // Act - default options: IncludeInternalComponents = false
        await indexer.BuildIndexAsync();

        // Assert
        var all = await indexer.GetAllComponentsAsync();
        Assert.Single(all);
        Assert.Equal("MudButton", all[0].Name);
    }

    [Fact]
    public async Task BuildIndexAsync_IncludesInternalComponents_WhenEnabled()
    {
        // Arrange
        var repoPath = await CreateRepoWithButtonAsync();
        var buttonDir = Path.Combine(repoPath, "src", "MudBlazor", "Components", "Button");

        await File.WriteAllTextAsync(Path.Combine(buttonDir, "MudButtonHelper.cs"), """
            namespace MudBlazor;
            internal class MudButtonHelper
            {
                [Parameter] public string? Hidden { get; set; }
            }
            """);

        var options = new MudBlazorOptions();
        options.Parsing.IncludeInternalComponents = true;

        var (indexer, _) = CreateIndexerWithContext(
            gitService: CreateGitService(repoPath).Object,
            mudBlazorOptions: options);

        // Act
        await indexer.BuildIndexAsync();

        // Assert - the internal component is indexed and flagged
        var all = await indexer.GetAllComponentsAsync();
        Assert.Equal(2, all.Count);
        var helper = await indexer.GetComponentAsync("MudButtonHelper");
        Assert.NotNull(helper);
        Assert.True(helper.IsInternal);
    }

    [Fact]
    public async Task BuildIndexAsync_ExcludesDeprecatedComponents_WhenDisabled()
    {
        // Arrange
        var repoPath = await CreateRepoWithButtonAsync();
        var buttonDir = Path.Combine(repoPath, "src", "MudBlazor", "Components", "Button");

        await File.WriteAllTextAsync(Path.Combine(buttonDir, "MudOldButton.razor.cs"), """
            namespace MudBlazor;
            [Obsolete("Use MudButton instead.")]
            public partial class MudOldButton : MudComponentBase
            {
            }
            """);

        var options = new MudBlazorOptions();
        options.Parsing.IncludeDeprecatedComponents = false;

        var (indexer, _) = CreateIndexerWithContext(
            gitService: CreateGitService(repoPath).Object,
            mudBlazorOptions: options);

        // Act
        await indexer.BuildIndexAsync();

        // Assert
        var all = await indexer.GetAllComponentsAsync();
        Assert.Single(all);
        Assert.Equal("MudButton", all[0].Name);
    }

    [Fact]
    public async Task BuildIndexAsync_IncludesDeprecatedComponents_ByDefault()
    {
        // Arrange
        var repoPath = await CreateRepoWithButtonAsync();
        var buttonDir = Path.Combine(repoPath, "src", "MudBlazor", "Components", "Button");

        await File.WriteAllTextAsync(Path.Combine(buttonDir, "MudOldButton.razor.cs"), """
            namespace MudBlazor;
            [Obsolete("Use MudButton instead.")]
            public partial class MudOldButton : MudComponentBase
            {
            }
            """);

        var (indexer, _) = CreateIndexerWithContext(gitService: CreateGitService(repoPath).Object);

        // Act - default options: IncludeDeprecatedComponents = true
        await indexer.BuildIndexAsync();

        // Assert - included and flagged
        var old = await indexer.GetComponentAsync("MudOldButton");
        Assert.NotNull(old);
        Assert.True(old.IsDeprecated);
    }

    [Fact]
    public async Task BuildIndexAsync_CapsExamplesPerComponent()
    {
        // Arrange - three examples but a cap of two
        var repoPath = await CreateRepoWithButtonAsync();
        var exampleDir = Path.Combine(repoPath, "src", "MudBlazor.Docs", "Pages", "Components", "Button");
        Directory.CreateDirectory(exampleDir);

        for (var i = 1; i <= 3; i++)
        {
            await File.WriteAllTextAsync(
                Path.Combine(exampleDir, $"ButtonExample{i}.razor"),
                $"<MudButton>Example {i}</MudButton>");
        }

        var options = new MudBlazorOptions();
        options.Parsing.MaxExamplesPerComponent = 2;

        var (indexer, _) = CreateIndexerWithContext(
            gitService: CreateGitService(repoPath).Object,
            mudBlazorOptions: options);

        // Act
        await indexer.BuildIndexAsync();

        // Assert
        var examples = await indexer.GetExamplesAsync("MudButton");
        Assert.Equal(2, examples.Count);
    }

    [Fact]
    public async Task BuildIndexAsync_IndexesEnumsFromRepository()
    {
        // Arrange
        var repoPath = await CreateRepoWithButtonAsync();
        var enumsDir = Path.Combine(repoPath, "src", "MudBlazor", "Enums");
        Directory.CreateDirectory(enumsDir);

        await File.WriteAllTextAsync(Path.Combine(enumsDir, "Color.cs"), """
            namespace MudBlazor;

            /// <summary>
            /// Theme colors.
            /// </summary>
            public enum Color
            {
                /// <summary>The default theme color.</summary>
                Default = 0,
                /// <summary>The primary theme color.</summary>
                Primary = 1,
                /// <summary>The secondary theme color.</summary>
                Secondary = 2
            }
            """);

        var (indexer, _) = CreateIndexerWithContext(gitService: CreateGitService(repoPath).Object);

        // Act
        await indexer.BuildIndexAsync();

        // Assert - the enum is served from source, version-accurate
        var apiRef = await indexer.GetApiReferenceAsync("Color");
        Assert.NotNull(apiRef);
        Assert.Equal("enum", apiRef.Kind);
        Assert.NotNull(apiRef.EnumValues);
        Assert.Equal(3, apiRef.EnumValues.Count);
        Assert.Contains(apiRef.EnumValues, v => v.Name == "Primary" && v.Description == "The primary theme color.");
    }

    [Fact]
    public async Task BuildIndexAsync_IndexesBaseTypesForApiReference()
    {
        // Arrange - base classes live under src/MudBlazor/Base in the upstream tree
        var repoPath = await CreateRepoWithButtonAsync();
        var baseDir = Path.Combine(repoPath, "src", "MudBlazor", "Base");
        Directory.CreateDirectory(baseDir);

        await File.WriteAllTextAsync(Path.Combine(baseDir, "MudComponentBase.cs"), """
            namespace MudBlazor;
            public abstract class MudComponentBase
            {
                /// <summary>The CSS classes applied to this component.</summary>
                [Parameter] public string? Class { get; set; }

                /// <summary>The inline styles applied to this component.</summary>
                [Parameter] public string? Style { get; set; }
            }
            """);

        var (indexer, _) = CreateIndexerWithContext(gitService: CreateGitService(repoPath).Object);

        // Act
        await indexer.BuildIndexAsync();

        // Assert - base type is queryable for inherited-member walks
        var baseRef = await indexer.GetApiReferenceAsync("MudComponentBase");
        Assert.NotNull(baseRef);
        Assert.NotNull(baseRef.Members);
        Assert.Contains(baseRef.Members, m => m.Name == "Class" && m.MemberType == "Property");
        Assert.Contains(baseRef.Members, m => m.Name == "Style" && m.MemberType == "Property");
    }

    [Fact]
    public async Task GetRelatedComponentsAsync_WithFlexibleName_ResolvesCanonicalName()
    {
        // Arrange - MudIconButton inherits from MudButton
        var repoPath = await CreateRepoWithButtonAsync();
        var buttonDir = Path.Combine(repoPath, "src", "MudBlazor", "Components", "Button");

        await File.WriteAllTextAsync(Path.Combine(buttonDir, "MudIconButton.razor.cs"), """
            namespace MudBlazor;
            public partial class MudIconButton : MudButton
            {
                [Parameter] public string? Icon { get; set; }
            }
            """);

        var (indexer, _) = CreateIndexerWithContext(gitService: CreateGitService(repoPath).Object);
        await indexer.BuildIndexAsync();

        // Act - "Button" must resolve to "MudButton" before the relationship query
        var related = await indexer.GetRelatedComponentsAsync("Button", RelationshipType.Child);

        // Assert
        Assert.Contains(related, c => c.Name == "MudIconButton");
    }

    [Fact]
    public async Task GetRelatedComponentsAsync_HonorsRelationshipTypeFilter()
    {
        // Arrange - two sibling components in the same category
        var repoPath = await CreateRepoWithButtonAsync();
        var buttonDir = Path.Combine(repoPath, "src", "MudBlazor", "Components", "Button");

        await File.WriteAllTextAsync(Path.Combine(buttonDir, "MudIconButton.razor.cs"), """
            namespace MudBlazor;
            public partial class MudIconButton : MudComponentBase
            {
                [Parameter] public string? Icon { get; set; }
            }
            """);

        var (indexer, _) = CreateIndexerWithContext(gitService: CreateGitService(repoPath).Object);
        await indexer.BuildIndexAsync();

        // Act
        var siblings = await indexer.GetRelatedComponentsAsync("MudButton", RelationshipType.Sibling);
        var commonlyUsed = await indexer.GetRelatedComponentsAsync("MudButton", RelationshipType.CommonlyUsedWith);

        // Assert - siblings come from the shared category; commonly-used-with must NOT
        // fall back to siblings when no explicit relationships are documented
        Assert.Contains(siblings, c => c.Name == "MudIconButton");
        Assert.Empty(commonlyUsed);
    }

    [Fact]
    public async Task GetRelatedComponentsAsync_ResolvesGenericBaseTypes()
    {
        // Arrange - a child whose base type carries generic arguments
        var repoPath = await CreateRepoWithButtonAsync();
        var inputDir = Path.Combine(repoPath, "src", "MudBlazor", "Components", "Input");
        Directory.CreateDirectory(inputDir);

        await File.WriteAllTextAsync(Path.Combine(inputDir, "MudBaseInput.cs"), """
            namespace MudBlazor;
            public abstract class MudBaseInput<T> : MudComponentBase
            {
                [Parameter] public T? Value { get; set; }
            }
            """);

        await File.WriteAllTextAsync(Path.Combine(inputDir, "MudInput.razor.cs"), """
            namespace MudBlazor;
            public partial class MudInput<T> : MudBaseInput<T>
            {
            }
            """);

        var (indexer, _) = CreateIndexerWithContext(gitService: CreateGitService(repoPath).Object);
        await indexer.BuildIndexAsync();

        // Act
        var parents = await indexer.GetRelatedComponentsAsync("MudInput", RelationshipType.Parent);

        // Assert - "MudBaseInput<T>" resolves to the indexed "MudBaseInput"
        Assert.Contains(parents, c => c.Name == "MudBaseInput");
    }

    [Fact]
    public async Task BuildIndexAsync_WithUnreadableComponentFile_CountsParseFailure()
    {
        // File permission-based failure injection requires POSIX permissions.
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            return;
        }

        // Arrange - one readable component and one unreadable component file
        var repoPath = await CreateRepoWithButtonAsync();
        var buttonDir = Path.Combine(repoPath, "src", "MudBlazor", "Components", "Button");

        var unreadablePath = Path.Combine(buttonDir, "MudBrokenButton.razor.cs");
        await File.WriteAllTextAsync(unreadablePath, """
            namespace MudBlazor;
            public partial class MudBrokenButton : MudComponentBase
            {
            }
            """);
        File.SetUnixFileMode(unreadablePath, UnixFileMode.None);

        var (indexer, _) = CreateIndexerWithContext(gitService: CreateGitService(repoPath).Object);

        try
        {
            // Act - the failure flows through the real XmlDocParser file-read boundary
            await indexer.BuildIndexAsync();

            // Assert - the healthy component is indexed, the failure is counted
            var all = await indexer.GetAllComponentsAsync();
            Assert.Single(all);
            Assert.Equal(1, indexer.FailureStatistics.ComponentParseFailures);
            Assert.Equal(1, indexer.FailureStatistics.Total);
        }
        finally
        {
            File.SetUnixFileMode(unreadablePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    [Fact]
    public async Task BuildIndexAsync_WithUnreadableDocumentationFile_CountsParseFailure()
    {
        // File permission-based failure injection requires POSIX permissions.
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            return;
        }

        // Arrange - a component with an unreadable documentation page
        var repoPath = await CreateRepoWithButtonAsync();
        var docsDir = Path.Combine(repoPath, "src", "MudBlazor.Docs", "Pages", "Components", "Button");
        Directory.CreateDirectory(docsDir);

        var unreadablePath = Path.Combine(docsDir, "ButtonPage.razor");
        await File.WriteAllTextAsync(unreadablePath, "<DocsPageHeader Title=\"Button\" />");
        File.SetUnixFileMode(unreadablePath, UnixFileMode.None);

        var (indexer, _) = CreateIndexerWithContext(gitService: CreateGitService(repoPath).Object);

        try
        {
            // Act - the failure flows through the real RazorDocParser file-read boundary
            await indexer.BuildIndexAsync();

            // Assert
            Assert.Equal(1, indexer.FailureStatistics.DocumentationParseFailures);
            Assert.True(indexer.IsIndexed);
        }
        finally
        {
            File.SetUnixFileMode(unreadablePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    [Fact]
    public async Task BuildIndexAsync_ResolvesRelatedComponentsFromDocsRoutes()
    {
        // Arrange - MudFab is documented under the route "buttonfab" (not "fab"),
        // MudBadge is documented by a route page NOT named *Page.razor (real v9.7
        // shape: Badge/MudBadge.razor), and the Button docs page links both plus
        // mentions MudIconButton in prose.
        var repoPath = await CreateRepoWithButtonAsync();
        var buttonDir = Path.Combine(repoPath, "src", "MudBlazor", "Components", "Button");

        await File.WriteAllTextAsync(Path.Combine(buttonDir, "MudFab.razor.cs"), """
            namespace MudBlazor;
            public partial class MudFab : MudComponentBase
            {
            }
            """);

        await File.WriteAllTextAsync(Path.Combine(buttonDir, "MudIconButton.razor.cs"), """
            namespace MudBlazor;
            public partial class MudIconButton : MudComponentBase
            {
            }
            """);

        var badgeDir = Path.Combine(repoPath, "src", "MudBlazor", "Components", "Badge");
        Directory.CreateDirectory(badgeDir);
        await File.WriteAllTextAsync(Path.Combine(badgeDir, "MudBadge.razor.cs"), """
            namespace MudBlazor;
            public partial class MudBadge : MudComponentBase
            {
            }
            """);

        var docsDir = Path.Combine(repoPath, "src", "MudBlazor.Docs", "Pages", "Components");
        Directory.CreateDirectory(Path.Combine(docsDir, "Button"));
        Directory.CreateDirectory(Path.Combine(docsDir, "ButtonFAB"));
        Directory.CreateDirectory(Path.Combine(docsDir, "Badge"));

        // The Fab page declares the non-derivable route "buttonfab" and its component
        await File.WriteAllTextAsync(Path.Combine(docsDir, "ButtonFAB", "FabPage.razor"), """
            @page "/components/buttonfab"
            <DocsPage>
                <DocsPageHeader Title="Floating Action Button" Component="@nameof(MudFab)" />
            </DocsPage>
            """);

        // Real v9.7 filename shape: a route page not named *Page.razor
        await File.WriteAllTextAsync(Path.Combine(docsDir, "Badge", "MudBadge.razor"), """
            @page "/components/badge"
            <DocsPage>
                <DocsPageHeader Title="Badge" Component="@nameof(MudBadge)" />
            </DocsPage>
            """);

        // The Button page links the Fab route, mentions MudIconButton in prose,
        // and uses layout tags that must NOT become relationships
        await File.WriteAllTextAsync(Path.Combine(docsDir, "Button", "ButtonPage.razor"), """
            @page "/components/button"
            <DocsPage>
                <DocsPageHeader Title="Button" Component="@nameof(MudButton)" />
                <MudText>See <MudLink Href="/components/buttonfab#usage">FAB</MudLink> for floating buttons.</MudText>
                <MudText>Add a <MudLink Href="/components/badge">Badge</MudLink> to show counts.</MudText>
                <MudText>Works well with <CodeInline>MudIconButton</CodeInline> and <CodeInline>MudUnknownThing</CodeInline>.</MudText>
            </DocsPage>
            """);

        var (indexer, _) = CreateIndexerWithContext(gitService: CreateGitService(repoPath).Object);

        // Act
        await indexer.BuildIndexAsync();

        // Assert - route-resolved and prose-resolved relations only; no layout-tag junk
        var button = await indexer.GetComponentAsync("MudButton");
        Assert.NotNull(button);
        Assert.Contains("MudFab", button.RelatedComponents);
        Assert.Contains("MudBadge", button.RelatedComponents);
        Assert.Contains("MudIconButton", button.RelatedComponents);
        Assert.DoesNotContain("MudText", button.RelatedComponents);
        Assert.DoesNotContain("MudLink", button.RelatedComponents);
        Assert.DoesNotContain("MudUnknownThing", button.RelatedComponents);
        Assert.DoesNotContain("MudButton", button.RelatedComponents);
    }

    [Fact]
    public async Task BuildIndexAsync_IndexesNestedEnumsUnderComponents()
    {
        var repoPath = await CreateChartRepoAsync();
        var (indexer, _) = CreateIndexerWithContext(gitService: CreateGitService(repoPath).Object);

        await indexer.BuildIndexAsync();

        var apiRef = await indexer.GetApiReferenceAsync("ChartType");
        Assert.NotNull(apiRef);
        Assert.Equal("enum", apiRef.Kind);
        Assert.NotNull(apiRef.EnumValues);
        Assert.Contains(apiRef.EnumValues, v => v.Name == "Bar" && v.Description == "Data is displayed as rectangles.");
    }

    [Fact]
    public async Task BuildIndexAsync_IndexesSupportingTypesAsApiReferencesNotComponents()
    {
        var repoPath = CreateTempRepoDir();
        var pickerDir = Path.Combine(repoPath, "src", "MudBlazor", "Components", "DatePicker");
        Directory.CreateDirectory(pickerDir);

        await File.WriteAllTextAsync(Path.Combine(pickerDir, "MudDateRangePicker.razor.cs"), """
            namespace MudBlazor;
            public partial class MudDateRangePicker
            {
                [Parameter] public DateRange? DateRange { get; set; }
            }
            """);

        await File.WriteAllTextAsync(Path.Combine(pickerDir, "DateRange.cs"), """
            namespace MudBlazor;
            public class DateRange
            {
                public DateTime? Start { get; set; }
                public DateTime? End { get; set; }

                public string ToIsoDateString() => string.Empty;
            }
            """);

        var (indexer, _) = CreateIndexerWithContext(gitService: CreateGitService(repoPath).Object);
        await indexer.BuildIndexAsync();

        Assert.Null(await indexer.GetComponentAsync("DateRange"));
        var all = await indexer.GetAllComponentsAsync();
        Assert.DoesNotContain(all, c => c.Name == "DateRange");

        var apiRef = await indexer.GetApiReferenceAsync("DateRange");
        Assert.NotNull(apiRef);
        Assert.Equal("class", apiRef.Kind);
        Assert.NotNull(apiRef.Members);
        Assert.Contains(apiRef.Members, m => m.Name == "ToIsoDateString" && m.MemberType == "Method");
    }

    [Fact]
    public async Task BuildIndexAsync_IndexesComponentLocalBaseTypes()
    {
        var repoPath = await CreateChartRepoAsync();
        var (indexer, _) = CreateIndexerWithContext(gitService: CreateGitService(repoPath).Object);

        await indexer.BuildIndexAsync();

        Assert.Null(await indexer.GetComponentAsync("MudChartBase"));

        var apiRef = await indexer.GetApiReferenceAsync("MudChartBase");
        Assert.NotNull(apiRef);
        Assert.NotNull(apiRef.Members);
        Assert.Contains(apiRef.Members, m => m.Name == "ChartType" && m.MemberType == "Property");
        Assert.Contains(apiRef.Members, m => m.Name == "SelectedIndexChanged" && m.MemberType == "Event");
    }

    [Fact]
    public async Task BuildIndexAsync_PromotesParametersFromRazorInheritsBase()
    {
        var repoPath = await CreateChartRepoAsync();
        var (indexer, _) = CreateIndexerWithContext(gitService: CreateGitService(repoPath).Object);

        await indexer.BuildIndexAsync();

        var chart = await indexer.GetComponentAsync("MudChart");
        Assert.NotNull(chart);
        Assert.Equal("MudChartBase<T, IChartOptions>", chart.BaseType);
        Assert.Contains(chart.Parameters, p => p.Name == "ChartType" && p.Type == "ChartType");
        Assert.Contains(chart.Parameters, p => p.Name == "Width");
        Assert.Contains(chart.Events, e => e.Name == "SelectedIndexChanged");

        var apiRef = await indexer.GetApiReferenceAsync("MudChart");
        Assert.NotNull(apiRef);
        Assert.Contains(apiRef.Members!, m => m.Name == "ChartType" && m.MemberType == "Property");
        Assert.Contains(apiRef.Members!, m => m.Name == "RebuildChart" && m.MemberType == "Method");
    }

    private async Task<string> CreateChartRepoAsync()
    {
        var repoPath = CreateTempRepoDir();
        var chartDir = Path.Combine(repoPath, "src", "MudBlazor", "Components", "Chart");
        var baseDir = Path.Combine(chartDir, "Base");
        var enumDir = Path.Combine(chartDir, "Models", "Enums");
        Directory.CreateDirectory(baseDir);
        Directory.CreateDirectory(enumDir);

        await File.WriteAllTextAsync(Path.Combine(chartDir, "MudChart.razor"), """
            @inherits MudChartBase<T, IChartOptions>
            @typeparam T
            """);

        await File.WriteAllTextAsync(Path.Combine(chartDir, "MudChart.razor.cs"), """
            namespace MudBlazor;
            public partial class MudChart<T>
            {
                public override void RebuildChart() { }
            }
            """);

        await File.WriteAllTextAsync(Path.Combine(baseDir, "MudChartBase.cs"), """
            namespace MudBlazor;
            public abstract class MudChartBase<T, TOptions>
            {
                [Parameter] public ChartType ChartType { get; set; }
                [Parameter] public string Width { get; set; } = "80%";
                [Parameter] public EventCallback<int> SelectedIndexChanged { get; set; }
            }
            """);

        await File.WriteAllTextAsync(Path.Combine(enumDir, "ChartType.cs"), """
            namespace MudBlazor;

            public enum ChartType
            {
                /// <summary>Data is displayed as rectangles.</summary>
                Bar,
                Pie
            }
            """);

        return repoPath;
    }
}
