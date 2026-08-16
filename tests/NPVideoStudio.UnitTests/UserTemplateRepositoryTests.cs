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
        FrameRate = FrameRatePreset.Fps60
    };

    [Fact]
    public void LoadAll_BeforeAnythingIsSaved_ReturnsEmptyInsteadOfThrowing()
    {
        Assert.Empty(_repository.LoadAll());
    }

    [Fact]
    public void SaveThenLoad_RoundTripsEverythingIncludingFormatAndTracks()
    {
        _repository.Save(Template("Moj TikTok"));

        var loaded = Assert.Single(_repository.LoadAll());
        Assert.Equal("Moj TikTok", loaded.Name);
        Assert.Equal(1080, loaded.Width);
        Assert.Equal(1920, loaded.Height);
        Assert.Equal(FrameRatePreset.Fps60, loaded.FrameRate);
        Assert.Equal(new[] { TimelineTrackKind.Video, TimelineTrackKind.Caption }, loaded.StarterTrackKinds);
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
    public void Delete_RemovesTheTemplate_AndReportsWhetherThereWasOne()
    {
        _repository.Save(Template("Za brisanje"));

        Assert.True(_repository.Delete("Za brisanje"));
        Assert.Empty(_repository.LoadAll());
        Assert.False(_repository.Delete("Za brisanje"));
    }

    [Fact]
    public void Save_NameWithCharactersWindowsForbidsInFileNames_StillSaves()
    {
        // A user naming a template after an aspect ratio ("9:16") must not hit a file-system error.
        _repository.Save(Template("9:16 vertikalni"));

        var loaded = Assert.Single(_repository.LoadAll());
        Assert.Equal("9:16 vertikalni", loaded.Name);
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
    public void FromProject_TakesTrackKindsAndFormat_ButNeverTheUsersFootage()
    {
        var project = new Project { Name = "Moj projekat" };
        project.Format.Width = 1080;
        project.Format.Height = 1920;
        project.Timeline.Tracks.Add(new TimelineTrack
        {
            Kind = TimelineTrackKind.Video,
            Clips = { new TimelineClip { MediaAssetId = "tajni-snimak", SourceTrimOutSeconds = 5 } }
        });

        var template = UserTemplateRepository.FromProject(project, "Moj šablon");

        Assert.Equal(new[] { TimelineTrackKind.Video }, template.StarterTrackKinds);
        Assert.Equal(1080, template.Width);
        Assert.Equal(1920, template.Height);
        // A template is a starting point: it must carry no reference to the media the project used.
        Assert.DoesNotContain("tajni-snimak", System.Text.Json.JsonSerializer.Serialize(template));
    }

    [Fact]
    public void SanitizeFileName_EmptyAfterCleaning_FallsBackToAUsableName()
    {
        Assert.Equal("sablon", UserTemplateRepository.SanitizeFileName("   "));
    }
}
