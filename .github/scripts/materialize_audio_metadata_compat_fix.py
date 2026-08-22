from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]

def read(rel): return (ROOT / rel).read_text(encoding='utf-8')
def write(rel, text): (ROOT / rel).write_text(text, encoding='utf-8')
def rep(text, old, new, label):
    n = text.count(old)
    if n != 1: raise RuntimeError(f'{label}: expected one anchor, found {n}')
    return text.replace(old, new, 1)

p='src/NPVideoStudio.Media/FfmpegFilterGraphBuilder.cs'
s=read(p)
s=rep(s,
'''            if (asset.HasAudioStream)
            {''',
'''            if (!IsConfirmedSilentVideo(asset))
            {''',
'base video confirmed silent condition')
s=rep(s,
'''                if (!asset.HasAudioStream)
                {
                    throw new InvalidOperationException(
                        $"Audio traka referencira medij bez audio stream-a: {asset.FileName}.");
                }''',
'''                if (IsConfirmedSilentVideo(asset))
                {
                    throw new InvalidOperationException(
                        $"Audio traka referencira potvrđeno analiziran video bez audio stream-a: {asset.FileName}.");
                }''',
'audio track confirmed silent condition')
anchor='''    /// <summary>Real per-clip audio cleanup. The chain intentionally uses only filters present in the
    /// bundled Windows FFmpeg build and returns an empty string for neutral settings.</summary>
    public static string BuildAudioEnhancementFilters(TimelineClip clip)'''
helper='''    /// <summary>Backward-compatible stream-state check. Historical project files and many pure graph
    /// tests predate HasAudioStream and deserialize/default that bool to false. Treating every false as
    /// authoritative would silently strip valid embedded audio from older projects. A video is considered
    /// confirmed silent only after probe metadata proves a real video stream (HasVideoStream + VideoCodec)
    /// while HasAudioStream remains false. Current FfprobeService always fills VideoCodec for a probed video.</summary>
    private static bool IsConfirmedSilentVideo(MediaAsset asset) =>
        asset.HasVideoStream &&
        !asset.HasAudioStream &&
        !string.IsNullOrWhiteSpace(asset.VideoCodec) &&
        asset.ProbeError is null;

''' + anchor
s=rep(s,anchor,helper,'insert confirmed silent helper')
write(p,s)

p='tests/NPVideoStudio.UnitTests/AudioEnhancementIntegrationTests.cs'
t=read(p)
t=rep(t,
'''var video = new MediaAsset { Id = "v", FilePath = "silent.mp4", HasVideoStream = true, HasAudioStream = false, Duration = TimeSpan.FromSeconds(1) };''',
'''var video = new MediaAsset { Id = "v", FilePath = "silent.mp4", HasVideoStream = true, HasAudioStream = false, VideoCodec = "h264", Duration = TimeSpan.FromSeconds(1) };''',
'confirmed silent test metadata')
t=rep(t,
'''var baseVideo = new MediaAsset { Id = "v", FilePath = "base.mp4", HasVideoStream = true, HasAudioStream = false, Duration = TimeSpan.FromSeconds(1) };
        var silent = new MediaAsset { Id = "s", FilePath = "not-audio.mp4", HasVideoStream = true, HasAudioStream = false, Duration = TimeSpan.FromSeconds(1) };''',
'''var baseVideo = new MediaAsset { Id = "v", FilePath = "base.mp4", HasVideoStream = true, HasAudioStream = false, VideoCodec = "h264", Duration = TimeSpan.FromSeconds(1) };
        var silent = new MediaAsset { Id = "s", FilePath = "not-audio.mp4", HasVideoStream = true, HasAudioStream = false, VideoCodec = "h264", Duration = TimeSpan.FromSeconds(1) };''',
'confirmed silent audio-track test metadata')
t=t.replace('Assert.Contains("bez audio stream-a", ex.Message);','Assert.Contains("bez audio stream-a", ex.Message);')
insert_anchor='''    [Fact]
    public async Task RealFfmpeg_ExecutesCompleteAudioEnhancementChain()'''
extra='''    [Fact]
    public void LegacyUnknownStreamMetadata_PreservesHistoricalEmbeddedAudioBehavior()
    {
        var legacy = new MediaAsset { Id = "legacy", FilePath = "legacy.mp4" };
        var timeline = new Timeline
        {
            Tracks = new List<TimelineTrack>
            {
                new() { Kind = TimelineTrackKind.Video, Clips = new List<TimelineClip> { new() { MediaAssetId = "legacy", SourceTrimOutSeconds = 2, SpeedMultiplier = 2 } } }
            }
        };
        var plan = FfmpegFilterGraphBuilder.Build(timeline, new[] { legacy }, 320, 180, 30);
        Assert.Contains("[0:a]", plan.FilterComplexArgument);
        Assert.Contains("atempo=2", plan.FilterComplexArgument);
    }

''' + insert_anchor
t=rep(t,insert_anchor,extra,'insert legacy metadata regression test')
write(p,t)

for rel in ['.github/scripts/materialize_audio_metadata_compat_fix.py','.github/workflows/materialize-audio-metadata-compat-fix.yml']:
    q=ROOT/rel
    if q.exists(): q.unlink()
print('audio metadata compatibility fix materialized')
