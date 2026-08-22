from pathlib import Path

root = Path(__file__).resolve().parents[1]

def replace_once(path, old, new):
    p = root / path
    text = p.read_text(encoding='utf-8')
    count = text.count(old)
    if count != 1:
        raise SystemExit(f'{path}: expected one anchor, found {count}')
    p.write_text(text.replace(old, new, 1), encoding='utf-8')

path = 'src/NPVideoStudio.Media/FfmpegFilterGraphBuilder.cs'
replace_once(path,
'''            prepared.Append(BuildStabilizationFilter(clip, stabilizationTransforms));
            prepared.Append(BuildTemporalVideoFilters(clip, clip.TimelineDurationSeconds));''',
'''            prepared.Append(BuildStabilizationFilter(clip, stabilizationTransforms));
            prepared.Append(BuildAutoReframeFilter(clip, targetWidth, targetHeight));
            prepared.Append(BuildTemporalVideoFilters(clip, clip.TimelineDurationSeconds));''')

replace_once(path,
'''        StabilizationZoomPercent = clip.StabilizationZoomPercent,
        StabilizationOptimalZoom = clip.StabilizationOptimalZoom,
        RotationDegrees = clip.RotationDegrees,''',
'''        StabilizationZoomPercent = clip.StabilizationZoomPercent,
        StabilizationOptimalZoom = clip.StabilizationOptimalZoom,
        TrackingRegionCenterX = clip.TrackingRegionCenterX,
        TrackingRegionCenterY = clip.TrackingRegionCenterY,
        TrackingRegionWidth = clip.TrackingRegionWidth,
        TrackingRegionHeight = clip.TrackingRegionHeight,
        MotionTrackingPoints = clip.MotionTrackingPoints.Select(point => new MotionTrackingPoint
        {
            SourceTimeSeconds = point.SourceTimeSeconds,
            CenterX = point.CenterX,
            CenterY = point.CenterY,
            Width = point.Width,
            Height = point.Height,
            Confidence = point.Confidence
        }).ToList(),
        AutoReframeEnabled = clip.AutoReframeEnabled,
        RotationDegrees = clip.RotationDegrees,''')

print('Tracking preview/overlay render consistency fixed.')
