using NPVideoStudio.Domain;
using NPVideoStudio.Infrastructure.Persistence;
using Xunit;

namespace NPVideoStudio.UnitTests;

/// <summary>Real file I/O against a throwaway temp folder - no mocked file system.</summary>
public class UserTemplateRepositoryTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"npvs-templates-{Guid.NewGuid():N}");
    private readonly UserTemplateRepository _repository;

    public UserTemplateRepositoryTests() => _repository = new UserTemplateRepository(_folder);

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private static UserTemplate Template(string name) => new()
    {
        Name = name,
        Description = "Opis",
        StarterTrackKinds = { TimelineTrackKind.Video, TimelineTrackKind.Caption },
        Width = 1080,
        Height = 1920,
        FrameRate = FrameRatePreset.Custom,
        Fps = 29.97
    };

    [Fact]
    public void LoadAll_BeforeAnythingIsSaved_ReturnsEmptyInsteadOfThrowing()
    {
        Assert.Empty(_repository.LoadAll());
    }

    [Fact]
    public void SaveThenLoad_RoundTripsEverythingIncludingExactCustomFpsAndTracks()
    {
        _repository.Save(Template("Moj TikTok"));

        var loaded = Assert.Single(_repository.LoadAll());
        Assert.Equal("Moj TikTok", loaded.Name);
        Assert.Equal(1080, loaded.Width);
        Assert.Equal(1920, loaded.Height);
        Assert.Equal(FrameRatePreset.Custom, loaded.FrameRate);
        Assert.Equal(29.97, loaded.Fps, 6);
        Assert.Equal(new[] { TimelineTrackKind.Video, TimelineTrackKind.Caption }, loaded.StarterTrackKinds);
        Assert.True(_repository.Exists("Moj TikTok"));
    }

    [Fact]
    public void Save_SameNameTwice_OverwritesInsteadOfCreatingADuplicate()
    {
        _repository.Save(Template("Isti"));
        var second = Template("Isti");
        second.Description = "Izmenjen opis";
        _repository.Save(second);

        var all = _repository.LoadAll();
        Assert.Single(all);
        Assert.Equal("Izmenjen opis", all[0].Description);
    }

    [Fact]
    public void Rename_UpdatesNameAndFile_AndDoesNotLeaveOldTemplate()
    {
        _repository.Save(Template("Stari"));

        var renamed = _repository.Rename("Stari", "Novi");

        Assert.Equal("Novi", renamed.Name);
        Assert.False(_repository.Exists("Stari"));
        Assert.True(_repository.Exists("Novi"));
        Assert.Equal("Novi", Assert.Single(_repository.LoadAll()).Name);
    }

    [Fact]
    public void Rename_RefusesSilentOverwrite_UntilCallerExplicitlyAllowsIt()
    {
        var first = Template("Prvi");
        first.Description = "prvi";
        var second = Template("Drugi");
        second.Description = "drugi";
        _repository.Save(first);
        _repository.Save(second);

        Assert.Throws<IOException>(() => _repository.Rename("Prvi", "Drugi"));
        Assert.True(_repository.Exists("Prvi"));
        Assert.Equal(2, _repository.LoadAll().Count);

        _repository.Rename("Prvi", "Drugi", overwrite: true);
        var only = Assert.Single(_repository.LoadAll());
        Assert.Equal("Drugi", only.Name);
        Assert.Equal("prvi", only.Description);
        Assert.False(_repository.Exists("Prvi"));
    }

    [Fact]
    public void Delete_RemovesTheTemplate_AndReportsWhetherThereWasOne()
    {
        _repository.Save(Template("Za brisanje"));

        Assert.True(_repository.Delete("Za brisanje"));
        Assert.Empty(_repository.LoadAll());
        Assert.False(_repository.Delete("Za brisanje"));
    }

    [Fact]
    public void Save_NameWithCharactersWindowsForbidsInFileNames_StillSavesAndExistsUsesSamePath()
    {
        _repository.Save(Template("9:16 vertikalni"));

        var loaded = Assert.Single(_repository.LoadAll());
        Assert.Equal("9:16 vertikalni", loaded.Name);
        Assert.True(_repository.Exists("9:16 vertikalni"));
    }

    [Fact]
    public void Save_BlankName_IsRejected()
    {
        Assert.Throws<ArgumentException>(() => _repository.Save(new UserTemplate { Name = "   " }));
    }

    [Fact]
    public void LoadAll_OneCorruptFile_SkipsItAndStillReturnsTheGoodOnes()
    {
        _repository.Save(Template("Ispravan"));
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path.Combine(_folder, "pokvaren.json"), "{ ovo nije ispravan JSON");

        var all = _repository.LoadAll();

        Assert.Single(all);
        Assert.Equal("Ispravan", all[0].Name);
    }

    [Fact]
    public void FromProject_TakesTrackKindsAndExactFormat_ButNeverTheUsersFootage()
    {
        var project = new Project { Name = "Moj projekat" };
        project.Format.Width = 1080;
        project.Format.Height = 1920;
        project.Format.FrameRate = FrameRatePreset.Custom;
        project.Format.Fps = 23.976;
        project.Timeline.Tracks.Add(new TimelineTrack
        {
            Kind = TimelineTrackKind.Video,
            Clips = { new TimelineClip { MediaAssetId = "tajni-snimak", SourceTrimOutSeconds = 5 } }
        });

        var template = UserTemplateRepository.FromProject(project, "Moj šablon");

        Assert.Equal(new[] { TimelineTrackKind.Video }, template.StarterTrackKinds);
        Assert.Equal(1080, template.Width);
        Assert.Equal(1920, template.Height);
        Assert.Equal(FrameRatePreset.Custom, template.FrameRate);
        Assert.Equal(23.976, template.Fps, 6);
        Assert.DoesNotContain("tajni-snimak", System.Text.Json.JsonSerializer.Serialize(template));
    }

    [Fact]
    public void SanitizeFileName_EmptyAfterCleaning_FallsBackToAUsableName()
    {
        Assert.Equal("sablon", UserTemplateRepository.SanitizeFileName("   "));
    }
}
