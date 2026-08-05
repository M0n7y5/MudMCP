// Copyright (c) 2026 Mud MCP Contributors
// Licensed under the GNU General Public License v2.0. See LICENSE file in the project root for full license information.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor.Mcp.Configuration;
using MudBlazor.Mcp.Models;
using MudBlazor.Mcp.Services;
using MudBlazor.Mcp.Tools;

namespace MudBlazor.Mcp.Tests.Tools;

public class ApiReferenceToolsTests
{
    private static readonly ILogger<ApiReferenceTools> NullLogger =
        NullLoggerFactory.Instance.CreateLogger<ApiReferenceTools>();

    private static readonly VersionContext _versionContext = new("9.0.0");

    #region GetEnumValuesAsync Tests

    [Fact]
    public async Task GetEnumValuesAsync_WithIndexedEnum_ReturnsValues()
    {
        // Arrange
        var indexer = CreateEnumIndexer();

        // Act
        var result = await ApiReferenceTools.GetEnumValuesAsync(
            indexer, NullLogger, _versionContext, "Color", CancellationToken.None);

        // Assert - values come from the indexed enum, not a hard-coded catalog
        Assert.Contains("Color Enum Values", result);
        Assert.Contains("Primary", result);
        Assert.Contains("Secondary", result);
        Assert.Contains("Success", result);
        Assert.Contains("Error", result);
        Assert.Contains("Theme colors.", result);
    }

    [Fact]
    public async Task GetEnumValuesAsync_UsageExample_ShowsEnumTypePrefix()
    {
        // Arrange
        var indexer = CreateEnumIndexer();

        // Act
        var result = await ApiReferenceTools.GetEnumValuesAsync(
            indexer, NullLogger, _versionContext, "Color", CancellationToken.None);

        // Assert - usage example must show EnumType.Value syntax for the first value
        Assert.Contains("Usage Example", result);
        Assert.Contains("Color.Primary", result);
    }

    [Fact]
    public async Task GetEnumValuesAsync_WithEmptyEnumName_ThrowsMcpException()
    {
        // Arrange
        var indexer = new Mock<IComponentIndexer>();

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ModelContextProtocol.McpException>(async () =>
            await ApiReferenceTools.GetEnumValuesAsync(
                indexer.Object, NullLogger, _versionContext, "", CancellationToken.None));

        Assert.Contains("enumName", ex.Message);
    }

    [Fact]
    public async Task GetEnumValuesAsync_WithNullEnumName_ThrowsMcpException()
    {
        // Arrange
        var indexer = new Mock<IComponentIndexer>();

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ModelContextProtocol.McpException>(async () =>
            await ApiReferenceTools.GetEnumValuesAsync(
                indexer.Object, NullLogger, _versionContext, null!, CancellationToken.None));

        Assert.Contains("enumName", ex.Message);
    }

    [Fact]
    public async Task GetEnumValuesAsync_WithUnknownEnum_ThrowsMcpException()
    {
        // Arrange
        var indexer = new Mock<IComponentIndexer>();
        indexer.Setup(x => x.GetApiReferenceAsync("UnknownEnum", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ApiReference?)null);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ModelContextProtocol.McpException>(async () =>
            await ApiReferenceTools.GetEnumValuesAsync(
                indexer.Object, NullLogger, _versionContext, "UnknownEnum", CancellationToken.None));

        Assert.Contains("not found", ex.Message);
    }

    [Fact]
    public async Task GetEnumValuesAsync_WithNonEnumType_ThrowsMcpException()
    {
        // Arrange - a class type must not be served as an enum
        var indexer = CreateMockIndexer();

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ModelContextProtocol.McpException>(async () =>
            await ApiReferenceTools.GetEnumValuesAsync(
                indexer, NullLogger, _versionContext, "MudButton", CancellationToken.None));

        Assert.Contains("not found", ex.Message);
    }

    #endregion

    #region GetApiReferenceAsync Tests

    [Fact]
    public async Task GetApiReferenceAsync_WithValidComponent_ReturnsReference()
    {
        // Arrange
        var indexer = CreateMockIndexer();

        // Act
        var result = await ApiReferenceTools.GetApiReferenceAsync(
            indexer, NullLogger, _versionContext, "MudButton", "all", CancellationToken.None);

        // Assert
        Assert.Contains("MudButton", result);
        Assert.Contains("API Reference", result);
    }

