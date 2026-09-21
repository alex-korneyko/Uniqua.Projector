-- accounts-and-sessions · staged migration 03 of 03 · UP
-- The data-protection key ring of ADR 0009, kept in the database so that 100% of unexpired sessions
-- survive a redeploy (spec §6 and KPI 3). Without this table the framework keeps its key ring in a
-- per-process folder, and the first container replacement would silently end every session.
--
-- THE SHAPE OF THIS TABLE IS NOT OURS TO CHOOSE. Microsoft.AspNetCore.DataProtection.EntityFrameworkCore
-- reads and writes exactly these three columns, which is why the primary key is an int IDENTITY rather
-- than the Guid.CreateVersion7() the rest of this schema uses. That divergence is deliberate and is
-- recorded in the audit report.
--
-- STAGED, NOT LIVE. The live migration is `dotnet ef migrations add CreateDataProtectionKeys` once the
-- DbContext implements IDataProtectionKeyContext. See data-model.md § Promotion.

IF OBJECT_ID(N'[dbo].[DataProtectionKeys]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[DataProtectionKeys] (
        [Id]           int           NOT NULL IDENTITY,
        [FriendlyName] nvarchar(max) NULL,
        -- Key material. At rest this is enough to mint a session for any account (spec §6.1), so the
        -- database must be readable only by the identity the application runs as.
        [Xml]          nvarchar(max) NULL,
        CONSTRAINT [PK_DataProtectionKeys] PRIMARY KEY ([Id])
    );
END;

-- No index beyond the primary key, and none is justified: the framework reads the whole table at
-- startup and on key-ring refresh, and it holds single-digit rows.
