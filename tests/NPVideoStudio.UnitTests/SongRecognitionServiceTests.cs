using System.Text.Json;
using NPVideoStudio.Domain;
using NPVideoStudio.Media;
using Xunit;

namespace NPVideoStudio.UnitTests;

/// <summary>
/// Real integration tests for SongRecognitionService: real ffmpeg extracts the five spec-mandated
/// windows (start/quarter/mid/three-quarter/end) from a real audio fixture, and a fake `fpcalc`
/// (tests/FakeFpcalc, content-hash-based but deterministic) stands in for the real Chromaprint tool -
/// same "mock process, not a fake in test code alone" pattern as YouTubeDownloadServiceTests. Matching
/// itself (FindMatches) is exercised against real fingerprints this service actually produced.
/// </summary>
public class SongRecognitionServiceTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"npvs_songrec_test_{Guid.NewGuid():N}");
    private readonly string _fixturePath = Path.Combine(AppContext.BaseDirectory, "TestAssets", "lyric_test_song.mp3");
    private readonly SongRecognitionService _service;

    public SongRecognitionServiceTests()
    {
        Directory.CreateDirectory(_tempDir);
        var exeName = OperatingSystem.IsWindows() ? "FakeFpcalc.exe" : "FakeFpcalc";
        var fakeFpcalcPath = Path.Combine(AppContext.BaseDirectory, exeName);
        Assert.True(File.Exists(fakeFpcalcPath), $"Fake fpcalc nije pronađen na {fakeFpcalcPath} - proveriti ProjectReference ka tests/FakeFpcalc.");

        _service = new SongRecognitionService(new FfprobeService(), fpcalcOverridePath: fakeFpcalcPath, windowSeconds: 5);
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    [Fact]
    public async Task ComputeFingerprintAsync_RealFixture_ProducesFiveNonEmptyWindows()
    {
        var result = await _service.ComputeFingerprintAsync(_fixturePath);

        Assert.Equal(5, result.Windows.Count);
        Assert.Equal(new[] { "start", "quarter", "mid", "three_quarter", "end" }, result.Windows.Select(w => w.Label));
        Assert.All(result.Windows, w => Assert.False(string.IsNullOrWhiteSpace(w.Raw)));
        Assert.InRange(result.DurationSeconds, 8.0, 9.0); // fixture is ~8.46s
    }

    [Fact]
    public async Task ComputeFingerprintAsync_SameFileTwice_IsDeterministic()
    {
        var first = await _service.ComputeFingerprintAsync(_fixturePath);
        var second = await _service.ComputeFingerprintAsync(_fixturePath);

        for (var i = 0; i < first.Windows.Count; i++)
        {
            Assert.Equal(first.Windows[i].Raw, second.Windows[i].Raw);
        }
    }

    [Fact]
    public async Task FindMatches_LibraryContainsSameFingerprint_ReturnsAutoAcceptEligibleMatch()
    {
        var fingerprint = await _service.ComputeFingerprintAsync(_fixturePath);
        var libraryEntry = new SongLibraryEntry
        {
            Title = "Postojeća pesma",
            OriginalAudioPath = _fixturePath,
            Duration = TimeSpan.FromSeconds(fingerprint.DurationSeconds),
            Fingerprint = JsonSerializer.Serialize(fingerprint)
        };

        var matches = _service.FindMatches(fingerprint, new[] { libraryEntry });

        var match = Assert.Single(matches);
        Assert.Equal(libraryEntry.Id, match.LibraryEntryId);
        Assert.Equal(5, match.AgreeingWindows);
        Assert.Equal(0, match.ConflictingWindows);
        Assert.True(match.Confidence > 0.95, $"Expected near-1.0 confidence for an identical fingerprint, got {match.Confidence}");
        Assert.True(match.AutoAcceptEligible);
    }

    [Fact]
    public async Task FindMatches_DifferentAudioContent_DoesNotAutoAccept()
    {
        var toneFilePath = Path.Combine(_tempDir, "tone.wav");
        await GenerateToneWavAsync(toneFilePath);

        var candidate = await _service.ComputeFingerprintAsync(_fixturePath);
        var differentFingerprint = await _service.ComputeFingerprintAsync(toneFilePath);
        var libraryEntry = new SongLibraryEntry
        {
            Title = "Sasvim druga pesma",
            OriginalAudioPath = toneFilePath,
            Duration = TimeSpan.FromSeconds(differentFingerprint.DurationSeconds),
            Fingerprint = JsonSerializer.Serialize(differentFingerprint)
        };

        var matches = _service.FindMatches(candidate, new[] { libraryEntry });

        Assert.All(matches, m => Assert.False(m.AutoAcceptEligible));
    }

    [Fact]
    public void FindMatches_EmptyLibrary_ReturnsNoMatches()
    {
        var candidate = new SongFingerprintResult { DurationSeconds = 10, Windows = Array.Empty<SongFingerprintWindow>() };

        var matches = _service.FindMatches(candidate, Array.Empty<SongLibraryEntry>());

        Assert.Empty(matches);
    }

    private async Task GenerateToneWavAsync(string outputPath)
    {
        using var process = new System.Diagnostics.Process
        {
            StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = FfmpegLocator.ResolveFfmpegPath(null),
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.StartInfo.ArgumentList.Add("-y");
        process.StartInfo.ArgumentList.Add("-f");
        process.StartInfo.ArgumentList.Add("lavfi");
        process.StartInfo.ArgumentList.Add("-i");
        process.StartInfo.ArgumentList.Add("sine=frequency=880:duration=9");
        process.StartInfo.ArgumentList.Add(outputPath);

        process.Start();
        await process.WaitForExitAsync();
        Assert.Equal(0, process.ExitCode);
    }
}
