$ErrorActionPreference = 'Stop'

function Read-Utf8([string]$Path) {
    return [System.IO.File]::ReadAllText((Resolve-Path $Path), [System.Text.Encoding]::UTF8)
}
function Write-Utf8([string]$Path, [string]$Text) {
    [System.IO.File]::WriteAllText((Resolve-Path $Path), $Text, (New-Object System.Text.UTF8Encoding($false)))
}
function Replace-Once([string]$Text, [string]$Old, [string]$New, [string]$Label) {
    $i = $Text.IndexOf($Old, [StringComparison]::Ordinal)
    if ($i -lt 0) { throw "Anchor not found: $Label" }
    if ($Text.IndexOf($Old, $i + $Old.Length, [StringComparison]::Ordinal) -ge 0) { throw "Anchor not unique: $Label" }
    return $Text.Substring(0, $i) + $New + $Text.Substring($i + $Old.Length)
}

# -----------------------------------------------------------------------------
# Persist keyframes on every clip.
# -----------------------------------------------------------------------------
$path = 'src/NPVideoStudio.Domain/Timeline.cs'
$t = Read-Utf8 $path
$old = @'
    public ClipBlendMode BlendMode { get; set; } = ClipBlendMode.Normal;
}
'@
$new = @'
    public ClipBlendMode BlendMode { get; set; } = ClipBlendMode.Normal;

    // --- Keyframe animation ----------------------------------------------------------------------
    // Times are local to this rendered clip (0 = first visible frame), not absolute project time.
    // Moving the clip therefore never changes the animation authored inside it.
    public List<ClipKeyframe> Keyframes { get; set; } = new();
}
'@
$t = Replace-Once $t $old $new 'TimelineClip.Keyframes'
Write-Utf8 $path $t

# -----------------------------------------------------------------------------
# Undo-safe keyframe editing + correct split/trim semantics + deep cloning.
# -----------------------------------------------------------------------------
$path = 'src/NPVideoStudio.AI/TimelineEditSession.cs'
$t = Read-Utf8 $path

$anchor = @'
    /// <summary>
    /// Sets a clip's picture look and playback speed (rendered by
'@
$insert = @'
    /// <summary>Adds or replaces one keyframe at a clip-local time. This is a normal session edit:
    /// it is persisted, undoable and never mutates the caller's pre-snapshot object first.</summary>
    public void UpsertKeyframe(
        string clipId,
        ClipKeyframeProperty property,
        double localTimeSeconds,
        double value,
        ClipKeyframeEasing easing)
    {
        var (_, clip) = FindClipWithTrack(clipId);
        if (clip is null)
        {
            return;
        }

        var time = Math.Clamp(localTimeSeconds, 0, Math.Max(0, clip.TimelineDurationSeconds));
        var clampedValue = ClipKeyframeEvaluator.ClampValue(property, value);
        var existing = clip.Keyframes.FirstOrDefault(k =>
            k.Property == property && Math.Abs(k.TimeSeconds - time) <= 0.001);

        if (existing is not null &&
            Math.Abs(existing.Value - clampedValue) <= 1e-9 &&
            existing.Easing == easing)
        {
            return;
        }

        SaveSnapshot();
        var liveClip = FindClipWithTrack(clipId).Clip!;
        var liveExisting = liveClip.Keyframes.FirstOrDefault(k =>
            k.Property == property && Math.Abs(k.TimeSeconds - time) <= 0.001);
        if (liveExisting is null)
        {
            liveClip.Keyframes.Add(new ClipKeyframe
            {
                Property = property,
                TimeSeconds = time,
                Value = clampedValue,
                Easing = easing
            });
        }
        else
        {
            liveExisting.TimeSeconds = time;
            liveExisting.Value = clampedValue;
            liveExisting.Easing = easing;
        }

        liveClip.Keyframes = liveClip.Keyframes
            .OrderBy(k => k.Property)
            .ThenBy(k => k.TimeSeconds)
            .ToList();
    }

    public void RemoveKeyframe(
        string clipId,
        ClipKeyframeProperty property,
        double localTimeSeconds,
        double toleranceSeconds = 0.08)
    {
        var (_, clip) = FindClipWithTrack(clipId);
        if (clip is null)
        {
            return;
        }

        var nearest = clip.Keyframes
            .Where(k => k.Property == property)
            .OrderBy(k => Math.Abs(k.TimeSeconds - localTimeSeconds))
            .FirstOrDefault();
        if (nearest is null || Math.Abs(nearest.TimeSeconds - localTimeSeconds) > Math.Max(0.001, toleranceSeconds))
        {
            return;
        }

        SaveSnapshot();
        FindClipWithTrack(clipId).Clip!.Keyframes.RemoveAll(k => k.Id == nearest.Id);
    }

    public void RemoveAllKeyframes(string clipId)
    {
        var (_, clip) = FindClipWithTrack(clipId);
        if (clip is null || clip.Keyframes.Count == 0)
        {
            return;
        }

        SaveSnapshot();
        FindClipWithTrack(clipId).Clip!.Keyframes.Clear();
    }

    /// <summary>
    /// Sets a clip's picture look and playback speed (rendered by
