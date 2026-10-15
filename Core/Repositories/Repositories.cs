using System.Data;
using System.Security.Cryptography;
using System.Text;
using Core.Data;
using Microsoft.Data.SqlClient;

namespace Core.Repositories;

public sealed record TaskListExtras(
    int TaskID,
    int SubtaskCount,
    int SubtaskCompletedCount,
    bool HasAttachments,
    int? AssignedToUserID,
    string? AssignedToName,
    string? AssignedToColor,
    DateTime? DueStartDate,
    DateTime? DueEndDate,
    int? RepetitionTypeID,
    string? RepetitionName);

public sealed record TaskCounts(
    int AllCount,
    int TodayCount,
    int ImportantCount,
    int PlannedCount);

public sealed record TaskStatusResult(
    int TaskStatusID,
    DateTime? PlannedStartDate,
    DateTime? PlannedEndDate,
    bool StatusForced);

public sealed record LookupBundle(
    List<TodoTaskStatus> Statuses,
    List<LevelOfImportance> Levels,
    List<RepetitionType> RepetitionTypes,
    List<User> Users);

public sealed record PostFeedItem(
    int PostID,
    string PostContent,
    DateTime PostDate,
    string ShowName,
    string UserColor);

public enum PlannedScope
{
    All,
    Daily
}

public static class TaskRepository
{
    public static async Task<int> AddTaskAsync(
        int? taskParentId, int taskStatusId, int userId, int? groupId,
        string taskName, int importanceLevelId, string? description, string color)
    {
        var taskId = await SaveTaskAsync(
            null, taskParentId, taskStatusId, userId, groupId,
            taskName, importanceLevelId, description, color, null, null, 1, null);

        return taskId ?? throw new InvalidOperationException("The task could not be added.");
    }

    public static async Task<int?> SaveTaskAsync(
        int? taskId, int? taskParentId, int taskStatusId, int userId, int? groupId,
        string taskName, int importanceLevelId, string? description, string color,
        DateTime? dueStartDate, DateTime? dueEndDate, int repetitionTypeId,
        int? assigneeUserId)
    {
        return await SqlHelper.ExecuteProcReturnAsync("SaveTask",
            new SqlParameter("@TaskID", SqlDbType.Int) { Value = taskId ?? (object)DBNull.Value },
            new SqlParameter("@TaskParentID", SqlDbType.Int)
            {
                Value = taskParentId ?? (object)DBNull.Value
            },
            new SqlParameter("@TaskStatusID", SqlDbType.Int) { Value = taskStatusId },
            new SqlParameter("@UserID", SqlDbType.Int) { Value = userId },
            new SqlParameter("@GroupID", SqlDbType.Int)
            {
                Value = groupId ?? (object)DBNull.Value
            },
            new SqlParameter("@TaskName", SqlDbType.NVarChar, 100) { Value = taskName },
            new SqlParameter("@ImportanceLevelID", SqlDbType.Int) { Value = importanceLevelId },
            new SqlParameter("@Description", SqlDbType.NVarChar, -1)
            {
                Value = description is null ? DBNull.Value : description
            },
            new SqlParameter("@Color", SqlDbType.VarChar, 16)
            {
                Value = string.IsNullOrWhiteSpace(color) ? "#FFFFFF" : color
            },
            new SqlParameter("@DueStartDate", SqlDbType.DateTime2)
            {
                Value = dueStartDate ?? (object)DBNull.Value
            },
            new SqlParameter("@DueEndDate", SqlDbType.DateTime2)
            {
                Value = dueEndDate ?? (object)DBNull.Value
            },
            new SqlParameter("@RepetitionTypeID", SqlDbType.Int) { Value = repetitionTypeId },
            new SqlParameter("@AssigneeUserID", SqlDbType.Int)
            {
                Value = assigneeUserId ?? (object)DBNull.Value
            });
    }

