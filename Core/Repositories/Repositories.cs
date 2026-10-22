using System.Data;
using System.Security.Cryptography;
using System.Text;
using Core.Data;
using Microsoft.Data.Sqlite;

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

internal static class AccessChecks
{
    internal static Task<bool> IsGroupMemberAsync(int userId, int? groupId) =>
        IsGroupMemberAsync(null, null, userId, groupId);

    internal static async Task<bool> IsGroupMemberAsync(
        SqliteConnection? connection, SqliteTransaction? transaction, int userId, int? groupId)
    {
        if (groupId is null)
            return false;

        if (await CanManageGroupAsync(connection, transaction, userId, groupId.Value))
            return true;

        var result = await ScalarAsync(connection, transaction,
            "SELECT EXISTS (SELECT 1 FROM GroupMembers WHERE GroupID = @GroupID AND UserID = @UserID)",
            new SqliteParameter("@GroupID", groupId.Value),
            new SqliteParameter("@UserID", userId));
        return Convert.ToInt32(result) == 1;
    }

    internal static Task<bool> CanManageGroupAsync(int userId, int? groupId) =>
        CanManageGroupAsync(null, null, userId, groupId);

    internal static async Task<bool> CanManageGroupAsync(
        SqliteConnection? connection, SqliteTransaction? transaction, int userId, int? groupId)
    {
        if (groupId is null)
            return false;

        var result = await ScalarAsync(connection, transaction,
            """
            SELECT CASE
                WHEN EXISTS (SELECT 1 FROM Groups WHERE GroupID = @GroupID AND AdminID = @UserID) THEN 1
                WHEN EXISTS (SELECT 1 FROM Users WHERE UserID = @UserID AND PermissionID = 2) THEN 1
                ELSE 0 END
            """,
            new SqliteParameter("@GroupID", groupId.Value),
            new SqliteParameter("@UserID", userId));
        return Convert.ToInt32(result) == 1;
    }

    internal static Task<bool> CanModifyTaskAsync(int taskId, int actingUserId) =>
        CanModifyTaskAsync(null, null, taskId, actingUserId);

    internal static async Task<bool> CanModifyTaskAsync(
        SqliteConnection? connection, SqliteTransaction? transaction, int taskId, int actingUserId)
    {
        var rows = await QueryAsync(connection, transaction,
            "SELECT UserID, GroupID FROM Tasks WHERE TaskID = @TaskID",
            new SqliteParameter("@TaskID", taskId));
        if (rows.Count == 0)
            return false;

        var ownerId = Convert.ToInt32(rows[0]["UserID"]);
        var groupValue = rows[0]["GroupID"];
        if (groupValue == DBNull.Value)
            return ownerId == actingUserId;

        return await IsGroupMemberAsync(connection, transaction, actingUserId, Convert.ToInt32(groupValue));
    }

    private static async Task<object?> ScalarAsync(
        SqliteConnection? connection, SqliteTransaction? transaction, string sql, params SqliteParameter[] parameters) =>
        connection is null
            ? await SqlHelper.ScalarAsync(sql, parameters)
            : await SqlHelper.ScalarAsync(connection, transaction, sql, parameters);

