-- =============================================
-- NEW CRM  -  PHASE 1 UPGRADE
-- SQL Server / S
--
-- 1) lead_notes   (nayi table - quick team notes)
-- 2) lead_followup mein naye columns:
--      status   (Pending / Done)
--      done_on
--      done_by
--
-- Idempotent hai: sirf missing table/columns
-- banati hai.
-- NOTE: target database ke andar run karein
--   (e.g. sqlcmd -d db68405 -i SQL/Phase1CrmUpgrade.sql)
-- =============================================


-- =============================================
-- 1) LEAD NOTES (quick team notes)
-- =============================================

IF OBJECT_ID(N'dbo.lead_notes', N'U') IS NULL
BEGIN

    CREATE TABLE dbo.lead_notes
    (
        id          INT IDENTITY(1,1)   NOT NULL
                            CONSTRAINT PK_lead_notes
                            PRIMARY KEY,

        lead_id     INT                 NOT NULL,

        -- Jo user note likh raha hai.
        user_id     INT                 NOT NULL,

        note_text   NVARCHAR(2000)      NOT NULL,

        created_at  DATETIME2           NOT NULL
                            CONSTRAINT DF_lead_notes_created_at
                            DEFAULT (SYSDATETIME()),

        CONSTRAINT FK_lead_notes_lead
            FOREIGN KEY (lead_id)
            REFERENCES dbo.lead_master (id),

        CONSTRAINT FK_lead_notes_user
            FOREIGN KEY (user_id)
            REFERENCES dbo.LoginUsers (Id)
    );

END
GO

PRINT 'lead_notes ready.';
GO


IF NOT EXISTS (
        SELECT 1
        FROM sys.indexes
        WHERE name = N'IX_lead_notes_lead_id'
          AND object_id = OBJECT_ID(N'dbo.lead_notes'))
BEGIN

    CREATE INDEX IX_lead_notes_lead_id
        ON dbo.lead_notes (lead_id)
        INCLUDE (user_id, created_at);

END
GO

PRINT 'lead_notes index ready.';
GO


-- =============================================
-- 2) LEAD_FOLLOWUP: status / done_on / done_by
-- (Follow-up page: Mark Done / Reschedule)
-- =============================================

IF COL_LENGTH(N'dbo.lead_followup', 'status') IS NULL
BEGIN

    ALTER TABLE dbo.lead_followup
        ADD status      NVARCHAR(10)    NOT NULL
                            CONSTRAINT DF_lead_followup_status
                            DEFAULT ('Pending'),

            done_on     DATETIME2       NULL,

            done_by     INT             NULL;

END
GO

PRINT 'lead_followup upgrade ready.';
GO