    public static async Task<TaskStatusResult> UpdateTaskStatusAsync(int taskId, int taskStatusId, int actingUserId)
    {
        var rows = await SqlHelper.QueryAsync("UpdateTaskStatus", true,
            new SqlParameter("@TaskID", SqlDbType.Int) { Value = taskId },
            new SqlParameter("@TaskStatusID", SqlDbType.Int) { Value = taskStatusId },
            new SqlParameter("@ActingUserID", SqlDbType.Int) { Value = actingUserId });

        if (rows.Count == 0)
            return new TaskStatusResult(taskStatusId, null, null, false);

        var row = rows[0];
        return new TaskStatusResult(
            Convert.ToInt32(row["TaskStatusID"]),
            GetNullableDateTime(row, "PlannedStartDate"),
            GetNullableDateTime(row, "PlannedEndDate"),
            !row.IsNull("StatusForced") && Convert.ToBoolean(row["StatusForced"]));
    }

    public static Task DeleteTaskAsync(int taskId, int actingUserId) =>
        SqlHelper.ExecuteAsync("DeleteTask", true,
            new SqlParameter("@TaskID", SqlDbType.Int) { Value = taskId },
            new SqlParameter("@ActingUserID", SqlDbType.Int) { Value = actingUserId });

    public static async Task<List<TodoTask>> GetParentTasksAsync(int userId, bool byImportance)
    {
        var rows = await SqlHelper.QueryAsync("GetParentTasks", true,
            new SqlParameter("@UserID", SqlDbType.Int) { Value = userId },
            new SqlParameter("@ByImportance", SqlDbType.Bit) { Value = byImportance });
        return rows.Select(r => new TodoTask(r)).ToList();
    }

    public static async Task<List<TodoTask>> GetGroupParentTasksAsync(int groupId, int actingUserId)
    {
        var rows = await SqlHelper.QueryAsync("GetParentTasks", true,
            new SqlParameter("@UserID", SqlDbType.Int) { Value = actingUserId },
            new SqlParameter("@GroupID", SqlDbType.Int) { Value = groupId });
        return rows.Select(r => new TodoTask(r)).ToList();
    }

    public static async Task<List<TodoTask>> GetChildTasksAsync(int taskParentId, int actingUserId)
    {
        var rows = await SqlHelper.QueryAsync("GetTaskChildrensByRanking", true,
            new SqlParameter("@TaskParentID", SqlDbType.Int) { Value = taskParentId },
            new SqlParameter("@ActingUserID", SqlDbType.Int) { Value = actingUserId });
        return rows.Select(r => new TodoTask(r)).ToList();
    }

    public static async Task<List<TodoTask>> GetChildTasksByIdsAsync(
        IReadOnlyList<int> parentIds, int actingUserId)
    {
        if (parentIds.Count == 0)
            return new List<TodoTask>();

        var rows = await SqlHelper.QueryAsync("GetChildTasksByIds", true,
            new SqlParameter("@TaskParentIDs", SqlDbType.NVarChar, -1)
            {
                Value = SqlHelper.TaskIdsJson(parentIds)
            },
            new SqlParameter("@ActingUserID", SqlDbType.Int) { Value = actingUserId });
        return rows.Select(r => new TodoTask(r)).ToList();
    }

    public static Task ReorderTasksAsync(
        int userId, int? groupId, int? parentTaskId, IReadOnlyList<int> orderedTaskIds) =>
        SqlHelper.ExecuteAsync("ReorderTasks", true,
            new SqlParameter("@UserID", SqlDbType.Int) { Value = userId },
            new SqlParameter("@GroupID", SqlDbType.Int)
            {
                Value = groupId ?? (object)DBNull.Value
            },
            new SqlParameter("@TaskParentID", SqlDbType.Int)
            {
                Value = parentTaskId ?? (object)DBNull.Value
            },
            new SqlParameter("@TaskIDs", SqlDbType.NVarChar, -1)
            {
                Value = SqlHelper.TaskIdsJson(orderedTaskIds)
            });

    public static async Task<List<TaskListExtras>> GetTaskListExtrasAsync(IReadOnlyList<int> taskIds, int actingUserId)
    {
        var rows = await SqlHelper.QueryAsync("GetTaskListExtras", true,
            new SqlParameter("@TaskIDs", SqlDbType.NVarChar, -1)
            {
                Value = SqlHelper.TaskIdsJson(taskIds)
            },
            new SqlParameter("@ActingUserID", SqlDbType.Int) { Value = actingUserId });

        return rows.Select(r => new TaskListExtras(
            Convert.ToInt32(r["TaskID"]),
            Convert.ToInt32(r["SubtaskCount"]),
            Convert.ToInt32(r["SubtaskCompletedCount"]),
            Convert.ToBoolean(r["HasAttachments"]),
            GetNullableInt(r, "AssignedToUserID"),
            GetString(r, "AssignedToName"),
            GetString(r, "AssignedToColor"),
            GetNullableDateTime(r, "DueStartDate"),
            GetNullableDateTime(r, "DueEndDate"),
            GetNullableInt(r, "RepetitionTypeID"),
            GetString(r, "RepetitionName"))).ToList();
    }

