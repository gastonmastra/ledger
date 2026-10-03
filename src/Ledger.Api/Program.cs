using System.Text.Json.Serialization;
using Ledger.Api;
using Ledger.BuildingBlocks.Infrastructure.Messaging;
using Ledger.BuildingBlocks.Presentation;
using Ledger.Modules.Accounts.Infrastructure;
using Ledger.Modules.Accounts.Presentation;
using Ledger.Modules.Contacts.Infrastructure;
using Ledger.Modules.Contacts.Presentation;
using Ledger.Modules.Debts.Infrastructure;
using Ledger.Modules.Debts.Presentation;
using Ledger.Modules.Groups.Infrastructure;
using Ledger.Modules.Groups.Presentation;
using Ledger.Modules.Identity.Infrastructure;
using Ledger.Modules.Identity.Presentation;
using Ledger.Modules.Investments.Infrastructure;
using Ledger.Modules.Investments.Presentation;
using Ledger.Modules.Transactions.Infrastructure;
using Ledger.Modules.Transactions.Presentation;
using Ledger.Modules.Wallets.Infrastructure;
using Ledger.Modules.Wallets.Presentation;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddKeycloakAuthentication(builder.Configuration);
builder.Services.AddAuthorization();
builder.Services.AddMemoryCache();
builder.Services.AddPresentationCore();
builder.Services.AddHealthChecks();
builder.Services.AddOpenApi(options => options.AddDocumentTransformer<OAuthSecuritySchemeTransformer>());

// Modules: each one owns its schema, its bus and its endpoints.
builder.Services
    .AddIdentityModule(builder.Configuration)
    .AddAccountsModule(builder.Configuration)
    .AddWalletsModule(builder.Configuration)
    .AddTransactionsModule(builder.Configuration)
    .AddContactsModule(builder.Configuration)
    .AddGroupsModule(builder.Configuration)
    .AddDebtsModule(builder.Configuration)
    .AddInvestmentsModule(builder.Configuration)
    .AddLedgerMessageBus(builder.Configuration);

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

app.UseAuthentication();
app.UseUserProvisioning();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(options => options
        .WithTitle("Ledger API")
        .AddPreferredSecuritySchemes(OAuthSecuritySchemeTransformer.SchemeName)
        .AddAuthorizationCodeFlow(OAuthSecuritySchemeTransformer.SchemeName, flow =>
        {
            flow.ClientId = builder.Configuration["Authentication:ScalarClientId"] ?? "ledger-scalar";
            flow.Pkce = Pkce.Sha256;
            flow.SelectedScopes = ["openid", "profile", "email"];
        }));
}

app.MapHealthChecks("/health");

app.MapIdentityEndpoints();
app.MapAccountsEndpoints();
app.MapWalletsEndpoints();
app.MapTransactionsEndpoints();
app.MapContactsEndpoints();
app.MapGroupsEndpoints();
app.MapDebtsEndpoints();
app.MapInvestmentsEndpoints();

app.Run();

/// <summary>Entry point, exposed for the integration tests.</summary>
public partial class Program;
