# Ledger — guide for Claude

Personal finance app: wallets, income/expenses, transfers, loans, splits, groups and settlements, on a **double-entry ledger**. .NET 10 **modular monolith** (API + console) + **Angular** web client, Keycloak, PostgreSQL, RabbitMQ.

Deeper references (read them before big changes): `documentation/README.md` (decisions, accounting model, flows, problems solved; sections 13–15 cover the web client, dates/reports and edits), `Migrations.md`, `README.md`, `clients/web/README.md`.

## Working with the user

* **Do not assume.** When a requirement is ambiguous (behaviour, UX, accounting rule, naming), ask before building and offer options with trade-offs. Then summarize the decisions in a plan.
* When the user says "Don't do anything else", do exactly what was asked and nothing more.
* Don't inspect or change the user's shell configuration (dotfiles, nvm, etc.) without asking.
* Never commit `clients/web/angular.json`: it has the user's local `"analytics": false`.
* Work on a feature branch created from the branch the user names; PRs go into that base branch. `gh` is not installed: build a prefilled `https://github.com/gastonmastra/ledger/compare/<base>...<branch>?expand=1&title=…&body=…` link and copy it with `pbcopy` when asked.
* Commits: one per area (backend module, web, console, docs), conventional style (`feat(debts): …`, `fix(web): …`, `docs: …`).
* Every feature ships to **API, web (EN + ES) and console** unless the user says otherwise, plus docs (`documentation/README.md` new section, root README API table, web README).

## Commands

```bash
docker compose up -d                                  # postgres 17, rabbitmq 4.2, keycloak 26 (+ migrator)
docker compose --profile full up -d --build           # also api + web containers (ALWAYS --build after code changes)
dotnet run --project src/Ledger.Migrator              # apply all module migrations
dotnet run --project src/Ledger.Api --launch-profile http   # API on :5209, Scalar at /scalar
dotnet run --project src/Ledger.Console               # console client (browser login, loopback :7890)
dotnet build Ledger.slnx && dotnet test Ledger.slnx   # unit + architecture + integration (Testcontainers, needs Docker)

cd clients/web
npm start                                             # ng serve :4200, proxies /api to :5209 (proxy.conf.json)
npm test -- --watch=false                             # Vitest via the Angular builder (bare `npx vitest` does not work)
npm run build                                         # production, EN + ES, with the service worker; fails on missing translations
npx ng extract-i18n --output-path src/locale          # then add the new ids to messages.es.xlf
npm run e2e                                           # Playwright; needs API + Keycloak; logs in as alice/bob

cd infra/terraform && terraform plan                  # AWS infrastructure (applied by hand, never by CI)
```

