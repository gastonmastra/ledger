# Ledger

Ledger is a personal finance app built on **double-entry bookkeeping**. Every movement of money is a transaction made of two or more *postings* against accounts, and the postings in a transaction must always add up to zero. That rule makes the books self-checking: money is never created or lost, only moved between accounts.

The project is written in **.NET 10** as a **modular monolith**: one deployable application split into independent modules, each with its own domain, persistence and PostgreSQL schema.

> **Status:** early development. The core domain and persistence are in place. The HTTP API and the console client expose only part of it so far.

## Core concepts

| Concept | Description |
| --- | --- |
| **Account** | A bucket that money flows into or out of, such as a bank account, "Groceries" or "Salary". Each account has a type: `Asset`, `Liability`, `Equity`, `Income` or `Expense`, and can be renamed or archived. |
| **Transaction** | Something that happened at a point in time (`OccurredAt`), with an optional description. It contains at least two postings. |
| **Posting** | A signed, non-zero amount applied to one account. Positive amounts are debits and negative amounts are credits. The postings in a transaction must sum to zero. |
| **Wallet** | A user-owned container with a name and a currency (`EUR`, `USD`, `ARS`). |
| **User** | A person authenticated through an external identity provider (Keycloak), identified by the token's `sub` claim. |

### Example: splitting a dinner bill

You pay 100 from your bank account for a dinner. Half of it is your food expense, and a friend owes you the other half:

```csharp
TransactionBuilder
    .Create(DateTimeOffset.UtcNow, "Dinner")
    .Add(foodAccountId,              50m)   // Expense
    .Add(friendReceivableAccountId,  50m)   // Asset: money owed to you
    .Add(bankAccountId,            -100m)   // Asset: money leaving the bank
    .Build();                               // throws if postings don't balance
```

You can find more examples in `src/Modules/Transactions/Application/Examples.cs`.

## Architecture

```text
Ledger.slnx
└── src
    ├── Ledger.Api           ASP.NET Core minimal API (JWT auth via Keycloak)
    ├── Ledger.Console       Interactive console client (uses the modules directly)
    └── Modules
        ├── Accounts         Chart of accounts
        ├── Identity         Users mapped from the identity provider
        ├── Transactions     Double-entry transactions and postings
        └── Wallets          User wallets and currencies
```

Every module follows the same structure:

```text
Modules/<Name>/
├── Domain/              Entities, value objects, invariants
├── Application/         Use cases, one folder per command/query (Command + Handler + DTO)
├── Infrastructure/      EF Core DbContext, entity configurations, repositories/readers
├── Migrations/          EF Core migrations owned by this module
└── <Name>Module.cs      AddXxxModule(IServiceCollection, IConfiguration) registration
```

Design principles:

- **Module isolation.** Each module has its own `DbContext`, its own PostgreSQL schema (`accounts`, `identity`, `transactions`, `wallets`) and its own `__EFMigrationsHistory` table. Modules don't share tables.
- **Rich domain model.** Entities have private setters and factory methods (`Create`) that enforce invariants. For example, a transaction can't be built unless its postings balance.
- **Vertical slices.** Each use case is a command or query paired with a handler, registered in DI and called by the API endpoints or console menus.
- **Hosts are thin.** `Ledger.Api` and `Ledger.Console` only compose modules and translate input and output.

## Tech stack

- .NET 10 / C# (ASP.NET Core minimal APIs, Generic Host)
- Entity Framework Core 10 + Npgsql
- PostgreSQL 17
- Keycloak (OpenID Connect / JWT bearer authentication)
- Docker Compose for local infrastructure

## Getting started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Docker
- EF Core CLI: `dotnet tool install --global dotnet-ef`

### 1. Start the infrastructure

```bash
docker compose up -d
```

This starts:

- **PostgreSQL** on `localhost:5432` (database `ledger`, user/password `postgres`/`postgres`)
- **Keycloak** on `http://localhost:8080` (admin/admin)

### 2. Configure the application

`appsettings.json` has no real connection string. Store local settings with [user secrets](https://learn.microsoft.com/aspnet/core/security/app-secrets) instead:

```bash
# API
dotnet user-secrets --project src/Ledger.Api set "ConnectionStrings:Database" \
  "Host=localhost;Port=5432;Database=ledger;Username=postgres;Password=postgres"
dotnet user-secrets --project src/Ledger.Api set "Authentication:Authority" \
  "http://localhost:8080/realms/<your-realm>"
dotnet user-secrets --project src/Ledger.Api set "Authentication:Audience" "<your-client-id>"

# Console
dotnet user-secrets --project src/Ledger.Console set "ConnectionStrings:Database" \
  "Host=localhost;Port=5432;Database=ledger;Username=postgres;Password=postgres"
```

In Keycloak, create a realm and a client for the API. The API reads the user's identity from the token's `sub` claim.

### 3. Apply database migrations

Every module has its own `DbContext`, so apply each one:

```bash
for ctx in Identity Wallets Transactions Accounts; do
  dotnet ef database update \
    --context ${ctx}DbContext \
    --project src/Modules/${ctx} \
    --startup-project src/Ledger.Api
done
```

See [Migrations.md](Migrations.md) for the full migration workflow: creating, reviewing, rolling back and generating SQL scripts.

### 4. Run

```bash
# HTTP API on http://localhost:5209 (https://localhost:7269)
dotnet run --project src/Ledger.Api

# Interactive console client
dotnet run --project src/Ledger.Console
```

## HTTP API

| Method | Route | Auth | Description |
| --- | --- | --- | --- |
| `GET` | `/api/wallets` | ✅ | List the current user's wallets |
| `POST` | `/api/wallets` | ✅ | Create a wallet: `{ "name": "...", "currency": "EUR" }` |
| `POST` | `/api/transactions` | — | Create a transaction with its postings |
| `GET` | `/api/transactions/{id}` | — | Get a transaction and its postings |

Example transaction request:

```json
POST /api/transactions
{
  "occurredAt": "2026-09-29T13:00:00Z",
  "description": "Lunch",
  "postings": [
    { "accountId": "<food-account-id>", "amount": 20 },
    { "accountId": "<bank-account-id>", "amount": -20 }
  ]
}
```

## Console client

`Ledger.Console` is a menu-driven terminal app. It calls module handlers directly, without going through the API. It's useful for exploring the domain during development:

- **Wallets**: list and create wallets
- **Transactions**: look up a transaction by id
- **Accounts**: create an account and get it by id

## Roadmap

- Endpoints for accounts in the API (`AccountsEndpoints.cs` is still empty)
- Listing and archiving accounts
- Validating that transaction postings reference existing, non-archived accounts
- Account balances and reporting
- Linking wallets and accounts to the authenticated user through the Identity module
- Tests