'@
$t = Replace-Once $t $anchor $insert 'keyframe session methods'

# Split keyframes into two clip-local timelines without changing the evaluated value at the cut.
$old = @'
        var second = Clone(liveClip!);
        second.Id = Guid.NewGuid().ToString("N");
        second.SourceTrimInSeconds = splitSourcePoint;
'@
$new = @'
        var second = Clone(liveClip!);
        second.Id = Guid.NewGuid().ToString("N");
        SplitKeyframesAt(liveClip!, second, offsetIntoClip);
        second.SourceTrimInSeconds = splitSourcePoint;
'@
$t = Replace-Once $t $old $new 'split keyframes'

# Trimming the leading edge shifts clip-local keyframe time. Extending the edge shifts existing points right.
$old = @'
        SaveSnapshot();
        var (_, liveClip) = FindClipWithTrack(clipId);
        liveClip!.SourceTrimInSeconds = clamped;
        liveClip.TimelineStartSeconds = Math.Max(0, liveClip.TimelineStartSeconds + delta / (liveClip.IsFreezeFrame ? 1.0 : Math.Clamp(liveClip.SpeedMultiplier, 0.25, 4)));
'@
$new = @'
        SaveSnapshot();
        var (_, liveClip) = FindClipWithTrack(clipId);
        var timelineDelta = delta / (liveClip!.IsFreezeFrame ? 1.0 : Math.Clamp(liveClip.SpeedMultiplier, 0.25, 4));
        TrimKeyframesAtStart(liveClip, timelineDelta);
        liveClip.SourceTrimInSeconds = clamped;
        liveClip.TimelineStartSeconds = Math.Max(0, liveClip.TimelineStartSeconds + timelineDelta);
'@
$t = Replace-Once $t $old $new 'trim-in keyframes'

$old = @'
        SaveSnapshot();
        FindClipWithTrack(clipId).Clip!.SourceTrimOutSeconds = clamped;
'@
$new = @'
        SaveSnapshot();
        var liveClip = FindClipWithTrack(clipId).Clip!;
        var speed = liveClip.IsFreezeFrame ? 1.0 : Math.Clamp(liveClip.SpeedMultiplier, 0.25, 4);
        var newDuration = Math.Max(0, clamped - liveClip.SourceTrimInSeconds) / speed;
        TrimKeyframesAtEnd(liveClip, newDuration);
        liveClip.SourceTrimOutSeconds = clamped;
'@
$t = Replace-Once $t $old $new 'trim-out keyframes'

# Clamp points if changing speed makes the visible clip shorter.
$old = @'
        liveClip.Saturation = Math.Clamp(saturation, 0, 3);
        liveClip.SpeedMultiplier = Math.Clamp(speed, 0.25, 4);
    }
'@
$new = @'
        liveClip.Saturation = Math.Clamp(saturation, 0, 3);
        liveClip.SpeedMultiplier = Math.Clamp(speed, 0.25, 4);
        ClampKeyframesToDuration(liveClip);
    }
'@
$t = Replace-Once $t $old $new 'speed keyframe clamp'

