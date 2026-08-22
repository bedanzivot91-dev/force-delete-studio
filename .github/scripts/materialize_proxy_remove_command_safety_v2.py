from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]

def read(rel): return (ROOT / rel).read_text(encoding='utf-8')
def write(rel, text): (ROOT / rel).write_text(text, encoding='utf-8')
def rep(text, old, new, label):
    n = text.count(old)
    if n != 1: raise RuntimeError(f'{label}: expected one anchor, found {n}')
    return text.replace(old, new, 1)

# Domain-owned safe proxy removal API.
p = 'src/NPVideoStudio.Domain/MediaAssetCollection.cs'
s = read(p)
old = '''public static class ProxyCacheCleanup
{
    public static bool TryDeleteOwnedProxy(MediaAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        if (string.IsNullOrWhiteSpace(asset.ProxyFilePath))
        {
            return false;
        }

        try
        {
            var proxyPath = Path.GetFullPath(asset.ProxyFilePath);
            var root = Path.GetFullPath(AppSettings.ProxyCacheFolder());
            var relative = Path.GetRelativePath(root, proxyPath);
            var outsideRoot = Path.IsPathRooted(relative) ||
                              relative.Equals("..", StringComparison.Ordinal) ||
                              relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
                              relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal);
            if (outsideRoot)
            {
                return false;
            }

            if (File.Exists(proxyPath))
            {
                File.Delete(proxyPath);
            }

            asset.ProxyFilePath = null;
            asset.ProxyStatus = MediaProxyStatus.Original;
            asset.ProxyError = null;
            return true;
        }
        catch
        {
            // Cache cleanup must never delete the original asset or make project removal crash. If a
            // disposable proxy is temporarily locked, normal cache maintenance can remove it later.
            return false;
        }
    }
}'''
new = '''public enum ProxyRemovalResult
{
    NoProxy,
    DeletedOwnedProxy,
    DetachedExternalReference,
    FailedToDeleteOwnedProxy
}

public static class ProxyCacheCleanup
{
    public static bool IsOwnedProxyPath(string? proxyFilePath)
    {
        if (string.IsNullOrWhiteSpace(proxyFilePath)) return false;
        try
        {
            var proxyPath = Path.GetFullPath(proxyFilePath);
            var root = Path.GetFullPath(AppSettings.ProxyCacheFolder());
            var relative = Path.GetRelativePath(root, proxyPath);
            return !Path.IsPathRooted(relative) &&
                   !relative.Equals("..", StringComparison.Ordinal) &&
                   !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) &&
                   !relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    public static bool TryDeleteOwnedProxy(MediaAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        if (!IsOwnedProxyPath(asset.ProxyFilePath)) return false;

        try
        {
            var proxyPath = Path.GetFullPath(asset.ProxyFilePath!);
            if (File.Exists(proxyPath)) File.Delete(proxyPath);
            ResetProxyMetadata(asset);
            return true;
        }
        catch
        {
            // A locked/unavailable app-owned cache file is not silently detached. Keeping its persisted
            // path lets the user retry later and prevents an orphaned file we can no longer identify.
            return false;
        }
    }

    /// <summary>Safe semantics for the explicit UI "Remove proxy" action. App-owned cache files are
    /// deleted. A persisted path outside NP's active proxy root is NEVER deleted; it is only detached
    /// from the project. A failure deleting an app-owned file leaves the metadata intact for retry.</summary>
    public static ProxyRemovalResult RemoveProxyReferenceSafely(MediaAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        if (string.IsNullOrWhiteSpace(asset.ProxyFilePath))
        {
            ResetProxyMetadata(asset);
            return ProxyRemovalResult.NoProxy;
        }

        if (!IsOwnedProxyPath(asset.ProxyFilePath))
        {
            ResetProxyMetadata(asset);
            return ProxyRemovalResult.DetachedExternalReference;
        }

        return TryDeleteOwnedProxy(asset)
            ? ProxyRemovalResult.DeletedOwnedProxy
            : ProxyRemovalResult.FailedToDeleteOwnedProxy;
    }

    private static void ResetProxyMetadata(MediaAsset asset)
    {
        asset.ProxyFilePath = null;
        asset.ProxyStatus = MediaProxyStatus.Original;
        asset.ProxyError = null;
    }
}'''
s = rep(s, old, new, 'proxy cleanup class')
write(p, s)

# Workspace command must use the safe domain API, never arbitrary File.Delete.
p = 'src/NPVideoStudio.App/ViewModels/WorkspaceViewModel.cs'
s = read(p)
old = '''        item.RemoveProxyCommand = new AsyncRelayCommand(async () =>
        {
            if (!string.IsNullOrWhiteSpace(asset.ProxyFilePath) && File.Exists(asset.ProxyFilePath))
            {
                File.Delete(asset.ProxyFilePath);
            }
            asset.ProxyFilePath = null;
            asset.ProxyStatus = MediaProxyStatus.Original;
            asset.ProxyError = null;
            item.NotifyAssetChanged();
            await PersistProxyStateAsync();
            RefreshPreviewFrame(Player.CurrentTimeSeconds);
            StatusMessage = $"Proxy za „{asset.FileName}“ je uklonjen. Preview koristi original.";
        });'''
new = '''        item.RemoveProxyCommand = new AsyncRelayCommand(async () =>
        {
            var result = ProxyCacheCleanup.RemoveProxyReferenceSafely(asset);
            item.NotifyAssetChanged();

            if (result == ProxyRemovalResult.FailedToDeleteOwnedProxy)
            {
                StatusMessage = $"Proxy za „{asset.FileName}“ nije uklonjen jer je fajl trenutno zaključan ili nedostupan. Zatvorite program koji ga koristi i pokušajte ponovo.";
                return;
            }

            await PersistProxyStateAsync();
            RefreshPreviewFrame(Player.CurrentTimeSeconds);
            StatusMessage = result == ProxyRemovalResult.DetachedExternalReference
                ? $"Proxy veza za „{asset.FileName}“ je uklonjena. Spoljni fajl nije obrisan jer nije u NP Video Studio proxy folderu."
                : $"Proxy za „{asset.FileName}“ je uklonjen. Preview koristi original.";
        });'''
s = rep(s, old, new, 'workspace RemoveProxyCommand')
write(p, s)

# Tests: direct runtime ownership semantics + structural command guard.
p = 'tests/NPVideoStudio.UnitTests/CacheFolderProxyConsistencyTests.cs'
s = read(p)
anchor = '''    [Fact]
    public void MediaRemoval_UsesSameCustomProxyRootButStillProtectsExternalFiles()'''
extra = '''    [Fact]
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

''' + anchor
s = rep(s, anchor, extra, 'insert proxy command tests')
# Add helper before final class brace.
old_end = '''    }
}'''
helper = '''    }

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
}'''
# Replace last occurrence only.
pos = s.rfind(old_end)
if pos < 0: raise RuntimeError('test class end not found')
s = s[:pos] + helper + s[pos+len(old_end):]
write(p, s)

for rel in ['.github/scripts/materialize_proxy_remove_command_safety_v2.py', '.github/workflows/materialize-proxy-remove-command-safety-v2.yml']:
    q = ROOT / rel
    if q.exists(): q.unlink()
print('proxy remove command safety v2 materialized')
