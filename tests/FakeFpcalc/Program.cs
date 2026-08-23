// Minimal stand-in for the real Chromaprint `fpcalc` CLI: understands `-raw <file>` (the only invocation
// SongRecognitionService actually sends) and prints fpcalc's real output shape (FILE=/DURATION=/
// FINGERPRINT=) so tests can exercise the real process-orchestration and FingerprintMatcher comparison
// code without the real (separately-installed) tool. The fingerprint itself is a deterministic
// content-derived hash, not a real Chromaprint algorithm - it exists only so "same audio bytes -> same
// fingerprint" and "different audio bytes -> different fingerprint" hold, which is exactly what
// SongRecognitionService/FingerprintMatcher's tests need to exercise duplicate-detection.
const int FingerprintLength = 48;

var filePath = args.LastOrDefault(a => !a.StartsWith('-'));
if (filePath is null || !File.Exists(filePath))
{
    Console.Error.WriteLine("fake-fpcalc: no readable file argument found");
    return 1;
}

var bytes = File.ReadAllBytes(filePath);
if (bytes.Length == 0)
{
    Console.Error.WriteLine("fake-fpcalc: empty input file");
    return 1;
}

var chunkSize = Math.Max(1, bytes.Length / FingerprintLength);
var fingerprint = new uint[FingerprintLength];
for (var i = 0; i < FingerprintLength; i++)
{
    var start = Math.Min(i * chunkSize, bytes.Length - 1);
    var end = Math.Min(start + chunkSize, bytes.Length);
    fingerprint[i] = Fnv1a32(bytes.AsSpan(start, Math.Max(1, end - start)));
}

// A rough but real stand-in for "duration": FakeFpcalc has no audio decoder, so this approximates
// duration from file size at a fixed nominal bitrate - good enough for tests that only check windows
// were requested at the right relative offsets, not exact seconds.
var approximateDurationSeconds = Math.Max(1.0, bytes.Length / 16000.0);

Console.WriteLine($"FILE={filePath}");
Console.WriteLine($"DURATION={approximateDurationSeconds:F0}");
Console.WriteLine($"FINGERPRINT={string.Join(',', fingerprint)}");
return 0;

static uint Fnv1a32(ReadOnlySpan<byte> data)
{
    const uint offsetBasis = 2166136261;
    const uint prime = 16777619;

    var hash = offsetBasis;
    foreach (var b in data)
    {
        hash ^= b;
        hash *= prime;
    }

    return hash;
}