# Helper routines live before track flags.
$anchor = @'
    public void SetTrackLocked(string trackId, bool locked) => SetTrackFlag(trackId, t => t.IsLocked, (t, v) => t.IsLocked = v, locked);
'@
$insert = @'
    private static void SplitKeyframesAt(TimelineClip first, TimelineClip second, double splitSeconds)
    {
        if (first.Keyframes.Count == 0)
        {
            return;
        }

        var original = first.Keyframes.Select(CloneKeyframe).ToList();
        var left = original.Where(k => k.TimeSeconds < splitSeconds - 0.001).Select(CloneKeyframe).ToList();
        var right = original.Where(k => k.TimeSeconds > splitSeconds + 0.001).Select(k =>
        {
            var clone = CloneKeyframe(k);
            clone.TimeSeconds -= splitSeconds;
            return clone;
        }).ToList();

        foreach (var property in original.Select(k => k.Property).Distinct())
        {
            var fallback = ClipKeyframeEvaluator.StaticValue(first, property);
            var boundary = ClipKeyframeEvaluator.Evaluate(original, property, splitSeconds, fallback);
            var easingIntoBoundary = original
                .Where(k => k.Property == property && k.TimeSeconds >= splitSeconds)
                .OrderBy(k => k.TimeSeconds)
                .Select(k => k.Easing)
                .FirstOrDefault();

            left.Add(new ClipKeyframe
            {
                Property = property,
                TimeSeconds = splitSeconds,
                Value = boundary,
                Easing = easingIntoBoundary
            });
            right.Add(new ClipKeyframe
            {
                Property = property,
                TimeSeconds = 0,
                Value = boundary,
                Easing = ClipKeyframeEasing.Linear
            });
        }

        first.Keyframes = left.OrderBy(k => k.Property).ThenBy(k => k.TimeSeconds).ToList();
        second.Keyframes = right.OrderBy(k => k.Property).ThenBy(k => k.TimeSeconds).ToList();
    }

    private static void TrimKeyframesAtStart(TimelineClip clip, double timelineDelta)
    {
        if (clip.Keyframes.Count == 0 || Math.Abs(timelineDelta) <= 1e-9)
        {
            return;
        }

        if (timelineDelta < 0)
        {
            foreach (var keyframe in clip.Keyframes)
            {
                keyframe.TimeSeconds -= timelineDelta;
            }
            return;
        }

        var original = clip.Keyframes.Select(CloneKeyframe).ToList();
        var shifted = original.Where(k => k.TimeSeconds > timelineDelta + 0.001).Select(k =>
        {
            var clone = CloneKeyframe(k);
            clone.TimeSeconds -= timelineDelta;
            return clone;
        }).ToList();

        foreach (var property in original.Select(k => k.Property).Distinct())
        {
            shifted.Add(new ClipKeyframe
            {
                Property = property,
                TimeSeconds = 0,
                Value = ClipKeyframeEvaluator.Evaluate(original, property, timelineDelta, ClipKeyframeEvaluator.StaticValue(clip, property)),
                Easing = ClipKeyframeEasing.Linear
            });
        }

        clip.Keyframes = shifted.OrderBy(k => k.Property).ThenBy(k => k.TimeSeconds).ToList();
    }

    private static void TrimKeyframesAtEnd(TimelineClip clip, double newDuration)
    {
        if (clip.Keyframes.Count == 0)
        {
            return;
        }

        var original = clip.Keyframes.Select(CloneKeyframe).ToList();
        var kept = original.Where(k => k.TimeSeconds < newDuration - 0.001).Select(CloneKeyframe).ToList();
        foreach (var property in original.Select(k => k.Property).Distinct())
        {
            kept.Add(new ClipKeyframe
            {
                Property = property,
                TimeSeconds = newDuration,
                Value = ClipKeyframeEvaluator.Evaluate(original, property, newDuration, ClipKeyframeEvaluator.StaticValue(clip, property)),
                Easing = original.Where(k => k.Property == property && k.TimeSeconds >= newDuration)
                    .OrderBy(k => k.TimeSeconds)
                    .Select(k => k.Easing)
                    .FirstOrDefault()
            });
        }

        clip.Keyframes = kept.OrderBy(k => k.Property).ThenBy(k => k.TimeSeconds).ToList();
    }

    private static void ClampKeyframesToDuration(TimelineClip clip)
    {
        var duration = Math.Max(0, clip.TimelineDurationSeconds);
        foreach (var keyframe in clip.Keyframes)
        {
            keyframe.TimeSeconds = Math.Clamp(keyframe.TimeSeconds, 0, duration);
        }
        clip.Keyframes = clip.Keyframes.OrderBy(k => k.Property).ThenBy(k => k.TimeSeconds).ToList();
    }

    private static ClipKeyframe CloneKeyframe(ClipKeyframe keyframe) => new()
    {
        Id = keyframe.Id,
        Property = keyframe.Property,
        TimeSeconds = keyframe.TimeSeconds,
        Value = keyframe.Value,
        Easing = keyframe.Easing
    };

    public void SetTrackLocked(string trackId, bool locked) => SetTrackFlag(trackId, t => t.IsLocked, (t, v) => t.IsLocked = v, locked);
