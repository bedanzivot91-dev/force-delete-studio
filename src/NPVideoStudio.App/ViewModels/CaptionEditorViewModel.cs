using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NPVideoStudio.AI;
using NPVideoStudio.Domain;
using NPVideoStudio.App.Services;
using NPVideoStudio.Core.Services;
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
    private readonly IAiWorkerClient? _aiWorkerClient;
    private CaptionEditSession? _session;

    public ObservableCollection<CaptionWordItemViewModel> Words { get; } = new();

    public IReadOnlyList<CaptionFileFormat> AvailableFormats { get; } = Enum.GetValues<CaptionFileFormat>();
    public IReadOnlyList<CaptionLanguageOption> AvailableLanguages { get; } =
    [
        new("sr", "Srpski"), new("en", "Engleski"), new("de", "Nemački"),
        new("fr", "Francuski"), new("es", "Španski"), new("it", "Italijanski")
    ];

    [ObservableProperty]
    private CaptionFileFormat _selectedFormat = CaptionFileFormat.Srt;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(TranslateDocumentCommand))]
    private bool _hasDocument;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(TranslateDocumentCommand))]
    private CaptionLanguageOption _selectedSourceLanguage = new("sr", "Srpski");

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(TranslateDocumentCommand))]
    private CaptionLanguageOption _selectedTargetLanguage = new("en", "Engleski");

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(TranslateDocumentCommand))]
    private bool _isTranslating;

    [ObservableProperty]
    private string _findText = string.Empty;

    [ObservableProperty]
    private string _replaceText = string.Empty;

    public CaptionEditorViewModel(IStorageService storageService, ILogger logger, IAiWorkerClient? aiWorkerClient = null)
    {
        _storageService = storageService;
        _logger = logger.ForContext("SourceContext", nameof(CaptionEditorViewModel));
        _aiWorkerClient = aiWorkerClient;
    }

    private bool CanTranslateDocument() =>
        HasDocument && !IsTranslating && _session?.Words.Count > 0 && _aiWorkerClient is not null &&
        SelectedSourceLanguage.Code != SelectedTargetLanguage.Code;

    [RelayCommand(CanExecute = nameof(CanTranslateDocument))]
    private async Task TranslateDocumentAsync()
    {
        if (_session is null || _aiWorkerClient is null)
        {
            return;
        }

        IsTranslating = true;
        StatusMessage = $"Pokrećem lokalni prevod {SelectedSourceLanguage.Name} → {SelectedTargetLanguage.Name}...";
        try
        {
            IReadOnlyList<string>? translatedTexts = null;
            await foreach (var evt in _aiWorkerClient.RunAsync(new AiWorkerRequest
            {
                JobKind = AiWorkerJobKind.SubtitleTranslation,
                Profile = AiProcessingProfile.Fast,
                Texts = _session.Words.Select(word => word.OriginalText).ToArray(),
                SourceLanguage = SelectedSourceLanguage.Code,
                TargetLanguage = SelectedTargetLanguage.Code
            }))
            {
                if (evt.Type == AiWorkerEventType.Progress && !string.IsNullOrWhiteSpace(evt.Message))
                {
                    StatusMessage = evt.Message;
                }
                else if (evt.Type == AiWorkerEventType.Result)
                {
                    translatedTexts = evt.TranslatedTexts;
                }
                else if (evt.Type == AiWorkerEventType.Error)
                {
                    throw new InvalidOperationException(evt.Message ?? "Lokalni prevodilac nije vratio rezultat.");
                }
            }

            if (translatedTexts is null || translatedTexts.Count != _session.Words.Count)
            {
                throw new InvalidOperationException("Broj prevedenih titlova ne odgovara otvorenom dokumentu.");
            }

            var translated = _session.Words.Select((word, index) => new CaptionWord
            {
                Id = word.Id,
                OriginalText = translatedTexts[index],
                NormalizedText = LyricMatcher.Normalize(translatedTexts[index]),
                Start = word.Start,
                End = word.End,
                Confidence = word.Confidence,
                Source = word.Source,
                VerificationStatus = word.VerificationStatus,
                LineBreakAfter = word.LineBreakAfter
            }).ToArray();

            _session.ReplaceAll(translated);
            RefreshFromSession();
            StatusMessage = $"Prevedeno je {translated.Length} titlova. Undo vraća originalni tekst.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Prevod nije uspeo: {ex.Message}";
            _logger.Error(ex, "Lokalni prevod titlova nije uspeo");
        }
        finally
        {
            IsTranslating = false;
        }
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

    /// <summary>Every readability/structure problem found by the last quality check, in Serbian, ready to
    /// list in the UI. Empty after a clean check - the UI can treat "empty" as "nothing to fix".</summary>
    public ObservableCollection<string> QualityProblems { get; } = new();

    [ObservableProperty]
    private bool _hasRunQualityCheck;

    /// <summary>
    /// Runs the caption-quality rules ported from the user's other project (PROGRAM-ZA-TEKST-U-VIDEO)
    /// over the whole open document: structural problems from <see cref="CaptionTrackValidator"/>
    /// (overlaps, empty text, impossible ranges) plus per-line readability from
    /// <see cref="CaptionReadability"/> (too fast to read, line too wide, on screen too briefly).
    /// Read-only - it never changes the document, so the user sees the full picture before deciding
    /// whether to let <see cref="FixQualityProblems"/> touch anything.
    /// </summary>
    [RelayCommand(CanExecute = nameof(HasDocument))]
    private void CheckQuality()
    {
        QualityProblems.Clear();
        HasRunQualityCheck = true;

        var captions = Words.Select(w => w.Word).ToList();

        var report = CaptionTrackValidator.Validate(captions, minimumGap: TimeSpan.FromMilliseconds(120));
        foreach (var problem in report.Problems)
        {
            QualityProblems.Add($"Titl #{problem.Index + 1}: {problem.Message}");
        }

        for (var i = 0; i < captions.Count; i++)
        {
            var caption = captions[i];
            foreach (var warning in CaptionReadability.BuildWarnings(caption.OriginalText, caption.End - caption.Start))
            {
                QualityProblems.Add($"Titl #{i + 1}: {warning}");
            }
        }

        StatusMessage = QualityProblems.Count == 0
            ? $"Provera kvaliteta: sve je u redu ({captions.Count} titlova)."
            : $"Provera kvaliteta: pronađeno {QualityProblems.Count} stvari za popravku.";

        _logger.Information(
            "Provera kvaliteta titlova: {ProblemCount} problema na {CaptionCount} titlova",
            QualityProblems.Count, captions.Count);
    }

    /// <summary>
    /// Repairs what can be repaired automatically - overlaps, captions running past the media, captions
    /// shorter than the minimum - via <see cref="CaptionTrackValidator.Normalize"/>. Deliberately does NOT
    /// touch anything that would change the words themselves (splitting an over-long line, rewriting text):
    /// timing is safe to correct mechanically, wording is the user's call.
    /// </summary>
    [RelayCommand(CanExecute = nameof(HasDocument))]
    private void FixQualityProblems()
    {
        if (_session is null)
        {
            return;
        }

        var before = Words.Select(w => w.Word).ToList();
        var repaired = CaptionTrackValidator.Normalize(before);

        _session.ReplaceAll(repaired);
        RefreshFromSession();
        CheckQuality();

        StatusMessage = $"Automatski ispravljeno vreme titlova ({repaired.Count} titlova). " +
                        "Poništavanje (Undo) vraća prethodno stanje.";
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
        TranslateDocumentCommand.NotifyCanExecuteChanged();
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

public sealed record CaptionLanguageOption(string Code, string Name);