    public static async Task<TaskCounts> GetTaskCountsAsync(int userId)
    {
        var rows = await SqlHelper.QueryAsync("GetTaskCounts", true,
            new SqlParameter("@UserID", SqlDbType.Int) { Value = userId },
            new SqlParameter("@IncludeCompleted", SqlDbType.Bit) { Value = true });

        if (rows.Count == 0)
            return new TaskCounts(0, 0, 0, 0);

        var row = rows[0];
        return new TaskCounts(
            Convert.ToInt32(row["AllCount"]),
            Convert.ToInt32(row["TodayCount"]),
            Convert.ToInt32(row["ImportantCount"]),
            Convert.ToInt32(row["PlannedCount"]));
    }

    public static async Task<LookupBundle> GetLookupsAsync()
    {
        await using var results = await SqlHelper.QueryMultipleAsync("GetLookups");

        var statuses = ReadRows(await results.ReadAsync(),
            r => new TodoTaskStatus
            {
                TaskStatusID = Convert.ToInt32(r["ID"]),
                StatusName = GetString(r, "Name") ?? string.Empty
            });

        var repetitionTypes = ReadRows(await results.ReadAsync(),
            r => new RepetitionType
            {
                RepetitionTypeID = Convert.ToInt32(r["ID"]),
                RepetitionName = GetString(r, "Name") ?? string.Empty
            });

        var levels = ReadRows(await results.ReadAsync(),
            r => new LevelOfImportance
            {
                LevelOfImportanceID = Convert.ToInt32(r["ID"]),
                LevelName = GetString(r, "Name") ?? string.Empty,
                Color = GetString(r, "Color") ?? string.Empty
            });

        var users = ReadRows(await results.ReadAsync(),
            r => new User
            {
                UserID = Convert.ToInt32(r["UserID"]),
                ShowName = GetString(r, "ShowName") ?? string.Empty,
                Color = GetString(r, "Color") ?? string.Empty
            });

        return new LookupBundle(statuses, levels, repetitionTypes, users);
    }

    private static List<T> ReadRows<T>(DataTable? table, Func<DataRow, T> map)
    {
        var items = new List<T>();
        if (table is null)
            return items;

        foreach (DataRow row in table.Rows)
            items.Add(map(row));

        return items;
    }

    private static string? GetString(DataRow row, string column) =>
        row.IsNull(column) ? null : Convert.ToString(row[column]);

    private static int? GetNullableInt(DataRow row, string column) =>
        row.IsNull(column) ? null : Convert.ToInt32(row[column]);

    private static DateTime? GetNullableDateTime(DataRow row, string column) =>
        row.IsNull(column) ? null : Convert.ToDateTime(row[column]);
}

public static class GroupRepository
{
    public static async Task<int> AddGroupAsync(int adminId, string groupName, string? description, string color)
    {
        var groupId = await SqlHelper.ExecuteProcReturnAsync("AddGroup",
            new SqlParameter("@AdminID", SqlDbType.Int) { Value = adminId },
            new SqlParameter("@GroupName", SqlDbType.NVarChar, 100) { Value = groupName },
            new SqlParameter("@GroupDescription", SqlDbType.NVarChar, -1)
            {
                Value = string.IsNullOrEmpty(description) ? DBNull.Value : description
            },
            new SqlParameter("@Color", SqlDbType.VarChar, 16)
            {
                Value = string.IsNullOrEmpty(color) ? "#FFFFFF" : color
            });

        return groupId ?? throw new InvalidOperationException("The group could not be created.");
    }