'@
$t = Replace-Once $t $anchor $insert 'keyframe timeline helpers'

# Deep-copy keyframes in every undo/redo snapshot.
$old = @'
        MaskInvert = clip.MaskInvert,
        BlendMode = clip.BlendMode
    };
}
'@
$new = @'
        MaskInvert = clip.MaskInvert,
        BlendMode = clip.BlendMode,
        Keyframes = clip.Keyframes.Select(CloneKeyframe).ToList()
    };
}
'@
$t = Replace-Once $t $old $new 'clone keyframes'
Write-Utf8 $path $t

# -----------------------------------------------------------------------------
# Clip inspector ViewModel: playhead-aware keyframe authoring controls.
# -----------------------------------------------------------------------------
$path = 'src/NPVideoStudio.App/ViewModels/TimelineClipItemViewModel.cs'
$t = Read-Utf8 $path
$t = Replace-Once $t "using System.Windows.Input;`n" "using System.Windows.Input;`nusing CommunityToolkit.Mvvm.Input;`n" 'RelayCommand import'

$old = @'
    private readonly Action<string, ClipTransformSettings>? _onTransformChanged;
    private readonly Action<string, ClipCompositingSettings>? _onCompositingChanged;
'@
$new = @'
    private readonly Action<string, ClipTransformSettings>? _onTransformChanged;
    private readonly Action<string, ClipCompositingSettings>? _onCompositingChanged;
    private readonly Func<double>? _getPlayheadSeconds;
    private readonly Action<string, ClipKeyframeProperty, double, double, ClipKeyframeEasing>? _onKeyframeUpsert;
    private readonly Action<string, ClipKeyframeProperty, double>? _onKeyframeRemove;
'@
$t = Replace-Once $t $old $new 'keyframe callbacks'

$old = @'
    public bool IsOverlayClip { get; }
    public bool IsPictureClip => IsVideoClip || IsOverlayClip;
    public bool IsAudioClip { get; }
'@
$new = @'
    public bool IsOverlayClip { get; }
    public bool IsPictureClip => IsVideoClip || IsOverlayClip;
    public bool IsAudioClip { get; }
    public bool SupportsKeyframes => IsPictureClip || IsTextClip;
'@
$t = Replace-Once $t $old $new 'supports keyframes'

$anchor = @'
    /// <summary>The clip's own words, editable - real fix for "how do I check/correct what Whisper
