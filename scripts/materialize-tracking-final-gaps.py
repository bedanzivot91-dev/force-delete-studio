from pathlib import Path

root = Path(__file__).resolve().parents[1]


def replace_once(path: str, old: str, new: str) -> None:
    p = root / path
    text = p.read_text(encoding="utf-8")
    count = text.count(old)
    if count != 1:
        raise SystemExit(f"{path}: expected one anchor, found {count}")
    p.write_text(text.replace(old, new, 1), encoding="utf-8")


# 1) Session-level defensive validation: never accept a path that does not cover the whole trimmed clip.
replace_once(
    "src/NPVideoStudio.AI/TimelineEditSession.cs",
    """        if (ordered.Count < 2) return false;\n\n        SaveSnapshot();""",
    """        if (ordered.Count < 2) return false;\n        const double endpointToleranceSeconds = 0.05;\n        if (ordered[0].SourceTimeSeconds > clip.SourceTrimInSeconds + endpointToleranceSeconds ||\n            ordered[^1].SourceTimeSeconds < clip.SourceTrimOutSeconds - endpointToleranceSeconds)\n        {\n            return false;\n        }\n\n        SaveSnapshot();""",
)

# Loaded/legacy project data can bypass ApplyMotionTrackingResult. Refuse enabling Auto Reframe unless
# the persisted path itself covers the complete current trim range.
replace_once(
    "src/NPVideoStudio.AI/TimelineEditSession.cs",
    """        if (enabled && (clip.IsReversed || clip.IsFreezeFrame || clip.MotionTrackingPoints.Count < 2)) return;""",
    """        if (enabled && (clip.IsReversed || clip.IsFreezeFrame || clip.MotionTrackingPoints.Count < 2 ||\n            clip.MotionTrackingPoints.Min(p => p.SourceTimeSeconds) > clip.SourceTrimInSeconds + 0.05 ||\n            clip.MotionTrackingPoints.Max(p => p.SourceTimeSeconds) < clip.SourceTrimOutSeconds - 0.05)) return;""",
)

# 2) Final render order must match stabilization pre-pass: crop/reframe first, then vidstabtransform.
replace_once(
    "src/NPVideoStudio.Media/FfmpegFilterGraphBuilder.cs",
    """            videoFilter.Append(BuildStabilizationFilter(clip, stabilizationTransforms));\n            videoFilter.Append(BuildAutoReframeFilter(clip, targetWidth, targetHeight));""",
    """            videoFilter.Append(BuildAutoReframeFilter(clip, targetWidth, targetHeight));\n            videoFilter.Append(BuildStabilizationFilter(clip, stabilizationTransforms));""",
)
replace_once(
    "src/NPVideoStudio.Media/FfmpegFilterGraphBuilder.cs",
    """            prepared.Append(BuildStabilizationFilter(clip, stabilizationTransforms));\n            prepared.Append(BuildAutoReframeFilter(clip, targetWidth, targetHeight));""",
    """            prepared.Append(BuildAutoReframeFilter(clip, targetWidth, targetHeight));\n            prepared.Append(BuildStabilizationFilter(clip, stabilizationTransforms));""",
)

# Render-time guard too: malformed persisted data must fail loudly instead of freezing the crop at an edge.
replace_once(
    "src/NPVideoStudio.Media/FfmpegFilterGraphBuilder.cs",
    """        if (clip.MotionTrackingPoints.Count < 2)\n            throw new InvalidOperationException(\"Auto Reframe je uključen, ali klip nema kompletnu Motion Tracking putanju.\");\n\n        var x = BuildTrackingValueExpression(clip, point => point.CenterX);""",
    """        if (clip.MotionTrackingPoints.Count < 2)\n            throw new InvalidOperationException(\"Auto Reframe je uključen, ali klip nema kompletnu Motion Tracking putanju.\");\n        var firstTrackingTime = clip.MotionTrackingPoints.Min(point => point.SourceTimeSeconds);\n        var lastTrackingTime = clip.MotionTrackingPoints.Max(point => point.SourceTimeSeconds);\n        if (firstTrackingTime > clip.SourceTrimInSeconds + 0.05 ||\n            lastTrackingTime < clip.SourceTrimOutSeconds - 0.05)\n        {\n            throw new InvalidOperationException(\"Auto Reframe putanja ne pokriva ceo trenutno isečeni klip. Pokrenite Motion Tracking ponovo.\");\n        }\n\n        var x = BuildTrackingValueExpression(clip, point => point.CenterX);""",
)

# 3) Dependency-manager test now reflects the actual product contract: a fully installed advanced AI toolset
# includes the CSRT tracker too. This is not weakening the production check; it makes the positive fixture complete.
replace_once(
    "tests/NPVideoStudio.UnitTests/DependencyManagerServiceTests.cs",
    """    public async Task GetDependenciesAsync_AiWorkerWithFasterWhisperDemucsAndLyricAlign_ReportsInstalled()""",
    """    public async Task GetDependenciesAsync_AiWorkerWithFasterWhisperDemucsLyricAlignAndOpenCv_ReportsInstalled()""",
)
replace_once(
    "tests/NPVideoStudio.UnitTests/DependencyManagerServiceTests.cs",
    """            DemucsAvailable = true,\n            LyricAlignAvailable = true\n        };""",
    """            DemucsAvailable = true,\n            LyricAlignAvailable = true,\n            OpenCvAvailable = true\n        };""",
)

# Add explicit malformed persisted-path coverage for render-time defense.
replace_once(
    "tests/NPVideoStudio.UnitTests/MotionTrackingAutoReframeTests.cs",
    """    [Fact]\n    public void RangePreview_PreservesTrackingPathAndAutoReframe()""",
    """    [Fact]\n    public void AutoReframeFilter_RejectsPersistedPartialTrackingPath()\n    {\n        var clip = NewTrackedClip();\n        clip.MotionTrackingPoints.RemoveAt(0);\n\n        var error = Assert.Throws<InvalidOperationException>(() =>\n            FfmpegFilterGraphBuilder.BuildAutoReframeFilter(clip, 1080, 1920));\n\n        Assert.Contains(\"ceo\", error.Message, StringComparison.OrdinalIgnoreCase);\n    }\n\n    [Fact]\n    public void RangePreview_PreservesTrackingPathAndAutoReframe()""",
)

print("Tracking final correctness fixes materialized.")
