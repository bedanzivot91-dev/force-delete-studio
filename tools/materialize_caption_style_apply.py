from pathlib import Path


def replace_once(path: str, old: str, new: str) -> None:
    p = Path(path)
    text = p.read_text(encoding="utf-8")
    if new in text:
        return
    if old not in text:
        raise SystemExit(f"anchor not found in {path}: {old[:100]!r}")
    p.write_text(text.replace(old, new, 1), encoding="utf-8")


def insert_before(path: str, anchor: str, addition: str) -> None:
    p = Path(path)
    text = p.read_text(encoding="utf-8")
    if addition.strip() in text:
        return
    if anchor not in text:
        raise SystemExit(f"anchor not found in {path}: {anchor[:100]!r}")
    p.write_text(text.replace(anchor, addition + anchor, 1), encoding="utf-8")

# 1) One undo-safe production operation in the editing engine.
session_method = r'''    /// <summary>
    /// Applies the renderable part of a caption-style gallery preset to one real Caption/Text clip in a
    /// single undo step. The gallery catalog also describes granularity and named animation ideas; those
    /// are intentionally not faked here. This method changes only fields that the current FFmpeg renderer
    /// actually consumes: text color, outline/shadow and optional panel background.
    /// </summary>
    public bool ApplyCaptionStylePreset(string clipId, CaptionStylePreset preset)
    {
        var (_, clip) = FindClipWithTrack(clipId);
        if (clip is null || clip.TextContent is null)
        {
            return false;
        }

        SaveSnapshot();
        var liveClip = FindClipWithTrack(clipId).Clip!;
        liveClip.TextColor = preset.TextColorHex;
        liveClip.TextOutlineWidthPx = Math.Max(2, liveClip.TextOutlineWidthPx);
        liveClip.TextShadowOffsetPx = Math.Max(2, liveClip.TextShadowOffsetPx);

        if (preset.Animation == CaptionAnimationKind.Shadow)
        {
            liveClip.TextOutlineColor = null;
            liveClip.TextShadowColor = preset.OutlineOrShadowColorHex;
        }
        else
        {
            // Outline is also the safe static fallback for Glow/Pop/Slide/etc. until their temporal
            // animation engines exist; unlike the old gallery this still produces a visible exported change.
            liveClip.TextOutlineColor = preset.OutlineOrShadowColorHex;
            liveClip.TextShadowColor = null;
        }

        if (!string.IsNullOrWhiteSpace(preset.PanelColorHex))
        {
            liveClip.HasTextBackground = true;
            var panel = preset.PanelColorHex!;
            if (panel.Length == 9 && panel[0] == '#')
            {
                // Avalonia catalog colors use #AARRGGBB. FFmpeg drawtext expects RGB plus a separate
                // opacity, so split the alpha instead of passing an invalid 8-digit color through.
                liveClip.TextBackgroundOpacity = Math.Clamp(Convert.ToInt32(panel.Substring(1, 2), 16) / 255.0, 0, 1);
                liveClip.TextBackgroundColor = "#" + panel.Substring(3, 6);
            }
            else
            {
                liveClip.TextBackgroundColor = panel;
                liveClip.TextBackgroundOpacity = Math.Clamp(liveClip.TextBackgroundOpacity, 0.15, 1);
            }
        }
        else
        {
            liveClip.HasTextBackground = false;
        }

        return true;
    }

'''
insert_before(
    "src/NPVideoStudio.AI/TimelineEditSession.cs",
    '    /// <summary>\n    /// "Primeni na sve titlove na ovoj traci"',
    session_method,
)

# 2) Reach the operation through the normal selected-clip Timeline ViewModel path.
timeline_method = r'''    /// <summary>Applies one gallery preset to the currently selected real text/caption clip.</summary>
    public bool ApplyCaptionStylePresetToSelected(CaptionStylePreset preset)
    {
        var selectedId = SelectedClipId;
        if (selectedId is null || SelectedClip is not { IsTextClip: true })
        {
            return false;
        }

        if (!_session.ApplyCaptionStylePreset(selectedId, preset))
        {
            return false;
        }

        RefreshFromSession();
        SelectedClipId = selectedId;
        return true;
    }

'''
insert_before(
    "src/NPVideoStudio.App/ViewModels/TimelineViewModel.cs",
    "    public double TotalDurationSeconds =>",
    timeline_method,
)

