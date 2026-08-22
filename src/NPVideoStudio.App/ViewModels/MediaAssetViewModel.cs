using System.Windows.Input;
using NPVideoStudio.Domain;

namespace NPVideoStudio.App.ViewModels;

public sealed class MediaAssetViewModel : ViewModelBase
{
    public MediaAsset Asset { get; }

    public ICommand? ToggleFavoriteCommand { get; set; }
    public ICommand? RemoveCommand { get; set; }
    public ICommand? GenerateProxyCommand { get; set; }
    public ICommand? RemoveProxyCommand { get; set; }
    public ICommand? OpenProxyFolderCommand { get; set; }

    public MediaAssetViewModel(MediaAsset asset)
    {
        Asset = asset;
    }

    public string FileName => Asset.FileName;
    public string KindLabel => Asset.Kind switch
    {
        MediaKind.Video => "Video",
        MediaKind.Audio => "Audio",
        MediaKind.Image => "Slika",
        _ => "Nepoznato"
    };

    public string DurationLabel => Asset.Duration > TimeSpan.Zero ? Asset.Duration.ToString(@"hh\:mm\:ss") : "-";
    public string ResolutionLabel => Asset.Width > 0 && Asset.Height > 0 ? $"{Asset.Width}x{Asset.Height}" : "-";
    public string FpsLabel => Asset.Fps > 0 ? $"{Asset.Fps:0.##} fps" : "-";
    public string CodecLabel => string.Join(" / ", new[] { Asset.VideoCodec, Asset.AudioCodec }.Where(c => !string.IsNullOrEmpty(c)));
    public string SizeLabel => Asset.FileSizeBytes > 0 ? $"{Asset.FileSizeBytes / 1024.0 / 1024.0:F1} MB" : "-";
    public bool HasError => !string.IsNullOrEmpty(Asset.ProbeError);
    public string? ErrorMessage => Asset.ProbeError;
    public bool IsFavorite => Asset.IsFavorite;

    public bool CanGenerateProxy => Asset.HasVideoStream && Asset.ProxyStatus is not MediaProxyStatus.Generating;
    public bool HasReadyProxy => Asset.ProxyStatus == MediaProxyStatus.Ready &&
                                 !string.IsNullOrWhiteSpace(Asset.ProxyFilePath) &&
                                 File.Exists(Asset.ProxyFilePath);
    public bool IsProxyGenerating => Asset.ProxyStatus == MediaProxyStatus.Generating;
    public string ProxyStatusLabel => Asset.ProxyStatus switch
    {
        MediaProxyStatus.Generating => "Proxy: generisanje...",
        MediaProxyStatus.Ready when HasReadyProxy => "Proxy: spreman (preview)",
        MediaProxyStatus.Ready => "Proxy: fajl nedostaje",
        MediaProxyStatus.Failed => $"Proxy: greška{(string.IsNullOrWhiteSpace(Asset.ProxyError) ? string.Empty : $" — {Asset.ProxyError}")}",
        _ => "Proxy: original"
    };

    /// <summary>Call after mutating <see cref="Asset"/> directly to refresh media-library bindings.</summary>
    public void NotifyAssetChanged()
    {
        OnPropertyChanged(nameof(IsFavorite));
        OnPropertyChanged(nameof(CanGenerateProxy));
        OnPropertyChanged(nameof(HasReadyProxy));
        OnPropertyChanged(nameof(IsProxyGenerating));
        OnPropertyChanged(nameof(ProxyStatusLabel));
    }
}
