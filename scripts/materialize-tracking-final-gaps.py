from pathlib import Path

root = Path(__file__).resolve().parents[1]

def replace_once(path, old, new):
    p = root / path
    text = p.read_text(encoding='utf-8')
    count = text.count(old)
    if count != 1:
        raise SystemExit(f'{path}: expected one anchor, found {count}')
    p.write_text(text.replace(old, new, 1), encoding='utf-8')

def replace_count(path, old, new, expected):
    p = root / path
    text = p.read_text(encoding='utf-8')
    count = text.count(old)
    if count != expected:
        raise SystemExit(f'{path}: expected {expected} anchors, found {count}')
    p.write_text(text.replace(old, new), encoding='utf-8')

# Auto Reframe coordinates describe original source frames. Crop first, then run vidstabtransform over
# exactly the same reframed geometry that vidstabdetect sees in its first pass.
replace_count(
    'src/NPVideoStudio.Media/FfmpegFilterGraphBuilder.cs',
    '''            videoFilter.Append(BuildStabilizationFilter(clip, stabilizationTransforms));\n            videoFilter.Append(BuildAutoReframeFilter(clip, targetWidth, targetHeight));''',
    '''            videoFilter.Append(BuildAutoReframeFilter(clip, targetWidth, targetHeight));\n            videoFilter.Append(BuildStabilizationFilter(clip, stabilizationTransforms));''',
    1)
replace_count(
    'src/NPVideoStudio.Media/FfmpegFilterGraphBuilder.cs',
    '''            prepared.Append(BuildStabilizationFilter(clip, stabilizationTransforms));\n            prepared.Append(BuildAutoReframeFilter(clip, targetWidth, targetHeight));''',
    '''            prepared.Append(BuildAutoReframeFilter(clip, targetWidth, targetHeight));\n            prepared.Append(BuildStabilizationFilter(clip, stabilizationTransforms));''',
    1)

# The stabilization pre-pass must analyze the exact same reframed pixels/dimensions as final render.
replace_once(
    'src/NPVideoStudio.Media/VideoStabilizationPrepass.cs',
    '''                await RunDetectAsync(ffmpegPath, asset.FilePath, clip, transformPath, cancellationToken)\n                    .ConfigureAwait(false);''',
    '''                await RunDetectAsync(\n                        ffmpegPath, asset.FilePath, clip, transformPath,\n                        project.Format.Width, project.Format.Height, cancellationToken)\n                    .ConfigureAwait(false);''')
replace_once(
    'src/NPVideoStudio.Media/VideoStabilizationPrepass.cs',
    '''        TimelineClip clip,\n        string transformPath,\n        CancellationToken cancellationToken)''',
    '''        TimelineClip clip,\n        string transformPath,\n        int targetWidth,\n        int targetHeight,\n        CancellationToken cancellationToken)''')
replace_once(
    'src/NPVideoStudio.Media/VideoStabilizationPrepass.cs',
    '''        startInfo.ArgumentList.Add(\n            $"vidstabdetect=result='{FfmpegFilterGraphBuilder.EscapeFilterPath(transformPath)}':shakiness={Math.Clamp(clip.StabilizationShakiness, 1, 10)}:accuracy={Math.Clamp(clip.StabilizationAccuracy, 1, 15)}");''',
    '''        var detectFilter =\n            $"vidstabdetect=result='{FfmpegFilterGraphBuilder.EscapeFilterPath(transformPath)}':shakiness={Math.Clamp(clip.StabilizationShakiness, 1, 10)}:accuracy={Math.Clamp(clip.StabilizationAccuracy, 1, 15)}";\n        if (clip.AutoReframeEnabled)\n        {\n            // The final graph resets PTS before Auto Reframe, and the tracking expression uses clip-local\n            // time. Do the same here so vidstabdetect and vidstabtransform operate on identical frames.\n            detectFilter = "setpts=PTS-STARTPTS" +\n                           FfmpegFilterGraphBuilder.BuildAutoReframeFilter(clip, targetWidth, targetHeight) +\n                           "," + detectFilter;\n        }\n        startInfo.ArgumentList.Add(detectFilter);''')

# A partial path must never silently become a successful Auto Reframe result.
replace_once(
    'src/NPVideoStudio.AI/TimelineEditSession.cs',
    '''        if (ordered.Count < 2) return false;\n\n        SaveSnapshot();''',
    '''        if (ordered.Count < 2) return false;\n        const double endpointToleranceSeconds = 0.05;\n        if (ordered[0].SourceTimeSeconds > clip.SourceTrimInSeconds + endpointToleranceSeconds ||\n            ordered[^1].SourceTimeSeconds < clip.SourceTrimOutSeconds - endpointToleranceSeconds)\n        {\n            return false;\n        }\n\n        SaveSnapshot();''')

# Ship the tracker itself in the executable payload, not only in source control.
replace_once(
    'src/NPVideoStudio.App/NPVideoStudio.App.csproj',
    '''    <None Include="..\\..\\ai-worker\\ai_worker.py" Link="Tools\\ai-worker\\ai_worker.py" CopyToOutputDirectory="PreserveNewest" />\n    <None Include="..\\..\\ai-worker\\requirements.txt" Link="Tools\\ai-worker\\requirements.txt" CopyToOutputDirectory="PreserveNewest" />''',
    '''    <None Include="..\\..\\ai-worker\\ai_worker.py" Link="Tools\\ai-worker\\ai_worker.py" CopyToOutputDirectory="PreserveNewest" />\n    <None Include="..\\..\\ai-worker\\motion_tracker.py" Link="Tools\\ai-worker\\motion_tracker.py" CopyToOutputDirectory="PreserveNewest" />\n    <None Include="..\\..\\ai-worker\\requirements.txt" Link="Tools\\ai-worker\\requirements.txt" CopyToOutputDirectory="PreserveNewest" />''')

# Packaging cannot go green unless the installed payload really contains the tracker script.
replace_once(
    'scripts/build-release.ps1',
    '''    'Tools\\ai-worker\\ai_worker.py',\n    'Tools\\ai-worker\\install-song-ai.ps1' ''',
    '''    'Tools\\ai-worker\\ai_worker.py',\n    'Tools\\ai-worker\\motion_tracker.py',\n    'Tools\\ai-worker\\install-song-ai.ps1' ''')

print('Final tracking correctness/payload gaps materialized.')
