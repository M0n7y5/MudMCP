// Copyright (c) 2025 Mud MCP Contributors
// Licensed under the GNU General Public License v2.0. See LICENSE file in the project root for full license information.

using Microsoft.Extensions.Logging;
using MudBlazor.Mcp.Services.Parsing;

namespace MudBlazor.Mcp.Tests.Parsing;

public class ExampleExtractorTests : IDisposable
{
    private readonly ExampleExtractor _extractor;
    private readonly List<string> _tempDirs = [];

    public ExampleExtractorTests()
    {
        var logger = Mock.Of<ILogger<ExampleExtractor>>();
        _extractor = new ExampleExtractor(logger);
    }

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
                // Ignore if the directory has already been deleted.
            }
            catch (IOException)
            {
                // Ignore I/O errors during cleanup of temporary directories.
            }
            catch (UnauthorizedAccessException)
            {
                // Ignore access issues during cleanup of temporary directories.
            }
        }
    }

    [Fact]
    public async Task ParseExampleFileAsync_WithRazorExample_ExtractsMarkupAndCode()
    {
        // Arrange
        var tempFile = Path.GetTempFileName() + ".razor";
        var content = """
            @* Basic button example *@
            <MudButton Color="Color.Primary" Variant="Variant.Filled">
                Click Me
            </MudButton>

            @code {
                private void HandleClick()
                {
                    Console.WriteLine("Clicked!");
                }
            }
            """;
        await File.WriteAllTextAsync(tempFile, content);

        try
        {
            // Act
            var result = await _extractor.ParseExampleFileAsync(tempFile, "MudButton", CancellationToken.None);

            // Assert
            Assert.NotNull(result);
            Assert.Contains("MudButton", result.RazorMarkup);
            Assert.Contains("HandleClick", result.CSharpCode);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ParseExampleFileAsync_WithNoCodeBlock_OnlyExtractsMarkup()
    {
        // Arrange
        var tempFile = Path.GetTempFileName() + ".razor";
        var content = """
            <MudButton Color="Color.Primary">
                Simple Button
            </MudButton>
            """;
        await File.WriteAllTextAsync(tempFile, content);

        try
        {
            // Act
            var result = await _extractor.ParseExampleFileAsync(tempFile, "MudButton", CancellationToken.None);

            // Assert
            Assert.NotNull(result);
            Assert.Contains("MudButton", result.RazorMarkup);
            Assert.Null(result.CSharpCode);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ParseExampleFileAsync_WithFeatures_ExtractsFeaturedFeatures()
    {
        // Arrange
        var tempFile = Path.GetTempFileName() + ".razor";
        var content = """
            <MudButton Color="Color.Primary" Variant="Variant.Filled" Size="Size.Large" @onclick="HandleClick">
                Click Me
            </MudButton>

            @code {
                private void HandleClick() { }
            }
            """;
        await File.WriteAllTextAsync(tempFile, content);

        try
        {
            // Act
            var result = await _extractor.ParseExampleFileAsync(tempFile, "MudButton", CancellationToken.None);

            // Assert
            Assert.NotNull(result);
            Assert.NotEmpty(result.Features);
            // Should detect common features like Colors, Variants, Sizes
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ParseExampleFileAsync_CleansUpDirectives()
    {
        // Arrange
        var tempFile = Path.GetTempFileName() + ".razor";
        var content = """
            @page "/components/button/basic"
            @using MudBlazor
            @namespace MudBlazor.Docs.Examples

            <MudButton>Test</MudButton>
            """;
        await File.WriteAllTextAsync(tempFile, content);

        try
        {
            // Act
            var result = await _extractor.ParseExampleFileAsync(tempFile, "MudButton", CancellationToken.None);

            // Assert
            Assert.NotNull(result);
            Assert.DoesNotContain("@page", result.RazorMarkup);
            Assert.DoesNotContain("@using", result.RazorMarkup);
            Assert.DoesNotContain("@namespace", result.RazorMarkup);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ParseExampleFileAsync_NonExistentFile_ReturnsNull()
    {
        // Act
        var result = await _extractor.ParseExampleFileAsync("/nonexistent/path.razor", "MudButton", CancellationToken.None);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task ExtractExamplesAsync_WithMismatchedCasing_FindsExamples()
    {
        // Arrange - simulate a case-sensitive filesystem scenario where the folder
        // name differs in casing from what the component name would produce.
        // Component "MudCheckBox" strips "Mud" → "CheckBox", but the folder is "Checkbox".
        var tempDir = Path.Combine(Path.GetTempPath(), $"mudmcp-test-{Guid.NewGuid():N}");
        _tempDirs.Add(tempDir);
        
        // Create folder with lowercase 'b' (like on the real MudBlazor repo)
        var exampleDir = Path.Combine(tempDir, "Pages", "Components", "Checkbox");
        Directory.CreateDirectory(exampleDir);

        await File.WriteAllTextAsync(Path.Combine(exampleDir, "CheckboxBasicExample.razor"), """
            <MudCheckBox @bind-Value="@checked">Check me</MudCheckBox>
            @code {
                private bool @checked = false;
            }
            """);

        // Act - pass "MudCheckBox" which strips to "CheckBox" (capital B)
        var result = await _extractor.ExtractExamplesAsync(tempDir, "MudCheckBox", CancellationToken.None);

        // Assert - should find the example despite casing mismatch
        Assert.Single(result.Examples);
        Assert.Equal(0, result.FailureCount);
    }

    [Fact]
    public async Task ExtractExamplesAsync_WithExactCasing_FindsExamples()
    {
        // Arrange - folder casing matches exactly
        var tempDir = Path.Combine(Path.GetTempPath(), $"mudmcp-test-{Guid.NewGuid():N}");
        _tempDirs.Add(tempDir);
        
        var exampleDir = Path.Combine(tempDir, "Pages", "Components", "Button");
        Directory.CreateDirectory(exampleDir);

        await File.WriteAllTextAsync(Path.Combine(exampleDir, "ButtonBasicExample.razor"), """
            <MudButton>Click</MudButton>
            """);

        // Act
        var result = await _extractor.ExtractExamplesAsync(tempDir, "MudButton", CancellationToken.None);

        // Assert
        Assert.Single(result.Examples);
        Assert.Equal(0, result.FailureCount);
    }

    [Fact]
    public async Task ExtractExamplesAsync_WithPluralDocsFolder_FindsExamples()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"mudmcp-test-{Guid.NewGuid():N}");
        _tempDirs.Add(tempDir);

        var exampleDir = Path.Combine(tempDir, "Pages", "Components", "Charts");
        Directory.CreateDirectory(exampleDir);

        await File.WriteAllTextAsync(Path.Combine(exampleDir, "BarChartBasicExample.razor"), """
            <MudChart ChartType="ChartType.Bar" />
            """);

        var result = await _extractor.ExtractExamplesAsync(tempDir, "MudChart", CancellationToken.None);

        Assert.Single(result.Examples);
        Assert.Equal(0, result.FailureCount);
        Assert.Contains("MudChart", result.Examples[0].RazorMarkup);
    }

    [Fact]
    public async Task ExtractExamplesAsync_WithUnreadableExampleFile_CountsFailureAndKeepsPartialResults()
    {
        // File permission-based failure injection requires POSIX permissions.
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            return;
        }

        // Arrange - one readable and one unreadable example file
        var tempDir = Path.Combine(Path.GetTempPath(), $"mudmcp-test-{Guid.NewGuid():N}");
        _tempDirs.Add(tempDir);

        var exampleDir = Path.Combine(tempDir, "Pages", "Components", "Button");
        Directory.CreateDirectory(exampleDir);

        await File.WriteAllTextAsync(Path.Combine(exampleDir, "ButtonBasicExample.razor"),
            "<MudButton>Click</MudButton>");

        var unreadablePath = Path.Combine(exampleDir, "ButtonBrokenExample.razor");
        await File.WriteAllTextAsync(unreadablePath, "<MudButton>Broken</MudButton>");
        File.SetUnixFileMode(unreadablePath, UnixFileMode.None);

        try
        {
            // Act - failure goes through the real file-read boundary
            var result = await _extractor.ExtractExamplesAsync(tempDir, "MudButton", CancellationToken.None);

            // Assert - partial results are kept and the failure is reported
            Assert.Single(result.Examples);
            Assert.Equal(1, result.FailureCount);
        }
        finally
        {
            // Restore permissions so Dispose can delete the directory.
            File.SetUnixFileMode(unreadablePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }
}
