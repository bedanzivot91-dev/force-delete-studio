using System.Text.Json;
using NPVideoStudio.Domain;
using Xunit;

namespace NPVideoStudio.UnitTests;

public sealed class MediaAssetCollectionTests
{
    [Fact]
    public void Remove_DeletesOwnedProxyAndResetsProxyMetadata()
    {
        var folder = Path.Combine(AppSettings.ProxyCacheFolder(), $"remove-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        var proxy = Path.Combine(folder, "proxy.mp4");
        File.WriteAllBytes(proxy, new byte[] { 1, 2, 3 });
        var asset = new MediaAsset
        {
            FilePath = "original.mp4",
            ProxyStatus = MediaProxyStatus.Ready,
            ProxyFilePath = proxy,
            ProxyError = "old error"
        };
        var project = new Project { Name = "Proxy cleanup" };
        project.MediaLibrary.Add(asset);

        try
        {
            Assert.True(project.MediaLibrary.Remove(asset));

            Assert.False(File.Exists(proxy));
            Assert.Null(asset.ProxyFilePath);
            Assert.Equal(MediaProxyStatus.Original, asset.ProxyStatus);
            Assert.Null(asset.ProxyError);
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void Remove_NeverDeletesExternalFileOrClearsExternalMetadata()
    {
        var external = Path.Combine(Path.GetTempPath(), $"npvs-external-{Guid.NewGuid():N}.mp4");
        File.WriteAllBytes(external, new byte[] { 9 });
        var asset = new MediaAsset
        {
            FilePath = "original.mp4",
            ProxyStatus = MediaProxyStatus.Ready,
            ProxyFilePath = external
        };
        var project = new Project { Name = "External protection" };
        project.MediaLibrary.Add(asset);

        try
        {
            Assert.True(project.MediaLibrary.Remove(asset));

            Assert.True(File.Exists(external));
            Assert.Equal(external, asset.ProxyFilePath);
            Assert.Equal(MediaProxyStatus.Ready, asset.ProxyStatus);
        }
        finally
        {
            File.Delete(external);
        }
    }

    [Fact]
    public void MediaAssetCollection_RoundTripsThroughProjectJson()
    {
        var project = new Project { Name = "Serialization" };
        project.MediaLibrary.Add(new MediaAsset { FilePath = "clip.mp4", Kind = MediaKind.Video });

        var json = JsonSerializer.Serialize(project);
        var loaded = JsonSerializer.Deserialize<Project>(json);

        Assert.NotNull(loaded);
        Assert.IsType<MediaAssetCollection>(loaded!.MediaLibrary);
        Assert.Single(loaded.MediaLibrary);
        Assert.Equal("clip.mp4", loaded.MediaLibrary[0].FilePath);
    }
}
