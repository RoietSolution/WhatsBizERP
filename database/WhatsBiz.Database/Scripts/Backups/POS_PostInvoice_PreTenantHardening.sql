/* Source backup captured before deterministic tenant hardening.
   The live Development definition was inspected from OBJECT_DEFINITION(OBJECT_ID('sales.POS_PostInvoice')).
   The repository baseline is retained in RCDEV008-RuntimeObjects.sql immediately before POS_ReturnInvoice.
   This file intentionally contains a replayable metadata query rather than executing any change. */
SELECT OBJECT_DEFINITION(OBJECT_ID(N'sales.POS_PostInvoice')) AS POS_PostInvoiceDefinition;
