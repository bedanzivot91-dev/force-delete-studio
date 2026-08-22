#Requires -Version 5.1
param(
    [Parameter(Mandatory = $true)]
    [string]$Destination
)

$ErrorActionPreference = 'Stop'

function Resolve-RealToolBinary([string]$Name) {
    $candidates = New-Object System.Collections.Generic.List[string]

    $command = Get-Command "$Name.exe" -ErrorAction SilentlyContinue
    if ($null -eq $command) { $command = Get-Command $Name -ErrorAction SilentlyContinue }
    if ($null -ne $command -and -not [string]::IsNullOrWhiteSpace($command.Source)) {
        $candidates.Add($command.Source)
    }

    # Chocolatey exposes tiny shim executables from its bin directory. Those shims cannot simply be copied
    # to a clean PC because their real target stays under Chocolatey's package folder. Search that package
    # folder as a second source and prefer a substantial real binary below.
    $chocoRoots = @()
    if (-not [string]::IsNullOrWhiteSpace($env:ChocolateyInstall)) {
        $chocoRoots += (Join-Path $env:ChocolateyInstall 'lib\ffmpeg\tools')
    }
    $chocoRoots += 'C:\ProgramData\chocolatey\lib\ffmpeg\tools'

    foreach ($root in ($chocoRoots | Select-Object -Unique)) {
        if (-not (Test-Path $root)) { continue }
        Get-ChildItem -Path $root -Filter "$Name.exe" -File -Recurse -ErrorAction SilentlyContinue |
            ForEach-Object { $candidates.Add($_.FullName) }
    }

    foreach ($candidate in ($candidates | Select-Object -Unique)) {
        if (-not (Test-Path $candidate -PathType Leaf)) { continue }
        $item = Get-Item $candidate
        # A static FFmpeg-family executable is many MB. Reject Chocolatey/other launch shims so the
        # produced installer never contains an exe that works only while a package manager remains installed.
        if ($item.Length -ge 1MB) { return $item.FullName }
    }

    throw "Nije pronadjen stvarni $Name.exe na PATH-u/Chocolatey instalaciji (shim se ne prihvata)."
}

New-Item -ItemType Directory -Force -Path $Destination | Out-Null
foreach ($name in @('ffmpeg', 'ffprobe', 'ffplay')) {
    $source = Resolve-RealToolBinary $name
    $target = Join-Path $Destination "$name.exe"
    Copy-Item -Path $source -Destination $target -Force
    if (-not (Test-Path $target) -or (Get-Item $target).Length -lt 1MB) {
        throw "Kopirani $name.exe nije validan stvarni binarni fajl."
    }

    # Validate the exact copied file, not the source on PATH.
    $probe = & $target -version 2>&1
    if ($LASTEXITCODE -ne 0 -or -not ($probe -match 'ffmpeg version|ffprobe version|ffplay version')) {
        throw "Kopirani $name.exe ne moze samostalno da se pokrene."
    }
}

Write-Host "Lokalni FFmpeg/FFprobe/FFplay su validirani i kopirani u $Destination." -ForegroundColor Green
