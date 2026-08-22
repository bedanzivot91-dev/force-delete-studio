from pathlib import Path

root = Path(__file__).resolve().parents[1]
path = root / 'src/NPVideoStudio.Media/FfmpegFilterGraphBuilder.cs'
text = path.read_text(encoding='utf-8')
old = '''            ClipVideoEffect.Invert => "negate",\n            ClipVideoEffect.Mirror => "hflip",'''
new = '''            ClipVideoEffect.Invert => "negate",\n            ClipVideoEffect.SmoothSlowMotion => "minterpolate=fps=60:mi_mode=mci:mc_mode=aobmc:me_mode=bidir:me=epzs:vsbmc=1",\n            ClipVideoEffect.Mirror => "hflip",'''
if text.count(old) != 1:
    raise SystemExit(f'Expected exactly one optical-flow switch anchor, found {text.count(old)}')
path.write_text(text.replace(old, new, 1), encoding='utf-8')
print('Patched SmoothSlowMotion to real FFmpeg minterpolate.')
