using System.Data;

public class Permission
{
    public int PermissionID { get; set; }
    public string PermissionName { get; set; }

    public Permission() { }

    public Permission(DataRow dr)
    {
        PermissionID = Convert.ToInt32(dr["PermissionID"]);
        PermissionName = dr["PermissionName"].ToString();
    }
}

public class User
{
    public int UserID { get; set; }
    public string UserName { get; set; }
    public string ShowName { get; set; }
    public string UserEmail { get; set; }
    public string PasswordHash { get; set; }
    public int PermissionID { get; set; }
    public string Color { get; set; }

    public User() { }

    public User(DataRow dr)
    {
        UserID = Convert.ToInt32(dr["UserID"]);
        UserName = dr["UserName"].ToString();
        ShowName = dr["ShowName"].ToString();

        if (dr["UserEmail"] != DBNull.Value)
            UserEmail = dr["UserEmail"].ToString();

        PasswordHash = dr["PasswordHash"].ToString();
        PermissionID = Convert.ToInt32(dr["PermissionID"]);
        Color = dr["Color"].ToString();
    }
}

public class Group
{
    public int GroupID { get; set; }
    public int AdminID { get; set; }
    public string GroupName { get; set; }
    public string GroupDescription { get; set; }
    public string Color { get; set; }

    public Group() { }

    public Group(DataRow dr)
    {
        GroupID = Convert.ToInt32(dr["GroupID"]);
        AdminID = Convert.ToInt32(dr["AdminID"]);
        GroupName = dr["GroupName"].ToString();
        if (dr["GroupDescription"] != DBNull.Value)
            GroupDescription = dr["GroupDescription"].ToString();
        Color = dr["Color"].ToString();
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
    public string PostContent { get; set; }
    public DateTime PostDate { get; set; }

    public PostInGroup() { }

    public PostInGroup(DataRow dr)
    {
        PostID = Convert.ToInt32(dr["PostID"]);
        GroupID = Convert.ToInt32(dr["GroupID"]);
        UserID = Convert.ToInt32(dr["UserID"]);
        PostContent = dr["PostContent"].ToString();
        PostDate = Convert.ToDateTime(dr["PostDate"]);
    }
}

public class TodoTaskStatus
{
    public int TaskStatusID { get; set; }
    public string StatusName { get; set; }
    public string StatusDescription { get; set; }

    public TodoTaskStatus() { }

    public TodoTaskStatus(DataRow dr)
    {
        TaskStatusID = Convert.ToInt32(dr["TaskStatusID"]);
        StatusName = dr["StatusName"].ToString();
        if (dr["StatusDescription"] != DBNull.Value)
            StatusDescription = dr["StatusDescription"].ToString();
    }
}

public class LevelOfImportance
{
    public int LevelOfImportanceID { get; set; }
    public string LevelName { get; set; }
    public string Color { get; set; }

    public LevelOfImportance() { }

    public LevelOfImportance(DataRow dr)
    {
        LevelOfImportanceID = Convert.ToInt32(dr["LevelOfImportanceID"]);
        LevelName = dr["LevelName"].ToString();
        Color = dr["Color"].ToString();
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
    public string TaskName { get; set; }
    public int LevelOfImportanceID { get; set; }
    public string Description { get; set; }
    public DateTime CreationDate { get; set; }
    public string Color { get; set; }

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
        TaskName = dr["TaskName"].ToString();
        LevelOfImportanceID = Convert.ToInt32(dr["LevelOfImportanceID"]);

        if (dr["Description"] != DBNull.Value)
            Description = dr["Description"].ToString();

        CreationDate = Convert.ToDateTime(dr["CreationDate"]);

        if (dr["Color"] != DBNull.Value)
            Color = dr["Color"].ToString();
    }
}

public class Attachment
{
    public int AttachmentID { get; set; }
    public int TaskID { get; set; }
    public string FileName { get; set; }
    public byte[] FileData { get; set; }
    public int FileSizeKB { get; set; }

    public Attachment() { }

    public Attachment(DataRow dr)
    {
        AttachmentID = Convert.ToInt32(dr["AttachmentID"]);
        TaskID = Convert.ToInt32(dr["TaskID"]);
        FileName = dr["FileName"].ToString();
        FileData = (byte[])dr["FileData"];
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
    public string RepetitionName { get; set; }

    public RepetitionType() { }

    public RepetitionType(DataRow dr)
    {
        RepetitionTypeID = Convert.ToInt32(dr["RepetitionTypeID"]);
        RepetitionName = dr["RepetitionName"].ToString();
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