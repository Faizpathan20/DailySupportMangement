-- ============================================================
-- DAILY SUPPORT  -  TICKETING SYSTEM (Step 1)
-- Database: Db_Faiz
-- SQL Server / SSMS
--
-- Ye script:
--   1) 6 master tables banati hai (Mst_ prefix) + seed data
--   2) DailySupport table ko upgrade karti hai (naye columns)
--   3) Trn_DailySupport_Updates   (roz ka work log) banati hai
--   4) Trn_DailySupport_History   (audit trail)      banati hai
--   5) Trn_DailySupport_Review    (boss review)      banati hai
--
-- Idempotent hai: dobara run karne pe kuch tootta nahi.
-- Purana data delete karne ka option neeche (OPTIONAL) hai.
--
-- Run:  sqlcmd -S wa.bestit.in,1433 -d Db_Faiz -U web_user -P *** -i SQL/DailySupport_Ticketing.sql
-- ============================================================

USE [Db_Faiz];
GO

SET NOCOUNT ON;
GO


-- ============================================================
-- 0) OPTIONAL: purana DailySupport data saaf karna
--    (Fresh start ke liye). Isko chalao tabhi jab aap
--    purana data delete karna chahte ho.
--    Comment se hata dein agar chalana ho.
-- ============================================================

-- DELETE FROM dbo.DailySupport;
-- GO


-- ============================================================
-- 1) MASTER TABLES
-- ============================================================

