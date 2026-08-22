from pathlib import Path

p = Path('src/NPVideoStudio.App/ViewModels/TimelineClipItemViewModel.cs')
text = p.read_text(encoding='utf-8')
old = '_onTextStyleChanged?.Invoke(Clip.Id, FontChoice, FontSizePx, TextColor, value);'
new = '_onTextStyleChanged?.Invoke(Clip.Id, Clip.FontChoice, FontSizePx, TextColor, value);'
if old not in text:
    raise RuntimeError('TextPosition font callback anchor not found')
p.write_text(text.replace(old, new, 1), encoding='utf-8')
print('Compile fix materialized.')
