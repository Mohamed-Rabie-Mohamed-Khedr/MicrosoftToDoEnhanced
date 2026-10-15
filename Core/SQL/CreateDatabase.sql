SET QUOTED_IDENTIFIER ON;
GO

IF DB_ID(N'MicrosoftToDoEnhanced') IS NULL CREATE DATABASE MicrosoftToDoEnhanced;
GO

USE MicrosoftToDoEnhanced;
GO

DECLARE @DropSql NVARCHAR(MAX) = N'';
SELECT @DropSql += N'DROP PROCEDURE IF EXISTS ' + QUOTENAME(SCHEMA_NAME(schema_id)) + N'.' + QUOTENAME(name) + N';' + CHAR(10)
FROM sys.objects WHERE type = 'P' AND is_ms_shipped = 0;
SELECT @DropSql += N'DROP FUNCTION IF EXISTS ' + QUOTENAME(SCHEMA_NAME(schema_id)) + N'.' + QUOTENAME(name) + N';' + CHAR(10)
FROM sys.objects WHERE type IN ('FN', 'IF', 'TF') AND is_ms_shipped = 0;
SELECT @DropSql += N'DROP TYPE IF EXISTS ' + QUOTENAME(SCHEMA_NAME(schema_id)) + N'.' + QUOTENAME(name) + N';' + CHAR(10)
FROM sys.types WHERE is_user_defined = 1;
EXEC sp_executesql @DropSql;
GO

IF OBJECT_ID(N'dbo.Permissions', N'U') IS NULL CREATE TABLE dbo.Permissions
(
    PermissionID   INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Permissions PRIMARY KEY,
    PermissionName VARCHAR(20) NOT NULL CONSTRAINT UQ_Permissions_Name UNIQUE
);
GO

IF OBJECT_ID(N'dbo.Users', N'U') IS NULL CREATE TABLE dbo.Users
(
    UserID       INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Users PRIMARY KEY,
    UserName     NVARCHAR(100) NOT NULL CONSTRAINT UQ_Users_UserName UNIQUE,
    ShowName     NVARCHAR(100) NOT NULL,
    UserEmail    VARCHAR(255) NULL,
    PasswordHash VARCHAR(255) NOT NULL,
    PermissionID INT NOT NULL,
    Color        VARCHAR(16) NOT NULL CONSTRAINT DF_Users_Color DEFAULT ('#FFFFFF'),
    CreatedAt    DATETIME2 NOT NULL CONSTRAINT DF_Users_CreatedAt DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT FK_Users_Permission FOREIGN KEY (PermissionID) REFERENCES dbo.Permissions(PermissionID)
);
GO

IF OBJECT_ID(N'dbo.Groups', N'U') IS NULL CREATE TABLE dbo.Groups
(
    GroupID          INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Groups PRIMARY KEY,
    AdminID          INT NOT NULL,
    GroupName        NVARCHAR(100) NOT NULL,
    GroupDescription NVARCHAR(MAX) NULL,
    Color            VARCHAR(16) NOT NULL CONSTRAINT DF_Groups_Color DEFAULT ('#FFFFFF'),
    CONSTRAINT FK_Groups_Admin FOREIGN KEY (AdminID) REFERENCES dbo.Users(UserID)
);
GO

IF OBJECT_ID(N'dbo.GroupMembers', N'U') IS NULL CREATE TABLE dbo.GroupMembers
(
    GroupMemberID INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_GroupMembers PRIMARY KEY,
    GroupID       INT NOT NULL,
    UserID        INT NOT NULL,
    CONSTRAINT FK_GroupMembers_Group FOREIGN KEY (GroupID) REFERENCES dbo.Groups(GroupID),
    CONSTRAINT FK_GroupMembers_User  FOREIGN KEY (UserID)  REFERENCES dbo.Users(UserID),
    CONSTRAINT UQ_GroupMembers_GroupUser UNIQUE (GroupID, UserID)
);
GO

IF OBJECT_ID(N'dbo.PostsInGroups', N'U') IS NULL CREATE TABLE dbo.PostsInGroups
(
    PostID      INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_PostsInGroups PRIMARY KEY,
    GroupID     INT NOT NULL,
    UserID      INT NOT NULL,
    PostContent NVARCHAR(MAX) NOT NULL,
    PostDate    DATE NOT NULL CONSTRAINT DF_PostsInGroups_PostDate DEFAULT (CAST(GETDATE() AS DATE)),
    CreatedAt   DATETIME2 NOT NULL CONSTRAINT DF_PostsInGroups_CreatedAt DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT FK_PostsInGroups_Group FOREIGN KEY (GroupID) REFERENCES dbo.Groups(GroupID),
    CONSTRAINT FK_PostsInGroups_User  FOREIGN KEY (UserID)  REFERENCES dbo.Users(UserID)
);
GO

IF OBJECT_ID(N'dbo.TaskStatus', N'U') IS NULL CREATE TABLE dbo.TaskStatus
(
    TaskStatusID      INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_TaskStatus PRIMARY KEY,
    StatusName        VARCHAR(20) NOT NULL CONSTRAINT UQ_TaskStatus_Name UNIQUE,
    StatusDescription VARCHAR(255) NULL
);
GO

IF OBJECT_ID(N'dbo.LevelOfImportance', N'U') IS NULL CREATE TABLE dbo.LevelOfImportance
(
    LevelOfImportanceID INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_LevelOfImportance PRIMARY KEY,
    LevelName           VARCHAR(20) NOT NULL CONSTRAINT UQ_LevelOfImportance_Name UNIQUE,
    Color               VARCHAR(16) NOT NULL
);
GO

