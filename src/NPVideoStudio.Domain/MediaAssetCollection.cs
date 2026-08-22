using System.Collections.ObjectModel;

namespace NPVideoStudio.Domain;

/// <summary>
/// Project media collection with one ownership rule: disposable proxy files created inside NP Video
/// Studio's proxy cache are removed when their media asset actually leaves the project. Original media
/// and arbitrary external files are never deleted here.
/// </summary>
public sealed class MediaAssetCollection : Collection<MediaAsset>
{
    protected override void RemoveItem(int index)
    {
        var asset = this[index];
        base.RemoveItem(index);
        ProxyCacheCleanup.TryDeleteOwnedProxy(asset);
    }

    protected override void SetItem(int index, MediaAsset item)
    {
        var replaced = this[index];
        base.SetItem(index, item);
        if (!ReferenceEquals(replaced, item))
        {
            ProxyCacheCleanup.TryDeleteOwnedProxy(replaced);
        }
    }

    protected override void ClearItems()
    {
        var removed = this.ToArray();
        base.ClearItems();
        foreach (var asset in removed)
        {
            ProxyCacheCleanup.TryDeleteOwnedProxy(asset);
        }
    }
}

public static class ProxyCacheCleanup
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
}
