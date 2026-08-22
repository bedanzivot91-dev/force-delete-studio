using Avalonia.Headless.XUnit;
using CommunityToolkit.Mvvm.Input;
using NPVideoStudio.App.ViewModels;
using NPVideoStudio.Core.Services;
using NPVideoStudio.Domain;
using NPVideoStudio.Infrastructure.Persistence;
using NPVideoStudio.Media;
using Serilog;
using Xunit;

namespace NPVideoStudio.UnitTests;

public sealed class FakeProxyGeneratorService : IProxyGeneratorService
{
    public int CallCount { get; private set; }
    public string? LastSource { get; private set; }
    public string? LastOutput { get; private set; }

    public Task<string> GenerateProxyAsync(
        string sourceFilePath,
        string outputFilePath,
        int targetHeight = 720,
        CancellationToken cancellationToken = default)
    {
        CallCount++;
        LastSource = sourceFilePath;
        LastOutput = outputFilePath;
        Directory.CreateDirectory(Path.GetDirectoryName(outputFilePath)!);
        File.WriteAllBytes(outputFilePath, new byte[] { 1, 2, 3 });
        return Task.FromResult(outputFilePath);
    }
}

public class ProxyWorkspaceIntegrationTests
{
    [Fact]
    public void PreviewLibrary_UsesReadyProxyButOriginalProjectRemainsUntouchedForExport()
    {
        var temp = Path.Combine(Path.GetTempPath(), $"npvs-proxy-{Guid.NewGuid():N}.mp4");
        File.WriteAllBytes(temp, new byte[] { 1 });
        try
        {
            var original = Path.Combine(Path.GetTempPath(), "original-full-quality.mp4");
            var asset = new MediaAsset
            {
                Id = "asset",
                FilePath = original,
                HasVideoStream = true,
                HasAudioStream = true,
                Duration = TimeSpan.FromSeconds(2),
                ProxyStatus = MediaProxyStatus.Ready,
                ProxyFilePath = temp
            };

            var previewLibrary = WorkspaceViewModel.BuildPreviewMediaLibrary(new[] { asset });

            Assert.Equal(original, asset.FilePath);
            Assert.Equal(temp, Assert.Single(previewLibrary).FilePath);
        }
        finally
        {
            File.Delete(temp);
        }
    }

    [Fact]
    public void FinalRenderPlan_UsesOriginalWhilePreviewRenderPlanUsesProxy()
    {
        var proxy = Path.Combine(Path.GetTempPath(), $"npvs-proxy-{Guid.NewGuid():N}.mp4");
        File.WriteAllBytes(proxy, new byte[] { 1 });
        try
        {
            var asset = new MediaAsset
            {
                Id = "video",
                FilePath = "D:/source/original-4k.mp4",
                HasVideoStream = true,
                HasAudioStream = true,
                Duration = TimeSpan.FromSeconds(2),
                ProxyStatus = MediaProxyStatus.Ready,
                ProxyFilePath = proxy
            };
            var timeline = new Timeline();
            timeline.Tracks.Add(new TimelineTrack
            {
                Kind = TimelineTrackKind.Video,
                Clips =
                {
                    new TimelineClip
                    {
                        MediaAssetId = asset.Id,
                        SourceTrimOutSeconds = 2
                    }
                }
            });

            var finalPlan = FfmpegFilterGraphBuilder.Build(timeline, new[] { asset }, 640, 360, 30);
            var previewPlan = FfmpegFilterGraphBuilder.Build(
                timeline,
                WorkspaceViewModel.BuildPreviewMediaLibrary(new[] { asset }),
                640, 360, 30);

            Assert.Equal("D:/source/original-4k.mp4", Assert.Single(finalPlan.InputFilePaths));
            Assert.Equal(proxy, Assert.Single(previewPlan.InputFilePaths));
        }
        finally
        {
            File.Delete(proxy);
        }
    }

