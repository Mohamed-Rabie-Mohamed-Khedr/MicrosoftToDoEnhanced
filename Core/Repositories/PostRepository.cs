using System.Data;
using Core.Data;
using Microsoft.Data.SqlClient;

namespace Core.Repositories;

public static class PostRepository
{
    public static async Task AddPostAsync(int groupId, int userId, string content)
    {
        await SqlHelper.ExecuteAsync("AddPostInGroup", true,
            new SqlParameter("@GroupID", SqlDbType.Int) { Value = groupId },
            new SqlParameter("@UserID", SqlDbType.Int) { Value = userId },
            new SqlParameter("@PostContent", SqlDbType.NVarChar, -1) { Value = content });
    }

    public static async Task DeletePostAsync(int postId)
    {
        await SqlHelper.ExecuteAsync("DeletePostInGroup", true,
            new SqlParameter("@PostID", SqlDbType.Int) { Value = postId });
    }

    public static async Task<List<PostInGroup>> GetPreviousCommentsAsync(int groupId, int startCount)
    {
        var rows = await SqlHelper.QueryAsync("GetPreviousCommentsInGroup", true,
            new SqlParameter("@GroupID", SqlDbType.Int) { Value = groupId },
            new SqlParameter("@StartCount", SqlDbType.Int) { Value = startCount });
        return rows.Select(r => new PostInGroup(r)).ToList();
    }

    public static async Task<List<PostInGroup>> GetNextCommentsAsync(int groupId, int startCount)
    {
        var rows = await SqlHelper.QueryAsync("GetNextCommentsInGroup", true,
            new SqlParameter("@GroupID", SqlDbType.Int) { Value = groupId },
            new SqlParameter("@StartCount", SqlDbType.Int) { Value = startCount });
        return rows.Select(r => new PostInGroup(r)).ToList();
    }

    /// <summary>
    /// Supports the "Load 10 more" / HasMorePosts state.
    /// </summary>
    public static async Task<bool> HasPostsAfterAsync(int groupId, int postId)
    {
        var result = await SqlHelper.ScalarAsync(
            "SELECT COUNT(*) FROM PostsInGroups WHERE GroupID = @GroupID AND PostID > @PostID",
            new SqlParameter("@GroupID", SqlDbType.Int) { Value = groupId },
            new SqlParameter("@PostID", SqlDbType.Int) { Value = postId });
        return Convert.ToInt32(result) > 0;
    }
}