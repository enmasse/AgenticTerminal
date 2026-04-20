using AgenticTerminal.Persistence;

namespace AgenticTerminal.Tests.Persistence;

public sealed class ConversationSessionStoreTests : IDisposable
{
    private readonly string _rootPath = Path.Combine(Path.GetTempPath(), $"agenticterminal-tests-{Guid.NewGuid():N}");

    [Fact]
    public async Task SaveSessionAsync_PersistsDocumentAndListsMostRecentFirst()
    {
        Directory.CreateDirectory(_rootPath);
        var store = new ConversationSessionStore(_rootPath);

        var older = new ConversationSessionDocument(
            "older",
            "Older session",
            new DateTimeOffset(2025, 1, 1, 10, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2025, 1, 1, 11, 0, 0, TimeSpan.Zero),
            [new ConversationMessage("user", "First prompt", new DateTimeOffset(2025, 1, 1, 10, 30, 0, TimeSpan.Zero))]);

        var newer = new ConversationSessionDocument(
            "newer",
            "Newer session",
            new DateTimeOffset(2025, 1, 2, 10, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2025, 1, 2, 11, 0, 0, TimeSpan.Zero),
            [new ConversationMessage("assistant", "Latest reply", new DateTimeOffset(2025, 1, 2, 10, 30, 0, TimeSpan.Zero))]);

        await store.SaveSessionAsync(older);
        await store.SaveSessionAsync(newer);

        var sessions = await store.ListSessionsAsync();
        var loaded = await store.LoadSessionAsync("newer");

        Assert.Collection(
            sessions,
            first =>
            {
                Assert.Equal("newer", first.SessionId);
                Assert.Equal("Newer session", first.Title);
                Assert.Equal("Latest reply", first.LastMessagePreview);
            },
            second => Assert.Equal("older", second.SessionId));

        Assert.NotNull(loaded);
        Assert.Equal("Newer session", loaded!.Title);
        Assert.Single(loaded.Messages);
        Assert.Equal("Latest reply", loaded.Messages[0].Content);
    }

    [Fact]
    public async Task LoadSessionAsync_ReturnsNullWhenSessionDoesNotExist()
    {
        Directory.CreateDirectory(_rootPath);
        var store = new ConversationSessionStore(_rootPath);

        var loaded = await store.LoadSessionAsync("missing");

        Assert.Null(loaded);
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootPath))
        {
            Directory.Delete(_rootPath, recursive: true);
        }
    }
}