IF OBJECT_ID(N'dbo.Tasks', N'U') IS NULL CREATE TABLE dbo.Tasks
(
    TaskID              INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Tasks PRIMARY KEY,
    TaskParentID        INT NULL,
    TaskStatusID        INT NOT NULL CONSTRAINT DF_Tasks_Status DEFAULT (2),
    UserID              INT NOT NULL,
    GroupID             INT NULL,
    Ranking             INT NOT NULL CONSTRAINT DF_Tasks_Ranking DEFAULT (0),
    TaskName            NVARCHAR(100) NOT NULL,
    LevelOfImportanceID INT NOT NULL CONSTRAINT DF_Tasks_Level DEFAULT (2),
    Description         NVARCHAR(MAX) NULL,
    CreationDate        DATE NOT NULL CONSTRAINT DF_Tasks_CreationDate DEFAULT (CAST(GETDATE() AS DATE)),
    Color               VARCHAR(16) NULL CONSTRAINT DF_Tasks_Color DEFAULT ('#FFFFFF'),
    CONSTRAINT FK_Tasks_Parent      FOREIGN KEY (TaskParentID) REFERENCES dbo.Tasks(TaskID),
    CONSTRAINT FK_Tasks_Status      FOREIGN KEY (TaskStatusID) REFERENCES dbo.TaskStatus(TaskStatusID),
    CONSTRAINT FK_Tasks_Level       FOREIGN KEY (LevelOfImportanceID) REFERENCES dbo.LevelOfImportance(LevelOfImportanceID),
    CONSTRAINT FK_Tasks_User        FOREIGN KEY (UserID) REFERENCES dbo.Users(UserID),
    CONSTRAINT FK_Tasks_Group       FOREIGN KEY (GroupID) REFERENCES dbo.Groups(GroupID)
);
GO

IF OBJECT_ID(N'dbo.Attachments', N'U') IS NULL CREATE TABLE dbo.Attachments
(
    AttachmentID INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Attachments PRIMARY KEY,
    TaskID       INT NOT NULL,
    FileName     NVARCHAR(260) NOT NULL,
    FileData     VARBINARY(MAX) NOT NULL,
    FileSizeKB   INT NOT NULL,
    CONSTRAINT FK_Attachments_Task FOREIGN KEY (TaskID) REFERENCES dbo.Tasks(TaskID)
);
GO

IF OBJECT_ID(N'dbo.Assigned', N'U') IS NULL CREATE TABLE dbo.Assigned
(
    AssignedID INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Assigned PRIMARY KEY,
    TaskID     INT NOT NULL,
    ToUserID   INT NOT NULL,
    CONSTRAINT FK_Assigned_Task FOREIGN KEY (TaskID) REFERENCES dbo.Tasks(TaskID),
    CONSTRAINT FK_Assigned_User FOREIGN KEY (ToUserID) REFERENCES dbo.Users(UserID),
    CONSTRAINT UQ_Assigned_TaskUser UNIQUE (TaskID, ToUserID)
);
GO

IF OBJECT_ID(N'dbo.RepetitionTypes', N'U') IS NULL CREATE TABLE dbo.RepetitionTypes
(
    RepetitionTypeID INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_RepetitionTypes PRIMARY KEY,
    RepetitionName   VARCHAR(20) NOT NULL CONSTRAINT UQ_RepetitionTypes_Name UNIQUE
);
GO

IF OBJECT_ID(N'dbo.Planned', N'U') IS NULL CREATE TABLE dbo.Planned
(
    PlannedID        INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Planned PRIMARY KEY,
    TaskID           INT NOT NULL,
    PlannedStartDate DATETIME2 NOT NULL,
    PlannedEndDate   DATETIME2 NULL,
    RepetitionTypeID INT NULL,
    CONSTRAINT FK_Planned_Task       FOREIGN KEY (TaskID) REFERENCES dbo.Tasks(TaskID),
    CONSTRAINT FK_Planned_Repetition FOREIGN KEY (RepetitionTypeID) REFERENCES dbo.RepetitionTypes(RepetitionTypeID)
);
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Permissions)
BEGIN
    INSERT INTO dbo.Permissions (PermissionName) VALUES (N'Person');
    INSERT INTO dbo.Permissions (PermissionName) VALUES (N'Admin');
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.TaskStatus)
BEGIN
    INSERT INTO dbo.TaskStatus (StatusName, StatusDescription) VALUES (N'Pending', N'Task is pending for admin approval');
    INSERT INTO dbo.TaskStatus (StatusName, StatusDescription) VALUES (N'Incomplete', N'Task is incomplete');
    INSERT INTO dbo.TaskStatus (StatusName, StatusDescription) VALUES (N'Completed', N'Task is completed');
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.LevelOfImportance)
BEGIN
    INSERT INTO dbo.LevelOfImportance (LevelName, Color) VALUES (N'Low', N'#7BA05B');
    INSERT INTO dbo.LevelOfImportance (LevelName, Color) VALUES (N'Medium', N'#C77D00');
    INSERT INTO dbo.LevelOfImportance (LevelName, Color) VALUES (N'High', N'#D13438');
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.RepetitionTypes)
BEGIN
    INSERT INTO dbo.RepetitionTypes (RepetitionName) VALUES (N'None');
    INSERT INTO dbo.RepetitionTypes (RepetitionName) VALUES (N'Daily');
    INSERT INTO dbo.RepetitionTypes (RepetitionName) VALUES (N'Weekly');
    INSERT INTO dbo.RepetitionTypes (RepetitionName) VALUES (N'Monthly');
    INSERT INTO dbo.RepetitionTypes (RepetitionName) VALUES (N'Yearly');
END
GO

CREATE FUNCTION dbo.UserExists (@UserName NVARCHAR(100))
RETURNS BIT
AS
BEGIN
    RETURN CASE WHEN EXISTS (SELECT 1 FROM dbo.Users WHERE UserName = @UserName)
                THEN 1 ELSE 0 END;
END
GO

CREATE FUNCTION dbo.GetUserPasswordHash (@UserName NVARCHAR(100))
RETURNS NVARCHAR(255)
AS
BEGIN
    DECLARE @PasswordHash NVARCHAR(255);
    SELECT @PasswordHash = PasswordHash FROM dbo.Users WHERE UserName = @UserName;
    RETURN @PasswordHash;
END
GO

CREATE FUNCTION dbo.CanManageGroup (@UserID INT, @GroupID INT)
RETURNS BIT
AS
BEGIN
    RETURN CASE
        WHEN @GroupID IS NULL THEN 0
        WHEN EXISTS (SELECT 1 FROM dbo.Groups WHERE GroupID = @GroupID AND AdminID = @UserID) THEN 1
        WHEN EXISTS (SELECT 1 FROM dbo.Users WHERE UserID = @UserID AND PermissionID = 2) THEN 1
        ELSE 0 END;
END
GO

