PRAGMA journal_mode=WAL;

CREATE TABLE Permissions
(
    PermissionID   INTEGER PRIMARY KEY AUTOINCREMENT,
    PermissionName TEXT NOT NULL UNIQUE
);

CREATE TABLE Users
(
    UserID       INTEGER PRIMARY KEY AUTOINCREMENT,
    UserName     TEXT NOT NULL COLLATE NOCASE UNIQUE,
    ShowName     TEXT NOT NULL,
    UserEmail    TEXT NULL COLLATE NOCASE,
    PasswordHash TEXT NOT NULL,
    PermissionID INTEGER NOT NULL,
    Color        TEXT NOT NULL DEFAULT '#FFFFFF',
    CreatedAt    TEXT NOT NULL DEFAULT (datetime('now')),
    FOREIGN KEY (PermissionID) REFERENCES Permissions (PermissionID)
);

CREATE TABLE Groups
(
    GroupID          INTEGER PRIMARY KEY AUTOINCREMENT,
    AdminID          INTEGER NOT NULL,
    GroupName        TEXT NOT NULL,
    GroupDescription TEXT NULL,
    Color            TEXT NOT NULL DEFAULT '#FFFFFF',
    FOREIGN KEY (AdminID) REFERENCES Users (UserID)
);

CREATE TABLE GroupMembers
(
    GroupMemberID INTEGER PRIMARY KEY AUTOINCREMENT,
    GroupID       INTEGER NOT NULL,
    UserID        INTEGER NOT NULL,
    UNIQUE (GroupID, UserID),
    FOREIGN KEY (GroupID) REFERENCES Groups (GroupID) ON DELETE CASCADE,
    FOREIGN KEY (UserID)  REFERENCES Users (UserID)  ON DELETE CASCADE
);

CREATE TABLE PostsInGroups
(
    PostID      INTEGER PRIMARY KEY AUTOINCREMENT,
    GroupID     INTEGER NOT NULL,
    UserID      INTEGER NOT NULL,
    PostContent TEXT NOT NULL,
    PostDate    TEXT NOT NULL DEFAULT (date('now', 'localtime')),
    CreatedAt   TEXT NOT NULL DEFAULT (datetime('now')),
    FOREIGN KEY (GroupID) REFERENCES Groups (GroupID) ON DELETE CASCADE,
    FOREIGN KEY (UserID)  REFERENCES Users (UserID)
);

CREATE TABLE TaskStatus
(
    TaskStatusID      INTEGER PRIMARY KEY AUTOINCREMENT,
    StatusName        TEXT NOT NULL UNIQUE,
    StatusDescription TEXT NULL
);

CREATE TABLE LevelOfImportance
(
    LevelOfImportanceID INTEGER PRIMARY KEY AUTOINCREMENT,
    LevelName           TEXT NOT NULL UNIQUE,
    Color               TEXT NOT NULL
);

CREATE TABLE Tasks
(
    TaskID              INTEGER PRIMARY KEY AUTOINCREMENT,
    TaskParentID        INTEGER NULL,
    TaskStatusID        INTEGER NOT NULL DEFAULT 2,
    UserID              INTEGER NOT NULL,
    GroupID             INTEGER NULL,
    Ranking             INTEGER NOT NULL DEFAULT 0,
    TaskName            TEXT NOT NULL,
    LevelOfImportanceID INTEGER NOT NULL DEFAULT 2,
    Description         TEXT NULL,
    CreationDate        TEXT NOT NULL DEFAULT (date('now', 'localtime')),
    Color               TEXT NULL DEFAULT '#FFFFFF',
    FOREIGN KEY (TaskParentID) REFERENCES Tasks (TaskID),
    FOREIGN KEY (TaskStatusID) REFERENCES TaskStatus (TaskStatusID),
    FOREIGN KEY (LevelOfImportanceID) REFERENCES LevelOfImportance (LevelOfImportanceID),
    FOREIGN KEY (UserID) REFERENCES Users (UserID),
    FOREIGN KEY (GroupID) REFERENCES Groups (GroupID)
);

