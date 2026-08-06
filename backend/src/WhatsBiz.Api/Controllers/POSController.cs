using MediatR;using Microsoft.AspNetCore.Mvc;using WhatsBiz.Api.Authorization;using WhatsBiz.Application.Features.POS;using WhatsBiz.SharedKernel;
namespace WhatsBiz.Api.Controllers;
[ApiController,Route("api/pos")]
public sealed class POSController(ISender sender):ControllerBase{
[HttpGet("products"),HasPermission(Permissions.POS.View)]public Task<IReadOnlyCollection<POSProductDto>> Products([FromQuery]string? search,[FromQuery]string? barcode,[FromQuery]int size=20,CancellationToken token=default)=>sender.Send(new SearchPOSProducts(search,barcode,size),token);
[HttpGet("customers"),HasPermission(Permissions.POS.View)]public Task<IReadOnlyCollection<POSCustomerDto>> Customers([FromQuery]string? search,[FromQuery]int size=20,CancellationToken token=default)=>sender.Send(new SearchPOSCustomers(search,size),token);
[HttpPost("customers/quick"),HasPermission(Permissions.POS.Create)]public Task<POSCustomerDto> QuickCustomer(QuickCustomerInput input,CancellationToken token)=>sender.Send(new CreateQuickCustomer(input),token);
[HttpPost("invoice"),HasPermission(Permissions.POS.Create)]public Task<PostedInvoiceDto> Invoice(POSInvoiceInput input,CancellationToken token)=>sender.Send(new PostInvoice(input),token);
[HttpGet("invoice/{id:guid}"),HasPermission(Permissions.POS.View)]public Task<POSInvoiceDto> Invoice(Guid id,CancellationToken token)=>sender.Send(new GetPOSInvoice(id),token);
[HttpGet("invoices"),HasPermission(Permissions.POS.View)]public Task<PagedPOSInvoices> Invoices([FromQuery]string? search,[FromQuery]string? status,[FromQuery]DateTimeOffset? from,[FromQuery]DateTimeOffset? to,[FromQuery]int pageNumber=1,[FromQuery]int pageSize=20,CancellationToken token=default)=>sender.Send(new GetPOSInvoices(search,status,from,to,pageNumber,pageSize),token);
[HttpPost("hold"),HasPermission(Permissions.POS.Edit)]public Task<PostedInvoiceDto> Hold(POSInvoiceInput input,CancellationToken token)=>sender.Send(new PostInvoice(input,"HELD"),token);
[HttpPost("resume"),HasPermission(Permissions.POS.Edit)]public Task<POSInvoiceDto> Resume([FromBody]Guid invoiceId,CancellationToken token)=>sender.Send(new ResumeInvoice(invoiceId),token);
[HttpPost("return"),HasPermission(Permissions.POS.Return)]public async Task<IActionResult> Return(POSReturnInput input,CancellationToken token){await sender.Send(new ReturnInvoice(input),token);return Ok();}
[HttpPost("payment"),HasPermission(Permissions.POS.Create)]public async Task<IActionResult> Payment(AddPaymentInput input,CancellationToken token){await sender.Send(new AddPOSPayment(input),token);return Ok();}
[HttpGet("payment-methods"),HasPermission(Permissions.POS.View)]public Task<IReadOnlyCollection<PaymentMethodDto>> PaymentMethods(CancellationToken token)=>sender.Send(new GetPaymentMethods(),token);
[HttpGet("today-sales"),HasPermission(Permissions.POS.View)]public Task<TodaySalesDto> Today(CancellationToken token)=>sender.Send(new GetTodaySales(),token);
[HttpGet("invoice/{id:guid}/print"),HasPermission(Permissions.POS.View)]public async Task<ContentResult> Print(Guid id,[FromQuery]string paper="80mm",CancellationToken token=default)=>Content(await sender.Send(new PrintInvoice(id,paper),token),"text/html");
[HttpGet("export"),HasPermission(Permissions.POS.View)]public async Task<IActionResult> Export([FromQuery]DateTimeOffset? from,[FromQuery]DateTimeOffset? to,CancellationToken token)=>File(await sender.Send(new ExportSales(from,to),token),"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet","sales.xlsx");}
