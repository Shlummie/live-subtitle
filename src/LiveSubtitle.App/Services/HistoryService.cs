using LiveSubtitle.Core;
using Microsoft.Data.Sqlite;

namespace LiveSubtitle.App.Services;

public sealed class HistoryService
{
    private readonly AppPaths paths;

    public HistoryService(AppPaths paths)
    {
        this.paths = paths;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        paths.EnsureCreated();
        await using var connection = Open();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode=WAL;
            CREATE TABLE IF NOT EXISTS sessions (
                id TEXT PRIMARY KEY,
                name TEXT NOT NULL,
                started_utc TEXT NOT NULL,
                ended_utc TEXT NULL
            );
            CREATE TABLE IF NOT EXISTS segments (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                session_id TEXT NOT NULL REFERENCES sessions(id) ON DELETE CASCADE,
                source_id TEXT NOT NULL,
                source_label TEXT NOT NULL,
                source_color TEXT NOT NULL,
                start_ms INTEGER NOT NULL,
                end_ms INTEGER NOT NULL,
                text TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_segments_session ON segments(session_id, start_ms);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task SaveSessionAsync(TranscriptSession session, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = Open();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        var sessionCommand = connection.CreateCommand();
        sessionCommand.Transaction = transaction;
        sessionCommand.CommandText = """
            INSERT INTO sessions(id, name, started_utc, ended_utc)
            VALUES($id, $name, $started, $ended)
            ON CONFLICT(id) DO UPDATE SET name=$name, ended_utc=$ended;
            """;
        sessionCommand.Parameters.AddWithValue("$id", session.Id);
        sessionCommand.Parameters.AddWithValue("$name", session.Name);
        sessionCommand.Parameters.AddWithValue("$started", session.StartedAtUtc.ToString("O"));
        sessionCommand.Parameters.AddWithValue("$ended", (object?)session.EndedAtUtc?.ToString("O") ?? DBNull.Value);
        await sessionCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        var delete = connection.CreateCommand();
        delete.Transaction = transaction;
        delete.CommandText = "DELETE FROM segments WHERE session_id=$id";
        delete.Parameters.AddWithValue("$id", session.Id);
        await delete.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        foreach (var segment in session.Segments.OrderBy(item => item.Start))
        {
            var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO segments(session_id, source_id, source_label, source_color, start_ms, end_ms, text)
                VALUES($session, $source, $label, $color, $start, $end, $text)
                """;
            insert.Parameters.AddWithValue("$session", session.Id);
            insert.Parameters.AddWithValue("$source", segment.SourceId);
            insert.Parameters.AddWithValue("$label", segment.SourceLabel);
            insert.Parameters.AddWithValue("$color", segment.SourceColorHex);
            insert.Parameters.AddWithValue("$start", (long)segment.Start.TotalMilliseconds);
            insert.Parameters.AddWithValue("$end", (long)segment.End.TotalMilliseconds);
            insert.Parameters.AddWithValue("$text", segment.Text);
            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<TranscriptSession>> SearchAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(paths.History)) return [];
        await using var connection = Open();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT DISTINCT s.id, s.name, s.started_utc, s.ended_utc
            FROM sessions s
            LEFT JOIN segments g ON g.session_id=s.id
            WHERE $query='' OR s.name LIKE $pattern ESCAPE '\' OR g.text LIKE $pattern ESCAPE '\'
            ORDER BY s.started_utc DESC
            LIMIT 100
            """;
        command.Parameters.AddWithValue("$query", query);
        command.Parameters.AddWithValue("$pattern", $"%{EscapeLike(query)}%");
        var sessions = new List<TranscriptSession>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            sessions.Add(new TranscriptSession
            {
                Id = reader.GetString(0),
                Name = reader.GetString(1),
                StartedAtUtc = DateTimeOffset.Parse(reader.GetString(2)),
                EndedAtUtc = reader.IsDBNull(3) ? null : DateTimeOffset.Parse(reader.GetString(3)),
                Saved = true
            });
        }
        return sessions;
    }

    public async Task<TranscriptSession?> LoadAsync(string id, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(paths.History)) return null;
        await using var connection = Open();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        var sessionCommand = connection.CreateCommand();
        sessionCommand.CommandText = "SELECT name, started_utc, ended_utc FROM sessions WHERE id=$id";
        sessionCommand.Parameters.AddWithValue("$id", id);
        await using var sessionReader = await sessionCommand.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await sessionReader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;
        var name = sessionReader.GetString(0);
        var started = DateTimeOffset.Parse(sessionReader.GetString(1));
        DateTimeOffset? ended = sessionReader.IsDBNull(2) ? null : DateTimeOffset.Parse(sessionReader.GetString(2));
        await sessionReader.DisposeAsync().ConfigureAwait(false);

        var segmentCommand = connection.CreateCommand();
        segmentCommand.CommandText = """
            SELECT id, source_id, source_label, source_color, start_ms, end_ms, text
            FROM segments WHERE session_id=$id ORDER BY start_ms, id
            """;
        segmentCommand.Parameters.AddWithValue("$id", id);
        var segments = new List<TranscriptSegment>();
        await using var segmentReader = await segmentCommand.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await segmentReader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            segments.Add(new TranscriptSegment
            {
                Id = segmentReader.GetInt64(0),
                SessionId = id,
                SourceId = segmentReader.GetString(1),
                SourceLabel = segmentReader.GetString(2),
                SourceColorHex = segmentReader.GetString(3),
                Start = TimeSpan.FromMilliseconds(segmentReader.GetInt64(4)),
                End = TimeSpan.FromMilliseconds(segmentReader.GetInt64(5)),
                Text = segmentReader.GetString(6)
            });
        }

        return new TranscriptSession
        {
            Id = id,
            Name = name,
            StartedAtUtc = started,
            EndedAtUtc = ended,
            Saved = true,
            Segments = segments
        };
    }

    public async Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(paths.History)) return;
        await using var connection = Open();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM sessions WHERE id=$id";
        command.Parameters.AddWithValue("$id", id);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private SqliteConnection Open()
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = paths.History,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            ForeignKeys = true,
            Pooling = false
        };
        return new SqliteConnection(builder.ToString());
    }

    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}
