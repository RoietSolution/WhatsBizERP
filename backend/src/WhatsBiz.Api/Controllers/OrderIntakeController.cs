using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WhatsBiz.Api.Authorization;
using WhatsBiz.Application.Common.Features;
using WhatsBiz.Application.Common.Interfaces;
using WhatsBiz.Application.Features.OrderIntake;
using WhatsBiz.SharedKernel;

namespace WhatsBiz.Api.Controllers;

[ApiController, Authorize, RequireFeature(FeatureKeys.MultiChannelOrderIntake), Route("api/order-intake")]
public sealed class OrderIntakeController(IOrderIntakeService intake, ICurrentUserService current) : ControllerBase
{
    private Guid TenantId() => current.TenantId ?? throw new UnauthorizedAccessException("A retailer tenant context is required.");
    private bool CanSeeMobile => current.Permissions.Contains(Permissions.Customer.View, StringComparer.Ordinal);
    private async Task<bool> CustomerDataAllowedForDelivery(Guid id, CancellationToken token)
    {
        if (CanSeeMobile) return true;
        var request = await intake.GetAsync(TenantId(), id, false, token);
        return !string.Equals(request.FulfilmentPreference, "DELIVERY", StringComparison.OrdinalIgnoreCase);
    }

    [HttpGet, HasPermission(Permissions.POS.View)]
    public async Task<IActionResult> List([FromQuery] string? search, [FromQuery] string? channel, [FromQuery] string? status,
        [FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25, CancellationToken token = default)
    {
        try { return Ok(await intake.ListAsync(TenantId(), search, channel, status, from, to, page, pageSize, CanSeeMobile, token)); }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpGet("{id:guid}"), HasPermission(Permissions.POS.View)]
    public Task<OrderIntakeDetail> Get(Guid id, CancellationToken token) => intake.GetAsync(TenantId(), id, CanSeeMobile, token);

    [HttpPost, HasPermission(Permissions.POS.Create)]
    public async Task<IActionResult> Create(OrderIntakeCreateInput input, CancellationToken token)
    {
        if (!CanSeeMobile && (string.Equals(input.FulfilmentPreference, "DELIVERY", StringComparison.OrdinalIgnoreCase)
            || string.Equals(input.FulfilmentPreference, "HOME_DELIVERY", StringComparison.OrdinalIgnoreCase))) return Forbid();
        try { var request = await intake.CreateAsync(TenantId(), input, current.Username, token); return CreatedAtAction(nameof(Get), new { id = request.Id }, request); }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpGet("{id:guid}/saved-addresses"), HasPermission(Permissions.POS.View)]
    public async Task<IActionResult> GetSavedAddresses(Guid id, CancellationToken token)
    {
        if (!CanSeeMobile) return Forbid();
        return Ok(await intake.GetLinkedCustomerAddressesAsync(TenantId(), id, token));
    }

    [HttpGet("customers/search"), HasPermission(Permissions.POS.View)]
    public async Task<IActionResult> SearchCustomers([FromQuery] string query, CancellationToken token)
    {
        if (!CanSeeMobile) return Forbid();
        try { return Ok(await intake.SearchCustomersAsync(TenantId(), query, token)); }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpPut("{id:guid}/verified-customer"), HasPermission(Permissions.POS.Edit)]
    public async Task<IActionResult> LinkVerifiedCustomer(Guid id, OrderIntakeCustomerLinkInput input, CancellationToken token)
    {
        if (!CanSeeMobile) return Forbid();
        try { return Ok(await intake.LinkVerifiedCustomerAsync(TenantId(), id, input, current.Username, true, token)); }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
        catch (System.Data.DBConcurrencyException ex) { return Conflict(new { message = ex.Message }); }
        catch (InvalidOperationException ex) { return Conflict(new { message = ex.Message }); }
    }

    [HttpPut("{id:guid}/delivery-details"), HasPermission(Permissions.POS.Edit)]
    public async Task<IActionResult> SaveDeliveryDetails(Guid id, OrderIntakeDeliveryDetailsInput input, CancellationToken token)
    {
        if (!CanSeeMobile && (string.Equals(input.Details.FulfilmentPreference, "DELIVERY", StringComparison.OrdinalIgnoreCase)
            || string.Equals(input.Details.FulfilmentPreference, "HOME_DELIVERY", StringComparison.OrdinalIgnoreCase))) return Forbid();
        try { return Ok(await intake.SaveDeliveryDetailsAsync(TenantId(), id, input, current.Username, CanSeeMobile, token)); }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
        catch (System.Data.DBConcurrencyException ex) { return Conflict(new { message = ex.Message }); }
        catch (InvalidOperationException ex) { return Conflict(new { message = ex.Message }); }
    }

    [HttpPost("{id:guid}/assign-to-me"), HasPermission(Permissions.POS.Edit)]
    public async Task<IActionResult> AssignToMe(Guid id, [FromBody] byte[]? version, CancellationToken token)
    {
        if (current.UserId is not Guid staffId) return Forbid();
        try { return Ok(await intake.AssignToSelfAsync(TenantId(), id, staffId, version, current.Username, token)); }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
        catch (System.Data.DBConcurrencyException ex) { return Conflict(new { message = ex.Message }); }
    }

    [HttpGet("matches"), HasPermission(Permissions.POS.View)]
    public async Task<IActionResult> Matches([FromQuery] string query, CancellationToken token)
    {
        try { return Ok(await intake.FindMatchesAsync(TenantId(), query, token)); }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpPut("{id:guid}/items/{itemId:guid}"), HasPermission(Permissions.POS.Edit)]
    public async Task<IActionResult> UpdateItem(Guid id, Guid itemId, OrderIntakeItemUpdateInput input, CancellationToken token)
    {
        try { return Ok(await intake.UpdateItemAsync(TenantId(), id, itemId, input, current.Username, CanSeeMobile, token)); }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
        catch (System.Data.DBConcurrencyException ex) { return Conflict(new { message = ex.Message }); }
        catch (InvalidOperationException ex) { return Conflict(new { message = ex.Message }); }
    }

    [HttpPut("{id:guid}/status"), HasPermission(Permissions.POS.Edit)]
    public async Task<IActionResult> SetStatus(Guid id, OrderIntakeStatusInput input, CancellationToken token)
    {
        try { return Ok(await intake.UpdateStatusAsync(TenantId(), id, input, current.Username, CanSeeMobile, token)); }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
        catch (System.Data.DBConcurrencyException ex) { return Conflict(new { message = ex.Message }); }
        catch (InvalidOperationException ex) { return Conflict(new { message = ex.Message }); }
    }

    [HttpPost("{id:guid}/quote"), HasPermission(Permissions.POS.Edit)]
    public async Task<IActionResult> CreateQuote(Guid id, OrderIntakeQuoteInput input, CancellationToken token)
    {
        try { if (!await CustomerDataAllowedForDelivery(id, token)) return Forbid(); }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
        try { return Ok(await intake.CreateQuoteAsync(TenantId(), id, input, current.Username, CanSeeMobile, token)); }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
        catch (System.Data.DBConcurrencyException ex) { return Conflict(new { message = ex.Message }); }
        catch (InvalidOperationException ex) { return Conflict(new { message = ex.Message }); }
    }

    [HttpGet("{id:guid}/quote"), HasPermission(Permissions.POS.View)]
    public async Task<IActionResult> GetQuote(Guid id, CancellationToken token)
    {
        var quote = await intake.GetCurrentQuoteAsync(TenantId(), id, CanSeeMobile, token);
        return quote is null ? NotFound() : Ok(quote);
    }

    [HttpPost("{id:guid}/confirmation-requested"), HasPermission(Permissions.POS.Edit)]
    public async Task<IActionResult> MarkConfirmationRequested(Guid id, OrderIntakeConfirmationRequestInput input, CancellationToken token)
    {
        try { if (!await CustomerDataAllowedForDelivery(id, token)) return Forbid(); }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
        try { return Ok(await intake.MarkConfirmationRequestedAsync(TenantId(), id, input, current.Username, CanSeeMobile, token)); }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
        catch (System.Data.DBConcurrencyException ex) { return Conflict(new { message = ex.Message }); }
        catch (InvalidOperationException ex) { return Conflict(new { message = ex.Message }); }
    }

    [HttpPost("{id:guid}/confirmation"), HasPermission(Permissions.POS.Edit)]
    public async Task<IActionResult> RecordConfirmation(Guid id, OrderIntakeConfirmationInput input, CancellationToken token)
    {
        try { if (!await CustomerDataAllowedForDelivery(id, token)) return Forbid(); }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
        try { return Ok(await intake.RecordConfirmationAsync(TenantId(), id, input, current.Username, CanSeeMobile, token)); }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
        catch (System.Data.DBConcurrencyException ex) { return Conflict(new { message = ex.Message }); }
        catch (InvalidOperationException ex) { return Conflict(new { message = ex.Message }); }
    }

    [HttpPost("{id:guid}/sales-order"), HasPermission(Permissions.POS.Create)]
    public async Task<IActionResult> ConvertToSalesOrder(Guid id, OrderIntakeConversionInput input, CancellationToken token)
    {
        try { if (!await CustomerDataAllowedForDelivery(id, token)) return Forbid(); }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
        try
        {
            await intake.ConvertToSalesOrderAsync(TenantId(), id, input, current.Username, token);
            return Ok(await intake.GetSalesOrderAsync(TenantId(), id, CanSeeMobile, token));
        }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
        catch (System.Data.DBConcurrencyException ex) { return Conflict(new { message = ex.Message }); }
        catch (InvalidOperationException ex) { return Conflict(new { message = ex.Message }); }
    }

    [HttpGet("{id:guid}/sales-order"), HasPermission(Permissions.POS.View)]
    public async Task<IActionResult> GetSalesOrder(Guid id, CancellationToken token)
    {
        var order = await intake.GetSalesOrderAsync(TenantId(), id, CanSeeMobile, token);
        return order is null ? NotFound() : Ok(order);
    }

    [HttpPut("{id:guid}/sales-order/status"), HasPermission(Permissions.POS.Edit)]
    public async Task<IActionResult> TransitionSalesOrder(Guid id, OrderIntakeSalesOrderStatusInput input, CancellationToken token)
    {
        try
        {
            await intake.TransitionSalesOrderAsync(TenantId(), id, input.Status, input.Version, current.Username, token);
            return Ok(await intake.GetSalesOrderAsync(TenantId(), id, CanSeeMobile, token));
        }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
        catch (System.Data.DBConcurrencyException ex) { return Conflict(new { message = ex.Message }); }
        catch (InvalidOperationException ex) { return Conflict(new { message = ex.Message }); }
    }
}