CREATE FUNCTION dbo.IsGroupMember (@UserID INT, @GroupID INT)
RETURNS BIT
AS
BEGIN
    RETURN CASE
        WHEN @GroupID IS NULL THEN 0
        WHEN dbo.CanManageGroup(@UserID, @GroupID) = 1 THEN 1
        WHEN EXISTS (SELECT 1 FROM dbo.GroupMembers WHERE GroupID = @GroupID AND UserID = @UserID) THEN 1
        ELSE 0 END;
END
GO

CREATE FUNCTION dbo.CanModifyTask (@TaskID INT, @ActingUserID INT)
RETURNS BIT
AS
BEGIN
    DECLARE @OwnerID INT, @GroupID INT;
    SELECT @OwnerID = UserID, @GroupID = GroupID FROM dbo.Tasks WHERE TaskID = @TaskID;
    RETURN CASE
        WHEN @OwnerID IS NULL THEN 0
        WHEN @GroupID IS NULL THEN CASE WHEN @OwnerID = @ActingUserID THEN 1 ELSE 0 END
        ELSE dbo.IsGroupMember(@ActingUserID, @GroupID) END;
END
GO

CREATE FUNCTION dbo.NextOccurrence (@Start DATETIME2, @RepetitionTypeID INT, @From DATETIME2)
RETURNS DATETIME2
AS
BEGIN
    DECLARE @n INT = CASE @RepetitionTypeID
        WHEN 2 THEN DATEDIFF(DAY,   CAST(@Start AS DATE), CAST(@From AS DATE))
        WHEN 3 THEN DATEDIFF(DAY,   CAST(@Start AS DATE), CAST(@From AS DATE)) / 7
        WHEN 4 THEN DATEDIFF(MONTH, @Start, @From)
        ELSE        DATEDIFF(YEAR,  @Start, @From) END;
    IF @n < 1 SET @n = 1;
    DECLARE @IsMonthEnd BIT = CASE WHEN @RepetitionTypeID >= 4 AND DAY(@Start) = DAY(EOMONTH(@Start)) THEN 1 ELSE 0 END;
    DECLARE @Next DATETIME2;
    WHILE 1 = 1
    BEGIN
        SET @Next = CASE @RepetitionTypeID
            WHEN 2 THEN DATEADD(DAY,   @n, @Start)
            WHEN 3 THEN DATEADD(WEEK,  @n, @Start)
            WHEN 4 THEN DATEADD(MONTH, @n, @Start)
            ELSE        DATEADD(YEAR,  @n, @Start) END;
        IF @IsMonthEnd = 1
            SET @Next = DATEADD(DAY, DATEDIFF(DAY, CAST(@Next AS DATE), EOMONTH(@Next)), @Next);
        IF @Next >= @From BREAK;
        SET @n = @n + 1;
    END
    RETURN @Next;
END
GO

CREATE PROCEDURE dbo.AddUser
    @UserName     NVARCHAR(100),
    @ShowName     NVARCHAR(100),
    @UserEmail    VARCHAR(255),
    @PasswordHash VARCHAR(255),
    @Color        VARCHAR(16) = '#FFFFFF'
AS
BEGIN
    IF dbo.UserExists(@UserName) = 1
        THROW 50003, N'AddUser: This username is already taken.', 1;
    INSERT INTO dbo.Users (UserName, ShowName, UserEmail, PasswordHash, PermissionID, Color)
    VALUES (@UserName, @ShowName,
            CASE WHEN NULLIF(LTRIM(RTRIM(@UserEmail)), N'') IS NULL THEN NULL
                 ELSE LOWER(LTRIM(RTRIM(@UserEmail))) END,
            @PasswordHash, 1, @Color);
    SELECT CAST(SCOPE_IDENTITY() AS INT) AS NewID;
END
GO

CREATE PROCEDURE dbo.UpdatePasswordHash
    @UserID        INT,
    @PasswordHash  VARCHAR(255)
AS
BEGIN
    UPDATE dbo.Users SET PasswordHash = @PasswordHash WHERE UserID = @UserID;
    IF @@ROWCOUNT = 0
        THROW 50002, N'UpdatePasswordHash: User was not found.', 1;
END
GO

CREATE PROCEDURE dbo.AddGroup
    @AdminID           INT,
    @GroupName         NVARCHAR(100),
    @GroupDescription  NVARCHAR(MAX) = NULL,
    @Color             VARCHAR(16) = '#FFFFFF'
AS
BEGIN
    INSERT INTO dbo.Groups (AdminID, GroupName, GroupDescription, Color)
    VALUES (@AdminID, @GroupName, @GroupDescription, @Color);
    SELECT CAST(SCOPE_IDENTITY() AS INT) AS NewID;
END
GO

CREATE PROCEDURE dbo.UpdateGroup
    @GroupID           INT,
    @ActingUserID      INT,
    @GroupName         NVARCHAR(100),
    @GroupDescription  NVARCHAR(MAX) = NULL,
    @Color             VARCHAR(16) = '#FFFFFF'
AS
BEGIN
    IF dbo.CanManageGroup(@ActingUserID, @GroupID) = 0
        THROW 50004, N'UpdateGroup: Only the group owner can edit this group.', 1;
    UPDATE dbo.Groups SET GroupName = @GroupName, GroupDescription = @GroupDescription, Color = @Color
    WHERE GroupID = @GroupID;
    IF @@ROWCOUNT = 0
        THROW 50002, N'UpdateGroup: Group was not found.', 1;
END
GO

CREATE PROCEDURE dbo.DeleteGroup
    @GroupID      INT,
    @ActingUserID INT
AS
BEGIN
    BEGIN TRY
        BEGIN TRANSACTION;
        IF dbo.CanManageGroup(@ActingUserID, @GroupID) = 0
            THROW 50004, N'DeleteGroup: Only the group owner can delete this group.', 1;
        DECLARE @TasksToDelete TABLE (TaskID INT PRIMARY KEY);
        INSERT INTO @TasksToDelete (TaskID) SELECT TaskID FROM dbo.Tasks WHERE GroupID = @GroupID;
        DELETE FROM dbo.Attachments WHERE TaskID IN (SELECT TaskID FROM @TasksToDelete);
        DELETE FROM dbo.Assigned    WHERE TaskID IN (SELECT TaskID FROM @TasksToDelete);
        DELETE FROM dbo.Planned     WHERE TaskID IN (SELECT TaskID FROM @TasksToDelete);
        DELETE FROM dbo.Tasks       WHERE TaskID IN (SELECT TaskID FROM @TasksToDelete);
        DELETE FROM dbo.PostsInGroups WHERE GroupID = @GroupID;
        DELETE FROM dbo.GroupMembers  WHERE GroupID = @GroupID;
        DELETE FROM dbo.Groups        WHERE GroupID = @GroupID;
        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK; THROW;
    END CATCH;
