using System.Runtime.CompilerServices;
using NPVideoStudio.App.ViewModels;
using NPVideoStudio.Core.Services;
using NPVideoStudio.Domain;
using Serilog;
using Xunit;

namespace NPVideoStudio.UnitTests;

public sealed class CaptionTranslationTests
{
    [Fact]
    public async Task TranslateDocument_ReplacesOnlyText_AndUndoRestoresOriginal()
    {
        var worker = new TranslationWorkerFake("Hello", "world");
        var viewModel = new CaptionEditorViewModel(
            new FakeStorageService(), new LoggerConfiguration().CreateLogger(), worker);
        var firstId = Guid.NewGuid();
        viewModel.LoadWords(new[]
        {
            new CaptionWord { Id = firstId, OriginalText = "Zdravo", NormalizedText = "zdravo", Start = TimeSpan.FromSeconds(1), End = TimeSpan.FromSeconds(2) },
            new CaptionWord { OriginalText = "svete", NormalizedText = "svete", Start = TimeSpan.FromSeconds(2), End = TimeSpan.FromSeconds(3), LineBreakAfter = true }
        });

        await viewModel.TranslateDocumentCommand.ExecuteAsync(null);

        Assert.Equal(new[] { "Hello", "world" }, viewModel.Words.Select(word => word.Text));
        Assert.Equal(firstId, viewModel.Words[0].Word.Id);
        Assert.Equal(TimeSpan.FromSeconds(1), viewModel.Words[0].Word.Start);
        Assert.Equal(TimeSpan.FromSeconds(2), viewModel.Words[0].Word.End);
        Assert.True(viewModel.Words[1].Word.LineBreakAfter);
        Assert.Equal(AiWorkerJobKind.SubtitleTranslation, worker.LastRequest?.JobKind);
        Assert.Equal(new[] { "Zdravo", "svete" }, worker.LastRequest?.Texts);

        viewModel.UndoCommand.Execute(null);

        Assert.Equal(new[] { "Zdravo", "svete" }, viewModel.Words.Select(word => word.Text));
    }

    private sealed class TranslationWorkerFake(params string[] translations) : IAiWorkerClient
    {
        public AiWorkerRequest? LastRequest { get; private set; }

        public Task<AiWorkerCapabilities> CheckCapabilitiesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new AiWorkerCapabilities { WorkerReachable = true, TranslationAvailable = true });

        public async IAsyncEnumerable<AiWorkerEvent> RunAsync(
            AiWorkerRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            await Task.Yield();
            yield return new AiWorkerEvent
            {
                Type = AiWorkerEventType.Result,
                TranslatedTexts = translations
            };
            yield return new AiWorkerEvent { Type = AiWorkerEventType.Done };
        }
    }
}