-- 1.1 Mst_Channel --------------------------------------------
IF OBJECT_ID(N'dbo.Mst_Channel', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Mst_Channel
    (
        ChannelId    INT IDENTITY(1,1) NOT NULL
                     CONSTRAINT PK_Mst_Channel PRIMARY KEY,
        ChannelName  NVARCHAR(100)     NOT NULL,
        IsActive     BIT               NOT NULL
                     CONSTRAINT DF_Mst_Channel_IsActive DEFAULT (1),
        SortOrder    INT               NOT NULL
                     CONSTRAINT DF_Mst_Channel_SortOrder DEFAULT (0),
        EntryOn      DATETIME2         NOT NULL
                     CONSTRAINT DF_Mst_Channel_EntryOn
                     DEFAULT (DATEADD(MINUTE, 330, SYSUTCDATETIME()))
    );
END
GO


-- 1.2 Mst_CallType -------------------------------------------
IF OBJECT_ID(N'dbo.Mst_CallType', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Mst_CallType
    (
        CallTypeId    INT IDENTITY(1,1) NOT NULL
                      CONSTRAINT PK_Mst_CallType PRIMARY KEY,
        CallTypeName  NVARCHAR(100)     NOT NULL,
        IsActive      BIT               NOT NULL
                      CONSTRAINT DF_Mst_CallType_IsActive DEFAULT (1),
        SortOrder     INT               NOT NULL
                      CONSTRAINT DF_Mst_CallType_SortOrder DEFAULT (0),
        EntryOn       DATETIME2         NOT NULL
                      CONSTRAINT DF_Mst_CallType_EntryOn
                      DEFAULT (DATEADD(MINUTE, 330, SYSUTCDATETIME()))
    );
END
GO


-- 1.3 Mst_Category -------------------------------------------
IF OBJECT_ID(N'dbo.Mst_Category', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Mst_Category
    (
        CategoryId    INT IDENTITY(1,1) NOT NULL
                      CONSTRAINT PK_Mst_Category PRIMARY KEY,
        CategoryName  NVARCHAR(100)     NOT NULL,
        IsActive      BIT               NOT NULL
                      CONSTRAINT DF_Mst_Category_IsActive DEFAULT (1),
        SortOrder     INT               NOT NULL
                      CONSTRAINT DF_Mst_Category_SortOrder DEFAULT (0),
        EntryOn       DATETIME2         NOT NULL
                      CONSTRAINT DF_Mst_Category_EntryOn
                      DEFAULT (DATEADD(MINUTE, 330, SYSUTCDATETIME()))
    );
END
GO


-- 1.4 Mst_SubCategory (Category ke andar) --------------------
IF OBJECT_ID(N'dbo.Mst_SubCategory', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Mst_SubCategory
    (
        SubCategoryId    INT IDENTITY(1,1) NOT NULL
                         CONSTRAINT PK_Mst_SubCategory PRIMARY KEY,
        CategoryId       INT               NOT NULL,
        SubCategoryName  NVARCHAR(100)     NOT NULL,
        IsActive         BIT               NOT NULL
                         CONSTRAINT DF_Mst_SubCategory_IsActive DEFAULT (1),
        SortOrder        INT               NOT NULL
                         CONSTRAINT DF_Mst_SubCategory_SortOrder DEFAULT (0),
        EntryOn          DATETIME2         NOT NULL
                         CONSTRAINT DF_Mst_SubCategory_EntryOn
                         DEFAULT (DATEADD(MINUTE, 330, SYSUTCDATETIME())),

        CONSTRAINT FK_Mst_SubCategory_Category
            FOREIGN KEY (CategoryId)
            REFERENCES dbo.Mst_Category (CategoryId)
    );

    CREATE INDEX IX_Mst_SubCategory_CategoryId
        ON dbo.Mst_SubCategory (CategoryId);
END
GO


-- 1.5 Mst_Priority (SLA hours ke saath) ----------------------
IF OBJECT_ID(N'dbo.Mst_Priority', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Mst_Priority
    (
        PriorityId    INT IDENTITY(1,1) NOT NULL
                      CONSTRAINT PK_Mst_Priority PRIMARY KEY,
        PriorityName  NVARCHAR(50)      NOT NULL,
        SlaHours      INT               NOT NULL
                      CONSTRAINT DF_Mst_Priority_SlaHours DEFAULT (24),
        IsActive      BIT               NOT NULL
                      CONSTRAINT DF_Mst_Priority_IsActive DEFAULT (1),
        SortOrder     INT               NOT NULL
                      CONSTRAINT DF_Mst_Priority_SortOrder DEFAULT (0),
        EntryOn       DATETIME2         NOT NULL
                      CONSTRAINT DF_Mst_Priority_EntryOn
                      DEFAULT (DATEADD(MINUTE, 330, SYSUTCDATETIME()))
    );
END
GO


-- 1.6 Mst_Status ---------------------------------------------
IF OBJECT_ID(N'dbo.Mst_Status', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Mst_Status
    (
        StatusId    INT IDENTITY(1,1) NOT NULL
                    CONSTRAINT PK_Mst_Status PRIMARY KEY,
        StatusName  NVARCHAR(50)      NOT NULL,
        SortOrder   INT               NOT NULL
                    CONSTRAINT DF_Mst_Status_SortOrder DEFAULT (0),
        IsClosed    BIT               NOT NULL
                    CONSTRAINT DF_Mst_Status_IsClosed DEFAULT (0),
        IsActive    BIT               NOT NULL
                    CONSTRAINT DF_Mst_Status_IsActive DEFAULT (1),
        EntryOn     DATETIME2         NOT NULL
                    CONSTRAINT DF_Mst_Status_EntryOn
                    DEFAULT (DATEADD(MINUTE, 330, SYSUTCDATETIME()))
    );
END
GO


-- ============================================================
-- 2) MASTER SEED DATA  (sirf tab jab table khaali ho)
-- ============================================================

IF NOT EXISTS (SELECT 1 FROM dbo.Mst_Channel)
BEGIN
    INSERT INTO dbo.Mst_Channel (ChannelName, SortOrder) VALUES
        (N'Phone',            1),
        (N'WhatsApp',         2),
        (N'Email',            3),
        (N'Walk-in / Service',4);
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Mst_CallType)
BEGIN
    INSERT INTO dbo.Mst_CallType (CallTypeName, SortOrder) VALUES
        (N'Inquiry',          1),
        (N'Complaint',        2),
        (N'Service',          3),
        (N'New Requirement',  4);
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Mst_Category)
BEGIN
    INSERT INTO dbo.Mst_Category (CategoryName, SortOrder) VALUES
        (N'Hardware',    1),
        (N'Software',    2),
        (N'Networking',  3),
        (N'Billing',     4),
        (N'Printer',     5);
END
GO

-- SubCategory: Category ke hisaab se (CategoryId se map)
IF NOT EXISTS (SELECT 1 FROM dbo.Mst_SubCategory)
BEGIN
    DECLARE @HardwareId INT = (SELECT CategoryId FROM dbo.Mst_Category WHERE CategoryName = N'Hardware');
    DECLARE @SoftwareId INT = (SELECT CategoryId FROM dbo.Mst_Category WHERE CategoryName = N'Software');
    DECLARE @NetworkId  INT = (SELECT CategoryId FROM dbo.Mst_Category WHERE CategoryName = N'Networking');
    DECLARE @BillingId  INT = (SELECT CategoryId FROM dbo.Mst_Category WHERE CategoryName = N'Billing');
    DECLARE @PrinterId  INT = (SELECT CategoryId FROM dbo.Mst_Category WHERE CategoryName = N'Printer');

    INSERT INTO dbo.Mst_SubCategory (CategoryId, SubCategoryName, SortOrder) VALUES
        (@HardwareId, N'Desktop / Laptop',  1),
        (@HardwareId, N'RAM / HDD Upgrade', 2),
        (@HardwareId, N'Keyboard / Mouse',  3),
        (@SoftwareId, N'Installation',      1),
        (@SoftwareId, N'License / Renewal', 2),
        (@SoftwareId, N'Update / Patch',    3),
        (@NetworkId,  N'Internet Down',     1),
        (@NetworkId,  N'Router / Switch',   2),
        (@NetworkId,  N'LAN / Cable',       3),
        (@BillingId,  N'Invoice Issue',     1),
        (@BillingId,  N'Payment Follow-up', 2),
        (@PrinterId,  N'Toner / Cartridge', 1),
        (@PrinterId,  N'Printer Not Working', 2);
END
GO

-- Priority: SlaHours = kitne ghante me resolve karna hai
IF NOT EXISTS (SELECT 1 FROM dbo.Mst_Priority)
BEGIN
    INSERT INTO dbo.Mst_Priority (PriorityName, SlaHours, SortOrder) VALUES
        (N'Urgent', 2,  1),
        (N'High',   8,  2),
        (N'Medium', 24, 3),
        (N'Low',    48, 4);
END
GO

-- Status: workflow order. IsClosed = ticket band ho gaya
IF NOT EXISTS (SELECT 1 FROM dbo.Mst_Status)
BEGIN
    INSERT INTO dbo.Mst_Status (StatusName, SortOrder, IsClosed) VALUES
        (N'Open',           1, 0),
        (N'In Progress',    2, 0),
        (N'On Hold',        3, 0),
        (N'Pending Review', 4, 0),
        (N'Resolved',       5, 1),
        (N'Closed',         6, 1);
END
GO


-- ============================================================
-- 3) DailySupport UPGRADE (purane columns rahenge, naye add)
-- ============================================================

IF COL_LENGTH(N'dbo.DailySupport', N'SubCategoryId') IS NULL
    ALTER TABLE dbo.DailySupport ADD SubCategoryId INT NULL;
GO

IF COL_LENGTH(N'dbo.DailySupport', N'ChannelId') IS NULL
    ALTER TABLE dbo.DailySupport ADD ChannelId INT NULL;
GO

IF COL_LENGTH(N'dbo.DailySupport', N'CallTypeId') IS NULL
    ALTER TABLE dbo.DailySupport ADD CallTypeId INT NULL;
GO

IF COL_LENGTH(N'dbo.DailySupport', N'CategoryId') IS NULL
    ALTER TABLE dbo.DailySupport ADD CategoryId INT NULL;
GO

IF COL_LENGTH(N'dbo.DailySupport', N'PriorityId') IS NULL
    ALTER TABLE dbo.DailySupport ADD PriorityId INT NULL;
GO

IF COL_LENGTH(N'dbo.DailySupport', N'StatusId') IS NULL
    ALTER TABLE dbo.DailySupport ADD StatusId INT NULL;
GO

-- AssignedTo: kis user ko ticket diya gaya (UserId = creator)
IF COL_LENGTH(N'dbo.DailySupport', N'AssignedTo') IS NULL
    ALTER TABLE dbo.DailySupport ADD AssignedTo INT NULL;
GO

IF COL_LENGTH(N'dbo.DailySupport', N'SlaDue') IS NULL
    ALTER TABLE dbo.DailySupport ADD SlaDue DATETIME2 NULL;
GO

IF COL_LENGTH(N'dbo.DailySupport', N'ResolvedOn') IS NULL
    ALTER TABLE dbo.DailySupport ADD ResolvedOn DATETIME2 NULL;
GO

IF COL_LENGTH(N'dbo.DailySupport', N'ClosedOn') IS NULL
    ALTER TABLE dbo.DailySupport ADD ClosedOn DATETIME2 NULL;
GO

IF COL_LENGTH(N'dbo.DailySupport', N'TimeSpent') IS NULL
    ALTER TABLE dbo.DailySupport ADD TimeSpent INT NULL;
GO

IF COL_LENGTH(N'dbo.DailySupport', N'ReviewStatus') IS NULL
    ALTER TABLE dbo.DailySupport ADD ReviewStatus NVARCHAR(20) NULL;
GO

IF COL_LENGTH(N'dbo.DailySupport', N'ReviewedBy') IS NULL
    ALTER TABLE dbo.DailySupport ADD ReviewedBy INT NULL;
GO

IF COL_LENGTH(N'dbo.DailySupport', N'ReviewedAt') IS NULL
    ALTER TABLE dbo.DailySupport ADD ReviewedAt DATETIME2 NULL;
GO

IF COL_LENGTH(N'dbo.DailySupport', N'CreatedBy') IS NULL
    ALTER TABLE dbo.DailySupport ADD CreatedBy INT NULL;
GO

IF COL_LENGTH(N'dbo.DailySupport', N'CreatedAt') IS NULL
    ALTER TABLE dbo.DailySupport ADD CreatedAt DATETIME2 NULL;
GO

IF COL_LENGTH(N'dbo.DailySupport', N'UpdatedAt') IS NULL
    ALTER TABLE dbo.DailySupport ADD UpdatedAt DATETIME2 NULL;
GO


-- FKs (sirf tab add karo jab constraint pehle se na ho)
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_DailySupport_AssignedTo')
    ALTER TABLE dbo.DailySupport
        ADD CONSTRAINT FK_DailySupport_AssignedTo
        FOREIGN KEY (AssignedTo) REFERENCES dbo.LoginUsers (Id);
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_DailySupport_Channel')
    ALTER TABLE dbo.DailySupport
        ADD CONSTRAINT FK_DailySupport_Channel
        FOREIGN KEY (ChannelId) REFERENCES dbo.Mst_Channel (ChannelId);
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_DailySupport_CallType')
    ALTER TABLE dbo.DailySupport
        ADD CONSTRAINT FK_DailySupport_CallType
        FOREIGN KEY (CallTypeId) REFERENCES dbo.Mst_CallType (CallTypeId);
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_DailySupport_Category')
    ALTER TABLE dbo.DailySupport
        ADD CONSTRAINT FK_DailySupport_Category
        FOREIGN KEY (CategoryId) REFERENCES dbo.Mst_Category (CategoryId);
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_DailySupport_SubCategory')
    ALTER TABLE dbo.DailySupport
        ADD CONSTRAINT FK_DailySupport_SubCategory
        FOREIGN KEY (SubCategoryId) REFERENCES dbo.Mst_SubCategory (SubCategoryId);
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_DailySupport_Priority')
    ALTER TABLE dbo.DailySupport
        ADD CONSTRAINT FK_DailySupport_Priority
        FOREIGN KEY (PriorityId) REFERENCES dbo.Mst_Priority (PriorityId);
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_DailySupport_Status')
    ALTER TABLE dbo.DailySupport
        ADD CONSTRAINT FK_DailySupport_Status
        FOREIGN KEY (StatusId) REFERENCES dbo.Mst_Status (StatusId);
GO


-- Indexes (list/filter performance ke liye)
IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = 'IX_DailySupport_StatusId'
                 AND object_id = OBJECT_ID(N'dbo.DailySupport'))
    CREATE INDEX IX_DailySupport_StatusId ON dbo.DailySupport (StatusId);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = 'IX_DailySupport_AssignedTo'
                 AND object_id = OBJECT_ID(N'dbo.DailySupport'))
    CREATE INDEX IX_DailySupport_AssignedTo ON dbo.DailySupport (AssignedTo);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = 'IX_DailySupport_SlaDue'
                 AND object_id = OBJECT_ID(N'dbo.DailySupport'))
    CREATE INDEX IX_DailySupport_SlaDue ON dbo.DailySupport (SlaDue);
