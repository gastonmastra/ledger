using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace Ledger.Api;

public static class Authentication
{
    /// <summary>
    /// Validates Keycloak access tokens. <c>Authentication:MetadataAddress</c> lets the API reach
    /// Keycloak through an internal address (docker network) while tokens keep the public issuer.
    /// </summary>
    public static IServiceCollection AddKeycloakAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.Authority = configuration["Authentication:Authority"];
                options.Audience = configuration["Authentication:Audience"];
                options.RequireHttpsMetadata = configuration.GetValue("Authentication:RequireHttpsMetadata", false);
                options.MapInboundClaims = false;

                if (configuration["Authentication:MetadataAddress"] is { Length: > 0 } metadataAddress)
                    options.MetadataAddress = metadataAddress;

                if (configuration["Authentication:Authority"] is { Length: > 0 } issuer)
                    options.TokenValidationParameters.ValidIssuer = issuer;
            });

        return services;
    }
}
