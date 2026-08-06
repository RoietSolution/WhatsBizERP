#pragma warning disable CA1725,CA1861
using FluentValidation;
using MediatR;
using WhatsBiz.Application.Common.Interfaces;

namespace WhatsBiz.Application.Features.Inventory;

public sealed record StockOperationItem(Guid ProductId,Guid? SourceZoneId,Guid? SourceBinId,Guid? DestinationZoneId,Guid? DestinationBinId,string? BatchNo,string? SerialNo,decimal Quantity,decimal UnitCost);
public sealed record StockAdjustmentDocument(string AdjustmentType,string ReasonCode,string ApprovalStatus,Guid WarehouseId,string? Remarks,IReadOnlyCollection<StockOperationItem> Items);
public sealed record StockTransferDocument(Guid SourceWarehouseId,Guid DestinationWarehouseId,DateTimeOffset TransferDate,string ApprovalStatus,string? Remarks,IReadOnlyCollection<StockOperationItem> Items);
public sealed record VerificationItem(Guid ProductId,Guid? ZoneId,Guid? BinId,string? BatchNo,string? SerialNo,decimal CountedQuantity,decimal UnitCost);
public sealed record VerificationDocument(Guid WarehouseId,DateTimeOffset VerificationDate,string ApprovalStatus,string? Remarks,IReadOnlyCollection<VerificationItem> Items);
public sealed record OperationListItem(Guid Id,string Number,DateTimeOffset Date,string Type,string Status,string? Remarks,decimal TotalQuantity,int ItemCount);
public sealed record PagedOperations(IReadOnlyCollection<OperationListItem> Items,int TotalCount,int PageNumber,int PageSize);
public sealed record StockControlRow(Guid Id,Guid ProductId,string ProductCode,string ProductName,Guid WarehouseId,string WarehouseName,string Type,decimal CurrentQuantity,decimal SuggestedQuantity,DateTimeOffset GeneratedOn,string Status,string? Detail);
public sealed record MovementRow(Guid StockMovementHistoryId,DateTimeOffset MovementDate,string MovementType,string TransactionNo,Guid ProductId,string ProductCode,string ProductName,Guid WarehouseId,string WarehouseName,Guid? ZoneId,Guid? BinId,decimal Quantity,decimal BalanceAfter,string? ReferenceType,string? Remarks,string? CreatedBy);
public sealed record PagedMovements(IReadOnlyCollection<MovementRow> Items,int TotalCount,int PageNumber,int PageSize);

public sealed record GetAdjustments(string? Search,Guid? WarehouseId,DateTimeOffset? From,DateTimeOffset? To,int PageNumber,int PageSize):IRequest<PagedOperations>;
public sealed record CreateAdjustment(StockAdjustmentDocument Input):IRequest<OperationDto>;
public sealed record GetTransfers(string? Search,Guid? WarehouseId,DateTimeOffset? From,DateTimeOffset? To,int PageNumber,int PageSize):IRequest<PagedOperations>;
public sealed record CreateTransfer(StockTransferDocument Input):IRequest<OperationDto>;
public sealed record GetVerifications(string? Search,Guid? WarehouseId,int PageNumber,int PageSize):IRequest<PagedOperations>;
public sealed record CreateVerification(VerificationDocument Input):IRequest<OperationDto>;
public sealed record GetReorder(Guid? WarehouseId,Guid? CategoryId,Guid? ProductId,string? Search):IRequest<IReadOnlyCollection<StockControlRow>>;
public sealed record GetAlerts(Guid? WarehouseId,Guid? CategoryId,Guid? ProductId,string? Status,string? Search):IRequest<IReadOnlyCollection<StockControlRow>>;
public sealed record GetMovementHistory(Guid? WarehouseId,Guid? ProductId,DateTimeOffset? From,DateTimeOffset? To,string? Search,int PageNumber,int PageSize):IRequest<PagedMovements>;