GO


-- ============================================================
-- 4) Trn_DailySupport_Updates  (roz ka work log)
--    In Progress karne ke liye kam se kam 1 row mandatory.
-- ============================================================

IF OBJECT_ID(N'dbo.Trn_DailySupport_Updates', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Trn_DailySupport_Updates
    (
        UpdateId     INT IDENTITY(1,1) NOT NULL
                     CONSTRAINT PK_Trn_DailySupport_Updates PRIMARY KEY,
        TicketId     INT               NOT NULL,
        WorkDate     DATE              NOT NULL,
        WorkDone     NVARCHAR(MAX)     NOT NULL,
        TimeSpent    INT               NULL,      -- minutes
        NextAction   NVARCHAR(500)     NULL,
        OldStatusId  INT               NULL,
        NewStatusId  INT               NULL,
        UpdatedBy    INT               NOT NULL,
        EntryOn      DATETIME2         NOT NULL
                     CONSTRAINT DF_Trn_DSUpd_EntryOn
                     DEFAULT (DATEADD(MINUTE, 330, SYSUTCDATETIME())),

        CONSTRAINT FK_Trn_DSUpd_Ticket
            FOREIGN KEY (TicketId)
            REFERENCES dbo.DailySupport (Id),

        CONSTRAINT FK_Trn_DSUpd_UpdatedBy
            FOREIGN KEY (UpdatedBy)
            REFERENCES dbo.LoginUsers (Id),

        CONSTRAINT FK_Trn_DSUpd_OldStatus
            FOREIGN KEY (OldStatusId)
            REFERENCES dbo.Mst_Status (StatusId),

        CONSTRAINT FK_Trn_DSUpd_NewStatus
            FOREIGN KEY (NewStatusId)
            REFERENCES dbo.Mst_Status (StatusId)
    );

    CREATE INDEX IX_Trn_DSUpd_TicketId
        ON dbo.Trn_DailySupport_Updates (TicketId, WorkDate);
END
GO


-- ============================================================
-- 5) Trn_DailySupport_History  (audit trail)
-- ============================================================

