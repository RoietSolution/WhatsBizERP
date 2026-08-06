using Microsoft.Data.SqlClient;using Microsoft.Extensions.Configuration;using WhatsBiz.Application.Common.Interfaces;using WhatsBiz.Application.Features.Administration;
namespace WhatsBiz.Infrastructure.Administration;
public sealed class DatabaseMaintenanceService(IConfiguration configuration):IDatabaseMaintenanceService
{
    private readonly string connection=configuration.GetConnectionString("DefaultConnection")!;
    public async Task<BackupDto> Backup(string? user,CancellationToken token)
    {
        var id=Guid.NewGuid();await using var c=new SqlConnection(connection);await c.OpenAsync(token);
        var folder=(string?)await new SqlCommand("SELECT CAST(SERVERPROPERTY('InstanceDefaultBackupPath') AS nvarchar(1000))",c).ExecuteScalarAsync(token)??throw new InvalidOperationException("SQL Server backup path is unavailable.");
        var name=$"WhatsBizERP_{DateTime.UtcNow:yyyyMMdd_HHmmss}.bak";var path=Path.GetFullPath(Path.Combine(folder,name));if(!path.StartsWith(Path.GetFullPath(folder),StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Invalid backup path.");
        await using(var insert=new SqlCommand("INSERT admin.BackupHistory(BackupHistoryId,FileName,FilePath,StartedOn,Status,CreatedBy) VALUES(@id,@name,@path,SYSDATETIMEOFFSET(),'RUNNING',@user)",c)){insert.Parameters.AddWithValue("@id",id);insert.Parameters.AddWithValue("@name",name);insert.Parameters.AddWithValue("@path",path);insert.Parameters.AddWithValue("@user",(object?)user??DBNull.Value);await insert.ExecuteNonQueryAsync(token);}
        try
        {
            await using(var backup=new SqlCommand("BACKUP DATABASE [WhatsBizERP] TO DISK=@path WITH COPY_ONLY,COMPRESSION,CHECKSUM,INIT,STATS=10",c)){backup.CommandTimeout=0;backup.Parameters.AddWithValue("@path",path);await backup.ExecuteNonQueryAsync(token);}
            await using(var verify=new SqlCommand("RESTORE VERIFYONLY FROM DISK=@path WITH CHECKSUM",c)){verify.CommandTimeout=0;verify.Parameters.AddWithValue("@path",path);await verify.ExecuteNonQueryAsync(token);}
            await using var sizeQuery=new SqlCommand("SELECT TOP 1 CAST(compressed_backup_size AS bigint) FROM msdb.dbo.backupset WHERE database_name='WhatsBizERP' AND is_copy_only=1 ORDER BY backup_finish_date DESC",c);var size=Convert.ToInt64(await sizeQuery.ExecuteScalarAsync(token),System.Globalization.CultureInfo.InvariantCulture);
            await using var done=new SqlCommand("UPDATE admin.BackupHistory SET CompletedOn=SYSDATETIMEOFFSET(),FileSizeBytes=@size,Status='COMPLETED',IsVerified=1 WHERE BackupHistoryId=@id",c);done.Parameters.AddWithValue("@size",size);done.Parameters.AddWithValue("@id",id);await done.ExecuteNonQueryAsync(token);return new(id,name,path,"FULL",DateTimeOffset.UtcNow,DateTimeOffset.UtcNow,size,"COMPLETED",true,null);
        }
        catch(Exception ex){await using var fail=new SqlCommand("UPDATE admin.BackupHistory SET CompletedOn=SYSDATETIMEOFFSET(),Status='FAILED',ErrorMessage=@error WHERE BackupHistoryId=@id",c);fail.Parameters.AddWithValue("@error",ex.Message);fail.Parameters.AddWithValue("@id",id);await fail.ExecuteNonQueryAsync(token);throw;}
    }
    public async Task<RestoreResultDto> Restore(RestoreInput input,string? user,CancellationToken token)
    {
        await using var c=new SqlConnection(connection);await c.OpenAsync(token);await using var find=new SqlCommand("SELECT FilePath FROM admin.BackupHistory WHERE BackupHistoryId=@id AND Status='COMPLETED' AND IsVerified=1",c);find.Parameters.AddWithValue("@id",input.BackupId);var path=(string?)await find.ExecuteScalarAsync(token)??throw new InvalidOperationException("Verified backup not found.");var id=Guid.NewGuid();
        await new SqlCommand("INSERT admin.RestoreHistory(RestoreHistoryId,BackupHistoryId,FilePath,StartedOn,Status,IsValidationOnly,CreatedBy) VALUES(@rid,@bid,@path,SYSDATETIMEOFFSET(),'RUNNING',@validation,@user)",c){Parameters={new("@rid",id),new("@bid",input.BackupId),new("@path",path),new("@validation",input.ValidationOnly),new("@user",(object?)user??DBNull.Value)}}.ExecuteNonQueryAsync(token);
        await using(var verify=new SqlCommand("RESTORE VERIFYONLY FROM DISK=@path WITH CHECKSUM",c)){verify.CommandTimeout=0;verify.Parameters.AddWithValue("@path",path);await verify.ExecuteNonQueryAsync(token);}
        if(!input.ValidationOnly){if(!input.Confirm)throw new InvalidOperationException("Restore confirmation is required.");throw new InvalidOperationException("Online self-restore is blocked. Stop the API and use the verified backup from the maintenance console.");}
        await new SqlCommand("UPDATE admin.RestoreHistory SET CompletedOn=SYSDATETIMEOFFSET(),Status='VALIDATED' WHERE RestoreHistoryId=@id",c){Parameters={new("@id",id)}}.ExecuteNonQueryAsync(token);return new(id,"VALIDATED",true,"Backup integrity and restore headers verified successfully.");
    }
}
