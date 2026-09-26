using System.Data;

namespace General.Models;

public class Permission
{
    public int PermissionID { get; set; }
    public string PermissionName { get; set; } = string.Empty;

    public Permission() { }

    public Permission(DataRow dr)
    {
        PermissionID = Convert.ToInt32(dr["PermissionID"]);
        PermissionName = dr["PermissionName"].ToString() ?? string.Empty;
    }
}

public class User
{
    public int UserID { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string ShowName { get; set; } = string.Empty;
    public string? UserEmail { get; set; }
    public string PasswordHash { get; set; } = string.Empty;
    public int PermissionID { get; set; }
    public string Color { get; set; } = string.Empty;

    public User() { }

    public User(DataRow dr)
    {
        UserID = Convert.ToInt32(dr["UserID"]);
        UserName = dr["UserName"].ToString() ?? string.Empty;
        ShowName = dr["ShowName"].ToString() ?? string.Empty;

        if (dr["UserEmail"] != DBNull.Value)
            UserEmail = dr["UserEmail"].ToString();

        PasswordHash = dr["PasswordHash"].ToString() ?? string.Empty;
        PermissionID = Convert.ToInt32(dr["PermissionID"]);
        Color = dr["Color"].ToString() ?? string.Empty;
    }
}

public class Group
{
    public int GroupID { get; set; }
    public int AdminID { get; set; }
    public string GroupName { get; set; } = string.Empty;
    public string? GroupDescription { get; set; }
    public string Color { get; set; } = string.Empty;

    public Group() { }

    public Group(DataRow dr)
    {
        GroupID = Convert.ToInt32(dr["GroupID"]);
        AdminID = Convert.ToInt32(dr["AdminID"]);
        GroupName = dr["GroupName"].ToString() ?? string.Empty;
        if (dr["GroupDescription"] != DBNull.Value)
            GroupDescription = dr["GroupDescription"].ToString();
        Color = dr["Color"].ToString() ?? string.Empty;
    }
}

public class GroupMember
{
    public int GroupMemberID { get; set; }
    public int GroupID { get; set; }
    public int UserID { get; set; }

    public GroupMember() { }

    public GroupMember(DataRow dr)
    {
        GroupMemberID = Convert.ToInt32(dr["GroupMemberID"]);
        GroupID = Convert.ToInt32(dr["GroupID"]);
        UserID = Convert.ToInt32(dr["UserID"]);
    }
}

public class PostInGroup
{
    public int PostID { get; set; }
    public int GroupID { get; set; }
    public int UserID { get; set; }
    public string PostContent { get; set; } = string.Empty;
    public DateTime PostDate { get; set; }

    public PostInGroup() { }

    public PostInGroup(DataRow dr)
    {
        PostID = Convert.ToInt32(dr["PostID"]);
        GroupID = Convert.ToInt32(dr["GroupID"]);
        UserID = Convert.ToInt32(dr["UserID"]);
        PostContent = dr["PostContent"].ToString() ?? string.Empty;
        PostDate = Convert.ToDateTime(dr["PostDate"]);
    }
}

public class TodoTaskStatus
{
    public int TaskStatusID { get; set; }
    public string StatusName { get; set; } = string.Empty;
    public string? StatusDescription { get; set; }

    public TodoTaskStatus() { }

    public TodoTaskStatus(DataRow dr)
    {
        TaskStatusID = Convert.ToInt32(dr["TaskStatusID"]);
        StatusName = dr["StatusName"].ToString() ?? string.Empty;
        if (dr["StatusDescription"] != DBNull.Value)
            StatusDescription = dr["StatusDescription"].ToString();
    }
}

public class LevelOfImportance
{
    public int LevelOfImportanceID { get; set; }
    public string LevelName { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;

    public LevelOfImportance() { }

    public LevelOfImportance(DataRow dr)
    {
        LevelOfImportanceID = Convert.ToInt32(dr["LevelOfImportanceID"]);
        LevelName = dr["LevelName"].ToString() ?? string.Empty;
        Color = dr["Color"].ToString() ?? string.Empty;
    }
}

public class TodoTask
{
    public int TaskID { get; set; }
    public int? TaskParentID { get; set; }
    public int TaskStatusID { get; set; }
    public int UserID { get; set; }
    public int? GroupID { get; set; }
    public int Ranking { get; set; }
    public string TaskName { get; set; } = string.Empty;
    public int LevelOfImportanceID { get; set; }
    public string? Description { get; set; }
    public DateTime CreationDate { get; set; }
    public string Color { get; set; } = "#FFFFFF";

    public TodoTask() { }

