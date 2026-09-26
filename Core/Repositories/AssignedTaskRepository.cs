using System.Data;
using Core.Data;
using Microsoft.Data.SqlClient;

namespace Core.Repositories;

public static class AssignedTaskRepository
{
    public static async Task AddAsync(int taskId, int toUserId, int actingUserId)
    {
        await SqlHelper.ExecuteAsync("AddAssignTask", true,
            new SqlParameter("@TaskID", SqlDbType.Int) { Value = taskId },
            new SqlParameter("@ToUserID", SqlDbType.Int) { Value = toUserId },
            new SqlParameter("@ActingUserID", SqlDbType.Int) { Value = actingUserId });
    }

    public static async Task DeleteAsync(int assignedId, int actingUserId)
    {
        await SqlHelper.ExecuteAsync("DeleteAssignedTask", true,
            new SqlParameter("@AssignedID", SqlDbType.Int) { Value = assignedId },
            new SqlParameter("@ActingUserID", SqlDbType.Int) { Value = actingUserId });
    }

    public static async Task<List<AssignedTask>> GetByTaskAsync(int taskId, int actingUserId)
    {
        var rows = await SqlHelper.QueryAsync(
            """
            SELECT A.* FROM Assigned A
            JOIN Tasks T ON A.TaskID = T.TaskID
            WHERE A.TaskID = @TaskID
              AND ((T.GroupID IS NOT NULL AND dbo.IsGroupMember(@ActingUserID, T.GroupID) = 1)
                OR (T.GroupID IS NULL AND T.UserID = @ActingUserID))
            """,
            false,
            new SqlParameter("@TaskID", SqlDbType.Int) { Value = taskId },
            new SqlParameter("@ActingUserID", SqlDbType.Int) { Value = actingUserId });
        return rows.Select(r => new AssignedTask(r)).ToList();
    }

    public static async Task<List<TodoTask>> GetAssignedTasksAsync(int userId, bool byImportance, int actingUserId)
    {
        var procedure = byImportance ? "GetAssignedTasksByImportance" : "GetAssignedTasksByRanking";
        var rows = await SqlHelper.QueryAsync(procedure, true,
            new SqlParameter("@UserID", SqlDbType.Int) { Value = userId },
            new SqlParameter("@ActingUserID", SqlDbType.Int) { Value = actingUserId });
        return rows.Select(r => new TodoTask(r)).ToList();
    }

    public static async Task<User?> GetAssignedUserAsync(int taskId, int actingUserId)
    {
        var rows = await SqlHelper.QueryAsync(
            """
            SELECT TOP 1 U.* FROM Assigned A
            JOIN Users U ON A.ToUserID = U.UserID
            JOIN Tasks T ON A.TaskID = T.TaskID
            WHERE A.TaskID = @TaskID
              AND ((T.GroupID IS NOT NULL AND dbo.IsGroupMember(@ActingUserID, T.GroupID) = 1)
                OR (T.GroupID IS NULL AND T.UserID = @ActingUserID))
            """,
            false,
            new SqlParameter("@TaskID", SqlDbType.Int) { Value = taskId },
            new SqlParameter("@ActingUserID", SqlDbType.Int) { Value = actingUserId });

        if (rows.Count == 0)
            return null;

        var user = new User(rows[0]);
        user.PasswordHash = string.Empty;
        return user;
    }
}