    private static async Task<IReadOnlyList<DataRow>> QueryAsync(
        SqliteConnection? connection, SqliteTransaction? transaction, string sql, params SqliteParameter[] parameters) =>
        connection is null
            ? await SqlHelper.QueryAsync(sql, parameters)
            : await SqlHelper.QueryAsync(connection, transaction, sql, parameters);
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
        return await SqlHelper.InTransactionAsync(async (connection, transaction) =>
        {
            if (dueStartDate is not null && dueEndDate is not null && dueEndDate < dueStartDate)
                throw new InvalidOperationException("SaveTask: End date cannot be before the start date.");
            if (groupId is not null
                && !await AccessChecks.IsGroupMemberAsync(connection, transaction, userId, groupId))
                throw new UnauthorizedAccessException("SaveTask: You must be a member of the group to modify tasks.");
            if (groupId is null && assigneeUserId is not null)
                throw new InvalidOperationException("SaveTask: Personal tasks cannot have assignees.");
            if (groupId is not null && assigneeUserId is not null
                && !await AccessChecks.IsGroupMemberAsync(connection, transaction, assigneeUserId.Value, groupId.Value))
                throw new InvalidOperationException("SaveTask: Assignees must be members of the group.");
            if (taskId is not null
                && !await AccessChecks.CanModifyTaskAsync(connection, transaction, taskId.Value, userId))
                throw new UnauthorizedAccessException("SaveTask: You do not have permission to modify this task.");

            var effectiveStatus = taskStatusId;
            int currentTaskId;

            if (taskId is null)
            {
                if (taskParentId is not null)
                {
                    var parentRows = await SqlHelper.QueryAsync(connection, transaction,
                        "SELECT UserID, GroupID, TaskParentID FROM Tasks WHERE TaskID = @TaskParentID",
                        new SqliteParameter("@TaskParentID", taskParentId.Value));
                    if (parentRows.Count == 0)
                        throw new InvalidOperationException("SaveTask: Parent task was not found.");

                    var parentRow = parentRows[0];
                    if (parentRow["TaskParentID"] != DBNull.Value)
                        throw new InvalidOperationException("SaveTask: A task cannot have a parent that is itself a step.");
                    if (!await AccessChecks.CanModifyTaskAsync(connection, transaction, taskParentId.Value, userId))
                        throw new UnauthorizedAccessException("SaveTask: You do not have permission to modify this task.");

                    var parentGroupValue = parentRow["GroupID"];
                    var parentGroupId = parentGroupValue == DBNull.Value ? (int?)null : Convert.ToInt32(parentGroupValue);
                    if ((parentGroupId ?? -1) != (groupId ?? -1))
                        throw new InvalidOperationException("SaveTask: Parent task must belong to the same list.");
                }

                if (groupId is not null && taskParentId is null
                    && !await AccessChecks.CanManageGroupAsync(connection, transaction, userId, groupId.Value))
                    effectiveStatus = 1;

                var rankingValue = await SqlHelper.ScalarAsync(connection, transaction,
                    """
                    SELECT COALESCE(MAX(Ranking), 0) + 1
                    FROM Tasks
                    WHERE (TaskParentID = @TaskParentID OR (TaskParentID IS NULL AND @TaskParentID IS NULL))
                      AND ((@GroupID IS NULL AND UserID = @UserID AND GroupID IS NULL)
                           OR (@GroupID IS NOT NULL AND GroupID = @GroupID))
                    """,
                    new SqliteParameter("@TaskParentID", taskParentId ?? (object)DBNull.Value),
                    new SqliteParameter("@GroupID", groupId ?? (object)DBNull.Value),
                    new SqliteParameter("@UserID", userId));
                var nextRanking = Convert.ToInt32(rankingValue);

                var insertedId = await SqlHelper.ExecuteReturnAsync(connection, transaction,
                    """
                    INSERT INTO Tasks (TaskParentID, TaskStatusID, UserID, GroupID, Ranking, TaskName,
                                       LevelOfImportanceID, Description, Color)
                    VALUES (@TaskParentID, @TaskStatusID, @UserID, @GroupID, @Ranking, @TaskName,
                            @ImportanceLevelID, @Description, @Color)
                    """,
                    new SqliteParameter("@TaskParentID", taskParentId ?? (object)DBNull.Value),
                    new SqliteParameter("@TaskStatusID", effectiveStatus),
                    new SqliteParameter("@UserID", userId),
                    new SqliteParameter("@GroupID", groupId ?? (object)DBNull.Value),
                    new SqliteParameter("@Ranking", nextRanking),
                    new SqliteParameter("@TaskName", taskName),
                    new SqliteParameter("@ImportanceLevelID", importanceLevelId),
                    new SqliteParameter("@Description", description ?? (object)DBNull.Value),
                    new SqliteParameter("@Color", string.IsNullOrWhiteSpace(color) ? "#FFFFFF" : color));
                currentTaskId = insertedId ?? throw new InvalidOperationException("The task could not be added.");
            }
            else
            {
                var currentRows = await SqlHelper.QueryAsync(connection, transaction,
                    "SELECT UserID, GroupID, TaskParentID FROM Tasks WHERE TaskID = @TaskID",
                    new SqliteParameter("@TaskID", taskId.Value));
                if (currentRows.Count == 0)
                    throw new InvalidOperationException("SaveTask: Task was not found.");

                var currentRow = currentRows[0];
                var currentOwnerId = Convert.ToInt32(currentRow["UserID"]);
                var currentGroupValue = currentRow["GroupID"];
                var currentParentValue = currentRow["TaskParentID"];
                var currentGroupId = currentGroupValue == DBNull.Value ? (int?)null : Convert.ToInt32(currentGroupValue);
                var currentParentId = currentParentValue == DBNull.Value ? (int?)null : Convert.ToInt32(currentParentValue);

                if (userId != currentOwnerId
                    || (groupId ?? -1) != (currentGroupId ?? -1)
                    || (taskParentId ?? -1) != (currentParentId ?? -1))
                    throw new InvalidOperationException("SaveTask: Task owner, group or parent must match the stored task.");

                await SqlHelper.ExecuteAsync(connection, transaction,
                    """
                    UPDATE Tasks SET TaskName = @TaskName, LevelOfImportanceID = @ImportanceLevelID,
                                     Description = @Description, Color = @Color
                    WHERE TaskID = @TaskID
                    """,
                    new SqliteParameter("@TaskName", taskName),
                    new SqliteParameter("@ImportanceLevelID", importanceLevelId),
                    new SqliteParameter("@Description", description ?? (object)DBNull.Value),
                    new SqliteParameter("@Color", string.IsNullOrWhiteSpace(color) ? "#FFFFFF" : color),
                    new SqliteParameter("@TaskID", taskId.Value));

                currentTaskId = taskId.Value;
            }

            if (dueStartDate is not null)
            {
                var plannedExists = await SqlHelper.ScalarAsync(connection, transaction,
                    "SELECT EXISTS (SELECT 1 FROM Planned WHERE TaskID = @TaskID)",
                    new SqliteParameter("@TaskID", currentTaskId));

                if (Convert.ToInt32(plannedExists) == 1)
                {
                    await SqlHelper.ExecuteAsync(connection, transaction,
                        """
                        UPDATE Planned SET PlannedStartDate = @PlannedStartDate, PlannedEndDate = @PlannedEndDate,
                                           RepetitionTypeID = @RepetitionTypeID
                        WHERE TaskID = @TaskID
                        """,
                        new SqliteParameter("@PlannedStartDate", dueStartDate),
                        new SqliteParameter("@PlannedEndDate", dueEndDate ?? (object)DBNull.Value),
                        new SqliteParameter("@RepetitionTypeID", repetitionTypeId),
                        new SqliteParameter("@TaskID", currentTaskId));
                }
                else
                {
                    await SqlHelper.ExecuteAsync(connection, transaction,
                        """
                        INSERT INTO Planned (TaskID, PlannedStartDate, PlannedEndDate, RepetitionTypeID)
                        VALUES (@TaskID, @PlannedStartDate, @PlannedEndDate, @RepetitionTypeID)
                        """,
                        new SqliteParameter("@TaskID", currentTaskId),
                        new SqliteParameter("@PlannedStartDate", dueStartDate),
                        new SqliteParameter("@PlannedEndDate", dueEndDate ?? (object)DBNull.Value),
                        new SqliteParameter("@RepetitionTypeID", repetitionTypeId));
                }
            }
            else
            {
                await SqlHelper.ExecuteAsync(connection, transaction,
                    "DELETE FROM Planned WHERE TaskID = @TaskID",
                    new SqliteParameter("@TaskID", currentTaskId));
            }

            await SqlHelper.ExecuteAsync(connection, transaction,
                "DELETE FROM Assigned WHERE TaskID = @TaskID",
                new SqliteParameter("@TaskID", currentTaskId));

            if (assigneeUserId is not null)
            {
                await SqlHelper.ExecuteAsync(connection, transaction,
                    "INSERT INTO Assigned (TaskID, ToUserID) VALUES (@TaskID, @ToUserID)",
                    new SqliteParameter("@TaskID", currentTaskId),
                    new SqliteParameter("@ToUserID", assigneeUserId.Value));
            }

            return currentTaskId;
        });
    }

    public static async Task<TaskStatusResult> UpdateTaskStatusAsync(int taskId, int taskStatusId, int actingUserId)
    {
        return await SqlHelper.InTransactionAsync(async (connection, transaction) =>
        {
            if (!await AccessChecks.CanModifyTaskAsync(connection, transaction, taskId, actingUserId))
                throw new UnauthorizedAccessException("UpdateTaskStatus: You do not have permission to modify this task.");

            var rows = await SqlHelper.QueryAsync(connection, transaction,
                "SELECT TaskStatusID, GroupID FROM Tasks WHERE TaskID = @TaskID",
                new SqliteParameter("@TaskID", taskId));
            if (rows.Count == 0)
                throw new InvalidOperationException("UpdateTaskStatus: Task was not found.");

            var currentStatus = Convert.ToInt32(rows[0]["TaskStatusID"]);
            var groupValue = rows[0]["GroupID"];
            var groupId = groupValue == DBNull.Value ? (int?)null : Convert.ToInt32(groupValue);

            if (currentStatus == 1 && taskStatusId == 3)
                throw new InvalidOperationException(
                    "UpdateTaskStatus: A pending task cannot be completed before it is approved.");

            var requestedStatus = taskStatusId;
            if (currentStatus == 1 && !await AccessChecks.CanManageGroupAsync(connection, transaction, actingUserId, groupId))
                taskStatusId = 1;
            var statusForced = taskStatusId != requestedStatus;

            var plannedRows = await SqlHelper.QueryAsync(connection, transaction,
                "SELECT PlannedStartDate, PlannedEndDate FROM Planned WHERE TaskID = @TaskID",
                new SqliteParameter("@TaskID", taskId));
            DateTime? oldStart = null;
            DateTime? oldEnd = null;
            if (plannedRows.Count > 0)
            {
                oldStart = (DateTime)plannedRows[0]["PlannedStartDate"];
                if (plannedRows[0]["PlannedEndDate"] != DBNull.Value)
                    oldEnd = (DateTime)plannedRows[0]["PlannedEndDate"];
            }

            await SqlHelper.ExecuteAsync(connection, transaction,
                "UPDATE Tasks SET TaskStatusID = @TaskStatusID WHERE TaskID = @TaskID",
                new SqliteParameter("@TaskStatusID", taskStatusId),
                new SqliteParameter("@TaskID", taskId));

            return new TaskStatusResult(taskStatusId, oldStart, oldEnd, statusForced);
        });
    }

    public static async Task DeleteTaskAsync(int taskId, int actingUserId)
    {
        await SqlHelper.InTransactionAsync(async (connection, transaction) =>
        {
            var allowed = await SqlHelper.ScalarAsync(connection, transaction,
                """
                SELECT EXISTS (
                    SELECT 1 FROM Tasks T
                    WHERE T.TaskID = @TaskID
                      AND (CASE WHEN T.GroupID IS NULL
                                THEN (CASE WHEN T.UserID = @ActingUserID THEN 1 ELSE 0 END)
                                ELSE (CASE
                                      WHEN EXISTS (SELECT 1 FROM Groups G WHERE G.GroupID = T.GroupID AND G.AdminID = @ActingUserID) THEN 1
                                      WHEN EXISTS (SELECT 1 FROM Users U WHERE U.UserID = @ActingUserID AND U.PermissionID = 2) THEN 1
                                      WHEN EXISTS (SELECT 1 FROM GroupMembers M WHERE M.GroupID = T.GroupID AND M.UserID = @ActingUserID) THEN 1
                                      ELSE 0 END) END) = 1
                      AND (T.UserID = @ActingUserID
                           OR (T.GroupID IS NOT NULL
                               AND (EXISTS (SELECT 1 FROM Groups G2 WHERE G2.GroupID = T.GroupID AND G2.AdminID = @ActingUserID)
                                    OR EXISTS (SELECT 1 FROM Users U2 WHERE U2.UserID = @ActingUserID AND U2.PermissionID = 2))))
                )
                """,
                new SqliteParameter("@TaskID", taskId),
                new SqliteParameter("@ActingUserID", actingUserId));

            if (Convert.ToInt32(allowed) != 1)
                throw new UnauthorizedAccessException("DeleteTask: You do not have permission to delete this task.");

            await SqlHelper.ExecuteAsync(connection, transaction,
                """
                DELETE FROM Attachments WHERE TaskID = @TaskID
                   OR TaskID IN (SELECT TaskID FROM Tasks WHERE TaskParentID = @TaskID)
                """,
                new SqliteParameter("@TaskID", taskId));
            await SqlHelper.ExecuteAsync(connection, transaction,
                """
                DELETE FROM Assigned WHERE TaskID = @TaskID
                   OR TaskID IN (SELECT TaskID FROM Tasks WHERE TaskParentID = @TaskID)
                """,
                new SqliteParameter("@TaskID", taskId));
            await SqlHelper.ExecuteAsync(connection, transaction,
                """
                DELETE FROM Planned WHERE TaskID = @TaskID
                   OR TaskID IN (SELECT TaskID FROM Tasks WHERE TaskParentID = @TaskID)
                """,
                new SqliteParameter("@TaskID", taskId));
            await SqlHelper.ExecuteAsync(connection, transaction,
                "DELETE FROM Tasks WHERE (TaskID = @TaskID OR TaskParentID = @TaskID) AND TaskParentID IS NOT NULL",
                new SqliteParameter("@TaskID", taskId));
            await SqlHelper.ExecuteAsync(connection, transaction,
                "DELETE FROM Tasks WHERE TaskID = @TaskID OR TaskParentID = @TaskID",
                new SqliteParameter("@TaskID", taskId));
        });
    }

    public static async Task<List<TodoTask>> GetParentTasksAsync(int userId, bool byImportance)
    {
        var rows = await SqlHelper.QueryAsync(
            """
            SELECT * FROM Tasks
            WHERE TaskParentID IS NULL
              AND ((@GroupID IS NULL AND UserID = @UserID AND GroupID IS NULL)
                   OR GroupID = @GroupID)
            ORDER BY (CASE WHEN @ByImportance = 1 THEN LevelOfImportanceID END) DESC, Ranking,
                     LevelOfImportanceID DESC
            """,
            new SqliteParameter("@UserID", userId),
            new SqliteParameter("@GroupID", DBNull.Value),
            new SqliteParameter("@ByImportance", byImportance ? 1 : 0));
        return rows.Select(r => new TodoTask(r)).ToList();
    }

    public static async Task<List<TodoTask>> GetGroupParentTasksAsync(int groupId, int actingUserId)
    {
        if (!await AccessChecks.IsGroupMemberAsync(actingUserId, groupId))
            throw new UnauthorizedAccessException("GetParentTasks: You do not have access to this group.");

        var rows = await SqlHelper.QueryAsync(
            """
            SELECT * FROM Tasks
            WHERE TaskParentID IS NULL
              AND ((@GroupID IS NULL AND UserID = @UserID AND GroupID IS NULL)
                   OR GroupID = @GroupID)
            ORDER BY (CASE WHEN @ByImportance = 1 THEN LevelOfImportanceID END) DESC, Ranking,
                     LevelOfImportanceID DESC
            """,
            new SqliteParameter("@UserID", actingUserId),
            new SqliteParameter("@GroupID", groupId),
            new SqliteParameter("@ByImportance", (object)0));
        return rows.Select(r => new TodoTask(r)).ToList();
    }

    public static async Task<List<TodoTask>> GetChildTasksAsync(int taskParentId, int actingUserId)
    {
        var exists = await SqlHelper.ScalarAsync(
            "SELECT EXISTS (SELECT 1 FROM Tasks WHERE TaskID = @TaskParentID)",
            new SqliteParameter("@TaskParentID", taskParentId));
        if (Convert.ToInt32(exists) != 1)
            throw new InvalidOperationException("GetTaskChildrensByRanking: Task was not found.");

        if (!await AccessChecks.CanModifyTaskAsync(taskParentId, actingUserId))
            throw new UnauthorizedAccessException("GetTaskChildrensByRanking: You do not have access to this task.");

        var rows = await SqlHelper.QueryAsync(
            "SELECT * FROM Tasks WHERE TaskParentID = @TaskParentID ORDER BY Ranking",
            new SqliteParameter("@TaskParentID", taskParentId));
        return rows.Select(r => new TodoTask(r)).ToList();
    }

    public static async Task<List<TodoTask>> GetChildTasksByIdsAsync(
        IReadOnlyList<int> parentIds, int actingUserId)
    {
        if (parentIds.Count == 0)
            return new List<TodoTask>();

        var (values, inputParameters) = SqlHelper.InValues(parentIds);
        var parameters = new List<SqliteParameter>(inputParameters)
        {
            new("@ActingUserID", actingUserId)
        };

        var rows = await SqlHelper.QueryAsync(
            $"""
            WITH Input(TaskID, InputOrder) AS (VALUES {values})
            SELECT T.* FROM Input
            JOIN Tasks T ON T.TaskParentID = Input.TaskID
            WHERE (T.GroupID IS NOT NULL
                   AND (EXISTS (SELECT 1 FROM Users U WHERE U.UserID = @ActingUserID AND U.PermissionID = 2)
                        OR EXISTS (SELECT 1 FROM Groups G WHERE G.GroupID = T.GroupID AND G.AdminID = @ActingUserID)
                        OR EXISTS (SELECT 1 FROM GroupMembers M WHERE M.GroupID = T.GroupID AND M.UserID = @ActingUserID)))
               OR (T.GroupID IS NULL AND T.UserID = @ActingUserID)
            ORDER BY T.TaskParentID, T.Ranking
            """,
            parameters.ToArray());

        return rows.Select(r => new TodoTask(r)).ToList();
    }

    public static async Task ReorderTasksAsync(
        int userId, int? groupId, int? parentTaskId, IReadOnlyList<int> orderedTaskIds)
    {
        await SqlHelper.InTransactionAsync(async (connection, transaction) =>
        {
            if (groupId is not null && !await AccessChecks.IsGroupMemberAsync(connection, transaction, userId, groupId))
                throw new UnauthorizedAccessException("You do not have permission to reorder this list.");

            var (values, inputParameters) = SqlHelper.InValues(orderedTaskIds);
            var parameters = new List<SqliteParameter>(inputParameters)
            {
                new("@UserID", userId),
                new("@GroupID", groupId ?? (object)DBNull.Value),
                new("@TaskParentID", parentTaskId ?? (object)DBNull.Value)
            };

            await SqlHelper.ExecuteAsync(connection, transaction,
                $"""
                WITH Raw(TaskID, InputOrder) AS (VALUES {values}),
                Input AS (SELECT TaskID, MIN(InputOrder) AS InputOrder FROM Raw GROUP BY TaskID),
                Ordered AS (
                    SELECT T.TaskID,
                           ROW_NUMBER() OVER (ORDER BY COALESCE(Input.InputOrder, 2147483647), T.Ranking) AS NewRanking
                    FROM Tasks T
                    LEFT JOIN Input ON Input.TaskID = T.TaskID
                    WHERE ((@TaskParentID IS NULL AND T.TaskParentID IS NULL)
                           OR T.TaskParentID = @TaskParentID)
                      AND ((@GroupID IS NULL AND T.UserID = @UserID AND T.GroupID IS NULL)
                           OR T.GroupID = @GroupID)
                )
                UPDATE Tasks
                SET Ranking = (SELECT O.NewRanking FROM Ordered O WHERE O.TaskID = Tasks.TaskID)
                WHERE EXISTS (SELECT 1 FROM Ordered O WHERE O.TaskID = Tasks.TaskID)
                """,
                parameters.ToArray());
        });
    }

    public static async Task<List<TaskListExtras>> GetTaskListExtrasAsync(
        IReadOnlyList<int> taskIds, int actingUserId)
    {
        if (taskIds.Count == 0)
            return new List<TaskListExtras>();

        var (values, inputParameters) = SqlHelper.InValues(taskIds);
        var parameters = new List<SqliteParameter>(inputParameters)
        {
            new("@ActingUserID", actingUserId)
        };

        var rows = await SqlHelper.QueryAsync(
            $"""
            WITH Input(TaskID, InputOrder) AS (VALUES {values})
            SELECT T.TaskID,
                   (SELECT COUNT(*) FROM Tasks C WHERE C.TaskParentID = T.TaskID) AS SubtaskCount,
                   (SELECT COUNT(*) FROM Tasks C WHERE C.TaskParentID = T.TaskID AND C.TaskStatusID = 3)
                       AS SubtaskCompletedCount,
                   (CASE WHEN EXISTS (SELECT 1 FROM Attachments A WHERE A.TaskID = T.TaskID)
                         THEN 1 ELSE 0 END) AS HasAttachments,
                   (SELECT X.ToUserID FROM Assigned X
                    WHERE X.TaskID = T.TaskID ORDER BY X.AssignedID LIMIT 1) AS AssignedToUserID,
                   (SELECT U.ShowName FROM Assigned X
                    JOIN Users U ON X.ToUserID = U.UserID
                    WHERE X.TaskID = T.TaskID ORDER BY X.AssignedID LIMIT 1) AS AssignedToName,
                   (SELECT U.Color FROM Assigned X
                    JOIN Users U ON X.ToUserID = U.UserID
                    WHERE X.TaskID = T.TaskID ORDER BY X.AssignedID LIMIT 1) AS AssignedToColor,
                   PP.PlannedStartDate AS DueStartDate,
                   PP.PlannedEndDate AS DueEndDate,
                   PP.RepetitionTypeID,
                   R.RepetitionName
            FROM Input
            JOIN Tasks T ON T.TaskID = Input.TaskID
            LEFT JOIN Planned PP ON PP.TaskID = T.TaskID
            LEFT JOIN RepetitionTypes R ON R.RepetitionTypeID = PP.RepetitionTypeID
            WHERE (T.GroupID IS NOT NULL
                   AND (EXISTS (SELECT 1 FROM Users U WHERE U.UserID = @ActingUserID AND U.PermissionID = 2)
                        OR EXISTS (SELECT 1 FROM Groups G WHERE G.GroupID = T.GroupID AND G.AdminID = @ActingUserID)
                        OR EXISTS (SELECT 1 FROM GroupMembers M WHERE M.GroupID = T.GroupID AND M.UserID = @ActingUserID)))
               OR (T.GroupID IS NULL AND T.UserID = @ActingUserID)
            ORDER BY Input.InputOrder, T.Ranking, T.LevelOfImportanceID DESC
            """,
            parameters.ToArray());

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
        var rows = await SqlHelper.QueryAsync(
            """
            WITH TodayRoots AS (
                SELECT T.TaskID
                FROM Tasks T
                INNER JOIN Planned P ON P.TaskID = T.TaskID
                WHERE T.UserID = @UserID AND T.GroupID IS NULL
                  AND (@IncludeCompleted = 1 OR T.TaskStatusID <> 3)
                  AND date(P.PlannedStartDate) <= date(@Today)
                  AND (P.PlannedEndDate IS NULL OR date(P.PlannedEndDate) >= date(@Today))
                  AND (T.TaskParentID IS NULL
                    OR NOT EXISTS (SELECT 1
                                   FROM Tasks TP
                                   INNER JOIN Planned PP ON PP.TaskID = TP.TaskID
                                   WHERE TP.TaskID = T.TaskParentID
                                     AND date(PP.PlannedStartDate) <= date(@Today)
                                     AND (PP.PlannedEndDate IS NULL OR date(PP.PlannedEndDate) >= date(@Today))))
            )
            SELECT
                (SELECT COUNT(*) FROM Tasks T
                 WHERE T.UserID = @UserID AND T.GroupID IS NULL
                   AND (@IncludeCompleted = 1 OR T.TaskStatusID <> 3)
                   AND (T.TaskParentID IS NULL
                     OR EXISTS (SELECT 1 FROM Tasks P
                                WHERE P.TaskID = T.TaskParentID AND P.TaskParentID IS NULL))) AS AllCount,
                (SELECT COUNT(*) FROM TodayRoots)
                  + (SELECT COUNT(*) FROM Tasks C
                     WHERE C.UserID = @UserID AND C.GroupID IS NULL
                       AND (@IncludeCompleted = 1 OR C.TaskStatusID <> 3)
                       AND EXISTS (SELECT 1 FROM TodayRoots R WHERE R.TaskID = C.TaskParentID)) AS TodayCount,
                (SELECT COUNT(*) FROM Tasks T
                 WHERE T.UserID = @UserID AND T.GroupID IS NULL
                   AND (@IncludeCompleted = 1 OR T.TaskStatusID <> 3)
                   AND ((T.TaskParentID IS NULL AND T.LevelOfImportanceID = 3)
                     OR (T.TaskParentID IS NOT NULL
                         AND EXISTS (SELECT 1 FROM Tasks P
                                     WHERE P.TaskID = T.TaskParentID AND P.TaskParentID IS NULL
                                       AND P.LevelOfImportanceID = 3)))) AS ImportantCount,
                (SELECT COUNT(*) FROM Tasks T
                 WHERE T.UserID = @UserID AND T.GroupID IS NULL
                   AND (@IncludeCompleted = 1 OR T.TaskStatusID <> 3)
                   AND ((T.TaskParentID IS NULL
                         AND EXISTS (SELECT 1 FROM Planned P WHERE P.TaskID = T.TaskID))
                     OR (T.TaskParentID IS NOT NULL
                         AND EXISTS (SELECT 1 FROM Tasks P
                                     INNER JOIN Planned PP ON PP.TaskID = P.TaskID
                                     WHERE P.TaskID = T.TaskParentID AND P.TaskParentID IS NULL))
                     OR (T.TaskParentID IS NOT NULL
                         AND EXISTS (SELECT 1 FROM Planned P WHERE P.TaskID = T.TaskID)
                         AND NOT EXISTS (SELECT 1 FROM Tasks P
                                         INNER JOIN Planned PP ON PP.TaskID = P.TaskID
                                         WHERE P.TaskID = T.TaskParentID)))) AS PlannedCount
            """,
            new SqliteParameter("@UserID", userId),
            new SqliteParameter("@IncludeCompleted", 1),
            new SqliteParameter("@Today", DateTime.Today));

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
        await using var results = await SqlHelper.QueryMultipleAsync(
            """
            SELECT TaskStatusID AS ID, StatusName AS Name FROM TaskStatus ORDER BY TaskStatusID;
            SELECT RepetitionTypeID AS ID, RepetitionName AS Name FROM RepetitionTypes ORDER BY RepetitionTypeID;
            SELECT LevelOfImportanceID AS ID, LevelName AS Name, Color FROM LevelOfImportance ORDER BY LevelOfImportanceID;
            SELECT UserID, ShowName, Color FROM Users ORDER BY ShowName;
            """);

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
        var groupId = await SqlHelper.ExecuteReturnAsync(
            """
            INSERT INTO Groups (AdminID, GroupName, GroupDescription, Color)
            VALUES (@AdminID, @GroupName, @GroupDescription, @Color)
            """,
            new SqliteParameter("@AdminID", adminId),
            new SqliteParameter("@GroupName", groupName),
            new SqliteParameter("@GroupDescription", string.IsNullOrEmpty(description) ? DBNull.Value : description),
            new SqliteParameter("@Color", string.IsNullOrEmpty(color) ? DBNull.Value : color));

        return groupId ?? throw new InvalidOperationException("The group could not be created.");
    }

    public static async Task UpdateGroupAsync(
        int groupId, int actingUserId, string groupName, string? description, string color)
    {
        if (!await AccessChecks.CanManageGroupAsync(actingUserId, groupId))
            throw new InvalidOperationException("UpdateGroup: Only the group owner can edit this group.");

        var rows = await SqlHelper.ExecuteAsync(
            """
            UPDATE Groups SET GroupName = @GroupName, GroupDescription = @GroupDescription, Color = @Color
            WHERE GroupID = @GroupID
            """,
            new SqliteParameter("@GroupID", groupId),
            new SqliteParameter("@GroupName", groupName),
            new SqliteParameter("@GroupDescription", string.IsNullOrEmpty(description) ? DBNull.Value : description),
            new SqliteParameter("@Color", string.IsNullOrEmpty(color) ? DBNull.Value : color));

        if (rows == 0)
            throw new InvalidOperationException("UpdateGroup: Group was not found.");
    }

    public static async Task DeleteGroupAsync(int groupId, int actingUserId)
    {
        await SqlHelper.InTransactionAsync(async (connection, transaction) =>
        {
            if (!await AccessChecks.CanManageGroupAsync(connection, transaction, actingUserId, groupId))
                throw new InvalidOperationException("DeleteGroup: Only the group owner can delete this group.");

            await SqlHelper.ExecuteAsync(connection, transaction,
                "DELETE FROM Attachments WHERE TaskID IN (SELECT TaskID FROM Tasks WHERE GroupID = @GroupID)",
                new SqliteParameter("@GroupID", groupId));
            await SqlHelper.ExecuteAsync(connection, transaction,
                "DELETE FROM Assigned WHERE TaskID IN (SELECT TaskID FROM Tasks WHERE GroupID = @GroupID)",
                new SqliteParameter("@GroupID", groupId));
            await SqlHelper.ExecuteAsync(connection, transaction,
                "DELETE FROM Planned WHERE TaskID IN (SELECT TaskID FROM Tasks WHERE GroupID = @GroupID)",
                new SqliteParameter("@GroupID", groupId));
            await SqlHelper.ExecuteAsync(connection, transaction,
                "DELETE FROM Tasks WHERE GroupID = @GroupID AND TaskParentID IS NOT NULL",
                new SqliteParameter("@GroupID", groupId));
            await SqlHelper.ExecuteAsync(connection, transaction,
                "DELETE FROM Tasks WHERE GroupID = @GroupID",
                new SqliteParameter("@GroupID", groupId));
            await SqlHelper.ExecuteAsync(connection, transaction,
                "DELETE FROM PostsInGroups WHERE GroupID = @GroupID",
                new SqliteParameter("@GroupID", groupId));
            await SqlHelper.ExecuteAsync(connection, transaction,
                "DELETE FROM GroupMembers WHERE GroupID = @GroupID",
                new SqliteParameter("@GroupID", groupId));
            await SqlHelper.ExecuteAsync(connection, transaction,
                "DELETE FROM Groups WHERE GroupID = @GroupID",
                new SqliteParameter("@GroupID", groupId));
        });
    }

    public static async Task<List<Group>> GetGroupsForUserAsync(int userId)
    {
        var rows = await SqlHelper.QueryAsync(
            """
            SELECT DISTINCT G.* FROM Groups G
            LEFT JOIN GroupMembers GM ON G.GroupID = GM.GroupID
            WHERE G.AdminID = @UserID OR GM.UserID = @UserID
            ORDER BY G.GroupName
            """,
            new SqliteParameter("@UserID", userId));
        return rows.Select(r => new Group(r)).ToList();
    }

    public static async Task AddGroupMemberAsync(int groupId, int userId, int actingUserId)
    {
        if (!await AccessChecks.CanManageGroupAsync(actingUserId, groupId))
            throw new UnauthorizedAccessException("AddGroupMember: You do not have permission to manage this group.");

        var exists = await SqlHelper.ScalarAsync(
            "SELECT EXISTS (SELECT 1 FROM Users WHERE UserID = @UserID)",
            new SqliteParameter("@UserID", userId));
        if (Convert.ToInt32(exists) != 1)
            throw new InvalidOperationException("AddGroupMember: User was not found.");

        await SqlHelper.ExecuteAsync(
            "INSERT INTO GroupMembers (GroupID, UserID) VALUES (@GroupID, @UserID)",
            new SqliteParameter("@GroupID", groupId),
            new SqliteParameter("@UserID", userId));
    }

    public static async Task DeleteGroupMemberAsync(int groupId, int userId, int actingUserId)
    {
        await SqlHelper.InTransactionAsync(async (connection, transaction) =>
        {
            if (!await AccessChecks.CanManageGroupAsync(connection, transaction, actingUserId, groupId)
                && actingUserId != userId)
                throw new UnauthorizedAccessException("DeleteGroupMember: You do not have permission to manage this group.");

            var owner = await SqlHelper.ScalarAsync(connection, transaction,
                "SELECT EXISTS (SELECT 1 FROM Groups WHERE GroupID = @GroupID AND AdminID = @UserID)",
                new SqliteParameter("@GroupID", groupId),
                new SqliteParameter("@UserID", userId));
            if (Convert.ToInt32(owner) == 1)
                throw new InvalidOperationException("DeleteGroupMember: The group owner cannot be removed from the group.");

            await SqlHelper.ExecuteAsync(connection, transaction,
                "DELETE FROM GroupMembers WHERE GroupID = @GroupID AND UserID = @UserID",
                new SqliteParameter("@GroupID", groupId),
                new SqliteParameter("@UserID", userId));
            await SqlHelper.ExecuteAsync(connection, transaction,
                """
                DELETE FROM Assigned WHERE ToUserID = @UserID
                  AND TaskID IN (SELECT TaskID FROM Tasks WHERE GroupID = @GroupID)
                """,
                new SqliteParameter("@UserID", userId),
                new SqliteParameter("@GroupID", groupId));
        });
    }

    public static async Task<List<User>> GetGroupMembersAsync(int groupId, int actingUserId)
    {
        if (!await AccessChecks.IsGroupMemberAsync(actingUserId, groupId))
            throw new UnauthorizedAccessException("GetGroupMembers: You do not have access to this group.");

        var rows = await SqlHelper.QueryAsync(
            """
            SELECT U.UserID, U.UserName, U.ShowName, U.UserEmail, U.Color FROM Users U
            WHERE U.UserID = (SELECT AdminID FROM Groups WHERE GroupID = @GroupID)
               OR U.UserID IN (SELECT UserID FROM GroupMembers WHERE GroupID = @GroupID)
            ORDER BY U.ShowName
            """,
            new SqliteParameter("@GroupID", groupId));

        return rows.Select(r => new User(r)).ToList();
    }
}

