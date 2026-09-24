-- boards-columns-cards · 01 create boards — rollback (STAGED)
-- Drops the five tables in reverse FK order; their indexes and constraints go with them.
-- Destroys every board, column, card and membership: a rollback after real use loses that data.

SET XACT_ABORT ON;
BEGIN TRANSACTION;

DROP TABLE IF EXISTS [dbo].[OwnedBoardCounters];
DROP TABLE IF EXISTS [dbo].[BoardMemberships];
DROP TABLE IF EXISTS [dbo].[Cards];
DROP TABLE IF EXISTS [dbo].[Columns];
DROP TABLE IF EXISTS [dbo].[Boards];

COMMIT TRANSACTION;
