using NPVideoStudio.App.ViewModels;
using NPVideoStudio.Media;
using Serilog;
using Xunit;

namespace NPVideoStudio.UnitTests;

/// <summary>
/// Drives SongHighlightsViewModel's real commands end to end (pick -> analyze -> export) against a real
/// audio fixture and real ffmpeg, without a Window/Application - ViewModels are plain MVVM objects, so
/// no Avalonia dispatcher is needed as long as nothing binds their ObservableCollection to a live UI
/// element. This upgrades highlights.analyze/highlights.export-all from service-only to real
/// command-chain coverage (FUNCTION_MATRIX.md).
/// </summary>
public class SongHighlightsViewModelTests : IDisposable
{
    private readonly string _songPath = Path.Combine(AppContext.BaseDirectory, "TestAssets", "lyric_test_song.mp3");
    private readonly string _exportDir = Path.Combine(Path.GetTempPath(), $"npvs_highlights_vm_test_{Guid.NewGuid():N}");
    private readonly FakeStorageService _storageService = new();
    private readonly SongHighlightsViewModel _viewModel;

    public SongHighlightsViewModelTests()
    {
        var highlightService = new SongHighlightService(new FfprobeService());
        _viewModel = new SongHighlightsViewModel(highlightService, _storageService, new LoggerConfiguration().CreateLogger());
    }

    public void Dispose()
    {
        if (Directory.Exists(_exportDir))
        {
            Directory.Delete(_exportDir, recursive: true);
        }
    }

    [Fact]
    public async Task PickSongCommand_UsesStorageServiceResult_SetsSelectedFile()
    {
        _storageService.FilesToReturn = new[] { _songPath };

        await _viewModel.PickSongCommand.ExecuteAsync(null);

        Assert.Equal(_songPath, _viewModel.SelectedFilePath);
        Assert.Equal("lyric_test_song.mp3", _viewModel.SelectedFileName);
        Assert.True(_viewModel.HasSelectedFile);
    }

    [Fact]
    public async Task AnalyzeCommand_RealFfmpegAnalysis_ProducesRealHighlights()
    {
        _storageService.FilesToReturn = new[] { _songPath };
        await _viewModel.PickSongCommand.ExecuteAsync(null);

        // ViewModel enforces a 10s floor (below which it rejects the request outright); the fixture is
        // only 8.46s, so this exercises SongHighlightService's "track shorter than one window" branch -
        // still a real end-to-end run of the command chain, just not the windowed-selection algorithm
        // (already covered directly by SongHighlightServiceTests.cs).
        _viewModel.MinDurationSeconds = 10;
        _viewModel.MaxDurationSeconds = 15;
        _viewModel.ClipCount = 2;

        await _viewModel.AnalyzeCommand.ExecuteAsync(null);

        Assert.True(_viewModel.HasHighlights);
        Assert.NotEmpty(_viewModel.Highlights);
        Assert.All(_viewModel.Highlights, h => Assert.False(string.IsNullOrEmpty(h.LoudnessLabel)));
    }

    [Fact]
    public async Task ExportAllCommand_RealFfmpegExport_WritesRealAudioFiles()
    {
        _storageService.FilesToReturn = new[] { _songPath };
        await _viewModel.PickSongCommand.ExecuteAsync(null);
        // ViewModel enforces a 10s floor (below which it rejects the request outright); the fixture is
        // only 8.46s, so this exercises SongHighlightService's "track shorter than one window" branch -
        // still a real end-to-end run of the command chain, just not the windowed-selection algorithm
        // (already covered directly by SongHighlightServiceTests.cs).
        _viewModel.MinDurationSeconds = 10;
        _viewModel.MaxDurationSeconds = 15;
        _viewModel.ClipCount = 2;
        await _viewModel.AnalyzeCommand.ExecuteAsync(null);
        Assert.NotEmpty(_viewModel.Highlights);

        Directory.CreateDirectory(_exportDir);
        _storageService.FolderToReturn = _exportDir;

        await _viewModel.ExportAllCommand.ExecuteAsync(null);

        var exportedFiles = Directory.GetFiles(_exportDir);
        Assert.Equal(_viewModel.Highlights.Count, exportedFiles.Length);
        Assert.All(exportedFiles, f => Assert.True(new FileInfo(f).Length > 0));
        Assert.All(_viewModel.Highlights, h => Assert.True(h.IsExported));
    }
}
