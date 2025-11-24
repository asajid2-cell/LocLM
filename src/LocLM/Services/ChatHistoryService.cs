using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using LocLM.Models;

namespace LocLM.Services;

public class ChatHistoryService : IChatHistoryService
{
    private readonly string _dbPath;
    private readonly string _connectionString;
    private readonly SemaphoreSlim _mutex = new(1, 1);
    private const int SchemaVersion = 1;
    private const int MaxSessions = 200;

    public ChatHistoryService()
    {
        var appDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LocLM");
        Directory.CreateDirectory(appDataFolder);
        _dbPath = Path.Combine(appDataFolder, "chat_history.db");
        _connectionString = $"Data Source={_dbPath};Cache=Shared;Pooling=True";
    }

    public async Task InitializeAsync()
    {
        using var connection = await OpenAsync();

        await EnableWalAsync(connection);
        await RunMigrationsAsync(connection);
        await ApplyRetentionAsync(connection);
    }

    private async Task<SqliteConnection> OpenAsync()
    {
        var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        return conn;
    }

    private static async Task EnableWalAsync(SqliteConnection connection)
    {
        using var wal = connection.CreateCommand();
        wal.CommandText = "PRAGMA journal_mode=WAL;";
        await wal.ExecuteNonQueryAsync();
    }

    private static async Task RunMigrationsAsync(SqliteConnection connection)
    {
        // Ensure migrations table
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = @"CREATE TABLE IF NOT EXISTS Migrations (Version INTEGER PRIMARY KEY);";
            await cmd.ExecuteNonQueryAsync();
        }

