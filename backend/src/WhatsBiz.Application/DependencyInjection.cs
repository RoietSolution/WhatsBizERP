using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using WhatsBiz.Application.Common.Behaviors;

namespace WhatsBiz.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddAutoMapper(_ => { }, typeof(DependencyInjection).Assembly);
        services.AddMediatR(configuration => { configuration.RegisterServicesFromAssembly(typeof(DependencyInjection).Assembly); configuration.AddOpenBehavior(typeof(ValidationBehavior<,>)); });
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly, includeInternalTypes: true);
        return services;
    }
}
