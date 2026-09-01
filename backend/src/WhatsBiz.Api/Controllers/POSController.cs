using MediatR;
using Microsoft.AspNetCore.Mvc;
using WhatsBiz.Api.Authorization;
using WhatsBiz.Application.Common.Exceptions;
using WhatsBiz.Application.Common.Interfaces;
using WhatsBiz.Application.Features.POS;
using WhatsBiz.Application.Features.CustomerNotifications;
using WhatsBiz.Application.Features.Printing;
using WhatsBiz.Application.Features.Warehouses;
using WhatsBiz.SharedKernel;
namespace WhatsBiz.Api.Controllers;
[ApiController, Route("api/pos")]
public sealed class POSController(ISender sender, IConfiguration configuration, ICurrentUserService currentUser, ICustomerNotificationService notifications, IPOSLifecycleService lifecycle) : ControllerBase
{
    [HttpGet("products"), HasPermission(Permissions.POS.View)] public Task<IReadOnlyCollection<POSProductDto>> Products([FromQuery] string? search, [FromQuery] string? barcode, [FromQuery] Guid? warehouseId, [FromQuery] int size = 20, CancellationToken token = default) => sender.Send(new SearchPOSProducts(search, barcode, warehouseId, size), token);
    [HttpGet("customers"), HasPermission(Permissions.POS.View)] public Task<IReadOnlyCollection<POSCustomerDto>> Customers([FromQuery] string? search, [FromQuery] int size = 20, CancellationToken token = default) => sender.Send(new SearchPOSCustomers(search, size), token);
    [HttpGet("warehouses"), HasPermission(Permissions.POS.View)] public Task<PagedWarehouses> Warehouses(CancellationToken token) => sender.Send(new GetWarehouses(null, true, null, "warehouseName", false, 1, 100), token);
    [HttpPost("customers/quick"), HasPermission(Permissions.POS.Create)] public Task<POSCustomerDto> QuickCustomer(QuickCustomerInput input, CancellationToken token) => sender.Send(new CreateQuickCustomer(input), token);
    [HttpPost("invoice"), HasPermission(Permissions.POS.Create)] public async Task<PostedInvoiceDto> Invoice(POSInvoiceInput input, CancellationToken token)
    {
        EnforceCashierDiscount(input);
        var result = await sender.Send(new PostInvoice(input), token);
        if (result.Status == "COMPLETED" && input.CustomerId.HasValue)
            await notifications.QueueInvoice(result.InvoiceId, CustomerNotificationEvents.SuccessfulSale, CancellationToken.None);
        return result;
    }
    [HttpGet("invoice/{id:guid}"), HasPermission(Permissions.POS.View)] public Task<POSInvoiceDto> Invoice(Guid id, CancellationToken token) => sender.Send(new GetPOSInvoice(id), token);
    [HttpGet("invoices"), HasPermission(Permissions.POS.View)] public Task<PagedPOSInvoices> Invoices([FromQuery] string? search, [FromQuery] string? status, [FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to, [FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 20, CancellationToken token = default) => sender.Send(new GetPOSInvoices(search, status, from, to, pageNumber, pageSize), token);
    [HttpPost("hold"), HasPermission(Permissions.POS.Create)] public Task<PostedInvoiceDto> Hold(POSInvoiceInput input, CancellationToken token) => sender.Send(new PostInvoice(input, "HELD"), token);
    [HttpPost("resume"), HasPermission(Permissions.POS.Edit)] public Task<POSInvoiceDto> Resume([FromBody] Guid invoiceId, CancellationToken token) => sender.Send(new ResumeInvoice(invoiceId), token);
    [HttpPost("invoice/{id:guid}/complete-held"), HasPermission(Permissions.POS.Create)] public async Task<IActionResult> CompleteHeld(Guid id, CancellationToken token) { await lifecycle.TransitionHeldAsync(id, "COMPLETE", currentUser.Username, token); return NoContent(); }
    [HttpPost("invoice/{id:guid}/cancel-held"), HasPermission(Permissions.POS.Edit)] public async Task<IActionResult> CancelHeld(Guid id, CancellationToken token) { await lifecycle.TransitionHeldAsync(id, "CANCEL", currentUser.Username, token); return NoContent(); }
    [HttpPost("return"), HasPermission(Permissions.POS.Return)] public async Task<IActionResult> Return(POSReturnInput input, CancellationToken token) { await sender.Send(new ReturnInvoice(input), token); return Ok(); }
    [HttpPost("payment"), HasPermission(Permissions.POS.Create)] public async Task<IActionResult> Payment(AddPaymentInput input, CancellationToken token)
    {
        await sender.Send(new AddPOSPayment(input), token);
        await notifications.QueueInvoice(input.InvoiceId, CustomerNotificationEvents.SuccessfulPayment, CancellationToken.None);
        return Ok();
    }
    [HttpGet("payment-methods"), HasPermission(Permissions.POS.View)] public Task<IReadOnlyCollection<PaymentMethodDto>> PaymentMethods(CancellationToken token) => sender.Send(new GetPaymentMethods(), token);
    [HttpGet("today-sales"), HasPermission(Permissions.POS.View)] public Task<TodaySalesDto> Today(CancellationToken token) => sender.Send(new GetTodaySales(), token);
    [HttpGet("invoice/{id:guid}/print"), HasPermission(Permissions.POS.View)] public async Task<ContentResult> Print(Guid id, [FromQuery] string? paper = null, CancellationToken token = default)
    {
        var selected = string.IsNullOrWhiteSpace(paper)
            ? (await sender.Send(new GetPrintingSettings(), token)).PaperSize
            : PaperSizes.Normalize(paper);
        return Content(await sender.Send(new PrintInvoice(id, selected), token), "text/html");
    }
    [HttpGet("export"), HasPermission(Permissions.POS.View)] public async Task<IActionResult> Export([FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to, CancellationToken token) => File(await sender.Send(new ExportSales(from, to), token), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "sales.xlsx");

    private void EnforceCashierDiscount(POSInvoiceInput input)
    {
        if (!currentUser.Roles.Any(x => string.Equals(x, "Cashier", StringComparison.OrdinalIgnoreCase))) return;
        var limit = configuration.GetValue<decimal?>("Retail:CashierMaxDiscountPercent") ?? throw new InvalidOperationException("Retail:CashierMaxDiscountPercent is not configured.");
        CashierDiscountPolicy.Enforce(input, limit);
    }
}

public static class CashierDiscountPolicy
{
    public static void Enforce(POSInvoiceInput input, decimal limit)
    {
        if (limit < 0 || limit > 100)
            throw new InvalidOperationException("Retail:CashierMaxDiscountPercent is invalid.");

        var subtotal = input.Items.Sum(x => x.Quantity * x.UnitPrice);
        var discount = input.Items.Sum(x =>
            x.DiscountAmount > 0
                ? x.DiscountAmount
                : x.Quantity * x.UnitPrice * x.DiscountPercentage / 100m
        ) + input.BillDiscount;
        var percent = subtotal <= 0 ? 0 : discount * 100m / subtotal;

        if (percent > limit)
            throw new BusinessRuleException($"Cashier discount cannot exceed {limit:0.##}%.");
    }
}
