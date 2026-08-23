using NPVideoStudio.Domain;
using NPVideoStudio.Infrastructure.Persistence;
using Xunit;

namespace NPVideoStudio.UnitTests;

[CollectionDefinition("RuntimeCachePath", DisableParallelization = true)]
public sealed class RuntimeCachePathCollectionDefinition
{
}

[Collection("RuntimeCachePath")]
public sealed class CacheFolderProxyConsistencyTests
{
    [Fact]
    public async Task SaveSettings_ImmediatelyMovesActiveProxyRootToCustomCache()
    {
        var root = Path.Combine(Path.GetTempPath(), $"npvs-cache-setting-{Guid.NewGuid():N}");
        var settingsPath = Path.Combine(root, "settings.json");
        var customCache = Path.Combine(root, "My Custom Cache");

        try
        {
            var service = new SettingsService(settingsPath);
            service.Current.CacheFolder = customCache;
            await service.SaveAsync();

            Assert.Equal(Path.GetFullPath(customCache), AppSettings.ActiveCacheFolder());
            Assert.Equal(Path.Combine(Path.GetFullPath(customCache), "Proxies"), AppSettings.ProxyCacheFolder());
        }
        finally
        {
            AppSettings.ConfigureRuntimeCacheFolder(AppSettings.DefaultCacheFolder());
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task LoadSettings_RestoresPersistedCustomProxyRootAfterRestart()
    {
        var root = Path.Combine(Path.GetTempPath(), $"npvs-cache-load-{Guid.NewGuid():N}");
        var settingsPath = Path.Combine(root, "settings.json");
        var customCache = Path.Combine(root, "Persisted Cache");

        try
        {
            var writer = new SettingsService(settingsPath);
            writer.Current.CacheFolder = customCache;
            await writer.SaveAsync();

            AppSettings.ConfigureRuntimeCacheFolder(AppSettings.DefaultCacheFolder());
            var reader = new SettingsService(settingsPath);
            await reader.LoadAsync();

            Assert.Equal(Path.GetFullPath(customCache), AppSettings.ActiveCacheFolder());
            Assert.Equal(Path.Combine(Path.GetFullPath(customCache), "Proxies"), AppSettings.ProxyCacheFolder());
        }
        finally
        {
            AppSettings.ConfigureRuntimeCacheFolder(AppSettings.DefaultCacheFolder());
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task InvalidManuallyTypedCachePath_DoesNotCrashSettingsSaveAndFallsBackSafely()
    {
        var root = Path.Combine(Path.GetTempPath(), $"npvs-cache-invalid-{Guid.NewGuid():N}");
        var settingsPath = Path.Combine(root, "settings.json");

        try
        {
            var service = new SettingsService(settingsPath);
            service.Current.CacheFolder = "bad\0cache";

            await service.SaveAsync();

            Assert.Equal(Path.GetFullPath(AppSettings.DefaultCacheFolder()), AppSettings.ActiveCacheFolder());
            Assert.Equal(Path.Combine(Path.GetFullPath(AppSettings.DefaultCacheFolder()), "Proxies"), AppSettings.ProxyCacheFolder());
            Assert.True(File.Exists(settingsPath));
        }
        finally
        {
            AppSettings.ConfigureRuntimeCacheFolder(AppSettings.DefaultCacheFolder());
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ExplicitRemoveProxy_DeletesOwnedFileButOnlyDetachesExternalReference()
    {
        var root = Path.Combine(Path.GetTempPath(), $"npvs-explicit-proxy-{Guid.NewGuid():N}");
        var customCache = Path.Combine(root, "Cache");
        var proxyRoot = Path.Combine(customCache, "Proxies");
        Directory.CreateDirectory(proxyRoot);
        var ownedPath = Path.Combine(proxyRoot, "owned.proxy.mp4");
        var externalPath = Path.Combine(root, "external-important.mp4");
        File.WriteAllBytes(ownedPath, new byte[] { 1, 2, 3 });
        File.WriteAllBytes(externalPath, new byte[] { 9, 8, 7 });

        try
        {
            AppSettings.ConfigureRuntimeCacheFolder(customCache);
            var owned = new MediaAsset { FilePath = "original.mp4", ProxyFilePath = ownedPath, ProxyStatus = MediaProxyStatus.Ready };
            Assert.Equal(ProxyRemovalResult.DeletedOwnedProxy, ProxyCacheCleanup.RemoveProxyReferenceSafely(owned));
            Assert.False(File.Exists(ownedPath));
            Assert.Null(owned.ProxyFilePath);
            Assert.Equal(MediaProxyStatus.Original, owned.ProxyStatus);

            var external = new MediaAsset { FilePath = "original2.mp4", ProxyFilePath = externalPath, ProxyStatus = MediaProxyStatus.Ready };
            Assert.Equal(ProxyRemovalResult.DetachedExternalReference, ProxyCacheCleanup.RemoveProxyReferenceSafely(external));
            Assert.True(File.Exists(externalPath));
            Assert.Null(external.ProxyFilePath);
            Assert.Equal(MediaProxyStatus.Original, external.ProxyStatus);
        }
        finally
        {
            AppSettings.ConfigureRuntimeCacheFolder(AppSettings.DefaultCacheFolder());
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void WorkspaceRemoveProxyCommand_DoesNotDirectlyDeletePersistedArbitraryPath()
    {
        var root = FindRepoRoot();
        var source = File.ReadAllText(Path.Combine(root, "src", "NPVideoStudio.App", "ViewModels", "WorkspaceViewModel.cs"));
        Assert.DoesNotContain("File.Delete(asset.ProxyFilePath)", source);
        Assert.Contains("ProxyCacheCleanup.RemoveProxyReferenceSafely(asset)", source);
    }

    [Fact]
    public void MediaRemoval_UsesSameCustomProxyRootButStillProtectsExternalFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), $"npvs-cache-cleanup-{Guid.NewGuid():N}");
        var customCache = Path.Combine(root, "Cache");
        var proxyRoot = Path.Combine(customCache, "Proxies");
        var external = Path.Combine(root, "must-not-delete.mp4");
        Directory.CreateDirectory(proxyRoot);
        var ownedProxy = Path.Combine(proxyRoot, "owned.proxy.mp4");
        File.WriteAllBytes(ownedProxy, new byte[] { 1, 2, 3 });
        File.WriteAllBytes(external, new byte[] { 9 });

        try
        {
            AppSettings.ConfigureRuntimeCacheFolder(customCache);
            var owned = new MediaAsset
            {
                FilePath = "original.mp4",
                ProxyStatus = MediaProxyStatus.Ready,
                ProxyFilePath = ownedProxy
            };
            var project = new Project { Name = "Custom cache cleanup" };
            project.MediaLibrary.Add(owned);

            Assert.True(project.MediaLibrary.Remove(owned));
            Assert.False(File.Exists(ownedProxy));
            Assert.Null(owned.ProxyFilePath);
            Assert.Equal(MediaProxyStatus.Original, owned.ProxyStatus);

            var outside = new MediaAsset
            {
                FilePath = "original-2.mp4",
                ProxyStatus = MediaProxyStatus.Ready,
                ProxyFilePath = external
            };
            project.MediaLibrary.Add(outside);
            Assert.True(project.MediaLibrary.Remove(outside));
            Assert.True(File.Exists(external));
            Assert.Equal(external, outside.ProxyFilePath);
            Assert.Equal(MediaProxyStatus.Ready, outside.ProxyStatus);
        }
        finally
        {
            AppSettings.ConfigureRuntimeCacheFolder(AppSettings.DefaultCacheFolder());
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "NPVideoStudio.sln"))) return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("NPVideoStudio.sln nije pronađen iz test output foldera.");
    }
}
