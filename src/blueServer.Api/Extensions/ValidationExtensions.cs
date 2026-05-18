using FluentValidation;
using FluentValidation.AspNetCore;

namespace blueServer.Api.Extensions;

public static class ValidationExtensions
{
    public static IServiceCollection AddValidation(
        this IServiceCollection services)
    {
        services.AddFluentValidationAutoValidation();

        services.AddValidatorsFromAssemblyContaining<Program>();

        return services;
    }
}