public static class PlannedTaskRepository
{
    public static async Task<int> AddAsync(
        int taskId, DateTime startDate, DateTime? endDate, int repetitionTypeId, int actingUserId)
    {
        return await SqlHelper.InTransactionAsync(async (connection, transaction) =>
        {
            if (endDate is not null && endDate < startDate)
                throw new InvalidOperationException("AddPlanned: End date cannot be before the start date.");
            if (!await AccessChecks.CanModifyTaskAsync(connection, transaction, taskId, actingUserId))
                throw new UnauthorizedAccessException("AddPlanned: You do not have permission to modify this task.");

            var existing = await SqlHelper.ScalarAsync(connection, transaction,
                "SELECT PlannedID FROM Planned WHERE TaskID = @TaskID",
                new SqliteParameter("@TaskID", taskId));

            if (existing is null || existing is DBNull)
            {
                var plannedId = await SqlHelper.ExecuteReturnAsync(connection, transaction,
                    """
                    INSERT INTO Planned (TaskID, PlannedStartDate, PlannedEndDate, RepetitionTypeID)
                    VALUES (@TaskID, @PlannedStartDate, @PlannedEndDate, @RepetitionTypeID)
                    """,
                    new SqliteParameter("@TaskID", taskId),
                    new SqliteParameter("@PlannedStartDate", startDate),
                    new SqliteParameter("@PlannedEndDate", endDate ?? (object)DBNull.Value),
                    new SqliteParameter("@RepetitionTypeID", repetitionTypeId));

                return plannedId ?? throw new InvalidOperationException("The plan could not be saved.");
            }

            await SqlHelper.ExecuteAsync(connection, transaction,
                """
                UPDATE Planned SET PlannedStartDate = @PlannedStartDate, PlannedEndDate = @PlannedEndDate,
                                   RepetitionTypeID = @RepetitionTypeID
                WHERE PlannedID = @PlannedID
                """,
                new SqliteParameter("@PlannedStartDate", startDate),
                new SqliteParameter("@PlannedEndDate", endDate ?? (object)DBNull.Value),
                new SqliteParameter("@RepetitionTypeID", repetitionTypeId),
                new SqliteParameter("@PlannedID", Convert.ToInt32(existing)));

            return Convert.ToInt32(existing);
        });
    }