END
GO

CREATE PROCEDURE dbo.AddGroupMember
    @GroupID      INT,
    @UserID       INT,
    @ActingUserID INT
AS
BEGIN
    IF dbo.CanManageGroup(@ActingUserID, @GroupID) = 0
        THROW 50001, N'AddGroupMember: You do not have permission to manage this group.', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.Users WHERE UserID = @UserID)
        THROW 50002, N'AddGroupMember: User was not found.', 1;
    INSERT INTO dbo.GroupMembers (GroupID, UserID) VALUES (@GroupID, @UserID);
END
GO

CREATE PROCEDURE dbo.DeleteGroupMember
    @GroupID      INT,
    @UserID       INT,
    @ActingUserID INT
AS
BEGIN
    BEGIN TRY
        BEGIN TRANSACTION;
        IF dbo.CanManageGroup(@ActingUserID, @GroupID) = 0 AND @ActingUserID <> @UserID
            THROW 50001, N'DeleteGroupMember: You do not have permission to manage this group.', 1;
        IF EXISTS (SELECT 1 FROM dbo.Groups WHERE GroupID = @GroupID AND AdminID = @UserID)
            THROW 50004, N'DeleteGroupMember: The group owner cannot be removed from the group.', 1;
        DELETE FROM dbo.GroupMembers WHERE GroupID = @GroupID AND UserID = @UserID;
        DELETE FROM dbo.Assigned WHERE ToUserID = @UserID
          AND TaskID IN (SELECT TaskID FROM dbo.Tasks WHERE GroupID = @GroupID);
        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK; THROW;
    END CATCH;
END
GO

CREATE PROCEDURE dbo.AddPostInGroup
    @GroupID       INT,
    @UserID        INT,
    @PostContent   NVARCHAR(MAX)
AS
BEGIN
    IF dbo.IsGroupMember(@UserID, @GroupID) = 0
        THROW 50001, N'AddPostInGroup: You must be a member of the group to post.', 1;
    IF NULLIF(LTRIM(RTRIM(@PostContent)), N'') IS NULL
        THROW 50003, N'AddPostInGroup: Post content cannot be empty.', 1;
    INSERT INTO dbo.PostsInGroups (GroupID, UserID, PostContent) VALUES (@GroupID, @UserID, @PostContent);
    SELECT CAST(SCOPE_IDENTITY() AS INT) AS NewID;
END
GO

CREATE PROCEDURE dbo.GetPostsInGroup
    @GroupID       INT,
    @ActingUserID  INT,
    @BeforePostID  INT = NULL,
    @Take          INT = 20
AS
BEGIN
    IF dbo.IsGroupMember(@ActingUserID, @GroupID) = 0
        THROW 50001, N'GetPostsInGroup: You do not have access to this group.', 1;
    SELECT TOP (@Take) P.PostID, P.GroupID, P.UserID, P.PostContent, P.PostDate, P.CreatedAt,
           U.UserName, U.ShowName, U.Color AS UserColor,
           CAST(CASE WHEN P.UserID = @ActingUserID THEN 1 ELSE 0 END AS BIT) AS IsMine
    FROM dbo.PostsInGroups P
    JOIN dbo.Users U ON P.UserID = U.UserID
    WHERE P.GroupID = @GroupID
      AND (@BeforePostID IS NULL OR P.PostID < @BeforePostID)
    ORDER BY P.PostID DESC;
END
GO

CREATE PROCEDURE dbo.SaveTask
    @TaskID              INT = NULL,
    @TaskParentID        INT = NULL,
    @TaskStatusID        INT = 2,
    @UserID              INT,
    @GroupID             INT = NULL,
    @TaskName            NVARCHAR(100),
    @ImportanceLevelID   INT = 2,
    @Description         NVARCHAR(MAX) = NULL,
    @Color               VARCHAR(16) = '#FFFFFF',
    @DueStartDate        DATETIME2 = NULL,
    @DueEndDate          DATETIME2 = NULL,
    @RepetitionTypeID    INT = 1,
    @AssigneeUserID      INT = NULL
