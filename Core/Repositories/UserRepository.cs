using System.Data;
using Core.Data;
using Core.Security;
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
            "SELECT * FROM Users WHERE UserName = @UserName",
            false,
            new SqlParameter("@UserName", SqlDbType.NVarChar, 100) { Value = userName });
        return rows.Count == 0 ? null : new User(rows[0]);
    }

    public static async Task<User?> GetUserByEmailAsync(string email)
    {
        var rows = await SqlHelper.QueryAsync(
            "SELECT * FROM Users WHERE UserEmail = @UserEmail",
            false,
            new SqlParameter("@UserEmail", SqlDbType.VarChar, 255)
            {
                Value = email.Trim().ToLowerInvariant()
            });
        if (rows.Count == 0)
            return null;

        var user = new User(rows[0]);
        user.PasswordHash = string.Empty;
        return user;
    }

    public static async Task<User?> GetUserAsync(int userId)
    {
        var rows = await SqlHelper.QueryAsync("GetUser", true,
            new SqlParameter("@UserID", SqlDbType.Int) { Value = userId });
        if (rows.Count == 0)
            return null;

        var user = new User(rows[0]);
        user.PasswordHash = string.Empty;
        return user;
    }

    public static async Task<List<User>> GetAllUsersAsync()
    {
        var rows = await SqlHelper.QueryAsync("SELECT * FROM Users ORDER BY ShowName", false);
        return rows.Select(r =>
        {
            var user = new User(r);
            user.PasswordHash = string.Empty;
            return user;
        }).ToList();
    }

    /// <summary>
    /// Creates a user. Returns the new identity. Raises InvalidOperationException
    /// when the username is already taken (server THROW 50003).
    /// </summary>
    public static async Task<int> AddUserAsync(
        string userName, string showName, string? email, string passwordHash, int permissionId, string color)
    {
        var userId = await SqlHelper.ExecuteProcReturnAsync("AddUser",
            new SqlParameter("@UserName", SqlDbType.NVarChar, 100) { Value = userName },
            new SqlParameter("@ShowName", SqlDbType.NVarChar, 100) { Value = showName },
            new SqlParameter("@UserEmail", SqlDbType.VarChar, 255)
            {
                Value = string.IsNullOrWhiteSpace(email) ? DBNull.Value : email.Trim()
            },
            new SqlParameter("@PasswordHash", SqlDbType.VarChar, DbLimits.MaxPasswordHashLength) { Value = passwordHash },
            new SqlParameter("@PermissionID", SqlDbType.Int) { Value = permissionId },
            new SqlParameter("@Color", SqlDbType.VarChar, 16) { Value = color });

        return userId ?? throw new InvalidOperationException("The user could not be added.");
    }

    /// <summary>
    /// Self-service profile edit: identity and display fields only, never password/permissions.
    /// </summary>
    public static async Task UpdateUserProfileAsync(
        int userId, string userName, string showName, string? email, string color)
    {
        await SqlHelper.ExecuteAsync("UpdateUserProfile", true,
            new SqlParameter("@UserID", SqlDbType.Int) { Value = userId },
            new SqlParameter("@UserName", SqlDbType.NVarChar, 100) { Value = userName },
            new SqlParameter("@ShowName", SqlDbType.NVarChar, 100) { Value = showName },
            new SqlParameter("@UserEmail", SqlDbType.VarChar, 255)
            {
                Value = string.IsNullOrWhiteSpace(email) ? DBNull.Value : email
            },
            new SqlParameter("@Color", SqlDbType.VarChar, 16) { Value = color });
    }

    public static async Task UpdatePasswordHashAsync(int userId, string passwordHash)
    {
        await SqlHelper.ExecuteAsync("UpdatePasswordHash", true,
            new SqlParameter("@UserID", SqlDbType.Int) { Value = userId },
            new SqlParameter("@PasswordHash", SqlDbType.VarChar, DbLimits.MaxPasswordHashLength) { Value = passwordHash });
    }

    public static async Task DeleteUserAsync(int userId, int actingUserId)
    {
        await SqlHelper.ExecuteAsync("DeleteUser", true,
            new SqlParameter("@UserID", SqlDbType.Int) { Value = userId },
            new SqlParameter("@ActingUserID", SqlDbType.Int) { Value = actingUserId });
    }

    /// <summary>Outcome of a password check. NeedsRehash means the stored verifier predates
    /// PBKDF2 and was transparently upgraded on this sign-in.</summary>
    public sealed record SignInResult(bool Success, User? User, bool NeedsRehash)
    {
        public static SignInResult Succeeded(User user) => new(true, user, false);
        public static SignInResult Failed() => new(false, null, false);
        public static SignInResult Migrated(User user) => new(true, user, true);
    }

    public sealed record RegisterResult(bool Success, string? ErrorMessage)
    {
        public static RegisterResult Succeeded() => new(true, null);
        public static RegisterResult Failed(string message) => new(false, message);
    }

    /// <summary>
    /// Password-based authentication. Uses PBKDF2 via <see cref="PasswordHasher"/> and
    /// transparently migrates legacy unsalted SHA-256 verifiers on the next successful sign-in.
    /// Throws when the database is unreachable, so callers can tell "wrong credentials"
    /// (a Failed result) apart from "service unavailable" (an exception).
    /// </summary>
    public static async Task<SignInResult> SignInAsync(string userName, string password)
    {
        var trimmedName = userName?.Trim();
        if (string.IsNullOrEmpty(trimmedName) || string.IsNullOrEmpty(password))
            return SignInResult.Failed();

        var storedHash = await GetUserPasswordHashAsync(trimmedName);
        if (string.IsNullOrEmpty(storedHash))
            return SignInResult.Failed();

        var verification = PasswordHasher.Verify(password, storedHash);
        if (!verification.Success)
            return SignInResult.Failed();

        var user = await GetUserByUserNameAsync(trimmedName);
        if (user is null)
            return SignInResult.Failed();

        user.PasswordHash = string.Empty;

        if (verification.NeedsRehash || !storedHash.StartsWith("pbkdf2", StringComparison.Ordinal))
        {
            await UpdatePasswordHashAsync(user.UserID, PasswordHasher.Hash(password));
            return SignInResult.Migrated(user);
        }

        return SignInResult.Succeeded(user);
    }

    public static async Task<RegisterResult> RegisterAsync(
        string userName, string showName, string? email, string password, string color)
    {
        var trimmedName = userName?.Trim();
        if (string.IsNullOrEmpty(trimmedName))
            return RegisterResult.Failed("User name is required");

        if (trimmedName.Contains(' ') || trimmedName.Contains('\t'))
            return RegisterResult.Failed("User name cannot contain spaces");

        if (trimmedName.Length < DbLimits.MinUserNameLength
            || trimmedName.Length > DbLimits.MaxUserNameLength)
            return RegisterResult.Failed(
                $"User name must be {DbLimits.MinUserNameLength}-{DbLimits.MaxUserNameLength} characters");

        var trimmedShowName = string.IsNullOrWhiteSpace(showName) ? trimmedName : showName.Trim();
        if (trimmedShowName.Length > DbLimits.MaxShowNameLength)
            return RegisterResult.Failed($"Display name cannot exceed {DbLimits.MaxShowNameLength} characters");

        var trimmedEmail = string.IsNullOrWhiteSpace(email) ? null : email.Trim();
        if (trimmedEmail is not null && trimmedEmail.Length > DbLimits.MaxEmailLength)
            return RegisterResult.Failed($"Email cannot exceed {DbLimits.MaxEmailLength} characters");

        if (string.IsNullOrEmpty(password))
            return RegisterResult.Failed("Password is required");

        if (password.Length < DbLimits.MinPasswordLength || password.Length > DbLimits.MaxPasswordLength)
            return RegisterResult.Failed(
                $"Password must be {DbLimits.MinPasswordLength}-{DbLimits.MaxPasswordLength} characters");

        if (await UserExistsAsync(trimmedName))
            return RegisterResult.Failed("This username is already taken");

        await AddUserAsync(
            trimmedName,
            trimmedShowName,
            trimmedEmail,
            PasswordHasher.Hash(password),
            (int)PermissionLevel.Person,
            string.IsNullOrWhiteSpace(color) ? "#0078D4" : color);

        return RegisterResult.Succeeded();
    }
}