        var currentVersion = 0;
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "SELECT IFNULL(MAX(Version),0) FROM Migrations;";
            var result = await cmd.ExecuteScalarAsync();
            currentVersion = Convert.ToInt32(result);
        }

        // Apply migrations incrementally
        if (currentVersion < 1)
        {
            var createSessionsTable = @"
                CREATE TABLE IF NOT EXISTS ChatSessions (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Title TEXT NOT NULL,
                    CreatedAt TEXT NOT NULL,
                    UpdatedAt TEXT NOT NULL,
                    ModelName TEXT NOT NULL,
                    Mode TEXT NOT NULL
                );";

            var createMessagesTable = @"
                CREATE TABLE IF NOT EXISTS ChatMessages (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    SessionId INTEGER NOT NULL,
                    Role TEXT NOT NULL,
                    Content TEXT NOT NULL,
                    CreatedAt TEXT NOT NULL,
                    FOREIGN KEY (SessionId) REFERENCES ChatSessions(Id) ON DELETE CASCADE
                );";

            var createIndexCommand = @"
                CREATE INDEX IF NOT EXISTS idx_messages_session ON ChatMessages(SessionId);
                CREATE INDEX IF NOT EXISTS idx_sessions_updated ON ChatSessions(UpdatedAt DESC);";

            using var command = connection.CreateCommand();
            command.CommandText = createSessionsTable;
            await command.ExecuteNonQueryAsync();

            command.CommandText = createMessagesTable;
            await command.ExecuteNonQueryAsync();

            command.CommandText = createIndexCommand;
            await command.ExecuteNonQueryAsync();

            using var insertVersion = connection.CreateCommand();
            insertVersion.CommandText = "INSERT INTO Migrations (Version) VALUES (1);";
            await insertVersion.ExecuteNonQueryAsync();
        }
    }

    private static async Task ApplyRetentionAsync(SqliteConnection connection)
    {
        // Limit session count
        using var deleteOld = connection.CreateCommand();
        deleteOld.CommandText = @"
            DELETE FROM ChatSessions WHERE Id IN (
                SELECT Id FROM ChatSessions ORDER BY UpdatedAt DESC LIMIT -1 OFFSET @keep
            );";
        deleteOld.Parameters.AddWithValue("@keep", MaxSessions);
        await deleteOld.ExecuteNonQueryAsync();

        // Vacuum occasionally
        using var vacuum = connection.CreateCommand();
        vacuum.CommandText = "VACUUM;";
        await vacuum.ExecuteNonQueryAsync();
    }

    public async Task<int> CreateSessionAsync(string title, string modelName, string mode)
    {
        await _mutex.WaitAsync();
        try
        {
            using var connection = await OpenAsync();

            var now = DateTime.UtcNow.ToString("o");
            using var command = connection.CreateCommand();
            command.CommandText = @"
                INSERT INTO ChatSessions (Title, CreatedAt, UpdatedAt, ModelName, Mode)
                VALUES (@title, @now, @now, @modelName, @mode);
                SELECT last_insert_rowid();
            ";
            command.Parameters.AddWithValue("@title", title);
            command.Parameters.AddWithValue("@now", now);
            command.Parameters.AddWithValue("@modelName", modelName);
            command.Parameters.AddWithValue("@mode", mode);

            var result = await command.ExecuteScalarAsync();
            return Convert.ToInt32(result);
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task<List<ChatSession>> GetAllSessionsAsync()
    {
        var sessions = new List<ChatSession>();
        try
        {
            using var connection = await OpenAsync();

            using var command = connection.CreateCommand();
            command.CommandText = "SELECT Id, Title, CreatedAt, UpdatedAt, ModelName, Mode FROM ChatSessions ORDER BY UpdatedAt DESC LIMIT 100";

            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                sessions.Add(new ChatSession
                {
                    Id = reader.GetInt32(0),
                    Title = reader.GetString(1),
                    CreatedAt = DateTime.Parse(reader.GetString(2)),
                    UpdatedAt = DateTime.Parse(reader.GetString(3)),
                    ModelName = reader.GetString(4),
                    Mode = reader.GetString(5)
                });
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ChatHistory] Error getting sessions: {ex.Message}");
        }
        return sessions;
    }

    public async Task<ChatSession?> GetSessionAsync(int sessionId)
    {
        using var connection = await OpenAsync();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Title, CreatedAt, UpdatedAt, ModelName, Mode FROM ChatSessions WHERE Id = @id";
        command.Parameters.AddWithValue("@id", sessionId);

        using var reader = await command.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return new ChatSession
            {
                Id = reader.GetInt32(0),
                Title = reader.GetString(1),
                CreatedAt = DateTime.Parse(reader.GetString(2)),
                UpdatedAt = DateTime.Parse(reader.GetString(3)),
                ModelName = reader.GetString(4),
                Mode = reader.GetString(5)
            };
        }
        return null;
    }

    public async Task UpdateSessionAsync(int sessionId, string title)
    {
        using var connection = await OpenAsync();

        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE ChatSessions SET Title = @title, UpdatedAt = @now WHERE Id = @id";
        command.Parameters.AddWithValue("@title", title);
        command.Parameters.AddWithValue("@now", DateTime.UtcNow.ToString("o"));
        command.Parameters.AddWithValue("@id", sessionId);

        await command.ExecuteNonQueryAsync();
    }

    public async Task DeleteSessionAsync(int sessionId)
    {
        try
        {
            using var connection = await OpenAsync();

            // Delete messages first (cascade doesn't always work with SQLite)
            using var deleteMessages = connection.CreateCommand();
            deleteMessages.CommandText = "DELETE FROM ChatMessages WHERE SessionId = @id";
            deleteMessages.Parameters.AddWithValue("@id", sessionId);
            await deleteMessages.ExecuteNonQueryAsync();

            // Then delete session
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM ChatSessions WHERE Id = @id";
            command.Parameters.AddWithValue("@id", sessionId);
            await command.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ChatHistory] Error deleting session: {ex.Message}");
        }
    }

    public async Task<int> AddMessageAsync(int sessionId, string role, string content)
    {
        try
        {
            // Truncate very long content to prevent database bloat (1MB limit)
            if (content.Length > 1024 * 1024)
            {
                content = content.Substring(0, 1024 * 1024) + "\n[Content truncated due to size]";
            }

            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            using var command = connection.CreateCommand();
            command.CommandText = @"
                INSERT INTO ChatMessages (SessionId, Role, Content, CreatedAt)
                VALUES (@sessionId, @role, @content, @createdAt);
                SELECT last_insert_rowid();
            ";
            command.Parameters.AddWithValue("@sessionId", sessionId);
            command.Parameters.AddWithValue("@role", role);
            command.Parameters.AddWithValue("@content", content);
            command.Parameters.AddWithValue("@createdAt", DateTime.UtcNow.ToString("o"));

            var result = await command.ExecuteScalarAsync();
            return Convert.ToInt32(result);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ChatHistory] Error adding message: {ex.Message}");
            return -1;
        }
    }

    public async Task<List<ChatMessage>> GetSessionMessagesAsync(int sessionId)
    {
        using var connection = await OpenAsync();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, SessionId, Role, Content, CreatedAt FROM ChatMessages WHERE SessionId = @sessionId ORDER BY CreatedAt ASC";
        command.Parameters.AddWithValue("@sessionId", sessionId);

        var messages = new List<ChatMessage>();
        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            messages.Add(new ChatMessage
            {
                Id = reader.GetInt32(0),
                SessionId = reader.GetInt32(1),
                Role = reader.GetString(2),
                Content = reader.GetString(3),
                CreatedAt = DateTime.Parse(reader.GetString(4))
            });
        }
        return messages;
    }

    public async Task UpdateSessionTimestampAsync(int sessionId)
    {
        using var connection = await OpenAsync();

        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE ChatSessions SET UpdatedAt = @now WHERE Id = @id";
        command.Parameters.AddWithValue("@now", DateTime.UtcNow.ToString("o"));
        command.Parameters.AddWithValue("@id", sessionId);

        await command.ExecuteNonQueryAsync();
    }
}
