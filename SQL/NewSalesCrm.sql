-- =============================================
-- NEW CRM  -  SALES LEAD MANAGEMENT
-- SQL Server / SSMS
--
-- 4 nayi tables:
--   1) step_master
--   2) lead_master
--   3) lead_followup
--   4) lead_history
--
-- + lead_master migration (idempotent):
--   contact_no / email / city / state_id columns
--   priority values: Low / Med / High
--
-- Maujooda ClientMaster / LoginUsers tables ke
-- saath FK connection. Koi existing table
-- add/modify NAHI hota.
--
-- NOTE: target database ke andar run karein
-- (e.g. sqlcmd -d db68405 -i SQL/NewSalesCrm.sql).
-- Idempotent hai: sirf missing tables banati hai.
-- =============================================


-- =============================================
-- 1) STEP MASTER
-- =============================================

IF OBJECT_ID(N'dbo.step_master', N'U') IS NULL
BEGIN

    CREATE TABLE dbo.step_master
    (
        id          INT IDENTITY(1,1)   NOT NULL
                        CONSTRAINT PK_step_master
                        PRIMARY KEY,

        step_name   NVARCHAR(100)       NOT NULL,

        step_order  INT                 NOT NULL,

        is_active   BIT                 NOT NULL
                        CONSTRAINT DF_step_master_is_active
                        DEFAULT (1),

        CONSTRAINT UQ_step_master_step_order
            UNIQUE (step_order)
    );

    INSERT INTO dbo.step_master (step_name, step_order)
    VALUES
        ('New', 1),
        ('Demo Done', 2),
        ('Quotation Done', 3),
        ('Client Side Visit Done', 4),
        ('PO Received', 5);

END
GO

PRINT 'step_master ready.';
GO


-- =============================================
-- 2) LEAD MASTER
-- =============================================

IF OBJECT_ID(N'dbo.lead_master', N'U') IS NULL
BEGIN

    CREATE TABLE dbo.lead_master
    (
        id                  INT IDENTITY(1,1)   NOT NULL
                                CONSTRAINT PK_lead_master
                                PRIMARY KEY,

        lead_no             NVARCHAR(20)        NULL,

        client_id           INT                 NOT NULL,

        -- Lead ke apne contact details.
        contact_no          NVARCHAR(30)        NULL,

        email               NVARCHAR(200)       NULL,

        city                NVARCHAR(100)       NULL,

        state_id            INT                 NULL,

        requirement         NVARCHAR(500)       NOT NULL,

        source              NVARCHAR(100)       NOT NULL,

        expected_value      DECIMAL(18,2)       NOT NULL
                                CONSTRAINT DF_lead_master_expected_value
                                DEFAULT (0),

        priority            NVARCHAR(10)        NOT NULL
                                CONSTRAINT DF_lead_master_priority
                                DEFAULT ('Med'),

        assigned_user_id    INT                 NOT NULL,

        -- step_master(id) se direct FK: steps
        -- update hone par leads adjust hoti hain.
        current_step        INT                 NOT NULL
                                CONSTRAINT DF_lead_master_current_step
                                DEFAULT (1),

        status              NVARCHAR(10)        NOT NULL
                                CONSTRAINT DF_lead_master_status
                                DEFAULT ('Open'),

        lost_reason         NVARCHAR(250)       NULL,

        -- Won / Lost leads par force nahi karna.
        next_followup_date  DATE                NULL,

        next_remarks        NVARCHAR(1000)      NULL,

        is_active           BIT                 NOT NULL
                                CONSTRAINT DF_lead_master_is_active
                                DEFAULT (1),

        created_by          INT                 NOT NULL,

        created_at          DATETIME2           NOT NULL
                                CONSTRAINT DF_lead_master_created_at
                                DEFAULT (SYSDATETIME()),

        updated_at          DATETIME2           NULL,

        CONSTRAINT CK_lead_master_priority
            CHECK (priority IN ('Low', 'Med', 'High')),

        CONSTRAINT CK_lead_master_status
            CHECK (status IN ('Open', 'Won', 'Lost')),

        CONSTRAINT CK_lead_master_lost_reason
            CHECK (status <> 'Lost' OR lost_reason IS NOT NULL),

        CONSTRAINT FK_lead_master_client
            FOREIGN KEY (client_id)
            REFERENCES dbo.ClientMaster (Id),

        CONSTRAINT FK_lead_master_state
            FOREIGN KEY (state_id)
            REFERENCES dbo.States (Id),

        CONSTRAINT FK_lead_master_assigned_user
            FOREIGN KEY (assigned_user_id)
            REFERENCES dbo.LoginUsers (Id),

        CONSTRAINT FK_lead_master_current_step
            FOREIGN KEY (current_step)
            REFERENCES dbo.step_master (id),

        CONSTRAINT FK_lead_master_created_by
            FOREIGN KEY (created_by)
            REFERENCES dbo.LoginUsers (Id)
    );

