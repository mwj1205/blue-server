using System.Text;
using blueServer.Api.Services;
using blueServer.Infrastructure.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace blueServer.Api.Extensions;

public static class JwtExtensions
{
    public static IServiceCollection AddJwt(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var jwtOptions = configuration
            .GetSection(JwtOptions.SectionName)
            .Get<JwtOptions>() ?? new JwtOptions();

        services.Configure<JwtOptions>(
            configuration.GetSection(JwtOptions.SectionName));
        services.AddScoped<JwtService>();

        services
            .AddAuthentication(
                JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters =
                    new TokenValidationParameters
                    {
                        ValidateIssuer = true,
                        ValidateAudience = true,
                        ValidateLifetime = true,
                        ValidateIssuerSigningKey = true,

                        ValidIssuer =
                            jwtOptions.Issuer,

                        ValidAudience =
                            jwtOptions.EffectiveAudience,

                        IssuerSigningKey =
                            new SymmetricSecurityKey(
                                Encoding.UTF8.GetBytes(
                                    jwtOptions.Key))
                    };
            });

        return services;
    }
}