    public static Task UpdateGroupAsync(
        int groupId, int actingUserId, string groupName, string? description, string color) =>
        SqlHelper.ExecuteAsync("UpdateGroup", true,
            new SqlParameter("@GroupID", SqlDbType.Int) { Value = groupId },
            new SqlParameter("@ActingUserID", SqlDbType.Int) { Value = actingUserId },
            new SqlParameter("@GroupName", SqlDbType.NVarChar, 100) { Value = groupName },
            new SqlParameter("@GroupDescription", SqlDbType.NVarChar, -1)
            {
                Value = string.IsNullOrEmpty(description) ? DBNull.Value : description
            },
            new SqlParameter("@Color", SqlDbType.VarChar, 16)
            {
                Value = string.IsNullOrEmpty(color) ? "#FFFFFF" : color
            });

    public static Task DeleteGroupAsync(int groupId, int actingUserId) =>
        SqlHelper.ExecuteAsync("DeleteGroup", true,
            new SqlParameter("@GroupID", SqlDbType.Int) { Value = groupId },
            new SqlParameter("@ActingUserID", SqlDbType.Int) { Value = actingUserId });

    public static async Task<List<Group>> GetGroupsForUserAsync(int userId)
    {
        var rows = await SqlHelper.QueryAsync(
            """
            SELECT DISTINCT G.* FROM Groups G
            LEFT JOIN GroupMembers GM ON G.GroupID = GM.GroupID
            WHERE G.AdminID = @UserID OR GM.UserID = @UserID
            ORDER BY G.GroupName
            """,
            false,
            new SqlParameter("@UserID", SqlDbType.Int) { Value = userId });
        return rows.Select(r => new Group(r)).ToList();
    }

    public static Task AddGroupMemberAsync(int groupId, int userId, int actingUserId) =>
        SqlHelper.ExecuteAsync("AddGroupMember", true,
            new SqlParameter("@GroupID", SqlDbType.Int) { Value = groupId },
            new SqlParameter("@UserID", SqlDbType.Int) { Value = userId },
            new SqlParameter("@ActingUserID", SqlDbType.Int) { Value = actingUserId });

    public static Task DeleteGroupMemberAsync(int groupId, int userId, int actingUserId) =>
        SqlHelper.ExecuteAsync("DeleteGroupMember", true,
            new SqlParameter("@GroupID", SqlDbType.Int) { Value = groupId },
            new SqlParameter("@UserID", SqlDbType.Int) { Value = userId },
            new SqlParameter("@ActingUserID", SqlDbType.Int) { Value = actingUserId });

    public static async Task<List<User>> GetGroupMembersAsync(int groupId, int actingUserId)
    {
        var rows = await SqlHelper.QueryAsync(
            """
            IF dbo.IsGroupMember(@ActingUserID, @GroupID) = 0
                THROW 50001, N'You do not have access to this group.', 1;

            SELECT U.UserID, U.ShowName, U.UserEmail, U.Color FROM Users U
            WHERE U.UserID = (SELECT AdminID FROM Groups WHERE GroupID = @GroupID)
               OR U.UserID IN (SELECT UserID FROM GroupMembers WHERE GroupID = @GroupID)
            ORDER BY U.ShowName
            """,
            false,
            new SqlParameter("@GroupID", SqlDbType.Int) { Value = groupId },
            new SqlParameter("@ActingUserID", SqlDbType.Int) { Value = actingUserId });

        return rows.Select(r => new User(r)).ToList();
    }
}

public static class PlannedTaskRepository
{
    public static async Task<int> AddAsync(
        int taskId, DateTime startDate, DateTime? endDate, int repetitionTypeId, int actingUserId)
    {
        var plannedId = await SqlHelper.ExecuteProcReturnAsync("AddPlanned",
            new SqlParameter("@TaskID", SqlDbType.Int) { Value = taskId },
            new SqlParameter("@PlannedStartDate", SqlDbType.DateTime2) { Value = startDate },
            new SqlParameter("@PlannedEndDate", SqlDbType.DateTime2)
            {
                Value = endDate ?? (object)DBNull.Value
            },
            new SqlParameter("@RepetitionTypeID", SqlDbType.Int) { Value = repetitionTypeId },
            new SqlParameter("@ActingUserID", SqlDbType.Int) { Value = actingUserId });

        return plannedId ?? throw new InvalidOperationException("The plan could not be saved.");
    }

    public static Task<int?> AutoUpdateAsync() =>
        SqlHelper.ExecuteProcReturnAsync("AutoUpdatePlanneds");

