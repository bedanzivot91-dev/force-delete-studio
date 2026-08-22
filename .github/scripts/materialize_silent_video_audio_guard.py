from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]

def rw(rel):
    p = ROOT / rel
    return p, p.read_text(encoding='utf-8')

def rep(text, old, new, label):
    n = text.count(old)
    if n != 1:
        raise RuntimeError(f'{label}: expected 1 anchor, found {n}')
    return text.replace(old, new, 1)

p, s = rw('src/NPVideoStudio.Media/FfmpegFilterGraphBuilder.cs')
old = '''            var audioFilter = new StringBuilder();
            var volume = clip.IsMuted ? 0 : clip.Volume;
            audioFilter.Append(FormattableString.Invariant(
                $"[{inputIndex}:a]atrim=start={clip.SourceTrimInSeconds}:end={clip.SourceTrimOutSeconds},asetpts=PTS-STARTPTS"));
            if (clip.IsReversed && !clip.IsFreezeFrame)
            {
                audioFilter.Append(",areverse");
            }
            audioFilter.Append(BuildAudioSpeedFilter(clip));
            audioFilter.Append(BuildAudioEnhancementFilters(clip));
            audioFilter.Append(FormattableString.Invariant($",volume={(clip.IsFreezeFrame ? 0 : volume)}"));
            if (clip.FadeInSeconds > 0)
            {
                audioFilter.Append(FormattableString.Invariant($",afade=t=in:st=0:d={clip.FadeInSeconds}"));
            }
            if (clip.FadeOutSeconds > 0)
            {
                var fadeOutStart = Math.Max(0, duration - clip.FadeOutSeconds);
                audioFilter.Append(FormattableString.Invariant($",afade=t=out:st={fadeOutStart}:d={clip.FadeOutSeconds}"));
            }
            audioFilter.Append(aLabel);
            filterLines.Add(audioFilter.ToString());'''
new = '''            if (asset.HasAudioStream)
            {
                var audioFilter = new StringBuilder();
                var volume = clip.IsMuted ? 0 : clip.Volume;
                audioFilter.Append(FormattableString.Invariant(
                    $"[{inputIndex}:a]atrim=start={clip.SourceTrimInSeconds}:end={clip.SourceTrimOutSeconds},asetpts=PTS-STARTPTS"));
                if (clip.IsReversed && !clip.IsFreezeFrame)
                {
                    audioFilter.Append(",areverse");
                }
                audioFilter.Append(BuildAudioSpeedFilter(clip));
                audioFilter.Append(BuildAudioEnhancementFilters(clip));
                audioFilter.Append(FormattableString.Invariant($",volume={(clip.IsFreezeFrame ? 0 : volume)}"));
                if (clip.FadeInSeconds > 0)
                {
                    audioFilter.Append(FormattableString.Invariant($",afade=t=in:st=0:d={clip.FadeInSeconds}"));
                }
                if (clip.FadeOutSeconds > 0)
                {
                    var fadeOutStart = Math.Max(0, duration - clip.FadeOutSeconds);
                    audioFilter.Append(FormattableString.Invariant($",afade=t=out:st={fadeOutStart}:d={clip.FadeOutSeconds}"));
                }
                audioFilter.Append(aLabel);
                filterLines.Add(audioFilter.ToString());
            }
            else
            {
                // A perfectly valid silent video has no [input:a] pad. Generate finite silence matching
                // this clip instead of emitting an invalid FFmpeg stream reference and failing export.
                filterLines.Add(FormattableString.Invariant(
                    $"anullsrc=r=44100:cl=stereo:d={duration}{aLabel}"));
            }'''
s = rep(s, old, new, 'base video silent-audio guard')
old2 = '''                if (asset is null)
                {
                    throw new InvalidOperationException(
                        $"Audio traka referencira medij koji ne postoji u biblioteci projekta (Id: {clip.MediaAssetId}).");
                }

                var inputIndex = inputs.Count;'''
new2 = '''                if (asset is null)
                {
                    throw new InvalidOperationException(
                        $"Audio traka referencira medij koji ne postoji u biblioteci projekta (Id: {clip.MediaAssetId}).");
                }
                if (!asset.HasAudioStream)
                {
                    throw new InvalidOperationException(
                        $"Audio traka referencira medij bez audio stream-a: {asset.FileName}.");
                }

                var inputIndex = inputs.Count;'''
s = rep(s, old2, new2, 'audio track no-stream guard')
p.write_text(s, encoding='utf-8')

p, t = rw('tests/NPVideoStudio.UnitTests/AudioEnhancementIntegrationTests.cs')
anchor = '''    [Fact]
    public async Task RealFfmpeg_ExecutesCompleteAudioEnhancementChain()'''
extra = '''    [Fact]
    public void SilentBaseVideo_GeneratesSilenceInsteadOfReferencingMissingAudioPad()
    {
        var video = new MediaAsset { Id = "v", FilePath = "silent.mp4", HasVideoStream = true, HasAudioStream = false, Duration = TimeSpan.FromSeconds(1) };
        var timeline = new Timeline
        {
            Tracks = new List<TimelineTrack>
            {
                new() { Kind = TimelineTrackKind.Video, Clips = new List<TimelineClip> { new() { MediaAssetId = "v", SourceTrimOutSeconds = 1 } } }
            }
        };
        var plan = FfmpegFilterGraphBuilder.Build(timeline, new[] { video }, 320, 180, 30);
        Assert.Contains("anullsrc=r=44100:cl=stereo:d=1[a0]", plan.FilterComplexArgument);
        Assert.DoesNotContain("[0:a]", plan.FilterComplexArgument);
    }

    [Fact]
    public void StandaloneAudioTrack_RejectsMediaWithoutAudioStreamClearly()
    {
        var baseVideo = new MediaAsset { Id = "v", FilePath = "base.mp4", HasVideoStream = true, HasAudioStream = false, Duration = TimeSpan.FromSeconds(1) };
        var silent = new MediaAsset { Id = "s", FilePath = "not-audio.mp4", HasVideoStream = true, HasAudioStream = false, Duration = TimeSpan.FromSeconds(1) };
        var timeline = new Timeline
        {
            Tracks = new List<TimelineTrack>
            {
                new() { Kind = TimelineTrackKind.Video, Clips = new List<TimelineClip> { new() { MediaAssetId = "v", SourceTrimOutSeconds = 1 } } },
                new() { Kind = TimelineTrackKind.Audio, Clips = new List<TimelineClip> { new() { MediaAssetId = "s", SourceTrimOutSeconds = 1 } } }
            }
        };
        var ex = Assert.Throws<InvalidOperationException>(() => FfmpegFilterGraphBuilder.Build(timeline, new[] { baseVideo, silent }, 320, 180, 30));
        Assert.Contains("bez audio stream-a", ex.Message);
    }

''' + anchor
t = rep(t, anchor, extra, 'insert silent video tests')
p.write_text(t, encoding='utf-8')

for rel in ['.github/scripts/materialize_silent_video_audio_guard.py', '.github/workflows/materialize-silent-video-audio-guard.yml']:
    q = ROOT / rel
    if q.exists(): q.unlink()
print('silent video audio guard materialized')
