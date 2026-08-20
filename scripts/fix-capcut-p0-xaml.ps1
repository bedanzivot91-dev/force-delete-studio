$ErrorActionPreference = 'Stop'
$path = 'src/NPVideoStudio.App/Views/WorkspaceView.axaml'
$text = [System.IO.File]::ReadAllText((Resolve-Path $path), [System.Text.Encoding]::UTF8)
$old = '<Grid ColumnDefinitions="*,*,*,*" ColumnSpacing="6">'
$new = '<Grid ColumnDefinitions="*,*,*,*">'
if (-not $text.Contains($old)) { throw 'CapCut P0 crop grid anchor not found' }
$text = $text.Replace($old, $new)
[System.IO.File]::WriteAllText((Resolve-Path $path), $text, (New-Object System.Text.UTF8Encoding($false)))
Write-Host 'Removed unsupported Avalonia Grid.ColumnSpacing.'
