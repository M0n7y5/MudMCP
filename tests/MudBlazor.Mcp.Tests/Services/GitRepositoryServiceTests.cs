// Copyright (c) 2026 Mud MCP Contributors
// Licensed under the GNU General Public License v2.0.

using LibGit2Sharp;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MudBlazor.Mcp.Configuration;
using MudBlazor.Mcp.Services;

namespace MudBlazor.Mcp.Tests.Services;

public sealed class GitRepositoryServiceTests : IDisposable
{
    private const string RequestedVersion = "1.0.0";
    private readonly List<string> _tempDirectories = [];

    [Fact]
    public async Task EnsureRepositoryAsync_WithMissingTag_DoesNotPublishIncompleteRepository()
    {
        var (sourcePath, _) = CreateSourceRepository(addRequestedTag: false);
        var dataPath = CreateTempDirectory();
        using var service = CreateService(sourcePath, dataPath);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.EnsureRepositoryAsync(CancellationToken.None));

        Assert.Contains("Tag 'v1.0.0' not found", exception.Message);
        Assert.False(Directory.Exists(service.RepositoryPath));
        Assert.Empty(GetStagingDirectories(service.RepositoryPath));
    }

    [Fact]
    public async Task EnsureRepositoryAsync_WhenStagingFailsAtCapacity_PreservesCachedVersion()
    {
        var (sourcePath, _) = CreateSourceRepository(addRequestedTag: false);
        var dataPath = CreateTempDirectory();
        var cachedVersionPath = Path.Combine(dataPath, "v0.9.0");
        var sentinelPath = Path.Combine(cachedVersionPath, "sentinel.txt");
        Directory.CreateDirectory(cachedVersionPath);
        File.WriteAllText(sentinelPath, "preserve");
        using var service = CreateService(sourcePath, dataPath, maxCachedVersions: 1);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.EnsureRepositoryAsync(CancellationToken.None));

        Assert.True(File.Exists(sentinelPath));
        Assert.False(Directory.Exists(service.RepositoryPath));
        Assert.Empty(GetStagingDirectories(service.RepositoryPath));
    }

    [Fact]
    public async Task EnsureRepositoryAsync_WithRequestedTag_PublishesTagCheckout()
    {
        var (sourcePath, taggedCommitSha) = CreateSourceRepository(
            addRequestedTag: true,
            addCommitAfterTag: true);
        var dataPath = CreateTempDirectory();
        using var service = CreateService(sourcePath, dataPath);

        var cloned = await service.EnsureRepositoryAsync(CancellationToken.None);
        var reused = await service.EnsureRepositoryAsync(CancellationToken.None);

        Assert.True(cloned);
        Assert.False(reused);
        Assert.True(service.IsAvailable);
        Assert.Equal(taggedCommitSha[..7], service.CurrentCommitHash);
        Assert.Empty(GetStagingDirectories(service.RepositoryPath));
    }

    [Fact]
    public async Task EnsureRepositoryAsync_WithWrongExistingCommit_ReplacesRepository()
    {
        var (sourcePath, taggedCommitSha) = CreateSourceRepository(
            addRequestedTag: true,
            addCommitAfterTag: true);
        var dataPath = CreateTempDirectory();
        using var service = CreateService(sourcePath, dataPath);

        Directory.CreateDirectory(Path.GetDirectoryName(service.RepositoryPath)!);
        Repository.Clone(sourcePath, service.RepositoryPath);
        using (var existingRepository = new Repository(service.RepositoryPath))
        {
            Assert.NotEqual(taggedCommitSha, existingRepository.Head.Tip.Sha);
        }

        var replaced = await service.EnsureRepositoryAsync(CancellationToken.None);

        Assert.True(replaced);
        Assert.Equal(taggedCommitSha[..7], service.CurrentCommitHash);
        Assert.Empty(GetStagingDirectories(service.RepositoryPath));
    }

    private GitRepositoryService CreateService(
        string sourcePath,
        string dataPath,
        int maxCachedVersions = 3)
    {
        var options = Options.Create(new MudBlazorOptions
        {
            Repository = new MudBlazor.Mcp.Configuration.RepositoryOptions
            {
                Url = sourcePath,
                DataPath = dataPath,
                MaxCachedVersions = maxCachedVersions
            }
        });
        var versionContext = new VersionContext(RequestedVersion, dataPath);
        var cacheManager = new VersionCacheManager(
            dataPath,
            maxVersions: maxCachedVersions,
            logger: NullLogger<VersionCacheManager>.Instance);

        return new GitRepositoryService(
            NullLogger<GitRepositoryService>.Instance,
            options,
            versionContext,
            cacheManager);
    }

    private (string Path, string TaggedCommitSha) CreateSourceRepository(
        bool addRequestedTag,
        bool addCommitAfterTag = false)
    {
        var sourcePath = CreateTempDirectory();
        Repository.Init(sourcePath);

        using var repository = new Repository(sourcePath);
        var signature = new Signature(
            "Mud MCP Tests",
            "tests@mudmcp.invalid",
            DateTimeOffset.UtcNow);

        File.WriteAllText(Path.Combine(sourcePath, "component.txt"), "tagged");
        Commands.Stage(repository, "component.txt");
        var taggedCommit = repository.Commit("Create tagged version", signature, signature);

        if (addRequestedTag)
        {
            repository.Tags.Add($"v{RequestedVersion}", taggedCommit);
        }

        if (addCommitAfterTag)
        {
            File.WriteAllText(Path.Combine(sourcePath, "component.txt"), "newer head");
            Commands.Stage(repository, "component.txt");
            repository.Commit("Advance default branch", signature, signature);
        }

        return (sourcePath, taggedCommit.Sha);
    }

    private string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"mudmcp-git-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        _tempDirectories.Add(path);
        return path;
    }

    private static string[] GetStagingDirectories(string repositoryPath)
    {
        var parentPath = Path.GetDirectoryName(repositoryPath)!;
        return Directory.Exists(parentPath)
            ? Directory.GetDirectories(parentPath, $"{Path.GetFileName(repositoryPath)}.tmp-*")
            : [];
    }

    public void Dispose()
    {
        foreach (var directory in _tempDirectories)
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
