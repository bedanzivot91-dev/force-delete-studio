# Suno MP3 + Lyrics Final Fix

Required final behavior for the Suno Studio Windows build:

- Select all songs from the complete Suno library, not only the visible page.
- Preset `MP3 ONLY`: download exactly the MP3 for every selected song.
- Preset `MP3 + LYRICS`: download the MP3 and the song lyrics TXT beside it.
- Download must resolve the current Suno audio URL before saving the MP3.
- A song is marked downloaded only after the MP3 file exists and passes validation.
- The final Windows package must include the corrected frontend and backend and must be produced only after the complete test suite succeeds.