    public static async Task<int?> AutoUpdateAsync()
    {
        return await SqlHelper.InTransactionAsync(async (connection, transaction) =>
        {
            var today = DateTime.Today;
            var rows = await SqlHelper.QueryAsync(connection, transaction,
                """
                SELECT TaskID, PlannedStartDate, PlannedEndDate, RepetitionTypeID
                FROM Planned
                WHERE RepetitionTypeID IS NOT NULL AND RepetitionTypeID > 1
                  AND date(PlannedStartDate) < date(@Today)
                """,
                new SqliteParameter("@Today", today));

            var count = 0;
            foreach (var row in rows)
            {
                var taskId = Convert.ToInt32(row["TaskID"]);
                var start = Convert.ToDateTime(row["PlannedStartDate"]);
                var repetitionTypeId = Convert.ToInt32(row["RepetitionTypeID"]);
                DateTime? end = row.IsNull("PlannedEndDate")
                    ? null
                    : Convert.ToDateTime(row["PlannedEndDate"]);

                var newStart = NextOccurrence(start, repetitionTypeId, today);
                var days = (int)(newStart.Date - start.Date).TotalDays;
                var newEnd = end?.AddDays(days);

                await SqlHelper.ExecuteAsync(connection, transaction,
                    """
                    UPDATE Planned SET PlannedStartDate = @NewStart, PlannedEndDate = @NewEnd
                    WHERE TaskID = @TaskID
                    """,
                    new SqliteParameter("@NewStart", newStart),
                    new SqliteParameter("@NewEnd", newEnd ?? (object)DBNull.Value),
                    new SqliteParameter("@TaskID", taskId));

                await SqlHelper.ExecuteAsync(connection, transaction,
                    "UPDATE Tasks SET TaskStatusID = 2 WHERE TaskID = @TaskID AND TaskStatusID = 3",
                    new SqliteParameter("@TaskID", taskId));

                count++;
            }

            return count;
        });
    }

