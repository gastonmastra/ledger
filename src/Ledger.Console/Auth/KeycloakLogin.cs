using IdentityModel.OidcClient;
using Microsoft.Extensions.Configuration;

namespace Ledger.Console.Auth;

/// <summary>Logs the user in with Keycloak (authorization code + PKCE) and keeps the tokens fresh.</summary>
public sealed class KeycloakLogin(IConfiguration configuration)
{
    public HttpMessageHandler? AuthenticatedHandler { get; private set; }

    public string? UserName { get; private set; }

    public async Task LoginAsync(CancellationToken cancellationToken)
    {
        var browser = new LoopbackBrowser(configuration.GetValue("Keycloak:CallbackPort", 7890));

        var client = new OidcClient(new OidcClientOptions
        {
            Authority = configuration["Keycloak:Authority"],
            ClientId = configuration["Keycloak:ClientId"],
            RedirectUri = browser.RedirectUri,
            Scope = "openid profile email",
            Browser = browser,
            LoadProfile = false,
            Policy = new Policy { Discovery = { RequireHttps = false } }
        });

        var result = await client.LoginAsync(new LoginRequest(), cancellationToken);

        if (result.IsError)
            throw new InvalidOperationException($"Login failed: {result.Error} {result.ErrorDescription}");

        UserName = result.User.FindFirst("name")?.Value ?? result.User.FindFirst("preferred_username")?.Value;

        // Adds the access token to every request and refreshes it when it expires.
        AuthenticatedHandler = new RefreshTokenDelegatingHandler(
            client, result.AccessToken, result.RefreshToken, "Bearer", new HttpClientHandler());
    }
}
