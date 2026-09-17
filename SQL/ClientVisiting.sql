USE [StateMasterDB];
GO

-- =============================================
-- Client Visiting
-- ClientMaster aur LoginUsers tables ke saath
-- proper Foreign Keys ke through connection.
-- =============================================

IF OBJECT_ID(N'dbo.ClientVisiting', N'U') IS NULL
BEGIN

    CREATE TABLE dbo.ClientVisiting
    (
        Id              INT IDENTITY(1, 1)  NOT NULL
                        CONSTRAINT PK_ClientVisiting
                        PRIMARY KEY,

        VisitDate       DATETIME            NOT NULL,

        ClientId        INT                 NOT NULL,

        UserId          INT                 NOT NULL,

        VisitType       NVARCHAR(100)       NOT NULL,

        PersonMet       NVARCHAR(150)       NULL,

        Subject         NVARCHAR(300)       NOT NULL,

        Discussion      NVARCHAR(MAX)       NULL,

        Outcome         NVARCHAR(MAX)       NULL,

        NextAction      NVARCHAR(MAX)       NULL,

        FollowUpDate    DATETIME            NULL,

        Remarks         NVARCHAR(MAX)       NULL,

        Status          NVARCHAR(30)        NOT NULL
                        CONSTRAINT DF_ClientVisiting_Status
                        DEFAULT 'Planned',

        PreviousMeetingId INT               NOT NULL
                        CONSTRAINT DF_ClientVisiting_PreviousMeetingId
                        DEFAULT (0),

        EntryOn         DATETIME            NOT NULL
                        CONSTRAINT DF_ClientVisiting_EntryOn
                        DEFAULT GETDATE(),

        CONSTRAINT FK_ClientVisiting_Client
            FOREIGN KEY (ClientId)
            REFERENCES dbo.ClientMaster (Id),

        CONSTRAINT FK_ClientVisiting_User
            FOREIGN KEY (UserId)
            REFERENCES dbo.LoginUsers (Id)
    );

END
GO

PRINT 'ClientVisiting table ready.';

-- Optional: index on ClientId for JOIN performance
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = 'IX_ClientVisiting_ClientId'
      AND object_id = OBJECT_ID(N'dbo.ClientVisiting')
)
BEGIN
    CREATE INDEX IX_ClientVisiting_ClientId
        ON dbo.ClientVisiting (ClientId);
END
GO

PRINT 'ClientVisiting indexes ready.';
GO