    public static async Task<PlannedTask?> GetPlannedAsync(int taskId)
    {
        var rows = await SqlHelper.QueryAsync(
            "SELECT TOP 1 TaskID, PlannedStartDate, PlannedEndDate, RepetitionTypeID FROM Planned WHERE TaskID = @TaskID ORDER BY PlannedStartDate DESC", false,
            new SqlParameter("@TaskID", SqlDbType.Int) { Value = taskId });
        return rows.Count == 0 ? null : new PlannedTask(rows[0]);
    }

    public static async Task<IReadOnlyDictionary<int, PlannedTask>> GetPlannedForTasksAsync(
        IReadOnlyList<int> taskIds)
    {
        var dict = new Dictionary<int, PlannedTask>(taskIds.Count);
        if (taskIds.Count == 0)
            return dict;

        var rows = await SqlHelper.QueryAsync(
            @"SELECT p.TaskID, p.PlannedStartDate, p.PlannedEndDate, p.RepetitionTypeID
              FROM Planned p
              INNER JOIN OPENJSON(@TaskIDs) J ON p.TaskID = CAST(J.value AS INT)
              WHERE NOT EXISTS (
                  SELECT 1
                  FROM Planned p2
                  WHERE p2.TaskID = p.TaskID
                    AND p2.PlannedStartDate > p.PlannedStartDate
              )", false,
            new SqlParameter("@TaskIDs", SqlDbType.NVarChar, -1)
            {
                Value = SqlHelper.TaskIdsJson(taskIds)
            });

        foreach (DataRow row in rows)
        {
            var planned = new PlannedTask(row);
            dict[planned.TaskID] = planned;
        }

        return dict;
    }

    public static async Task<List<TodoTask>> GetTasksAsync(int userId, PlannedScope scope)
    {
        var rows = await SqlHelper.QueryAsync("GetTasksPlanned", true,
            new SqlParameter("@UserID", SqlDbType.Int) { Value = userId },
            new SqlParameter("@Scope", SqlDbType.TinyInt) { Value = (byte)scope });
        return rows.Select(r => new TodoTask(r)).ToList();
    }
}

public static class AssignedTaskRepository
{
    public static async Task<User?> GetAssignedUserAsync(int taskId, int actingUserId)
    {
        var rows = await SqlHelper.QueryAsync(
            """
            SELECT TOP 1 U.UserID, U.ShowName, U.UserEmail, U.Color FROM Assigned A
            JOIN Users U ON A.ToUserID = U.UserID
            JOIN Tasks T ON A.TaskID = T.TaskID
            WHERE A.TaskID = @TaskID
              AND ((T.GroupID IS NOT NULL AND dbo.IsGroupMember(@ActingUserID, T.GroupID) = 1)
                OR (T.GroupID IS NULL AND T.UserID = @ActingUserID))
            """,
            false,
            new SqlParameter("@TaskID", SqlDbType.Int) { Value = taskId },
            new SqlParameter("@ActingUserID", SqlDbType.Int) { Value = actingUserId });

        return rows.Count == 0 ? null : new User(rows[0]);
    }
}

public static class AttachmentRepository
{
    public static async Task<List<Attachment>> GetAttachmentInfosAsync(IReadOnlyList<int> taskIds, int actingUserId)
    {
        var rows = await SqlHelper.QueryAsync("GetAttachmentInfos", true,
            new SqlParameter("@TaskIDs", SqlDbType.NVarChar, -1)
            {
                Value = SqlHelper.TaskIdsJson(taskIds)
            },
            new SqlParameter("@ActingUserID", SqlDbType.Int) { Value = actingUserId });

        return rows.Select(r => new Attachment
        {
            AttachmentID = Convert.ToInt32(r["AttachmentID"]),
            FileName = Convert.ToString(r["FileName"]) ?? string.Empty,
            FileSizeKB = Convert.ToInt32(r["FileSizeKB"])
        }).ToList();
    }

