#pragma warning disable CA1725
using System.Text.Json;
using FluentValidation;
using MediatR;
using WhatsBiz.Application.Common.Exceptions;
using WhatsBiz.Application.Common.Interfaces;
using WhatsBiz.Domain.Customers;
using WhatsBiz.Domain.POS;

namespace WhatsBiz.Application.Features.POS;

internal static class POSJson
{
    internal static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = null, DictionaryKeyPolicy = null };
}

/* DTOs / Input models */

public sealed record POSProductDto(
    Guid ProductId,
    string ProductCode,
    string? Barcode,
    string ProductName,
    decimal SellingPrice,
    decimal MRP,
    decimal GSTPercentage,
    bool IsBatchManaged,
    bool IsSerialManaged,
    decimal? AvailableQuantity,
    bool NegativeStockAllowed);

public sealed record POSCustomerDto(
    Guid CustomerId,
    string CustomerCode,
    string CustomerName,
    string? Mobile,
    string? GSTIN);

public sealed record POSItemInput(
    Guid ProductId,
    string? Barcode,
    decimal Quantity,
    decimal UnitPrice,
    decimal DiscountPercentage,
    decimal DiscountAmount,
    decimal TaxPercentage);

public sealed record POSPaymentInput(string MethodCode, decimal Amount, string? ReferenceNumber);

public sealed record POSInvoiceInput(
    Guid? CounterId,
    Guid? ShiftId,
    Guid? CustomerId,
    Guid WarehouseId,
    Guid? SalesPersonId,
    IReadOnlyCollection<POSItemInput> Items,
    IReadOnlyCollection<POSPaymentInput> Payments,
    decimal BillDiscount,
    decimal RoundOff,
    string? Remarks,
    bool InterState,
    string? DiscountAuthorizedBy);

public sealed record POSPaymentDto(
    Guid PaymentId,
    string MethodCode,
    string MethodName,
    decimal Amount,
    string? ReferenceNumber,
    DateTimeOffset PaymentDate);

public sealed record POSInvoiceItemDto(
    Guid InvoiceItemId,
    Guid ProductId,
    string ProductCode,
    string ProductName,
    string? Barcode,
    decimal Quantity,
    decimal ReturnedQuantity,
    decimal UnitPrice,
    decimal DiscountPercentage,
    decimal DiscountAmount,
    decimal TaxPercentage,
    decimal TaxAmount,
    decimal LineTotal);

public sealed record POSInvoiceDto(
    Guid InvoiceId,
    string InvoiceNumber,
    DateTimeOffset InvoiceDate,
    Guid? CustomerId,
    string? CustomerName,
    Guid WarehouseId,
    string WarehouseName,
    decimal Subtotal,
    decimal DiscountAmount,
    decimal TaxAmount,
    decimal RoundOff,
    decimal GrandTotal,
    decimal PaidAmount,
    decimal BalanceAmount,
    string Status,
    string? Remarks,
    IReadOnlyCollection<POSInvoiceItemDto> Items,
    IReadOnlyCollection<POSPaymentDto> Payments);

public sealed record POSInvoiceListDto(
    Guid InvoiceId,
    string InvoiceNumber,
    DateTimeOffset InvoiceDate,
    string? CustomerName,
    decimal GrandTotal,
    decimal PaidAmount,
    decimal BalanceAmount,
    string Status,
    string? SourceChannel);

public sealed record PagedPOSInvoices(IReadOnlyCollection<POSInvoiceListDto> Items, int TotalCount, int PageNumber, int PageSize);

