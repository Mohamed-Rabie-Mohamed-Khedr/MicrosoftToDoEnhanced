using System.Data;
using Core.Data;
using Microsoft.Data.SqlClient;

namespace Core.Repositories;

public static class AssignedTaskRepository
{
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
