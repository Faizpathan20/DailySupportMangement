-- =============================================
-- NEW CRM  -  LEAD TIME LOG (Meeting In / Out)
-- SQL Server / SSMS
--
-- 1 nayi table:
--   1) lead_time_log
--
-- Meeting ka time track karne ke liye:
--   meeting_in  -> meeting kab shuru hui
--   meeting_out -> meeting kab khatam hui (NULL = LIVE)
--   duration_min-> auto calculate (out - in)
--
-- Idempotent hai: sirf missing table/index banati hai.
-- NOTE: target database ke andar run karein
--   (e.g. sqlcmd -d db68405 -i SQL/LeadTimeLog.sql)
-- =============================================


-- =============================================
-- 1) LEAD TIME LOG TABLE
-- =============================================

IF OBJECT_ID(N'dbo.lead_time_log', N'U') IS NULL
BEGIN

    CREATE TABLE dbo.lead_time_log
    (
        id              INT IDENTITY(1,1)   NOT NULL
                                CONSTRAINT PK_lead_time_log
                                PRIMARY KEY,

        lead_id         INT                 NOT NULL,

        -- Jo user meeting kar raha hai.
        user_id         INT                 NOT NULL,

        -- Call / Meeting / Visit
        meeting_type    NVARCHAR(20)        NOT NULL
                                CONSTRAINT DF_lead_time_log_meeting_type
                                DEFAULT ('Meeting'),

        -- Meeting shui (server time).
        meeting_in      DATETIME2           NOT NULL,

        -- Meeting khatam. NULL = abhi LIVE hai.
        meeting_out     DATETIME2           NULL,

        -- Auto: DATEDIFF(MINUTE, in, out). NULL = LIVE.
        duration_min    INT                 NULL,

        notes           NVARCHAR(1000)      NULL,

        created_at      DATETIME2           NOT NULL
                                CONSTRAINT DF_lead_time_log_created_at
                                DEFAULT (SYSDATETIME()),

        -- Live timer ek user par sirf ek ho.
        CONSTRAINT FK_lead_time_log_lead
            FOREIGN KEY (lead_id)
            REFERENCES dbo.lead_master (id),

        CONSTRAINT FK_lead_time_log_user
            FOREIGN KEY (user_id)
            REFERENCES dbo.LoginUsers (Id)
    );

END
GO

PRINT 'lead_time_log ready.';
GO


-- Har user ka LIVE timer unique rahe
-- (meeting_out IS NULL par filtered unique index).
IF NOT EXISTS (
        SELECT 1
        FROM sys.indexes
        WHERE name = N'UX_lead_time_log_user_live'
          AND object_id = OBJECT_ID(N'dbo.lead_time_log'))
BEGIN

    CREATE UNIQUE INDEX UX_lead_time_log_user_live
        ON dbo.lead_time_log (user_id)
        WHERE meeting_out IS NULL;

END
GO


-- Reports: date-wise filter.
IF NOT EXISTS (
        SELECT 1
        FROM sys.indexes
        WHERE name = N'IX_lead_time_log_meeting_in'
          AND object_id = OBJECT_ID(N'dbo.lead_time_log'))
BEGIN

    CREATE INDEX IX_lead_time_log_meeting_in
        ON dbo.lead_time_log (meeting_in)
        INCLUDE (lead_id, user_id, meeting_out,
                 duration_min, meeting_type);

END
GO


-- Reports: lead-wise filter.
IF NOT EXISTS (
        SELECT 1
        FROM sys.indexes
        WHERE name = N'IX_lead_time_log_lead_id'
          AND object_id = OBJECT_ID(N'dbo.lead_time_log'))
BEGIN

    CREATE INDEX IX_lead_time_log_lead_id
        ON dbo.lead_time_log (lead_id);

END
GO


-- Reports: user-wise filter.
IF NOT EXISTS (
        SELECT 1
        FROM sys.indexes
        WHERE name = N'IX_lead_time_log_user_id'
          AND object_id = OBJECT_ID(N'dbo.lead_time_log'))
BEGIN

    CREATE INDEX IX_lead_time_log_user_id
        ON dbo.lead_time_log (user_id);

END
GO

PRINT 'lead_time_log indexes ready.';
GO
