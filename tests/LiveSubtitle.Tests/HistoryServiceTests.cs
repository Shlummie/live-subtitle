using LiveSubtitle.App.Services;
using LiveSubtitle.Core;

namespace LiveSubtitle.Tests;

public sealed class HistoryServiceTests
{
    [Fact]
    public async Task HistoryIsCreatedOnlyBySaveAndEscapesSearchWildcards()
    {
        var root = Path.Combine(Path.GetTempPath(), "LiveSubtitle.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new AppPaths(root);
            var history = new HistoryService(paths);

            Assert.Empty(await history.SearchAsync("anything"));
            Assert.False(File.Exists(paths.History));

            var session = new TranscriptSession
            {
                Id = "session-1",
                Name = "Literal search",
                StartedAtUtc = DateTimeOffset.UtcNow,
                EndedAtUtc = DateTimeOffset.UtcNow.AddSeconds(2),
                Saved = true,
                Segments =
                [
                    new TranscriptSegment
                    {
                        SessionId = "session-1",
                        SourceId = "source-1",
                        SourceLabel = "Fixture",
                        SourceColorHex = "#69A7FF",
                        Start = TimeSpan.Zero,
                        End = TimeSpan.FromSeconds(2),
                        Text = "Progress is 100%_safe \\ locally"
                    }
                ]
            };

            await history.SaveSessionAsync(session);

            Assert.True(File.Exists(paths.History));
            var matches = await history.SearchAsync("100%_safe \\");
            Assert.Single(matches);
            var loaded = await history.LoadAsync(session.Id);
            Assert.NotNull(loaded);
            Assert.Equal(session.Segments[0].Text, Assert.Single(loaded.Segments).Text);

            await history.DeleteAsync(session.Id);
            Assert.Empty(await history.SearchAsync(string.Empty));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