    [Fact]
    public async Task GetApiReferenceAsync_WithInvalidType_ThrowsMcpException()
    {
        // Arrange
        var indexer = new Mock<IComponentIndexer>();
        indexer.Setup(x => x.GetApiReferenceAsync("Unknown", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ApiReference?)null);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ModelContextProtocol.McpException>(async () =>
            await ApiReferenceTools.GetApiReferenceAsync(
                indexer.Object, NullLogger, _versionContext, "Unknown", "all", CancellationToken.None));

        Assert.Contains("not found", ex.Message);
    }

    [Fact]
    public async Task GetApiReferenceAsync_WithEmptyTypeName_ThrowsMcpException()
    {
        // Arrange
        var indexer = new Mock<IComponentIndexer>();

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ModelContextProtocol.McpException>(async () =>
            await ApiReferenceTools.GetApiReferenceAsync(
                indexer.Object, NullLogger, _versionContext, "", "all", CancellationToken.None));

        Assert.Contains("typeName", ex.Message);
    }

    [Fact]
    public async Task GetApiReferenceAsync_WithInvalidMemberType_ThrowsMcpException()
    {
        // Arrange
        var indexer = CreateMockIndexer();

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ModelContextProtocol.McpException>(async () =>
            await ApiReferenceTools.GetApiReferenceAsync(
                indexer, NullLogger, _versionContext, "MudButton", "invalid", CancellationToken.None));

        Assert.Contains("memberType", ex.Message);
    }

    [Fact]
    public async Task GetApiReferenceAsync_FilterByProperties_ReturnsOnlyProperties()
    {
        // Arrange
        var indexer = CreateMockIndexer();

        // Act
        var result = await ApiReferenceTools.GetApiReferenceAsync(
            indexer, NullLogger, _versionContext, "MudButton", "properties", CancellationToken.None);

        // Assert
        Assert.Contains("Properties", result);
        Assert.Contains("Color", result);
    }

    [Fact]
    public async Task GetApiReferenceAsync_WithEnumKind_RendersValues()
    {
        // Arrange
        var indexer = CreateEnumIndexer();

        // Act
        var result = await ApiReferenceTools.GetApiReferenceAsync(
            indexer, NullLogger, _versionContext, "Color", "all", CancellationToken.None);

        // Assert - enums render their values, not empty member tables
        Assert.Contains("## Values", result);
        Assert.Contains("Primary", result);
        Assert.DoesNotContain("## Properties", result);
    }

    #endregion

    private static IComponentIndexer CreateMockIndexer()
    {
        var indexer = new Mock<IComponentIndexer>();

        var apiReference = new ApiReference(
            TypeName: "MudButton",
            Namespace: "MudBlazor",
            Summary: "A Material Design button component",
            BaseType: "MudBaseButton",
            Members: [
                new ApiMember("Color", "Property", "Color", "The button color"),
                new ApiMember("Variant", "Property", "Variant", "The button variant"),
                new ApiMember("OnClick", "Event", "EventCallback<MouseEventArgs>", "Click event"),
                new ApiMember("FocusAsync", "Method", "Task", "Focus the button")
            ]
        );

        indexer.Setup(x => x.GetApiReferenceAsync("MudButton", It.IsAny<CancellationToken>()))
            .ReturnsAsync(apiReference);

        return indexer.Object;
    }

    private static IComponentIndexer CreateEnumIndexer()
    {
        var indexer = new Mock<IComponentIndexer>();

        var colorEnum = new ApiReference(
            TypeName: "Color",
            Namespace: "MudBlazor",
            Summary: "Theme colors.",
            BaseType: null,
            Members: null,
            Kind: "enum",
            EnumValues: [
                new EnumValue("Primary", "1", "Primary theme color"),
                new EnumValue("Secondary", "2", "Secondary theme color"),
                new EnumValue("Success", "4", "Success green color"),
                new EnumValue("Error", "5", "Error red color")
            ]);

        indexer.Setup(x => x.GetApiReferenceAsync("Color", It.IsAny<CancellationToken>()))
            .ReturnsAsync(colorEnum);

        return indexer.Object;
    }
}