# 3) Workspace navigation + apply/save/preview path.
replace_once(
    "src/NPVideoStudio.App/ViewModels/WorkspaceViewModel.cs",
    "    public event Action? ExportRequested;\n",
    "    public event Action? ExportRequested;\n    public event Action? CaptionStyleGalleryRequested;\n",
)
workspace_methods = r'''    [RelayCommand]
    private void OpenCaptionStyleGallery()
    {
        if (Timeline.SelectedClip is not { IsTextClip: true })
        {
            StatusMessage = "Izaberite titl ili tekst klip na timeline-u, pa ponovo otvorite Stilove titlova.";
            return;
        }

        // Keep every live timeline edit in the project model before navigating away from the workspace.
        Timeline.SaveToProject();
        CaptionStyleGalleryRequested?.Invoke();
    }

    public async Task<string> ApplyCaptionStylePresetAsync(CaptionStylePreset preset)
    {
        if (!Timeline.ApplyCaptionStylePresetToSelected(preset))
        {
            return "Preset nije primenjen: izabrani klip više nije titl/tekst klip.";
        }

        Timeline.SaveToProject();
        if (!string.IsNullOrEmpty(Project.ProjectFilePath))
        {
            await _projectRepository.SaveAsync(Project, Project.ProjectFilePath);
        }

        RefreshPreviewFrame(Player.CurrentTimeSeconds);
        RaiseCaptionPreviewChanged();

        var message = $"Stil „{preset.Name}“ je primenjen i sačuvan. Boja/kontura-senka/panel ulaze u finalni FFmpeg render. " +
                      $"Deklarisana animacija {preset.Animation} i granularnost {preset.Granularity} nisu lažno označene kao primenjene dok njihov renderer ne bude dodat.";
        StatusMessage = message;
        return message;
    }

'''
insert_before(
    "src/NPVideoStudio.App/ViewModels/WorkspaceViewModel.cs",
    "    [RelayCommand]\n    private async Task ImportMediaAsync()",
    workspace_methods,
)

# 4) Keep the same live Workspace instance while gallery is open and wire it from the project.
replace_once(
    "src/NPVideoStudio.App/ViewModels/MainWindowViewModel.cs",
    "        if (oldValue is IDisposable disposable && !(oldValue is WorkspaceViewModel && newValue is RenderQueueViewModel))\n        {\n            disposable.Dispose();\n        }",
    "        var keepWorkspaceAlive = oldValue is WorkspaceViewModel &&\n            newValue is RenderQueueViewModel or CaptionStyleGalleryViewModel;\n        if (oldValue is IDisposable disposable && !keepWorkspaceAlive)\n        {\n            disposable.Dispose();\n        }",
)
caption_page_method = r'''    private CaptionStyleGalleryViewModel CreateCaptionStyleGalleryPage(WorkspaceViewModel workspace)
    {
        var vm = new CaptionStyleGalleryViewModel(workspace.ApplyCaptionStylePresetAsync);
        vm.BackRequested += () => CurrentPage = workspace;
        return vm;
    }

'''
insert_before(
    "src/NPVideoStudio.App/ViewModels/MainWindowViewModel.cs",
    "    private ViewModelBase CreateNewProjectPage(",
    caption_page_method,
)
replace_once(
    "src/NPVideoStudio.App/ViewModels/MainWindowViewModel.cs",
    "        workspace.ExportRequested += () => CurrentPage = CreateRenderQueuePage(workspace);\n",
    "        workspace.ExportRequested += () => CurrentPage = CreateRenderQueuePage(workspace);\n        workspace.CaptionStyleGalleryRequested += () => CurrentPage = CreateCaptionStyleGalleryPage(workspace);\n",
)

