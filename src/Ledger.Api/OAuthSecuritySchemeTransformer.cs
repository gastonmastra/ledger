using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Ledger.Api;

/// <summary>Documents the Keycloak authorization-code flow so Scalar can log in.</summary>
public sealed class OAuthSecuritySchemeTransformer(IConfiguration configuration) : IOpenApiDocumentTransformer
{
    public const string SchemeName = "keycloak";

    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        var authority = configuration["Authentication:Authority"]?.TrimEnd('/') ?? "http://localhost:8080/realms/ledger";

        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes[SchemeName] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.OAuth2,
            Description = "Keycloak login",
            Flows = new OpenApiOAuthFlows
            {
                AuthorizationCode = new OpenApiOAuthFlow
                {
                    AuthorizationUrl = new Uri($"{authority}/protocol/openid-connect/auth"),
                    TokenUrl = new Uri($"{authority}/protocol/openid-connect/token"),
                    Scopes = new Dictionary<string, string>
                    {
                        ["openid"] = "OpenID",
                        ["profile"] = "Profile",
                        ["email"] = "Email"
                    }
                }
            }
        };

        document.Security ??= [];
        document.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference(SchemeName, document)] = []
        });

        return Task.CompletedTask;
    }
}
