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

$path = 'src/NPVideoStudio.Media/FfmpegFilterGraphBuilder.cs'
$t = Read-Utf8 $path

# -----------------------------------------------------------------------------
# Base video segment: static clips keep the old exact chain. Animated clips are composited onto a finite
# black target-size canvas so X/Y/scale/rotation/opacity can vary per frame while concat still receives a
# fixed target-sized stream.
# -----------------------------------------------------------------------------
$old = @'
            videoFilter.Append(BuildTemporalVideoFilters(clip, duration));
            videoFilter.Append(BuildSpeedFilter(clip));
            videoFilter.Append(BuildTransformFilters(clip));
            videoFilter.Append(FormattableString.Invariant(
                $",scale={targetWidth}:{targetHeight}:force_original_aspect_ratio=decrease,pad={targetWidth}:{targetHeight}:(ow-iw)/2:(oh-ih)/2:color=black,setsar=1"));
            videoFilter.Append(BuildEffectFilters(clip));
            if (clip.FadeInSeconds > 0)
            {
                videoFilter.Append(FormattableString.Invariant($",fade=t=in:st=0:d={clip.FadeInSeconds}"));
            }
            if (clip.FadeOutSeconds > 0)
            {
                var fadeOutStart = Math.Max(0, duration - clip.FadeOutSeconds);
                videoFilter.Append(FormattableString.Invariant($",fade=t=out:st={fadeOutStart}:d={clip.FadeOutSeconds}"));
            }
            videoFilter.Append(vLabel);
            filterLines.Add(videoFilter.ToString());
'@
$new = @'
            videoFilter.Append(BuildTemporalVideoFilters(clip, duration));
            videoFilter.Append(BuildSpeedFilter(clip));
            videoFilter.Append(BuildTransformFilters(clip));

            if (!HasVisualKeyframes(clip))
            {
                videoFilter.Append(FormattableString.Invariant(
                    $",scale={targetWidth}:{targetHeight}:force_original_aspect_ratio=decrease,pad={targetWidth}:{targetHeight}:(ow-iw)/2:(oh-ih)/2:color=black,setsar=1"));
                videoFilter.Append(BuildEffectFilters(clip));
                if (clip.FadeInSeconds > 0)
                {
                    videoFilter.Append(FormattableString.Invariant($",fade=t=in:st=0:d={clip.FadeInSeconds}"));
                }
                if (clip.FadeOutSeconds > 0)
                {
                    var fadeOutStart = Math.Max(0, duration - clip.FadeOutSeconds);
                    videoFilter.Append(FormattableString.Invariant($",fade=t=out:st={fadeOutStart}:d={clip.FadeOutSeconds}"));
                }
                videoFilter.Append(vLabel);
                filterLines.Add(videoFilter.ToString());
            }
            else
            {
                var animatedSource = $"[vanimsrc{segmentIndex}]";
                var scaleExpr = BuildKeyframeValueExpression(clip, ClipKeyframeProperty.Scale, "t", clip.ScalePercent);
                var fitWidth = FormattableString.Invariant($"min({targetWidth},{targetHeight}*iw/ih)");
                videoFilter.Append(FormattableString.Invariant(
                    $",scale=w='max(2,({fitWidth})*({scaleExpr})/100)':h=-1:eval=frame"));
                videoFilter.Append(BuildAnimatedRotationFilter(clip, "t"));
                videoFilter.Append(BuildEffectFilters(clip));
                videoFilter.Append(",format=rgba");
                videoFilter.Append(BuildAnimatedOpacityFilter(clip, "T"));
                if (clip.FadeInSeconds > 0)
                {
                    videoFilter.Append(FormattableString.Invariant($",fade=t=in:st=0:d={clip.FadeInSeconds}:alpha=1"));
                }
                if (clip.FadeOutSeconds > 0)
                {
                    var fadeOutStart = Math.Max(0, duration - clip.FadeOutSeconds);
                    videoFilter.Append(FormattableString.Invariant($",fade=t=out:st={fadeOutStart}:d={clip.FadeOutSeconds}:alpha=1"));
                }
                videoFilter.Append(animatedSource);
                filterLines.Add(videoFilter.ToString());

                var animatedCanvas = $"[vanimbg{segmentIndex}]";
                filterLines.Add(FormattableString.Invariant(
                    $"color=c=black:s={targetWidth}x{targetHeight}:d={duration}:r={frameRate},format=rgba{animatedCanvas}"));
                var posX = BuildKeyframeValueExpression(clip, ClipKeyframeProperty.PositionX, "t", clip.PositionXPercent);
                var posY = BuildKeyframeValueExpression(clip, ClipKeyframeProperty.PositionY, "t", clip.PositionYPercent);
                var x = $"(main_w*(({posX})/100))-(overlay_w/2)";
                var y = $"(main_h*(({posY})/100))-(overlay_h/2)";
                filterLines.Add(FormattableString.Invariant(
                    $"{animatedCanvas}{animatedSource}overlay=x='{x}':y='{y}':shortest=1:format=auto,format=yuv420p,setsar=1{vLabel}"));
            }
