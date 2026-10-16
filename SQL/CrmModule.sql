-- =============================================
-- CRM MODULE
-- Existing ClientMaster / LoginUsers / States
-- tables ke saath proper Foreign Keys ke through
-- connection. Koi existing table add/modify NAHI
-- hota.
--
-- NOTE: Ye script target database ke andar run karein
-- (e.g. sqlcmd -d db68405 -i SQL/CrmModule.sql).
-- Idempotent hai: sirf missing tables/indexes banati hai.
-- =============================================


-- =============================================
-- 1) CRM Lead
-- =============================================

IF OBJECT_ID(N'dbo.CrmLead', N'U') IS NULL
BEGIN

    CREATE TABLE dbo.CrmLead
    (
        Id                  INT IDENTITY(1, 1)  NOT NULL
                            CONSTRAINT PK_CrmLead
                            PRIMARY KEY,

        LeadNo              NVARCHAR(20)        NULL,

        LeadDate            DATETIME            NULL,

        LeadName            NVARCHAR(150)       NOT NULL,

        CompanyName         NVARCHAR(200)       NULL,

        MobileNo            NVARCHAR(20)        NOT NULL,

        Email               NVARCHAR(150)       NULL,

        City                NVARCHAR(100)       NULL,

        StateId             INT                 NULL,

        ClientId            INT                 NULL,

        Source              NVARCHAR(50)        NOT NULL,

        AssignedTo          INT                 NULL,

        Status              NVARCHAR(30)        NOT NULL
                            CONSTRAINT DF_CrmLead_Status
                            DEFAULT ('New'),

        Priority            NVARCHAR(20)        NOT NULL
                            CONSTRAINT DF_CrmLead_Priority
                            DEFAULT ('Medium'),

        Requirement         NVARCHAR(MAX)       NULL,

        EstimatedValue      DECIMAL(18, 2)      NULL,

        NextFollowUpDate    DATETIME            NULL,

        LostReason          NVARCHAR(300)       NULL,

        IsActive            BIT                 NOT NULL
                            CONSTRAINT DF_CrmLead_IsActive
                            DEFAULT (1),

        CreatedByUserId     INT                 NULL,

        EntryOn             DATETIME            NOT NULL
                            CONSTRAINT DF_CrmLead_EntryOn
                            DEFAULT GETDATE(),

        CONSTRAINT FK_CrmLead_State
            FOREIGN KEY (StateId)
            REFERENCES dbo.States (Id),

        CONSTRAINT FK_CrmLead_Client
            FOREIGN KEY (ClientId)
            REFERENCES dbo.ClientMaster (Id),

        CONSTRAINT FK_CrmLead_AssignedTo
            FOREIGN KEY (AssignedTo)
            REFERENCES dbo.LoginUsers (Id),

        CONSTRAINT FK_CrmLead_CreatedBy
            FOREIGN KEY (CreatedByUserId)
            REFERENCES dbo.LoginUsers (Id)
    );

END
GO

PRINT 'CrmLead table ready.';
GO


-- =============================================
-- 2) CRM Follow-Up
-- =============================================

IF OBJECT_ID(N'dbo.CrmFollowUp', N'U') IS NULL
BEGIN

    CREATE TABLE dbo.CrmFollowUp
    (
        Id                  INT IDENTITY(1, 1)  NOT NULL
                            CONSTRAINT PK_CrmFollowUp
                            PRIMARY KEY,

        LeadId              INT                 NOT NULL,

        FollowUpDate        DATETIME            NOT NULL,

        FollowUpTime        TIME(0)             NULL,

        ContactMode         NVARCHAR(30)        NOT NULL,

        Remarks             NVARCHAR(MAX)       NULL,

        NextAction          NVARCHAR(MAX)       NULL,

        NextFollowUpDate    DATETIME            NULL,

        Status              NVARCHAR(30)        NOT NULL
                            CONSTRAINT DF_CrmFollowUp_Status
                            DEFAULT ('Pending'),

        DoneOn              DATETIME            NULL,

        UserId              INT                 NULL,

        EntryOn             DATETIME            NOT NULL
                            CONSTRAINT DF_CrmFollowUp_EntryOn
                            DEFAULT GETDATE(),

        CONSTRAINT FK_CrmFollowUp_Lead
            FOREIGN KEY (LeadId)
            REFERENCES dbo.CrmLead (Id)
            ON DELETE CASCADE,

        CONSTRAINT FK_CrmFollowUp_User
            FOREIGN KEY (UserId)
            REFERENCES dbo.LoginUsers (Id)
    );

