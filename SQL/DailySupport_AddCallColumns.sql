USE [Db_Faiz];
GO

-- =============================================
-- DailySupport: reference-style "Add Call" form
-- ke liye naye columns.
-- Purane rows safe: sab naye columns NULL hain.
-- =============================================

IF COL_LENGTH(N'dbo.DailySupport', N'TicketNo') IS NULL
    ALTER TABLE dbo.DailySupport ADD TicketNo NVARCHAR(30) NULL;
GO

IF COL_LENGTH(N'dbo.DailySupport', N'Mobile') IS NULL
    ALTER TABLE dbo.DailySupport ADD Mobile NVARCHAR(20) NULL;
GO

IF COL_LENGTH(N'dbo.DailySupport', N'CallerName') IS NULL
    ALTER TABLE dbo.DailySupport ADD CallerName NVARCHAR(150) NULL;
GO

IF COL_LENGTH(N'dbo.DailySupport', N'Channel') IS NULL
    ALTER TABLE dbo.DailySupport ADD Channel NVARCHAR(50) NULL;
GO

IF COL_LENGTH(N'dbo.DailySupport', N'CallType') IS NULL
    ALTER TABLE dbo.DailySupport ADD CallType NVARCHAR(100) NULL;
GO

IF COL_LENGTH(N'dbo.DailySupport', N'Category') IS NULL
    ALTER TABLE dbo.DailySupport ADD Category NVARCHAR(100) NULL;
GO

-- Duplicate ticket number nahi hona chahiye
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = 'UX_DailySupport_TicketNo'
      AND object_id = OBJECT_ID(N'dbo.DailySupport')
)
BEGIN
    SET QUOTED_IDENTIFIER ON;

    CREATE UNIQUE INDEX UX_DailySupport_TicketNo
        ON dbo.DailySupport (TicketNo)
        WHERE TicketNo IS NOT NULL;
END
GO

PRINT 'DailySupport call columns ready.';
GO