'@
$insert = @'
    private static readonly ClipKeyframeProperty[] PictureKeyframeProperties = Enum.GetValues<ClipKeyframeProperty>();
    private static readonly ClipKeyframeProperty[] TextKeyframeProperties =
    {
        ClipKeyframeProperty.PositionX,
        ClipKeyframeProperty.PositionY,
        ClipKeyframeProperty.Scale,
        ClipKeyframeProperty.Opacity
    };

    public IReadOnlyList<ClipKeyframeProperty> AvailableKeyframeProperties =>
        IsTextClip ? TextKeyframeProperties : PictureKeyframeProperties;
    public IReadOnlyList<ClipKeyframeEasing> AvailableKeyframeEasings { get; } = Enum.GetValues<ClipKeyframeEasing>();

    private ClipKeyframeProperty _selectedKeyframeProperty = ClipKeyframeProperty.PositionX;
    public ClipKeyframeProperty SelectedKeyframeProperty
    {
        get => _selectedKeyframeProperty;
        set
        {
            if (_selectedKeyframeProperty == value) return;
            _selectedKeyframeProperty = value;
            OnPropertyChanged();
            _keyframeValue = CurrentKeyframeValue(value);
            OnPropertyChanged(nameof(KeyframeValue));
            OnPropertyChanged(nameof(KeyframeValueLabel));
            OnPropertyChanged(nameof(KeyframeValueMinimum));
            OnPropertyChanged(nameof(KeyframeValueMaximum));
            OnPropertyChanged(nameof(KeyframeValueIncrement));
        }
    }

    private ClipKeyframeEasing _selectedKeyframeEasing = ClipKeyframeEasing.EaseInOut;
    public ClipKeyframeEasing SelectedKeyframeEasing
    {
        get => _selectedKeyframeEasing;
        set { if (_selectedKeyframeEasing == value) return; _selectedKeyframeEasing = value; OnPropertyChanged(); }
    }

    private double _keyframeValue = 50;
    public double KeyframeValue
    {
        get => _keyframeValue;
        set
        {
            var clamped = ClipKeyframeEvaluator.ClampValue(SelectedKeyframeProperty, value);
            if (Math.Abs(_keyframeValue - clamped) < 1e-9) return;
            _keyframeValue = clamped;
            OnPropertyChanged();
        }
    }

    public string KeyframeValueLabel => SelectedKeyframeProperty switch
    {
        ClipKeyframeProperty.PositionX => "X pozicija (%)",
        ClipKeyframeProperty.PositionY => "Y pozicija (%)",
        ClipKeyframeProperty.Scale => "Veličina (%)",
        ClipKeyframeProperty.Rotation => "Rotacija (°)",
        ClipKeyframeProperty.Opacity => "Providnost (0-1)",
        _ => "Vrednost"
    };
    public double KeyframeValueMinimum => SelectedKeyframeProperty switch
    {
        ClipKeyframeProperty.PositionX or ClipKeyframeProperty.PositionY => -200,
        ClipKeyframeProperty.Scale => 1,
        ClipKeyframeProperty.Rotation => -3600,
        ClipKeyframeProperty.Opacity => 0,
        _ => -10000
    };
    public double KeyframeValueMaximum => SelectedKeyframeProperty switch
    {
        ClipKeyframeProperty.PositionX or ClipKeyframeProperty.PositionY => 300,
        ClipKeyframeProperty.Scale => 1000,
        ClipKeyframeProperty.Rotation => 3600,
        ClipKeyframeProperty.Opacity => 1,
        _ => 10000
    };
    public double KeyframeValueIncrement => SelectedKeyframeProperty == ClipKeyframeProperty.Opacity ? 0.05 : 1;
    public string KeyframeSummary => Clip.Keyframes.Count == 0
        ? "Nema keyframe-ova"
        : $"{Clip.Keyframes.Count} keyframe tačaka";

    public ICommand AddKeyframeAtPlayheadCommand { get; }
    public ICommand RemoveKeyframeAtPlayheadCommand { get; }

    private double CurrentKeyframeValue(ClipKeyframeProperty property)
    {
        if (IsTextClip)
        {
            return property switch
            {
                ClipKeyframeProperty.PositionX => HorizontalAlign switch
                {
                    TextHorizontalAlign.Left => 10,
                    TextHorizontalAlign.Right => 90,
                    _ => 50
                },
                ClipKeyframeProperty.PositionY => TextPosition switch
                {
                    CaptionTextPosition.Top => 10,
                    CaptionTextPosition.Middle => 50,
                    _ => 85
                },
                ClipKeyframeProperty.Scale => 100,
                ClipKeyframeProperty.Opacity => 1,
                _ => 0
            };
        }

        return ClipKeyframeEvaluator.StaticValue(Clip, property);
    }

    private void AddKeyframeAtPlayhead()
    {
        if (_getPlayheadSeconds is null || _onKeyframeUpsert is null)
        {
            return;
        }

        var local = Math.Clamp(_getPlayheadSeconds() - Clip.TimelineStartSeconds, 0, Clip.TimelineDurationSeconds);
        _onKeyframeUpsert(Clip.Id, SelectedKeyframeProperty, local, KeyframeValue, SelectedKeyframeEasing);
        OnPropertyChanged(nameof(KeyframeSummary));
    }

    private void RemoveKeyframeAtPlayhead()
    {
        if (_getPlayheadSeconds is null || _onKeyframeRemove is null)
        {
            return;
        }

        var local = Math.Clamp(_getPlayheadSeconds() - Clip.TimelineStartSeconds, 0, Clip.TimelineDurationSeconds);
        _onKeyframeRemove(Clip.Id, SelectedKeyframeProperty, local);
        OnPropertyChanged(nameof(KeyframeSummary));
    }

    /// <summary>The clip's own words, editable - real fix for "how do I check/correct what Whisper