IF OBJECT_ID(N'dbo.Trn_DailySupport_History', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Trn_DailySupport_History
    (
        HistoryId   INT IDENTITY(1,1) NOT NULL
                    CONSTRAINT PK_Trn_DailySupport_History PRIMARY KEY,
        TicketId    INT               NOT NULL,
        Action      NVARCHAR(50)      NOT NULL,  -- Created / StatusChanged / Assigned / ...
        OldValue    NVARCHAR(200)     NULL,
        NewValue    NVARCHAR(200)     NULL,
        Remarks     NVARCHAR(500)     NULL,
        ChangedBy   INT               NULL,
        ChangedAt   DATETIME2         NOT NULL
                    CONSTRAINT DF_Trn_DSHist_ChangedAt
                    DEFAULT (DATEADD(MINUTE, 330, SYSUTCDATETIME())),

        CONSTRAINT FK_Trn_DSHist_Ticket
            FOREIGN KEY (TicketId)
            REFERENCES dbo.DailySupport (Id),

        CONSTRAINT FK_Trn_DSHist_ChangedBy
            FOREIGN KEY (ChangedBy)
            REFERENCES dbo.LoginUsers (Id)
    );

    CREATE INDEX IX_Trn_DSHist_TicketId
        ON dbo.Trn_DailySupport_History (TicketId, ChangedAt);
END
GO


-- ============================================================
-- 6) Trn_DailySupport_Review  (boss review)
--    Ek ticket pe multiple review rows (reject -> approve).
-- ============================================================

