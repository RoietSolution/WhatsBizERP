using Microsoft.Data.SqlClient;
using WhatsBiz.Application.Features.Administration;

namespace WhatsBiz.Infrastructure.Persistence;

public sealed partial class AdminRepository
{
    public async Task<BranchDto> UpdateBranch(Guid id, BranchInput x, CancellationToken t)
    {
        await using var connection = Connection();
        await connection.OpenAsync(t);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(t);
        if (x.IsDefault)
        {
            await using var clear = new SqlCommand("UPDATE b SET IsDefault=0 FROM admin.Branches b JOIN admin.Companies c ON c.CompanyId=b.CompanyId WHERE c.TenantId=@tenant AND b.BranchId<>@id",connection,transaction);
            Add(clear,"@tenant",Tenant); Add(clear,"@id",id); await clear.ExecuteNonQueryAsync(t);
        }
        await using var command = new SqlCommand("""
            UPDATE b SET BranchCode=@code,BranchName=@name,DefaultWarehouseId=@warehouse,Email=@email,Phone=@phone,
              Address=@address,City=@city,State=@state,PostalCode=@postal,IsDefault=@default,IsActive=@active
            FROM admin.Branches b JOIN admin.Companies c ON c.CompanyId=b.CompanyId
            WHERE b.BranchId=@id AND c.TenantId=@tenant
            """,connection,transaction);
        Add(command,"@tenant",Tenant); Add(command,"@id",id); Add(command,"@code",x.BranchCode); Add(command,"@name",x.BranchName);
        Add(command,"@warehouse",x.DefaultWarehouseId); Add(command,"@email",x.Email); Add(command,"@phone",x.Phone);
        Add(command,"@address",x.Address); Add(command,"@city",x.City); Add(command,"@state",x.State); Add(command,"@postal",x.PostalCode);
        Add(command,"@default",x.IsDefault); Add(command,"@active",x.IsActive);
        if(await command.ExecuteNonQueryAsync(t)==0) throw new KeyNotFoundException("Branch was not found.");
        await transaction.CommitAsync(t);
        return (await Branches(t)).Single(branch=>branch.BranchId==id);
    }
}
