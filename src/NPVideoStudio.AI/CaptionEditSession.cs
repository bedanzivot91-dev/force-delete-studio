using NPVideoStudio.Domain;

namespace NPVideoStudio.AI;

/// <summary>
/// Pure, testable editing operations over a caption word list, with full undo/redo (spec Phase 6: split/
/// merge/add/delete/undo/redo/time-nudge/ripple-edit/find-replace). Every mutating method snapshots the
/// pre-edit state before changing anything, so undo/redo is just popping between two stacks of whole-
/// list snapshots - simple and unambiguous to reason about, unlike a per-field command-pattern that
/// would need its own inverse for every operation. Holds no file/UI state - <see cref="CaptionFormatConverter"/>
/// handles import/export, callers handle persistence.
/// </summary>
public sealed class CaptionEditSession
{
    private readonly List<List<CaptionWord>> _undoStack = new();
    private readonly List<List<CaptionWord>> _redoStack = new();
    private List<CaptionWord> _words;

    public CaptionEditSession(IEnumerable<CaptionWord> words)
    {
        _words = words.OrderBy(w => w.Start).ToList();
    }

    public IReadOnlyList<CaptionWord> Words => _words;
    public bool CanUndo => _undoStack.Count > 0;
    public bool CanRedo => _redoStack.Count > 0;

    public void Undo()
    {
        if (!CanUndo)
        {
            return;
        }

        _redoStack.Add(CloneWords(_words));
        _words = _undoStack[^1];
        _undoStack.RemoveAt(_undoStack.Count - 1);
    }

    public void Redo()
    {
        if (!CanRedo)
        {
            return;
        }

        _undoStack.Add(CloneWords(_words));
        _words = _redoStack[^1];
        _redoStack.RemoveAt(_redoStack.Count - 1);
    }

    public void InsertWord(int index, CaptionWord word)
    {
        SaveUndoSnapshot();
        _words.Insert(Math.Clamp(index, 0, _words.Count), word);
    }

    public void DeleteWords(IEnumerable<Guid> ids)
    {
        var idSet = ids.ToHashSet();
        if (!_words.Any(w => idSet.Contains(w.Id)))
        {
            return;
        }

        SaveUndoSnapshot();
        _words.RemoveAll(w => idSet.Contains(w.Id));
    }

    /// <summary>Splits one word into two at a fractional position (0..1) of its own text and time span - e.g. un-merging a misrecognized run-together word.</summary>
    public void SplitWord(Guid id, double textSplitRatio)
    {
        var index = _words.FindIndex(w => w.Id == id);
        if (index < 0)
        {
            return;
        }

        var word = _words[index];
        var ratio = Math.Clamp(textSplitRatio, 0.05, 0.95);
        var splitPos = Math.Clamp((int)Math.Round(word.OriginalText.Length * ratio), 1, Math.Max(1, word.OriginalText.Length - 1));

        var firstText = word.OriginalText[..splitPos].TrimEnd();
        var secondText = word.OriginalText[splitPos..].TrimStart();
        if (firstText.Length == 0 || secondText.Length == 0)
        {
            return;
        }

        var splitTime = word.Start + (word.End - word.Start) * ratio;

        SaveUndoSnapshot();
        var first = Clone(word);
        first.Id = Guid.NewGuid();
        first.OriginalText = firstText;
        first.NormalizedText = LyricMatcher.Normalize(firstText);
        first.End = splitTime;
        first.LineBreakAfter = false;

        var second = Clone(word);
        second.Id = Guid.NewGuid();
        second.OriginalText = secondText;
        second.NormalizedText = LyricMatcher.Normalize(secondText);
        second.Start = splitTime;

        _words.RemoveAt(index);
        _words.Insert(index, second);
        _words.Insert(index, first);
    }