AS
BEGIN
    BEGIN TRY
        BEGIN TRANSACTION;
        IF @DueEndDate < @DueStartDate
            THROW 50003, N'SaveTask: End date cannot be before the start date.', 1;
        IF @GroupID IS NOT NULL AND dbo.IsGroupMember(@UserID, @GroupID) = 0
            THROW 50001, N'SaveTask: You must be a member of the group to modify tasks.', 1;
        IF @GroupID IS NULL AND @AssigneeUserID IS NOT NULL
            THROW 50003, N'SaveTask: Personal tasks cannot have assignees.', 1;
        IF @GroupID IS NOT NULL AND @AssigneeUserID IS NOT NULL
            AND dbo.IsGroupMember(@AssigneeUserID, @GroupID) = 0
            THROW 50003, N'SaveTask: Assignees must be members of the group.', 1;
        IF @TaskID IS NOT NULL AND dbo.CanModifyTask(@TaskID, @UserID) = 0
            THROW 50001, N'SaveTask: You do not have permission to modify this task.', 1;
        DECLARE @IsInsert BIT = CASE WHEN @TaskID IS NULL THEN 1 ELSE 0 END;
        DECLARE @EffectiveStatus INT = @TaskStatusID;
        IF @IsInsert = 1
        BEGIN
            IF @TaskParentID IS NOT NULL
            BEGIN
                DECLARE @ParentOwnerID INT, @ParentGroupID INT;
                SELECT @ParentOwnerID = UserID, @ParentGroupID = GroupID
                FROM dbo.Tasks WHERE TaskID = @TaskParentID;
                IF @ParentOwnerID IS NULL
                    THROW 50002, N'SaveTask: Parent task was not found.', 1;
                IF EXISTS (SELECT 1 FROM dbo.Tasks WHERE TaskID = @TaskParentID AND TaskParentID IS NOT NULL)
                    THROW 50003, N'SaveTask: A task cannot have a parent that is itself a step.', 1;
                IF dbo.CanModifyTask(@TaskParentID, @UserID) = 0
                    THROW 50001, N'SaveTask: You do not have permission to modify this task.', 1;
                IF ISNULL(@ParentGroupID, -1) <> ISNULL(@GroupID, -1)
                    THROW 50003, N'SaveTask: Parent task must belong to the same list.', 1;
            END
            IF @GroupID IS NOT NULL AND @TaskParentID IS NULL AND dbo.CanManageGroup(@UserID, @GroupID) = 0
                SET @EffectiveStatus = 1;
            DECLARE @NextRanking INT;
            SELECT @NextRanking = ISNULL(MAX(T.Ranking), 0) + 1
            FROM dbo.Tasks T WITH (UPDLOCK, HOLDLOCK)
            WHERE (T.TaskParentID = @TaskParentID OR (T.TaskParentID IS NULL AND @TaskParentID IS NULL))
              AND ((@GroupID IS NULL AND T.UserID = @UserID AND T.GroupID IS NULL)
                   OR (@GroupID IS NOT NULL AND T.GroupID = @GroupID));
            INSERT INTO dbo.Tasks (TaskParentID, TaskStatusID, UserID, GroupID, Ranking, TaskName, LevelOfImportanceID, Description, Color)
            VALUES (@TaskParentID, @EffectiveStatus, @UserID, @GroupID, @NextRanking, @TaskName, @ImportanceLevelID, @Description, @Color);
            SET @TaskID = SCOPE_IDENTITY();
        END
        ELSE
        BEGIN
            DECLARE @CurrentUserID INT, @CurrentGroupID INT, @CurrentParentID INT;
            SELECT @CurrentUserID = UserID, @CurrentGroupID = GroupID, @CurrentParentID = TaskParentID
            FROM dbo.Tasks WHERE TaskID = @TaskID;
            IF @CurrentUserID IS NULL
                THROW 50002, N'SaveTask: Task was not found.', 1;
            IF ISNULL(@UserID, -1) <> ISNULL(@CurrentUserID, -1)
                OR ISNULL(@GroupID, -1) <> ISNULL(@CurrentGroupID, -1)
                OR ISNULL(@TaskParentID, -1) <> ISNULL(@CurrentParentID, -1)
                THROW 50003, N'SaveTask: Task owner, group or parent must match the stored task.', 1;
            UPDATE dbo.Tasks SET TaskName = @TaskName, LevelOfImportanceID = @ImportanceLevelID,
                                 Description = @Description, Color = @Color
            WHERE TaskID = @TaskID;
        END
        IF @DueStartDate IS NOT NULL
        BEGIN
            IF EXISTS (SELECT 1 FROM dbo.Planned WHERE TaskID = @TaskID)
                UPDATE dbo.Planned SET PlannedStartDate = @DueStartDate, PlannedEndDate = @DueEndDate,
                                       RepetitionTypeID = @RepetitionTypeID
                WHERE TaskID = @TaskID;
            ELSE
                INSERT INTO dbo.Planned (TaskID, PlannedStartDate, PlannedEndDate, RepetitionTypeID)
                VALUES (@TaskID, @DueStartDate, @DueEndDate, @RepetitionTypeID);
        END
        ELSE
            DELETE FROM dbo.Planned WHERE TaskID = @TaskID;
        DELETE FROM dbo.Assigned WHERE TaskID = @TaskID;
        IF @AssigneeUserID IS NOT NULL
            INSERT INTO dbo.Assigned (TaskID, ToUserID) VALUES (@TaskID, @AssigneeUserID);
        COMMIT TRANSACTION;
        SELECT @TaskID AS NewID;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK; THROW;
    END CATCH;
END
GO

CREATE PROCEDURE dbo.UpdateTaskStatus
    @TaskID        INT,
    @TaskStatusID  INT,
    @ActingUserID  INT
AS
BEGIN
    BEGIN TRY
        BEGIN TRANSACTION;
        IF dbo.CanModifyTask(@TaskID, @ActingUserID) = 0
            THROW 50001, N'UpdateTaskStatus: You do not have permission to modify this task.', 1;
        DECLARE @GroupID INT, @CurrentStatus INT;
        SELECT @CurrentStatus = TaskStatusID, @GroupID = GroupID
        FROM dbo.Tasks WHERE TaskID = @TaskID;
        IF @CurrentStatus IS NULL
            THROW 50002, N'UpdateTaskStatus: Task was not found.', 1;
        IF @CurrentStatus = 1 AND @TaskStatusID = 3
            THROW 50004, N'UpdateTaskStatus: A pending task cannot be completed before it is approved.', 1;
        DECLARE @RequestedStatus INT = @TaskStatusID;
        IF @CurrentStatus = 1 AND dbo.CanManageGroup(@ActingUserID, @GroupID) = 0
            SET @TaskStatusID = 1;
        DECLARE @StatusForced BIT = CASE WHEN @TaskStatusID <> @RequestedStatus THEN 1 ELSE 0 END;
        DECLARE @OldStart DATETIME2, @OldEnd DATETIME2;
        SELECT @OldStart = P.PlannedStartDate, @OldEnd = P.PlannedEndDate
        FROM dbo.Planned P WHERE P.TaskID = @TaskID;
        UPDATE dbo.Tasks SET TaskStatusID = @TaskStatusID WHERE TaskID = @TaskID;
        SELECT @TaskStatusID AS TaskStatusID, @OldStart AS PlannedStartDate,
               @OldEnd AS PlannedEndDate, @StatusForced AS StatusForced;
        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK; THROW;
    END CATCH;
END
GO

CREATE PROCEDURE dbo.DeleteTask
    @TaskID       INT,
    @ActingUserID INT
AS
BEGIN
    BEGIN TRY
        BEGIN TRANSACTION;
        IF NOT EXISTS (
            SELECT 1 FROM dbo.Tasks T
            WHERE T.TaskID = @TaskID
              AND dbo.CanModifyTask(T.TaskID, @ActingUserID) = 1
              AND (T.UserID = @ActingUserID OR dbo.CanManageGroup(@ActingUserID, T.GroupID) = 1))
            THROW 50001, N'DeleteTask: You do not have permission to delete this task.', 1;
        DECLARE @TasksToDelete TABLE (TaskID INT PRIMARY KEY);
        INSERT INTO @TasksToDelete (TaskID)
        SELECT TaskID FROM dbo.Tasks WHERE TaskID = @TaskID OR TaskParentID = @TaskID;
        DELETE FROM dbo.Attachments WHERE TaskID IN (SELECT TaskID FROM @TasksToDelete);
        DELETE FROM dbo.Assigned    WHERE TaskID IN (SELECT TaskID FROM @TasksToDelete);
        DELETE FROM dbo.Planned     WHERE TaskID IN (SELECT TaskID FROM @TasksToDelete);
        DELETE FROM dbo.Tasks       WHERE TaskID IN (SELECT TaskID FROM @TasksToDelete);
        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK; THROW;
    END CATCH;
