from pathlib import Path


def replace_once(path: str, old: str, new: str) -> None:
    p = Path(path)
    text = p.read_text(encoding="utf-8")
    if old not in text:
        raise RuntimeError(f"Anchor not found in {path}: {old[:120]!r}")
    if text.count(old) != 1:
        raise RuntimeError(f"Anchor is not unique in {path}: {text.count(old)} matches")
    p.write_text(text.replace(old, new, 1), encoding="utf-8")


# TimelineEditSession: undo-safe real installed font selection, batch copy, deep clone.
path = "src/NPVideoStudio.AI/TimelineEditSession.cs"
replace_once(path,
'''    public void SetTextStyle(string clipId, CaptionFontChoice fontChoice, int fontSizePx, string textColor, CaptionTextPosition position)\n    {\n        var (_, clip) = FindClipWithTrack(clipId);\n        if (clip is null)\n        {\n            return;\n        }\n\n        SaveSnapshot();\n        var liveClip = FindClipWithTrack(clipId).Clip!;\n        liveClip.FontChoice = fontChoice;\n        liveClip.FontSizePx = Math.Clamp(fontSizePx, 8, 200);\n        liveClip.TextColor = textColor;\n        liveClip.TextPosition = position;\n    }\n''',
'''    public void SetTextStyle(string clipId, CaptionFontChoice fontChoice, int fontSizePx, string textColor, CaptionTextPosition position)\n    {\n        var (_, clip) = FindClipWithTrack(clipId);\n        if (clip is null)\n        {\n            return;\n        }\n\n        SaveSnapshot();\n        var liveClip = FindClipWithTrack(clipId).Clip!;\n        liveClip.FontChoice = fontChoice;\n        liveClip.FontSizePx = Math.Clamp(fontSizePx, 8, 200);\n        liveClip.TextColor = textColor;\n        liveClip.TextPosition = position;\n    }\n\n    /// <summary>Selects either a legacy preset or a real installed font file. This is its own undo step\n    /// and deliberately keeps family + path so projects can be moved to another Windows installation.</summary>\n    public void SetTextFont(string clipId, CaptionFontChoice legacyChoice, string? installedFamilyName, string? installedFilePath)\n    {\n        var (_, clip) = FindClipWithTrack(clipId);\n        if (clip is null || clip.TextContent is null)\n        {\n            return;\n        }\n\n        var family = string.IsNullOrWhiteSpace(installedFamilyName) ? null : installedFamilyName.Trim();\n        var path = string.IsNullOrWhiteSpace(installedFilePath) ? null : installedFilePath.Trim();\n        if (clip.FontChoice == legacyChoice &&\n            string.Equals(clip.TextFontFamilyName, family, StringComparison.Ordinal) &&\n            string.Equals(clip.TextFontFilePath, path, StringComparison.Ordinal))\n        {\n            return;\n        }\n\n        SaveSnapshot();\n        var liveClip = FindClipWithTrack(clipId).Clip!;\n        liveClip.FontChoice = legacyChoice;\n        liveClip.TextFontFamilyName = family;\n        liveClip.TextFontFilePath = path;\n    }\n''')
replace_once(path,
'''            target.FontChoice = source.FontChoice;\n            target.FontSizePx = source.FontSizePx;''',
'''            target.FontChoice = source.FontChoice;\n            target.TextFontFamilyName = source.TextFontFamilyName;\n            target.TextFontFilePath = source.TextFontFilePath;\n            target.FontSizePx = source.FontSizePx;''')
replace_once(path,
'''        FontChoice = clip.FontChoice,\n        FontSizePx = clip.FontSizePx,''',
'''        FontChoice = clip.FontChoice,\n        TextFontFamilyName = clip.TextFontFamilyName,\n        TextFontFilePath = clip.TextFontFilePath,\n        FontSizePx = clip.FontSizePx,''')