'@
$t = Replace-Once $t $old $new 'animated base video chain'

# -----------------------------------------------------------------------------
# Text: X/Y, font-size scale and opacity use the same persisted keyframes. Rotation is intentionally not
# exposed for text yet; drawing text directly with drawtext cannot rotate glyph layers truthfully.
# -----------------------------------------------------------------------------
$old = @'
            var y = clip.TextPosition switch
            {
                CaptionTextPosition.Top => "h*0.08",
                CaptionTextPosition.Middle => "(h-text_h)/2",
                _ => "h*0.85"
            };
            var x = clip.TextHorizontalAlign switch
            {
                TextHorizontalAlign.Left => "w*0.05",
                TextHorizontalAlign.Right => "w-text_w-w*0.05",
                _ => "(w-text_w)/2"
            };
'@
$new = @'
            var defaultYPercent = clip.TextPosition switch
            {
                CaptionTextPosition.Top => 8.0,
                CaptionTextPosition.Middle => 50.0,
                _ => 85.0
            };
            var defaultXPercent = clip.TextHorizontalAlign switch
            {
                TextHorizontalAlign.Left => 5.0,
                TextHorizontalAlign.Right => 95.0,
                _ => 50.0
            };
            var textLocalTime = FormattableString.Invariant($"(t-{MapToRenderedTime(clip.TimelineStartSeconds)})");
            var y = HasKeyframes(clip, ClipKeyframeProperty.PositionY)
                ? $"h*(({BuildKeyframeValueExpression(clip, ClipKeyframeProperty.PositionY, textLocalTime, defaultYPercent)})/100)-text_h/2"
                : clip.TextPosition switch
                {
                    CaptionTextPosition.Top => "h*0.08",
                    CaptionTextPosition.Middle => "(h-text_h)/2",
                    _ => "h*0.85"
                };
            var x = HasKeyframes(clip, ClipKeyframeProperty.PositionX)
                ? $"w*(({BuildKeyframeValueExpression(clip, ClipKeyframeProperty.PositionX, textLocalTime, defaultXPercent)})/100)-text_w/2"
                : clip.TextHorizontalAlign switch
                {
                    TextHorizontalAlign.Left => "w*0.05",
                    TextHorizontalAlign.Right => "w-text_w-w*0.05",
                    _ => "(w-text_w)/2"
                };
'@
$t = Replace-Once $t $old $new 'animated text position'

$old = @'
            if (clip.FadeInSeconds > 0 || clip.FadeOutSeconds > 0)
            {
                extraArguments.Append($":alpha='{BuildTextAlphaExpression(clip, renderedStart, renderedEnd)}'");
            }

            filterLines.Add(FormattableString.Invariant(
                $"{currentTextVideoLabel}drawtext=text='{escapedText}':enable='between(t,{renderedStart},{renderedEnd})':x={x}:y={y}:fontsize={clip.FontSizePx}:fontcolor={clip.TextColor}{fontFileArgument}{extraArguments}{nextLabel}"));
'@
$new = @'
            var textAlpha = clip.FadeInSeconds > 0 || clip.FadeOutSeconds > 0
                ? BuildTextAlphaExpression(clip, renderedStart, renderedEnd)
                : "1";
            if (HasKeyframes(clip, ClipKeyframeProperty.Opacity))
            {
                var opacity = BuildKeyframeValueExpression(clip, ClipKeyframeProperty.Opacity, textLocalTime, 1);
                textAlpha = $"({textAlpha})*({opacity})";
            }
            if (textAlpha != "1")
            {
                extraArguments.Append($":alpha='{textAlpha}'");
            }

            var fontSize = HasKeyframes(clip, ClipKeyframeProperty.Scale)
                ? $"'{clip.FontSizePx}*({BuildKeyframeValueExpression(clip, ClipKeyframeProperty.Scale, textLocalTime, 100)})/100'"
                : clip.FontSizePx.ToString(CultureInfo.InvariantCulture);

            filterLines.Add(FormattableString.Invariant(
                $"{currentTextVideoLabel}drawtext=text='{escapedText}':enable='between(t,{renderedStart},{renderedEnd})':x='{x}':y='{y}':fontsize={fontSize}:fontcolor={clip.TextColor}{fontFileArgument}{extraArguments}{nextLabel}"));