# 5) Gallery is no longer preview-only when opened from a project.
Path("src/NPVideoStudio.App/ViewModels/CaptionStyleGalleryViewModel.cs").write_text(r'''using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NPVideoStudio.Domain;
using NPVideoStudio.App.Services;

namespace NPVideoStudio.App.ViewModels;

/// <summary>
/// Caption preset browser. It can still be opened from Home as a read-only catalog, but when opened from
/// an active project it receives the real workspace apply callback and every card becomes an actual edit.
/// </summary>
public sealed partial class CaptionStyleGalleryViewModel : ViewModelBase
{
    private readonly Func<CaptionStylePreset, Task<string>>? _applyPreset;

    public IReadOnlyList<AppTheme?> Themes { get; } = new AppTheme?[] { null }.Concat(Enum.GetValues<AppTheme>().Cast<AppTheme?>()).ToList();
    public ObservableCollection<CaptionStylePresetItemViewModel> Presets { get; } = new();
    public bool CanApplyToProject => _applyPreset is not null;

    [ObservableProperty]
    private AppTheme? _selectedThemeFilter;

    [ObservableProperty]
    private string? _statusMessage;

    public event Action? BackRequested;

    public CaptionStyleGalleryViewModel() : this(null)
    {
    }

    public CaptionStyleGalleryViewModel(Func<CaptionStylePreset, Task<string>>? applyPreset)
    {
        _applyPreset = applyPreset;
        Refresh();
    }

    partial void OnSelectedThemeFilterChanged(AppTheme? value) => Refresh();

    [RelayCommand]
    private void Back() => BackRequested?.Invoke();

    public async Task ApplyPresetAsync(CaptionStylePreset preset)
    {
        if (_applyPreset is null)
        {
            StatusMessage = "Otvorite galeriju iz projekta i izaberite titl/tekst klip da biste primenili preset.";
            return;
        }

        StatusMessage = await _applyPreset(preset);
    }

    private void Refresh()
    {
        var source = SelectedThemeFilter is null
            ? CaptionStylePresetCatalog.All
            : CaptionStylePresetCatalog.ForTheme(SelectedThemeFilter.Value);

        Presets.Clear();
        foreach (var preset in source)
        {
            Presets.Add(new CaptionStylePresetItemViewModel(
                preset,
                _applyPreset is null ? null : () => ApplyPresetAsync(preset)));
        }
    }
}
''', encoding="utf-8")

Path("src/NPVideoStudio.App/ViewModels/CaptionStylePresetItemViewModel.cs").write_text(r'''using System.Windows.Input;
using Avalonia.Media;
using CommunityToolkit.Mvvm.Input;
using NPVideoStudio.Domain;

namespace NPVideoStudio.App.ViewModels;

/// <summary>One caption-preset card plus an optional real-project apply command.</summary>
public sealed class CaptionStylePresetItemViewModel
{
    public CaptionStylePreset Preset { get; }

    public string Name => Preset.Name;
    public IBrush TextBrush { get; }
    public IBrush AccentBrush { get; }
    public IBrush OutlineOrShadowBrush { get; }
    public IBrush? PanelBrush { get; }
    public bool HasPanel => PanelBrush is not null;
    public ICommand? ApplyCommand { get; }
    public bool CanApply => ApplyCommand is not null;

    public string GranularityLabel => Preset.Granularity switch
    {
        CaptionGranularity.WordByWord => "Reč po reč",
        CaptionGranularity.Karaoke => "Karaoke (aktivna reč)",
        _ => "Red po red"
    };

    public string AnimationLabel => Preset.Animation switch
    {
        CaptionAnimationKind.Pop => "Pop",
        CaptionAnimationKind.Scale => "Uvećanje",
        CaptionAnimationKind.Slide => "Klizanje",
        CaptionAnimationKind.Fade => "Iščezavanje",
        CaptionAnimationKind.Bounce => "Otkucaj",
        CaptionAnimationKind.Glow => "Sjaj",
        CaptionAnimationKind.Outline => "Kontura",
        CaptionAnimationKind.Shadow => "Senka",
        CaptionAnimationKind.BlurPanel => "Stakleni panel",
        CaptionAnimationKind.GradientPanel => "Gradijent panel",
        _ => Preset.Animation.ToString()
    };

    public CaptionStylePresetItemViewModel(CaptionStylePreset preset, Func<Task>? applyAsync = null)
    {
        Preset = preset;
        TextBrush = Brush.Parse(preset.TextColorHex);
        AccentBrush = Brush.Parse(preset.AccentColorHex);
        OutlineOrShadowBrush = Brush.Parse(preset.OutlineOrShadowColorHex);
        PanelBrush = preset.PanelColorHex is null ? null : Brush.Parse(preset.PanelColorHex);
        ApplyCommand = applyAsync is null ? null : new AsyncRelayCommand(applyAsync);
    }
}
''', encoding="utf-8")

