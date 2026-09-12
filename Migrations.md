# Database Migrations

This project uses **Entity Framework Core migrations** with PostgreSQL.

Each module owns its own `DbContext`, migrations and PostgreSQL schema.

## Database structure

```text
PostgreSQL
│
├── identity
│   ├── users
│   └── __EFMigrationsHistory
│
├── wallets
│   ├── wallets
│   └── __EFMigrationsHistory
│
└── ledger
    ├── transactions
    └── __EFMigrationsHistory
```

Each module is responsible for its own database changes.

---

# 1. When changing a module

Whenever you change an Entity Framework model, for example:

* Add a property
* Remove a property
* Rename a property
* Add a table
* Remove a table
* Add an index
* Change a relationship
* Change a column type
* Change constraints

you should create a new migration.

Example:

```bash
dotnet ef migrations add AddWalletDescription \
  --context WalletsDbContext \
  --project src/Modules/Wallets \
  --startup-project src/Ledger.Api
```

This creates a new migration inside the Wallets module.

---

# 2. Review the migration

Before applying it, inspect the generated migration.

Check that EF generated the change you actually intended.

For example:

```text
Migrations/
├── 20260912120000_InitialWallets.cs
├── 20260912121500_AddWalletDescription.cs
└── WalletsDbContextModelSnapshot.cs
```

Do not blindly apply a migration without checking it.

---

# 3. Apply the migration locally

Run:

```bash
dotnet ef database update \
  --context WalletsDbContext \
  --project src/Modules/Wallets \
  --startup-project src/Ledger.Api
```

EF will apply all pending migrations for that `DbContext`.

You should see something similar to:

```text
Applying migration '20260912121500_AddWalletDescription'.
Done.
```

If the database is already up to date:

```text
No migrations were applied. The database is already up to date.
```

---

# 4. Check migration status

To see the migrations for a module:

```bash
dotnet ef migrations list \
  --context WalletsDbContext \
  --project src/Modules/Wallets \
  --startup-project src/Ledger.Api
```

Example:

```text
20260912120000_InitialWallets
20260912121500_AddWalletDescription
```

---

# 5. Working with multiple DbContexts

This project has multiple `DbContext`s.

**Always specify the context.**

### Identity

```bash
dotnet ef migrations add MigrationName \
  --context IdentityDbContext \
  --project src/Modules/Identity \
  --startup-project src/Ledger.Api
```

```bash
dotnet ef database update \
  --context IdentityDbContext \
  --project src/Modules/Identity \
  --startup-project src/Ledger.Api
```

### Wallets

```bash
dotnet ef migrations add MigrationName \
  --context WalletsDbContext \
  --project src/Modules/Wallets \
  --startup-project src/Ledger.Api
```

```bash
dotnet ef database update \
  --context WalletsDbContext \
  --project src/Modules/Wallets \
  --startup-project src/Ledger.Api
```

### List available DbContexts

If you are unsure which contexts exist:

```bash
dotnet ef dbcontext list \
  --project src/Modules/Wallets \
  --startup-project src/Ledger.Api
```

---

# 6. Never modify an existing migration that has already been applied

Once a migration has been committed and applied to a shared environment, don't edit it.

Instead, create a new migration.

### Wrong

```text
InitialWallets
    ↓
Edit the migration
```

### Correct

```text
InitialWallets
    ↓
AddWalletDescription
    ↓
AddWalletCurrency
```

Each migration represents a step in the evolution of the database.

---

# 7. Removing the last migration

If you created a migration by mistake and **have not applied it**, you can remove it:

```bash
dotnet ef migrations remove \
  --context WalletsDbContext \
  --project src/Modules/Wallets \
  --startup-project src/Ledger.Api
```

Only use this for the most recent migration.

If the migration has already been applied to a shared environment, don't remove it. Create a new migration instead.

---

# 8. Rolling back locally

To roll the database back to a specific migration:

```bash
dotnet ef database update MigrationName \
  --context WalletsDbContext \
  --project src/Modules/Wallets \
  --startup-project src/Ledger.Api
```

