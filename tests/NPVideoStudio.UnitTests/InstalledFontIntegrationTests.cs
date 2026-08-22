using System.Collections.ObjectModel;
using NPVideoStudio.AI;
using NPVideoStudio.App.ViewModels;
using NPVideoStudio.Domain;
using NPVideoStudio.Infrastructure.Persistence;
using NPVideoStudio.Media;
using Xunit;

namespace NPVideoStudio.UnitTests;

public class InstalledFontIntegrationTests
{
    private static InstalledFont GetRealSerbianFont()
    {
        var font = SystemFontCatalog.ListFontsUsableForSerbian().FirstOrDefault();
        Assert.NotNull(font);
        Assert.True(File.Exists(font!.FilePath));
        return font;
    }

    [Fact]
    public void TimelineSession_InstalledFont_IsUndoRedoSafeAndSurvivesDeepClone()
    {
        var font = GetRealSerbianFont();
        var clip = new TimelineClip
        {
            TextContent = "ČĆŽŠĐ љњћџђј",
            SourceTrimOutSeconds = 3
        };
        var track = new TimelineTrack { Kind = TimelineTrackKind.Text, Clips = { clip } };
        var session = new TimelineEditSession(new[] { track });

        session.SetTextFont(clip.Id, CaptionFontChoice.Default, font.FamilyName, font.FilePath);
        session.SetTextStyle(clip.Id, CaptionFontChoice.Default, 64, "#FFFFFF", CaptionTextPosition.Middle);

        session.Undo();
        var afterFirstUndo = Assert.Single(Assert.Single(session.Tracks).Clips);
        Assert.Equal(font.FamilyName, afterFirstUndo.TextFontFamilyName);
        Assert.Equal(font.FilePath, afterFirstUndo.TextFontFilePath);
        Assert.Equal(36, afterFirstUndo.FontSizePx);

        session.Undo();
        var beforeFontSelection = Assert.Single(Assert.Single(session.Tracks).Clips);
        Assert.Null(beforeFontSelection.TextFontFamilyName);
        Assert.Null(beforeFontSelection.TextFontFilePath);

        session.Redo();
        var afterRedo = Assert.Single(Assert.Single(session.Tracks).Clips);
        Assert.Equal(font.FamilyName, afterRedo.TextFontFamilyName);
        Assert.Equal(font.FilePath, afterRedo.TextFontFilePath);
    }

    [Fact]
    public void NormalInspectorPath_SelectsRealInstalledFontThroughTimelineSession()
    {
        var font = GetRealSerbianFont();
        var clip = new TimelineClip
        {
            TextContent = "Đorđe čuva šljive",
            SourceTrimOutSeconds = 3
        };
        var project = new Project { Name = "Font UI test" };
        project.Timeline.Tracks.Add(new TimelineTrack { Kind = TimelineTrackKind.Text, Clips = { clip } });
        var vm = new TimelineViewModel(project, new ObservableCollection<MediaAssetViewModel>(), () => 0);

        var item = Assert.Single(Assert.Single(vm.Tracks).Clips);
        Assert.Contains(item.AvailableFontChoices.OfType<InstalledFont>(), f => f.FilePath == font.FilePath);
        item.FontChoice = font;

        var edited = Assert.Single(Assert.Single(vm.CurrentTracks).Clips);
        Assert.Equal(font.FamilyName, edited.TextFontFamilyName);
        Assert.Equal(font.FilePath, edited.TextFontFilePath);

        vm.UndoCommand.Execute(null);
        var undone = Assert.Single(Assert.Single(vm.CurrentTracks).Clips);
        Assert.Null(undone.TextFontFamilyName);
        Assert.Null(undone.TextFontFilePath);
    }

    [Fact]
    public async Task ProjectRepository_RoundTripsInstalledFontSelection()
    {
        var font = GetRealSerbianFont();
        var project = new Project { Name = "Font persistence" };
        project.Timeline.Tracks.Add(new TimelineTrack
        {
            Kind = TimelineTrackKind.Caption,
            Clips =
            {
                new TimelineClip
                {
                    TextContent = "ČĆŽŠĐ",
                    SourceTrimOutSeconds = 2,
                    TextFontFamilyName = font.FamilyName,
                    TextFontFilePath = font.FilePath
                }
            }
        });

        var dir = Path.Combine(Path.GetTempPath(), $"npvs-font-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "font.npvsproject");
            var repository = new ProjectRepository();
            await repository.SaveAsync(project, path);
            var loaded = await repository.LoadAsync(path);
            var loadedClip = Assert.Single(Assert.Single(loaded.Timeline.Tracks).Clips);
            Assert.Equal(font.FamilyName, loadedClip.TextFontFamilyName);
            Assert.Equal(font.FilePath, loadedClip.TextFontFilePath);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void CaptionFontResolver_ExplicitInstalledFontPathWins()
    {
        var font = GetRealSerbianFont();
        var clip = new TimelineClip
        {
            TextContent = "ćirilica ћ",
            TextFontFamilyName = font.FamilyName,
            TextFontFilePath = font.FilePath
        };

        Assert.Equal(font.FilePath, CaptionFontResolver.ResolveFontFilePath(clip));
    }

    [Fact]
    public void FfmpegGraph_UsesRealInstalledFontForFinalDrawtext()
    {
        var font = GetRealSerbianFont();
        var asset = new MediaAsset
        {
            Id = "video",
            FilePath = "input-with-audio.mp4",
            Duration = TimeSpan.FromSeconds(2),
            HasVideoStream = true,
            HasAudioStream = true
        };
        var timeline = new Timeline();
        timeline.Tracks.Add(new TimelineTrack
        {
            Kind = TimelineTrackKind.Video,
            Clips =
            {
                new TimelineClip
                {
                    MediaAssetId = asset.Id,
                    SourceTrimOutSeconds = 2
                }
            }
        });
        timeline.Tracks.Add(new TimelineTrack
        {
            Kind = TimelineTrackKind.Caption,
            Clips =
            {
                new TimelineClip
                {
                    TextContent = "ČĆŽŠĐ љњћџђј",
                    SourceTrimOutSeconds = 2,
                    TextFontFamilyName = font.FamilyName,
                    TextFontFilePath = font.FilePath
                }
            }
        });

        var plan = FfmpegFilterGraphBuilder.Build(timeline, new[] { asset }, 640, 360, 30);

        Assert.Contains("drawtext=", plan.FilterComplexArgument);
        Assert.Contains("fontfile=", plan.FilterComplexArgument);
        Assert.Contains(Path.GetFileName(font.FilePath), plan.FilterComplexArgument, StringComparison.OrdinalIgnoreCase);
    }
}
