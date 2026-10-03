using System.Security.Claims;
using System.Text.Encodings.Web;
using Ledger.Migrator;
using Ledger.Modules.Accounts.Infrastructure;
using Ledger.Modules.Contacts.Infrastructure;
using Ledger.Modules.Debts.Infrastructure;
using Ledger.Modules.Groups.Infrastructure;
using Ledger.Modules.Identity.Infrastructure;
using Ledger.Modules.Investments.Infrastructure;
using Ledger.Modules.Transactions.Infrastructure;
using Ledger.Modules.Wallets.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;

namespace Ledger.IntegrationTests;

/// <summary>
/// Runs the real API (all modules, real PostgreSQL and RabbitMQ in containers). Keycloak is
/// replaced by <see cref="TestAuthHandler"/>: the <c>X-Test-User</c> header is the token subject.
/// </summary>
public sealed class LedgerApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17")
        .WithDatabase("ledger")
        .Build();

    private readonly RabbitMqContainer _rabbit = new RabbitMqBuilder("rabbitmq:4.2-management")
        .Build();

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _rabbit.StartAsync());
        await MigrateAsync();
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await Task.WhenAll(_postgres.DisposeAsync().AsTask(), _rabbit.DisposeAsync().AsTask());
    }

    public HttpClient ClientFor(string user)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, user);
        return client;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Database", _postgres.GetConnectionString());
        builder.UseSetting("ConnectionStrings:RabbitMq", _rabbit.GetConnectionString());
        builder.UseSetting("Authentication:Authority", "http://keycloak.test/realms/ledger");

        builder.ConfigureTestServices(services =>
            services
                .AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                    options.DefaultChallengeScheme = TestAuthHandler.SchemeName;
                })
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { }));
    }

    /// <summary>Applies every module migration, exactly like the migrator container does.</summary>
    private async Task MigrateAsync()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Database"] = _postgres.GetConnectionString()
            })
            .Build();

        await using var services = new ServiceCollection()
            .AddLogging()
            .AddIdentityPersistence(configuration)
            .AddAccountsPersistence(configuration)
            .AddWalletsPersistence(configuration)
            .AddTransactionsPersistence(configuration)
            .AddContactsPersistence(configuration)
            .AddGroupsPersistence(configuration)
            .AddDebtsPersistence(configuration)
            .AddInvestmentsPersistence(configuration)
            .BuildServiceProvider();

        await ModuleMigrations.ApplyAsync(services, NullLogger.Instance, CancellationToken.None);
    }
}

public sealed class TestAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Test";
    public const string UserHeader = "X-Test-User";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(UserHeader, out var user) || string.IsNullOrWhiteSpace(user))
            return Task.FromResult(AuthenticateResult.NoResult());

        var claims = new[]
        {
            new Claim("sub", user!),
            new Claim("email", $"{user}@test.local"),
            new Claim("name", user!)
        };

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));

        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
    }
}

[CollectionDefinition(Name)]
public sealed class LedgerCollection : ICollectionFixture<LedgerApiFactory>
{
    public const string Name = "Ledger API";
}
