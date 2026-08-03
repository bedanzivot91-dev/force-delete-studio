using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NPVideoStudio.AI;
using NPVideoStudio.Domain;
using NPVideoStudio.App.Services;
using Serilog;

namespace NPVideoStudio.App.ViewModels;

/// <summary>
/// Central caption editor (spec Phase 6): word-level transcript editing (split/merge/add/delete/undo/
/// redo/find-replace/time-nudge with optional ripple/Latin↔Cyrillic) and import/export across SRT/VTT/
/// ASS/TXT/JSON/LRC. All editing logic lives in <see cref="CaptionEditSession"/> (pure/testable) - this
/// ViewModel only wires it to the UI and to file I/O.
/// </summary>
public sealed partial class CaptionEditorViewModel : ViewModelBase
{
    private readonly IStorageService _storageService;
    private readonly ILogger _logger;
    private CaptionEditSession? _session;

    public ObservableCollection<CaptionWordItemViewModel> Words { get; } = new();

    public IReadOnlyList<CaptionFileFormat> AvailableFormats { get; } = Enum.GetValues<CaptionFileFormat>();

    [ObservableProperty]
    private CaptionFileFormat _selectedFormat = CaptionFileFormat.Srt;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private bool _hasDocument;

    [ObservableProperty]
    private string _findText = string.Empty;

    [ObservableProperty]
    private string _replaceText = string.Empty;

    public CaptionEditorViewModel(IStorageService storageService, ILogger logger)
    {
        _storageService = storageService;
        _logger = logger.ForContext("SourceContext", nameof(CaptionEditorViewModel));
    }

    /// <summary>Lets other tools (e.g. "Generiši titlove") hand off their result straight into the editor.</summary>
    public void LoadWords(IEnumerable<CaptionWord> words, string? sourceLabel = null)
    {
        _session = new CaptionEditSession(words);
        HasDocument = true;
        RefreshFromSession();
        StatusMessage = sourceLabel is null ? null : $"Učitano iz: {sourceLabel}";
    }

    [RelayCommand]
    private void NewDocument()
    {
        _session = new CaptionEditSession(Array.Empty<CaptionWord>());
        HasDocument = true;
        RefreshFromSession();
        StatusMessage = "Nov, prazan dokument.";
    }

    [RelayCommand]
    private async Task OpenAsync()
    {
        var filters = new (string Name, string[] Extensions)[]
        {
            ("SRT titl", new[] { "srt" }),
            ("WebVTT titl", new[] { "vtt" }),
            ("LRC tekst pesme", new[] { "lrc" }),
            ("JSON (puna vernost)", new[] { "json" })
        };

        var files = await _storageService.PickFilesAsync("Otvori fajl titlova", filters, allowMultiple: false);
        if (files.Count == 0)
        {
            return;
        }

        try
        {
            var content = await File.ReadAllTextAsync(files[0]);
            var extension = Path.GetExtension(files[0]).TrimStart('.').ToLowerInvariant();
            var words = extension switch
            {
                "srt" => CaptionFormatConverter.FromSrt(content),
                "vtt" => CaptionFormatConverter.FromVtt(content),
                "lrc" => CaptionFormatConverter.FromLrc(content),
                "json" => CaptionFormatConverter.FromJson(content),
                _ => throw new InvalidOperationException($"Nepodržan format za uvoz: .{extension}")
            };

            _session = new CaptionEditSession(words);
            HasDocument = true;
            RefreshFromSession();
            StatusMessage = $"Učitano {words.Count} reč(i) iz {Path.GetFileName(files[0])}.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Otvaranje nije uspelo: {ex.Message}";
            _logger.Error(ex, "Otvaranje fajla titlova nije uspelo");
        }
    }

