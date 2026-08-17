using Xunit;

namespace NPVideoStudio.UnitTests;

/// <summary>
/// Real bug regression test: <c>NPVideoStudio.Installer.Program.CopyDirectory</c> used to join paths via
/// a naive <c>string.Replace(sourceDir, targetDir)</c>, which silently breaks whenever the two directories
/// disagree on a trailing separator - which they always did in the real installer, since
/// <c>AppContext.BaseDirectory</c> (the real source directory) is documented to always end with a trailing
/// separator, while the real install target (built via <c>Path.Combine</c>) never has one. This test
/// reproduces that exact mismatch with real temporary directories - cross-platform logic, even though the
/// real installer only ever executes on Windows (this Linux sandbox can never run it to catch this the
/// way the user's real machine did).
/// </summary>
public class InstallerCopyDirectoryTests
{
    [Fact]
    public void CopyDirectory_SourceHasTrailingSeparatorTargetDoesNot_FilesLandAtCorrectNestedPaths()
    {
        var root = Path.Combine(Path.GetTempPath(), "npvs-installer-test-" + Guid.NewGuid().ToString("N"));
        var source = Path.Combine(root, "source");
        var target = Path.Combine(root, "target", "NP Video Studio"); // deliberately no trailing separator, matching real InstallDir

        try
        {
            Directory.CreateDirectory(source);
            Directory.CreateDirectory(Path.Combine(source, "libvlc", "win-x64"));
            File.WriteAllText(Path.Combine(source, "NPVideoStudio.exe"), "top-level-exe");
            File.WriteAllText(Path.Combine(source, "libvlc", "win-x64", "libvlc.dll"), "nested-dll");

            // AppContext.BaseDirectory is documented to always end with a trailing separator - reproduce
            // that exactly, rather than relying on however Path.Combine happened to format `source` above.
            var sourceWithTrailingSeparator = source + Path.DirectorySeparatorChar;

            NPVideoStudio.Installer.Program.CopyDirectory(sourceWithTrailingSeparator, target);

            var topLevelExePath = Path.Combine(target, "NPVideoStudio.exe");
            var nestedDllPath = Path.Combine(target, "libvlc", "win-x64", "libvlc.dll");

            Assert.True(File.Exists(topLevelExePath), $"Expected {topLevelExePath} to exist - this is exactly the real bug (it ended up at a path missing the separator instead).");
            Assert.True(File.Exists(nestedDllPath), $"Expected {nestedDllPath} to exist - the nested subfolder must be a real child of the target, not a mangled sibling.");
            Assert.Equal("top-level-exe", File.ReadAllText(topLevelExePath));
            Assert.Equal("nested-dll", File.ReadAllText(nestedDllPath));

            // The specific, concrete symptom a real user hit: a sibling folder named "NP Video
            // Studiolibvlc" instead of a real "libvlc" subfolder inside "NP Video Studio".
            var buggyPath = Path.Combine(root, "target", "NP Video Studiolibvlc");
            Assert.False(Directory.Exists(buggyPath), "The old buggy string-replace path-joining must not reappear.");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void ResetLocalApplicationState_RemovesOnlyGeneratedState_AndKeepsUnrelatedFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "npvs-reset-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "AutoSave"));
            Directory.CreateDirectory(Path.Combine(root, "PreviewCache"));
            Directory.CreateDirectory(Path.Combine(root, "Models"));
            File.WriteAllText(Path.Combine(root, "settings.json"), "old settings");
            File.WriteAllText(Path.Combine(root, "npvideostudio.db"), "old database");
            File.WriteAllText(Path.Combine(root, "AutoSave", "old.npvsproject"), "autosave");
            File.WriteAllText(Path.Combine(root, "Models", "model.bin"), "keep expensive model");

            NPVideoStudio.Installer.Program.ResetLocalApplicationState(root);

            Assert.False(File.Exists(Path.Combine(root, "settings.json")));
            Assert.False(File.Exists(Path.Combine(root, "npvideostudio.db")));
            Assert.False(Directory.Exists(Path.Combine(root, "AutoSave")));
            Assert.False(Directory.Exists(Path.Combine(root, "PreviewCache")));
            Assert.True(File.Exists(Path.Combine(root, "Models", "model.bin")));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
