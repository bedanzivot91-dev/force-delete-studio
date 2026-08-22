from pathlib import Path

root = Path(__file__).resolve().parents[1]

def replace_once(path, old, new):
    p = root / path
    text = p.read_text(encoding='utf-8')
    count = text.count(old)
    if count != 1:
        raise SystemExit(f'{path}: expected one anchor, found {count}')
    p.write_text(text.replace(old, new, 1), encoding='utf-8')

replace_once('ai-worker/ai_worker.py',
'''    check_engine("demucs", "demucs")\n    check_engine("lyric_align", "lyric_align")\n    emit({"type": "Done"})''',
'''    check_engine("demucs", "demucs")\n    check_engine("lyric_align", "lyric_align")\n    check_engine("cv2", "opencv")\n    emit({"type": "Done"})''')

replace_once('src/NPVideoStudio.Domain/AiWorkerProtocol.cs',
'''    public bool DemucsAvailable { get; init; }\n    public bool LyricAlignAvailable { get; init; }\n    public string? Error { get; init; }''',
'''    public bool DemucsAvailable { get; init; }\n    public bool LyricAlignAvailable { get; init; }\n    public bool OpenCvAvailable { get; init; }\n    public string? Error { get; init; }''')

replace_once('src/NPVideoStudio.AI/AiWorkerClient.cs',
'''        var demucs = false;\n        var lyricAlign = false;\n        var errorMessage = (string?)null;''',
'''        var demucs = false;\n        var lyricAlign = false;\n        var openCv = false;\n        var errorMessage = (string?)null;''')
replace_once('src/NPVideoStudio.AI/AiWorkerClient.cs',
'''                    case "lyric_align":\n                        lyricAlign = evt.EngineAvailable == true;\n                        break;''',
'''                    case "lyric_align":\n                        lyricAlign = evt.EngineAvailable == true;\n                        break;\n                    case "opencv":\n                        openCv = evt.EngineAvailable == true;\n                        break;''')
replace_once('src/NPVideoStudio.AI/AiWorkerClient.cs',
'''            DemucsAvailable = demucs,\n            LyricAlignAvailable = lyricAlign\n        };''',
'''            DemucsAvailable = demucs,\n            LyricAlignAvailable = lyricAlign,\n            OpenCvAvailable = openCv\n        };''')

replace_once('src/NPVideoStudio.Diagnostics/DependencyManagerService.cs',
'''                        capabilities.FasterWhisperAvailable &&\n                        capabilities.DemucsAvailable &&\n                        capabilities.LyricAlignAvailable;''',
'''                        capabilities.FasterWhisperAvailable &&\n                        capabilities.DemucsAvailable &&\n                        capabilities.LyricAlignAvailable &&\n                        capabilities.OpenCvAvailable;''')
replace_once('src/NPVideoStudio.Diagnostics/DependencyManagerService.cs',
'''              $"Demucs: {(capabilities.DemucsAvailable ? "da" : "ne")}, " +\n              $"lyric-align: {(capabilities.LyricAlignAvailable ? "da" : "ne")}" +''',
'''              $"Demucs: {(capabilities.DemucsAvailable ? "da" : "ne")}, " +\n              $"lyric-align: {(capabilities.LyricAlignAvailable ? "da" : "ne")}, " +\n              $"OpenCV/CSRT: {(capabilities.OpenCvAvailable ? "da" : "ne")}" +''')
replace_once('src/NPVideoStudio.Diagnostics/DependencyManagerService.cs',
'''            WhyItMatters = "Za pesme moraju raditi faster-whisper, Demucs i lyric-align. WhisperX je opcion za napredno poravnanje.",''',
'''            WhyItMatters = "Za pesme moraju raditi faster-whisper, Demucs i lyric-align; OpenCV/CSRT je potreban za Motion Tracking i Auto Reframe. WhisperX je opcion za napredno poravnanje.",''')

print('Tracking capability wiring materialized.')