public sealed record POSReturnItem(Guid InvoiceItemId, decimal Quantity);
public sealed record POSReturnInput(Guid InvoiceId, IReadOnlyCollection<POSReturnItem> Items, string Reason);
public sealed record AddPaymentInput(Guid InvoiceId, string MethodCode, decimal Amount, string? ReferenceNumber);
public sealed record PaymentMethodDto(Guid PaymentMethodId, string MethodCode, string MethodName, bool RequiresReference);
public sealed record TodaySalesDto(decimal GrossSales, decimal Collections, int InvoiceCount, decimal Cash, decimal UPI, decimal Card);
public sealed record QuickCustomerInput(string CustomerName, string? Mobile, string? GSTIN);
public sealed record PostedInvoiceDto(Guid InvoiceId, string InvoiceNumber, decimal GrandTotal, decimal PaidAmount, string Status);

/* Requests */

public sealed record SearchPOSProducts(string? Search, string? Barcode, Guid? WarehouseId, int Size = 20) : IRequest<IReadOnlyCollection<POSProductDto>>;
public sealed record SearchPOSCustomers(string? Search, int Size = 20) : IRequest<IReadOnlyCollection<POSCustomerDto>>;
public sealed record CreateQuickCustomer(QuickCustomerInput Input) : IRequest<POSCustomerDto>;
public sealed record PostInvoice(POSInvoiceInput Input, string Status = "COMPLETED") : IRequest<PostedInvoiceDto>;
public sealed record GetPOSInvoice(Guid Id) : IRequest<POSInvoiceDto>;
public sealed record GetPOSInvoices(string? Search, string? Status, DateTimeOffset? From, DateTimeOffset? To, int Page, int Size) : IRequest<PagedPOSInvoices>;
public sealed record ResumeInvoice(Guid Id) : IRequest<POSInvoiceDto>;
public sealed record ReturnInvoice(POSReturnInput Input) : IRequest;
public sealed record AddPOSPayment(AddPaymentInput Input) : IRequest;
public sealed record GetPaymentMethods : IRequest<IReadOnlyCollection<PaymentMethodDto>>;
public sealed record GetTodaySales : IRequest<TodaySalesDto>;
public sealed record PrintInvoice(Guid Id, string Paper) : IRequest<string>;
public sealed record ExportSales(DateTimeOffset? From, DateTimeOffset? To) : IRequest<byte[]>;

/* Validators */

public sealed class POSInvoiceValidator : AbstractValidator<PostInvoice>
{
    public POSInvoiceValidator()
    {
        RuleFor(x => x.Input.WarehouseId).NotEmpty();
        RuleFor(x => x.Input.Items).NotEmpty();

        RuleForEach(x => x.Input.Items).ChildRules(i =>
        {
            i.RuleFor(x => x.ProductId).NotEmpty();
            i.RuleFor(x => x.Quantity).GreaterThan(0);
            i.RuleFor(x => x.UnitPrice).GreaterThanOrEqualTo(0);
            i.RuleFor(x => x.DiscountPercentage).InclusiveBetween(0, 100);
            i.RuleFor(x => x.TaxPercentage).InclusiveBetween(0, 100);
        });

        RuleForEach(x => x.Input.Payments).ChildRules(p =>
        {
            p.RuleFor(x => x.MethodCode).NotEmpty();
            p.RuleFor(x => x.Amount).GreaterThan(0);
        });
    }
}

public sealed class ReturnValidator : AbstractValidator<ReturnInvoice>
{
    public ReturnValidator()
    {
        RuleFor(x => x.Input.InvoiceId).NotEmpty();
        RuleFor(x => x.Input.Items).NotEmpty();
        RuleForEach(x => x.Input.Items).ChildRules(i => i.RuleFor(x => x.Quantity).GreaterThan(0));
        RuleFor(x => x.Input.Reason).NotEmpty();
    }
}

public sealed class PaymentValidator : AbstractValidator<AddPOSPayment>
{
    public PaymentValidator()
    {
        RuleFor(x => x.Input.InvoiceId).NotEmpty();
        RuleFor(x => x.Input.MethodCode).NotEmpty();
        RuleFor(x => x.Input.Amount).GreaterThan(0);
    }
}

/* Handlers */