# TimelineClipItemViewModel: expose real installed Serbian-capable fonts through the existing ComboBox.
path = "src/NPVideoStudio.App/ViewModels/TimelineClipItemViewModel.cs"
replace_once(path,
'''using CommunityToolkit.Mvvm.Input;\nusing NPVideoStudio.Domain;''',
'''using CommunityToolkit.Mvvm.Input;\nusing NPVideoStudio.Domain;\nusing NPVideoStudio.Media;''')
replace_once(path,
'''    private readonly Action<string, CaptionFontChoice, int, string, CaptionTextPosition>? _onTextStyleChanged;''',
'''    private readonly Action<string, CaptionFontChoice, int, string, CaptionTextPosition>? _onTextStyleChanged;\n    private readonly Action<string, CaptionFontChoice, string?, string?>? _onTextFontChanged;''')
replace_once(path,
'''    public CaptionFontChoice FontChoice\n    {\n        get => Clip.FontChoice;\n        set\n        {\n            if (Clip.FontChoice == value) return;\n            _onTextStyleChanged?.Invoke(Clip.Id, value, FontSizePx, TextColor, TextPosition);\n        }\n    }''',
'''    private static readonly Lazy<IReadOnlyList<object>> FontPickerChoices = new(() =>\n        Enum.GetValues<CaptionFontChoice>().Cast<object>()\n            .Concat(SystemFontCatalog.ListFontsUsableForSerbian().Cast<object>())\n            .ToList());\n\n    /// <summary>The same property name keeps the existing inspector binding intact, but it now accepts\n    /// both legacy enum presets and real InstalledFont entries from SystemFontCatalog.</summary>\n    public object FontChoice\n    {\n        get\n        {\n            if (!string.IsNullOrWhiteSpace(Clip.TextFontFilePath))\n            {\n                var exact = AvailableFontChoices.OfType<InstalledFont>().FirstOrDefault(f =>\n                    string.Equals(f.FilePath, Clip.TextFontFilePath, StringComparison.OrdinalIgnoreCase));\n                if (exact is not null) return exact;\n            }\n\n            if (!string.IsNullOrWhiteSpace(Clip.TextFontFamilyName))\n            {\n                var family = AvailableFontChoices.OfType<InstalledFont>().FirstOrDefault(f =>\n                    string.Equals(f.FamilyName, Clip.TextFontFamilyName, StringComparison.OrdinalIgnoreCase));\n                if (family is not null) return family;\n            }\n\n            return Clip.FontChoice;\n        }\n        set\n        {\n            if (value is CaptionFontChoice legacy)\n            {\n                if (Clip.FontChoice == legacy && Clip.TextFontFamilyName is null && Clip.TextFontFilePath is null) return;\n                if (_onTextFontChanged is not null)\n                    _onTextFontChanged(Clip.Id, legacy, null, null);\n                else\n                    _onTextStyleChanged?.Invoke(Clip.Id, legacy, FontSizePx, TextColor, TextPosition);\n                return;\n            }\n\n            if (value is InstalledFont installed)\n            {\n                if (string.Equals(Clip.TextFontFilePath, installed.FilePath, StringComparison.OrdinalIgnoreCase)) return;\n                _onTextFontChanged?.Invoke(Clip.Id, CaptionFontChoice.Default, installed.FamilyName, installed.FilePath);\n            }\n        }\n    }''')
replace_once(path,
'''            _onTextStyleChanged?.Invoke(Clip.Id, FontChoice, value, TextColor, TextPosition);''',
'''            _onTextStyleChanged?.Invoke(Clip.Id, Clip.FontChoice, value, TextColor, TextPosition);''')
replace_once(path,
'''            _onTextStyleChanged?.Invoke(Clip.Id, FontChoice, FontSizePx, value, TextPosition);''',
'''            _onTextStyleChanged?.Invoke(Clip.Id, Clip.FontChoice, FontSizePx, value, TextPosition);''')
replace_once(path,
'''    public IReadOnlyList<CaptionFontChoice> AvailableFontChoices { get; } = Enum.GetValues<CaptionFontChoice>();''',
'''    public IReadOnlyList<object> AvailableFontChoices => FontPickerChoices.Value;''')
replace_once(path,
'''        Action<string, ClipKeyframeProperty, double>? onKeyframeRemove = null)''',
'''        Action<string, ClipKeyframeProperty, double>? onKeyframeRemove = null,\n        Action<string, CaptionFontChoice, string?, string?>? onTextFontChanged = null)''')
replace_once(path,
'''        _onTextStyleChanged = onTextStyleChanged;\n        _onTransitionChanged = onTransitionChanged;''',
'''        _onTextStyleChanged = onTextStyleChanged;\n        _onTextFontChanged = onTextFontChanged;\n        _onTransitionChanged = onTransitionChanged;''')

# TimelineViewModel: route selection through TimelineEditSession so undo/redo is correct.
path = "src/NPVideoStudio.App/ViewModels/TimelineViewModel.cs"
replace_once(path,
'''        void OnTextStyleChanged(string clipId, CaptionFontChoice font, int size, string color, CaptionTextPosition position)\n        {\n            _session.SetTextStyle(clipId, font, size, color, position);\n            RefreshFromSession();\n        }''',
'''        void OnTextStyleChanged(string clipId, CaptionFontChoice font, int size, string color, CaptionTextPosition position)\n        {\n            _session.SetTextStyle(clipId, font, size, color, position);\n            RefreshFromSession();\n        }\n        void OnTextFontChanged(string clipId, CaptionFontChoice legacy, string? family, string? filePath)\n        {\n            _session.SetTextFont(clipId, legacy, family, filePath);\n            RefreshFromSession();\n        }''')
replace_once(path,
'''            _getPlayhead, OnKeyframeUpsert, OnKeyframeRemove)''',
'''            _getPlayhead, OnKeyframeUpsert, OnKeyframeRemove, OnTextFontChanged)''')

# Renderer: selected installed font reaches final drawtext and range clones.
path = "src/NPVideoStudio.Media/FfmpegFilterGraphBuilder.cs"
replace_once(path,
'''            var fontFilePath = CaptionFontResolver.ResolveFontFilePath(clip.FontChoice, clip.IsTextBold, clip.IsTextItalic);''',
'''            var fontFilePath = CaptionFontResolver.ResolveFontFilePath(clip);''')
replace_once(path,
'''        FontChoice = clip.FontChoice,\n        FontSizePx = clip.FontSizePx,''',
'''        FontChoice = clip.FontChoice,\n        TextFontFamilyName = clip.TextFontFamilyName,\n        TextFontFilePath = clip.TextFontFilePath,\n        FontSizePx = clip.FontSizePx,''')

# Font picker display label in the existing ComboBox.
path = "src/NPVideoStudio.Media/SystemFontCatalog.cs"
replace_once(path,
'''    public string DisplayLabel => (IsBold, IsItalic) switch\n    {\n        (true, true) => $"{FamilyName} (podebljano, kurziv)",\n        (true, false) => $"{FamilyName} (podebljano)",\n        (false, true) => $"{FamilyName} (kurziv)",\n        _ => FamilyName\n    };''',
'''    public string DisplayLabel => (IsBold, IsItalic) switch\n    {\n        (true, true) => $"{FamilyName} (podebljano, kurziv)",\n        (true, false) => $"{FamilyName} (podebljano)",\n        (false, true) => $"{FamilyName} (kurziv)",\n        _ => FamilyName\n    };\n\n    public override string ToString() => DisplayLabel;''')

print("System font production wiring materialized.")
