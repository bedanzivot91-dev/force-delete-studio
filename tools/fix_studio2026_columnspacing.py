from pathlib import Path

path = Path('src/NPVideoStudio.App/Views/ModernInspectorView.axaml')
text = path.read_text(encoding='utf-8')
old = '<Grid ColumnDefinitions="*,*,*,*" ColumnSpacing="6">'
new = '<Grid ColumnDefinitions="*,*,*,*">'
if old not in text:
    raise SystemExit('ColumnSpacing anchor missing')
text = text.replace(old, new, 1)
# Add equivalent visual spacing without relying on unsupported Avalonia Grid.ColumnSpacing.
text = text.replace(
    '<NumericUpDown Grid.Column="1" Value="{Binding CropTopPercent}"',
    '<NumericUpDown Grid.Column="1" Margin="6,0,0,0" Value="{Binding CropTopPercent}"', 1)
text = text.replace(
    '<NumericUpDown Grid.Column="2" Value="{Binding CropRightPercent}"',
    '<NumericUpDown Grid.Column="2" Margin="6,0,0,0" Value="{Binding CropRightPercent}"', 1)
text = text.replace(
    '<NumericUpDown Grid.Column="3" Value="{Binding CropBottomPercent}"',
    '<NumericUpDown Grid.Column="3" Margin="6,0,0,0" Value="{Binding CropBottomPercent}"', 1)
path.write_text(text, encoding='utf-8')
