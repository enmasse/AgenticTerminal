using System.Text.Json;

namespace AgenticTerminal.Persistence;

public sealed class ConversationSessionStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _sessionsDirectoryPath;

    public ConversationSessionStore(string rootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        _sessionsDirectoryPath = Path.Combine(rootPath, "sessions");
    }

    public async Task SaveSessionAsync(ConversationSessionDocument session, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        Directory.CreateDirectory(_sessionsDirectoryPath);
        var filePath = GetSessionFilePath(session.SessionId);

        await using var stream = File.Create(filePath);
        await JsonSerializer.SerializeAsync(stream, session, SerializerOptions, cancellationToken);
    }

    public async Task<ConversationSessionDocument?> LoadSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        var filePath = GetSessionFilePath(sessionId);

        if (!File.Exists(filePath))
        {
            return null;
        }

        await using var stream = File.OpenRead(filePath);
        return await JsonSerializer.DeserializeAsync<ConversationSessionDocument>(stream, SerializerOptions, cancellationToken);
    }

    public async Task<IReadOnlyList<ConversationSessionSummary>> ListSessionsAsync(CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_sessionsDirectoryPath))
        {
            return [];
        }

        var sessions = new List<ConversationSessionSummary>();

        foreach (var filePath in Directory.EnumerateFiles(_sessionsDirectoryPath, "*.json", SearchOption.TopDirectoryOnly))
        {
            await using var stream = File.OpenRead(filePath);
            var document = await JsonSerializer.DeserializeAsync<ConversationSessionDocument>(stream, SerializerOptions, cancellationToken);
            if (document is null)
            {
                continue;
            }

            sessions.Add(new ConversationSessionSummary(
                document.SessionId,
                document.Title,
                document.CreatedAt,
                document.UpdatedAt,
                document.Messages.LastOrDefault()?.Content ?? string.Empty));
        }

        return sessions
            .OrderByDescending(session => session.UpdatedAt)
            .ToArray();
    }

    private string GetSessionFilePath(string sessionId)
    {
        return Path.Combine(_sessionsDirectoryPath, $"{sessionId}.json");
    }
}
