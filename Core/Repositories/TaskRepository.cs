using System.Data;
using Core.Data;
using Microsoft.Data.SqlClient;

namespace Core.Repositories;

public static class TaskRepository
{
    public static async Task AddTaskAsync(
        int? taskParentId, int taskStatusId, int userId, int? groupId,
        string taskName, int importanceLevelId, string? description, string color)
    {
        await SqlHelper.ExecuteAsync("AddTask", true,
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
                Value = string.IsNullOrEmpty(color) ? "#FFFFFF" : color
            });
    }

    public static async Task UpdateTaskAsync(
        int taskId, int? taskParentId, int taskStatusId, int userId, int? groupId,
        string taskName, int importanceLevelId, string? description, string color)
    {
        await SqlHelper.ExecuteAsync("UpdateTask", true,
            new SqlParameter("@TaskID", SqlDbType.Int) { Value = taskId },
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
                Value = string.IsNullOrEmpty(color) ? "#FFFFFF" : color
            });
    }

    public static async Task UpdateRankingAsync(int taskId, int ranking)
    {
        await SqlHelper.ExecuteAsync("UpdateRanking", true,
            new SqlParameter("@TaskID", SqlDbType.Int) { Value = taskId },
            new SqlParameter("@Ranking", SqlDbType.Int) { Value = ranking });
    }

    public static async Task UpdateTaskStatusAsync(int taskId, int taskStatusId)
    {
        await SqlHelper.ExecuteAsync("UpdateTaskStatus", true,
            new SqlParameter("@TaskID", SqlDbType.Int) { Value = taskId },
            new SqlParameter("@TaskStatusID", SqlDbType.Int) { Value = taskStatusId });
    }

    public static async Task DeleteTaskAsync(int taskId)
    {
        await SqlHelper.ExecuteAsync("DeleteTask", true,
            new SqlParameter("@TaskID", SqlDbType.Int) { Value = taskId });
    }

    public static async Task<TodoTask?> GetTaskAsync(int taskId)
    {
        var rows = await SqlHelper.QueryAsync("GetTask", true,
            new SqlParameter("@TaskID", SqlDbType.Int) { Value = taskId });
        return rows.Count == 0 ? null : new TodoTask(rows[0]);
    }

    public static async Task<List<TodoTask>> GetParentTasksAsync(int userId, bool byImportance)
    {
        var procedure = byImportance ? "GetTaskParentsByImportance" : "GetTaskParentsByRanking";
        var rows = await SqlHelper.QueryAsync(procedure, true,
            new SqlParameter("@UserID", SqlDbType.Int) { Value = userId });
        return rows.Select(r => new TodoTask(r)).ToList();
    }

    public static async Task<List<TodoTask>> GetChildTasksAsync(int taskParentId)
    {
        var rows = await SqlHelper.QueryAsync("GetTaskChildrensByRanking", true,
            new SqlParameter("@TaskParentID", SqlDbType.Int) { Value = taskParentId });
        return rows.Select(r => new TodoTask(r)).ToList();
    }

    /// <summary>
    /// Parent tasks of a group. No stored procedure exists for this, so a parameterized
    /// query is used (tasks are not restricted to a single user because the whole group shares them).
    /// </summary>
    public static async Task<List<TodoTask>> GetTasksByGroupAsync(int groupId)
    {
        var rows = await SqlHelper.QueryAsync(
            "SELECT * FROM Tasks WHERE GroupID = @GroupID AND TaskParentID IS NULL ORDER BY Ranking, LevelOfImportanceID DESC",
            false,
            new SqlParameter("@GroupID", SqlDbType.Int) { Value = groupId });
        return rows.Select(r => new TodoTask(r)).ToList();
    }

    public static async Task<List<TodoTaskStatus>> GetTaskStatusesAsync()
    {
        var rows = await SqlHelper.QueryAsync("SELECT * FROM TaskStatus ORDER BY TaskStatusID", false);
        return rows.Select(r => new TodoTaskStatus(r)).ToList();
    }

    public static async Task<List<LevelOfImportance>> GetImportanceLevelsAsync()
    {
        var rows = await SqlHelper.QueryAsync("SELECT * FROM LevelOfImportance ORDER BY LevelOfImportanceID", false);
        return rows.Select(r => new LevelOfImportance(r)).ToList();
    }

    /// <summary>
    /// Counts visible (non-completed) parent tasks for a smart-view badge.
    /// </summary>
    public static async Task<int> CountParentTasksAsync(int userId, int? importanceLevelId = null)
    {
        var sql = "SELECT COUNT(*) FROM Tasks WHERE UserID = @UserID AND TaskParentID IS NULL AND TaskStatusID <> 3";
        var parameters = new List<SqlParameter> { new("@UserID", SqlDbType.Int) { Value = userId } };
        if (importanceLevelId.HasValue)
        {
            sql += " AND LevelOfImportanceID = @ImportanceLevelID";
            parameters.Add(new SqlParameter("@ImportanceLevelID", SqlDbType.Int) { Value = importanceLevelId.Value });
        }

        var result = await SqlHelper.ScalarAsync(sql, parameters.ToArray());
        return Convert.ToInt32(result);
    }
}