# Audio Files — 0.6.1

- Open Audio Files at default/minimum window sizes and in light/dark mode; scroll to transcript and copy/save controls.
- Choose multiple local WAV/AIFF/FLAC/MP3/M4A/OGG/WebM files, including spaces/non-ASCII names. No request should occur before Transcribe.
- With no saved key, Transcribe should explain how to connect without preparing/uploading audio.
- Transcribe WAV: check lossless savings and a usable transcript; verify source bytes are unchanged.
- Transcribe already compressed MP3/M4A: verify no unnecessary FLAC conversion.
- Transcribe a PCM/FLAC file over 24 MB: all parts should upload in order and the final text should include the ending.
- Cancel while compressing and uploading: UI becomes available, temporary files disappear, completed/partial results remain, later queue files remain unstarted.
- Retry a batch: completed files are skipped; partial/failed files restart without duplicating old text.
- Check missing, empty, malformed, inaccessible and oversized compressed files: clear per-file errors; next queued file can still run.
- Copy the selected transcript; save it as UTF-8 text; cancel the Save dialog; test an unwritable destination.
- Toggle dictation writing cleanup and command intent: file transcription should still use only Whisper.
- Quit during work: cancellation completes and temporary files are deleted. Closing to tray should allow processing to continue.
- Install 0.6.1 over 0.6.0: verify saved key/settings survive and bundled FLAC files are available under Data/Tools/Flac.
