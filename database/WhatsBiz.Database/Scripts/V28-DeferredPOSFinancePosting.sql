/*
  Reapply the deterministic tenant-scoped POS definition with deferred finance
  posting for HELD/SUSPENDED invoices. This is safe and idempotent because the
  referenced script uses CREATE OR ALTER PROCEDURE and performs no data writes.

  Run this script from the Scripts directory when applying it with sqlcmd.
*/
:r .\V18-POS-PostInvoice-TenantHardening.sql