END
GO

PRINT 'CrmFollowUp table ready.';
GO


-- =============================================
-- 3) CRM Lead History
-- =============================================

IF OBJECT_ID(N'dbo.CrmLeadHistory', N'U') IS NULL
BEGIN

    CREATE TABLE dbo.CrmLeadHistory
    (
        Id                  INT IDENTITY(1, 1)  NOT NULL
                            CONSTRAINT PK_CrmLeadHistory
                            PRIMARY KEY,

        LeadId              INT                 NOT NULL,

        EventDate           DATETIME            NOT NULL
                            CONSTRAINT DF_CrmLeadHistory_EventDate
                            DEFAULT GETDATE(),

        EventType           NVARCHAR(40)        NOT NULL,

        OldValue            NVARCHAR(200)       NULL,

        NewValue            NVARCHAR(200)       NULL,

        Remarks             NVARCHAR(MAX)       NULL,

        UserId              INT                 NULL,

        EntryOn             DATETIME            NOT NULL
                            CONSTRAINT DF_CrmLeadHistory_EntryOn
                            DEFAULT GETDATE(),

        CONSTRAINT FK_CrmLeadHistory_Lead
            FOREIGN KEY (LeadId)
            REFERENCES dbo.CrmLead (Id)
            ON DELETE CASCADE,

        CONSTRAINT FK_CrmLeadHistory_User
            FOREIGN KEY (UserId)
            REFERENCES dbo.LoginUsers (Id)
    );

END
GO

PRINT 'CrmLeadHistory table ready.';
GO


-- =============================================
-- Indexes
-- =============================================

IF OBJECT_ID(N'dbo.CrmLead', N'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_CrmLead_Status')
        CREATE INDEX IX_CrmLead_Status ON dbo.CrmLead (Status);

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_CrmLead_AssignedTo')
        CREATE INDEX IX_CrmLead_AssignedTo ON dbo.CrmLead (AssignedTo);

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_CrmLead_NextFollowUpDate')
        CREATE INDEX IX_CrmLead_NextFollowUpDate ON dbo.CrmLead (NextFollowUpDate);

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_CrmLead_ClientId')
        CREATE INDEX IX_CrmLead_ClientId ON dbo.CrmLead (ClientId);

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_CrmLead_MobileNo')
        CREATE INDEX IX_CrmLead_MobileNo ON dbo.CrmLead (MobileNo);
END
GO

IF OBJECT_ID(N'dbo.CrmFollowUp', N'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_CrmFollowUp_LeadId')
        CREATE INDEX IX_CrmFollowUp_LeadId ON dbo.CrmFollowUp (LeadId);

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_CrmFollowUp_FollowUpDate')
        CREATE INDEX IX_CrmFollowUp_FollowUpDate ON dbo.CrmFollowUp (FollowUpDate);

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_CrmFollowUp_Status')
        CREATE INDEX IX_CrmFollowUp_Status ON dbo.CrmFollowUp (Status);

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_CrmFollowUp_NextFollowUpDate')
        CREATE INDEX IX_CrmFollowUp_NextFollowUpDate ON dbo.CrmFollowUp (NextFollowUpDate);
END
GO

IF OBJECT_ID(N'dbo.CrmLeadHistory', N'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_CrmLeadHistory_LeadId')
        CREATE INDEX IX_CrmLeadHistory_LeadId ON dbo.CrmLeadHistory (LeadId);

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_CrmLeadHistory_EventDate')
        CREATE INDEX IX_CrmLeadHistory_EventDate ON dbo.CrmLeadHistory (EventDate);
END
GO

PRINT 'CRM module indexes ready.';
GO
