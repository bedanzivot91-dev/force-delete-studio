using System.Windows.Input;
using NPVideoStudio.Domain;

namespace NPVideoStudio.App.ViewModels;

public sealed class MediaAssetViewModel : ViewModelBase
{
    public MediaAsset Asset { get; }

    public ICommand? ToggleFavoriteCommand { get; set; }
    public ICommand? RemoveCommand { get; set; }

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

    /// <summary>Call after mutating <see cref="Asset"/> directly (e.g. toggling favorite) to refresh bindings.</summary>
    public void NotifyAssetChanged() => OnPropertyChanged(nameof(IsFavorite));
}
