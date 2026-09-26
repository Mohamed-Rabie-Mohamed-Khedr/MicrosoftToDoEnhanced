using System.Data;
using Core.Data;
using Microsoft.Data.SqlClient;

namespace Core.Repositories;

public sealed record PostFeedItem(
    int PostID,
    int GroupID,
    int UserID,
    string PostContent,
    DateTime PostDate,
    DateTime CreatedAt,
    string UserName,
    string ShowName,
    string UserColor,
    int LikeCount,
    bool LikedByMe,
    bool IsMine);

public static class PostRepository
{
    public static async Task<int> AddPostAsync(int groupId, int userId, int actingUserId, string content)
    {
        var postId = await SqlHelper.ExecuteProcReturnAsync("AddPostInGroup",
            new SqlParameter("@GroupID", SqlDbType.Int) { Value = groupId },
            new SqlParameter("@UserID", SqlDbType.Int) { Value = userId },
            new SqlParameter("@PostContent", SqlDbType.NVarChar, -1) { Value = content },
            new SqlParameter("@ActingUserID", SqlDbType.Int) { Value = actingUserId });

        return postId ?? throw new InvalidOperationException("The post could not be created.");
    }

    public static async Task DeletePostAsync(int postId, int actingUserId)
    {
        await SqlHelper.ExecuteAsync("DeletePostInGroup", true,
            new SqlParameter("@PostID", SqlDbType.Int) { Value = postId },
            new SqlParameter("@ActingUserID", SqlDbType.Int) { Value = actingUserId });
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
            Convert.ToInt32(r["GroupID"]),
            Convert.ToInt32(r["UserID"]),
            Convert.ToString(r["PostContent"]) ?? string.Empty,
            Convert.ToDateTime(r["PostDate"]),
            Convert.ToDateTime(r["CreatedAt"]),
            Convert.ToString(r["UserName"]) ?? string.Empty,
            Convert.ToString(r["ShowName"]) ?? string.Empty,
            Convert.ToString(r["UserColor"]) ?? string.Empty,
            Convert.ToInt32(r["LikeCount"]),
            Convert.ToBoolean(r["LikedByMe"]),
            Convert.ToBoolean(r["IsMine"]))).ToList();
    }

    public static async Task AddPostLikeAsync(int postId, int actingUserId)
    {
        await SqlHelper.ExecuteAsync("AddPostLike", true,
            new SqlParameter("@PostID", SqlDbType.Int) { Value = postId },
            new SqlParameter("@ActingUserID", SqlDbType.Int) { Value = actingUserId });
    }

    public static async Task RemovePostLikeAsync(int postId, int actingUserId)
    {
        await SqlHelper.ExecuteAsync("RemovePostLike", true,
            new SqlParameter("@PostID", SqlDbType.Int) { Value = postId },
            new SqlParameter("@ActingUserID", SqlDbType.Int) { Value = actingUserId });
    }
}
