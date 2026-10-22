using Microsoft.Data.Sqlite;

namespace Core.Data;

public static class DatabaseProvisioner
{
    public static async Task<string?> EnsureDatabaseAsync()
    {
        var connectionString = SqlHelper.ConnectionString;
        var builder = new SqliteConnectionStringBuilder(connectionString);
        var dataSource = builder.DataSource;

        if (string.IsNullOrWhiteSpace(dataSource))
            return "The connection string does not specify a database file.";

        var fullPath = Path.GetFullPath(dataSource);
        var fileExists = File.Exists(fullPath);

        if (fileExists && await IsProvisionedAsync(builder.ConnectionString))
            return null;

        try
        {
            SqliteConnection.ClearAllPools();

            if (fileExists)
            {
                File.Delete(fullPath);
                TryDeleteFile(fullPath + "-wal");
                TryDeleteFile(fullPath + "-shm");
            }

            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            await using var connection = new SqliteConnection(builder.ConnectionString);
            await connection.OpenAsync();

            await using var command = connection.CreateCommand();
            command.CommandText = LoadScript();
            await command.ExecuteNonQueryAsync();

            return null;
        }
        catch (Exception exception)
        {
            return exception.InnerException?.Message ?? exception.Message;
        }
    }

    private static async Task<bool> IsProvisionedAsync(string connectionString)
    {
        try
        {
            await using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync();

            await using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT (SELECT COUNT(*) FROM sqlite_master WHERE type = 'table'" +
                "        AND name IN ('Permissions', 'Users', 'Groups', 'GroupMembers', 'PostsInGroups', 'Tasks'," +
                "                      'TaskStatus', 'LevelOfImportance', 'RepetitionTypes', 'Planned', 'Assigned', 'Attachments')) = 12" +
                " AND (SELECT COUNT(*) FROM TaskStatus) >= 3" +
                " AND (SELECT COUNT(*) FROM LevelOfImportance) >= 3" +
                " AND (SELECT COUNT(*) FROM RepetitionTypes) >= 5;";

            var result = await command.ExecuteScalarAsync();
            return result is not null && result is not DBNull && Convert.ToInt32(result) == 1;
        }
        catch (SqliteException)
        {
            return false;
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception)
        {
        }
    }

    private static string LoadScript()
    {
        var assembly = typeof(DatabaseProvisioner).Assembly;
        using var stream = assembly.GetManifestResourceStream("Core.SQL.CreateSqliteDatabase.sql")
            ?? throw new InvalidOperationException("The embedded database setup script was not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}