using System.Data;
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

public sealed record TaskDetailBundle(
    DataRow Task,
    List<TodoTask> Steps,
    List<Attachment> Attachments,
    List<User> Assignees,
    bool CanManage,
    string OwnerName,
    string OwnerColor);

public sealed record TaskCounts(
    int AllCount,
    int TodayCount,
    int ImportantCount,
    int PlannedCount,
    int AssignedCount,
    int WeeklyCount,
    int MonthlyCount,
    int YearlyCount);

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

public static class TaskRepository
{
    public static async Task<int> AddTaskAsync(
        int? taskParentId, int taskStatusId, int userId, int? groupId,
        string taskName, int importanceLevelId, string? description, string color, int actingUserId)
    {
        var taskId = await SqlHelper.ExecuteProcReturnAsync("AddTask",
            new SqlParameter("@TaskParentID", SqlDbType.Int) { Value = taskParentId ?? (object)DBNull.Value },
            new SqlParameter("@TaskStatusID", SqlDbType.Int) { Value = taskStatusId },
            new SqlParameter("@UserID", SqlDbType.Int) { Value = userId },
            new SqlParameter("@GroupID", SqlDbType.Int) { Value = groupId ?? (object)DBNull.Value },
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
            new SqlParameter("@ActingUserID", SqlDbType.Int) { Value = actingUserId });

        return taskId ?? throw new InvalidOperationException("The task could not be added.");
    }

