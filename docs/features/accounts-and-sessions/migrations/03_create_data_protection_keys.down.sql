-- accounts-and-sessions · staged migration 03 of 03 · DOWN
-- Reverses 03_create_data_protection_keys.up.sql exactly.
--
-- Dropping this table destroys the key material that protects every issued session cookie. Every
-- browser holding a cookie is presented the sign-in form on its next request, because the reference
-- inside the cookie can no longer be decrypted — that is KPI 3 ("sessions surviving a redeploy")
-- going to zero. Reversible in development; in production this is the failure ADR 0009 exists to
-- prevent, so it must never be run there without saying so out loud.

IF OBJECT_ID(N'[dbo].[DataProtectionKeys]', N'U') IS NOT NULL
    DROP TABLE [dbo].[DataProtectionKeys];
