using Xunit;

namespace NPVideoStudio.UnitTests;

/// <summary>
/// Every test class that downloads the real Whisper model to the shared TestAssets model path must be
/// in this collection - xUnit runs different collections in parallel by default, and two concurrent
/// downloads racing to write/move the same "ggml-tiny.bin.tmp" file caused a real CI failure
/// (System.IO.IOException: file in use by another process) once a second such test class was added.
/// </summary>
[CollectionDefinition("Whisper model tests")]
public class WhisperModelCollection : ICollectionFixture<object>
{
}