END
GO

PRINT 'lead_master ready.';
GO


-- Auto lead number (LEAD-0001) ke liye unique index.
IF NOT EXISTS (
        SELECT 1
        FROM sys.indexes
        WHERE name = N'UX_lead_master_lead_no'
          AND object_id = OBJECT_ID(N'dbo.lead_master'))
BEGIN

    CREATE UNIQUE INDEX UX_lead_master_lead_no
        ON dbo.lead_master (lead_no);

END
GO

-- Overdue follow-up reports ke liye.
IF NOT EXISTS (
        SELECT 1
        FROM sys.indexes
        WHERE name = N'IX_lead_master_next_followup_date'
          AND object_id = OBJECT_ID(N'dbo.lead_master'))
BEGIN

    CREATE INDEX IX_lead_master_next_followup_date
        ON dbo.lead_master (next_followup_date)
        INCLUDE (status, client_id);

END
GO


-- =============================================
-- 3) LEAD FOLLOW-UP (log)
-- =============================================

IF OBJECT_ID(N'dbo.lead_followup', N'U') IS NULL
BEGIN

    CREATE TABLE dbo.lead_followup
    (
        id              INT IDENTITY(1,1)   NOT NULL
                            CONSTRAINT PK_lead_followup
                            PRIMARY KEY,

        lead_id         INT                 NOT NULL,

        followup_date   DATE                NOT NULL,

        remarks         NVARCHAR(1000)      NULL,

        created_by      INT                 NULL,

        created_at      DATETIME2           NOT NULL
                            CONSTRAINT DF_lead_followup_created_at
                            DEFAULT (SYSDATETIME()),

        CONSTRAINT FK_lead_followup_lead
            FOREIGN KEY (lead_id)
            REFERENCES dbo.lead_master (id),

        CONSTRAINT FK_lead_followup_created_by
            FOREIGN KEY (created_by)
            REFERENCES dbo.LoginUsers (Id)
    );

END
GO

PRINT 'lead_followup ready.';
GO


-- =============================================
-- 4) LEAD HISTORY (audit trail)
-- =============================================

IF OBJECT_ID(N'dbo.lead_history', N'U') IS NULL
BEGIN

    CREATE TABLE dbo.lead_history
    (
        id              INT IDENTITY(1,1)   NOT NULL
                            CONSTRAINT PK_lead_history
                            PRIMARY KEY,

        lead_id         INT                 NOT NULL,

        old_step        NVARCHAR(100)       NULL,

        new_step        NVARCHAR(100)       NULL,

        old_status      NVARCHAR(10)        NULL,

        new_status      NVARCHAR(10)        NULL,

        remarks         NVARCHAR(1000)      NULL,

        changed_by      INT                 NULL,

        changed_at      DATETIME2           NOT NULL
                            CONSTRAINT DF_lead_history_changed_at
                            DEFAULT (SYSDATETIME()),

        CONSTRAINT FK_lead_history_lead
            FOREIGN KEY (lead_id)
            REFERENCES dbo.lead_master (id),

        CONSTRAINT FK_lead_history_changed_by
            FOREIGN KEY (changed_by)
            REFERENCES dbo.LoginUsers (Id)
    );

