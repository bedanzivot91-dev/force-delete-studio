using System.Text.Json;

// Minimal stand-in for ai-worker/ai_worker.py: reads the same JSON request file (--request <path>) and
// prints the same JSONL event shape AiWorkerClient parses, so tests exercise the real subprocess
// launch/cancellation/JSONL-parsing code without needing Python or any ML package installed.
var argsList = args.ToList();
var requestIndex = argsList.IndexOf("--request");
if (requestIndex < 0 || requestIndex + 1 >= argsList.Count)
{
    Console.Error.WriteLine("fake-ai-worker: missing --request argument");
    return 1;
}

var requestPath = argsList[requestIndex + 1];
if (!File.Exists(requestPath))
{
    Console.Error.WriteLine("fake-ai-worker: request file not found");
    return 1;
}

using var doc = JsonDocument.Parse(File.ReadAllText(requestPath));
var root = doc.RootElement;
var jobKind = root.TryGetProperty("jobKind", out var jobKindEl) ? jobKindEl.GetString() : null;
var audioFilePath = root.TryGetProperty("audioFilePath", out var audioEl) ? audioEl.GetString() : null;

void Emit(string json) => Console.WriteLine(json);

if (jobKind == "CapabilityCheck")
{
    Emit("""{"type":"CapabilityCheck","engine":"python","engineAvailable":true,"message":"3.11.0-fake"}""");
    Emit("""{"type":"CapabilityCheck","engine":"faster_whisper","engineAvailable":true}""");
    Emit("""{"type":"CapabilityCheck","engine":"whisperx","engineAvailable":false}""");
    Emit("""{"type":"CapabilityCheck","engine":"demucs","engineAvailable":false}""");
    Emit("""{"type":"Done"}""");
    return 0;
}

if (audioFilePath == "TRIGGER_ERROR")
{
    Emit("""{"type":"Error","message":"fake-ai-worker: simulated failure"}""");
    return 1;
}

if (audioFilePath == "TRIGGER_SLOW")
{
    Emit("""{"type":"Progress","progressPercent":1.0,"message":"starting slow job"}""");
    Thread.Sleep(TimeSpan.FromSeconds(30));
    Emit("""{"type":"Done"}""");
    return 0;
}

if (audioFilePath == "TRIGGER_MALFORMED_EXIT")
{
    Console.Error.WriteLine("fake-ai-worker: crashed without a proper Error event");
    return 3;
}

Emit("""{"type":"Progress","progressPercent":50.0,"message":"processing"}""");
Emit("""{"type":"Result","words":[{"text":"reč","start":1.0,"end":1.5,"confidence":0.9}],"rawText":"reč"}""");
Emit("""{"type":"Done"}""");
return 0;