END
GO

CREATE PROCEDURE dbo.GetParentTasks
    @UserID       INT,
    @GroupID      INT = NULL,
    @ByImportance BIT = 0
AS
BEGIN
    IF @GroupID IS NOT NULL AND dbo.IsGroupMember(@UserID, @GroupID) = 0
        THROW 50001, N'GetParentTasks: You do not have access to this group.', 1;
    SELECT * FROM dbo.Tasks
    WHERE TaskParentID IS NULL
      AND ((@GroupID IS NULL AND UserID = @UserID AND GroupID IS NULL)
           OR GroupID = @GroupID)
    ORDER BY CASE WHEN @ByImportance = 1 THEN LevelOfImportanceID END DESC, Ranking, LevelOfImportanceID DESC;
END
GO

CREATE PROCEDURE dbo.GetTaskChildrensByRanking
    @TaskParentID INT,
    @ActingUserID INT
AS
BEGIN
    IF NOT EXISTS (SELECT 1 FROM dbo.Tasks WHERE TaskID = @TaskParentID)
        THROW 50002, N'GetTaskChildrensByRanking: Task was not found.', 1;
    IF dbo.CanModifyTask(@TaskParentID, @ActingUserID) = 0
        THROW 50001, N'GetTaskChildrensByRanking: You do not have access to this task.', 1;
    SELECT * FROM dbo.Tasks WHERE TaskParentID = @TaskParentID ORDER BY Ranking;
END
GO

CREATE PROCEDURE dbo.GetChildTasksByIds
    @TaskParentIDs NVARCHAR(MAX),
    @ActingUserID  INT
AS
BEGIN
    DECLARE @IsGlobalAdmin BIT =
        CASE WHEN EXISTS (SELECT 1 FROM dbo.Users WHERE UserID = @ActingUserID AND PermissionID = 2)
             THEN 1 ELSE 0 END;
    SELECT T.* FROM OPENJSON(@TaskParentIDs) J
    JOIN dbo.Tasks T ON T.TaskParentID = CAST(J.value AS INT)
    WHERE (T.GroupID IS NOT NULL
           AND (@IsGlobalAdmin = 1
                OR EXISTS (SELECT 1 FROM dbo.Groups G WHERE G.GroupID = T.GroupID AND G.AdminID = @ActingUserID)
                OR EXISTS (SELECT 1 FROM dbo.GroupMembers M WHERE M.GroupID = T.GroupID AND M.UserID = @ActingUserID)))
       OR (T.GroupID IS NULL AND T.UserID = @ActingUserID)
    ORDER BY T.TaskParentID, T.Ranking;
END
GO

CREATE PROCEDURE dbo.ReorderTasks
    @UserID      INT,
    @GroupID     INT = NULL,
    @TaskParentID INT = NULL,
    @TaskIDs     NVARCHAR(MAX)
AS
BEGIN
    IF @GroupID IS NOT NULL AND dbo.IsGroupMember(@UserID, @GroupID) = 0
        THROW 50001, N'You do not have permission to reorder this list.', 1;
    WITH Ordered AS
    (
        SELECT T.TaskID,
               ROW_NUMBER() OVER (ORDER BY ISNULL(CAST(J.[key] AS INT), 2147483647), T.Ranking) AS NewRanking
        FROM dbo.Tasks T
        LEFT JOIN OPENJSON(@TaskIDs) J ON CAST(J.value AS INT) = T.TaskID
        WHERE ((@TaskParentID IS NULL AND T.TaskParentID IS NULL)
               OR T.TaskParentID = @TaskParentID)
          AND ((@GroupID IS NULL AND T.UserID = @UserID AND T.GroupID IS NULL)
               OR T.GroupID = @GroupID)
    )
    UPDATE T SET T.Ranking = O.NewRanking
    FROM dbo.Tasks T
    JOIN Ordered O ON T.TaskID = O.TaskID;
END
GO

CREATE PROCEDURE dbo.GetTaskListExtras
    @TaskIDs      NVARCHAR(MAX),
    @ActingUserID INT
AS
BEGIN
    DECLARE @IsGlobalAdmin BIT =
        CASE WHEN EXISTS (SELECT 1 FROM dbo.Users WHERE UserID = @ActingUserID AND PermissionID = 2)
             THEN 1 ELSE 0 END;
    SELECT T.TaskID, S.SubtaskCount, S.SubtaskCompletedCount,
           CAST(CASE WHEN EXISTS (SELECT 1 FROM dbo.Attachments A WHERE A.TaskID = T.TaskID)
                     THEN 1 ELSE 0 END AS BIT) AS HasAttachments,
           AU.UserID AS AssignedToUserID, AU.ShowName AS AssignedToName, AU.Color AS AssignedToColor,
           PP.PlannedStartDate AS DueStartDate, PP.PlannedEndDate AS DueEndDate,
           PP.RepetitionTypeID, R.RepetitionName
    FROM OPENJSON(@TaskIDs) J
    JOIN dbo.Tasks T ON T.TaskID = CAST(J.value AS INT)
    OUTER APPLY (SELECT COUNT(*) AS SubtaskCount, COUNT(CASE WHEN C.TaskStatusID = 3 THEN 1 END) AS SubtaskCompletedCount
                 FROM dbo.Tasks C WHERE C.TaskParentID = T.TaskID) S
    OUTER APPLY (SELECT TOP 1 U.UserID, U.ShowName, U.Color
                 FROM dbo.Assigned X JOIN dbo.Users U ON X.ToUserID = U.UserID
                 WHERE X.TaskID = T.TaskID ORDER BY X.AssignedID) AU
    LEFT JOIN dbo.Planned PP ON PP.TaskID = T.TaskID
    LEFT JOIN dbo.RepetitionTypes R ON R.RepetitionTypeID = PP.RepetitionTypeID
    WHERE (T.GroupID IS NOT NULL
           AND (@IsGlobalAdmin = 1
                OR EXISTS (SELECT 1 FROM dbo.Groups G WHERE G.GroupID = T.GroupID AND G.AdminID = @ActingUserID)
                OR EXISTS (SELECT 1 FROM dbo.GroupMembers M WHERE M.GroupID = T.GroupID AND M.UserID = @ActingUserID)))
       OR (T.GroupID IS NULL AND T.UserID = @ActingUserID)
    ORDER BY CAST(J.[key] AS INT), T.Ranking, T.LevelOfImportanceID DESC;
