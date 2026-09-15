using Ledger.Api.Wallets;
using Ledger.Modules.Wallets;
using Ledger.Modules.Identity;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Ledger.Api.Identity;
using Ledger.Modules.Transactions.Api;
using Ledger.Modules.Transactions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = builder.Configuration["Authentication:Authority"];;
        options.Audience = builder.Configuration["Authentication:Audience"];;
        options.RequireHttpsMetadata = false;
        options.MapInboundClaims = false;


        options.Events = new JwtBearerEvents
        {
            OnAuthenticationFailed = context =>
            {
                Console.WriteLine("===== JWT AUTH FAILED =====");
                Console.WriteLine(context.Exception.ToString());
                Console.WriteLine("===========================");

                return Task.CompletedTask;
            },

            OnTokenValidated = context =>
            {
                Console.WriteLine("===== JWT VALIDATED =====");

                foreach (var claim in context.Principal!.Claims)
                {
                    Console.WriteLine(
                        $"TYPE: {claim.Type} | VALUE: {claim.Value}");
                }

                return Task.CompletedTask;
            },

            OnMessageReceived = context =>
            {
                Console.WriteLine("===== JWT MESSAGE RECEIVED =====");
                Console.WriteLine(
                    $"Token present: {!string.IsNullOrEmpty(context.Token)}");
                
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();

builder.Services.AddHttpContextAccessor();

builder.Services.AddScoped<ICurrentUser, CurrentUser>();

builder.Services.AddWalletsModule(builder.Configuration);
builder.Services.AddIdentityModule(builder.Configuration);
builder.Services.AddTransactionsModule(builder.Configuration);

var app = builder.Build();


app.UseAuthentication();
app.UseAuthorization();
app.MapWalletsEndpoints();
app.MapTransactionEndpoints();

app.Run();