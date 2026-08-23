using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NPVideoStudio.Core.Services;
using NPVideoStudio.Media;

namespace NPVideoStudio.App.ViewModels;

public sealed partial class WorkspaceViewModel
{
    // The packaged application always carries FFmpeg; FfmpegLocator also honours a valid app-local/PATH
    // installation. Scene analysis is deliberately isolated from rendering, so a failed analysis never
    // mutates the timeline.
    private readonly ISceneDetectionService _sceneDetectionService = new SceneDetectionService();
    private CancellationTokenSource? _sceneDetectionCts;

    [ObservableProperty]
    private bool _isDetectingScenes;

    [ObservableProperty]
    private double _sceneDetectionThresholdPercent = 10.0;

    [ObservableProperty]
    private double _sceneMinimumSpacingSeconds = 0.35;

    [ObservableProperty]
    private string? _sceneDetectionStatusMessage;

    [RelayCommand]
    private async Task AutoCutSelectedClipAsync()
    {
        if (IsDetectingScenes)
        {
            return;
        }

        var selected = Timeline.SelectedClip?.Clip;
        if (selected is null || selected.MediaAssetId is null)
        {
            SceneDetectionStatusMessage = "Izaberite video klip na timeline-u pre Scene Auto Cut analize.";
            return;
        }
        if (selected.IsFreezeFrame || selected.IsReversed)
        {
            SceneDetectionStatusMessage = "Scene Auto Cut trenutno zahteva normalan smer videa bez Freeze Frame-a. Isključite Reverse/Freeze, napravite rezove, pa ih po potrebi ponovo uključite.";
            return;
        }

        var asset = Project.MediaLibrary.FirstOrDefault(a => a.Id == selected.MediaAssetId);
        if (asset is null || !asset.HasVideoStream || string.IsNullOrWhiteSpace(asset.FilePath) || !File.Exists(asset.FilePath))
        {
            SceneDetectionStatusMessage = "Originalni video za izabrani klip nije dostupan.";
            return;
        }

        _sceneDetectionCts?.Cancel();
        _sceneDetectionCts?.Dispose();
        _sceneDetectionCts = new CancellationTokenSource();
        var token = _sceneDetectionCts.Token;
        IsDetectingScenes = true;
        SceneDetectionStatusMessage = $"Analiziram scene u „{asset.FileName}“…";

        try
        {
            var scenes = await _sceneDetectionService.DetectAsync(
                asset.FilePath,
                selected.SourceTrimInSeconds,
                selected.SourceTrimOutSeconds,
                Math.Clamp(SceneDetectionThresholdPercent, 0.1, 100),
                Math.Clamp(SceneMinimumSpacingSeconds, 0.05, 10),
                token);

            token.ThrowIfCancellationRequested();
            if (scenes.Count == 0)
            {
                SceneDetectionStatusMessage = "Nijedna dovoljno jaka promena scene nije pronađena u izabranom klipu.";
                return;
            }

            var applied = Timeline.AutoCutSelectedAtSourceTimes(scenes.Select(s => s.SourceTimeSeconds));
            if (applied == 0)
            {
                SceneDetectionStatusMessage = "Scene su pronađene, ali nijedna nije bila dovoljno daleko od ivica klipa za bezbedan rez.";
                return;
            }

            Timeline.SaveToProject();
            if (!string.IsNullOrWhiteSpace(Project.ProjectFilePath))
            {
                await _projectRepository.SaveAsync(Project, Project.ProjectFilePath, token);
            }
            RefreshPreviewFrame(Player.CurrentTimeSeconds);
            SceneDetectionStatusMessage = $"Scene Auto Cut: napravljeno {applied} rezova. Svaki rez je normalan timeline edit i može da se vrati Undo komandom.";
        }
        catch (OperationCanceledException)
        {
            SceneDetectionStatusMessage = "Scene Detection je prekinut; timeline nije dodatno menjan.";
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Scene Detection / Auto Cut nije uspeo za {Path}", asset.FilePath);
            SceneDetectionStatusMessage = $"Scene Detection nije uspeo: {ex.Message}";
        }
        finally
        {
            IsDetectingScenes = false;
            _sceneDetectionCts?.Dispose();
            _sceneDetectionCts = null;
        }
    }

    [RelayCommand]
    private void CancelSceneDetection()
    {
        _sceneDetectionCts?.Cancel();
    }

    /// <summary>MainWindow disposes pages through IDisposable. Ensure a running FFmpeg analysis cannot
    /// outlive the workspace, then delegate to the existing public Dispose implementation for frame,
    /// caption, player and real-preview cleanup.</summary>
    void IDisposable.Dispose()
    {
        _sceneDetectionCts?.Cancel();
        _sceneDetectionCts?.Dispose();
        _sceneDetectionCts = null;
        Dispose();
    }
}
