-- =====================================================
-- DailySupport: status values aligned with the
-- reference Daily Support page.
--
--   Old  ->  New
--   Completed  ->  Closed
--   Cancelled  ->  On Hold
--
-- New allowed set:
--   Open, In Progress, On Hold, Pending, Closed
--
-- Safe to run more than once (no rows match twice).
-- =====================================================

USE [Db_Faiz];
GO

UPDATE [dbo].[DailySupport]
SET [Status] = 'Closed'
WHERE [Status] = 'Completed';

UPDATE [dbo].[DailySupport]
SET [Status] = 'On Hold'
WHERE [Status] = 'Cancelled';

-- Verify
SELECT [Status], COUNT(*) AS Cnt
FROM [dbo].[DailySupport]
GROUP BY [Status]
ORDER BY [Status];
GO