    private static DateTime NextOccurrence(DateTime start, int repetitionTypeId, DateTime from)
    {
        var n = repetitionTypeId switch
        {
            2 => (int)(from.Date - start.Date).TotalDays,
            3 => (int)((from.Date - start.Date).TotalDays / 7),
            4 => (from.Year - start.Year) * 12 + from.Month - start.Month,
            _ => from.Year - start.Year
        };
        if (n < 1)
            n = 1;

        var isMonthEnd = repetitionTypeId >= 4
            && start.Day == DateTime.DaysInMonth(start.Year, start.Month);

        while (true)
        {
            var next = repetitionTypeId switch
            {
                2 => start.AddDays(n),
                3 => start.AddDays(7d * n),
                4 => start.AddMonths(n),
                _ => start.AddYears(n)
            };

            if (isMonthEnd)
            {
                var lastDay = DateTime.DaysInMonth(next.Year, next.Month);
                if (next.Day != lastDay)
                    next = new DateTime(next.Year, next.Month, lastDay, next.Hour, next.Minute,
                        next.Second, next.Millisecond, next.Kind);
            }

            if (next >= from)
                return next;

            n++;
        }
    }

    public static async Task<PlannedTask?> GetPlannedAsync(int taskId)
    {
        var rows = await SqlHelper.QueryAsync(
            """
            SELECT TaskID, PlannedStartDate, PlannedEndDate, RepetitionTypeID
            FROM Planned
            WHERE TaskID = @TaskID
            ORDER BY PlannedStartDate DESC
            LIMIT 1
            """,
            new SqliteParameter("@TaskID", taskId));
        return rows.Count == 0 ? null : new PlannedTask(rows[0]);
    }

