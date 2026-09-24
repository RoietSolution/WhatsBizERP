using MediatR;
using WhatsBiz.Application.Common.Interfaces;

namespace WhatsBiz.Application.Features.Administration;

public sealed record UpdateBranch(Guid Id, BranchInput Input) : IRequest<BranchDto>;

internal sealed class UpdateBranchHandler(IAdminRepository repository) : IRequestHandler<UpdateBranch, BranchDto>
{
    public Task<BranchDto> Handle(UpdateBranch request, CancellationToken cancellationToken)
        => repository.UpdateBranch(request.Id, request.Input, cancellationToken);
}