    [Fact]
    public async Task ProjectRepository_RoundTripsProxyMetadataWithoutReplacingOriginalPath()
    {
        var root = Path.Combine(Path.GetTempPath(), $"npvs-proxy-project-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var project = new Project { Name = "Proxy persistence" };
            project.MediaLibrary.Add(new MediaAsset
            {
                FilePath = "D:/media/original.mp4",
                ProxyStatus = MediaProxyStatus.Ready,
                ProxyFilePath = "C:/cache/video.proxy.mp4",
                ProxyError = null
            });
            var path = Path.Combine(root, "proxy.npvsproject");
            var repository = new ProjectRepository();

            await repository.SaveAsync(project, path);
            var loaded = await repository.LoadAsync(path);
            var asset = Assert.Single(loaded.MediaLibrary);

            Assert.Equal("D:/media/original.mp4", asset.FilePath);
            Assert.Equal(MediaProxyStatus.Ready, asset.ProxyStatus);
            Assert.Equal("C:/cache/video.proxy.mp4", asset.ProxyFilePath);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task MediaLibraryGenerateProxyCommand_UsesRealWorkspaceServiceAndUpdatesAssetState()
    {
        var source = Path.Combine(Path.GetTempPath(), $"npvs-proxy-source-{Guid.NewGuid():N}.mp4");
        File.WriteAllBytes(source, new byte[] { 1, 2, 3 });
        var proxy = new FakeProxyGeneratorService();
        var asset = new MediaAsset
        {
            FilePath = source,
            Kind = MediaKind.Video,
            HasVideoStream = true,
            HasAudioStream = true,
            Duration = TimeSpan.FromSeconds(2)
        };
        var project = new Project { Name = "Proxy UI" };
        project.MediaLibrary.Add(asset);

        try
        {
            using var workspace = new WorkspaceViewModel(
                project,
                new FakeProjectRepository(),
                new FakeMediaProbeService(),
                new FakeStorageService(),
                new FakeFramePreviewService(),
                new FakeSubtitleGeneratorService(),
                new FakeRenderService(),
                new LoggerConfiguration().CreateLogger(),
                aiWorkerClient: null,
                proxyGeneratorService: proxy);

            var item = Assert.Single(workspace.MediaLibrary);
            var command = Assert.IsAssignableFrom<IAsyncRelayCommand>(item.GenerateProxyCommand);
            await command.ExecuteAsync(null);

            Assert.Equal(1, proxy.CallCount);
            Assert.Equal(source, proxy.LastSource);
            Assert.Equal(MediaProxyStatus.Ready, asset.ProxyStatus);
            Assert.NotNull(asset.ProxyFilePath);
            Assert.True(File.Exists(asset.ProxyFilePath));
            Assert.True(item.HasReadyProxy);
            Assert.Contains("export", workspace.StatusMessage, StringComparison.OrdinalIgnoreCase);

            var removeCommand = Assert.IsAssignableFrom<IAsyncRelayCommand>(item.RemoveProxyCommand);
            var generatedPath = asset.ProxyFilePath!;
            await removeCommand.ExecuteAsync(null);
            Assert.Equal(MediaProxyStatus.Original, asset.ProxyStatus);
            Assert.Null(asset.ProxyFilePath);
            Assert.False(File.Exists(generatedPath));
        }
        finally
        {
            File.Delete(source);
        }
    }
    [AvaloniaFact]
    public async Task RemovingUnusedMedia_DeletesOnlyOwnedProxyAndRemovesProjectEntry()
    {
        var source = Path.Combine(Path.GetTempPath(), $"npvs-proxy-remove-{Guid.NewGuid():N}.mp4");
        File.WriteAllBytes(source, new byte[] { 1 });
        var project = new Project { Name = "Proxy removal cleanup" };
        var asset = new MediaAsset { FilePath = source, Kind = MediaKind.Video, HasVideoStream = true, HasAudioStream = true, Duration = TimeSpan.FromSeconds(2) };
        project.MediaLibrary.Add(asset);
        var proxy = new FakeProxyGeneratorService();
        try
        {
            using var workspace = new WorkspaceViewModel(project, new FakeProjectRepository(), new FakeMediaProbeService(), new FakeStorageService(), new FakeFramePreviewService(), new FakeSubtitleGeneratorService(), new FakeRenderService(), new LoggerConfiguration().CreateLogger(), aiWorkerClient: null, proxyGeneratorService: proxy);
            var item = Assert.Single(workspace.MediaLibrary);
            await Assert.IsAssignableFrom<IAsyncRelayCommand>(item.GenerateProxyCommand).ExecuteAsync(null);
            var generated = asset.ProxyFilePath!;
            Assert.True(File.Exists(generated));

            await Assert.IsAssignableFrom<IAsyncRelayCommand>(item.RemoveCommand).ExecuteAsync(null);

            Assert.Empty(project.MediaLibrary);
            Assert.Empty(workspace.MediaLibrary);
            Assert.False(File.Exists(generated));
        }
        finally { File.Delete(source); }
    }

    [Fact]
    public void DeleteOwnedProxyFile_NeverDeletesArbitraryExternalPath()
    {
        var external = Path.Combine(Path.GetTempPath(), $"npvs-external-{Guid.NewGuid():N}.mp4");
        File.WriteAllBytes(external, new byte[] { 9 });
        try
        {
            var asset = new MediaAsset { FilePath = "original.mp4", ProxyStatus = MediaProxyStatus.Ready, ProxyFilePath = external };
            Assert.False(WorkspaceViewModel.DeleteOwnedProxyFile(asset));
            Assert.True(File.Exists(external));
            Assert.Equal(external, asset.ProxyFilePath);
        }
        finally { File.Delete(external); }
    }

}