    public static async Task<IReadOnlyDictionary<int, PlannedTask>> GetPlannedForTasksAsync(
        IReadOnlyList<int> taskIds)
    {
        var dict = new Dictionary<int, PlannedTask>(taskIds.Count);
        if (taskIds.Count == 0)
            return dict;

        var (values, inputParameters) = SqlHelper.InValues(taskIds);
        var rows = await SqlHelper.QueryAsync(
            $"""
            WITH Input(TaskID, InputOrder) AS (VALUES {values})
            SELECT p.TaskID, p.PlannedStartDate, p.PlannedEndDate, p.RepetitionTypeID
            FROM Planned p
            JOIN Input ON p.TaskID = Input.TaskID
            WHERE NOT EXISTS (
                SELECT 1
                FROM Planned p2
                WHERE p2.TaskID = p.TaskID
                  AND p2.PlannedStartDate > p.PlannedStartDate
            )
            """,
            inputParameters);

        foreach (DataRow row in rows)
        {
            var planned = new PlannedTask(row);
            dict[planned.TaskID] = planned;
        }

        return dict;
    }

    public static async Task<List<TodoTask>> GetTasksAsync(int userId, PlannedScope scope)
    {
        if (scope == PlannedScope.All)
        {
            var rows = await SqlHelper.QueryAsync(
                """
                SELECT T.* FROM Tasks T
                INNER JOIN Planned P ON P.TaskID = T.TaskID
                WHERE T.UserID = @UserID AND T.GroupID IS NULL
                  AND (T.TaskParentID IS NULL
                    OR NOT EXISTS (SELECT 1
                                   FROM Tasks TP
                                   INNER JOIN Planned PP ON PP.TaskID = TP.TaskID
                                   WHERE TP.TaskID = T.TaskParentID))
                ORDER BY date(P.PlannedStartDate), T.Ranking
                """,
                new SqliteParameter("@UserID", userId));
            return rows.Select(r => new TodoTask(r)).ToList();
        }

        var dailyRows = await SqlHelper.QueryAsync(
            """
            SELECT T.* FROM Tasks T
            INNER JOIN Planned P ON P.TaskID = T.TaskID
            WHERE T.UserID = @UserID AND T.GroupID IS NULL
              AND date(P.PlannedStartDate) <= date(@Today)
              AND (P.PlannedEndDate IS NULL OR date(P.PlannedEndDate) >= date(@Today))
              AND ( T.TaskParentID IS NULL
                 OR NOT EXISTS (SELECT 1
                                FROM Tasks TP
                                INNER JOIN Planned PP ON PP.TaskID = TP.TaskID
                                WHERE TP.TaskID = T.TaskParentID
                                  AND date(PP.PlannedStartDate) <= date(@Today)
                                  AND (PP.PlannedEndDate IS NULL OR date(PP.PlannedEndDate) >= date(@Today))))
            ORDER BY date(P.PlannedStartDate), T.Ranking
            """,
            new SqliteParameter("@UserID", userId),
            new SqliteParameter("@Today", DateTime.Today));
        return dailyRows.Select(r => new TodoTask(r)).ToList();
    }
}

public static class AssignedTaskRepository
{
    public static async Task<User?> GetAssignedUserAsync(int taskId, int actingUserId)
    {
        var rows = await SqlHelper.QueryAsync(
            """
            SELECT U.UserID, U.UserName, U.ShowName, U.UserEmail, U.Color FROM Assigned A
            JOIN Users U ON A.ToUserID = U.UserID
            JOIN Tasks T ON A.TaskID = T.TaskID
            WHERE A.TaskID = @TaskID
              AND ((T.GroupID IS NOT NULL
                    AND (EXISTS (SELECT 1 FROM Groups G WHERE G.GroupID = T.GroupID AND G.AdminID = @ActingUserID)
                         OR EXISTS (SELECT 1 FROM Users U2 WHERE U2.UserID = @ActingUserID AND U2.PermissionID = 2)
                         OR EXISTS (SELECT 1 FROM GroupMembers M WHERE M.GroupID = T.GroupID AND M.UserID = @ActingUserID)))
                OR (T.GroupID IS NULL AND T.UserID = @ActingUserID))
            LIMIT 1
            """,
            new SqliteParameter("@TaskID", taskId),
            new SqliteParameter("@ActingUserID", actingUserId));

        return rows.Count == 0 ? null : new User(rows[0]);
    }
}

