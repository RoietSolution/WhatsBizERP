/* RC-DEV-009: constrain persisted print paper sizes and default to 80MM. */
IF OBJECT_ID(N'printing.PrinterConfigurations', N'U') IS NOT NULL
BEGIN
    UPDATE printing.PrinterConfigurations
       SET PaperSize = CASE UPPER(LTRIM(RTRIM(PaperSize)))
                         WHEN N'58MM' THEN N'58MM'
                         WHEN N'A4' THEN N'A4'
                         ELSE N'80MM'
                       END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.default_constraints d
        JOIN sys.columns c ON c.object_id=d.parent_object_id AND c.column_id=d.parent_column_id
        WHERE d.parent_object_id=OBJECT_ID(N'printing.PrinterConfigurations') AND c.name=N'PaperSize'
    )
        ALTER TABLE printing.PrinterConfigurations
          ADD CONSTRAINT DF_PrinterConfigurations_PaperSize DEFAULT N'80MM' FOR PaperSize;

    IF OBJECT_ID(N'printing.CK_PrinterConfigurations_PaperSize', N'C') IS NULL
        ALTER TABLE printing.PrinterConfigurations WITH CHECK
          ADD CONSTRAINT CK_PrinterConfigurations_PaperSize CHECK (PaperSize IN (N'58MM',N'80MM',N'A4'));

    ;WITH Defaults AS
    (
        SELECT PrinterConfigurationId,
               ROW_NUMBER() OVER (ORDER BY CreatedOn DESC,PrinterConfigurationId) AS Position
        FROM printing.PrinterConfigurations
        WHERE IsDefault=1
    )
    UPDATE p SET IsDefault=0
    FROM printing.PrinterConfigurations p
    JOIN Defaults d ON d.PrinterConfigurationId=p.PrinterConfigurationId
    WHERE d.Position>1;

    IF NOT EXISTS
       (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'printing.PrinterConfigurations') AND name=N'UX_PrinterConfigurations_Default')
        CREATE UNIQUE INDEX UX_PrinterConfigurations_Default
            ON printing.PrinterConfigurations(IsDefault)
            WHERE IsDefault=1;
END;
