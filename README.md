# Ledger

Ledger is a personal finance app built on **double-entry bookkeeping**. Every movement of money is a transaction made of two or more *postings* against accounts, and the postings of a transaction always add up to zero **in every currency**. Money is never created or lost, only moved.

It is a **.NET 10 modular monolith**: one deployable application made of independent modules. Each module has its own clean-architecture layers, its own PostgreSQL schema and EF Core migrations, and its own RabbitMQ queue. Modules only talk to each other through integration events, so any of them can later be moved into its own service.

## Features

| Feature | How |
| --- | --- |
| Wallets | Create (with an opening balance), rename, archive (only when empty) and delete (only without transactions). EUR, USD and ARS. |
| Income and expenses | Record money in or out of a wallet, optionally in a user-defined category. Without a category it goes to *Uncategorized*. |
| Transfers | Between two of my wallets. Across currencies I type the amount received, and the ledger balances each currency through a *Currency exchange* account. A wallet can never go below zero. |
| Loans | Lend money from a wallet to a contact. The contact now owes me. |
| Splits | Split an expense among me, my contacts or a group: equally, by amount or by percentage, with **any number of payers**. "A friend paid for me" is a split where the friend pays and I consume. |
| Groups | Shared with every member who is a registered user. Group balances are **simplified** into the fewest payments. |
| Settlements | Full settlement of one debt, of everything with a contact, or of one suggested group payment. |
| Linked contacts | A contact with a registered user's email is linked. Debts are then mirrored into that user's books automatically, with contacts created for them. Money they paid or received waits in *Pending wallet assignments* until they choose the wallet. |
| Summary | Per currency: wallets, owed to me, I owe, pending and **net**, with a breakdown per wallet and per person or group. |
| History | Transactions newest first, paginated, filterable by wallet, category, contact and date range. |

## Architecture

```text
Ledger.slnx
├── src
│   ├── BuildingBlocks                 Shared kernel (no business logic)
│   │   ├── Ledger.BuildingBlocks.Domain           DomainException, Currency, Money
│   │   ├── Ledger.BuildingBlocks.Application      CQRS interfaces (MediatR 12), validation, ICurrentUser, IEventBus
│   │   ├── Ledger.BuildingBlocks.Infrastructure   Module DbContext registration, outbox/inbox, RabbitMQ (MassTransit 8)
│   │   └── Ledger.BuildingBlocks.Presentation     ProblemDetails mapping, current user
│   ├── Modules
│   │   └── <Module>
│   │       ├── Ledger.Modules.<Module>.Domain          Entities and invariants
│   │       ├── Ledger.Modules.<Module>.Application     Commands, queries, handlers, validators, DTOs
│   │       ├── Ledger.Modules.<Module>.Infrastructure  DbContext, fluent configurations, migrations, consumers
│   │       ├── Ledger.Modules.<Module>.Contracts       Integration events: the only thing other modules may use
│   │       └── Ledger.Modules.<Module>.Presentation    Minimal API endpoints
│   ├── Ledger.Api        Host: composes the modules, JWT (Keycloak), OpenAPI + Scalar
│   ├── Ledger.Migrator   Applies the migrations of every module, then exits
│   └── Ledger.Console    Terminal client of the HTTP API (Keycloak login)
├── tests
│   ├── Ledger.UnitTests           Domain rules and split math
│   ├── Ledger.ArchitectureTests   Module boundaries and layer rules (NetArchTest)
│   └── Ledger.IntegrationTests    Whole API with PostgreSQL and RabbitMQ in Testcontainers
└── infra/keycloak/ledger-realm.json   Realm imported at startup
```

### Modules

