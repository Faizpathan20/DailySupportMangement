-- ============================================================
-- Daily Support — in-app notifications
--
-- Review workflow ke notifications:
--   * review    -> user ne review pe bheja  (admin ko)
--   * approved  -> admin ne accept kiya     (user ko)
--   * rejected  -> admin ne reject kiya     (user ko)
--   * assigned  -> ticket kisi ko assign hua (naye assignee ko)
--   * unassigned-> ticket reassign hua      (purane assignee ko)
-- ============================================================

IF OBJECT_ID('dbo.[Trn_Notification]', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.[Trn_Notification]
    (
        [Id]        int IDENTITY(1,1) NOT NULL,
        [UserId]    int NOT NULL,           -- kis user ko dikhani hai
        [TicketId]  int NULL,               -- kaunsa ticket
        [Title]     nvarchar(200) NOT NULL,
        [Body]      nvarchar(500) NULL,
        [Kind]      nvarchar(30)  NOT NULL,-- review | approved | rejected
        [Rating]    int NULL,
        [IsRead]    bit NOT NULL
            CONSTRAINT [DF_Trn_Notification_IsRead] DEFAULT (0),
        [CreatedAt] datetime2 NOT NULL
            CONSTRAINT [DF_Trn_Notification_CreatedAt] DEFAULT (SYSDATETIME()),

        CONSTRAINT [PK_Trn_Notification]
            PRIMARY KEY ([Id])
    );

    CREATE INDEX [IX_Trn_Notification_User]
        ON dbo.[Trn_Notification] ([UserId], [IsRead]);
END
GO
