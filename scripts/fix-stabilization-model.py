from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]

def edit(rel, fn):
    p = ROOT / rel
    old = p.read_text(encoding='utf-8')
    new = fn(old)
    if new == old:
        raise RuntimeError(f'{rel}: expected change was not applied')
    p.write_text(new, encoding='utf-8')

# Session: use the project's already-existing stabilization schema and deep-copy every libvidstab field.
def session(text):
    text = text.replace('clip.StabilizationSmoothingFrames', 'clip.StabilizationSmoothing')
    text = text.replace('liveClip.StabilizationSmoothingFrames', 'liveClip.StabilizationSmoothing')
    text = text.replace('StabilizationSmoothingFrames = clip.StabilizationSmoothing,', 'StabilizationSmoothing = clip.StabilizationSmoothing,')
    needle = '''        StabilizationEnabled = clip.StabilizationEnabled,\n        StabilizationSmoothing = clip.StabilizationSmoothing,\n        StabilizationAccuracy = clip.StabilizationAccuracy,\n        StabilizationZoomPercent = clip.StabilizationZoomPercent,'''
    repl = '''        StabilizationEnabled = clip.StabilizationEnabled,\n        StabilizationShakiness = clip.StabilizationShakiness,\n        StabilizationAccuracy = clip.StabilizationAccuracy,\n        StabilizationSmoothing = clip.StabilizationSmoothing,\n        StabilizationZoomPercent = clip.StabilizationZoomPercent,\n        StabilizationOptimalZoom = clip.StabilizationOptimalZoom,'''
    if needle not in text: raise RuntimeError('session stabilization clone anchor missing')
    return text.replace(needle, repl, 1)
edit('src/NPVideoStudio.AI/TimelineEditSession.cs', session)

# VM keeps the friendly public property/binding name but reads the real persisted Smoothing field.
def vm(text):
    text = text.replace('Clip.StabilizationSmoothingFrames', 'Clip.StabilizationSmoothing')
    return text
edit('src/NPVideoStudio.App/ViewModels/TimelineClipItemViewModel.cs', vm)

# Builder: actual model names + existing OptimalZoom and full deep clone.
def builder(text):
    text = text.replace('clip.StabilizationSmoothingFrames', 'clip.StabilizationSmoothing')
    text = text.replace('StabilizationSmoothingFrames = clip.StabilizationSmoothing,', 'StabilizationSmoothing = clip.StabilizationSmoothing,')
    needle = '''        StabilizationEnabled = clip.StabilizationEnabled,\n        StabilizationSmoothing = clip.StabilizationSmoothing,\n        StabilizationAccuracy = clip.StabilizationAccuracy,\n        StabilizationZoomPercent = clip.StabilizationZoomPercent,'''
    repl = '''        StabilizationEnabled = clip.StabilizationEnabled,\n        StabilizationShakiness = clip.StabilizationShakiness,\n        StabilizationAccuracy = clip.StabilizationAccuracy,\n        StabilizationSmoothing = clip.StabilizationSmoothing,\n        StabilizationZoomPercent = clip.StabilizationZoomPercent,\n        StabilizationOptimalZoom = clip.StabilizationOptimalZoom,'''
    if needle not in text: raise RuntimeError('builder stabilization clone anchor missing')
    text = text.replace(needle, repl, 1)
    needle2 = '''        var smoothing = Math.Clamp(clip.StabilizationSmoothing, 0, 120);\n        var zoom = Math.Clamp(clip.StabilizationZoomPercent, 0, 30);\n        return FormattableString.Invariant(\n            $",vidstabtransform=input='{escapedPath}':smoothing={smoothing}:zoom={zoom}:optzoom=0:interpol=bicubic");'''
    repl2 = '''        var smoothing = Math.Clamp(clip.StabilizationSmoothing, 0, 120);\n        var zoom = Math.Clamp(clip.StabilizationZoomPercent, 0, 30);\n        var optimalZoom = Math.Clamp(clip.StabilizationOptimalZoom, 0, 2);\n        return FormattableString.Invariant(\n            $",vidstabtransform=input='{escapedPath}':smoothing={smoothing}:zoom={zoom}:optzoom={optimalZoom}:interpol=bicubic");'''
    if needle2 not in text: raise RuntimeError('builder transform options anchor missing')
    return text.replace(needle2, repl2, 1)
edit('src/NPVideoStudio.Media/FfmpegFilterGraphBuilder.cs', builder)

# First pass uses the existing persisted shakiness setting rather than a hard-coded 5.
def prepass(text):
    needle = '''$"vidstabdetect=result='{FfmpegFilterGraphBuilder.EscapeFilterPath(transformPath)}':shakiness=5:accuracy={Math.Clamp(clip.StabilizationAccuracy, 1, 15)}"'''
    repl = '''$"vidstabdetect=result='{FfmpegFilterGraphBuilder.EscapeFilterPath(transformPath)}':shakiness={Math.Clamp(clip.StabilizationShakiness, 1, 10)}:accuracy={Math.Clamp(clip.StabilizationAccuracy, 1, 15)}"'''
    if needle not in text: raise RuntimeError('prepass shakiness anchor missing')
    return text.replace(needle, repl, 1)
edit('src/NPVideoStudio.Media/VideoStabilizationPrepass.cs', prepass)

# Tests use persisted domain field names; UI binding name intentionally remains StabilizationSmoothingFrames.
def tests(text):
    text = text.replace('applied.StabilizationSmoothingFrames', 'applied.StabilizationSmoothing')
    text = text.replace('SingleClip(session).StabilizationSmoothingFrames', 'SingleClip(session).StabilizationSmoothing')
    text = text.replace('StabilizationSmoothingFrames = 27', 'StabilizationSmoothing = 27')
    text = text.replace('loaded.StabilizationSmoothingFrames', 'loaded.StabilizationSmoothing')
    text = text.replace('c.StabilizationSmoothingFrames', 'c.StabilizationSmoothing')
    text = text.replace('StabilizationSmoothingFrames = 22', 'StabilizationSmoothing = 22')
    text = text.replace('StabilizationSmoothingFrames = 10', 'StabilizationSmoothing = 10')
    return text
edit('tests/NPVideoStudio.UnitTests/StabilizationIntegrationTests.cs', tests)

print('Stabilization model consistency fixed.')
