/* DEVELOPMENT/QA ONLY. Never include from PostDeployment.sql.
   Replace the IDs with a non-production tenant, purchased products and demo customers before running manually. */
DECLARE @Ratings table(Rating int, ReviewText nvarchar(1000));
INSERT @Ratings VALUES
(5,N'Excellent quality and neatly packed.'),(5,N'Fresh product and quick delivery.'),(4,N'Good value for the pack size.'),
(4,N'Product matched the description.'),(4,N'Would order this again.'),(3,N'Good overall, packaging can improve.');
SELECT Rating,ReviewText FROM @Ratings ORDER BY Rating DESC,ReviewText; -- deterministic preview only; intentionally performs no inserts.