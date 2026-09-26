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
        return rows.Count == 0 ? null : Scrubbed(rows[0]);
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

    public static async Task<User?> GetUserAsync(int userId, int actingUserId)
    {
        var rows = await SqlHelper.QueryAsync("GetUser", true,
            new SqlParameter("@UserID", SqlDbType.Int) { Value = userId },
            new SqlParameter("@ActingUserID", SqlDbType.Int) { Value = actingUserId });
        return rows.Count == 0 ? null : Scrubbed(rows[0]);
    }

    public static async Task<List<User>> GetAllUsersAsync()
    {
        var rows = await SqlHelper.QueryAsync("SELECT * FROM Users ORDER BY ShowName", false);
        return rows.Select(Scrubbed).ToList();
    }

    private static User Scrubbed(DataRow row)
    {
        var user = new User(row);
        user.PasswordHash = string.Empty;
        return user;
    }

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

    public static async Task UpdateUserProfileAsync(
        int userId, int actingUserId, string userName, string showName, string? email, string color)
    {
        await SqlHelper.ExecuteAsync("UpdateUserProfile", true,
            new SqlParameter("@UserID", SqlDbType.Int) { Value = userId },
            new SqlParameter("@UserName", SqlDbType.NVarChar, 100) { Value = userName },
            new SqlParameter("@ShowName", SqlDbType.NVarChar, 100) { Value = showName },
            new SqlParameter("@UserEmail", SqlDbType.VarChar, 255)
            {
                Value = string.IsNullOrWhiteSpace(email) ? DBNull.Value : email
            },
            new SqlParameter("@Color", SqlDbType.VarChar, 16) { Value = color },
            new SqlParameter("@ActingUserID", SqlDbType.Int) { Value = actingUserId });
    }

    public static async Task UpdatePasswordHashAsync(int userId, int actingUserId, string passwordHash)
    {
        await SqlHelper.ExecuteAsync("UpdatePasswordHash", true,
            new SqlParameter("@UserID", SqlDbType.Int) { Value = userId },
            new SqlParameter("@PasswordHash", SqlDbType.VarChar, DbLimits.MaxPasswordHashLength) { Value = passwordHash },
            new SqlParameter("@ActingUserID", SqlDbType.Int) { Value = actingUserId });
    }

    public static async Task DeleteUserAsync(int userId, int actingUserId)
    {
        await SqlHelper.ExecuteAsync("DeleteUser", true,
            new SqlParameter("@UserID", SqlDbType.Int) { Value = userId },
            new SqlParameter("@ActingUserID", SqlDbType.Int) { Value = actingUserId });
    }

    public sealed record SignInResult(bool Success, User? User, bool NeedsRehash)
    {
        /// <summary>Non-zero when the caller is rate limited and must wait before retrying.</summary>
        public TimeSpan RetryAfter { get; init; }

        public bool IsThrottled => RetryAfter > TimeSpan.Zero;

        public static SignInResult Succeeded(User user) => new(true, user, false);
        public static SignInResult Failed() => new(false, null, false);
        public static SignInResult Migrated(User user) => new(true, user, true);
        public static SignInResult Throttled(TimeSpan retryAfter) => new(false, null, false) { RetryAfter = retryAfter };
    }

    public sealed record RegisterResult(bool Success, string? ErrorMessage)
    {
        public static RegisterResult Succeeded() => new(true, null);
        public static RegisterResult Failed(string message) => new(false, message);
    }

    /// <summary>
    /// In-process brute-force throttle. Tracks consecutive failures per user name and
    /// escalates an artificial delay plus a temporary lock-out, so guessing a password
    /// is orders of magnitude slower than a legitimate sign-in.
    /// </summary>
    private sealed class SignInThrottle
    {
        private const int FreeAttempts = 4;

        // Without a cap, unknown user names would grow the table without limit.
        private const int MaxTrackedNames = 512;

        private static readonly TimeSpan BasePenalty = TimeSpan.FromMilliseconds(250);
        private static readonly TimeSpan MaxPenalty = TimeSpan.FromSeconds(4);
        private static readonly TimeSpan BaseLockout = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan MaxLockout = TimeSpan.FromSeconds(60);

        private sealed class Entry
        {
            public int Failures;
            public long LockedUntil;
        }

        // Keyed case-insensitively so "Alice" and "alice" share one attempt budget.
        private readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);
        private readonly Lock _gate = new();

        public TimeSpan GetRetryAfter(string userName)
        {
            lock (_gate)
            {
                if (!_entries.TryGetValue(userName, out var entry))
                    return TimeSpan.Zero;

                var now = Environment.TickCount64;
                return entry.LockedUntil > now
                    ? TimeSpan.FromMilliseconds(entry.LockedUntil - now)
                    : TimeSpan.Zero;
            }
        }

        /// <summary>Records a failed attempt and returns how long to stall before replying.</summary>
        public TimeSpan RegisterFailure(string userName)
        {
            lock (_gate)
            {
                EvictIfFull();

                if (!_entries.TryGetValue(userName, out var entry))
                    _entries[userName] = entry = new Entry();

                entry.Failures++;

                if (entry.Failures > FreeAttempts)
                {
                    var scale = Math.Min(entry.Failures - FreeAttempts - 1, 8);
                    var lockout = TimeSpan.FromTicks(BaseLockout.Ticks * (1L << scale));
                    if (lockout > MaxLockout)
                        lockout = MaxLockout;

                    entry.LockedUntil = Environment.TickCount64 + (long)lockout.TotalMilliseconds;
                }

                var penalty = TimeSpan.FromTicks(BasePenalty.Ticks * (1L << Math.Min(entry.Failures - 1, 8)));
                return penalty > MaxPenalty ? MaxPenalty : penalty;
            }
        }

        public void RegisterSuccess(string userName)
        {
            lock (_gate)
            {
                _entries.Remove(userName);
            }
        }

        /// <summary>
        /// Drops expired entries first, then the entries closest to their free-attempt
        /// budget, so real accounts under attack keep their counters.
        /// </summary>
        private void EvictIfFull()
        {
            if (_entries.Count < MaxTrackedNames)
                return;

            long now = Environment.TickCount64;
            foreach (string name in _entries.Where(p => p.Value.LockedUntil <= now).Select(p => p.Key).ToList())
                _entries.Remove(name);

            while (_entries.Count >= MaxTrackedNames)
            {
                var victim = _entries
                    .OrderBy(p => p.Value.Failures)
                    .ThenBy(p => p.Value.LockedUntil)
                    .First();

                _entries.Remove(victim.Key);
            }
        }
    }

    private static readonly SignInThrottle SignInAttempts = new();

    /// <summary>
    /// A throw-away hash used to spend roughly the same CPU on an unknown user name as
    /// on a known one, so sign-in timing does not reveal which accounts exist.
    /// </summary>
    private static readonly string DecoyHash =
        PasswordHasher.Hash(Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)));

    public static async Task<SignInResult> SignInAsync(string userName, string password)
    {
        var trimmedName = userName?.Trim();
        if (string.IsNullOrEmpty(trimmedName) || string.IsNullOrEmpty(password))
            return SignInResult.Failed();

        var key = trimmedName.ToUpperInvariant();

        var retryAfter = SignInAttempts.GetRetryAfter(key);
        if (retryAfter > TimeSpan.Zero)
            return SignInResult.Throttled(retryAfter);

        var storedHash = await GetUserPasswordHashAsync(trimmedName);
        if (string.IsNullOrEmpty(storedHash))
        {
            // Spend comparable time, then charge the attempt to the throttle.
            PasswordHasher.Verify(password, DecoyHash);
            await Task.Delay(SignInAttempts.RegisterFailure(key));
            return SignInResult.Failed();
        }

        var verification = PasswordHasher.Verify(password, storedHash);
        if (!verification.Success)
        {
            await Task.Delay(SignInAttempts.RegisterFailure(key));
            return SignInResult.Failed();
        }

        var user = await GetUserByUserNameAsync(trimmedName);
        if (user is null)
        {
            await Task.Delay(SignInAttempts.RegisterFailure(key));
            return SignInResult.Failed();
        }

        SignInAttempts.RegisterSuccess(key);

        if (verification.NeedsRehash || !storedHash.StartsWith("pbkdf2", StringComparison.Ordinal))
        {
            await UpdatePasswordHashAsync(user.UserID, user.UserID, PasswordHasher.Hash(password));
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