# 6) Visible UI entry from the normal Workspace and real Apply button on each gallery card.
replace_once(
    "src/NPVideoStudio.App/Views/WorkspaceView.axaml",
    '        <Button Classes="ghost" Content="Karaoke titlovi (reč po reč)"\n                ToolTip.Tip="Svaka izgovorena reč se pojavljuje na ekranu pojedinačno, tačno kad se izgovori."\n                Command="{Binding GenerateKaraokeCaptionsForVideoCommand}" IsEnabled="{Binding !IsGeneratingCaptions}" />',
    '        <Button Classes="ghost" Content="Karaoke titlovi (reč po reč)"\n                ToolTip.Tip="Svaka izgovorena reč se pojavljuje na ekranu pojedinačno, tačno kad se izgovori."\n                Command="{Binding GenerateKaraokeCaptionsForVideoCommand}" IsEnabled="{Binding !IsGeneratingCaptions}" />\n        <Button Classes="ghost" Content="🎨 STILOVI ZA IZABRANI TITL" Command="{Binding OpenCaptionStyleGalleryCommand}"\n                ToolTip.Tip="Izaberite titl/tekst klip na timeline-u, pa primenite pravi preset koji se čuva i ulazi u export." />',
)

gallery = Path("src/NPVideoStudio.App/Views/CaptionStyleGalleryView.axaml")
text = gallery.read_text(encoding="utf-8")
text = text.replace(
    '      <TextBlock Classes="subtle" TextWrapping="Wrap"\n                 Text="24 gotova stila (najmanje 3 po temi): red-po-red, reč-po-reč i karaoke, sa bojama uzetim direktno iz svake teme. Ovo je samo statički pregled boja/ideja za inspiraciju - klik ovde NE menja izgled titla u projektu. Za font/veličinu/boju/položaj koji se stvarno vide u izvezenom videu, podesite ih direktno na titl/tekst klipu u radnom prostoru projekta (Timeline)." />',
    '      <TextBlock Classes="subtle" TextWrapping="Wrap"\n                 Text="24 gotova stila. Kada ovu galeriju otvorite iz aktivnog projekta, dugme PRIMENI menja pravi izabrani titl/tekst klip, čuva renderovane boje/konturu/senku/panel i podržava Undo/Redo. Animacije i granularnost koje renderer još nema prijavljuju se eksplicitno — nema tihog no-op ponašanja." />\n\n      <StackPanel Orientation="Horizontal" Spacing="10">\n        <Button Classes="ghost" Content="← Nazad u projekat" Command="{Binding BackCommand}" IsVisible="{Binding CanApplyToProject}" />\n        <TextBlock Text="{Binding StatusMessage}" Classes="subtle" TextWrapping="Wrap" VerticalAlignment="Center" MaxWidth="760" />\n      </StackPanel>',
)
anchor = '''                <StackPanel Orientation="Horizontal" Spacing="6">
                  <Border Width="18" Height="18" CornerRadius="4" Background="{Binding TextBrush}" ToolTip.Tip="Boja teksta" />
                  <Border Width="18" Height="18" CornerRadius="4" Background="{Binding AccentBrush}" ToolTip.Tip="Naglašena boja" />
                  <Border Width="18" Height="18" CornerRadius="4" Background="{Binding OutlineOrShadowBrush}" ToolTip.Tip="Kontura/senka" />
                </StackPanel>'''
replacement = anchor + '''
                <Button Classes="cta" Content="PRIMENI NA IZABRANI TITL" Command="{Binding ApplyCommand}" IsVisible="{Binding CanApply}" />'''
if replacement not in text:
    if anchor not in text:
        raise SystemExit("gallery card anchor not found")
    text = text.replace(anchor, replacement, 1)
gallery.write_text(text, encoding="utf-8")

print("caption style gallery production integration materialized")
