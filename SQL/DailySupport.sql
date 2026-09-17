USE [StateMasterDB];
GO

-- =============================================
-- Daily Support
-- Existing ClientMaster aur LoginUsers tables ke
-- saath proper Foreign Keys ke through connection.
-- ClientMaster me koi column add/modify NAHI hota.
-- =============================================

IF OBJECT_ID(N'dbo.DailySupport', N'U') IS NULL
BEGIN

    CREATE TABLE dbo.DailySupport
    (
        Id              INT IDENTITY(1, 1)  NOT NULL
                        CONSTRAINT PK_DailySupport
                        PRIMARY KEY,

        SupportDate     DATETIME            NOT NULL,

        ClientId        INT                 NOT NULL,

        UserId          INT                 NOT NULL,

        SupportType     NVARCHAR(100)       NOT NULL,

        Subject         NVARCHAR(300)       NOT NULL,

        Description     NVARCHAR(MAX)       NOT NULL,

        ActionTaken     NVARCHAR(MAX)       NULL,

        Status          NVARCHAR(30)        NOT NULL
                        CONSTRAINT DF_DailySupport_Status
                        DEFAULT 'Open',

        Priority        NVARCHAR(20)        NOT NULL
                        CONSTRAINT DF_DailySupport_Priority
                        DEFAULT 'Medium',

        StartTime       DATETIME            NULL,

        EndTime         DATETIME            NULL,

        FollowUpDate    DATETIME            NULL,

        Remarks         NVARCHAR(MAX)       NULL,

        EntryOn         DATETIME            NOT NULL
                        CONSTRAINT DF_DailySupport_EntryOn
                        DEFAULT GETDATE(),

        CONSTRAINT FK_DailySupport_Client
            FOREIGN KEY (ClientId)
            REFERENCES dbo.ClientMaster (Id),

        CONSTRAINT FK_DailySupport_User
            FOREIGN KEY (UserId)
            REFERENCES dbo.LoginUsers (Id)
    );

END
GO

PRINT 'DailySupport table ready.';

-- Optional: index on ClientId for JOIN performance
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = 'IX_DailySupport_ClientId'
      AND object_id = OBJECT_ID(N'dbo.DailySupport')
)
BEGIN
    CREATE INDEX IX_DailySupport_ClientId
        ON dbo.DailySupport (ClientId);
END
GO

PRINT 'DailySupport indexes ready.';
GO