    public static async Task<int> AddAttachmentAsync(
        int taskId, string fileName, byte[] fileData, int fileSizeKB, int actingUserId)
    {
        if (fileData.Length > DbLimits.MaxAttachmentBytes)
            throw new ArgumentOutOfRangeException(nameof(fileData),
                "Attachment exceeds the maximum allowed size of 10 MiB.");

        var attachmentId = await SqlHelper.ExecuteProcReturnAsync("AddAttachment",
            new SqlParameter("@TaskID", SqlDbType.Int) { Value = taskId },
            new SqlParameter("@FileName", SqlDbType.NVarChar, DbLimits.MaxFileNameLength) { Value = fileName },
            new SqlParameter("@FileData", SqlDbType.VarBinary, -1) { Value = fileData },
            new SqlParameter("@FileSizeKB", SqlDbType.Int) { Value = fileSizeKB },
            new SqlParameter("@ActingUserID", SqlDbType.Int) { Value = actingUserId });

        return attachmentId ?? throw new InvalidOperationException("The attachment could not be saved.");
    }

    public static Task DeleteAttachmentAsync(int attachmentId, int actingUserId) =>
        SqlHelper.ExecuteAsync("DeleteAttachment", true,
            new SqlParameter("@AttachmentID", SqlDbType.Int) { Value = attachmentId },
            new SqlParameter("@ActingUserID", SqlDbType.Int) { Value = actingUserId });
}

public static class PostRepository
{
    public static async Task<int> AddPostAsync(int groupId, int userId, string content)
    {
        var postId = await SqlHelper.ExecuteProcReturnAsync("AddPostInGroup",
            new SqlParameter("@GroupID", SqlDbType.Int) { Value = groupId },
            new SqlParameter("@UserID", SqlDbType.Int) { Value = userId },
            new SqlParameter("@PostContent", SqlDbType.NVarChar, -1) { Value = content });

        return postId ?? throw new InvalidOperationException("The post could not be created.");
    }

    public static async Task<List<PostFeedItem>> GetPostsAsync(
        int groupId, int actingUserId, int? beforePostId = null, int take = 20)
    {
        var rows = await SqlHelper.QueryAsync("GetPostsInGroup", true,
            new SqlParameter("@GroupID", SqlDbType.Int) { Value = groupId },
            new SqlParameter("@ActingUserID", SqlDbType.Int) { Value = actingUserId },
            new SqlParameter("@BeforePostID", SqlDbType.Int)
            {
                Value = beforePostId ?? (object)DBNull.Value
            },
            new SqlParameter("@Take", SqlDbType.Int) { Value = take });

        return rows.Select(r => new PostFeedItem(
            Convert.ToInt32(r["PostID"]),
            Convert.ToString(r["PostContent"]) ?? string.Empty,
            DateTime.SpecifyKind(Convert.ToDateTime(r["CreatedAt"]), DateTimeKind.Utc).ToLocalTime(),
            Convert.ToString(r["ShowName"]) ?? string.Empty,
            Convert.ToString(r["UserColor"]) ?? string.Empty)).ToList();
    }
}

public static class UserRepository
{
    private const string UserColumns = "UserID, ShowName, UserEmail, Color";

    private static async Task<bool> UserExistsAsync(string userName)
    {
        var result = await SqlHelper.ScalarAsync(
            "SELECT dbo.UserExists(@UserName)",
            new SqlParameter("@UserName", SqlDbType.NVarChar, 100) { Value = userName });
        return result is not null && result is not DBNull && Convert.ToBoolean(result);
    }

    private static async Task<string?> GetUserPasswordHashAsync(string userName)
    {
        var result = await SqlHelper.ScalarAsync(
            "SELECT dbo.GetUserPasswordHash(@UserName)",
            new SqlParameter("@UserName", SqlDbType.NVarChar, 100) { Value = userName });
        return result is null or DBNull ? null : Convert.ToString(result);
    }

    public static async Task<User?> GetUserByUserNameAsync(string userName)
    {
        var rows = await SqlHelper.QueryAsync(
            $"SELECT {UserColumns} FROM Users WHERE UserName = @UserName",
            false,
            new SqlParameter("@UserName", SqlDbType.NVarChar, 100) { Value = userName });
        return rows.Count == 0 ? null : new User(rows[0]);
    }