CREATE TABLE Attachments
(
    AttachmentID INTEGER PRIMARY KEY AUTOINCREMENT,
    TaskID       INTEGER NOT NULL,
    FileName     TEXT NOT NULL,
    FileData     BLOB NOT NULL,
    FileSizeKB   INTEGER NOT NULL,
    FOREIGN KEY (TaskID) REFERENCES Tasks (TaskID) ON DELETE CASCADE
);

CREATE TABLE Assigned
(
    AssignedID INTEGER PRIMARY KEY AUTOINCREMENT,
    TaskID     INTEGER NOT NULL,
    ToUserID   INTEGER NOT NULL,
    UNIQUE (TaskID, ToUserID),
    FOREIGN KEY (TaskID)   REFERENCES Tasks (TaskID) ON DELETE CASCADE,
    FOREIGN KEY (ToUserID) REFERENCES Users (UserID)
);

CREATE TABLE RepetitionTypes
(
    RepetitionTypeID INTEGER PRIMARY KEY AUTOINCREMENT,
    RepetitionName   TEXT NOT NULL UNIQUE
);

CREATE TABLE Planned
(
    PlannedID        INTEGER PRIMARY KEY AUTOINCREMENT,
    TaskID           INTEGER NOT NULL UNIQUE,
    PlannedStartDate TEXT NOT NULL,
    PlannedEndDate   TEXT NULL,
    RepetitionTypeID INTEGER NULL,
    FOREIGN KEY (TaskID) REFERENCES Tasks (TaskID) ON DELETE CASCADE,
    FOREIGN KEY (RepetitionTypeID) REFERENCES RepetitionTypes (RepetitionTypeID)
);

INSERT INTO Permissions (PermissionName) VALUES ('Person');
INSERT INTO Permissions (PermissionName) VALUES ('Admin');

INSERT INTO TaskStatus (StatusName, StatusDescription) VALUES ('Pending', 'Task is pending for admin approval');
INSERT INTO TaskStatus (StatusName, StatusDescription) VALUES ('Incomplete', 'Task is incomplete');
INSERT INTO TaskStatus (StatusName, StatusDescription) VALUES ('Completed', 'Task is completed');

INSERT INTO LevelOfImportance (LevelName, Color) VALUES ('Low', '#7BA05B');
INSERT INTO LevelOfImportance (LevelName, Color) VALUES ('Medium', '#C77D00');
INSERT INTO LevelOfImportance (LevelName, Color) VALUES ('High', '#D13438');

INSERT INTO RepetitionTypes (RepetitionName) VALUES ('None');
INSERT INTO RepetitionTypes (RepetitionName) VALUES ('Daily');
INSERT INTO RepetitionTypes (RepetitionName) VALUES ('Weekly');
INSERT INTO RepetitionTypes (RepetitionName) VALUES ('Monthly');
INSERT INTO RepetitionTypes (RepetitionName) VALUES ('Yearly');

CREATE INDEX IX_Tasks_PersonalScope ON Tasks (UserID, TaskParentID, Ranking);
CREATE INDEX IX_Tasks_GroupScope ON Tasks (GroupID, TaskParentID, Ranking);
CREATE INDEX IX_Tasks_Parent ON Tasks (TaskParentID);
CREATE UNIQUE INDEX UQ_Users_UserEmail ON Users (UserEmail) WHERE UserEmail IS NOT NULL;
CREATE INDEX IX_Groups_AdminID ON Groups (AdminID);
CREATE INDEX IX_GroupMembers_UserID ON GroupMembers (UserID);
CREATE INDEX IX_PostsInGroups_Group ON PostsInGroups (GroupID, PostID);
CREATE INDEX IX_Attachments_TaskID ON Attachments (TaskID);
CREATE INDEX IX_Assigned_ToUser ON Assigned (ToUserID);
CREATE UNIQUE INDEX UQ_Planned_TaskID ON Planned (TaskID);