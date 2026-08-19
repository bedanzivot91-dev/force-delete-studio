# CapCut-like editor: verified scope and gap analysis

Checked on 2026-08-19 against CapCut's public desktop-editor pages and the supplied screenshots. This
document deliberately distinguishes working NP Video Studio features from future work. It does not
claim that NP Video Studio is a complete CapCut clone.

## Delivered in this release

- Four-area editing workspace: media browser left, project-shaped player centre, selected-clip
  inspector right, full-width multi-track timeline below.
- Resizable left/player/right columns and resizable player/timeline rows.
- Player zoom, pan, fit, focus mode and full screen for portrait and landscape projects.
- Multi-track video, audio, caption, text and image-overlay lanes; append, drag, split, duplicate,
  delete, mute, hide, lock, solo, undo/redo and timeline zoom.
- Selected text inspector: content, font, size, colour, position, alignment, bold, italic, outline,
  shadow, background and apply-style-to-track.
- Video inspector: effect, brightness, contrast, saturation and speed.
- Known-lyrics workflow using `lyric-align`, with Demucs vocal separation and faster-whisper word
  timings. `lyric-align` is now a required, separately verified component of the song AI package.
- User-controlled tool updates: notify-only (default), automatic on a chosen interval, or manual;
  diagnostics always exposes an explicit install/update command.

## CapCut families not yet implemented

These are intentionally backlog, not hidden behind buttons:

- Stock media/sticker/effect/template marketplace and cloud collaboration.
- Motion tracking, masks, chroma key, stabilization, optical-flow slow motion and keyframe curves.
- Text-to-speech, voice cloning, AI avatars, generative video/image tools and cloud AI services.
- Hundreds of proprietary transitions, effects, filters, animations and licensed media assets.
- Professional colour wheels/scopes, nested sequences, compound clips and proxy relinking UI.
- Screen/camera recording, transcript-based editing, beat detection and automatic reframing.

Those capabilities require separate editor-engine work, model downloads, licences and focused tests.
They must be added in reviewable phases rather than represented as finished.

## Open-source references evaluated

- `ijuinryukichi/lyric-align` (MIT): known-text alignment for sung vocals; integrated.
- `m-bain/whisperX` (BSD-4-Clause): optional advanced speech alignment; remains optional.
- `facebookresearch/demucs` (MIT): vocal separation; integrated in the song AI environment.
- Kdenlive, Shotcut and OpenReelio: useful interaction references, not copied wholesale. Their
  application code and architectures are not drop-in modules for this Avalonia/.NET editor.
