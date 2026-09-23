using System.Data;
using Core.Data;
using Microsoft.Data.SqlClient;

namespace Core.Repositories;

public static class GroupRepository
{
    public static async Task AddGroupAsync(int adminId, string groupName, string? description, string color)
    {
        await SqlHelper.ExecuteAsync("AddGroup", true,
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
    }

    public static async Task UpdateGroupAsync(
        int groupId, int adminId, string groupName, string? description, string color)
    {
        await SqlHelper.ExecuteAsync("UpdateGroup", true,
            new SqlParameter("@GroupID", SqlDbType.Int) { Value = groupId },
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
    }

    public static async Task DeleteGroupAsync(int groupId)
    {
        await SqlHelper.ExecuteAsync("DeleteGroup", true,
            new SqlParameter("@GroupID", SqlDbType.Int) { Value = groupId });
    }

    public static async Task<Group?> GetGroupAsync(int groupId)
    {
        var rows = await SqlHelper.QueryAsync(
            "SELECT * FROM Groups WHERE GroupID = @GroupID", false,
            new SqlParameter("@GroupID", SqlDbType.Int) { Value = groupId });
        return rows.Count == 0 ? null : new Group(rows[0]);
    }

    /// <summary>
    /// The AddGroup procedure does not return the identity, so we look it up afterwards.
    /// </summary>
    public static async Task<int?> GetGroupIdAsync(int adminId, string groupName)
    {
        var rows = await SqlHelper.QueryAsync(
            "SELECT TOP 1 GroupID FROM Groups WHERE AdminID = @AdminID AND GroupName = @GroupName ORDER BY GroupID DESC",
            false,
            new SqlParameter("@AdminID", SqlDbType.Int) { Value = adminId },
            new SqlParameter("@GroupName", SqlDbType.NVarChar, 100) { Value = groupName });
        return rows.Count == 0 ? null : Convert.ToInt32(rows[0]["GroupID"]);
    }

    /// <summary>
    /// Groups the current user administers or is a member of.
    /// Not exposed by a stored procedure, so a parameterized query is used.
    /// </summary>
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

    public static async Task AddGroupMemberAsync(int groupId, int userId)
    {
        await SqlHelper.ExecuteAsync("AddGroupMember", true,
            new SqlParameter("@GroupID", SqlDbType.Int) { Value = groupId },
            new SqlParameter("@UserID", SqlDbType.Int) { Value = userId });
    }

    public static async Task DeleteGroupMemberAsync(int groupId, int userId)
    {
        await SqlHelper.ExecuteAsync("DeleteGroupMember", true,
            new SqlParameter("@GroupID", SqlDbType.Int) { Value = groupId },
            new SqlParameter("@UserID", SqlDbType.Int) { Value = userId });
    }

    /// <summary>
    /// Members include the group admin plus everyone in GroupMembers.
    /// </summary>
    public static async Task<List<User>> GetGroupMembersAsync(int groupId)
    {
        var rows = await SqlHelper.QueryAsync(
            """
            SELECT U.* FROM Users U
            WHERE U.UserID = (SELECT AdminID FROM Groups WHERE GroupID = @GroupID)
               OR U.UserID IN (SELECT UserID FROM GroupMembers WHERE GroupID = @GroupID)
            ORDER BY U.ShowName
            """,
            false,
            new SqlParameter("@GroupID", SqlDbType.Int) { Value = groupId });
        return rows.Select(r => new User(r)).ToList();
    }
}