END
GO

CREATE PROCEDURE dbo.GetTaskCounts
    @UserID           INT,
    @IncludeCompleted BIT = 0
AS
BEGIN
    DECLARE @Today DATE = CAST(GETDATE() AS DATE);
    DECLARE @AllCount INT, @ImportantCount INT, @TodayCount INT, @PlannedCount INT;
    SELECT @AllCount = COUNT(*),
           @ImportantCount = ISNULL(SUM(CASE WHEN T.LevelOfImportanceID = 3 THEN 1 ELSE 0 END), 0),
           @PlannedCount = ISNULL(SUM(CASE WHEN P.TaskID IS NOT NULL THEN 1 ELSE 0 END), 0),
           @TodayCount = ISNULL(SUM(CASE WHEN P.TaskID IS NOT NULL AND CAST(P.PlannedStartDate AS DATE) <= @Today
                                          AND (P.PlannedEndDate IS NULL OR CAST(P.PlannedEndDate AS DATE) >= @Today)
                                         THEN 1 ELSE 0 END), 0)
    FROM dbo.Tasks T
    LEFT JOIN dbo.Planned P ON P.TaskID = T.TaskID
    WHERE T.TaskParentID IS NULL
      AND (@IncludeCompleted = 1 OR T.TaskStatusID <> 3)
      AND T.UserID = @UserID AND T.GroupID IS NULL;

    SELECT @TodayCount = @TodayCount + COUNT(*)
    FROM dbo.Tasks T
    INNER JOIN dbo.Planned P ON P.TaskID = T.TaskID
    WHERE T.UserID = @UserID AND T.GroupID IS NULL
      AND T.TaskParentID IS NOT NULL
      AND (@IncludeCompleted = 1 OR T.TaskStatusID <> 3)
      AND CAST(P.PlannedStartDate AS DATE) <= @Today
      AND (P.PlannedEndDate IS NULL OR CAST(P.PlannedEndDate AS DATE) >= @Today)
      AND NOT EXISTS (SELECT 1
                      FROM dbo.Tasks TP
                      INNER JOIN dbo.Planned PP ON PP.TaskID = TP.TaskID
                      WHERE TP.TaskID = T.TaskParentID
                        AND CAST(PP.PlannedStartDate AS DATE) <= @Today
                        AND (PP.PlannedEndDate IS NULL OR CAST(PP.PlannedEndDate AS DATE) >= @Today));

    SELECT @AllCount AS AllCount, @TodayCount AS TodayCount,
           @ImportantCount AS ImportantCount, @PlannedCount AS PlannedCount;
END
GO

CREATE PROCEDURE dbo.AddAttachment
    @TaskID       INT,
    @FileName     NVARCHAR(260),
    @FileData     VARBINARY(MAX),
    @FileSizeKB   INT,
    @ActingUserID INT
AS
BEGIN
    IF dbo.CanModifyTask(@TaskID, @ActingUserID) = 0
        THROW 50001, N'AddAttachment: You do not have permission to modify this task.', 1;
    IF @FileData IS NULL
        THROW 50003, N'AddAttachment: Attachment content is required.', 1;
    IF DATALENGTH(@FileData) > 10 * 1024 * 1024
        THROW 50003, N'AddAttachment: Attachment exceeds the maximum allowed size of 10 MB.', 1;
    INSERT INTO dbo.Attachments (TaskID, FileName, FileData, FileSizeKB)
    VALUES (@TaskID, @FileName, @FileData, CAST(CEILING(DATALENGTH(@FileData) / 1024.0) AS INT));
    SELECT CAST(SCOPE_IDENTITY() AS INT) AS NewID;
END
GO

CREATE PROCEDURE dbo.GetAttachmentInfos
    @TaskIDs       NVARCHAR(MAX),
    @ActingUserID INT
AS
BEGIN
    SELECT A.AttachmentID, A.TaskID, A.FileName, A.FileSizeKB
    FROM dbo.Attachments A
    JOIN OPENJSON(@TaskIDs) J ON A.TaskID = CAST(J.value AS INT)
    WHERE dbo.CanModifyTask(A.TaskID, @ActingUserID) = 1
    ORDER BY CAST(J.[key] AS INT), A.AttachmentID;
END
GO

CREATE PROCEDURE dbo.DeleteAttachment
    @AttachmentID INT,
    @ActingUserID INT
AS
BEGIN
    DECLARE @TaskID INT = (SELECT TaskID FROM dbo.Attachments WHERE AttachmentID = @AttachmentID);
    IF @TaskID IS NULL
        THROW 50002, N'DeleteAttachment: Attachment was not found.', 1;
    IF dbo.CanModifyTask(@TaskID, @ActingUserID) = 0
        THROW 50001, N'DeleteAttachment: You do not have permission to delete this attachment.', 1;
    DELETE FROM dbo.Attachments WHERE AttachmentID = @AttachmentID;
END
GO

CREATE PROCEDURE dbo.AddPlanned
    @TaskID           INT,
    @PlannedStartDate DATETIME2,
    @PlannedEndDate   DATETIME2 = NULL,
    @RepetitionTypeID INT = 1,
    @ActingUserID     INT