'@
$t = Replace-Once $t $old $new 'animated text scale opacity'

# -----------------------------------------------------------------------------
# Range preview: clone keyframes, then slice local keyframe time to exactly the visible window.
# -----------------------------------------------------------------------------
$old = @'
                if (trimmedFromStart > 0)
                {
                    newClip.TransitionInType = ClipTransitionType.None;
                }

                newTrack.Clips.Add(newClip);
'@
$new = @'
                if (trimmedFromStart > 0)
                {
                    newClip.TransitionInType = ClipTransitionType.None;
                }

                SliceKeyframesForRange(clip, newClip, trimmedFromStart, Math.Max(0.05, overlapEnd - overlapStart));
                newTrack.Clips.Add(newClip);
'@
$t = Replace-Once $t $old $new 'range preview keyframe slice'

$old = @'
        MaskInvert = clip.MaskInvert,
        BlendMode = clip.BlendMode
    };
'@
$new = @'
        MaskInvert = clip.MaskInvert,
        BlendMode = clip.BlendMode,
        Keyframes = clip.Keyframes.Select(CloneKeyframe).ToList()
    };

    private static ClipKeyframe CloneKeyframe(ClipKeyframe keyframe) => new()
    {
        Id = keyframe.Id,
        Property = keyframe.Property,
        TimeSeconds = keyframe.TimeSeconds,
        Value = keyframe.Value,
        Easing = keyframe.Easing
    };

    private static void SliceKeyframesForRange(TimelineClip original, TimelineClip sliced, double trimmedFromStart, double visibleDuration)
    {
        if (original.Keyframes.Count == 0)
        {
            return;
        }

        var result = new List<ClipKeyframe>();
        foreach (var property in original.Keyframes.Select(k => k.Property).Distinct())
        {
            var fallback = ClipKeyframeEvaluator.StaticValue(original, property);
            result.Add(new ClipKeyframe
            {
                Property = property,
                TimeSeconds = 0,
                Value = ClipKeyframeEvaluator.Evaluate(original, property, trimmedFromStart),
                Easing = ClipKeyframeEasing.Linear
            });

            foreach (var point in original.Keyframes.Where(k => k.Property == property &&
                         k.TimeSeconds > trimmedFromStart + 0.001 &&
                         k.TimeSeconds < trimmedFromStart + visibleDuration - 0.001))
            {
                var clone = CloneKeyframe(point);
                clone.TimeSeconds -= trimmedFromStart;
                result.Add(clone);
            }

            result.Add(new ClipKeyframe
            {
                Property = property,
                TimeSeconds = visibleDuration,
                Value = ClipKeyframeEvaluator.Evaluate(original, property, trimmedFromStart + visibleDuration),
                Easing = original.Keyframes.Where(k => k.Property == property && k.TimeSeconds >= trimmedFromStart + visibleDuration)
                    .OrderBy(k => k.TimeSeconds)
                    .Select(k => k.Easing)
                    .FirstOrDefault()
            });
        }
        sliced.Keyframes = result.OrderBy(k => k.Property).ThenBy(k => k.TimeSeconds).ToList();
    }
'@
$t = Replace-Once $t $old $new 'range clone keyframes'

# -----------------------------------------------------------------------------
# Overlay: dynamic scale/rotation/opacity and global-time X/Y.
# -----------------------------------------------------------------------------
$old = @'
            var scale = Math.Clamp(clip.ScalePercent, 1, 1000) / 100.0;
            var overlayWidth = Math.Max(1, (int)Math.Round(targetWidth * scale));
            var opacity = Math.Clamp(clip.Opacity, 0, 1);
            var start = mapToRenderedTime(clip.TimelineStartSeconds);
