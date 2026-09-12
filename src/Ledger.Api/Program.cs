using Ledger.Api.Wallets;
using Ledger.Modules.Wallets;
using Ledger.Modules.Identity;
using Microsoft.AspNetCore.Authentication.JwtBearer;

var builder = WebApplication.CreateBuilder(args);

// builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
//     .AddJwtBearer(options =>
//     {
//         options.Authority = builder.Configuration["Authentication:Authority"];
//         options.Audience = builder.Configuration["Authentication:Audience"];
//         options.RequireHttpsMetadata = false;
//     });

// builder.Services.AddAuthorization();

builder.Services.AddWalletsModule(builder.Configuration);
builder.Services.AddIdentityModule(builder.Configuration);

var app = builder.Build();



// app.UseAuthentication();
// app.UseAuthorization();
app.MapWalletsEndpoints();

app.Run();