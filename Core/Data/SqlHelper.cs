using System.Data;
using Microsoft.Data.SqlClient;

namespace Core.Data;

/// <summary>
/// Thin ADO.NET wrapper shared by all repositories. Executes parameterized commands
/// (stored procedures or text) over a short-lived connection and never string-concatenates SQL.
/// </summary>
internal static class SqlHelper
{
    public static async Task<IReadOnlyList<DataRow>> QueryAsync(
        string sql, bool storedProcedure, params SqlParameter[] parameters)
    {
        await using var connection = DbConnectionFactory.CreateConnection();
        await connection.OpenAsync();
        using var command = new SqlCommand(sql, connection);
        command.CommandType = storedProcedure ? CommandType.StoredProcedure : CommandType.Text;
        if (parameters.Length > 0)
            command.Parameters.AddRange(parameters);

        using var reader = await command.ExecuteReaderAsync();
        var table = new DataTable();
        table.Load(reader);

        var rows = new List<DataRow>(table.Rows.Count);
        foreach (DataRow row in table.Rows)
            rows.Add(row);
        return rows;
    }

    public static async Task<object?> ScalarAsync(string sql, params SqlParameter[] parameters)
    {
        await using var connection = DbConnectionFactory.CreateConnection();
        await connection.OpenAsync();
        using var command = new SqlCommand(sql, connection);
        if (parameters.Length > 0)
            command.Parameters.AddRange(parameters);

        return await command.ExecuteScalarAsync();
    }

    public static async Task ExecuteAsync(string sql, bool storedProcedure, params SqlParameter[] parameters)
    {
        await using var connection = DbConnectionFactory.CreateConnection();
        await connection.OpenAsync();
        using var command = new SqlCommand(sql, connection);
        command.CommandType = storedProcedure ? CommandType.StoredProcedure : CommandType.Text;
        if (parameters.Length > 0)
            command.Parameters.AddRange(parameters);

        await command.ExecuteNonQueryAsync();
    }
}