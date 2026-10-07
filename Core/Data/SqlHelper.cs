using System.Configuration;
using System.Data;
using Microsoft.Data.SqlClient;

namespace Core.Data;

internal static class SqlHelper
{
    private const string ConnectionStringName = "MicrosoftToDoEnhanced";
    private const string ConnectionStringEnvironmentVariable = "MICROSOFTTODOENHANCED_CONNECTIONSTRING";

    private static SqlConnection CreateConnection() =>
        new(GetConnectionString());

    private static string GetConnectionString()
    {
        var fromEnvironment = Environment.GetEnvironmentVariable(ConnectionStringEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(fromEnvironment))
            return fromEnvironment;

        var settings = ConfigurationManager.ConnectionStrings[ConnectionStringName];
        if (settings is null || string.IsNullOrWhiteSpace(settings.ConnectionString))
            throw new InvalidOperationException(
                $"Connection string '{ConnectionStringName}' was not found in the application configuration.");

        return settings.ConnectionString;
    }

    private static async Task<T> ExecuteCoreAsync<T>(
        string sql,
        bool storedProcedure,
        SqlParameter[] parameters,
        Func<SqlCommand,
        Task<T>> execute)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync();

        using var command = new SqlCommand(sql, connection);
        command.CommandType = storedProcedure ? CommandType.StoredProcedure : CommandType.Text;
        if (parameters.Length > 0)
            command.Parameters.AddRange(parameters);

        try
        {
            return await execute(command);
        }
        catch (SqlException exception)
        {
            throw Translate(exception);
        }
    }

    public static Task<IReadOnlyList<DataRow>> QueryAsync(
        string sql, bool storedProcedure = false, params SqlParameter[] parameters) =>
        ExecuteCoreAsync(sql, storedProcedure, parameters, ReadResultSetAsync);

    private static async Task<IReadOnlyList<DataRow>> ReadResultSetAsync(SqlCommand command)
    {
        await using var reader = await command.ExecuteReaderAsync();
        var table = new DataTable();
        table.Load(reader);

        var rows = new List<DataRow>(table.Rows.Count);
        foreach (DataRow row in table.Rows)
            rows.Add(row);
        return rows;
    }

    public static Task<object?> ScalarAsync(string sql, params SqlParameter[] parameters) =>
        ExecuteCoreAsync(sql, false, parameters, static command => command.ExecuteScalarAsync());

    public static async Task ExecuteAsync(string sql, bool storedProcedure = false, params SqlParameter[] parameters) =>
        await ExecuteCoreAsync<int>(sql, storedProcedure, parameters, static command =>
            command.ExecuteNonQueryAsync());

    public static async Task<int?> ExecuteProcReturnAsync(string procedure, params SqlParameter[] parameters)
    {
        var value = await ExecuteCoreAsync<object?>(procedure, true, parameters,
            static command => command.ExecuteScalarAsync());
        return value is null || value is DBNull ? (int?)null : Convert.ToInt32(value);
    }

    public static async Task<MultiResultReader> QueryMultipleAsync(string procedure, params SqlParameter[] parameters)
    {
        var connection = CreateConnection();
        await connection.OpenAsync();

        var command = new SqlCommand(procedure, connection)
        {
            CommandType = CommandType.StoredProcedure
        };
        if (parameters.Length > 0)
            command.Parameters.AddRange(parameters);

        try
        {
            var reader = await command.ExecuteReaderAsync();
            return new MultiResultReader(connection, command, reader);
        }
        catch (SqlException exception)
        {
            await command.DisposeAsync();
            await connection.DisposeAsync();
            throw Translate(exception);
        }
    }

    public static string TaskIdsJson(IReadOnlyList<int> taskIds) =>
        "[" + string.Join(",", taskIds) + "]";

    private static Exception Translate(SqlException exception)
    {
        switch (exception.Number)
        {
            case 50001:
                return new UnauthorizedAccessException(exception.Message, exception);
            case 50002:
            case 50003:
            case 50004:
                return new InvalidOperationException(exception.Message, exception);
            case 2601:
            case 2627:
                return new InvalidOperationException("That record already exists.", exception);
            default:
                return exception;
        }
    }
}

public sealed class MultiResultReader : IAsyncDisposable
{
    private readonly SqlConnection _connection;
    private readonly SqlCommand _command;
    private readonly SqlDataReader _reader;
    private bool _started;

    internal MultiResultReader(SqlConnection connection, SqlCommand command, SqlDataReader reader)
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

        var table = new DataTable();
        var fieldCount = _reader.FieldCount;
        var values = new object[fieldCount];
        for (var i = 0; i < fieldCount; i++)
            table.Columns.Add(_reader.GetName(i), _reader.GetFieldType(i));

        while (await _reader.ReadAsync())
        {
            _reader.GetValues(values);
            table.Rows.Add(values);
        }

        return table;
    }

    public async ValueTask DisposeAsync()
    {
        await _reader.DisposeAsync();
        await _command.DisposeAsync();
        await _connection.DisposeAsync();
    }
}