| Module | Schema | Owns | Listens to |
| --- | --- | --- | --- |
| Identity | `identity` | Users mapped from the Keycloak `sub` claim (internal user id) | — |
| Accounts | `accounts` | Chart of accounts: wallet accounts, categories, system accounts | User registered, wallet created/renamed/archived/deleted |
| Wallets | `wallets` | Wallets and a projection of their balance | Wallet balance changed |
| Transactions | `transactions` | The ledger: transactions, postings, wallet balances, pending wallet assignments | Accounts, contacts, groups, ledger posting requested |
| Contacts | `contacts` | People of each user, linked to registered users by email | User registered, debt operation completed, group created/member added |
| Groups | `groups` | Groups and members | User registered, contacts, debt operation completed |
| Debts | `debts` | Loans, splits, settlements, who-owes-whom, group balances | Users, contacts, groups, wallets, ledger posting completed/rejected |

### How modules communicate

* **Outbox.** A module never publishes directly. `IEventBus.PublishAsync` stores the event in the module's `integration_outbox` table, in the same database transaction as the change. A background processor per module then sends it to RabbitMQ.
* **Inbox.** Each module has **one queue** (`ledger-<module>`) that holds all its consumers. Consumers derive from `ModuleConsumer`, which records the message id in the module's `inbox_messages` table in the same transaction as the handler's changes, so redelivered messages are ignored.
* **Eventual consistency.** Other modules learn about new data a few milliseconds later. Wallets expose `isReady` once the ledger knows them. A request that depends on data still being provisioned gets **503** with `Retry-After` (the console retries automatically).
* **Asynchronous operations.** Loans, splits and settlements affect several users' books, so the API answers **202 Accepted** with an operation. Debts asks Transactions to post the entries (`LedgerPostingRequested`), and Transactions answers `LedgerPostingCompleted`, or `LedgerPostingRejected` (for example when a wallet does not have enough money). Poll `GET /api/debts/operations/{id}`.

### Key modelling decisions

* **Wallet ↔ account.** Each wallet is backed by an Asset account in the ledger. The wallet balance is the sum of that account's postings, and the Wallets module keeps a projection for display and rules.
* **Currency per posting.** Categories and debt accounts accept any currency, while wallets hold one. Totals are shown per currency, with no conversion.
* **Debts use a counterparty.** Each user has one *Owed to me* (asset) and one *I owe* (liability) account. Every posting on them carries a `CounterpartyId`: a contact's person id, or a group id for group expenses. Lending 200 to Pedro is `Bank −200 / Owed to me [Pedro] +200`.
* **Person ids.** A registered user's person id is their user id. A person who is not registered gets an id when they are first added as a contact. A contact can be linked to a user later only while it is not used in any debt or group.

## Getting started

### Prerequisites