IF OBJECT_ID(N'dbo.Trn_DailySupport_Review', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Trn_DailySupport_Review
    (
        ReviewId      INT IDENTITY(1,1) NOT NULL
                      CONSTRAINT PK_Trn_DailySupport_Review PRIMARY KEY,
        TicketId      INT               NOT NULL,
        ReviewedBy    INT               NOT NULL,
        ReviewAction  NVARCHAR(30)      NOT NULL,  -- Approved / Rejected
        Rating        INT               NULL,      -- 1-5 stars
        Remarks       NVARCHAR(1000)    NULL,
        ReviewedAt    DATETIME2         NOT NULL
                      CONSTRAINT DF_Trn_DSReview_ReviewedAt
                      DEFAULT (DATEADD(MINUTE, 330, SYSUTCDATETIME())),

        CONSTRAINT FK_Trn_DSReview_Ticket
            FOREIGN KEY (TicketId)
            REFERENCES dbo.DailySupport (Id),

        CONSTRAINT FK_Trn_DSReview_ReviewedBy
            FOREIGN KEY (ReviewedBy)
            REFERENCES dbo.LoginUsers (Id)
    );

    CREATE INDEX IX_Trn_DSReview_TicketId
        ON dbo.Trn_DailySupport_Review (TicketId, ReviewedAt);
END
GO


-- ============================================================
-- 7) VERIFY
-- ============================================================

SELECT N'Mst_Channel'     AS TableName, COUNT(*) AS Rows FROM dbo.Mst_Channel
UNION ALL SELECT N'Mst_CallType',   COUNT(*) FROM dbo.Mst_CallType
UNION ALL SELECT N'Mst_Category',   COUNT(*) FROM dbo.Mst_Category
UNION ALL SELECT N'Mst_SubCategory',COUNT(*) FROM dbo.Mst_SubCategory
UNION ALL SELECT N'Mst_Priority',   COUNT(*) FROM dbo.Mst_Priority
UNION ALL SELECT N'Mst_Status',     COUNT(*) FROM dbo.Mst_Status;
GO

PRINT 'Daily Support Ticketing System (Step 1) ready.';
GO