'@
$new = @'
            var scale = Math.Clamp(clip.ScalePercent, 1, 1000) / 100.0;
            var overlayWidth = Math.Max(1, (int)Math.Round(targetWidth * scale));
            var opacity = Math.Clamp(clip.Opacity, 0, 1);
            var start = mapToRenderedTime(clip.TimelineStartSeconds);
'@
# Anchor intentionally unchanged here; subsequent replacement uses overlayWidth line in the chain.
if ($t.IndexOf($old, [StringComparison]::Ordinal) -lt 0) { throw 'Anchor not found: overlay setup' }

$old = @'
            prepared.Append(BuildSpeedFilter(clip));
            prepared.Append(BuildTransformFilters(clip));
            prepared.Append(FormattableString.Invariant($",scale={overlayWidth}:-1"));
            prepared.Append(BuildEffectFilters(clip));
            // colorchannelmixer/chromakey/masks need an alpha-capable pixel format.
            prepared.Append(",format=rgba");
            prepared.Append(BuildChromaKeyFilter(clip));
            prepared.Append(BuildMaskFilter(clip));
            prepared.Append(FormattableString.Invariant($",colorchannelmixer=aa={opacity}"));
'@
$new = @'
            prepared.Append(BuildSpeedFilter(clip));
            prepared.Append(BuildTransformFilters(clip));
            if (HasKeyframes(clip, ClipKeyframeProperty.Scale))
            {
                var scaleExpr = BuildKeyframeValueExpression(clip, ClipKeyframeProperty.Scale, "t", clip.ScalePercent);
                prepared.Append(FormattableString.Invariant($",scale=w='max(2,{targetWidth}*({scaleExpr})/100)':h=-1:eval=frame"));
            }
            else
            {
                prepared.Append(FormattableString.Invariant($",scale={overlayWidth}:-1"));
            }
            prepared.Append(BuildAnimatedRotationFilter(clip, "t"));
            prepared.Append(BuildEffectFilters(clip));
            // geq/chromakey/masks need an alpha-capable pixel format.
            prepared.Append(",format=rgba");
            prepared.Append(BuildChromaKeyFilter(clip));
            prepared.Append(BuildMaskFilter(clip));
            if (HasKeyframes(clip, ClipKeyframeProperty.Opacity))
            {
                prepared.Append(BuildAnimatedOpacityFilter(clip, "T"));
            }
            else
            {
                prepared.Append(FormattableString.Invariant($",colorchannelmixer=aa={opacity}"));
            }
'@
$t = Replace-Once $t $old $new 'animated overlay scale rotation opacity'

$old = @'
            // Centre-anchored: shift left/up by half the overlay's own rendered size.
            var centreX = FormattableString.Invariant($"(main_w*{clip.PositionXPercent / 100.0})-(overlay_w/2)");
            var centreY = FormattableString.Invariant($"(main_h*{clip.PositionYPercent / 100.0})-(overlay_h/2)");
'@
$new = @'
            // Centre-anchored. overlay() runs on the global rendered clock, so local clip keyframe time is
            // global t minus this clip's rendered start.
            var localOverlayTime = FormattableString.Invariant($"(t-{start})");
            var xPercent = BuildKeyframeValueExpression(clip, ClipKeyframeProperty.PositionX, localOverlayTime, clip.PositionXPercent);
            var yPercent = BuildKeyframeValueExpression(clip, ClipKeyframeProperty.PositionY, localOverlayTime, clip.PositionYPercent);
            var centreX = $"(main_w*(({xPercent})/100))-(overlay_w/2)";
            var centreY = $"(main_h*(({yPercent})/100))-(overlay_h/2)";
'@
$t = Replace-Once $t $old $new 'animated overlay position'

# Static transform must not apply the same rotation again when rotation has keyframes.
$old = @'
        var rotation = clip.RotationDegrees % 360.0;
        if (Math.Abs(rotation) > 1e-6)
'@
$new = @'
        var rotation = clip.RotationDegrees % 360.0;
        if (!HasKeyframes(clip, ClipKeyframeProperty.Rotation) && Math.Abs(rotation) > 1e-6)
'@
$t = Replace-Once $t $old $new 'skip static animated rotation'

# -----------------------------------------------------------------------------
# Shared FFmpeg keyframe expression builder. This is public only so unit tests can prove easing syntax;
# callers still persist keyframes through TimelineEditSession, never by editing expression strings.
# -----------------------------------------------------------------------------
$anchor = @'
    public static string BuildTemporalVideoFilters(TimelineClip clip, double durationSeconds)
