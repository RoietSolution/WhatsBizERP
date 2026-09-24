using MediatR;
using Microsoft.AspNetCore.Mvc;
using WhatsBiz.Api.Authorization;
using WhatsBiz.Application.Common.Capacity;
using WhatsBiz.Application.Common.Interfaces;
using WhatsBiz.Application.Features.Administration;
using WhatsBiz.SharedKernel;

namespace WhatsBiz.Api.Controllers;

[ApiController, Route("api/admin")]
public sealed class AdminController(ISender sender, ITenantResourceLimitService capacity, ICurrentUserService currentUser) : ControllerBase
{
    private string? CurrentUser => User.Identity?.Name;
    [HttpGet("company"), HasPermission(Permissions.Admin.View)] public Task<CompanyDto> Company(CancellationToken token) => sender.Send(new GetCompany(),token);
    [HttpPut("company"), HasPermission(Permissions.Admin.Company)] public Task<CompanyDto> Company(CompanyInput input,CancellationToken token)=>sender.Send(new UpdateCompany(input),token);
    [HttpGet("branches"), HasPermission(Permissions.Admin.View)] public Task<IReadOnlyCollection<BranchDto>> Branches(CancellationToken token)=>sender.Send(new GetBranches(),token);
    [HttpPost("branches"), HasPermission(Permissions.Admin.Settings)] public async Task<BranchDto> Branch(BranchInput input,CancellationToken token){if(input.IsActive)await capacity.EnsureCanCreateAsync(RequireTenant(),TenantResourceTypes.Branches,token);return await sender.Send(new CreateBranch(input),token);}
    [HttpPut("branches/{id:guid}"), HasPermission(Permissions.Admin.Settings)] public async Task<BranchDto> Branch(Guid id,BranchInput input,CancellationToken token){var existing=(await sender.Send(new GetBranches(),token)).SingleOrDefault(x=>x.BranchId==id)??throw new KeyNotFoundException("Branch was not found.");if(!existing.IsActive&&input.IsActive)await capacity.EnsureCanCreateAsync(RequireTenant(),TenantResourceTypes.Branches,token);return await sender.Send(new UpdateBranch(id,input),token);}
    [HttpGet("settings"), HasPermission(Permissions.Admin.View)] public Task<IReadOnlyCollection<SettingDto>> Settings(CancellationToken token)=>sender.Send(new GetSettings(),token);
    [HttpPut("settings"), HasPermission(Permissions.Admin.Settings)] public async Task<IActionResult> Settings(IReadOnlyCollection<SettingInput> input,CancellationToken token){await sender.Send(new UpdateSettings(input,CurrentUser),token);return NoContent();}
    [HttpGet("financial-years"), HasPermission(Permissions.Admin.View)] public Task<IReadOnlyCollection<FinancialYearDto>> FinancialYears(CancellationToken token)=>sender.Send(new GetFinancialYears(),token);
    [HttpPost("financial-years"), HasPermission(Permissions.Admin.Settings)] public async Task<IActionResult> FinancialYear(FinancialYearInput input,CancellationToken token){await sender.Send(new SaveFinancialYear(input),token);return NoContent();}
    [HttpGet("backup"), PlatformAuthorize] public Task<IReadOnlyCollection<BackupDto>> Backups(CancellationToken token)=>sender.Send(new GetBackups(),token);
    [HttpPost("backup"), PlatformAuthorize] public Task<BackupDto> Backup(CancellationToken token)=>sender.Send(new CreateBackup(CurrentUser),token);
    [HttpPost("restore"), PlatformAuthorize] public Task<RestoreResultDto> Restore(RestoreInput input,CancellationToken token)=>sender.Send(new RestoreDatabase(input,CurrentUser),token);
    [HttpGet("audit"), PlatformAuthorize] public Task<IReadOnlyCollection<AuditDto>> Audit(DateTimeOffset? from,DateTimeOffset? to,string? action,int take=200,CancellationToken token=default)=>sender.Send(new GetAudit(from,to,action,take),token);
    [HttpGet("login-history"), PlatformAuthorize] public Task<IReadOnlyCollection<LoginDto>> Logins(DateTimeOffset? from,DateTimeOffset? to,int take=200,CancellationToken token=default)=>sender.Send(new GetLoginHistory(from,to,take),token);
    private Guid RequireTenant()=>currentUser.TenantId??throw new UnauthorizedAccessException("A tenant context is required.");
}