    public static async Task<int?> SaveTaskAsync(
        int? taskId, int? taskParentId, int taskStatusId, int userId, int? groupId,
        string taskName, int importanceLevelId, string? description, string color,
        DateTime? dueStartDate, DateTime? dueEndDate, int repetitionTypeId,
        IReadOnlyList<int> assigneeIds, int actingUserId)
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
            SqlHelper.Tvp("@Assignees", "TVPUserIDs", SqlHelper.BuildUserIdTable(assigneeIds)),
            new SqlParameter("@ActingUserID", SqlDbType.Int) { Value = actingUserId });
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

    public static async Task ApproveTaskAsync(int taskId, int actingUserId)
    {
        await SqlHelper.ExecuteAsync("ApproveTask", true,
            new SqlParameter("@TaskID", SqlDbType.Int) { Value = taskId },
            new SqlParameter("@ActingUserID", SqlDbType.Int) { Value = actingUserId });
    }

    public static async Task DeleteTaskAsync(int taskId, int actingUserId)
    {
        await SqlHelper.ExecuteAsync("DeleteTask", true,
            new SqlParameter("@TaskID", SqlDbType.Int) { Value = taskId },
            new SqlParameter("@ActingUserID", SqlDbType.Int) { Value = actingUserId });
    }

    public static async Task<TodoTask?> GetTaskAsync(int taskId, int actingUserId)
    {
        var rows = await SqlHelper.QueryAsync("GetTask", true,
            new SqlParameter("@TaskID", SqlDbType.Int) { Value = taskId },
            new SqlParameter("@ActingUserID", SqlDbType.Int) { Value = actingUserId });
        return rows.Count == 0 ? null : new TodoTask(rows[0]);
    }

    public static async Task<List<TodoTask>> GetParentTasksAsync(int userId, bool byImportance)
    {
        var procedure = byImportance ? "GetTaskParentsByImportance" : "GetTaskParentsByRanking";
        var rows = await SqlHelper.QueryAsync(procedure, true,
            new SqlParameter("@UserID", SqlDbType.Int) { Value = userId });
        return rows.Select(r => new TodoTask(r)).ToList();
    }

    public static async Task<List<TodoTask>> GetGroupParentTasksAsync(int groupId, int actingUserId)
    {
        var rows = await SqlHelper.QueryAsync("GetGroupParentTasks", true,
            new SqlParameter("@GroupID", SqlDbType.Int) { Value = groupId },
            new SqlParameter("@ActingUserID", SqlDbType.Int) { Value = actingUserId });
        return rows.Select(r => new TodoTask(r)).ToList();
    }

    public static async Task<List<TodoTask>> GetChildTasksAsync(int taskParentId, int actingUserId)
    {
        var rows = await SqlHelper.QueryAsync("GetTaskChildrensByRanking", true,
            new SqlParameter("@TaskParentID", SqlDbType.Int) { Value = taskParentId },
            new SqlParameter("@ActingUserID", SqlDbType.Int) { Value = actingUserId });
        return rows.Select(r => new TodoTask(r)).ToList();
    }

    public static async Task ReorderTasksAsync(
        int userId, int? groupId, IReadOnlyList<int> orderedTaskIds, int actingUserId)
    {
        await SqlHelper.ExecuteAsync("ReorderTasks", true,
            new SqlParameter("@UserID", SqlDbType.Int) { Value = userId },
            new SqlParameter("@GroupID", SqlDbType.Int)
            {
                Value = groupId ?? (object)DBNull.Value
            },
            SqlHelper.Tvp("@TaskIDs", "TVPTaskIDs", SqlHelper.BuildTaskIdTable(orderedTaskIds)),
            new SqlParameter("@ActingUserID", SqlDbType.Int) { Value = actingUserId });
    }

    public static async Task<List<TodoTask>> GetTasksByIdsAsync(IReadOnlyList<int> taskIds)
    {
        var rows = await SqlHelper.QueryAsync("GetTasksByIds", true,
            SqlHelper.Tvp("@TaskIDs", "TVPTaskIDs", SqlHelper.BuildTaskIdTable(taskIds)));
        return rows.Select(r => new TodoTask(r)).ToList();
    }

    public static async Task<List<TaskListExtras>> GetTaskListExtrasAsync(IReadOnlyList<int> taskIds)
    {
        var rows = await SqlHelper.QueryAsync("GetTaskListExtras", true,
            SqlHelper.Tvp("@TaskIDs", "TVPTaskIDs", SqlHelper.BuildTaskIdTable(taskIds)));

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

    public static async Task<TaskDetailBundle?> GetTaskExtrasAsync(int taskId, int actingUserId)
    {
        await using var results = await SqlHelper.QueryMultipleAsync("GetTaskExtras",
            new SqlParameter("@TaskID", SqlDbType.Int) { Value = taskId },
            new SqlParameter("@ActingUserID", SqlDbType.Int) { Value = actingUserId });

        var taskTable = await results.ReadAsync();
        if (taskTable is null || taskTable.Rows.Count == 0)
            return null;

        var taskRow = taskTable.Rows[0];

        var stepsTable = await results.ReadAsync() ?? new DataTable();
        var attachmentsTable = await results.ReadAsync() ?? new DataTable();
        var assigneesTable = await results.ReadAsync() ?? new DataTable();

        return new TaskDetailBundle(
            taskRow,
            stepsTable.Rows.Cast<DataRow>().Select(r => new TodoTask(r)).ToList(),
            attachmentsTable.Rows.Cast<DataRow>().Select(ToAttachment).ToList(),
            assigneesTable.Rows.Cast<DataRow>().Select(ToLookupUser).ToList(),
            Convert.ToBoolean(taskRow["CanManage"]),
            GetString(taskRow, "OwnerName") ?? string.Empty,
            GetString(taskRow, "OwnerColor") ?? string.Empty);
    }

    public static async Task<TaskCounts> GetTaskCountsAsync(int userId, int actingUserId)
    {
        var rows = await SqlHelper.QueryAsync("GetTaskCounts", true,
            new SqlParameter("@UserID", SqlDbType.Int) { Value = userId },
            new SqlParameter("@ActingUserID", SqlDbType.Int) { Value = actingUserId });

        if (rows.Count == 0)
            return new TaskCounts(0, 0, 0, 0, 0, 0, 0, 0);

        var row = rows[0];
        return new TaskCounts(
            Convert.ToInt32(row["AllCount"]),
            Convert.ToInt32(row["TodayCount"]),
            Convert.ToInt32(row["ImportantCount"]),
            Convert.ToInt32(row["PlannedCount"]),
            Convert.ToInt32(row["AssignedCount"]),
            Convert.ToInt32(row["WeeklyCount"]),
            Convert.ToInt32(row["MonthlyCount"]),
            Convert.ToInt32(row["YearlyCount"]));
    }

    public static async Task<LookupBundle> GetLookupsAsync()
    {
        await using var results = await SqlHelper.QueryMultipleAsync("GetLookups");

        var statuses = new List<TodoTaskStatus>();
        var repetitions = new List<RepetitionType>();
        var levels = new List<LevelOfImportance>();
        var users = new List<User>();

        var statusTable = await results.ReadAsync();
        if (statusTable is not null)
        {
            foreach (DataRow row in statusTable.Rows)
            {
                statuses.Add(new TodoTaskStatus
                {
                    TaskStatusID = Convert.ToInt32(row["ID"]),
                    StatusName = Convert.ToString(row["Name"]) ?? string.Empty
                });
            }
        }

        var repetitionTable = await results.ReadAsync();
        if (repetitionTable is not null)
        {
            foreach (DataRow row in repetitionTable.Rows)
            {
                repetitions.Add(new RepetitionType
                {
                    RepetitionTypeID = Convert.ToInt32(row["ID"]),
                    RepetitionName = Convert.ToString(row["Name"]) ?? string.Empty
                });
            }
        }

        var levelTable = await results.ReadAsync();
        if (levelTable is not null)
        {
            foreach (DataRow row in levelTable.Rows)
            {
                levels.Add(new LevelOfImportance
                {
                    LevelOfImportanceID = Convert.ToInt32(row["ID"]),
                    LevelName = Convert.ToString(row["Name"]) ?? string.Empty,
                    Color = GetString(row, "Color") ?? string.Empty
                });
            }
        }

        var userTable = await results.ReadAsync();
        if (userTable is not null)
        {
            foreach (DataRow row in userTable.Rows)
                users.Add(ToLookupUser(row));
        }

        return new LookupBundle(statuses, levels, repetitions, users);
    }

    public static async Task<List<TodoTaskStatus>> GetTaskStatusesAsync() =>
        (await GetLookupsAsync()).Statuses;

    public static async Task<List<LevelOfImportance>> GetImportanceLevelsAsync() =>
        (await GetLookupsAsync()).Levels;

    public static async Task<int> CountParentTasksAsync(int userId, int? importanceLevelId = null)
    {
        var sql = "SELECT COUNT(*) FROM Tasks WHERE UserID = @UserID AND TaskParentID IS NULL AND TaskStatusID <> 3 AND GroupID IS NULL";
        var parameters = new List<SqlParameter> { new("@UserID", SqlDbType.Int) { Value = userId } };
        if (importanceLevelId.HasValue)
        {
            sql += " AND LevelOfImportanceID = @ImportanceLevelID";
            parameters.Add(new SqlParameter("@ImportanceLevelID", SqlDbType.Int) { Value = importanceLevelId.Value });
        }

        var result = await SqlHelper.ScalarAsync(sql, parameters.ToArray());
        return Convert.ToInt32(result);
    }

    private static User ToLookupUser(DataRow row) =>
        new()
        {
            UserID = Convert.ToInt32(row["UserID"]),
            UserName = GetString(row, "UserName") ?? string.Empty,
            ShowName = GetString(row, "ShowName") ?? string.Empty,
            UserEmail = string.Empty,
            PasswordHash = string.Empty,
            PermissionID = 1,
            Color = GetString(row, "Color") ?? string.Empty
        };

    private static Attachment ToAttachment(DataRow row) =>
        new()
        {
            AttachmentID = Convert.ToInt32(row["AttachmentID"]),
            TaskID = Convert.ToInt32(row["TaskID"]),
            FileName = GetString(row, "FileName") ?? string.Empty,
            FileData = Array.Empty<byte>(),
            FileSizeKB = Convert.ToInt32(row["FileSizeKB"])
        };

    private static string? GetString(DataRow row, string column) =>
        row.IsNull(column) ? null : Convert.ToString(row[column]);

    private static int? GetNullableInt(DataRow row, string column) =>
        row.IsNull(column) ? null : Convert.ToInt32(row[column]);

    private static DateTime? GetNullableDateTime(DataRow row, string column) =>
        row.IsNull(column) ? null : Convert.ToDateTime(row[column]);
}
