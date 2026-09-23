using System.Data;
using Core.Data;
using Microsoft.Data.SqlClient;

namespace Core.Repositories;

public static class UserRepository
{
    public static async Task<bool> UserExistsAsync(string userName)
    {
        var result = await SqlHelper.ScalarAsync(
            "SELECT dbo.UserExists(@UserName)",
            new SqlParameter("@UserName", SqlDbType.NVarChar, 100) { Value = userName });
        return result is not null && result is not DBNull && Convert.ToBoolean(result);
    }

    public static async Task<string?> GetUserPasswordHashAsync(string userName)
    {
        var result = await SqlHelper.ScalarAsync(
            "SELECT dbo.GetUserPasswordHash(@UserName)",
            new SqlParameter("@UserName", SqlDbType.NVarChar, 100) { Value = userName });
        return result is null or DBNull ? null : Convert.ToString(result);
    }

    public static async Task<User?> GetUserByUserNameAsync(string userName)
    {
        var rows = await SqlHelper.QueryAsync(
            "SELECT * FROM Users WHERE UserName = @UserName", false,
            new SqlParameter("@UserName", SqlDbType.NVarChar, 100) { Value = userName });
        return rows.Count == 0 ? null : new User(rows[0]);
    }

    public static async Task<User?> GetUserByEmailAsync(string email)
    {
        var rows = await SqlHelper.QueryAsync(
            "SELECT * FROM Users WHERE UserEmail = @UserEmail", false,
            new SqlParameter("@UserEmail", SqlDbType.VarChar, 255) { Value = email });
        return rows.Count == 0 ? null : new User(rows[0]);
    }

    public static async Task<User?> GetUserAsync(int userId)
    {
        var rows = await SqlHelper.QueryAsync("GetUser", true,
            new SqlParameter("@UserID", SqlDbType.Int) { Value = userId });
        return rows.Count == 0 ? null : new User(rows[0]);
    }

    public static async Task<List<User>> GetAllUsersAsync()
    {
        var rows = await SqlHelper.QueryAsync("SELECT * FROM Users ORDER BY ShowName", false);
        return rows.Select(r => new User(r)).ToList();
    }

    public static async Task AddUserAsync(
        string userName, string showName, string? email, string passwordHash, int permissionId, string color)
    {
        await SqlHelper.ExecuteAsync("AddUser", true,
            new SqlParameter("@UserName", SqlDbType.NVarChar, 100) { Value = userName },
            new SqlParameter("@ShowName", SqlDbType.NVarChar, 100) { Value = showName },
            new SqlParameter("@UserEmail", SqlDbType.VarChar, 255)
            {
                Value = string.IsNullOrEmpty(email) ? DBNull.Value : email
            },
            new SqlParameter("@PasswordHash", SqlDbType.VarChar, 255) { Value = passwordHash },
            new SqlParameter("@PermissionID", SqlDbType.Int) { Value = permissionId },
            new SqlParameter("@Color", SqlDbType.VarChar, 16) { Value = color });
    }

    public static async Task UpdateUserAsync(
        int userId, string userName, string showName, string? email,
        string passwordHash, int permissionId, string color)
    {
        await SqlHelper.ExecuteAsync("UpdateUser", true,
            new SqlParameter("@UserID", SqlDbType.Int) { Value = userId },
            new SqlParameter("@UserName", SqlDbType.NVarChar, 100) { Value = userName },
            new SqlParameter("@ShowName", SqlDbType.NVarChar, 100) { Value = showName },
            new SqlParameter("@UserEmail", SqlDbType.VarChar, 255)
            {
                Value = string.IsNullOrEmpty(email) ? DBNull.Value : email
            },
            new SqlParameter("@PasswordHash", SqlDbType.VarChar, 255) { Value = passwordHash },
            new SqlParameter("@PermissionID", SqlDbType.Int) { Value = permissionId },
            new SqlParameter("@Color", SqlDbType.VarChar, 16) { Value = color });
    }

    public static async Task DeleteUserAsync(int userId)
    {
        await SqlHelper.ExecuteAsync("DeleteUser", true,
            new SqlParameter("@UserID", SqlDbType.Int) { Value = userId });
    }
}