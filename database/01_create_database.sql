-- Creates the GolfStatsApp database on (localdb)\MSSQLLocalDB (or any SQL Server instance).
-- Run this first, then 02_create_tables.sql.
IF DB_ID(N'GolfStatsApp') IS NULL
BEGIN
	CREATE DATABASE [GolfStatsApp];
END
GO

ALTER DATABASE [GolfStatsApp] SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE;
GO
