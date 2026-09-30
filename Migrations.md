# Database migrations

Ledger uses **Entity Framework Core migrations** with PostgreSQL. Every module owns:

* its `DbContext` (in `Ledger.Modules.<Module>.Infrastructure`), configured only with the fluent API (`IEntityTypeConfiguration<T>` classes);
* its PostgreSQL **schema**;
* its `__EFMigrationsHistory` table, inside that schema;
* its migrations, in `src/Modules/<Module>/Ledger.Modules.<Module>.Infrastructure/Migrations`.

`src/Ledger.Migrator` is the startup project for every `dotnet ef` command. It is also the program that applies the migrations: run it locally, or as the `migrator` container in docker compose.

## Database structure

```text
PostgreSQL database "ledger"
├── identity       users
├── accounts       accounts
├── wallets        wallets
├── transactions   transactions, postings, ledger_accounts, pending_wallet_assignments, counterparties
├── contacts       contacts, registered_users
├── groups         groups, group_members, known_contacts, known_users
└── debts          operations, operation_payers, operation_shares, debts, known_*
```

Every schema also has `__EFMigrationsHistory`, `integration_outbox` (events waiting to be published) and `inbox_messages` (messages already handled).

| Module | DbContext | Project |
| --- | --- | --- |
| Identity | `IdentityDbContext` | `src/Modules/Identity/Ledger.Modules.Identity.Infrastructure` |
| Accounts | `AccountsDbContext` | `src/Modules/Accounts/Ledger.Modules.Accounts.Infrastructure` |
| Wallets | `WalletsDbContext` | `src/Modules/Wallets/Ledger.Modules.Wallets.Infrastructure` |
| Transactions | `TransactionsDbContext` | `src/Modules/Transactions/Ledger.Modules.Transactions.Infrastructure` |
| Contacts | `ContactsDbContext` | `src/Modules/Contacts/Ledger.Modules.Contacts.Infrastructure` |
| Groups | `GroupsDbContext` | `src/Modules/Groups/Ledger.Modules.Groups.Infrastructure` |
| Debts | `DebtsDbContext` | `src/Modules/Debts/Ledger.Modules.Debts.Infrastructure` |

## Apply migrations

```bash
# Local (uses src/Ledger.Migrator/appsettings.json → localhost:5432)
dotnet run --project src/Ledger.Migrator

# Or with docker compose (the API waits for it in the "full" profile)
docker compose up migrator
```

The migrator applies every module in turn and exits with a non-zero code if something fails.

## Create a migration

After changing an entity or its configuration in a module:

```bash
dotnet ef migrations add AddWalletDescription \
  --context WalletsDbContext \
  --project src/Modules/Wallets/Ledger.Modules.Wallets.Infrastructure \
  --startup-project src/Ledger.Migrator \
  --output-dir Migrations
```

Always review the generated `Up`/`Down` methods before applying the migration. In particular, check for renames that EF Core interpreted as drop + add.

## Other useful commands

```bash
# List the migrations of a module and whether they are applied
dotnet ef migrations list --context WalletsDbContext \
  --project src/Modules/Wallets/Ledger.Modules.Wallets.Infrastructure --startup-project src/Ledger.Migrator

# Remove the last migration (only if it was not applied anywhere)
dotnet ef migrations remove --context WalletsDbContext \
  --project src/Modules/Wallets/Ledger.Modules.Wallets.Infrastructure --startup-project src/Ledger.Migrator

# Roll a module back to a given migration
dotnet ef database update InitialCreate --context WalletsDbContext \
  --project src/Modules/Wallets/Ledger.Modules.Wallets.Infrastructure --startup-project src/Ledger.Migrator

# Generate an idempotent SQL script to review or run in production
dotnet ef migrations script --idempotent --context WalletsDbContext \
  --project src/Modules/Wallets/Ledger.Modules.Wallets.Infrastructure --startup-project src/Ledger.Migrator
```

## Reset the local database

This deletes all local data:

```bash
docker compose down -v                                 # removes the PostgreSQL volume
docker compose up -d --wait postgres rabbitmq keycloak
dotnet run --project src/Ledger.Migrator
```

## Inspect the schemas

```bash
docker exec -it ledger-postgres psql -U postgres -d ledger
```

```sql
\dn                                            -- schemas
\dt transactions.*                             -- tables of a module
SELECT * FROM wallets."__EFMigrationsHistory"; -- applied migrations of a module
```

## Production

Do not run migrations from the API. Run the migrator (or reviewed SQL scripts generated with `--idempotent`) as a step of the deployment pipeline, before starting the new version of the API.
