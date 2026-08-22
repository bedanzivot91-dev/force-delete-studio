using System.Collections.ObjectModel;

namespace NPVideoStudio.Domain;

/// <summary>
/// Project media collection with one ownership rule: disposable proxy files created inside NP Video
/// Studio's proxy cache are removed when their media asset actually leaves the project. Original media
/// and arbitrary external files are never deleted here.
/// </summary>
public sealed class MediaAssetCollection : Collection<MediaAsset>
{
    public MediaAssetCollection()
    {
    }

    public MediaAssetCollection(IList<MediaAsset> items) : base(items)
    {
    }

    /// <summary>
    /// Keeps existing preview/project-construction code source-compatible when it materializes media with
    /// LINQ ToList(). The resulting project still receives the cleanup-aware collection.
    /// </summary>
    public static implicit operator MediaAssetCollection(List<MediaAsset> items) => new(items);

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

public enum ProxyRemovalResult
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
}
