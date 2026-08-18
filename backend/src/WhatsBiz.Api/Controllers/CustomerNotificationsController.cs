using Microsoft.AspNetCore.Mvc;
using WhatsBiz.Api.Authorization;
using WhatsBiz.Application.Common.Interfaces;
using WhatsBiz.Application.Features.CustomerNotifications;
using WhatsBiz.SharedKernel;

namespace WhatsBiz.Api.Controllers;

[ApiController, Route("api/admin/customer-notifications")]
public sealed class CustomerNotificationsController(ICustomerNotificationService service) : ControllerBase
{
    [HttpGet("settings"), HasPermission(Permissions.Admin.View)]
    public Task<CustomerNotificationSettingsDto> Settings(CancellationToken token) => service.GetSettings(token);

    [HttpPut("settings"), HasPermission(Permissions.Admin.Settings)]
    public async Task<IActionResult> Settings(CustomerNotificationSettingsInput input, CancellationToken token)
    { await service.SaveSettings(input, User.Identity?.Name, token); return NoContent(); }

    [HttpGet("history"), HasPermission(Permissions.Admin.View)]
    public Task<IReadOnlyCollection<CustomerNotificationHistoryDto>> History([FromQuery] int take = 200, CancellationToken token = default) => service.History(take, token);

    [HttpPost("history/{id:guid}/retry"), HasPermission(Permissions.Admin.Settings)]
    public async Task<IActionResult> Retry(Guid id, CancellationToken token)
    { await service.Retry(id, User.Identity?.Name, token); return NoContent(); }

    [HttpGet("configuration-status"), HasPermission(Permissions.Admin.Settings)]
    public Task<NotificationConfigurationStatusDto> ConfigurationStatus(CancellationToken token) => service.ConfigurationStatus(token);
}