    [RelayCommand(CanExecute = nameof(HasDocument))]
    private async Task SaveAsAsync()
    {
        if (_session is null)
        {
            return;
        }

        var (extension, filterName) = SelectedFormat switch
        {
            CaptionFileFormat.Srt => ("srt", "SRT titl"),
            CaptionFileFormat.Vtt => ("vtt", "WebVTT titl"),
            CaptionFileFormat.Ass => ("ass", "ASS titl"),
            CaptionFileFormat.Txt => ("txt", "Običan tekst"),
            CaptionFileFormat.Json => ("json", "JSON (puna vernost)"),
            CaptionFileFormat.Lrc => ("lrc", "LRC tekst pesme"),
            _ => ("srt", "SRT titl")
        };

        var path = await _storageService.PickSaveFileAsync("Sačuvaj titlove", $"titlovi.{extension}",
            new[] { (filterName, new[] { extension }) });
        if (path is null)
        {
            return;
        }

        try
        {
            var content = SelectedFormat switch
            {
                CaptionFileFormat.Srt => CaptionFormatConverter.ToSrt(_session.Words),
                CaptionFileFormat.Vtt => CaptionFormatConverter.ToVtt(_session.Words),
                CaptionFileFormat.Ass => CaptionFormatConverter.ToAss(_session.Words),
                CaptionFileFormat.Txt => CaptionFormatConverter.ToPlainText(_session.Words),
                CaptionFileFormat.Json => CaptionFormatConverter.ToJson(_session.Words),
                CaptionFileFormat.Lrc => CaptionFormatConverter.ToLrc(_session.Words),
                _ => string.Empty
            };

            await File.WriteAllTextAsync(path, content);
            StatusMessage = $"Sačuvano: {Path.GetFileName(path)}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Čuvanje nije uspelo: {ex.Message}";
            _logger.Error(ex, "Čuvanje fajla titlova nije uspelo");
        }
    }

    [RelayCommand(CanExecute = nameof(HasDocument))]
    private void AddWord()
    {
        if (_session is null)
        {
            return;
        }

        var lastEnd = _session.Words.Count > 0 ? _session.Words[^1].End : TimeSpan.Zero;
        _session.InsertWord(_session.Words.Count, new CaptionWord
        {
            OriginalText = "nova reč",
            Start = lastEnd,
            End = lastEnd + TimeSpan.FromSeconds(1),
            Source = CaptionWordSource.Manual,
            LineBreakAfter = true
        });
        RefreshFromSession();
    }

    [RelayCommand]
    private void Undo()
    {
        _session?.Undo();
        RefreshFromSession();
    }

    private bool CanUndo => _session?.CanUndo == true;

    [RelayCommand]
    private void Redo()
    {
        _session?.Redo();
        RefreshFromSession();
    }

    private bool CanRedo => _session?.CanRedo == true;

    [RelayCommand(CanExecute = nameof(HasDocument))]
    private void FindReplace()
    {
        if (_session is null || string.IsNullOrEmpty(FindText))
        {
            return;
        }

        var count = _session.FindAndReplace(FindText, ReplaceText);
        RefreshFromSession();
        StatusMessage = count > 0 ? $"Zamenjeno u {count} reč(i)." : "Nema podudaranja.";
    }

    [RelayCommand(CanExecute = nameof(HasDocument))]
    private void ConvertToCyrillic()
    {
        _session?.ConvertScript(SerbianScriptConverter.ToCyrillic);
        RefreshFromSession();
    }

    [RelayCommand(CanExecute = nameof(HasDocument))]
    private void ConvertToLatin()
    {
        _session?.ConvertScript(SerbianScriptConverter.ToLatin);
        RefreshFromSession();
    }

    [RelayCommand(CanExecute = nameof(HasDocument))]
    private void DeleteSelected()
    {
        if (_session is null)
        {
            return;
        }

        var ids = Words.Where(w => w.IsSelected).Select(w => w.Word.Id).ToList();
        if (ids.Count == 0)
        {
            StatusMessage = "Nijedna reč nije izabrana.";
            return;
        }

        _session.DeleteWords(ids);
        RefreshFromSession();
    }

    private void RefreshFromSession()
    {
        Words.Clear();
        if (_session is not null)
        {
            foreach (var word in _session.Words)
            {
                Words.Add(CreateItem(word));
            }
        }

        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
    }

    private CaptionWordItemViewModel CreateItem(CaptionWord word)
    {
        var split = new RelayCommand(() =>
        {
            _session!.SplitWord(word.Id, 0.5);
            RefreshFromSession();
        });
        var mergeWithNext = new RelayCommand(() =>
        {
            _session!.MergeWithNext(word.Id);
            RefreshFromSession();
        });
        var delete = new RelayCommand(() =>
        {
            _session!.DeleteWords(new[] { word.Id });
            RefreshFromSession();
        });
        var nudgeEarlier = new RelayCommand(() =>
        {
            _session!.NudgeTiming(word.Id, TimeSpan.FromSeconds(-0.1), ripple: true);
            RefreshFromSession();
        });
        var nudgeLater = new RelayCommand(() =>
        {
            _session!.NudgeTiming(word.Id, TimeSpan.FromSeconds(0.1), ripple: true);
            RefreshFromSession();
        });
        var toggleLineBreak = new RelayCommand(() =>
        {
            word.LineBreakAfter = !word.LineBreakAfter;
            RefreshFromSession();
        });

        return new CaptionWordItemViewModel(word, split, mergeWithNext, delete, nudgeEarlier, nudgeLater, toggleLineBreak);
    }
}
