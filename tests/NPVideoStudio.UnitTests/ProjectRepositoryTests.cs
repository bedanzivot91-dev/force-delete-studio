using NPVideoStudio.Domain;
using NPVideoStudio.Infrastructure.Persistence;
using Xunit;

namespace NPVideoStudio.UnitTests;

public class ProjectRepositoryTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ProjectRepository _repository = new();

    public ProjectRepositoryTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"npvs_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    [Fact]
    public async Task SaveAndLoad_RoundTripsBasicProject()
    {
        var project = new Project
        {
            Name = "Test projekat",
            Format = ProjectFormat.FromPresets(AspectRatioPreset.Widescreen16x9, ResolutionPreset.FullHd1080, FrameRatePreset.Fps30)
        };
        var path = Path.Combine(_tempDir, "test.npvsproject");

        await _repository.SaveAsync(project, path);
        var loaded = await _repository.LoadAsync(path);

        Assert.Equal(project.Id, loaded.Id);
        Assert.Equal(project.Name, loaded.Name);
        Assert.Equal(1920, loaded.Format.Width);
        Assert.Equal(1080, loaded.Format.Height);
        Assert.Equal(path, loaded.ProjectFilePath);
    }

    [Fact]
    public async Task SaveAndLoad_PreservesSerbianLatinAndCyrillicCharacters()
    {
        var project = new Project { Name = "Пројекат са ђ, č, ć, š, ž, đ и Ђ" };
        var path = Path.Combine(_tempDir, "srpski.npvsproject");

        await _repository.SaveAsync(project, path);
        var loaded = await _repository.LoadAsync(path);

        Assert.Equal(project.Name, loaded.Name);
    }

    [Fact]
    public async Task SaveAndLoad_RoundTripsTimelineTracksAndClips()
    {
        var project = new Project { Name = "Sa timeline-om" };
        var clip = new TimelineClip
        {
            MediaAssetId = "abc123",
            SourceTrimInSeconds = 1.5,
            SourceTrimOutSeconds = 6.5,
            TimelineStartSeconds = 2,
            FadeInSeconds = 0.5,
            IsMuted = true,
            Volume = 0.8
        };
        project.Timeline.Tracks.Add(new TimelineTrack { Kind = TimelineTrackKind.Video, Name = "Glavni video", Clips = { clip } });
        project.Timeline.PlayheadSeconds = 3.25;
        var path = Path.Combine(_tempDir, "timeline.npvsproject");

        await _repository.SaveAsync(project, path);
        var loaded = await _repository.LoadAsync(path);

        var loadedTrack = Assert.Single(loaded.Timeline.Tracks);
        Assert.Equal(TimelineTrackKind.Video, loadedTrack.Kind);
        Assert.Equal("Glavni video", loadedTrack.Name);
        var loadedClip = Assert.Single(loadedTrack.Clips);
        Assert.Equal("abc123", loadedClip.MediaAssetId);
        Assert.Equal(1.5, loadedClip.SourceTrimInSeconds);
        Assert.Equal(6.5, loadedClip.SourceTrimOutSeconds);
        Assert.Equal(2, loadedClip.TimelineStartSeconds);
        Assert.True(loadedClip.IsMuted);
        Assert.Equal(0.8, loadedClip.Volume);
        Assert.Equal(3.25, loaded.Timeline.PlayheadSeconds);
    }

    [Fact]
    public async Task Load_ProjectFileFromBeforeTimelineExisted_LoadsWithEmptyTimeline()
    {
        // A real Phase-1-era .npvsproject file has no "timeline" property at all - simulates that
        // exact shape rather than assuming System.Text.Json's default-missing-property behavior.
        var path = Path.Combine(_tempDir, "pre-timeline.npvsproject");
        var project = new Project { Name = "Stari projekat" };
        var json = System.Text.Json.JsonSerializer.Serialize(new
        {
            project.ProjectFormatVersion,
            project.Id,
            project.Name,
            project.Format,
            project.TargetPlatform,
            project.CreatedAt,
            project.LastModifiedAt,
            project.MediaLibrary
        });
        await File.WriteAllTextAsync(path, json);

        var loaded = await _repository.LoadAsync(path);

        Assert.Equal("Stari projekat", loaded.Name);
        Assert.NotNull(loaded.Timeline);
        Assert.Empty(loaded.Timeline.Tracks);
    }

    [Fact]
    public async Task SaveAndLoad_WorksWithSpacesInPath()
    {
        var dirWithSpaces = Path.Combine(_tempDir, "folder with spaces");
        Directory.CreateDirectory(dirWithSpaces);
        var project = new Project { Name = "Projekat" };
        var path = Path.Combine(dirWithSpaces, "moj projekat.npvsproject");

        await _repository.SaveAsync(project, path);
        var loaded = await _repository.LoadAsync(path);

        Assert.Equal(project.Name, loaded.Name);
    }

    [Fact]
    public async Task Save_IsAtomic_OriginalSurvivesIfOverwrittenAgain()
    {
        var project = new Project { Name = "Prvo" };
        var path = Path.Combine(_tempDir, "atomic.npvsproject");
        await _repository.SaveAsync(project, path);

        project.Name = "Drugo";
        await _repository.SaveAsync(project, path);

        Assert.False(File.Exists(path + ".tmp"));
        var loaded = await _repository.LoadAsync(path);
        Assert.Equal("Drugo", loaded.Name);
    }

    [Fact]
    public async Task Load_MissingFile_ThrowsFileNotFoundException()
    {
        var path = Path.Combine(_tempDir, "does-not-exist.npvsproject");
        await Assert.ThrowsAsync<FileNotFoundException>(() => _repository.LoadAsync(path));
    }

    [Fact]
    public async Task Load_CorruptedJson_ThrowsInvalidDataOrJsonException()
    {
        var path = Path.Combine(_tempDir, "corrupt.npvsproject");
        await File.WriteAllTextAsync(path, "{ ovo nije ispravan json");

        await Assert.ThrowsAnyAsync<Exception>(() => _repository.LoadAsync(path));
    }

    [Fact]
    public async Task Backup_CreatesTimestampedCopyAndLimitsCount()
    {
        var project = new Project { Name = "Backup test" };
        var path = Path.Combine(_tempDir, "backup.npvsproject");
        await _repository.SaveAsync(project, path);

        for (var i = 0; i < 3; i++)
        {
            await _repository.BackupAsync(path, maxBackups: 2);
            await Task.Delay(1100); // ensure distinct second-resolution timestamps
        }

        var backupDir = Path.Combine(_tempDir, "Backups");
        var backups = Directory.GetFiles(backupDir, "backup_*.npvsproject");
        Assert.True(backups.Length <= 2);
    }
}
