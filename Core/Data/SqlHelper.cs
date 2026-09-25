using System.Data;
using Microsoft.Data.SqlClient;

namespace Core.Data;

/// <summary>
/// Thin ADO.NET wrapper shared by all repositories. Executes parameterized commands
/// (stored procedures or text) over a short-lived connection and never string-concatenates SQL.
///
/// It also:
///  - validates that string/byte values fit their declared parameter size up-front
///    (so truncation can never silently corrupt data),
///  - supports table-valued parameters,
///  - reads the stored procedure's NewID column (the first column of its first row,
///    produced by "SELECT ... AS NewID") via ExecuteProcReturnAsync,
///  - reads multiple result sets via QueryMultipleAsync,
///  - translates the canonical server THROW codes into typed .NET exceptions:
///       50001 -> UnauthorizedAccessException   (permission / authorization denied)
///       50002/50003/50004 -> InvalidOperationException (not found / invalid / not owner)
///       2601/2627 -> InvalidOperationException (duplicate key)
/// </summary>
internal static class SqlHelper
{
    private static async Task<T> ExecuteCoreAsync<T>(
        string sql,
        bool storedProcedure,
        SqlParameter[] parameters,
        Func<SqlCommand, Task<T>> execute)
    {
        EnsureValidParameters(parameters);

        await using var connection = DbConnectionFactory.CreateConnection();
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
        ExecuteCoreAsync(sql, storedProcedure, parameters, static command =>
        {
            return ReadResultSetAsync(command);
        });

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

    /// <summary>
    /// Executes a stored procedure and returns the first column of the first row of its
    /// result set (the NewID column emitted by "SELECT ... AS NewID").
    /// </summary>
    public static async Task<int?> ExecuteProcReturnAsync(string procedure, params SqlParameter[] parameters)
    {
        var value = await ExecuteCoreAsync<object?>(procedure, true, parameters,
            static command => command.ExecuteScalarAsync());
        return value is null || value is DBNull ? (int?)null : Convert.ToInt32(value);
    }

    /// <summary>
    /// Runs a stored procedure whose result set is consumed one page at a time.
    /// Calling ReadAsync repeatedly walks successive result sets; call
    /// <see cref="MultiResultReader.ReadAsync"/> until it returns null.
    /// </summary>
    public static async Task<MultiResultReader> QueryMultipleAsync(string procedure, params SqlParameter[] parameters)
    {
        EnsureValidParameters(parameters);

        var connection = DbConnectionFactory.CreateConnection();
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

    /* ------------------------- Table-valued parameters ------------------------- */

    public static DataTable BuildTaskIdTable(IReadOnlyList<int> taskIds)
    {
        var table = new DataTable();
        table.Columns.Add("TaskID", typeof(int));
        table.Columns.Add("SortOrder", typeof(int));

        for (var i = 0; i < taskIds.Count; i++)
            table.Rows.Add(taskIds[i], i + 1);

        return table;
    }

    public static DataTable BuildUserIdTable(IReadOnlyList<int> userIds)
    {
        var table = new DataTable();
        table.Columns.Add("UserID", typeof(int));
        table.Columns.Add("SortOrder", typeof(int));

        for (var i = 0; i < userIds.Count; i++)
            table.Rows.Add(userIds[i], i + 1);

        return table;
    }

    public static SqlParameter Tvp(string name, string typeName, DataTable table) =>
        new(name, SqlDbType.Structured)
        {
            TypeName = $"dbo.{typeName}",
            Value = table
        };

    /* ----------------------------- Validation ----------------------------- */

    private static void EnsureValidParameters(SqlParameter[] parameters)
    {
        foreach (var parameter in parameters)
        {
            if (parameter.SqlValue is null || parameter.SqlValue is DBNull)
                continue;

            switch (parameter.SqlValue)
            {
                case string value when parameter.Size > 0 && parameter.Size < int.MaxValue
                    && value.Length > parameter.Size:
                    throw new ArgumentOutOfRangeException(
                        parameter.ParameterName,
                        $"Parameter '{parameter.ParameterName}' value exceeds its declared size of {parameter.Size} characters.");
                case byte[] value when parameter.Size > 0 && parameter.Size < int.MaxValue
                    && value.Length > parameter.Size:
                    throw new ArgumentOutOfRangeException(
                        parameter.ParameterName,
                        $"Parameter '{parameter.ParameterName}' value exceeds its declared size of {parameter.Size} bytes.");
            }
        }
    }

    /* --------------------------- Exception mapping --------------------------- */

    private static Exception Translate(SqlException exception)
    {
        switch (exception.Number)
        {
            case 50001:
                return new UnauthorizedAccessException(exception.Message, exception);
            case 50002: // not found / invalid state
            case 50003: // invalid input
            case 50004: // not owner / administrator
                return new InvalidOperationException(exception.Message, exception);
            case 2601:
            case 2627: // unique constraint
                return new InvalidOperationException("That record already exists.", exception);
            default:
                return exception;
        }
    }
}

/// <summary>
/// Sequential reader over a stored procedure producing several result sets.
/// Owns the underlying connection/command; dispose with <see cref="DisposeAsync"/>.
/// </summary>
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

    /// <summary>
    /// Loads the next result set into a DataTable, or returns null when exhausted.
    /// </summary>
    /// <remarks>
    /// The rows are copied manually instead of using <see cref="DataTable.Load(System.Data.IDataReader)"/>:
    /// Load silently advances past an intermediate result set when a stored procedure returns more
    /// than two result sets, which would silently reorder the pages consumed by callers.
    /// </remarks>
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
        for (int i = 0; i < fieldCount; i++)
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