    private static async Task<int> AddUserAsync(
        string userName, string showName, string? email, string passwordHash, string color)
    {
        var userId = await SqlHelper.ExecuteProcReturnAsync("AddUser",
            new SqlParameter("@UserName", SqlDbType.NVarChar, 100) { Value = userName },
            new SqlParameter("@ShowName", SqlDbType.NVarChar, 100) { Value = showName },
            new SqlParameter("@UserEmail", SqlDbType.VarChar, 255)
            {
                Value = string.IsNullOrWhiteSpace(email) ? DBNull.Value : email.Trim()
            },
            new SqlParameter("@PasswordHash", SqlDbType.VarChar, DbLimits.MaxPasswordHashLength) { Value = passwordHash },
            new SqlParameter("@Color", SqlDbType.VarChar, 16) { Value = color });

        return userId ?? throw new InvalidOperationException("The user could not be added.");
    }

    private static Task UpdatePasswordHashAsync(int userId, string passwordHash) =>
        SqlHelper.ExecuteAsync("UpdatePasswordHash", true,
            new SqlParameter("@UserID", SqlDbType.Int) { Value = userId },
            new SqlParameter("@PasswordHash", SqlDbType.VarChar, DbLimits.MaxPasswordHashLength) { Value = passwordHash });

    public sealed record SignInResult(bool Success, User? User, bool NeedsRehash)
    {
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

    private sealed class SignInThrottle
    {
        private const int FreeAttempts = 4;
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

    private static readonly string DecoyHash =
        PasswordHasher.Hash(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));

    public static async Task<SignInResult> SignInAsync(string userName, string password)
    {
        var trimmedName = userName.Trim();
        if (trimmedName.Length == 0 || password.Length == 0)
            return SignInResult.Failed();

        var key = trimmedName.ToUpperInvariant();

        var retryAfter = SignInAttempts.GetRetryAfter(key);
        if (retryAfter > TimeSpan.Zero)
            return SignInResult.Throttled(retryAfter);

        var storedHash = await GetUserPasswordHashAsync(trimmedName);
        if (string.IsNullOrEmpty(storedHash))
        {
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
            await UpdatePasswordHashAsync(user.UserID, PasswordHasher.Hash(password));
            return SignInResult.Migrated(user);
        }

        return SignInResult.Succeeded(user);
    }

    public static async Task<RegisterResult> RegisterAsync(
        string userName, string showName, string? email, string password, string color)
    {
        var trimmedName = userName.Trim();
        if (trimmedName.Length == 0)
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

        if (password.Length == 0)
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
            string.IsNullOrWhiteSpace(color) ? "#0078D4" : color);

        return RegisterResult.Succeeded();
    }
}

public static class PasswordHasher
{
    private const int Iterations = 210_000;
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const string FormatPrefix = "pbkdf2";

    public static string Hash(string password)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(
            password, salt, Iterations, HashAlgorithmName.SHA256, HashSize);

        return $"{FormatPrefix}${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    private static bool IsLegacySha256(string stored)
    {
        if (string.IsNullOrEmpty(stored) || stored.Length != 64)
            return false;

        foreach (char c in stored)
            if (!Uri.IsHexDigit(c))
                return false;

        return true;
    }

    public static VerifyResult Verify(string password, string stored)
    {
        if (string.IsNullOrEmpty(stored))
            return new VerifyResult(false, false);

        if (IsLegacySha256(stored))
        {
            bool valid = VerifyLegacySha256(password, stored);
            return new VerifyResult(valid, valid);
        }

        var parts = stored.Split('$');
        if (parts.Length != 4 || parts[0] != FormatPrefix)
            return new VerifyResult(false, false);

        if (!int.TryParse(parts[1], out int iterations) || iterations < 1)
            return new VerifyResult(false, false);

        byte[] salt;
        byte[] expected;
        try
        {
            salt = Convert.FromBase64String(parts[2]);
            expected = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            return new VerifyResult(false, false);
        }

        if (salt.Length == 0 || expected.Length == 0)
            return new VerifyResult(false, false);

        byte[] actual = Rfc2898DeriveBytes.Pbkdf2(
            password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);

        bool matches = CryptographicOperations.FixedTimeEquals(actual, expected);
        return new VerifyResult(matches, false);
    }

    private static bool VerifyLegacySha256(string password, string stored)
    {
        byte[] actual = SHA256.HashData(Encoding.UTF8.GetBytes(password));
        string actualHex = Convert.ToHexString(actual);
        return string.Equals(actualHex, stored, StringComparison.OrdinalIgnoreCase);
    }

    public readonly record struct VerifyResult(bool Success, bool NeedsRehash);
}