'@
$insert = @'
    private static bool HasVisualKeyframes(TimelineClip clip) =>
        HasKeyframes(clip, ClipKeyframeProperty.PositionX) ||
        HasKeyframes(clip, ClipKeyframeProperty.PositionY) ||
        HasKeyframes(clip, ClipKeyframeProperty.Scale) ||
        HasKeyframes(clip, ClipKeyframeProperty.Rotation) ||
        HasKeyframes(clip, ClipKeyframeProperty.Opacity);

    private static bool HasKeyframes(TimelineClip clip, ClipKeyframeProperty property) =>
        clip.Keyframes.Any(k => k.Property == property);

    /// <summary>Piecewise FFmpeg expression for one animated property. The easing attached to the RIGHT
    /// keyframe controls travel into that point, matching ClipKeyframeEvaluator exactly.</summary>
    public static string BuildKeyframeValueExpression(
        TimelineClip clip,
        ClipKeyframeProperty property,
        string timeExpression,
        double fallback)
    {
        var points = clip.Keyframes.Where(k => k.Property == property).OrderBy(k => k.TimeSeconds).ToArray();
        if (points.Length == 0)
        {
            return F(ClipKeyframeEvaluator.ClampValue(property, fallback));
        }
        if (points.Length == 1)
        {
            return F(ClipKeyframeEvaluator.ClampValue(property, points[0].Value));
        }

        string result = F(ClipKeyframeEvaluator.ClampValue(property, points[^1].Value));
        for (var i = points.Length - 1; i >= 1; i--)
        {
            var left = points[i - 1];
            var right = points[i];
            var leftValue = ClipKeyframeEvaluator.ClampValue(property, left.Value);
            var rightValue = ClipKeyframeEvaluator.ClampValue(property, right.Value);
            var span = Math.Max(1e-9, right.TimeSeconds - left.TimeSeconds);
            var u = $"(({timeExpression}-{F(left.TimeSeconds)})/{F(span)})";
            var eased = BuildEasingExpression(u, right.Easing);
            var segment = $"({F(leftValue)}+({F(rightValue)}-{F(leftValue)})*({eased}))";
            result = $"if(lt({timeExpression},{F(right.TimeSeconds)}),{segment},{result})";
        }
        return $"if(lte({timeExpression},{F(points[0].TimeSeconds)}),{F(ClipKeyframeEvaluator.ClampValue(property, points[0].Value))},{result})";
    }

    private static string BuildEasingExpression(string u, ClipKeyframeEasing easing) => easing switch
    {
        ClipKeyframeEasing.EaseIn => $"(({u})*({u}))",
        ClipKeyframeEasing.EaseOut => $"(1-(1-({u}))*(1-({u})))",
        ClipKeyframeEasing.EaseInOut => $"if(lt(({u}),0.5),2*({u})*({u}),1-2*(1-({u}))*(1-({u})))",
        ClipKeyframeEasing.Hold => "0",
        _ => u
    };

    private static string BuildAnimatedRotationFilter(TimelineClip clip, string localTimeExpression)
    {
        if (!HasKeyframes(clip, ClipKeyframeProperty.Rotation))
        {
            return string.Empty;
        }
        var angle = BuildKeyframeValueExpression(clip, ClipKeyframeProperty.Rotation, localTimeExpression, clip.RotationDegrees);
        return $",format=rgba,rotate=angle='({angle})*PI/180':ow='hypot(iw,ih)':oh='hypot(iw,ih)':c=black@0";
    }

    private static string BuildAnimatedOpacityFilter(TimelineClip clip, string localTimeExpression)
    {
        var opacity = BuildKeyframeValueExpression(clip, ClipKeyframeProperty.Opacity, localTimeExpression, clip.Opacity);
        return $",geq=r='r(X,Y)':g='g(X,Y)':b='b(X,Y)':a='alpha(X,Y)*({opacity})'";
    }

    private static string F(double value) => value.ToString("0.#########", CultureInfo.InvariantCulture);

    public static string BuildTemporalVideoFilters(TimelineClip clip, double durationSeconds)
'@
$t = Replace-Once $t $anchor $insert 'shared keyframe renderer helpers'

Write-Utf8 $path $t
Write-Host 'P3 real FFmpeg keyframe renderer patch applied.'