'@
$t = Replace-Once $t $anchor $insert 'keyframe inspector properties'

# Extend constructor signature.
$old = @'
        Action<string, ClipTransformSettings>? onTransformChanged = null,
        Action<string, ClipCompositingSettings>? onCompositingChanged = null,
        bool isAudioClip = false)
'@
$new = @'
        Action<string, ClipTransformSettings>? onTransformChanged = null,
        Action<string, ClipCompositingSettings>? onCompositingChanged = null,
        bool isAudioClip = false,
        Func<double>? getPlayheadSeconds = null,
        Action<string, ClipKeyframeProperty, double, double, ClipKeyframeEasing>? onKeyframeUpsert = null,
        Action<string, ClipKeyframeProperty, double>? onKeyframeRemove = null)
'@
$t = Replace-Once $t $old $new 'keyframe constructor signature'

$old = @'
        _onCompositingChanged = onCompositingChanged;
        _onLayerPlacementChanged = onLayerPlacementChanged;
        IsOverlayClip = isOverlayClip;
'@
$new = @'
        _onCompositingChanged = onCompositingChanged;
        _onLayerPlacementChanged = onLayerPlacementChanged;
        _getPlayheadSeconds = getPlayheadSeconds;
        _onKeyframeUpsert = onKeyframeUpsert;
        _onKeyframeRemove = onKeyframeRemove;
        IsOverlayClip = isOverlayClip;
'@
$t = Replace-Once $t $old $new 'keyframe constructor fields'

$old = @'
        _onTextContentChanged = onTextContentChanged;
        _onAdvancedStyleChanged = onAdvancedStyleChanged;
    }
'@
$new = @'
        _onTextContentChanged = onTextContentChanged;
        _onAdvancedStyleChanged = onAdvancedStyleChanged;
        _keyframeValue = CurrentKeyframeValue(_selectedKeyframeProperty);
        AddKeyframeAtPlayheadCommand = new RelayCommand(AddKeyframeAtPlayhead);
        RemoveKeyframeAtPlayheadCommand = new RelayCommand(RemoveKeyframeAtPlayhead);
    }
'@
$t = Replace-Once $t $old $new 'keyframe command initialization'
Write-Utf8 $path $t