    public TodoTask(DataRow dr)
    {
        TaskID = Convert.ToInt32(dr["TaskID"]);

        if (dr["TaskParentID"] != DBNull.Value)
            TaskParentID = Convert.ToInt32(dr["TaskParentID"]);

        TaskStatusID = Convert.ToInt32(dr["TaskStatusID"]);
        UserID = Convert.ToInt32(dr["UserID"]);

        if (dr["GroupID"] != DBNull.Value)
            GroupID = Convert.ToInt32(dr["GroupID"]);

        Ranking = Convert.ToInt32(dr["Ranking"]);
        TaskName = dr["TaskName"].ToString() ?? string.Empty;
        LevelOfImportanceID = Convert.ToInt32(dr["LevelOfImportanceID"]);

        if (dr["Description"] != DBNull.Value)
            Description = dr["Description"].ToString();

        CreationDate = Convert.ToDateTime(dr["CreationDate"]);

        if (dr["Color"] != DBNull.Value)
            Color = dr["Color"].ToString() ?? "#FFFFFF";
    }
}

public class Attachment
{
    public int AttachmentID { get; set; }
    public int TaskID { get; set; }
    public string FileName { get; set; } = string.Empty;
    public byte[] FileData { get; set; } = Array.Empty<byte>();
    public int FileSizeKB { get; set; }

    public Attachment() { }

    public Attachment(DataRow dr)
    {
        AttachmentID = Convert.ToInt32(dr["AttachmentID"]);
        TaskID = Convert.ToInt32(dr["TaskID"]);
        FileName = dr["FileName"].ToString() ?? string.Empty;
        FileData = dr["FileData"] as byte[] ?? Array.Empty<byte>();
        FileSizeKB = Convert.ToInt32(dr["FileSizeKB"]);
    }
}

public class AssignedTask
{
    public int AssignedID { get; set; }
    public int TaskID { get; set; }
    public int ToUserID { get; set; }

    public AssignedTask() { }

    public AssignedTask(DataRow dr)
    {
        AssignedID = Convert.ToInt32(dr["AssignedID"]);
        TaskID = Convert.ToInt32(dr["TaskID"]);
        ToUserID = Convert.ToInt32(dr["ToUserID"]);
    }
}

public class RepetitionType
{
    public int RepetitionTypeID { get; set; }
    public string RepetitionName { get; set; } = string.Empty;

    public RepetitionType() { }

    public RepetitionType(DataRow dr)
    {
        RepetitionTypeID = Convert.ToInt32(dr["RepetitionTypeID"]);
        RepetitionName = dr["RepetitionName"].ToString() ?? string.Empty;
    }
}

public class PlannedTask
{
    public int PlannedID { get; set; }
    public int TaskID { get; set; }
    public DateTime PlannedStartDate { get; set; }
    public DateTime? PlannedEndDate { get; set; }
    public int? RepetitionTypeID { get; set; }

    public PlannedTask() { }

    public PlannedTask(DataRow dr)
    {
        PlannedID = Convert.ToInt32(dr["PlannedID"]);
        TaskID = Convert.ToInt32(dr["TaskID"]);
        PlannedStartDate = Convert.ToDateTime(dr["PlannedStartDate"]);

        if (dr["PlannedEndDate"] != DBNull.Value)
            PlannedEndDate = Convert.ToDateTime(dr["PlannedEndDate"]);
        if (dr["RepetitionTypeID"] != DBNull.Value)
            RepetitionTypeID = Convert.ToInt32(dr["RepetitionTypeID"]);
    }
}

public enum PermissionLevel
{
    Person = 1,
    Admin = 2
}

public enum TaskState
{
    Pending = 1,
    Incomplete = 2,
    Completed = 3
}

public enum TaskImportance
{
    Low = 1,
    Medium = 2,
    High = 3
}

public enum RepetitionPeriod
{
    None = 1,
    Daily = 2,
    Weekly = 3,
    Monthly = 4,
    Yearly = 5
}

public static class DbLimits
{
    public const int MinUserNameLength = 1;
    public const int MaxUserNameLength = 100;

    public const int MinShowNameLength = 1;
    public const int MaxShowNameLength = 100;

    public const int MaxEmailLength = 255;

    public const int MaxPasswordHashLength = 255;
    public const int MinPasswordLength = 8;
    public const int MaxPasswordLength = 1024;

    public const int MaxGroupNameLength = 100;
    public const int MaxGroupDescriptionLength = int.MaxValue;

    public const int MaxTaskNameLength = 100;

    public const int MaxFileNameLength = 260;

    public const long MaxAttachmentBytes = 10L * 1024 * 1024;
}