public sealed class CreateAdjustmentValidator:AbstractValidator<CreateAdjustment>{public CreateAdjustmentValidator(){RuleFor(x=>x.Input.WarehouseId).NotEmpty();RuleFor(x=>x.Input.AdjustmentType).Must(x=>x is "INCREASE" or "DECREASE");RuleFor(x=>x.Input.ReasonCode).Must(x=>new[]{"DAMAGE","EXPIRY","LOST","FOUND","INTERNAL_CONSUMPTION","PHYSICAL_VERIFICATION","OTHER"}.Contains(x));RuleFor(x=>x.Input.ApprovalStatus).Must(x=>x is "PENDING" or "APPROVED");RuleFor(x=>x.Input.Items).NotEmpty();RuleForEach(x=>x.Input.Items).ChildRules(i=>{i.RuleFor(x=>x.ProductId).NotEmpty();i.RuleFor(x=>x.Quantity).GreaterThan(0);i.RuleFor(x=>x.UnitCost).GreaterThanOrEqualTo(0);});}}
public sealed class CreateTransferValidator:AbstractValidator<CreateTransfer>{public CreateTransferValidator(){RuleFor(x=>x.Input.SourceWarehouseId).NotEmpty();RuleFor(x=>x.Input.DestinationWarehouseId).NotEmpty();RuleFor(x=>x.Input).Must(x=>x.SourceWarehouseId!=x.DestinationWarehouseId||x.Items.Any(i=>i.SourceZoneId!=i.DestinationZoneId||i.SourceBinId!=i.DestinationBinId)).WithMessage("Source and destination locations must differ.");RuleFor(x=>x.Input.ApprovalStatus).Must(x=>x is "PENDING" or "APPROVED");RuleFor(x=>x.Input.Items).NotEmpty();RuleForEach(x=>x.Input.Items).ChildRules(i=>{i.RuleFor(x=>x.ProductId).NotEmpty();i.RuleFor(x=>x.Quantity).GreaterThan(0);});}}
public sealed class CreateVerificationValidator:AbstractValidator<CreateVerification>{public CreateVerificationValidator(){RuleFor(x=>x.Input.WarehouseId).NotEmpty();RuleFor(x=>x.Input.ApprovalStatus).Must(x=>x is "PENDING" or "APPROVED");RuleFor(x=>x.Input.Items).NotEmpty();RuleForEach(x=>x.Input.Items).ChildRules(i=>{i.RuleFor(x=>x.ProductId).NotEmpty();i.RuleFor(x=>x.CountedQuantity).GreaterThanOrEqualTo(0);});}}

public sealed class InventoryOperationsHandlers(IInventoryOperationsRepository repository,ICurrentUserService user):
 IRequestHandler<GetAdjustments,PagedOperations>,IRequestHandler<CreateAdjustment,OperationDto>,IRequestHandler<GetTransfers,PagedOperations>,IRequestHandler<CreateTransfer,OperationDto>,IRequestHandler<GetVerifications,PagedOperations>,IRequestHandler<CreateVerification,OperationDto>,IRequestHandler<GetReorder,IReadOnlyCollection<StockControlRow>>,IRequestHandler<GetAlerts,IReadOnlyCollection<StockControlRow>>,IRequestHandler<GetMovementHistory,PagedMovements>{
 public Task<PagedOperations> Handle(GetAdjustments x,CancellationToken t)=>repository.Adjustments(x.Search,x.WarehouseId,x.From,x.To,x.PageNumber,x.PageSize,t);
 public Task<OperationDto> Handle(CreateAdjustment x,CancellationToken t)=>repository.CreateAdjustment(x.Input,user.Username,t);
 public Task<PagedOperations> Handle(GetTransfers x,CancellationToken t)=>repository.Transfers(x.Search,x.WarehouseId,x.From,x.To,x.PageNumber,x.PageSize,t);
 public Task<OperationDto> Handle(CreateTransfer x,CancellationToken t)=>repository.CreateTransfer(x.Input,user.Username,t);
 public Task<PagedOperations> Handle(GetVerifications x,CancellationToken t)=>repository.Verifications(x.Search,x.WarehouseId,x.PageNumber,x.PageSize,t);
 public Task<OperationDto> Handle(CreateVerification x,CancellationToken t)=>repository.CreateVerification(x.Input,user.Username,t);
 public Task<IReadOnlyCollection<StockControlRow>> Handle(GetReorder x,CancellationToken t)=>repository.Reorder(x.WarehouseId,x.CategoryId,x.ProductId,x.Search,t);
 public Task<IReadOnlyCollection<StockControlRow>> Handle(GetAlerts x,CancellationToken t)=>repository.Alerts(x.WarehouseId,x.CategoryId,x.ProductId,x.Status,x.Search,t);
 public Task<PagedMovements> Handle(GetMovementHistory x,CancellationToken t)=>repository.Movements(x.WarehouseId,x.ProductId,x.From,x.To,x.Search,x.PageNumber,x.PageSize,t);
}
