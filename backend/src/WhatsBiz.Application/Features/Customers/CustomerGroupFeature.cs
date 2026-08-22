using FluentValidation;
using MediatR;
using WhatsBiz.Application.Common.Interfaces;
using WhatsBiz.Domain.Customers;
namespace WhatsBiz.Application.Features.Customers;
public sealed record CustomerGroupDto(Guid CustomerGroupId, string GroupCode, string GroupName, bool IsActive);
public sealed record CustomerGroupInput(string GroupCode, string GroupName, bool IsActive);
public sealed record GetCustomerGroups : IRequest<IReadOnlyCollection<CustomerGroupDto>>;
public sealed record CreateCustomerGroup(CustomerGroupInput Input) : IRequest<CustomerGroupDto>;
public sealed record GetCustomerGroupCustomers(Guid CustomerGroupId) : IRequest<PagedCustomers>;
public sealed class CustomerGroupInputValidator : AbstractValidator<CustomerGroupInput> { public CustomerGroupInputValidator() { RuleFor(x => x.GroupCode).NotEmpty().MaximumLength(50); RuleFor(x => x.GroupName).NotEmpty().MaximumLength(150); } }
public sealed class CustomerGroupHandlers(ICustomerGroupRepository repository, ICustomerRepository customers, ICurrentUserService user) : IRequestHandler<GetCustomerGroups, IReadOnlyCollection<CustomerGroupDto>>, IRequestHandler<CreateCustomerGroup, CustomerGroupDto>, IRequestHandler<GetCustomerGroupCustomers, PagedCustomers>
{
    public async Task<IReadOnlyCollection<CustomerGroupDto>> Handle(GetCustomerGroups request, CancellationToken cancellationToken) => (await repository.List(cancellationToken)).Select(Map).ToArray();
    public async Task<CustomerGroupDto> Handle(CreateCustomerGroup request, CancellationToken cancellationToken) { if (await repository.Exists(request.Input.GroupCode, request.Input.GroupName, cancellationToken)) throw new WhatsBiz.Application.Common.Exceptions.BusinessRuleException("A customer group with this code or name already exists."); var group = new CustomerGroup { GroupCode = request.Input.GroupCode.Trim().ToUpperInvariant(), GroupName = request.Input.GroupName.Trim(), IsActive = request.Input.IsActive, CreatedBy = user.Username }; repository.Add(group); await repository.Save(cancellationToken); return Map(group); }
    public async Task<PagedCustomers> Handle(GetCustomerGroupCustomers request, CancellationToken cancellationToken) { var (items, count) = await customers.Search(null, true, "customerName", false, 1, 200, request.CustomerGroupId, cancellationToken); return new(items.Select(x => new CustomerListDto(x.CustomerId, x.CustomerCode, x.CustomerName, x.CustomerType, x.GSTIN, x.Mobile, x.Email, x.Currency, x.CreditLimit, x.IsActive)).ToArray(), count, 1, 200); }
    private static CustomerGroupDto Map(CustomerGroup x) => new(x.CustomerGroupId, x.GroupCode, x.GroupName, x.IsActive);
}