Production now: **one VPS** (documentation §19, `deploy/vps/`): Docker Compose with Caddy (Let's Encrypt, Keycloak admin allowlist), images from GHCR (`ghcr.io/gastonmastra/ledger-<name>:<sha>`), deployed on every push to `main` by `.github/workflows/deploy-vps.yml` (CI → images → SSH → `deploy.sh <sha>`: migrator, then the stack), nightly `backup.sh` to Hetzner Object Storage. Data from another environment: `import-dump.sh <dump> <email>=<keycloak id>…` (replaces the DB, remaps `identity.users."ExternalId"`), later users with `link-user.sh` before their first login (§19.8). Copy of the VPS on the laptop (both `ledger` and `keycloak` dumps from `backup.sh`, no remapping): §19.9. New API settings go in `deploy/vps/docker-compose.yml` (and `.env.example` for secrets).

AWS deployment (documentation §18, kept for the future, deployed **by hand only**): eu-west-1, ECS Fargate + RDS PostgreSQL + RabbitMQ on EFS + Keycloak (`infra/keycloak-prod`, realm without users, invite only) behind one ALB at `ledger.<domain>` / `auth.<domain>`. *Actions → Deploy (AWS) → Run workflow* runs `.github/workflows/deploy-aws.yml` (CI → arm64 images to ECR as `<sha>` + `latest` → db-init + migrator tasks → force new deployment). The web app reads the Keycloak issuer from `/config.json` at startup (`public/config.json` in dev, nginx `KEYCLOAK_ISSUER` in the container).

Keycloak realm `ledger` (`infra/keycloak`), users `alice`/`alice` and `bob`/`bob`. Clients: `ledger-web` (redirects :4200 and :4201 only), `ledger-console` (127.0.0.1:7890), `ledger-scalar`.

## Architecture

```
src/
  BuildingBlocks/   Domain (Money, Currency, DomainException) · Application (ICommand/IQuery, behaviors,
                    ValidationRules.NotInFuture, exceptions, PagedResult) · Infrastructure (module DbContext,
                    outbox/inbox, ModuleConsumer, UTC conventions) · Presentation (ICurrentUser, problem details)
  Modules/<Module>/ Ledger.Modules.<M>.Domain | .Application | .Infrastructure | .Contracts | .Presentation
  Ledger.Api        composition root (Program.cs registers every module + one bus)
  Ledger.Migrator   startup project for `dotnet ef` and the migrations runner
  Ledger.Console    HTTP client of the API (it knows only the HTTP contracts, never module code)
clients/web         Angular 22 standalone + signals + zoneless, Tailwind 4, CDK dialogs, Chart.js
tests/              UnitTests · ArchitectureTests (NetArchTest) · IntegrationTests (real API + Testcontainers)
```

Modules: **Identity** (Keycloak `sub` → internal user, `ledger_user_id` claim via provisioning middleware), **Accounts** (categories / account catalogue), **Wallets**, **Transactions** (the double-entry ledger, reports, summary, lock date), **Contacts**, **Groups**, **Debts** (loans, existing debts, splits, settlements, change confirmations), **Investments** (assets, holdings at average cost, append-only trades posted through `InvestmentPostingRequested`, manual prices; valued at the latest sell price outside the ledger — documentation §16).

### Layer rules (enforced by `tests/Ledger.ArchitectureTests`)
* A module references **only the `.Contracts`** of other modules (integration events). Never another module's Domain/Application/Infrastructure, and never another module's tables or schema.
* Contracts depend on nothing; integration events are immutable `record`s.
* Domain: no EF, no ASP.NET, no MediatR. Rules live here and throw `DomainException(code, message)`.
* Application: CQRS with MediatR (`ICommand`/`IQuery` + handlers + FluentValidation validators); talks to persistence through `I<Module>DbContext`; no Infrastructure or web references.
* Presentation: minimal API endpoints only (`<Module>Endpoints.cs`), request records → commands; no DbContext.

### Persistence
* PostgreSQL schema **per module**, own `DbContext`, own `__EFMigrationsHistory`, **fluent API only** (`IEntityTypeConfiguration<T>`), enums stored as strings, money `HasPrecision(19, 4)`.
* `ConfigureConventions` → `UseUtcDateTimeOffsets()` in every DbContext (Npgsql rejects non-UTC offsets).
* Wallet rows use `xmin` as row version (concurrent balance events).

### Messaging (one RabbitMQ bus, a queue per module `ledger-<module>`)
* Publish with `I<Module>EventBus.PublishAsync` → writes to the module's own **`integration_outbox`** in the same transaction; `OutboxProcessor` publishes (MessageId = outbox row id).
* Consume with a `ModuleConsumer<TMessage, TDbContext>` subclass → own **`inbox_messages`** (dedupe) + one DB transaction. Consumers just `sender.Send(command)`. Register them in `<Module>Module.AddModuleMessaging(...)`.
* Retries: exponential, ~3 min. Throw `NotYetAvailableException` when something hasn't arrived yet (the API turns it into 503 + `Retry-After`; clients retry).
* Cross-module data is read from **local projections** (`known_*` tables) kept up to date by events — never by querying another module.

### Accounting model (Transactions)
* Every `Transaction` has postings that **balance per currency**. Accounts: wallets, categories, Receivables/Payables (with a **counterparty**: person or group), Opening balances, Currency exchange, Uncategorized (income), Pending settlements, Balance adjustments.
* **A wallet never goes negative.** `LedgerWriter` checks the **final** balance per wallet over the whole batch and locks accounts; always write through it.
* **Posted transactions are never modified or deleted.** Changes = reversal (same date, negated postings, `ReversalOfId`) + new revision (`EntryId`, `Revision`, `ChangeAction`, required reason). "Current" = not reversed and not a reversal. Reports count income/expenses from **current** transactions only; balances/net worth use every posting.
* **Lock date** (`books_locks`): nothing dated on or before it can be written for that owner (`books.locked`).
* Debts asks the ledger through contracts: `LedgerPostingRequested` (new operation, legs per registered user) and `LedgerRevisionRequested` (edit/delete/restore). Answers: `...Completed` / `...Rejected`. A `LedgerLeg` must satisfy: cash + expense − income + openingBalance + positions = 0.
* Existing debts are **neither income nor expense** (Opening balances). Balance adjustments are excluded from income/expense.

### Debts operations
* Loans, splits, settlements and existing debts are **asynchronous**: the API answers `202` with an operation (`Pending → Completed | Rejected`); clients poll (`OperationTracker` in web, `WaitForOperationAsync` in console).
* Operations are built by `OperationFactory` **from the initiator's point of view** (their contacts, wallets, category). Changes are `OperationChange`s: linked users involved approve; the author and those who accept get the new version, rejecters keep the old one (operation `IsDisputed`). Paid debts (fully or partly) must have their payments changed or undone first (`debt.settled`).
* Non-registered contacts have no books: only registered users get ledger legs.
* **Payments** (outside groups) pay debts **oldest first** (`PaymentAllocation`, documentation §20): mutual debts cancel out, any extra becomes a debt the other way; `debt_payments` rows record what each payment paid and `Debt.Paid` what is left. Settlements with scope `Payment`; groups still use `GroupPair`. Paid loans/splits/existing debts are blocked until their payments change.
* Linking a contact that is already in use is a **link request** the user accepts; it merges the person id into the user (Debts re-keys, Transactions keeps the old id as a counterparty alias) and copies the shared history into the user's books (documentation §17).

### Web client (`clients/web/src/app`)
* `core/api/ledger-api.ts` (typed client) + `core/api/models.ts` (mirror of API DTOs, enums as string unions). `core/api/error-messages.ts` maps API error `code`s to localized text — add new codes there.
* `ui/` is the UI kit (import only from `ui/index.ts`): Button, Card, Dialog (CDK; width via `DialogService` `maxWidth`, not Tailwind), FormField, Input, DateTimeInput (value = UTC ISO), ReasonField, ConfirmService (`confirm`, `askReason`), Table, Badge, Chart…
* Colors: `src/styles/tokens.css` (palette) and `theme.css` (semantic light/dark tokens). Use semantic classes (`bg-surface`, `text-muted`, `text-danger`), never raw colors.
* Data loading: `rxResource` keyed on `DataChanges.version()`; after a change call `DataChanges.notify()` (reloads now and again 1.5 s later for eventual consistency).
* i18n: every user-visible string uses `i18n="@@id"` / `$localize\`:@@id:…\``. Same `@@id` must always have the same English text. Placeholders in templates: `{{ x // i18n(ph="name") }}`.
* Features live in `features/<area>/`; shared operation actions in `features/debts/operation-actions.ts`.
* Rows with several actions: `uiRowAction` + a detail pop-up that closes with the action, plus `ui-actions-menu` (⋯) with the same `MenuAction`s; operations use `OperationActions.open()/menu()`. Filters go in a `DialogService.open(…, 'panel')` side panel with `ui-filter-chips`.
* **PWA** (documentation §21): build with `npm run build` (adds `--service-worker`; don't add it to `angular.json`). `public/sw.js` must keep leaving cross-origin (Keycloak) requests to the browser; never add API data groups to `ngsw-config.json`. Tokens: `core/auth/device-session.ts` (Remember me → localStorage + `offline_access`); call `oauth.setStorage()` when switching storage.

### Console (`src/Ledger.Console`)
* `Api/LedgerApiClient.cs` + `Api/Models.cs` (copies of the HTTP contracts), menus in `Menus/` deriving from `Menu`, prompts via `ConsoleUi` (`ReadWhen` → UTC, `ReadReason`, `ReadTextOrKeep`, `Choose`). Register new menus in `Program.cs` and `App.cs`.

## Adding a feature (checklist)

1. **Clarify** the behaviour with the user; agree the accounting effect (postings per user) for anything that moves money.
2. **Domain**: entity/value changes and rules (`DomainException` with a stable `code` like `area.reason`).
3. **Application**: command/query + validator (`NotInFuture` for dates, max lengths) + handler. Writing to the ledger → `LedgerWriter`. Cross-module effect → publish an integration event (add the record to the right `.Contracts`).
4. **Infrastructure**: EF configuration, consumers (registered in the module), then the migration:
   `dotnet ef migrations add <Name> --context <Module>DbContext --project src/Modules/<M>/Ledger.Modules.<M>.Infrastructure --startup-project src/Ledger.Migrator --output-dir Migrations`
   Backfill existing rows with `migrationBuilder.Sql(...)` when new columns need values; test it on a **copy** of the dev DB.
5. **Presentation**: endpoint + request record; `.WithSummary(...)`; async operations return `202`.
6. **Tests**: unit tests for rules, an integration test for the flow (`tests/Ledger.IntegrationTests`, use `EventuallyAsync`/`WalletBalanceAsync` waits — balances update asynchronously), keep architecture tests green.
7. **Web**: models, API method, UI (reuse the UI kit), error codes, `ng extract-i18n` + Spanish in `messages.es.xlf`, `npx ng build` (both locales), unit specs, Playwright E2E if it is a user flow.
8. **Console**: client method, models, menu option.
9. **Docs** and per-area commits.

## Don't (things that break the infra)

* Don't reference another module's internals, share a DbContext, or query another schema — use Contracts + events + local projections.
* Don't publish to RabbitMQ directly or bypass the outbox/inbox; don't add a second bus or switch to MassTransit's EF outbox/inbox (not multi-bus capable; its inbox caches SQL per type — see documentation §9).
* Don't edit or delete posted transactions/postings, and don't write ledger rows outside `LedgerWriter`.
* Don't edit an applied migration or the model snapshot by hand; add a new migration. Don't use data annotations for mapping.
* Don't store non-UTC `DateTimeOffset`s; don't accept future dates for operations.
* Don't rename/remove fields of integration events in place: queued messages and outbox rows still use the old shape. Add optional fields with defaults.
* Don't make a handler assume another module's data already arrived — throw `NotYetAvailableException`.
* Don't change Keycloak client ids, ports (API 5209, web 4200/4201, Keycloak 8080, console callback 7890) or the realm import without updating `infra/keycloak`, `infra/keycloak-prod`, `proxy.conf.json`, `public/config.json`, `app.settings.ts`, appsettings, `infra/terraform` and docs.
* New API settings or secrets for production go in `deploy/vps/docker-compose.yml` / `.env.example` (VPS) and `infra/terraform` (AWS), not only in appsettings.
* Don't hard-code English text in the web UI or reuse an `@@id` with different text (the ES build fails).
* Don't run tests or experiments against the user's `ledger` database or the default RabbitMQ vhost while their API may be running.

## Verifying against a real stack without touching the user's data

Use a throwaway database + vhost + API port (e.g. 5219), then delete them:
```bash
docker exec ledger-postgres psql -U postgres -c "CREATE DATABASE ledger_scratch;"
docker exec ledger-rabbitmq rabbitmqctl add_vhost ledger-scratch && docker exec ledger-rabbitmq rabbitmqctl set_permissions -p ledger-scratch guest ".*" ".*" ".*"
ConnectionStrings__Database="Host=localhost;Database=ledger_scratch;Username=postgres;Password=postgres" dotnet run --project src/Ledger.Migrator
ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://localhost:5219 ConnectionStrings__Database=... ConnectionStrings__RabbitMq=amqp://guest:guest@localhost:5672/ledger-scratch dotnet run --no-launch-profile --project src/Ledger.Api
# web: ng serve --port 4200 --proxy-config <copy of proxy.conf.json pointing to 5219>
# cleanup: DROP DATABASE ledger_scratch WITH (FORCE); rabbitmqctl delete_vhost ledger-scratch; kill the processes
```
The console opens the browser with `open` on macOS: for scripted runs put a no-op `open` first in `PATH` and log in with headless Playwright at the printed URL.

## Known gotchas

* After pulling changes, rebuild containers (`--build`) — old images cause 500s (e.g. unknown enum values like `BalanceAdjustments`) and dead-lettered messages in `ledger-<module>_error` queues.
* VS Code web tasks run in an interactive login zsh (`-i -l -c`) so the right Node version (nvm) is used; Angular needs Node ≥ 24.15.
* Balances, contacts links and projections are eventually consistent: in tests and clients wait/retry instead of asserting immediately.
* CDK dialogs ignore Tailwind `max-w-*` (unlayered runtime CSS): pass the size to `DialogService.open`.
* Existing data needs backfills when adding columns (see the `TransactionRevisions` and `OperationRevisions` migrations for examples).