    /// <summary>Merges a word with the next word into one, keeping the combined time span.</summary>
    public void MergeWithNext(Guid id)
    {
        var index = _words.FindIndex(w => w.Id == id);
        if (index < 0 || index + 1 >= _words.Count)
        {
            return;
        }

        SaveUndoSnapshot();
        var first = _words[index];
        var second = _words[index + 1];
        var merged = Clone(first);
        merged.Id = Guid.NewGuid();
        merged.OriginalText = $"{first.OriginalText} {second.OriginalText}";
        merged.NormalizedText = LyricMatcher.Normalize(merged.OriginalText);
        merged.End = second.End;
        merged.Confidence = Math.Min(first.Confidence, second.Confidence);
        merged.LineBreakAfter = second.LineBreakAfter;

        _words.RemoveRange(index, 2);
        _words.Insert(index, merged);
    }

    /// <summary>
    /// Shifts one word's timing by <paramref name="delta"/>. With <paramref name="ripple"/>, every
    /// following word shifts by the same delta too, preserving relative gaps (spec: "ripple-edit") -
    /// without it, only the targeted word moves and its neighbors' timing is left alone.
    /// </summary>
    public void NudgeTiming(Guid id, TimeSpan delta, bool ripple)
    {
        var index = _words.FindIndex(w => w.Id == id);
        if (index < 0)
        {
            return;
        }

        SaveUndoSnapshot();
        for (var i = index; i < _words.Count; i++)
        {
            if (!ripple && i > index)
            {
                break;
            }

            var word = _words[i];
            var newStart = word.Start + delta;
            var newEnd = word.End + delta;
            if (newStart < TimeSpan.Zero)
            {
                newEnd -= newStart;
                newStart = TimeSpan.Zero;
            }

            word.Start = newStart;
            word.End = newEnd;
        }
    }

    /// <summary>
    /// Runs a text transform (e.g. <see cref="SerbianScriptConverter.ToCyrillic"/>/<see cref="SerbianScriptConverter.ToLatin"/>)
    /// over every word's text - an explicit, undoable editing action the user chooses, not a silent
    /// background normalization (spec: "don't let normalization overwrite the original text" - the
    /// "original" here is whatever was loaded into this session; converting is something the user does
    /// on purpose to the working copy, and can undo).
    /// </summary>
    public void ConvertScript(Func<string, string> converter)
    {
        if (_words.Count == 0)
        {
            return;
        }

        SaveUndoSnapshot();
        foreach (var word in _words)
        {
            word.OriginalText = converter(word.OriginalText);
            word.NormalizedText = LyricMatcher.Normalize(word.OriginalText);
        }
    }

    public int FindAndReplace(string find, string replace, bool caseSensitive = false)
    {
        if (string.IsNullOrEmpty(find))
        {
            return 0;
        }

        var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var affected = _words.Where(w => w.OriginalText.Contains(find, comparison)).ToList();
        if (affected.Count == 0)
        {
            return 0;
        }

        SaveUndoSnapshot();
        foreach (var word in affected)
        {
            word.OriginalText = ReplaceAll(word.OriginalText, find, replace, comparison);
            word.NormalizedText = LyricMatcher.Normalize(word.OriginalText);
        }

        return affected.Count;
    }

    private void SaveUndoSnapshot()
    {
        _undoStack.Add(CloneWords(_words));
        _redoStack.Clear();
    }

    private static List<CaptionWord> CloneWords(IEnumerable<CaptionWord> words) => words.Select(Clone).ToList();

    private static CaptionWord Clone(CaptionWord word) => new()
    {
        Id = word.Id,
        OriginalText = word.OriginalText,
        NormalizedText = word.NormalizedText,
        Start = word.Start,
        End = word.End,
        Confidence = word.Confidence,
        Source = word.Source,
        VerificationStatus = word.VerificationStatus,
        LineBreakAfter = word.LineBreakAfter
    };

    private static string ReplaceAll(string source, string find, string replace, StringComparison comparison)
    {
        var builder = new System.Text.StringBuilder();
        var pos = 0;
        while (true)
        {
            var idx = source.IndexOf(find, pos, comparison);
            if (idx < 0)
            {
                builder.Append(source, pos, source.Length - pos);
                break;
            }

            builder.Append(source, pos, idx - pos).Append(replace);
            pos = idx + find.Length;
        }

        return builder.ToString();
    }
}
