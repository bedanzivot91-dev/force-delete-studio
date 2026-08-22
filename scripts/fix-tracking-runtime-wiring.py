from pathlib import Path

root = Path(__file__).resolve().parents[1]

def replace_once(path, old, new):
    p = root / path
    text = p.read_text(encoding='utf-8')
    count = text.count(old)
    if count != 1:
        raise SystemExit(f'{path}: expected one anchor, found {count}')
    p.write_text(text.replace(old, new, 1), encoding='utf-8')

replace_once('src/NPVideoStudio.App/ViewModels/MainWindowViewModel.cs',
'''            _services.GetRequiredService<IAiWorkerClient>(),
            _services.GetRequiredService<IProxyGeneratorService>());''',
'''            _services.GetRequiredService<IAiWorkerClient>(),
            _services.GetRequiredService<IProxyGeneratorService>(),
            _services.GetRequiredService<IMotionTrackingService>());''')

replace_once('ai-worker/ai_worker.py',
'''def run_capability_check() -> int:
''',
'''def check_opencv_tracking() -> bool:
    try:
        import cv2
    except ImportError as ex:
        emit({"type": "CapabilityCheck", "engine": "opencv", "engineAvailable": False, "message": str(ex)})
        return False

    creator = getattr(cv2, "TrackerCSRT_create", None)
    if creator is None:
        legacy = getattr(cv2, "legacy", None)
        creator = getattr(legacy, "TrackerCSRT_create", None) if legacy is not None else None
    available = creator is not None
    emit({
        "type": "CapabilityCheck",
        "engine": "opencv",
        "engineAvailable": available,
        "message": f"OpenCV {getattr(cv2, '__version__', '?')}" if available else "OpenCV je instaliran, ali CSRT tracker nije dostupan; potreban je opencv-contrib-python-headless.",
    })
    return available


def run_capability_check() -> int:
''')

replace_once('ai-worker/ai_worker.py',
'''    check_engine("lyric_align", "lyric_align")
    check_engine("cv2", "opencv")
''',
'''    check_engine("lyric_align", "lyric_align")
    check_opencv_tracking()
''')

print('Tracking runtime wiring and CSRT capability check fixed.')