AS
BEGIN
    BEGIN TRY
        BEGIN TRANSACTION;
        IF @PlannedEndDate < @PlannedStartDate
            THROW 50003, N'AddPlanned: End date cannot be before the start date.', 1;
        IF dbo.CanModifyTask(@TaskID, @ActingUserID) = 0
            THROW 50001, N'AddPlanned: You do not have permission to modify this task.', 1;
        DECLARE @PlannedID INT;
        SELECT @PlannedID = PlannedID FROM dbo.Planned WHERE TaskID = @TaskID;
        IF @PlannedID IS NULL
        BEGIN
            INSERT INTO dbo.Planned (TaskID, PlannedStartDate, PlannedEndDate, RepetitionTypeID)
            VALUES (@TaskID, @PlannedStartDate, @PlannedEndDate, @RepetitionTypeID);
            SET @PlannedID = SCOPE_IDENTITY();
        END
        ELSE
        BEGIN
            UPDATE dbo.Planned SET PlannedStartDate = @PlannedStartDate, PlannedEndDate = @PlannedEndDate,
                                   RepetitionTypeID = @RepetitionTypeID
            WHERE PlannedID = @PlannedID;
        END
        COMMIT TRANSACTION;
        SELECT @PlannedID AS NewID;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK; THROW;
    END CATCH;
END
GO

CREATE PROCEDURE dbo.AutoUpdatePlanneds
AS
BEGIN
    DECLARE @Today DATETIME2 = CAST(CAST(GETDATE() AS DATE) AS DATETIME2);
    DECLARE @Rolled TABLE (TaskID INT PRIMARY KEY, NewStart DATETIME2 NOT NULL, NewEnd DATETIME2 NULL);
    INSERT INTO @Rolled (TaskID, NewStart, NewEnd)
    SELECT P.TaskID, N.NewStart,
           DATEADD(DAY, DATEDIFF(DAY, P.PlannedStartDate, N.NewStart), P.PlannedEndDate)
    FROM dbo.Planned P
    CROSS APPLY (SELECT NewStart = dbo.NextOccurrence(P.PlannedStartDate, P.RepetitionTypeID, @Today)) N
    WHERE P.RepetitionTypeID > 1 AND P.PlannedStartDate < @Today;
    UPDATE P SET PlannedStartDate = R.NewStart, PlannedEndDate = R.NewEnd
    FROM dbo.Planned P JOIN @Rolled R ON P.TaskID = R.TaskID;
    UPDATE T SET T.TaskStatusID = 2
    FROM dbo.Tasks T JOIN @Rolled R ON T.TaskID = R.TaskID
    WHERE T.TaskStatusID = 3;
    SELECT CAST(COUNT(*) AS INT) FROM @Rolled;
END
GO

CREATE PROCEDURE dbo.GetTasksPlanned
    @UserID INT,
    @Scope  TINYINT = 0
AS
BEGIN
    DECLARE @Today DATE = CAST(GETDATE() AS DATE);

    IF @Scope = 0
    BEGIN
        SELECT T.* FROM dbo.Tasks T
        INNER JOIN dbo.Planned P ON P.TaskID = T.TaskID
        WHERE T.UserID = @UserID AND T.GroupID IS NULL AND T.TaskParentID IS NULL
        ORDER BY CAST(P.PlannedStartDate AS DATE), T.Ranking;
        RETURN;
    END

    SELECT T.* FROM dbo.Tasks T
    INNER JOIN dbo.Planned P ON P.TaskID = T.TaskID
    WHERE T.UserID = @UserID AND T.GroupID IS NULL
      AND CAST(P.PlannedStartDate AS DATE) <= @Today
      AND (P.PlannedEndDate IS NULL OR CAST(P.PlannedEndDate AS DATE) >= @Today)
      AND ( T.TaskParentID IS NULL
         OR NOT EXISTS (SELECT 1
                        FROM dbo.Tasks TP
                        INNER JOIN dbo.Planned PP ON PP.TaskID = TP.TaskID
                        WHERE TP.TaskID = T.TaskParentID
                          AND CAST(PP.PlannedStartDate AS DATE) <= @Today
                          AND (PP.PlannedEndDate IS NULL OR CAST(PP.PlannedEndDate AS DATE) >= @Today)))
    ORDER BY CAST(P.PlannedStartDate AS DATE), T.Ranking;
END
GO

CREATE PROCEDURE dbo.GetLookups
AS
BEGIN
    SELECT TaskStatusID AS ID, StatusName AS Name FROM dbo.TaskStatus ORDER BY TaskStatusID;
    SELECT RepetitionTypeID AS ID, RepetitionName AS Name FROM dbo.RepetitionTypes ORDER BY RepetitionTypeID;
    SELECT LevelOfImportanceID AS ID, LevelName AS Name, Color FROM dbo.LevelOfImportance ORDER BY LevelOfImportanceID;
    SELECT UserID, ShowName, Color FROM dbo.Users ORDER BY ShowName;
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Tasks_PersonalScope' AND object_id = OBJECT_ID(N'dbo.Tasks')) CREATE INDEX IX_Tasks_PersonalScope ON dbo.Tasks (UserID, TaskParentID, Ranking);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Tasks_GroupScope' AND object_id = OBJECT_ID(N'dbo.Tasks')) CREATE INDEX IX_Tasks_GroupScope ON dbo.Tasks (GroupID, TaskParentID, Ranking);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Tasks_Parent' AND object_id = OBJECT_ID(N'dbo.Tasks')) CREATE INDEX IX_Tasks_Parent ON dbo.Tasks (TaskParentID);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UQ_Users_UserEmail' AND object_id = OBJECT_ID(N'dbo.Users')) CREATE UNIQUE INDEX UQ_Users_UserEmail ON dbo.Users (UserEmail) WHERE UserEmail IS NOT NULL;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Groups_AdminID' AND object_id = OBJECT_ID(N'dbo.Groups')) CREATE INDEX IX_Groups_AdminID ON dbo.Groups (AdminID);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_GroupMembers_UserID' AND object_id = OBJECT_ID(N'dbo.GroupMembers')) CREATE INDEX IX_GroupMembers_UserID ON dbo.GroupMembers (UserID);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PostsInGroups_Group' AND object_id = OBJECT_ID(N'dbo.PostsInGroups')) CREATE INDEX IX_PostsInGroups_Group ON dbo.PostsInGroups (GroupID, PostID);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Attachments_TaskID' AND object_id = OBJECT_ID(N'dbo.Attachments')) CREATE INDEX IX_Attachments_TaskID ON dbo.Attachments (TaskID);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Assigned_ToUser' AND object_id = OBJECT_ID(N'dbo.Assigned')) CREATE INDEX IX_Assigned_ToUser ON dbo.Assigned (ToUserID);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UQ_Planned_TaskID' AND object_id = OBJECT_ID(N'dbo.Planned')) CREATE UNIQUE INDEX UQ_Planned_TaskID ON dbo.Planned (TaskID);
GO