END
GO

PRINT 'lead_history ready.';
GO


-- =============================================
-- 5) MIGRATION (existing databases ke liye)
--    - contact_no / email / city / state_id
--    - priority: Low / Med / High
-- =============================================

-- Naye contact columns (sirf missing add).
IF COL_LENGTH(N'dbo.lead_master', N'contact_no') IS NULL
    ALTER TABLE dbo.lead_master ADD contact_no NVARCHAR(30) NULL;
GO

IF COL_LENGTH(N'dbo.lead_master', N'email') IS NULL
    ALTER TABLE dbo.lead_master ADD email NVARCHAR(200) NULL;
GO

IF COL_LENGTH(N'dbo.lead_master', N'city') IS NULL
    ALTER TABLE dbo.lead_master ADD city NVARCHAR(100) NULL;
GO

IF COL_LENGTH(N'dbo.lead_master', N'state_id') IS NULL
    ALTER TABLE dbo.lead_master ADD state_id INT NULL;
GO

-- state_id -> States(Id) FK (sirf missing).
IF NOT EXISTS (
        SELECT 1
        FROM sys.foreign_keys
        WHERE name = N'FK_lead_master_state'
          AND parent_object_id = OBJECT_ID(N'dbo.lead_master'))
 AND OBJECT_ID(N'dbo.States', N'U') IS NOT NULL
BEGIN
    ALTER TABLE dbo.lead_master WITH CHECK
        ADD CONSTRAINT FK_lead_master_state
            FOREIGN KEY (state_id)
            REFERENCES dbo.States (Id);
END
GO

-- Purana priority data map karo:
-- Hot -> High, Warm -> Med, Cold -> Low.
UPDATE dbo.lead_master
SET priority = CASE priority
                    WHEN 'Hot' THEN 'High'
                    WHEN 'Warm' THEN 'Med'
                    WHEN 'Cold' THEN 'Low'
               END
WHERE priority IN ('Hot', 'Warm', 'Cold');
GO

-- Purana CHECK constraint (Hot/Warm/Cold) hatao.
IF EXISTS (
        SELECT 1
        FROM sys.check_constraints
        WHERE name = N'CK_lead_master_priority'
          AND parent_object_id = OBJECT_ID(N'dbo.lead_master')
          AND definition LIKE N'%Hot%')
BEGIN
    ALTER TABLE dbo.lead_master
        DROP CONSTRAINT CK_lead_master_priority;
END
GO

-- Naya CHECK constraint (Low/Med/High).
IF NOT EXISTS (
        SELECT 1
        FROM sys.check_constraints
        WHERE name = N'CK_lead_master_priority'
          AND parent_object_id = OBJECT_ID(N'dbo.lead_master'))
BEGIN
    ALTER TABLE dbo.lead_master WITH CHECK
        ADD CONSTRAINT CK_lead_master_priority
            CHECK (priority IN ('Low', 'Med', 'High'));
END
GO

-- Default priority: Warm -> Med.
DECLARE @df NVARCHAR(200) =
    (SELECT dc.name
     FROM sys.default_constraints dc
     WHERE dc.parent_object_id = OBJECT_ID(N'dbo.lead_master')
       AND dc.parent_column_id =
           (SELECT column_id
            FROM sys.columns
            WHERE object_id = OBJECT_ID(N'dbo.lead_master')
              AND name = N'priority'));

IF @df IS NOT NULL
   AND EXISTS (SELECT 1
               FROM sys.default_constraints
               WHERE name = @df
                 AND definition LIKE N'%Warm%')
BEGIN
    EXEC ('ALTER TABLE dbo.lead_master DROP CONSTRAINT [' + @df + ']');

    ALTER TABLE dbo.lead_master
        ADD CONSTRAINT DF_lead_master_priority
            DEFAULT ('Med') FOR priority;
END
GO

PRINT 'lead_master migration (contact / email / city / state / priority) ready.';
GO
