using System.Data;
using Core.Data;
using Microsoft.Data.SqlClient;

namespace Core.Repositories;

public static class AttachmentRepository
{
    public static async Task<List<Attachment>> GetAttachmentInfosAsync(IReadOnlyList<int> taskIds)
    {
        var rows = await SqlHelper.QueryAsync("GetAttachmentInfos", true,
            SqlHelper.Tvp("@TaskIDs", "TVPTaskIDs", SqlHelper.BuildTaskIdTable(taskIds)));

        var attachments = new List<Attachment>(rows.Count);
        foreach (DataRow row in rows)
        {
            attachments.Add(new Attachment
            {
                AttachmentID = Convert.ToInt32(row["AttachmentID"]),
                TaskID = Convert.ToInt32(row["TaskID"]),
                FileName = Convert.ToString(row["FileName"]) ?? string.Empty,
                FileData = Array.Empty<byte>(),
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

    public static async Task<Attachment?> GetAttachmentDataAsync(int attachmentId, int actingUserId)
    {
        var rows = await SqlHelper.QueryAsync("GetAttachmentData", true,
            new SqlParameter("@AttachmentID", SqlDbType.Int) { Value = attachmentId },
            new SqlParameter("@ActingUserID", SqlDbType.Int) { Value = actingUserId });

        if (rows.Count == 0)
            return null;

        DataRow row = rows[0];
        return new Attachment
        {
            AttachmentID = Convert.ToInt32(row["AttachmentID"]),
            TaskID = Convert.ToInt32(row["TaskID"]),
            FileName = Convert.ToString(row["FileName"]) ?? string.Empty,
            FileData = row["FileData"] is byte[] bytes ? bytes : Array.Empty<byte>(),
            FileSizeKB = Convert.ToInt32(row["FileSizeKB"])
        };
    }

    public static async Task DeleteAttachmentAsync(int attachmentId, int actingUserId)
    {
        await SqlHelper.ExecuteAsync("DeleteAttachment", true,
            new SqlParameter("@AttachmentID", SqlDbType.Int) { Value = attachmentId },
            new SqlParameter("@ActingUserID", SqlDbType.Int) { Value = actingUserId });
    }
}
