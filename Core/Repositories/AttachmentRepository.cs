using System.Data;
using Core.Data;
using Microsoft.Data.SqlClient;

namespace Core.Repositories;

public static class AttachmentRepository
{
    public static async Task<List<Attachment>> GetAttachmentsAsync(int taskId)
    {
        var rows = await SqlHelper.QueryAsync(
            "SELECT * FROM Attachments WHERE TaskID = @TaskID ORDER BY AttachmentID", false,
            new SqlParameter("@TaskID", SqlDbType.Int) { Value = taskId });
        return rows.Select(r => new Attachment(r)).ToList();
    }

    public static async Task AddAttachmentAsync(int taskId, string fileName, byte[] fileData, int fileSizeKB)
    {
        await SqlHelper.ExecuteAsync("AddAttachment", true,
            new SqlParameter("@TaskID", SqlDbType.Int) { Value = taskId },
            new SqlParameter("@FileName", SqlDbType.NVarChar, 260) { Value = fileName },
            new SqlParameter("@FileData", SqlDbType.VarBinary, -1)
            {
                Value = fileData ?? Array.Empty<byte>()
            },
            new SqlParameter("@FileSizeKB", SqlDbType.Int) { Value = fileSizeKB });
    }

    public static async Task DeleteAttachmentAsync(int attachmentId)
    {
        await SqlHelper.ExecuteAsync("DeleteAttachment", true,
            new SqlParameter("@AttachmentID", SqlDbType.Int) { Value = attachmentId });
    }
}