* [.NET 10 SDK](https://dotnet.microsoft.com/download)
* Docker (with Compose)
* Optional: `dotnet tool install --global dotnet-ef` to create migrations
* VS Code with the C# extension, to use the debug configurations

### Option A: run and debug from VS Code (recommended for development)

1. Open the folder in VS Code.
2. Pick one of these launch configurations and press F5:
   * **API + Console**: starts PostgreSQL, RabbitMQ and Keycloak (`docker compose up -d --wait`), applies the migrations, builds, then debugs the API and the console together.
   * **API**: same, API only. It opens Scalar at `http://localhost:5209/scalar`.
   * **Console** or **Migrator**: on their own.

Without VS Code:

```bash
docker compose up -d --wait postgres rabbitmq keycloak
dotnet run --project src/Ledger.Migrator
dotnet run --project src/Ledger.Api --launch-profile http     # http://localhost:5209
dotnet run --project src/Ledger.Console                       # in another terminal
```

### Option B: everything in containers

```bash
docker compose --profile full up -d --build
```

The migrator container runs once, then the API starts on `http://localhost:8081` (Scalar at `/scalar`). To debug it, use the **Docker: attach to API** launch configuration (the image ships `vsdbg`).

### Local services

| Service | URL | Credentials |
| --- | --- | --- |
| API (IDE) / API (container) | http://localhost:5209 / http://localhost:8081 | Keycloak login |
| Keycloak | http://localhost:8080 | admin / admin |
| RabbitMQ management | http://localhost:15672 | guest / guest |
| PostgreSQL | localhost:5432, database `ledger` | postgres / postgres |

The `ledger` realm is imported on Keycloak startup with two users, **alice / alice** and **bob / bob**. Use both to try linked contacts: log in once as each of them, then add the other as a contact using their email (`bob@ledger.local`). Clients: `ledger-api` (the token audience), `ledger-console` (PKCE, loopback redirect on port 7890) and `ledger-scalar`.

## HTTP API

All endpoints need a Keycloak access token. Explore them with Scalar at `/scalar` (Development).

| Method | Route | Description |
| --- | --- | --- |
| GET | `/api/me` | Current user (its `personId` identifies "me" in splits) |
| GET/POST/PATCH/DELETE | `/api/wallets`, `/api/wallets/{id}`, `POST /api/wallets/{id}/archive` | Wallets |
| GET/POST/PATCH | `/api/categories`, `POST /api/categories/{id}/archive` | Income and expense categories |
| GET | `/api/transactions?page&pageSize&walletId&categoryId&contactId&from&to` | History, newest first |
| GET | `/api/transactions/{id}` | One transaction with its postings |
| POST | `/api/transactions/income`, `/expense`, `/transfer` | Record money in, out, or between wallets |
| GET | `/api/summary` | Totals per currency, per wallet and per person or group |
| GET/POST | `/api/pending-assignments`, `/{id}/assign` | Choose the wallet for money others recorded for me |
| GET/POST | `/api/contacts`, `/{id}/link` | Contacts |
| GET/POST/DELETE | `/api/groups`, `/{id}`, `/{id}/members`, `/{id}/members/{personId}` | Groups |
| GET | `/api/groups/{id}/balances` | Balances, simplified payments and raw debts |
| GET | `/api/debts` | My debts and the net per contact |
| POST | `/api/debts/loans`, `/api/debts/splits`, `/api/debts/settlements` | Asynchronous: `202` plus an operation |
| GET | `/api/debts/operations/{id}` | `Pending`, `Completed` or `Rejected` (with the reason) |

Errors are RFC 7807 problem details. Business rule violations return `400` with a `code`, for example `ledger.insufficient_funds`.

## Console client

`Ledger.Console` is a client of the HTTP API. On start it opens the browser on the Keycloak login page (authorization code + PKCE) and receives the result on `http://127.0.0.1:7890/callback/`. Menus: summary, wallets, transactions, categories, contacts, groups, debts and splits, and pending wallet assignments. The API URL and Keycloak settings are in `src/Ledger.Console/appsettings.json`.

## Tests

```bash
dotnet test Ledger.slnx
```

* **Unit**: transaction balancing per currency, overdraft, split math (rounding, multiple payers, simplification), wallet, contact and group rules, ledger legs.
* **Architecture**: a module only references other modules' `Contracts`, the Domain is free of EF, MassTransit and ASP.NET, Application does not depend on infrastructure, and contracts are immutable.
* **Integration** (needs Docker): the real API with PostgreSQL and RabbitMQ in Testcontainers, and a test authentication scheme in place of Keycloak. It covers transfers and overdraft, a split mirrored to a linked user and settled, a rejected loan, and group simplification and settlement.

## Database migrations

Every module owns its migrations (`src/Modules/<Module>/Ledger.Modules.<Module>.Infrastructure/Migrations`). See [Migrations.md](Migrations.md).

## Known limitations and next steps

* Mirrored debts are applied automatically. Letting the other user cancel or dispute them is future work.
* Totals are not converted between currencies.
* A contact can only be linked to a user before it is used in debts or groups.
* MediatR 12 and MassTransit 8 are the last Apache-licensed versions. MassTransit 8 receives security fixes until the end of 2026.
