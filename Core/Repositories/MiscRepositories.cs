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
    bool IsMine);

public static class AssignedTaskRepository
{
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

public static class AttachmentRepository
{
    public static async Task<List<Attachment>> GetAttachmentInfosAsync(IReadOnlyList<int> taskIds, int actingUserId)
    {
        var rows = await SqlHelper.QueryAsync("GetAttachmentInfos", true,
            SqlHelper.Tvp("@TaskIDs", "TVPTaskIDs", SqlHelper.BuildTaskIdTable(taskIds)),
            new SqlParameter("@ActingUserID", SqlDbType.Int) { Value = actingUserId });

        var attachments = new List<Attachment>(rows.Count);
        foreach (DataRow row in rows)
        {
            attachments.Add(new Attachment
            {
                AttachmentID = Convert.ToInt32(row["AttachmentID"]),
                TaskID = Convert.ToInt32(row["TaskID"]),
                FileName = Convert.ToString(row["FileName"]) ?? string.Empty,
                FileSizeKB = Convert.ToInt32(row["FileSizeKB"])
            });
        }

        return attachments;
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

    public static async Task DeleteAttachmentAsync(int attachmentId, int actingUserId)
    {
        await SqlHelper.ExecuteAsync("DeleteAttachment", true,
            new SqlParameter("@AttachmentID", SqlDbType.Int) { Value = attachmentId },
            new SqlParameter("@ActingUserID", SqlDbType.Int) { Value = actingUserId });
    }
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
            Convert.ToInt32(r["GroupID"]),
            Convert.ToInt32(r["UserID"]),
            Convert.ToString(r["PostContent"]) ?? string.Empty,
            Convert.ToDateTime(r["PostDate"]),
            Convert.ToDateTime(r["CreatedAt"]),
            Convert.ToString(r["UserName"]) ?? string.Empty,
            Convert.ToString(r["ShowName"]) ?? string.Empty,
            Convert.ToString(r["UserColor"]) ?? string.Empty,
            Convert.ToBoolean(r["IsMine"]))).ToList();
    }
}