# -----------------------------------------------------------------------------
# Timeline wiring: session callbacks use current playhead, and do not rebuild the inspector after each
# keyframe click (the Clip object is live; the item notifies its own summary after the callback returns).
# -----------------------------------------------------------------------------
$path = 'src/NPVideoStudio.App/ViewModels/TimelineViewModel.cs'
$t = Read-Utf8 $path
$anchor = @'
        void OnCompositingChanged(string clipId, ClipCompositingSettings settings)
        {
            _session.SetClipCompositing(clipId, settings);
            RefreshFromSession();
        }
        return new TimelineClipItemViewModel(clip, track.Id, ResolveClipLabel(clip), track.Kind == TimelineTrackKind.Video,
'@
$insert = @'
        void OnCompositingChanged(string clipId, ClipCompositingSettings settings)
        {
            _session.SetClipCompositing(clipId, settings);
            RefreshFromSession();
        }
        void OnKeyframeUpsert(string clipId, ClipKeyframeProperty property, double localTime, double value, ClipKeyframeEasing easing)
        {
            _session.UpsertKeyframe(clipId, property, localTime, value, easing);
            TimelineChanged?.Invoke();
        }
        void OnKeyframeRemove(string clipId, ClipKeyframeProperty property, double localTime)
        {
            _session.RemoveKeyframe(clipId, property, localTime);
            TimelineChanged?.Invoke();
        }
        return new TimelineClipItemViewModel(clip, track.Id, ResolveClipLabel(clip), track.Kind == TimelineTrackKind.Video,
'@
$t = Replace-Once $t $anchor $insert 'timeline keyframe callbacks'

$old = @'
            OnLayerPlacementChanged, track.Kind == TimelineTrackKind.ImageOverlay || (track.Kind == TimelineTrackKind.Video && _session.Tracks.Where(t => t.Kind == TimelineTrackKind.Video).FirstOrDefault()?.Id != track.Id), OnEffectsChanged, OnTransformChanged, OnCompositingChanged, track.Kind == TimelineTrackKind.Audio)
'@
$new = @'
            OnLayerPlacementChanged, track.Kind == TimelineTrackKind.ImageOverlay || (track.Kind == TimelineTrackKind.Video && _session.Tracks.Where(t => t.Kind == TimelineTrackKind.Video).FirstOrDefault()?.Id != track.Id), OnEffectsChanged, OnTransformChanged, OnCompositingChanged, track.Kind == TimelineTrackKind.Audio,
            _getPlayhead, OnKeyframeUpsert, OnKeyframeRemove)
'@
$t = Replace-Once $t $old $new 'pass keyframe callbacks'
Write-Utf8 $path $t

# -----------------------------------------------------------------------------
# Inspector UI. One real keyframe panel for picture and text clips; rotation is automatically absent for
# text until the text renderer can rotate glyph layers without lying about support.
# -----------------------------------------------------------------------------
$path = 'src/NPVideoStudio.App/Views/WorkspaceView.axaml'
$t = Read-Utf8 $path
$anchor = @'
            </StackPanel>
          </StackPanel>
        </ScrollViewer>
      </Border>
      </Grid>
'@
$insert = @'
            </StackPanel>

            <StackPanel Spacing="7" IsVisible="{Binding SupportsKeyframes}">
              <TextBlock Text="KEYFRAMES / ANIMACIJA" Classes="eyebrow" Margin="0,10,0,0" />
              <TextBlock Text="{Binding KeyframeSummary}" Classes="subtle" />
              <TextBlock Text="Svojstvo" Classes="subtle" />
              <ComboBox ItemsSource="{Binding AvailableKeyframeProperties}" SelectedItem="{Binding SelectedKeyframeProperty}" />
              <TextBlock Text="{Binding KeyframeValueLabel}" Classes="subtle" />
              <NumericUpDown Value="{Binding KeyframeValue}"
                             Minimum="{Binding KeyframeValueMinimum}" Maximum="{Binding KeyframeValueMaximum}"
                             Increment="{Binding KeyframeValueIncrement}" />
              <TextBlock Text="Easing do ove tačke" Classes="subtle" />
              <ComboBox ItemsSource="{Binding AvailableKeyframeEasings}" SelectedItem="{Binding SelectedKeyframeEasing}" />
              <WrapPanel Orientation="Horizontal">
                <Button Classes="cta" Content="◆ DODAJ NA PLEJHEDU" Command="{Binding AddKeyframeAtPlayheadCommand}" Margin="0,0,8,4" />
                <Button Content="Ukloni najbliži" Command="{Binding RemoveKeyframeAtPlayheadCommand}" Margin="0,0,0,4" />
              </WrapPanel>
              <TextBlock Text="Za tačan pokret koristi ‘Renderuj deo oko plejhed-a (brzo)’ — finalni export koristi isti FFmpeg keyframe engine."
                         Classes="subtle" TextWrapping="Wrap" />
            </StackPanel>
          </StackPanel>
        </ScrollViewer>
      </Border>
      </Grid>
'@
$t = Replace-Once $t $anchor $insert 'workspace keyframe inspector'
Write-Utf8 $path $t

Write-Host 'P3 keyframe model/session/UI patch applied.'