public static class AttachmentRepository
{
    public static async Task<List<Attachment>> GetAttachmentInfosAsync(IReadOnlyList<int> taskIds, int actingUserId)
    {
        if (taskIds.Count == 0)
            return new List<Attachment>();

        var (values, inputParameters) = SqlHelper.InValues(taskIds);
        var parameters = new List<SqliteParameter>(inputParameters)
        {
            new("@ActingUserID", actingUserId)
        };

        var rows = await SqlHelper.QueryAsync(
            $"""
            WITH Input(TaskID, InputOrder) AS (VALUES {values})
            SELECT A.AttachmentID, A.TaskID, A.FileName, A.FileSizeKB
            FROM Input
            JOIN Attachments A ON A.TaskID = Input.TaskID
            JOIN Tasks T ON T.TaskID = A.TaskID
            WHERE (CASE WHEN T.GroupID IS NULL
                        THEN (CASE WHEN T.UserID = @ActingUserID THEN 1 ELSE 0 END)
                        ELSE (CASE
                              WHEN EXISTS (SELECT 1 FROM Groups G WHERE G.GroupID = T.GroupID AND G.AdminID = @ActingUserID) THEN 1
                              WHEN EXISTS (SELECT 1 FROM Users U WHERE U.UserID = @ActingUserID AND U.PermissionID = 2) THEN 1
                              WHEN EXISTS (SELECT 1 FROM GroupMembers M WHERE M.GroupID = T.GroupID AND M.UserID = @ActingUserID) THEN 1
                              ELSE 0 END) END) = 1
            ORDER BY Input.InputOrder, A.AttachmentID
            """,
            parameters.ToArray());

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

        return await SqlHelper.InTransactionAsync(async (connection, transaction) =>
        {
            if (!await AccessChecks.CanModifyTaskAsync(connection, transaction, taskId, actingUserId))
                throw new UnauthorizedAccessException("AddAttachment: You do not have permission to modify this task.");
            if (fileData is null)
                throw new InvalidOperationException("AddAttachment: Attachment content is required.");
            if (fileData.Length > 100L * 1024 * 1024)
                throw new InvalidOperationException("AddAttachment: Attachment exceeds the maximum allowed size of 100 MB.");

            var sizeKb = (int)Math.Ceiling(fileData.Length / 1024.0);
            var attachmentId = await SqlHelper.ExecuteReturnAsync(connection, transaction,
                """
                INSERT INTO Attachments (TaskID, FileName, FileData, FileSizeKB)
                VALUES (@TaskID, @FileName, @FileData, @FileSizeKB)
                """,
                new SqliteParameter("@TaskID", taskId),
                new SqliteParameter("@FileName", fileName),
                new SqliteParameter("@FileData", fileData),
                new SqliteParameter("@FileSizeKB", sizeKb));

            return attachmentId ?? throw new InvalidOperationException("The attachment could not be saved.");
        });
    }

    public static async Task DeleteAttachmentAsync(int attachmentId, int actingUserId)
    {
        var taskValue = await SqlHelper.ScalarAsync(
            "SELECT TaskID FROM Attachments WHERE AttachmentID = @AttachmentID",
            new SqliteParameter("@AttachmentID", attachmentId));
        if (taskValue is null || taskValue is DBNull)
            throw new InvalidOperationException("DeleteAttachment: Attachment was not found.");

        if (!await AccessChecks.CanModifyTaskAsync(Convert.ToInt32(taskValue), actingUserId))
            throw new UnauthorizedAccessException("DeleteAttachment: You do not have permission to delete this attachment.");

        await SqlHelper.ExecuteAsync(
            "DELETE FROM Attachments WHERE AttachmentID = @AttachmentID",
            new SqliteParameter("@AttachmentID", attachmentId));
    }
}

public static class PostRepository
{
    public static async Task<int> AddPostAsync(int groupId, int userId, string content)
    {
        if (!await AccessChecks.IsGroupMemberAsync(userId, groupId))
            throw new UnauthorizedAccessException("AddPostInGroup: You must be a member of the group to post.");
        if (string.IsNullOrEmpty(content?.Trim(' ')))
            throw new InvalidOperationException("AddPostInGroup: Post content cannot be empty.");

        var postId = await SqlHelper.ExecuteReturnAsync(
            """
            INSERT INTO PostsInGroups (GroupID, UserID, PostContent)
            VALUES (@GroupID, @UserID, @PostContent)
            """,
            new SqliteParameter("@GroupID", groupId),
            new SqliteParameter("@UserID", userId),
            new SqliteParameter("@PostContent", content));

        return postId ?? throw new InvalidOperationException("The post could not be created.");
    }

