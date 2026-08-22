from pathlib import Path

root = Path(__file__).resolve().parents[2]
release = root / 'scripts' / 'build-release.ps1'
text = release.read_text(encoding='utf-8-sig')
old = "$toolsDir = Join-Path $publishDir 'Tools'\n$bundledToolsOk = $true\n\ntry {"
new = "$toolsDir = Join-Path $publishDir 'Tools'\n$bundledToolsOk = $true\n# This destination must exist before the network attempt. If gyan.dev fails before any code\n# inside try assigns local variables, the fallback still needs a real, non-empty target.\n$ffmpegToolsDir = Join-Path $toolsDir 'ffmpeg'\nNew-Item -ItemType Directory -Force -Path $ffmpegToolsDir | Out-Null\n\ntry {"
if old not in text:
    raise SystemExit('release anchor 1 not found')
text = text.replace(old, new, 1)
old2 = "    $ffmpegBinDir = Join-Path (Get-ChildItem -Path $ffmpegExtractDir -Directory | Select-Object -First 1).FullName 'bin'\n    $ffmpegToolsDir = Join-Path $toolsDir 'ffmpeg'\n    New-Item -ItemType Directory -Force -Path $ffmpegToolsDir | Out-Null\n"
new2 = "    $ffmpegBinDir = Join-Path (Get-ChildItem -Path $ffmpegExtractDir -Directory | Select-Object -First 1).FullName 'bin'\n"
if old2 not in text:
    raise SystemExit('release anchor 2 not found')
text = text.replace(old2, new2, 1)
release.write_text(text, encoding='utf-8')

test = root / 'tests' / 'NPVideoStudio.UnitTests' / 'ReleaseFfmpegFallbackIntegrationTests.cs'
t = test.read_text(encoding='utf-8-sig')
anchor = "public sealed class ReleaseFfmpegFallbackIntegrationTests\n{\n"
method = r'''    [Fact]
    public void BuildRelease_InitializesFallbackDestinationBeforeNetworkDownload()
    {
        var repoRoot = FindRepositoryRoot();
        var scriptPath = Path.Combine(repoRoot, "scripts", "build-release.ps1");
        var script = File.ReadAllText(scriptPath);

        var destinationAssignment = script.IndexOf("$ffmpegToolsDir = Join-Path $toolsDir 'ffmpeg'", StringComparison.Ordinal);
        var destinationCreation = script.IndexOf("New-Item -ItemType Directory -Force -Path $ffmpegToolsDir", StringComparison.Ordinal);
        var networkAttempt = script.IndexOf("Invoke-WebRequest -Uri 'https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip'", StringComparison.Ordinal);
        var fallbackCall = script.IndexOf("& $fallbackScript -Destination $ffmpegToolsDir", StringComparison.Ordinal);

        Assert.True(destinationAssignment >= 0, "Release skripta ne inicijalizuje FFmpeg destination.");
        Assert.True(destinationCreation > destinationAssignment, "FFmpeg destination folder mora da se kreira posle izračunavanja putanje.");
        Assert.True(networkAttempt > destinationCreation, "FFmpeg destination mora postojati pre prvog mrežnog pokušaja.");
        Assert.True(fallbackCall > networkAttempt, "Fallback poziv mora ostati posle primarnog download pokušaja.");

        var assignmentsBeforeNetwork = script[..networkAttempt].Split("$ffmpegToolsDir = Join-Path $toolsDir 'ffmpeg'", StringSplitOptions.None).Length - 1;
        Assert.Equal(1, assignmentsBeforeNetwork);
    }

'''
if anchor not in t:
    raise SystemExit('test anchor not found')
t = t.replace(anchor, anchor + method, 1)
test.write_text(t, encoding='utf-8')

Path(__file__).unlink()
workflow = root / '.github' / 'workflows' / 'materialize-release-fallback-destination-fix.yml'
if workflow.exists():
    workflow.unlink()