For example:

```bash
dotnet ef database update InitialWallets \
  --context WalletsDbContext \
  --project src/Modules/Wallets \
  --startup-project src/Ledger.Api
```

To roll back all migrations:

```bash
dotnet ef database update 0 \
  --context WalletsDbContext \
  --project src/Modules/Wallets \
  --startup-project src/Ledger.Api
```

Use this only when working locally.

---

# 9. Creating a migration from scratch

If the database is disposable and you are still in early development, it can sometimes be easier to reset everything.

Stop the containers:

```bash
docker compose down -v
```

Start them again:

```bash
docker compose up -d
```

Then remove the module migrations:

```bash
rm -rf src/Modules/Wallets/Migrations
```

Create the migration again:

```bash
dotnet ef migrations add InitialWallets \
  --context WalletsDbContext \
  --project src/Modules/Wallets \
  --startup-project src/Ledger.Api
```

Apply it:

```bash
dotnet ef database update \
  --context WalletsDbContext \
  --project src/Modules/Wallets \
  --startup-project src/Ledger.Api
```

> **Warning:** `docker compose down -v` deletes the PostgreSQL volume and therefore all local database data.

Do not use this approach once the database contains data that needs to be preserved.

---

# 10. Verify the PostgreSQL schemas

You can connect to PostgreSQL:

```bash
docker exec -it ledger-postgres psql -U postgres -d ledger
```

List schemas:

```sql
\dn
```

List tables:

```sql
\dt identity.*
\dt wallets.*
```

Check migration history:

```sql
SELECT *
FROM identity."__EFMigrationsHistory";
```

```sql
SELECT *
FROM wallets."__EFMigrationsHistory";
```

Exit:

```sql
\q
```

---

# 11. Typical development workflow

When making a change to a module:

```text
1. Modify Entity / EF configuration
          ↓
2. Build the project
          ↓
3. Create migration
          ↓
4. Review migration
          ↓
5. Apply migration
          ↓
6. Test application
          ↓
7. Commit migration + model changes
```

Example:

```bash
dotnet build

dotnet ef migrations add AddWalletDescription \
  --context WalletsDbContext \
  --project src/Modules/Wallets \
  --startup-project src/Ledger.Api

dotnet ef database update \
  --context WalletsDbContext \
  --project src/Modules/Wallets \
  --startup-project src/Ledger.Api
```

---

# 12. Production

For production, don't rely on manually changing the database.

The migration should be part of the deployment process.

Before deploying, it is useful to generate the SQL script:

```bash
dotnet ef migrations script \
  --context WalletsDbContext \
  --project src/Modules/Wallets \
  --startup-project src/Ledger.Api
```

This allows the generated SQL to be reviewed before being executed against production.

Production deployments should be handled through the application's CI/CD process.

---

# Quick reference

## Create migration

```bash
dotnet ef migrations add MigrationName \
  --context <DbContext> \
  --project src/Modules/<Module> \
  --startup-project src/Ledger.Api
```

## Apply migrations

```bash
dotnet ef database update \
  --context <DbContext> \
  --project src/Modules/<Module> \
  --startup-project src/Ledger.Api
```

## List migrations

```bash
dotnet ef migrations list \
  --context <DbContext> \
  --project src/Modules/<Module> \
  --startup-project src/Ledger.Api
```

## Remove last migration

```bash
dotnet ef migrations remove \
  --context <DbContext> \
  --project src/Modules/<Module> \
  --startup-project src/Ledger.Api
```

## Generate SQL

```bash
dotnet ef migrations script \
  --context <DbContext> \
  --project src/Modules/<Module> \
  --startup-project src/Ledger.Api
```

## List DbContexts

```bash
dotnet ef dbcontext list \
  --project src/Modules/<Module> \
  --startup-project src/Ledger.Api
```

---

## Golden rule

> **A migration is a versioned description of how the database changes over time.**
>
> Change the model → create a migration → review it → apply it.
>
> Don't manually modify the database to make it match the model.