public sealed class POSHandlers(
    IPOSRepository repository,
    IPOSEngine engine,
    IPOSDocumentService documents,
    IAdminRepository admin,
    ICustomerRepository customers,
    ICurrentUserService user) :
    IRequestHandler<SearchPOSProducts, IReadOnlyCollection<POSProductDto>>,
    IRequestHandler<SearchPOSCustomers, IReadOnlyCollection<POSCustomerDto>>,
    IRequestHandler<CreateQuickCustomer, POSCustomerDto>,
    IRequestHandler<PostInvoice, PostedInvoiceDto>,
    IRequestHandler<GetPOSInvoice, POSInvoiceDto>,
    IRequestHandler<GetPOSInvoices, PagedPOSInvoices>,
    IRequestHandler<ResumeInvoice, POSInvoiceDto>,
    IRequestHandler<ReturnInvoice>,
    IRequestHandler<AddPOSPayment>,
    IRequestHandler<GetPaymentMethods, IReadOnlyCollection<PaymentMethodDto>>,
    IRequestHandler<GetTodaySales, TodaySalesDto>,
    IRequestHandler<PrintInvoice, string>,
    IRequestHandler<ExportSales, byte[]>
{
    public async Task<IReadOnlyCollection<POSProductDto>> Handle(SearchPOSProducts q, CancellationToken t) =>
        (await repository.Products(q.Search, q.Barcode, q.WarehouseId, q.Size, t))
            .Select(x => new POSProductDto(x.Product.ProductId, x.Product.ProductCode, x.MatchedBarcode, x.Product.ProductName, x.Product.SellingPrice, x.Product.MRP, x.Product.GSTPercentage, x.Product.IsBatchManaged, x.Product.IsSerialManaged, x.AvailableQuantity, x.NegativeStockAllowed))
            .ToArray();

    public async Task<IReadOnlyCollection<POSCustomerDto>> Handle(SearchPOSCustomers q, CancellationToken t) =>
        (await repository.Customers(q.Search, q.Size, t))
            .Select(x => new POSCustomerDto(x.CustomerId, x.CustomerCode, x.CustomerName, x.Mobile, x.GSTIN))
            .ToArray();

    public async Task<POSCustomerDto> Handle(CreateQuickCustomer q, CancellationToken t)
    {
        var code = "CASH-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var c = new Customer
        {
            CustomerCode = code,
            CustomerName = q.Input.CustomerName,
            CustomerType = "Retail",
            Mobile = q.Input.Mobile,
            GSTIN = q.Input.GSTIN,
            Currency = "INR",
            IsActive = true,
            CreatedBy = user.Username
        };

        customers.Add(c);
        await customers.Save(t);

        return new(c.CustomerId, c.CustomerCode, c.CustomerName, c.Mobile, c.GSTIN);
    }

    public async Task<PostedInvoiceDto> Handle(PostInvoice q, CancellationToken t)
    {
        var i = q.Input;
        var postRequest = new POSPostRequest(
            i.CounterId,
            i.ShiftId,
            i.CustomerId,
            i.WarehouseId,
            i.SalesPersonId,
            JsonSerializer.Serialize(i.Items, POSJson.Options),
            JsonSerializer.Serialize(i.Payments, POSJson.Options),
            i.BillDiscount,
            i.RoundOff,
            i.Remarks,
            q.Status,
            i.InterState,
            i.DiscountAuthorizedBy,
            user.Username);

        var r = await engine.Post(postRequest, t);

        return new(r.InvoiceId, r.InvoiceNumber, r.GrandTotal, r.PaidAmount, r.Status);
    }

    public async Task<POSInvoiceDto> Handle(GetPOSInvoice q, CancellationToken t) =>
        Map(await repository.Invoice(q.Id, t) ?? throw new EntityNotFoundException("Invoice not found."));

    public async Task<PagedPOSInvoices> Handle(GetPOSInvoices q, CancellationToken t)
    {
        var (x, n) = await repository.Invoices(q.Search, q.Status, q.From, q.To, q.Page, q.Size, t);
        var sources = await repository.SourceChannels(x.Select(a => a.InvoiceId).ToArray(), t);

        return new(
            x.Select(a => new POSInvoiceListDto(a.InvoiceId, a.InvoiceNumber, a.InvoiceDate, a.Customer?.CustomerName, a.GrandTotal, a.PaidAmount, a.BalanceAmount, a.Status, sources.GetValueOrDefault(a.InvoiceId))).ToArray(),
            n,
            q.Page,
            q.Size);
    }

    public async Task<POSInvoiceDto> Handle(ResumeInvoice q, CancellationToken t)
    {
        var x = await repository.Invoice(q.Id, t) ?? throw new EntityNotFoundException("Held invoice not found.");
        if (x.Status is not ("HELD" or "SUSPENDED"))
            throw new BusinessRuleException("Invoice is not held or suspended.");

        return Map(x);
    }

    public async Task Handle(ReturnInvoice q, CancellationToken t)
    {
        await engine.Return(new POSReturnRequest(q.Input.InvoiceId, JsonSerializer.Serialize(q.Input.Items, POSJson.Options), q.Input.Reason, user.Username), t);
    }

    public Task Handle(AddPOSPayment q, CancellationToken t) =>
        engine.Pay(new POSPaymentRequest(q.Input.InvoiceId, q.Input.MethodCode, q.Input.Amount, q.Input.ReferenceNumber, user.Username), t);

    public async Task<IReadOnlyCollection<PaymentMethodDto>> Handle(GetPaymentMethods q, CancellationToken t) =>
        (await repository.PaymentMethods(t)).Select(x => new PaymentMethodDto(x.PaymentMethodId, x.MethodCode, x.MethodName, x.RequiresReference)).ToArray();

    public async Task<TodaySalesDto> Handle(GetTodaySales q, CancellationToken t)
    {
        var x = await repository.Today(t);
        return new(x.GrossSales, x.Collections, x.InvoiceCount, x.Cash, x.UPI, x.Card);
    }

    public async Task<string> Handle(PrintInvoice q, CancellationToken t)
    {
        var invoice = await repository.Invoice(q.Id, t)
            ?? throw new EntityNotFoundException("Invoice not found.");
        var company = await admin.Company(t);
        var loyalty = await repository.CoinSummary(invoice.InvoiceId, t);
        return documents.InvoiceHtml(invoice, q.Paper, new(company, loyalty));
    }

    public async Task<byte[]> Handle(ExportSales q, CancellationToken t)
    {
        var (x, _) = await repository.Invoices(null, null, q.From, q.To, 1, 10000, t);
        return documents.Export(x);
    }

    private static POSInvoiceDto Map(SalesInvoice x) =>
        new(
            x.InvoiceId,
            x.InvoiceNumber,
            x.InvoiceDate,
            x.CustomerId,
            x.Customer?.CustomerName,
            x.WarehouseId,
            x.Warehouse.WarehouseName,
            x.Subtotal,
            x.DiscountAmount,
            x.TaxAmount,
            x.RoundOff,
            x.GrandTotal,
            x.PaidAmount,
            x.BalanceAmount,
            x.Status,
            x.Remarks,
            x.Items.Select(i => new POSInvoiceItemDto(i.InvoiceItemId, i.ProductId, i.Product.ProductCode, i.Product.ProductName, i.Barcode, i.Quantity, i.ReturnedQuantity, i.UnitPrice, i.DiscountPercentage, i.DiscountAmount, i.TaxPercentage, i.TaxAmount, i.LineTotal)).ToArray(),
            x.Payments.Select(p => new POSPaymentDto(p.PaymentId, p.PaymentMethod.MethodCode, p.PaymentMethod.MethodName, p.Amount, p.ReferenceNumber, p.PaymentDate)).ToArray());
}
