using System.Configuration;
using Microsoft.Data.SqlClient;

namespace Core.Data;

public static class DbConnectionFactory
{
    public const string ConnectionStringName = "MicrosoftToDoEnhanced";

    private const string ConnectionStringEnvironmentVariable = "MICROSOFTTODOENHANCED_CONNECTIONSTRING";

    public static string GetConnectionString()
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

    public static SqlConnection CreateConnection() =>
        new(GetConnectionString());
}