    public static async Task<List<PostFeedItem>> GetPostsAsync(
        int groupId, int actingUserId, int? beforePostId = null, int take = 20)
    {
        if (!await AccessChecks.IsGroupMemberAsync(actingUserId, groupId))
            throw new UnauthorizedAccessException("GetPostsInGroup: You do not have access to this group.");

        var rows = await SqlHelper.QueryAsync(
            """
            SELECT P.PostID, P.GroupID, P.UserID, P.PostContent, P.PostDate, P.CreatedAt,
                   U.UserName, U.ShowName, U.Color AS UserColor,
                   (CASE WHEN P.UserID = @ActingUserID THEN 1 ELSE 0 END) AS IsMine
            FROM PostsInGroups P
            JOIN Users U ON P.UserID = U.UserID
            WHERE P.GroupID = @GroupID
              AND (@BeforePostID IS NULL OR P.PostID < @BeforePostID)
            ORDER BY P.PostID DESC
            LIMIT @Take
            """,
            new SqliteParameter("@GroupID", groupId),
            new SqliteParameter("@ActingUserID", actingUserId),
            new SqliteParameter("@BeforePostID", beforePostId ?? (object)DBNull.Value),
            new SqliteParameter("@Take", take));

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
    private const string UserColumns = "UserID, UserName, ShowName, UserEmail, Color";

    private static async Task<bool> UserExistsAsync(string userName)
    {
        var result = await SqlHelper.ScalarAsync(
            "SELECT EXISTS (SELECT 1 FROM Users WHERE UserName = @UserName)",
            new SqliteParameter("@UserName", userName));
        return result is not null && result is not DBNull && Convert.ToBoolean(result);
    }

    private static async Task<string?> GetUserPasswordHashAsync(string userName)
    {
        var result = await SqlHelper.ScalarAsync(
            "SELECT PasswordHash FROM Users WHERE UserName = @UserName",
            new SqliteParameter("@UserName", userName));
        return result is null or DBNull ? null : Convert.ToString(result);
    }

    public static async Task<User?> GetUserByUserNameAsync(string userName)
    {
        var rows = await SqlHelper.QueryAsync(
            $"SELECT {UserColumns} FROM Users WHERE UserName = @UserName",
            new SqliteParameter("@UserName", userName));
        return rows.Count == 0 ? null : new User(rows[0]);
    }

    private static async Task<int> AddUserAsync(
        string userName, string showName, string? email, string passwordHash, string color)
    {
        if (await UserExistsAsync(userName))
            throw new InvalidOperationException("AddUser: This username is already taken.");

        var userId = await SqlHelper.ExecuteReturnAsync(
            """
            INSERT INTO Users (UserName, ShowName, UserEmail, PasswordHash, PermissionID, Color)
            VALUES (@UserName, @ShowName, @UserEmail, @PasswordHash, 1, @Color)
            """,
            new SqliteParameter("@UserName", userName),
            new SqliteParameter("@ShowName", showName),
            new SqliteParameter("@UserEmail", string.IsNullOrWhiteSpace(email) ? DBNull.Value : email.Trim().ToLowerInvariant()),
            new SqliteParameter("@PasswordHash", passwordHash),
            new SqliteParameter("@Color", color));

        return userId ?? throw new InvalidOperationException("The user could not be added.");
    }

    private static async Task UpdatePasswordHashAsync(int userId, string passwordHash)
    {
        var rows = await SqlHelper.ExecuteAsync(
            "UPDATE Users SET PasswordHash = @PasswordHash WHERE UserID = @UserID",
            new SqliteParameter("@UserID", userId),
            new SqliteParameter("@PasswordHash", passwordHash));

        if (rows == 0)
            throw new InvalidOperationException("UpdatePasswordHash: User was not found.");
    }

    public sealed record UpdateProfileResult(bool Success, string? ErrorMessage)
    {
        public static UpdateProfileResult Succeeded() => new(true, null);
        public static UpdateProfileResult Failed(string message) => new(false, message);
    }

    public static async Task<UpdateProfileResult> UpdateUserInfoAsync(
        int userId, string userName, string showName, string? email, string color)
    {
        var trimmedName = userName.Trim();
        if (trimmedName.Length == 0)
            return UpdateProfileResult.Failed("User name is required");

        if (trimmedName.Contains(' ') || trimmedName.Contains('\t'))
            return UpdateProfileResult.Failed("User name cannot contain spaces");

        if (trimmedName.Length < DbLimits.MinUserNameLength
            || trimmedName.Length > DbLimits.MaxUserNameLength)
            return UpdateProfileResult.Failed(
                $"User name must be {DbLimits.MinUserNameLength}-{DbLimits.MaxUserNameLength} characters");

        var trimmedShowName = string.IsNullOrWhiteSpace(showName) ? trimmedName : showName.Trim();
        if (trimmedShowName.Length > DbLimits.MaxShowNameLength)
            return UpdateProfileResult.Failed($"Display name cannot exceed {DbLimits.MaxShowNameLength} characters");

        var trimmedEmail = string.IsNullOrWhiteSpace(email) ? null : email.Trim();
        if (trimmedEmail is not null && trimmedEmail.Length > DbLimits.MaxEmailLength)
            return UpdateProfileResult.Failed($"Email cannot exceed {DbLimits.MaxEmailLength} characters");

        var existing = await GetUserByUserNameAsync(trimmedName);
        if (existing is not null && existing.UserID != userId)
            return UpdateProfileResult.Failed("This username is already taken");

        var normalizedEmail = trimmedEmail?.ToLowerInvariant();

        var userExists = await SqlHelper.ScalarAsync(
            "SELECT EXISTS (SELECT 1 FROM Users WHERE UserID = @UserID)",
            new SqliteParameter("@UserID", userId));
        if (Convert.ToInt32(userExists) != 1)
            throw new InvalidOperationException("UpdateUserInfo: User was not found.");

        var takenByOther = await SqlHelper.ScalarAsync(
            "SELECT EXISTS (SELECT 1 FROM Users WHERE UserName = @UserName AND UserID <> @UserID)",
            new SqliteParameter("@UserName", trimmedName),
            new SqliteParameter("@UserID", userId));
        if (Convert.ToInt32(takenByOther) == 1)
            throw new InvalidOperationException("UpdateUserInfo: This username is already taken.");

        if (normalizedEmail is not null)
        {
            var emailUsed = await SqlHelper.ScalarAsync(
                "SELECT EXISTS (SELECT 1 FROM Users WHERE UserEmail = @UserEmail AND UserID <> @UserID)",
                new SqliteParameter("@UserEmail", normalizedEmail),
                new SqliteParameter("@UserID", userId));
            if (Convert.ToInt32(emailUsed) == 1)
                throw new InvalidOperationException("UpdateUserInfo: This email is already in use.");
        }

        await SqlHelper.ExecuteAsync(
            """
            UPDATE Users SET UserName = @UserName, ShowName = @ShowName, UserEmail = @UserEmail, Color = @Color
            WHERE UserID = @UserID
            """,
            new SqliteParameter("@UserID", userId),
            new SqliteParameter("@UserName", trimmedName),
            new SqliteParameter("@ShowName", trimmedShowName),
            new SqliteParameter("@UserEmail", normalizedEmail ?? (object)DBNull.Value),
            new SqliteParameter("@Color", color));

        return UpdateProfileResult.Succeeded();
    }

    public static async Task DeleteUserAccountAsync(int userId)
    {
        await SqlHelper.InTransactionAsync(async (connection, transaction) =>
        {
            var exists = await SqlHelper.ScalarAsync(connection, transaction,
                "SELECT EXISTS (SELECT 1 FROM Users WHERE UserID = @UserID)",
                new SqliteParameter("@UserID", userId));
            if (Convert.ToInt32(exists) != 1)
                throw new InvalidOperationException("DeleteUserAccount: User was not found.");

            await SqlHelper.ExecuteAsync(connection, transaction,
                """
                DELETE FROM Attachments
                WHERE TaskID IN (SELECT TaskID FROM Tasks WHERE GroupID IN (SELECT GroupID FROM Groups WHERE AdminID = @UserID))
                """,
                new SqliteParameter("@UserID", userId));
            await SqlHelper.ExecuteAsync(connection, transaction,
                """
                DELETE FROM Assigned
                WHERE TaskID IN (SELECT TaskID FROM Tasks WHERE GroupID IN (SELECT GroupID FROM Groups WHERE AdminID = @UserID))
                """,
                new SqliteParameter("@UserID", userId));
            await SqlHelper.ExecuteAsync(connection, transaction,
                """
                DELETE FROM Planned
                WHERE TaskID IN (SELECT TaskID FROM Tasks WHERE GroupID IN (SELECT GroupID FROM Groups WHERE AdminID = @UserID))
                """,
                new SqliteParameter("@UserID", userId));
            await SqlHelper.ExecuteAsync(connection, transaction,
                """
                DELETE FROM Tasks
                WHERE GroupID IN (SELECT GroupID FROM Groups WHERE AdminID = @UserID)
                  AND TaskParentID IS NOT NULL
                """,
                new SqliteParameter("@UserID", userId));
            await SqlHelper.ExecuteAsync(connection, transaction,
                "DELETE FROM Tasks WHERE GroupID IN (SELECT GroupID FROM Groups WHERE AdminID = @UserID)",
                new SqliteParameter("@UserID", userId));
            await SqlHelper.ExecuteAsync(connection, transaction,
                "DELETE FROM PostsInGroups WHERE GroupID IN (SELECT GroupID FROM Groups WHERE AdminID = @UserID)",
                new SqliteParameter("@UserID", userId));
            await SqlHelper.ExecuteAsync(connection, transaction,
                "DELETE FROM GroupMembers WHERE GroupID IN (SELECT GroupID FROM Groups WHERE AdminID = @UserID)",
                new SqliteParameter("@UserID", userId));
            await SqlHelper.ExecuteAsync(connection, transaction,
                "DELETE FROM Groups WHERE AdminID = @UserID",
                new SqliteParameter("@UserID", userId));

            await SqlHelper.ExecuteAsync(connection, transaction,
                "DELETE FROM Attachments WHERE TaskID IN (SELECT TaskID FROM Tasks WHERE UserID = @UserID AND GroupID IS NULL)",
                new SqliteParameter("@UserID", userId));
            await SqlHelper.ExecuteAsync(connection, transaction,
                "DELETE FROM Assigned WHERE TaskID IN (SELECT TaskID FROM Tasks WHERE UserID = @UserID AND GroupID IS NULL)",
                new SqliteParameter("@UserID", userId));
            await SqlHelper.ExecuteAsync(connection, transaction,
                "DELETE FROM Planned WHERE TaskID IN (SELECT TaskID FROM Tasks WHERE UserID = @UserID AND GroupID IS NULL)",
                new SqliteParameter("@UserID", userId));
            await SqlHelper.ExecuteAsync(connection, transaction,
                """
                DELETE FROM Tasks
                WHERE UserID = @UserID AND GroupID IS NULL AND TaskParentID IS NOT NULL
                """,
                new SqliteParameter("@UserID", userId));
            await SqlHelper.ExecuteAsync(connection, transaction,
                "DELETE FROM Tasks WHERE UserID = @UserID AND GroupID IS NULL",
                new SqliteParameter("@UserID", userId));

            await SqlHelper.ExecuteAsync(connection, transaction,
                """
                UPDATE Tasks SET UserID = (SELECT AdminID FROM Groups WHERE GroupID = Tasks.GroupID)
                WHERE UserID = @UserID AND GroupID IS NOT NULL
                """,
                new SqliteParameter("@UserID", userId));

            await SqlHelper.ExecuteAsync(connection, transaction,
                "DELETE FROM Assigned WHERE ToUserID = @UserID",
                new SqliteParameter("@UserID", userId));
            await SqlHelper.ExecuteAsync(connection, transaction,
                "DELETE FROM GroupMembers WHERE UserID = @UserID",
                new SqliteParameter("@UserID", userId));
            await SqlHelper.ExecuteAsync(connection, transaction,
                "DELETE FROM PostsInGroups WHERE UserID = @UserID",
                new SqliteParameter("@UserID", userId));
            await SqlHelper.ExecuteAsync(connection, transaction,
                "DELETE FROM Users WHERE UserID = @UserID",
                new SqliteParameter("@UserID", userId));
        });
    }

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
