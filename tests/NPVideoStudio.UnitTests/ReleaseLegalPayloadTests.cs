using Xunit;

namespace NPVideoStudio.UnitTests;

public sealed class ReleaseLegalPayloadTests
{
    [Fact]
    public void AppPublish_ExplicitlyShipsThirdPartyNoticesAndLicenses()
    {
        var root = FindRepositoryRoot();
        var project = File.ReadAllText(Path.Combine(root, "src", "NPVideoStudio.App", "NPVideoStudio.App.csproj"));

        Assert.Contains("THIRD_PARTY_NOTICES.md", project);
        Assert.Contains("Licenses\\**\\*", project);
        Assert.Contains("CopyToPublishDirectory=\"PreserveNewest\"", project);
    }

    [Fact]
    public void ReleaseCompletenessGate_RequiresLegalPayload()
    {
        var root = FindRepositoryRoot();
        var script = File.ReadAllText(Path.Combine(root, "scripts", "build-release.ps1"));

        Assert.Contains("'THIRD_PARTY_NOTICES.md'", script);
        Assert.Contains("'Licenses\\GPLv3-FFmpeg.txt'", script);
        Assert.Contains("'Licenses\\LGPL-2.1-LibVLC.txt'", script);
    }

    private static string FindRepositoryRoot()
    {
        foreach (var startingPath in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var directory = new DirectoryInfo(startingPath);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "NPVideoStudio.sln"))) return directory.FullName;
                directory = directory.Parent;
            }
        }

        throw new DirectoryNotFoundException("Nije pronađen NPVideoStudio.sln iz test radnog direktorijuma.");
    }
}
