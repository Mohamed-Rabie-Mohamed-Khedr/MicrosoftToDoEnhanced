using System.Configuration;
using Microsoft.Data.SqlClient;

namespace Core.Data;

/// <summary>
/// Centralizes the "MicrosoftToDoEnhanced" connection string so repositories never
/// touch ConfigurationManager directly. Matches the convention originally used in LoginView.
/// </summary>
public static class DbConnectionFactory
{
    public const string ConnectionStringName = "MicrosoftToDoEnhanced";

    public static string GetConnectionString()
    {
        var settings = ConfigurationManager.ConnectionStrings[ConnectionStringName];
        if (settings is null || string.IsNullOrWhiteSpace(settings.ConnectionString))
            throw new InvalidOperationException(
                $"Connection string '{ConnectionStringName}' was not found in the application configuration.");

        return settings.ConnectionString;
    }

    public static SqlConnection CreateConnection() =>
        new(GetConnectionString());
}