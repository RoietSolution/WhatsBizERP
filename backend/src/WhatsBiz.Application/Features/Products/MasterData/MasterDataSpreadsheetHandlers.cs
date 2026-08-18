using MediatR;
using WhatsBiz.Application.Common.Exceptions;
using WhatsBiz.Application.Common.Interfaces;
using WhatsBiz.Application.Features.Products.DTOs;
using WhatsBiz.Domain.Products;

namespace WhatsBiz.Application.Features.Products.MasterData;

public sealed class ProductMasterSpreadsheetHandlers(
    IProductRepository repository,
    IProductMasterSpreadsheetService spreadsheet,
    ICurrentUserService currentUser) :
    IRequestHandler<ExportProductCategoriesQuery, byte[]>, IRequestHandler<DownloadProductCategoryTemplateQuery, byte[]>, IRequestHandler<ImportProductCategoriesCommand, ImportProductMasterResult>,
    IRequestHandler<ExportBrandsQuery, byte[]>, IRequestHandler<DownloadBrandTemplateQuery, byte[]>, IRequestHandler<ImportBrandsCommand, ImportProductMasterResult>,
    IRequestHandler<ExportUnitsOfMeasureQuery, byte[]>, IRequestHandler<DownloadUnitOfMeasureTemplateQuery, byte[]>, IRequestHandler<ImportUnitsOfMeasureCommand, ImportProductMasterResult>
{
    public async Task<byte[]> Handle(ExportProductCategoriesQuery request, CancellationToken cancellationToken) => spreadsheet.ExportCategories(await repository.GetCategoriesAsync(cancellationToken));
    public Task<byte[]> Handle(DownloadProductCategoryTemplateQuery request, CancellationToken cancellationToken) => Task.FromResult(spreadsheet.CategoryTemplate());
    public async Task<byte[]> Handle(ExportBrandsQuery request, CancellationToken cancellationToken) => spreadsheet.ExportBrands(await repository.GetBrandsAsync(cancellationToken));
    public Task<byte[]> Handle(DownloadBrandTemplateQuery request, CancellationToken cancellationToken) => Task.FromResult(spreadsheet.BrandTemplate());
    public async Task<byte[]> Handle(ExportUnitsOfMeasureQuery request, CancellationToken cancellationToken) => spreadsheet.ExportUnits(await repository.GetUnitsAsync(cancellationToken));
    public Task<byte[]> Handle(DownloadUnitOfMeasureTemplateQuery request, CancellationToken cancellationToken) => Task.FromResult(spreadsheet.UnitTemplate());

    public async Task<ImportProductMasterResult> Handle(ImportProductCategoriesCommand request, CancellationToken cancellationToken)
    {
        var rows = Read(() => spreadsheet.ReadCategories(request.Content));
        var existing = (await repository.GetCategoriesAsync(cancellationToken)).ToDictionary(x => x.CategoryCode, StringComparer.OrdinalIgnoreCase);
        var candidates = new Dictionary<string, (CategoryImportRow Row, ProductCategory Entity)>(StringComparer.OrdinalIgnoreCase);
        var errors = new List<string>();
        foreach (var row in rows)
        {
            if (Invalid(row.Code, 50) || Invalid(row.Name, 200) || row.Description?.Length > 1000 || row.DisplayOrder < 0) { errors.Add($"Row {row.RowNumber}: category code, name, description, or display order is invalid."); continue; }
            if (existing.ContainsKey(row.Code) || !candidates.TryAdd(row.Code, (row, new ProductCategory { CategoryCode = row.Code.Trim(), CategoryName = row.Name.Trim(), Description = Trim(row.Description), DisplayOrder = row.DisplayOrder, IsActive = row.IsActive, CreatedBy = currentUser.Username }))) errors.Add($"Row {row.RowNumber}: category code already exists.");
        }
        var invalidParents = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in candidates.Values)
        {
            var parentCode = pair.Row.ParentCode?.Trim();
            if (string.IsNullOrWhiteSpace(parentCode)) continue;
            if (parentCode.Equals(pair.Row.Code, StringComparison.OrdinalIgnoreCase)) { errors.Add($"Row {pair.Row.RowNumber}: a category cannot be its own parent."); invalidParents.Add(pair.Row.Code); }
            else if (existing.TryGetValue(parentCode, out var existingParent)) pair.Entity.ParentCategoryId = existingParent.ProductCategoryId;
            else if (candidates.TryGetValue(parentCode, out var importedParent)) pair.Entity.ParentCategoryId = importedParent.Entity.ProductCategoryId;
            else { errors.Add($"Row {pair.Row.RowNumber}: parent category code '{parentCode}' was not found."); invalidParents.Add(pair.Row.Code); }
        }
        foreach (var start in candidates.Values.Where(x => !invalidParents.Contains(x.Row.Code)))
        {
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase); var code = start.Row.Code;
            while (candidates.TryGetValue(code, out var current) && !string.IsNullOrWhiteSpace(current.Row.ParentCode))
            {
                if (!visited.Add(code)) { foreach (var item in visited) invalidParents.Add(item); errors.Add($"Row {start.Row.RowNumber}: category parent hierarchy contains a cycle."); break; }
                code = current.Row.ParentCode!;
            }
        }
        bool changed;
        do
        {
            changed = false;
            foreach (var pair in candidates.Values.Where(x => !invalidParents.Contains(x.Row.Code) && !string.IsNullOrWhiteSpace(x.Row.ParentCode) && invalidParents.Contains(x.Row.ParentCode!)))
            { errors.Add($"Row {pair.Row.RowNumber}: parent category row is invalid."); changed |= invalidParents.Add(pair.Row.Code); }
        } while (changed);
        foreach (var pair in candidates.Values.Where(x => !invalidParents.Contains(x.Row.Code))) repository.Add(pair.Entity);
        var imported = candidates.Count - invalidParents.Count;
        if (imported > 0) await repository.SaveChangesAsync(cancellationToken);
        return new(imported, errors);
    }

    public async Task<ImportProductMasterResult> Handle(ImportBrandsCommand request, CancellationToken cancellationToken)
    {
        var rows = Read(() => spreadsheet.ReadBrands(request.Content)); var existing = (await repository.GetBrandsAsync(cancellationToken)).Select(x => x.BrandCode).ToHashSet(StringComparer.OrdinalIgnoreCase); var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase); var errors = new List<string>(); var imported = 0;
        foreach (var row in rows) { if (Invalid(row.Code, 50) || Invalid(row.Name, 200) || row.Description?.Length > 1000 || row.Logo?.Length > 500) { errors.Add($"Row {row.RowNumber}: brand data is invalid."); continue; } if (existing.Contains(row.Code) || !seen.Add(row.Code)) { errors.Add($"Row {row.RowNumber}: brand code already exists."); continue; } repository.Add(new Brand { BrandCode = row.Code.Trim(), BrandName = row.Name.Trim(), Description = Trim(row.Description), Logo = Trim(row.Logo), IsActive = row.IsActive, CreatedBy = currentUser.Username }); imported++; }
        if (imported > 0) await repository.SaveChangesAsync(cancellationToken); return new(imported, errors);
    }

    public async Task<ImportProductMasterResult> Handle(ImportUnitsOfMeasureCommand request, CancellationToken cancellationToken)
    {
        var rows = Read(() => spreadsheet.ReadUnits(request.Content)); var existing = (await repository.GetUnitsAsync(cancellationToken)).Select(x => x.UnitCode).ToHashSet(StringComparer.OrdinalIgnoreCase); var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase); var errors = new List<string>(); var imported = 0;
        foreach (var row in rows) { if (Invalid(row.Code, 50) || Invalid(row.Name, 200) || Invalid(row.ShortName, 20) || row.DecimalPlaces is < 0 or > 6) { errors.Add($"Row {row.RowNumber}: unit data or decimal places are invalid."); continue; } if (existing.Contains(row.Code) || !seen.Add(row.Code)) { errors.Add($"Row {row.RowNumber}: unit code already exists."); continue; } repository.Add(new UnitOfMeasure { UnitCode = row.Code.Trim(), UnitName = row.Name.Trim(), ShortName = row.ShortName.Trim(), DecimalPlaces = (byte)row.DecimalPlaces, IsActive = row.IsActive, CreatedBy = currentUser.Username }); imported++; }
        if (imported > 0) await repository.SaveChangesAsync(cancellationToken); return new(imported, errors);
    }

    private static IReadOnlyCollection<T> Read<T>(Func<IReadOnlyCollection<T>> read) { try { return read(); } catch (Exception exception) when (exception is not OperationCanceledException) { throw new BusinessRuleException($"The workbook could not be read: {exception.Message}"); } }
    private static bool Invalid(string value, int maximum) => string.IsNullOrWhiteSpace(value) || value.Trim().Length > maximum;
    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
