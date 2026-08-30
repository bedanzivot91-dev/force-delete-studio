# Suno MP3 download fix

## Required behavior
Suno downloads must produce a real local MP3 file before a song is marked as downloaded.

1. Resolve the current `audio_url`; refresh it when expired.
2. Download the audio response to a temporary `.part` file.
3. Validate the response is actually audio and has non-zero size.
4. If the source is WAV/other supported audio, transcode to MP3 with the bundled FFmpeg when MP3 output is required.
5. Atomically rename the validated MP3 to the final `.mp3` path.
6. Never mark a song downloaded when the request returns HTML/JSON, a zero-byte file, or an incomplete response.
7. On failure, remove the `.part` file and report the actual HTTP/FFmpeg error.
8. Reuse the existing Suno URL-refresh path rather than requiring the user to manually copy a URL.

## Implementation contract
The existing download action must call the same Suno client/source-selection logic used by the remote-only song-finder path, then perform the validated MP3 download described above. The UI must expose download progress and the final local MP3 path.

This document is intentionally a specification only; it does not pretend that the binary release has been rebuilt until CI produces and validates a new Windows FULL OFFLINE ZIP.
