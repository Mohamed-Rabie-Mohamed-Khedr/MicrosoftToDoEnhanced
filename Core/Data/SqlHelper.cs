using System.Configuration;
using System.Data;
using System.Data.Common;
using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Core.Data;

internal static class SqlHelper
{
    private const string ConnectionStringName = "MicrosoftToDoEnhanced";
    private const string ConnectionStringEnvironmentVariable = "MICROSOFTTODOENHANCED_CONNECTIONSTRING";

    private static readonly string DefaultDataSource = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MicrosoftToDoEnhanced",
        "todo.db");

    private static readonly HashSet<string> DateColumnNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CreationDate",
        "PlannedStartDate",
        "PlannedEndDate",
        "DueStartDate",
        "DueEndDate",
        "CreatedAt",
        "PostDate"
    };

    internal static string ConnectionString => GetConnectionString();

    private static SqliteConnection CreateConnection()
    {
        var connection = new SqliteConnection(GetConnectionString());
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys = ON;";
        command.ExecuteNonQuery();

        return connection;
    }

    private static string GetConnectionString()
    {
        var fromEnvironment = Environment.GetEnvironmentVariable(ConnectionStringEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(fromEnvironment))
            return fromEnvironment;

        var settings = ConfigurationManager.ConnectionStrings[ConnectionStringName];
        if (settings is not null && !string.IsNullOrWhiteSpace(settings.ConnectionString))
            return settings.ConnectionString;

        return $"Data Source={DefaultDataSource};Foreign Keys=True";
    }

    private static SqliteCommand CreateCommand(
        SqliteConnection connection, SqliteTransaction? transaction, string sql, SqliteParameter[] parameters)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        if (transaction is not null)
            command.Transaction = transaction;
        if (parameters.Length > 0)
            command.Parameters.AddRange(parameters);
        return command;
    }

    public static async Task<IReadOnlyList<DataRow>> QueryAsync(string sql, params SqliteParameter[] parameters)
    {
        await using var connection = CreateConnection();
        return await QueryAsync(connection, null, sql, parameters);
    }

    public static async Task<IReadOnlyList<DataRow>> QueryAsync(
        SqliteConnection connection, SqliteTransaction? transaction, string sql, params SqliteParameter[] parameters)
    {
        try
        {
            await using var command = CreateCommand(connection, transaction, sql, parameters);
            await using var reader = await command.ExecuteReaderAsync();
            return ReadRows(BuildTable(reader));
        }
        catch (SqliteException exception)
        {
            throw Translate(exception);
        }
    }

    internal static DataTable BuildTable(DbDataReader reader)
    {
        var table = new DataTable();
        var fieldCount = reader.FieldCount;
        var dateColumns = new bool[fieldCount];

        for (var i = 0; i < fieldCount; i++)
        {
            var name = reader.GetName(i);
            var type = GetFieldTypeSafe(reader, i);
            if (type == typeof(string) && DateColumnNames.Contains(name))
            {
                type = typeof(DateTime);
                dateColumns[i] = true;
            }

            table.Columns.Add(name, type);
        }

        while (reader.Read())
        {
            var row = table.NewRow();
            for (var i = 0; i < fieldCount; i++)
            {
                var value = reader.GetValue(i);
                if (dateColumns[i] && value is string text)
                    value = DateTime.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.None);
                row[i] = value ?? DBNull.Value;
            }

            table.Rows.Add(row);
        }

        return table;
    }

    private static Type GetFieldTypeSafe(DbDataReader reader, int ordinal)
    {
        try
        {
            return reader.GetFieldType(ordinal);
        }
        catch (Exception)
        {
            return typeof(object);
        }
    }

    internal static IReadOnlyList<DataRow> ReadRows(DataTable table)
    {
        var rows = new List<DataRow>(table.Rows.Count);
        foreach (DataRow row in table.Rows)
            rows.Add(row);
        return rows;
    }

    public static async Task<object?> ScalarAsync(string sql, params SqliteParameter[] parameters)
    {
        await using var connection = CreateConnection();
        return await ScalarAsync(connection, null, sql, parameters);
    }

    public static async Task<object?> ScalarAsync(
        SqliteConnection connection, SqliteTransaction? transaction, string sql, params SqliteParameter[] parameters)
    {
        try
        {
            await using var command = CreateCommand(connection, transaction, sql, parameters);
            return await command.ExecuteScalarAsync();
        }
        catch (SqliteException exception)
        {
            throw Translate(exception);
        }
    }

    public static async Task<int> ExecuteAsync(string sql, params SqliteParameter[] parameters)
    {
        await using var connection = CreateConnection();
        return await ExecuteAsync(connection, null, sql, parameters);
    }

    public static async Task<int> ExecuteAsync(
        SqliteConnection connection, SqliteTransaction? transaction, string sql, params SqliteParameter[] parameters)
    {
        try
        {
            await using var command = CreateCommand(connection, transaction, sql, parameters);
            return await command.ExecuteNonQueryAsync();
        }
        catch (SqliteException exception)
        {
            throw Translate(exception);
        }
    }

    public static async Task<int?> ExecuteReturnAsync(string sql, params SqliteParameter[] parameters)
    {
        await using var connection = CreateConnection();
        return await ExecuteReturnAsync(connection, null, sql, parameters);
    }

    public static async Task<int?> ExecuteReturnAsync(
        SqliteConnection connection, SqliteTransaction? transaction, string sql, params SqliteParameter[] parameters)
    {
        try
        {
            var text = sql.TrimEnd().EndsWith(';')
                ? sql + " SELECT last_insert_rowid();"
                : sql + "; SELECT last_insert_rowid();";

            await using var command = CreateCommand(connection, transaction, text, parameters);
            var value = await command.ExecuteScalarAsync();
            return value is null || value is DBNull ? null : Convert.ToInt32(value);
        }
        catch (SqliteException exception)
        {
            throw Translate(exception);
        }
    }

    public static async Task InTransactionAsync(Func<SqliteConnection, SqliteTransaction, Task> operation)
    {
        await using var connection = CreateConnection();
        using var transaction = connection.BeginTransaction();
        try
        {
            await operation(connection, transaction);
            transaction.Commit();
        }
        catch
        {
            try
            {
                transaction.Rollback();
            }
            catch (Exception)
            {
            }

            throw;
        }
    }

    public static async Task<T> InTransactionAsync<T>(Func<SqliteConnection, SqliteTransaction, Task<T>> operation)
    {
        await using var connection = CreateConnection();
        using var transaction = connection.BeginTransaction();
        try
        {
            var result = await operation(connection, transaction);
            transaction.Commit();
            return result;
        }
        catch
        {
            try
            {
                transaction.Rollback();
            }
            catch (Exception)
            {
            }

            throw;
        }
    }

    public static async Task<MultiResultReader> QueryMultipleAsync(string sql, params SqliteParameter[] parameters)
    {
        var connection = CreateConnection();
        SqliteCommand? command = null;
        try
        {
            command = CreateCommand(connection, null, sql, parameters);
            var reader = await command.ExecuteReaderAsync();
            return new MultiResultReader(connection, command, reader);
        }
        catch (SqliteException exception)
        {
            if (command is not null)
                await command.DisposeAsync();
            await connection.DisposeAsync();
            throw Translate(exception);
        }
    }

    public static (string Values, SqliteParameter[] Parameters) InValues(IReadOnlyList<int> ids)
    {
        var values = new string[ids.Count];
        var parameters = new SqliteParameter[ids.Count];
        for (var i = 0; i < ids.Count; i++)
        {
            values[i] = $"(@t{i}, {i})";
            parameters[i] = new SqliteParameter($"@t{i}", ids[i]);
        }

        return (string.Join(", ", values), parameters);
    }

    private static Exception Translate(SqliteException exception)
    {
        if (exception.SqliteExtendedErrorCode is 1555 or 2067)
            return new InvalidOperationException("That record already exists.", exception);

        return exception;
    }
}

public sealed class MultiResultReader : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly SqliteCommand _command;
    private readonly DbDataReader _reader;
    private bool _started;

    internal MultiResultReader(SqliteConnection connection, SqliteCommand command, DbDataReader reader)
    {
        _connection = connection;
        _command = command;
        _reader = reader;
    }

    public async Task<DataTable?> ReadAsync()
    {
        if (_started)
        {
            if (!await _reader.NextResultAsync())
                return null;
        }
        else
        {
            _started = true;
        }

        return SqlHelper.BuildTable(_reader);
    }

    public async ValueTask DisposeAsync()
    {
        await _reader.DisposeAsync();
        await _command.DisposeAsync();
        await _connection.DisposeAsync();
    }
}
