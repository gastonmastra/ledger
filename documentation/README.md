# Ledger — project documentation

This is the reference for **why** Ledger is built the way it is. It records the requirements, every design decision (with the options considered and their trade-offs), the accounting model, how the modules talk to each other, the problems found while building the MVP and how they were solved, and how everything was verified.

For a quick start see the [root README](../README.md). For database migrations see [Migrations.md](../Migrations.md).

## Contents

1. [Requirements](#1-requirements)
2. [Decision log](#2-decision-log)
3. [Architecture](#3-architecture)
4. [Accounting model](#4-accounting-model)
5. [Module communication](#5-module-communication)
6. [Feature walkthroughs](#6-feature-walkthroughs)
7. [Local environment, running and debugging](#7-local-environment-running-and-debugging)
8. [Testing and verification](#8-testing-and-verification)
9. [Problems found while building the MVP](#9-problems-found-while-building-the-mvp)
10. [Troubleshooting](#10-troubleshooting)
11. [Known limitations and next steps](#11-known-limitations-and-next-steps)
12. [Reference: versions, conventions, commits](#12-reference)
13. [Web client](#13-web-client)
14. [Dates, opening balances, adjustments, existing debts and reports](#14-dates-opening-balances-adjustments-existing-debts-and-reports)
15. [Editable, auditable ledger](#15-editable-auditable-ledger)
16. [Investments](#16-investments)
17. [Linking contacts that are already in use](#17-linking-contacts-that-are-already-in-use)
18. [Deployment (AWS)](#18-deployment-aws)
19. [Deployment (VPS)](#19-deployment-vps)
20. [Partial payments](#20-partial-payments)
21. [Web app: grouped actions, detail pop-ups and installable app](#21-web-app-grouped-actions-detail-pop-ups-and-installable-app)

---

## 1. Requirements

The MVP request, as given:

* **Modules:** Transactions, Accounts, Wallets, Identity. Any extra module had to be proposed and approved; Contacts, Debts and Groups were approved.
* **Persistence:** PostgreSQL. Every module uses **its own schema**, every change is tracked with **EF Core migrations**, and models are configured with the **fluent API**.
* **Architecture:** **clean architecture with CQRS** (MediatR allowed). Modules must be **loosely coupled** so they can later **scale separately**.
* **Features:**
  * transfer money from one wallet to another;
  * lend money to people and see how much I am owed;
  * see the user's total money across every wallet and everything they have;
  * list transactions ordered by time;
  * split transactions, e.g. I pay something and a friend automatically owes me their part.
* **Access:** both a **console app** and an **HTTP API**.
* **Users:** handled by **Keycloak**.
* **Environment:** everything runs locally with **docker compose**, with a way to **run and debug** it.
* **Process:** no assumptions; every open question goes to the product owner.

## 2. Decision log

Every product and architecture question was asked before implementation. The answers below are binding for the design.

### 2.1 Product decisions

| # | Question | Decision | Consequences |
| --- | --- | --- | --- |
| P1 | How do wallets relate to the ledger? | **A wallet is backed by an Asset account.** Wallets stays a module of its own. | See [2.3](#23-trade-off-wallet-backed-by-an-account-vs-merging-wallets-into-accounts). The wallet balance is the sum of its account's postings. |
| P2 | Currencies (EUR, USD, ARS) in transfers and totals? | First "manual exchange rates", refined to **the rate is typed per transfer only**. | A cross-currency transfer needs the received amount. Totals are shown **per currency, with no conversion**. |
| P3 | Who are the "friends"? | **Both**: contacts (plain names) that can be **linked to a registered user**. | New **Contacts** module. |
| P4 | Is a debt with a linked user mirrored into their books? | **Yes, applied automatically.** Cancelling or disputing it is future work. | Contacts that are missing on the other side are created automatically (P13). |
| P5 | Split types | **Equal, custom amounts, percentages, "a friend paid for me".** | — |
| P6 | How is a debt settled? | ~~Full settlement only~~. Since §20: **payments of any amount**, applied to the oldest debts first (mutual debts cancel out, any extra becomes a debt the other way). Groups still pay their suggested payments. | Partially paid debts. |
| P7 | What happens after a debt with a linked user is settled? | Their side is updated too, and a **"choose the wallet" item** is created so the money lands in (or leaves) one of their wallets. | *Pending wallet assignments* ([4.6](#46-pending-wallet-assignments)). |
| P8 | Who can pay a split, and who is tracked? | **Any payer, all pairs tracked** (Splitwise-like). | Debts between two non-registered contacts live only in the Debts module. |
| P9 | Groups? | **Yes**, as a **separate Groups module**, **shared** with every member who is a registered user. | — |
| P10 | Simplify debts? | **Yes, within a group.** | Greedy minimum-payments algorithm per currency. |
| P11 | Several payers on one expense? | **Yes.** | Debts are allocated in proportion to what each payer advanced, exact to the cent. |
| P12 | Money in and out besides transfers | **Opening balance, income, expense.** | — |
| P13 | Categories | **The user creates all of them.** An expense with no category goes to an automatic **Uncategorized** account. | Income with no category goes to **Uncategorized income** (by symmetry, see I4). |
| P14 | Can a wallet go below zero? | **No, reject.** | Enforced by row locks in the ledger ([5.5](#55-consistency-rules)). |
| P15 | Transaction list | **Paginated**, filtered by **wallet, date range, contact and category**, newest first. | — |
| P16 | "Total money" | **All four figures per currency**: wallets, owed to me, I owe, net. | *Pending* was added as a fifth figure so the net stays correct (see [4.7](#47-summary)). |
| P17 | Wallet lifecycle | **Rename, archive (only with a zero balance), hard delete (only with no transactions).** | — |
| P18 | Debt representation in the ledger | **One Receivables and one Payables account per user, plus a counterparty tag on each posting**, instead of one account per contact. | See [4.3](#43-debts-counterparty-dimension). |

### 2.2 Technical decisions

| # | Topic | Decision |
| --- | --- | --- |
| T1 | CQRS dispatcher | **MediatR 12.5** (the last Apache-licensed version; v13+ needs a license key). |
| T2 | Module communication | **RabbitMQ + transactional outbox** from day one (MassTransit 8.5, the last Apache version). |
| T3 | Layering | **One project per layer and module**: Domain, Application, Infrastructure, Contracts, Presentation. |
| T4 | Endpoints | **Inside each module** (`Map<Module>Endpoints()`); `Ledger.Api` only composes them. |
| T5 | User id stored by modules | The **internal Identity `User.Id`**. Keycloak's `sub` is mapped on the first request. |
| T6 | Console access | **HTTP client of the API**, logging in with **authorization code + PKCE** through the browser and a localhost callback. |
| T7 | Currency in the ledger | **Stored on every posting.** A transaction must balance in each currency. |
| T8 | Docker and debugging | **Both**: infrastructure in compose while debugging the API and console from VS Code (with a compound launch), **and** a `full` profile that also runs the API in a container, with an attach configuration. |
| T9 | Migrations | A **separate migrator** (a console app and a one-shot container). Existing migrations were **reset** to a fresh `InitialCreate` per module. |
| T10 | Keycloak | A realm **imported at startup** with the clients and **two test users** (alice, bob). |
| T11 | API docs | Built-in **OpenAPI + Scalar UI** with a Keycloak login. |
| T12 | Tests | **Unit, integration (Testcontainers) and architecture (NetArchTest)** tests. |

Implementation choices made without a question (listed in the plan and accepted with it):

| # | Choice |
| --- | --- |
| I1 | **FluentValidation 11** (Apache) as a MediatR pipeline behaviour. |
| I2 | **IdentityModel.OidcClient 6** (Apache) for the console login. |
| I3 | The old unauthenticated `POST /api/transactions` (raw postings) was **removed**. Every ledger write goes through a use case, so ownership and the no-overdraft rule always apply. |
| I4 | Income with no category goes to an **Uncategorized income** account. |
| I5 | Operations that touch several users' books (loan, split, settlement) are **asynchronous**: `202 Accepted` plus an operation whose status is `Pending`, then `Completed` or `Rejected`. |
| I6 | Money recorded for a linked user whose wallet is unknown waits in a **Pending settlements** clearing account. |
| I7 | **Group expenses use the group as the counterparty** in the ledger, not each person ([4.4](#44-group-expenses)). |
| I8 | A contact can be **linked to a user later only while it is unused**, because linking changes its person id ([4.5](#45-people-and-person-ids)). |

### 2.3 Trade-off: wallet backed by an account vs merging wallets into accounts

Both options store money as an Asset account in the double-entry ledger; the question is whether "wallet" is a module of its own.

| | Option 1: Wallets module; each wallet owns a ledger account (**chosen**) | Option 3: no Wallets module; a wallet is an Asset account with owner and currency |
| --- | --- | --- |
| Pros | Wallet-specific concerns (currency, owner, archiving, and later icons, limits or bank sync) stay out of the generic `Account`. It matches the requested module list and can scale separately. Accounts stays a pure accounting concept. | Simpler: one entity, one module, one write per operation, no cross-module consistency problem. Less code. |
| Cons | Two records per wallet across two modules and two schemas must be kept consistent (solved with events). More code, and a mapping from wallet id to account id. | Contradicts the module list. `Account` gets bloated. Splitting wallets out later needs a data migration across schemas. |

### 2.4 Trade-off: message bus library

A bus library provides (1) a transactional outbox, (2) consumer hosting, (3) retries and dead-lettering, (4) idempotency (an inbox) and (5) topology and serialization.

| Option | Pros | Cons |
| --- | --- | --- |
| **MassTransit 8.x** (**chosen**) | Free (Apache 2.0) and the de-facto .NET standard. Good docs. Changing the broker is a config change. | Frozen: security fixes only, until the end of 2026. Lots of "magic". **Its EF outbox/inbox turned out not to support one DbContext per module in one process** ([9.1](#91-masstransit-8-bus-outbox-does-not-support-one-bus-per-module)). |
| MassTransit 9+ | Supported and evolving. | Commercial license. |
| Wolverine | MIT, actively developed, outbox and inbox. Could replace MediatR too. | Two dispatching styles if kept next to MediatR. Different conventions, smaller community. |
| Own outbox + RabbitMQ.Client | No framework, full control. | You own everything (outbox, workers, retries, dedup, reconnection): about 500–1000 lines of subtle code. |

Final result: MassTransit 8 handles the **transport, consumers and retries**. The **outbox and inbox are implemented in the building blocks** (about 200 lines) because of the limitations found in [9.1](#91-masstransit-8-bus-outbox-does-not-support-one-bus-per-module) and [9.2](#92-masstransit-8-ef-inbox-caches-its-sql-per-entity-type).

## 3. Architecture

### 3.1 Solution layout

```text
Ledger.slnx
├── Directory.Build.props / Directory.Packages.props   net10.0, nullable, central package versions
├── src
│   ├── BuildingBlocks
│   │   ├── Ledger.BuildingBlocks.Domain          DomainException, Currency (EUR/USD/ARS), Money (2 decimals)
│   │   ├── Ledger.BuildingBlocks.Application     ICommand/IQuery/handlers over MediatR, ValidationBehavior,
│   │   │                                         LoggingBehavior, IEventBus, ICurrentUser, PagedResult,
│   │   │                                         NotFound/Forbidden/Conflict/NotYetAvailable exceptions
│   │   ├── Ledger.BuildingBlocks.Infrastructure  AddModuleDbContext, InTransactionAsync, Messaging/
│   │   │                                         (OutboxMessage, OutboxProcessor, ModuleEventBus,
│   │   │                                          InboxMessage, ModuleConsumer, AddModuleMessaging,
│   │   │                                          AddLedgerMessageBus, AddMessagingTables)
│   │   └── Ledger.BuildingBlocks.Presentation    CurrentUser (from the ledger_user_id claim),
│   │                                             ExceptionHandler (→ RFC 7807), AddPresentationCore
│   ├── Modules/<Module>/Ledger.Modules.<Module>.{Domain,Application,Infrastructure,Contracts,Presentation}
│   ├── Ledger.Api          Composes the modules: Keycloak JWT, user provisioning, OpenAPI + Scalar, /health
│   ├── Ledger.Migrator     Applies every module's migrations; startup project for `dotnet ef`
│   └── Ledger.Console      HTTP client of the API with a Keycloak PKCE login and menus
├── tests/Ledger.{UnitTests,ArchitectureTests,IntegrationTests}
├── infra/keycloak/ledger-realm.json
├── docker-compose.yml, src/Ledger.Api/Dockerfile, src/Ledger.Migrator/Dockerfile
└── .vscode/{tasks,launch}.json
```

### 3.2 Layer rules (enforced by the architecture tests)

| Layer | May depend on | Must not depend on |
| --- | --- | --- |
| Domain | BuildingBlocks.Domain | EF Core, MassTransit, MediatR, ASP.NET, other layers |
| Application | Domain, own Contracts, BuildingBlocks.Application, EF Core abstractions (`DbSet`) | MassTransit, Npgsql, ASP.NET, Infrastructure, Presentation |
| Infrastructure | Application, BuildingBlocks.Infrastructure, **other modules' Contracts** | other modules' Domain/Application/Infrastructure/Presentation |
| Presentation | Application, BuildingBlocks.Presentation | EF Core, MassTransit, Infrastructure |
| Contracts | nothing (plain immutable records) | anything from Ledger, EF, MassTransit or MediatR |

Across modules, **only Contracts** (integration events) may be referenced. There is one deliberate case where an Application layer references another module's contracts: **Debts.Application → Transactions.Contracts**, to publish `LedgerPostingRequested`, which is the ledger's input contract.

Application handlers access data through an interface of their module (`IWalletsDbContext`, …) that exposes `DbSet`s and is implemented by the Infrastructure DbContext. Operations that need raw SQL (row locks) are methods on that interface, implemented in Infrastructure.

### 3.3 Modules

| Module | Schema | Owns | Publishes | Consumes |
| --- | --- | --- | --- | --- |
| **Identity** | `identity` | `users` (ExternalId = Keycloak `sub`, Email, DisplayName) | `UserRegistered` | — |
| **Accounts** | `accounts` | Chart of accounts: wallet accounts, categories, 7 system accounts per user | `AccountCreated` (with the opening balance for wallets), `AccountRenamed`, `AccountArchived`, `AccountDeleted` | `UserRegistered`, `WalletCreated`, `WalletRenamed`, `WalletArchived`, `WalletDeleted` |
| **Wallets** | `wallets` | Wallets plus a projection of the balance and transaction count (row-versioned) | `WalletCreated`, `WalletRenamed`, `WalletArchived`, `WalletDeleted` | `WalletBalanceChanged` |
| **Transactions** | `transactions` | `transactions`, `postings`, `ledger_accounts` (projection plus running wallet balance), `pending_wallet_assignments`, `counterparties` (names of contacts and groups) | `WalletBalanceChanged`, `LedgerPostingCompleted`, `LedgerPostingRejected` | `AccountCreated/Renamed/Archived/Deleted`, `ContactCreated/Linked`, `GroupCreated`, `GroupMemberAdded`, `LedgerPostingRequested` |
| **Contacts** | `contacts` | `contacts` (person id, optional link to a user), `registered_users` projection | `ContactCreated`, `ContactLinked` | `UserRegistered`, `DebtOperationCompleted`, `GroupCreated`, `GroupMemberAdded` |
| **Groups** | `groups` | `groups`, `group_members`, projections of contacts and users | `GroupCreated`, `GroupMemberAdded`, `GroupMemberRemoved` | `UserRegistered`, `ContactCreated/Linked`, `DebtOperationCompleted` |
| **Debts** | `debts` | `operations` (with payers and shares), `debts`, projections of contacts, users, group members and wallets | `LedgerPostingRequested`, `DebtOperationCompleted` | `UserRegistered`, `ContactCreated/Linked`, `GroupCreated`, `GroupMemberAdded/Removed`, `WalletCreated/Deleted`, `LedgerPostingCompleted/Rejected` |
| **Investments** | `investments` | `assets` (with the holding: quantity, cost basis, realized gain), `trades`, `asset_prices` | `AssetCreated/Renamed/Archived`, `InvestmentPostingRequested` | `InvestmentPostingCompleted/Rejected` |

Every schema also holds `__EFMigrationsHistory`, `integration_outbox` and `inbox_messages`.

### 3.4 Request pipeline (API)

`UseExceptionHandler` → `UseAuthentication` (Keycloak JWT: audience `ledger-api`, issuer `http://localhost:8080/realms/ledger`) → **`UseUserProvisioning`** → `UseAuthorization` → module endpoints → MediatR (`LoggingBehavior`, `ValidationBehavior`) → handler.

`UseUserProvisioning` maps the token's `sub` to the internal user id, creating the user and publishing `UserRegistered` on the first request, and caches the result for 30 minutes. It adds the id as a `ledger_user_id` claim, which `ICurrentUser.UserId` reads.

Error mapping (RFC 7807):

| Exception | Status |
| --- | --- |
| `ValidationException` (FluentValidation) | 400 with `errors` |
| `DomainException` | 400 with `code`, e.g. `ledger.insufficient_funds` |
| `NotFoundException` | 404 |
| `ForbiddenException` | 403 |
| `ConflictException` | 409 |
| `NotYetAvailableException` | 503 with `Retry-After: 1` (data still being provisioned by another module) |

## 4. Accounting model

### 4.1 Rules

* A **transaction** belongs to one user (`OwnerId`), happens at `OccurredAt`, has a `Kind` (`OpeningBalance`, `Income`, `Expense`, `Transfer`, `Loan`, `Split`, `Settlement`, `WalletAssignment`) and an optional `CorrelationId` (the wallet or debt operation that produced it).
* A **posting** is a non-zero signed amount with at most 2 decimals in one currency. Positive is a debit, negative a credit.
* A transaction has **at least two postings**, and they **sum to zero in every currency**.
* Each user's books are separate: an operation involving two users produces **one transaction per user** with the same `CorrelationId`.

### 4.2 Accounts per user

| Account | Type | Purpose | Created when |
| --- | --- | --- | --- |
| *(wallet name)* | Asset | `Wallet` (one currency, running balance, never below 0) | a wallet is created |
| *(category name)* | Income / Expense | `Category` | the user creates it |
| Owed to me | Asset | `Receivables` | the user registers |
| I owe | Liability | `Payables` | the user registers |
| Opening balances | Equity | `OpeningBalances` | the user registers |
| Currency exchange | Equity | `CurrencyExchange` | the user registers |
| Uncategorized | Expense | `Uncategorized` | the user registers |
| Uncategorized income | Income | `UncategorizedIncome` | the user registers |
| Pending settlements | Asset | `PendingSettlements` | the user registers |
| *(asset name)* | Asset | `Investment` (one currency; holds the **cost** of an investment asset) | an asset is added ([section 16](#16-investments)) |
| Investment gains | Equity | `InvestmentGains` (realized gains and losses; neither income nor expense) | the user registers (existing users: startup backfill) |

### 4.3 Debts: counterparty dimension

Postings on *Owed to me*, *I owe* and *Pending settlements* carry a `CounterpartyId`: a contact's **person id**, or a **group id** for group expenses. "How much does Pedro owe me?" is the sum of those postings for Pedro. The `counterparties` projection in Transactions provides names, and the contact filter of the history uses it.

### 4.4 Group expenses

Inside a group, each registered member's position is recorded **against the group** (the counterparty is the group id), not against individual people. This is what makes **simplified** payments representable in the books.

Example: raw debts A→B 10 and B→C 10 simplify to A pays C 10. With per-person counterparties, B's books would never be updated. Against the group: A pays 10 (reducing A's debt to the group), C receives 10 (reducing what the group owes C), and B was already even.

Pairwise who-owes-whom debts are still computed and stored in Debts, and shown in the group balances ("raw debts"). Once every member of a group is even in a currency, all its open debts in that currency are marked Settled.

### 4.5 People and person ids

* A registered user's **person id is their user id**.
* A non-registered contact gets a person id the first time it is added (its contact id). If somebody else's debt is mirrored to a linked user, the auto-created contact on their side reuses that person id.
* Linking a contact to a user **changes its person id** to the user id. An **unused** contact (no debt or group) is linked right away. A contact that is **in use** sends a **link request** the user must accept; accepting merges the old person into the user everywhere and copies the shared history into the user's books ([section 17](#17-linking-contacts-that-are-already-in-use)).

### 4.6 Pending wallet assignments

When somebody else records an operation that moved *my* money (my friend records that I paid them back, or that they lent me money), the ledger doesn't know which wallet was used. The amount goes to **Pending settlements** (positive: money I received; negative: money I paid) and a **pending wallet assignment** is created. When I choose a wallet (`POST /api/pending-assignments/{id}/assign`), the amount moves between Pending settlements and that wallet, and the no-overdraft rule applies.

### 4.7 Summary

Per currency:

```text
Wallets    = Σ wallet balances
Owed to me = Σ Receivables postings
I owe      = −Σ Payables postings
Pending    = Σ Pending settlements postings
Net        = Wallets + Owed to me − I owe + Pending
```

Settlements and transfers don't change **Net**; only income, expenses and your own share of splits do. The summary also lists every wallet, and every person or group with what they owe me and what I owe them.

### 4.8 Worked examples (postings per user)

| Operation | Postings |
| --- | --- |
| Wallet "Bank" opened with 1000 EUR | Bank +1000 · Opening balances −1000 |
| Salary 500 into Bank | Bank +500 · Salary (Income) −500 |
| Groceries 30 from Bank | Food (Expense) +30 · Bank −30 |
| Transfer 100 USD → 92 EUR | Dollars −100 USD · Currency exchange +100 USD · Currency exchange −92 EUR · Bank +92 EUR |
| Alice lends 200 to Pedro (not registered) | **Alice**: Bank −200 · Owed to me [Pedro] +200 |
| Alice lends 50 to Bob (linked) | **Alice**: Bank −50 · Owed to me [Bob] +50 — **Bob**: Pending settlements +50 · I owe [Alice] −50, plus a pending assignment of +50 |
| Alice pays 60 dinner, split equally with Bob | **Alice**: Bank −60 · Food +30 · Owed to me [Bob] +30 — **Bob**: Uncategorized +30 · I owe [Alice] −30 |
| Bob paid 30 cinema for Alice ("friend paid for me") | **Alice**: Uncategorized +30 · I owe [Bob] −30 — **Bob**: Pending −30 (assignment −30) · Owed to me [Alice] +30 |
| Group "Trip": hotel 120 split between Alice, Bob and Carl; Alice paid 80, Bob 40 | **Alice**: Bank −80 · Food +40 · Owed to me [Trip] +40 — **Bob**: Pending −40 (assignment −40) · Uncategorized +40 (Bob is even) — Carl is not registered, so he has no books |
| Carl pays Alice 40 (suggested group payment, recorded by Alice into Bank) | **Alice**: Bank +40 · Owed to me [Trip] −40 |
| Bob settles up with Alice (Bob owed 30), paying from his wallet | **Bob**: Wallet −30 · I owe [Alice] +30 — **Alice**: Pending +30 (assignment +30) · Owed to me [Bob] −30 |

### 4.9 Split math (exact to the cent)

* **Equal:** `total / n` in cents; leftover cents go one each to the first participants (100 / 3 → 33.34, 33.33, 33.33).
* **Percent:** the percentages must add up to 100. Each share is rounded down to the cent, and the leftover cents go to the first participants with a non-zero percentage.
* **Custom:** each participant's amount; the amounts must add up to the total.
* **Debts with several payers:** net = paid − consumed. Each debtor owes each creditor in proportion to that creditor's surplus. The allocation is rounded down to the cent and the leftover cents are distributed so that **every row and column is exact**.
* **Simplification:** per currency, the largest debtor repeatedly pays the largest creditor, which settles everything in at most n−1 payments.

## 5. Module communication

### 5.1 Outbox (publishing)

`IEventBus.PublishAsync` (the implementation is `ModuleEventBus<TDbContext>`) **adds an `OutboxMessage` row** (type plus JSON) to the module's DbContext, so the event is committed in the same transaction as the business change, or not at all. An `OutboxProcessor<TDbContext>` hosted service per module claims unprocessed rows with `FOR UPDATE SKIP LOCKED` (several instances can run), publishes them to RabbitMQ with **MessageId = outbox row id**, and marks them processed. It polls every 250 ms when idle and keeps the order: if one message fails, the rest of the batch waits.

### 5.2 Inbox (consuming)

Each module has **one RabbitMQ queue**, `ledger-<module>`, holding all its consumers, with message retry at 100 ms, 500 ms, 1 s, 2 s, 5 s and 10 s. Consumers derive from `ModuleConsumer<TMessage, TDbContext>`, which:

1. opens a database transaction;
2. `INSERT … ON CONFLICT DO NOTHING` into `inbox_messages (MessageId, Consumer)`; if nothing was inserted, the message was already handled and is skipped (a concurrent duplicate waits for the first one's row, then skips);
3. runs the handler (usually `ISender.Send(command)`);
4. saves (flushing any outbox messages) and commits;
5. on error, clears the change tracker so nothing leaks into the retry, then rethrows.

### 5.3 Why the flows are asynchronous

Anything that affects another module happens through events, so it is **eventually consistent**: typically a few hundred milliseconds. Visible effects:

* A new wallet is **not usable immediately**. Its `isReady` flag turns true once the ledger has its account.
* A category or contact used a few milliseconds after it was created may get **404** from another module. Resources still being provisioned for the current user return **503** with `Retry-After`. The console retries 503 automatically.
* **Loans, splits and settlements** return `202 Accepted` and an operation. Poll `GET /api/debts/operations/{id}` for `Completed`, or `Rejected` with a reason.

### 5.4 Main flows

**User registration** (first authenticated request):

```text
API → Identity: GetOrCreateUser ──UserRegistered──► Accounts (system accounts) ──AccountCreated──► Transactions (projection)
                                                ├─► Contacts (registered users, for linking by email)
                                                ├─► Groups (display names)
                                                └─► Debts (display names)
```

**Wallet creation:**

```text
POST /api/wallets → Wallets ──WalletCreated──► Accounts: wallet account ──AccountCreated(+opening balance)──► Transactions
                             └─► Debts (wallet projection)                    projection + opening-balance transaction
Transactions ──WalletBalanceChanged(version)──► Wallets (balance, isReady)
```

**Loan, split or settlement:**

```text
POST /api/debts/... → Debts: validate, store the operation and debts (Pending) ──LedgerPostingRequested(legs)──► Transactions
Transactions: one transaction per registered user, all or nothing; overdraft check on the initiator's wallet
   ├─ OK   ──LedgerPostingCompleted──► Debts: operation Completed, debts Open/Settled ──DebtOperationCompleted──► Contacts (mirrored contacts)
   │                                                                                                    └─► Groups (member activity)
   └─ fail ──LedgerPostingRejected(reason)──► Debts: operation Rejected, debts Rejected / settlement released
```

A **leg** is the effect on one registered user's books: an optional wallet, an optional category, a cash amount, an expense amount and positions (Receivable or Payable per counterparty). It must satisfy `cash + expense + Σ positions = 0`. Legs without a wallet use Pending settlements plus a pending assignment.

### 5.5 Consistency rules

| Risk | Protection |
| --- | --- |
| Overdraft through concurrent postings | Wallet ledger accounts are loaded with `SELECT … FOR UPDATE`, ordered by id to avoid deadlocks, inside the posting transaction. |
| Stale or out-of-order balance events | `WalletBalanceChanged.Version` grows monotonically; Wallets ignores older versions. The wallet row has an **`xmin` row version** (optimistic concurrency), so two concurrent updates can't overwrite each other: the loser retries and is ignored. |
| Duplicate delivery | The inbox (`inbox_messages`). Handlers are also idempotent (projections check for existing rows; ledger postings check the `CorrelationId`). |
| Rejected ledger posting inside a consumer | Tracked ledger entities are discarded (`DiscardLedgerChanges`) before the rejection reply is published. |
| Two settlements of the same debt | `Debt.StartSettlement` marks it as settling; a second attempt fails. Group settlements are refused while another operation of that group is pending. |
| Concurrent first requests of a new user | The unique index on `ExternalId`; the loser re-reads the winner. |

## 6. Feature walkthroughs

### Transfer between wallets

`POST /api/transactions/transfer { fromWalletId, toWalletId, amount, receivedAmount? }`. With the same currency, `receivedAmount` must be omitted or equal to `amount`. Across currencies it is required, and it implies the rate. Returns `201`, or `400 ledger.insufficient_funds`.

### Lend money and see what I'm owed

`POST /api/debts/loans { contactId, walletId, amount }` → `202` and an operation. `GET /api/debts` returns the open debts plus `byContact` (owed to me, I owe, net per person and currency). `GET /api/summary` shows the totals.

### Total money

`GET /api/summary`: totals per currency (wallets, owed to me, I owe, pending, net), each wallet's balance, and each person or group.

### Transactions ordered by time

`GET /api/transactions?page=1&pageSize=20&walletId=&categoryId=&contactId=&from=&to=`, newest first (`OccurredAt`, then `CreatedAt`). Page size is 1–100. Every posting includes its account name, wallet id and counterparty name.

### Split expenses

`POST /api/debts/splits`:

```json
{
  "groupId": null,
  "total": 120, "currency": "EUR", "method": "Equal",
  "payers": [ { "personId": "<me>", "amount": 80, "walletId": "<my wallet>" },
              { "personId": "<bob>", "amount": 40 } ],
  "participants": [ { "personId": "<me>" }, { "personId": "<bob>" }, { "personId": "<carl>" } ],
  "categoryId": null, "description": "Hotel"
}
```

* `method`: `Equal`, `Custom` (with `value` = amount) or `Percent` (with `value` = percentage).
* `<me>` is the `personId` from `GET /api/me`. The others are contacts' `personId`s, or group members.
* My own payer entry needs a `walletId` in the split currency.
* "A friend paid for me": payers = [friend: total], participants = [me: total], method `Custom`.

### Settlements

* Outside groups: `POST /api/debts/payments { "contactId", "currency", "direction": "TheyPaidMe" | "IPaidThem", "amount", "walletId" }`. It pays the debts with that person from the oldest, mutual debts cancel out first and any extra becomes a debt the other way (§20). `POST /api/debts/payments/preview` shows what it would pay.
* Groups: `POST /api/debts/settlements { "groupId", "fromPersonId", "toPersonId", "currency", "walletId?" }` must match a suggested payment exactly. `walletId` is required if I am the payer or the receiver.

(Before §20 settlements were full only, with scopes `Debt` and `Contact`; those stored settlements now behave as payments.)

## 7. Local environment, running and debugging

### 7.1 Services (docker compose, project `ledger`)

| Service | Image | Port(s) | Notes |
| --- | --- | --- | --- |
| postgres | `postgres:17` | 5432 | db `ledger`, postgres/postgres, volume `ledger-postgres-data`, healthcheck |
| rabbitmq | `rabbitmq:4.2-management` | 5672, 15672 | guest/guest, healthcheck |
| keycloak | `quay.io/keycloak/keycloak:26.7.4` | 8080 | `start-dev --import-realm`, admin/admin. `KC_HOSTNAME=http://localhost:8080` plus `KC_HOSTNAME_BACKCHANNEL_DYNAMIC=true`, so tokens always have the `localhost` issuer while containers fetch keys at `keycloak:8080` |
| migrator | built from `src/Ledger.Migrator/Dockerfile` | — | one-shot; applies all migrations |
| api (profile `full`) | built from `src/Ledger.Api/Dockerfile` | 8081 → 8080 | Debug build with `vsdbg` at `/vsdbg`; waits for the migrator to succeed |

### 7.2 Keycloak realm `ledger`

| Client | Kind | Use |
| --- | --- | --- |
| `ledger-api` | confidential, no flows | The token **audience** accepted by the API |
| `ledger-console` | public, auth code + PKCE (S256) | Redirect `http://127.0.0.1:7890/*` (loopback, fixed port 7890) |
| `ledger-scalar` | public, auth code + PKCE | Redirects `http://localhost:5209/*` and `http://localhost:8081/*` |

The console and Scalar clients have an audience mapper that adds `ledger-api`. The users are **alice/alice** (`alice@ledger.local`) and **bob/bob** (`bob@ledger.local`). The realm is imported **only when Keycloak's data is empty**: recreating the container re-imports it and discards any manual changes.

### 7.3 Run and debug

| Goal | How |
| --- | --- |
| Debug API and console together | VS Code → **API + Console**. It runs the `prepare-debug` task (`docker compose up -d --wait postgres rabbitmq keycloak`, migrator, build) once, then starts both. |
| Debug the API only | VS Code → **API** (opens `http://localhost:5209/scalar`). |
| Console only | VS Code → **Console**. **The API must already be running**; the console waits up to 30 s for `/health`. |
| Apply migrations | VS Code → **Migrator**, task `migrate`, or `dotnet run --project src/Ledger.Migrator`. |
| Everything in containers | `docker compose --profile full up -d --build` (task `full-up`). API on http://localhost:8081. |
| Debug the container | VS Code → **Docker: attach to API** (docker exec + vsdbg, source map `/source` → workspace). |
| Stop | `docker compose --profile full down` (task `down`); add `-v` to delete the database. |

Configuration:

* **API:** `appsettings.Development.json` holds the local connection strings (PostgreSQL, `amqp://guest:guest@localhost:5672/`) and the authority. Override them with environment variables, e.g. `ConnectionStrings__Database`, `Authentication__Authority` or `Authentication__MetadataAddress`.
* **Console:** `src/Ledger.Console/appsettings.json` (`Api:BaseUrl`, `Keycloak:*`), overridable with `Api__BaseUrl`.
* **Migrator:** `src/Ledger.Migrator/appsettings.json`.

## 8. Testing and verification

### 8.1 Automated tests (`dotnet test Ledger.slnx`)

| Project | Count | Covers |
| --- | --- | --- |
| Ledger.UnitTests | 28 | Per-currency balancing, the two-posting minimum, cents, overdraft, wallet currency, ownership and archiving, wallet archive/delete and stale balances, split math (equal, percent, custom, multi-payer exactness, simplification), debt settling, balanced ledger legs (ad-hoc and group), contact linking, group member rules |
| Ledger.ArchitectureTests | 43 | For each of the 7 modules: only other modules' Contracts; contracts depend on nothing; framework-free Domain; Application without infrastructure or web; Presentation without persistence; immutable contracts. Plus a positive control proving dependency detection works |
| Ledger.IntegrationTests | 4 | The whole API with PostgreSQL 17 and RabbitMQ 4.2 in Testcontainers and a header-based test auth (`X-Test-User`): transfers with FX and overdraft; a split mirrored to a linked user, then settled and assigned; a rejected loan; group simplification and settlement |

### 8.2 Manual verification performed during the MVP

* A scripted end-to-end scenario as **alice and bob** against the API run locally **and** in the `full` containers. It covered: wallets EUR 1000 / EUR 0 / USD 200; income; expenses (categorized and uncategorized); a same-currency transfer; a USD→EUR transfer (100 → 92); overdraft rejection; an FX transfer without a received amount (rejected); a linked contact (Bob) and an unlinked one (Carl); a loan to Carl; a loan above the balance (Rejected); group "Trip" with a multi-payer equal split; a "friend paid for me" split; the summary; every history filter; Bob's mirrored contacts, group and pending items; Bob assigning wallets; Alice settling up with Bob; a group-pair settlement; and the final balances.
  * Alice's **net stayed at 1,482 EUR** through the settlements. Final wallets were Bank 1,402, Cash 30 and Dollars 100.
* The **console** was driven end to end (login, summary, paged history, debts overview, the split wizard, exit on EOF). The browser login was simulated with a fake `open` command that submitted the Keycloak form with curl.
* Keycloak accepts the console redirect on port 7890 and rejects other ports.
* The containerised API validated Keycloak tokens (`localhost` issuer, keys fetched from `keycloak:8080`), and `vsdbg` is present in the image.

## 9. Problems found while building the MVP

### 9.1 MassTransit 8 bus outbox does not support one bus per module

**Plan:** one MassTransit bus per module (MultiBus), each with its own EF bus outbox.
**Found:** API startup failed. `EntityFrameworkScopedBusContextProvider` and `BusOutboxDeliveryService` are registered against the default `IBus`/`IBusControl` only.
**Fix:** a single bus with **one receive endpoint (queue) per module**, and our **own outbox** per module DbContext ([5.1](#51-outbox-publishing)).

### 9.2 MassTransit 8 EF inbox caches its SQL per entity type

**Found:** consumers failed with *"InboxState cannot be tracked because another instance with the same key…"*, then *duplicate key AK_inbox_state_MessageId_ConsumerId*. MassTransit's `SqlLockStatementProvider` caches table names in a **static dictionary keyed by entity type**, so every module's inbox query hit the first module's `inbox_state` table.
**Fix:** our **own inbox** through `ModuleConsumer` ([5.2](#52-inbox-consuming)); MassTransit.EntityFrameworkCore was removed.

### 9.3 Lost update on the wallet balance projection

**Found:** a wallet created with an opening balance showed 0. Its two `WalletBalanceChanged` events (versions 1 and 2) were consumed concurrently, and the older one's save won.
**Fix:** an `xmin` row version on `wallets` (optimistic concurrency). The migration generator correctly skips the `xmin` system column.

### 9.4 Smaller fixes

| Problem | Fix |
| --- | --- |
| The migrator couldn't find `appsettings.json` with `dotnet run` (content root = current directory) | `ContentRootPath = AppContext.BaseDirectory` (also in the console) |
| `Microsoft.OpenApi 2.0.0` (from ASP.NET OpenApi) had a high-severity advisory (GHSA-v5pm-xwqc-g5wc) | Pinned `Microsoft.OpenApi 2.12.2` |
| `FromSqlRaw` analyzer warning EF1002 in the outbox | The SQL is built from EF model identifiers only, never user input |
| `TransactionsDbContext.InTransactionAsync` called itself instead of the extension | Explicit `PersistenceExtensions.InTransactionAsync(this, …)` |
| Console login: "The inner handler has not been assigned" | Build a `RefreshTokenDelegatingHandler` with an inner `HttpClientHandler`; `SetHandlerLifetime(Infinite)` so the factory never disposes the token handler |
| Console looped forever when stdin closed | `ConsoleUi.ReadLine` treats EOF as exit |
| Amounts formatted with the machine culture (`60,0000`) | Invariant `0.00` / `N2` formatting in messages and the console |
| **Console: `[ERROR] Connection refused (localhost:5209)` right after a successful Keycloak login** | The API wasn't listening yet (console started alone, or started together with the API while Keycloak still had a session, so login was instant). The console now **waits up to 30 s for `/health`** and otherwise says how to start the API. |

### 9.5 Local environment changes made during the MVP

* The local `ledger` database was **dropped and recreated** for the fresh migrations (decision T9).
* The Keycloak container was **recreated** to import the realm; any manually created realm was lost.
* Password grant was **temporarily enabled** on `ledger-console` for scripted smoke tests, then **disabled again**. Recreating the Keycloak container also resets it from the realm file.

## 10. Troubleshooting

| Symptom | Cause and solution |
| --- | --- |
| Console: `Connection refused (localhost:5209)` or "The Ledger API is not reachable" | The API isn't running. Start **API** or **API + Console** in VS Code, or `dotnet run --project src/Ledger.Api --launch-profile http`. For the containerised API, point the console at it: `Api__BaseUrl=http://localhost:8081`. |
| Console login page says "Invalid parameter: redirect_uri" | The callback port must be **7890** (`Keycloak:CallbackPort`); it's the only one registered in the realm. |
| Port 7890 in use | Another console instance is waiting for a login; close it. |
| 401 from the API | Missing or expired token, or the token's issuer or audience doesn't match: the authority must be `http://localhost:8080/realms/ledger` and the audience `ledger-api`. |
| 503 "Not available yet" | Something for your user is still being provisioned (e.g. system accounts just after the first login). Retry after a second. |
| 404 for a wallet or category created a moment ago | Eventual consistency ([5.3](#53-why-the-flows-are-asynchronous)). Wait for `isReady` on wallets, or retry shortly. |
| A loan, split or settlement stays `Pending` | The API (outbox processors and consumers) must be running, and RabbitMQ must be reachable. Check the queues at http://localhost:15672 (`ledger-*`) and for messages in `_error` queues. |
| `split.unknown_person` right after linking a contact | Debts hasn't received the link yet; retry. |
| Tables missing / `relation … does not exist` | Run the migrator. |
| Start from scratch | `docker compose --profile full down -v && docker compose up -d --wait postgres rabbitmq keycloak && dotnet run --project src/Ledger.Migrator` |

## 11. Known limitations and next steps

* **Mirrored debts** can't yet be **cancelled or disputed** by the other user (agreed as future work).
* No currency conversion in totals (by decision P2).
* Payments apply oldest first; choosing which debt a payment pays is not supported (§20).
* A contact can't be linked to a user once it's used (I8). Re-keying past debts would lift this.
* Deleting a wallet right after creating it relies on retries while the account is still being created.
* **Licensing:** MediatR 12 and MassTransit 8 are the last Apache versions. MassTransit 8 gets security fixes until the end of 2026; plan a move (to MassTransit 9 with a license, Wolverine, or plain RabbitMQ.Client, since the outbox and inbox are already ours).
* **Operations:** no outbox/inbox cleanup job yet, no dead-letter dashboard, no metrics or tracing. Adding OpenTelemetry and periodic cleanup of processed outbox and inbox rows is recommended.
* Production (HTTPS, secrets, Keycloak in production mode, migrations as a pipeline step) is covered by the VPS deployment (§19) and the AWS one (§18). Still missing: alarms on errors or dead-lettered messages, a staging environment, and email (password reset, invitations).

## 12. Reference

### 12.1 Versions

| Component | Version |
| --- | --- |
| .NET / C# | net10.0 (SDK 10.0.401) |
| EF Core / Npgsql provider | 10.0.4 / 10.0.3 |
| MediatR | 12.5.0 |
| FluentValidation | 11.12.0 |
| MassTransit (+ RabbitMQ) | 8.5.10 |
| ASP.NET JwtBearer / OpenApi | 10.0.4 (Microsoft.OpenApi pinned to 2.12.2) |
| Scalar.AspNetCore | 2.17.11 |
| IdentityModel.OidcClient | 6.0.0 |
| xUnit / Test SDK | 2.9.3 / 18.10.1 |
| NetArchTest.eNhancedEdition | 1.4.5 |
| Testcontainers | 4.15.0 |
| PostgreSQL / RabbitMQ / Keycloak | 17 / 4.2-management / 26.7.4 |

Package versions are managed centrally in `Directory.Packages.props`.

### 12.2 Conventions

* Tables are `snake_case`, in the module's schema; columns use the EF default names (PascalCase). Enums are stored as strings, money as `numeric(19,4)`, and user amounts have at most 2 decimals.
* One folder per use case in Application; command, query, handler and validator live together.
* A handler loads through its module's `I<Module>DbContext`, changes aggregates, publishes events through `I<Module>EventBus`, then saves. The outbox rows are part of that same save.
* Consumers do no business logic: they map the event to a command and send it.
* Integration events are immutable records in `Contracts`, with primitive or string fields only (currencies as ISO codes).

### 12.3 Commits (branch `feature/mvp`)

| Commit | Content |
| --- | --- |
| `c806de8` | Modular backend: building blocks, 7 modules in per-layer projects, migrations, API, migrator |
| `36728b4` | Docker compose, Keycloak realm, Dockerfiles, VS Code tasks and launch configurations |
| `cd9d564` | Console client of the API with the Keycloak PKCE login |
| `11e7520` | Unit, architecture and integration tests |
| `afd511d` | README and Migrations.md |
| `0b64f45` | Console waits for the API before its first call |

## 13. Web client

An Angular client in `clients/web`. How to run, debug, theme, translate and test it is in [clients/web/README.md](../clients/web/README.md); this section records the decisions and what building it changed in the backend.

### 13.1 Decisions (asked before building)

| Topic | Decision |
| --- | --- |
| Branch | `feature/web-client`, based on `feature/mvp` |
| Location | `clients/web` |
| Screens | All of them: dashboard, wallets, transactions, categories, contacts, debts and splits, groups, pending assignments |
| Languages | English and Spanish; English by default |
| i18n | **Angular built-in i18n** (one build per language at `/en/` and `/es/`; switching reloads the page; `ng serve` serves one language) |
| Amounts and dates | Formatted according to the language |
| Auth | **angular-oauth2-oidc** (authorization code + PKCE) with a new Keycloak client `ledger-web` |
| API access | **Same-origin proxy** (Angular dev server / nginx) instead of CORS |
| Styling | Tailwind CSS 4; palette = neutral (slate) + one accent (**indigo**); every color is a token |
| Theme | Light and dark, **following the OS**, with a manual override |
| Devices | Desktop and mobile (sidebar ↔ bottom bar and menu) |
| Components | Own UI kit, with the **Angular CDK** only for behaviour (dialogs) |
| State | Angular signals + services |
| Asynchronous operations | **Fire and notify**: the dialog closes right away and a toast reports the outcome |
| Running | `ng serve` (VS Code launch configurations) **and** an nginx container in the compose `full` profile, on port **4201** |
| Tests | Vitest (unit) and Playwright (end-to-end) |

### 13.2 Backend changes it required

Building and testing the web client surfaced these backend issues, fixed on the same branch:

| Problem | Fix |
| --- | --- |
| `GET /api/me` without a token returned **500** (it was the only endpoint without `RequireAuthorization`) | Requires authorization and returns **401**. There's an integration test. |
| A wallet created with an opening balance was announced as ready with a **balance of 0**, and the real balance arrived in a second event, so a page could show 0 | The ledger announces a new wallet **once**, already including its opening balance |
| Debts answered **404** for a wallet or contact created a moment before (not yet projected there) | **503 Not available yet** (clients retry). It is still 404 when the wallet or contact belongs to someone else. There's an integration test. |
| A consumer that depends on another module's event could run out of retries (about 19 s) on a cold start and be dead-lettered | Exponential retry, about 3 minutes in total |
| EF warning for aggregates with several owned collections | Split queries for the module DbContexts |
| The web app needed a Keycloak client | `ledger-web` added to `infra/keycloak/ledger-realm.json` (and to the running Keycloak with `kcadm`) |

### 13.3 UI lessons

* **CDK overlay styles:** the CDK injects its overlay CSS at runtime, unlayered, so it overrides Tailwind's layered utilities (e.g. `max-w-*` on the dialog pane). Dialog sizes are therefore passed through the CDK config (`maxWidth`), not classes.
* **`<select [value]>`** is applied before its options render, so options use `[selected]`.
* **Eventual consistency in the UI:** after every change the pages reload now and once more 1.5 s later, because projections in other modules (e.g. wallet balances) update a moment after the change.

## 14. Dates, opening balances, adjustments, existing debts and reports

Added on branch `feature/ledger-enhancements`, to the API, the web app and the console.

### 14.1 Decisions (asked before building)

| Topic | Decision |
| --- | --- |
| Dated operations | Income, expense, transfer, loans, splits, settlements, wallet opening balance, pending wallet assignments |
| Precision | Date **and time**; stored as `DateTimeOffset` in **UTC**; the clients show local time; default now |
| Future dates | Rejected (`NotInFuture`, 1 minute of clock skew) |
| Wallet initial amount | **Both**: edit the opening balance (amount and date; append-only) and a balance adjustment to the real balance |
| Adjustments | New system account **Balance adjustments** (Equity), **excluded** from income and expenses in reports |
| Existing debts | Both directions, **contacts only**; **mirrored** to linked users. First recorded as expense (I owed) or income (they owed me); **changed in [section 15](#15-editable-auditable-ledger)** to neither (Opening balances) |
| Charts | Spending by category, income vs expenses, net worth over time, balance per wallet; Month / Year / **Custom**; currency selector; **Chart.js** |
| Console | Same features, plus a **text Reports menu** |

### 14.2 Accounting

| Operation | Postings |
| --- | --- |
| Replace an opening balance of 100 with 250 | New: Wallet +250 · Opening balances −250 (new date). Reversal of the old one (dated like it): Wallet −100 · Opening balances +100. The new entry is written first, so the no-overdraft rule only fails if the **final** balance would be negative. |
| Adjust a wallet from 100 to 87.50 | Wallet −12.50 · Balance adjustments +12.50 |
| "I already owed Sofi 50" | Me: Uncategorized (or a chosen expense category) +50 · I owe [Sofi] −50. Sofi, if linked: Owed to me [me] +50 · Uncategorized income −50 |
| "Sofi already owed me 50" | Me: Owed to me [Sofi] +50 · income category −50. Sofi, if linked: expense +50 · I owe [me] −50 |

* The active opening entry is the `OpeningBalance` transaction of the wallet that no `OpeningBalanceReversal` points to (the reversal's `CorrelationId` is the replaced transaction's id).
* `LedgerLeg` gained `IncomeAmount` (credited to an income category) so the ledger can post the income side of existing debts.
* Existing users got the new system account through a startup backfill that runs the idempotent provisioning for every owner.

### 14.3 Reports

`GET /api/reports?from&to&currency&timeZone` is computed in memory from the user's postings in one currency:
* **Buckets:** days when the range is 62 days or shorter, months when it is longer, cut in the given IANA time zone.
* **Income and expenses:** only Income and Expense accounts, so opening balances, currency exchange and balance adjustments are excluded.
* **Net worth:** the cumulative wallets + owed to me − I owe + pending at the end of each bucket, including history before the period.
* **Spending by category**, and the **balance per wallet** at the end of the period.

### 14.4 Problems found and fixed

| Problem | Fix |
| --- | --- |
| A date with a non-UTC offset (e.g. `+02:00`) made PostgreSQL reject the value (500). The console's reports sent local offsets; the web app always sends UTC. | EF convention in every module: `DateTimeOffset` values are converted to UTC (same instant) when written and in queries. No schema change. Integration test added. |
| While testing, the developer's API from a VS Code debug session ran old code on the same database and queues | Tests ran against an **isolated** copy: database `ledger_enh`, RabbitMQ virtual host `ledger-enh`, API on port 5219 and a temporary dev-server proxy |

## 15. Editable, auditable ledger

Added on branch `feature/ledger-edits`, to the API, the web app and the console. Users can correct or delete past income, expenses, transfers, loans, existing debts, splits (also group expenses) and settlements, restore what they deleted, see every version, close past periods, and review changes other people make to what they share. It also fixes the bug report "the income vs expense chart shows a debt as income".

### 15.1 Decisions (asked before building)

| Topic | Decision |
| --- | --- |
| Edit model | **Reverse + replace.** Posted entries are never modified. An edit posts a reversal of the current version (same date, so both cancel out) and the corrected entry; a delete posts only the reversal. Lists, balances and charts show the **current** version only; a **History** shows every version. |
| Reason | **Required** for every edit, delete and restore (max 500 characters); kept in the history. |
| What can change | Income, expense, transfer (and delete/restore of balance adjustments); loans and existing debts; splits, including group expenses; settlements (date, description, wallet: the amount comes from the debts it paid). |
| Settled debts | **Blocked**: undo (delete) the settlement first. Deleting a settlement reopens its debts. |
| Shared with linked users | **They confirm.** Until everybody involved answers, everybody keeps the old version, the operation shows *change pending* and it cannot be settled. **Each user decides for their own books**: the author and those who accept get the new version; those who reject keep the old one and the operation is marked **disputed**. Contacts without an account need no confirmation. |
| Who may change a shared operation | Any **registered user involved** (for group operations, any member). The contact, wallets, category and currency belong to whoever recorded it; others keep them. |
| Existing debts | **Neither income nor expense**: posted against *Opening balances*, like a wallet's starting amount. Old ones were migrated with reverse + replace, marked as a **system** change. |
| Extras | **Restore** deleted items; a **lock date** per user ("my books are closed up to X", can be moved back); a shared change that touches a linked user's locked period is **refused** for the author; an **Activity** feed. |
| Console | Everything the web app has. |

### 15.2 Ledger (Transactions module)

* `Transaction` gained `EntryId` (the logical entry: its first version's id), `Revision`, `ReversalOfId`, `ReversedById`, `ChangedByUserId`, `ChangeAction` (`Edit`, `Delete`, `Restore`, `System`) and `ChangeReason`. Kind `Reversal` was added. Setting `ReversedById` on the original is the only change ever made to a posted row, and it is metadata.
* **Current** = not reversed and not a reversal. Lists show only current transactions unless `includeHistory=true` (statuses `Current`, `Changed`, `Deleted`, `Reversal`). **Reports count income, expenses and spending from current transactions only**, so a correction is never counted twice; net worth and balances use every posting (a reversal and its original cancel out).
* `LedgerWriter` checks the no-overdraft rule on each wallet's **final** balance over the whole batch (reversal + correction), not on intermediate steps.
* **Lock date** (`books_locks`): every write checks `OccurredAt > LockedUntil` for every owner in the batch (reversals carry the original's date, so a closed period cannot be edited or deleted either). Error `books.locked`. Changes are published as `BooksLockChanged`; the Debts module keeps a copy to refuse shared changes early (`books.locked_for_other`).
* Shared operations are revised through `LedgerRevisionRequested`: for each listed user, reverse their current transactions of **any earlier version** of the operation (so a user who kept an old version after a dispute is brought up to date by the next accepted change), cancel their open pending wallet assignments, and post their leg of the new version as the next revision. All users are updated atomically; the answer is `LedgerRevisionCompleted` or `LedgerRevisionRejected`.
* Migration `TransactionRevisions` backfills `EntryId = Id` and `Revision = 1`, chains the successive opening balances of each wallet into one entry and links the old `OpeningBalanceReversal` rows to what they cancelled. Opening-balance changes now use the generic reversal.
* **Existing debts migration** (`ExistingDebtsMigrationRunner`, at startup, idempotent): every current `InitialDebt` transaction with an income or expense posting is reversed and replaced by one against Opening balances, `ChangeAction = System`, reason "Existing debts no longer count as income or expenses". This fixes the "debt shown as income" report.

| Operation | Postings |
| --- | --- |
| Correct an expense of 30 to 50 | Reversal (same date): Bank +30 · Food −30. Revision 2: Food +50 · Bank −50 |
| Delete it | Reversal: Bank +50 · Food −50 |
| "I already owed Sofi 50" (new rule) | Me: Opening balances +50 · I owe [Sofi] −50. Sofi, if linked: Owed to me [me] +50 · Opening balances −50 |

### 15.3 Shared operations (Debts module)

* `DebtOperation` gained `EntryId`, `Revision`, `ReplacesOperationId`, `PendingChangeId`, `IsDisputed` and `WalletId` (settlements: the initiator's wallet); statuses `Superseded` and `Deleted`. `Debt` gained status `Superseded`.
* `OperationChange` (`Revise`, `Delete`, `Restore`; `Pending → Applying → Applied | Failed`) stores the requested values (`Draft`, JSON), a preview of the new version and one `ChangeApproval` per linked user involved other than the author.
* Flow: request (validations: involved user, no pending change, debts not settled, no locked period) → approvals → when everybody answered, the new version is built **from the initiator's point of view** and the ledger is asked to update the author and those who accepted → on success the old version becomes `Superseded`/`Deleted`, its debts `Superseded`, the new one `Completed` (and `IsDisputed` if anybody rejected). On failure (e.g. not enough money, a locked period) the change is `Failed` and everything stays as it was.
* While a change is pending, settling the debts of that operation (or anything in that group) is refused (`operation.change_pending`).

### 15.4 API

| Method | Route | Description |
| --- | --- | --- |
| PUT / DELETE | `/api/transactions/{id}` (DELETE body `{ reason }`) | Correct or delete an income, expense or transfer (delete also balance adjustments) |
| POST | `/api/transactions/{id}/restore` | Restore a deleted one |
| GET | `/api/transactions/{id}/history`, `/api/transactions/activity` | Versions; my recent changes |
| GET/PUT | `/api/settings/books-lock` | Lock date (`null` = nothing locked) |
| GET | `/api/debts/operations?kind&groupId&personId&includeHistory&page`, `/{id}`, `/{id}/history` | Operations with their flags (`canEdit`, `canDelete`, `canRestore`, `blockedReason`, `pendingChange`, `isDisputed`) |
| PUT / DELETE | `/api/debts/operations/{id}`, POST `/{id}/restore` | Request a change; returns the change (`Pending` with who must answer, or `Applying`) |
| GET / POST | `/api/debts/changes?filter=ToReview|All`, `/api/debts/changes/{id}/accept|reject` | Review changes |
| GET | `/api/debts/activity` | Changes I requested or was asked about |

### 15.5 Clients

* **Web:** Edit / Delete / History on transactions and a *Show changes* toggle; an operations list in Debts and in each group (Edit, Delete, Restore, History, *Undo settlement*) with *Change pending* and *Disputed* badges; *Changes to review* page (badge in the menu and a banner in Debts); *Activity* and *Settings* pages; a reusable `ReasonField` and reason dialog. English and Spanish.
* **Console:** Transactions (edit, delete, restore, history), Debts and Groups (*Loans, splits and settlements* / *Expenses and payments*), and the main menu's *Changes to review*, *Activity* and *Settings*.

### 15.6 Verification

* **Unit:** reversal, revisions, lock rule, change approvals (accept, reject, disputed, answered once), one pending change at a time, reopening settled debts, existing debts against Opening balances.
* **Integration (Testcontainers):** edit/delete/restore an expense counted once in reports with a 3-row history; an overdrawing correction refused; the lock date blocks record, edit and delete; a loan change with a linked user accepted (both updated) and then rejected (disputed, books differ), and refused for a linked user's locked period; a settled loan blocked until the settlement is deleted; an existing debt not counted as income, edited, deleted and restored; a group expense edit recalculating group balances.
* **Migrations** were run on a copy of a development database: opening-balance chains and the existing-debt migration were checked row by row.
* **Web:** unit specs and Playwright (edit/delete/restore with history, lock date, a loan change accepted by the linked user); **console:** a scripted run of the same flows. Both against an isolated database, RabbitMQ virtual host and API (port 5219).

### 15.7 Known limitations

* Settlements recorded **before** this branch stored the initiator's wallet only when the initiator paid. If somebody else edits one where the initiator received the money, the initiator gets it in *Pending wallet assignments*.
* A percentage split is shown for editing with the resulting amounts (percentages are not stored).
* When a non-initiator edits a split outside groups, people who are only in the initiator's contacts may show as "(unknown)".
* A disputed operation shows the version the author applied in the Debts screens; the books of whoever rejected keep the previous one.

## 16. Investments

Added on branch `feature/investments`, to the API, the web app and the console. A user records that they bought (or already owned) an amount of an asset (shares, ETFs, crypto…) at a price, sells it, and updates its price. Holdings count toward net worth as a separate kind of asset, with charts of how they change over time. Market prices are entered by hand for now.

### 16.1 Decisions (asked before building)

| Topic | Decision |
| --- | --- |
| Books vs value | The **ledger records cost** (what was paid). **Market value** = quantity × valuation price, computed by the Investments module. No revaluation postings: a future real-time price feed will not create ledger entries. |
| Valuation price at a moment | The latest **sell** price before it (what the holding would fetch), else the latest **buy** price, else the average cost. |
| Prices | A buy records the buy price and optionally the current sell price; a sale records the sell price and optionally the current buy price. **Update price** records buy and/or sell prices without moving money. Prices can be backdated (not in the future). |
| Precision | Quantity `numeric(28,12)`. Unit price up to **8 decimals** (some assets cost less than a cent). The total, `round(quantity × price, 2)`, is what the ledger posts. |
| Selling | **Average cost**: the cost removed is `round(costBasis × sold / held, 2)`, or the whole cost basis when everything is sold. The difference with what was received goes to **Investment gains** (Equity), so it is not income or expense in the reports. |
| Currency | Every asset has a **quote currency**. Any wallet can pay or receive: in another currency the user enters the wallet amount and the difference goes through **Currency exchange** (like transfers). |
| Already owned | "Already owned" holdings are posted against **Opening balances** (no wallet), like existing debts. |
| Edits | **Append-only** and **no backdating**: a trade can't be dated before the latest recorded trade of the asset (average costs never need recomputing); mistakes are fixed with an opposite trade. One pending trade per asset at a time. |
| Catalogue | **Per user**: name, optional symbol, type (`Stock`, `Etf`, `Crypto`, `Bond`, `Fund`, `Other`) and quote currency (fixed). Assets can be renamed and archived once nothing is held. One holding per asset (no broker/location). |
| Fees | Not modelled yet (record a normal expense). |
| Dashboard | Per currency: **Money (net)** (the ledger's net, which excludes investments), **Investments** (market value) and **Total**. The net-worth chart stacks money and investments; an *Investments over time* chart shows value against cost. |

### 16.2 Accounting

T = total in the asset currency, C = cost removed (average cost of the units sold).

| Operation | Postings |
| --- | --- |
| Buy 2 BTC at 100 USD from a USD wallet | Investment[BTC] +200 · Wallet −200 |
| Buy 1.5 AAPL at 200 USD paying 360,000 ARS | Investment +300 USD · Currency exchange −300 USD · Currency exchange +360,000 ARS · Wallet −360,000 ARS |
| Sell 1 BTC at 150 (average cost 100) | Wallet +150 · Investment −100 · Investment gains −50 (a loss is a debit; omitted when 0) |
| Sell into a wallet in another currency (R received) | Investment −C · Investment gains −(T−C) · Currency exchange +T · Currency exchange −R · Wallet +R |
| Already owned 3 ETF units at 10.50 EUR | Investment +31.50 · Opening balances −31.50 |

* The Transactions summary and report keep **Net = money only** (wallets, owed to me, I owe, pending): Investment and Investment gains are not in those sums. Buying moves money into the Investment account, so the ledger's net drops by T and the investments' market value (T at the purchase price) takes its place in the total.
* Investment transactions appear in the transaction list (kinds `InvestmentBuy`, `InvestmentSell`, `InvestmentOpening`) but cannot be edited or deleted there.
* `ReportBuckets` (days up to 62 days, months above, cut in the user's time zone) moved to `BuildingBlocks.Application` so both reports produce aligned buckets for the stacked chart.

### 16.3 Flow

```text
POST /api/investments/assets → Investments ──AssetCreated──► Accounts: Investment account (ReferenceId = asset) ──AccountCreated──► Transactions projection
POST /api/investments/trades/{buy|sell|opening} → Investments: checks (asset owned and active, no pending trade, date ≥ latest trade,
     sell quantity ≤ held), computes T and C, stores the trade (Pending) and reserves the asset
  ──InvestmentPostingRequested──► Transactions: postings above through LedgerWriter (no overdraft, books lock, wallet ownership and currency)
     ├─ OK   ──InvestmentPostingCompleted(walletCurrency)──► Investments: trade Completed, holding updated, trade prices recorded
     └─ fail ──InvestmentPostingRejected(reason)──► Investments: trade Rejected, asset released
```

* The **ledger validates the wallet** (owner, archived, currency): a wallet projection in Investments would start empty for wallets created before the module existed (their `WalletCreated` events were consumed long ago). The completion event reports the wallet currency back for display.
* A trade on a just-created asset waits (retries) until its Investment account reaches the ledger (`NotYetAvailableException`).
* Two trades of the same asset at the same time: the `xmin` row version of the asset makes the second fail with `investment.trade_pending`.

### 16.4 API

| Method | Route | Description |
| --- | --- | --- |
| GET/POST | `/api/investments/assets?includeArchived`, `/{id}` | Assets with their holding and valuation (`quantity`, `costBasis`, `averageCost`, `buyPrice`, `sellPrice`, `valuationPrice`, `valuationSource`, `marketValue`, `unrealizedGain`, `realizedGain`) |
| PUT / POST | `/api/investments/assets/{id}`, `/{id}/archive` | Rename or change the symbol; archive (nothing held) |
| GET/POST | `/api/investments/assets/{id}/prices` | Known prices; record `{ buyPrice?, sellPrice?, occurredAt? }` |
| POST | `/api/investments/trades/buy`, `/sell`, `/opening` | `202` with a `Pending` trade; `walletAmount` when the wallet's currency differs; optional `sellPrice` (buy, opening) or `buyPrice` (sell) |
| GET | `/api/investments/trades?assetId&page&pageSize`, `/{id}` | Trades newest first; one trade (`Pending`, `Completed`, `Rejected` with the reason) |
| GET | `/api/investments/summary` | Per currency: value, cost, unrealized and realized gains |
| GET | `/api/investments/report?from&to&currency&timeZone` | Value and cost at the end of each bucket (same buckets as `/api/reports`), value per asset, realized gains in the period |

Error codes: `investment.trade_pending`, `investment.backdated`, `investment.insufficient_quantity`, `investment.total_zero`, `investment.not_empty`, `investment.archived`, `investment.quantity_too_many_decimals`, `investment.price_too_many_decimals`, `investment.wallet_required`, `investment.opening_with_wallet`. The ledger may reject a trade with `ledger.insufficient_funds`, `ledger.currency_mismatch` or `books.locked`.

### 16.5 Clients

* **Web:** *Investments* page (totals per currency, holdings table with Buy / Sell / Update price / History / Edit / Archive, trades list), a trade dialog that can add a new asset on the way and shows the total, the latest price and the date limit, a price dialog and an asset history (trades and prices). Trades are tracked like debt operations (toast when recorded or refused). Dashboard cards, stacked net-worth chart, *Investments over time* and a holdings card. English and Spanish.
* **Console:** main menu *Investments* (holdings, buy, sell, already owned, update price, trades, price history, new/edit/archive asset); the summary adds investments and the total; the reports add investments over time with the total net worth.

### 16.6 Verification

* **Unit:** average cost (partial and full sale), rounding of totals, losses, no backdating, insufficient quantity, one pending trade and release on rejection, precision rules, wallet rules, trade prices and counter-prices, valuation fallback (sell → buy → cost), archive rules.
* **Integration (Testcontainers):** buy at cost with a sell price (value 190 on a cost of 200), money-only ledger net, manual price, partial sale with a realized gain that is not income, aligned report buckets, investment transactions not editable, overdraft rejected, selling too much and backdating refused; cross-currency purchase through Currency exchange (rejected without the wallet amount); already owned holding; lock date refusing a trade; archive rules.
* **Real stack:** a copy of the development database (migration and the *Investment gains* backfill for existing users checked), an isolated RabbitMQ virtual host and API on port 5219. Web: Playwright (new asset bought, price updated, partial sale, wallet balance, dashboard) and the full E2E suite; console: a scripted run (already owned → buy → sell → update price → holdings → trades → summary).

### 16.7 Known limitations and next steps

* No fees, dividends, staking income or splits of shares.
* Trades cannot be edited, deleted or backdated before the latest trade of the asset; fix mistakes with an opposite trade.
* Prices are manual. A market feed can later add `AssetPrice` rows (source `Feed`) by symbol without touching the ledger.
* Average cost only (no FIFO lots for taxes); one holding per asset (no brokers/locations).
* The ledger's descriptions of trades without one are generated in English ("Buy 0.5 BTC").

## 17. Linking contacts that are already in use

Added on branch `feature/link-used-contacts`, to the API, the web app and the console. Use case: I have debts with a contact who later creates a Ledger account; I want my contact to become their account. Before, linking was refused once the contact took part in a debt or a group (`contact.in_use`), because the contact's **person id** (minted for them) is stored in ledger postings, debts, operations, group members and other users' mirrored contacts.

### 17.1 Decisions (asked before building)

| Topic | Decision |
| --- | --- |
| Consent | Linking a **used** contact creates a **link request**; the user sees it in *Changes to review* with a **preview** of what their books will get, and accepts or rejects it. Nothing changes until they accept. The owner can cancel it. Unused contacts are still linked right away. |
| History in their books | **Every current operation** involving the person (loans, existing debts, splits, settlements, group expenses and payments) is **copied** into the user's books as an existing debt: their positions against **Opening balances** (neither income nor expense, no wallet), with the **original date**, correlated to the original operation. Settled pairs cancel out. |
| Duplicates | If the owner already had another contact for that user (e.g. the old workaround), the old contact is **merged** into it: one person, one balance; the old one is hidden but its history kept. |
| Ledger immutability | Old postings keep the old person id. In Transactions it becomes an **alias** of the user (`counterparties.MergedIntoPersonId`): the summary and the contact filter resolve it. No posting is rewritten. |

### 17.2 Flow

```text
POST /api/contacts/{id}/link (used contact) → Contacts: ContactLinkRequest(Pending) ──ContactLinkRequested──► Debts (who may see the preview)
GET  /api/debts/link-requests/{id}/preview → what the user would owe or be owed, per person or group and currency
POST /api/contacts/link-requests/{id}/accept → Contacts: Applying ──PersonMergeRequested──► Debts
  Debts: refuses (→ PersonMergeFailed) if an operation with the person is still pending, has a pending change, a debt is being
         settled, the user already takes part in one of those operations or groups as themselves, or another merge is running.
         Stores a PersonMerge (operations and changes involving the person are refused meanwhile: contact.link_pending) and builds one
         copy per operation ──LedgerOpeningsRequested──► Transactions: one InitialDebt transaction per copy, all or nothing, through
         LedgerWriter (the user's lock date applies)
     ├─ OK   ──LedgerOpeningsCompleted──► Debts: re-keys the person to the user in debts, operations (sides, payers, shares), contact and
     │        group-member projections ──PersonMerged──► Contacts: request Applied; every contact of that person (the owner's and
     │        other users' mirrored ones) is linked to the user or merged into an existing contact for them ──ContactLinked──►
     │        Transactions (alias), Groups, Debts; the user gets contacts for the people they now share debts with.
     │        Groups: memberships of the person become the user ──GroupMemberAdded──► the usual consumers (the group is shared)
     └─ fail ──LedgerOpeningsRejected──► Debts ──PersonMergeFailed(reason)──► Contacts: request Failed, contact unchanged
```

* Because each copy is correlated to its operation, a later **edit, delete or restore** of that operation reverses the copy in the user's books like any version, and the user now confirms changes like any linked user. A deleted settlement reopens debts in both books.
* Person-id re-keying uses explicit SQL (`IDebtsDbContext.ReplacePersonAsync`, `IGroupsDbContext.ReplaceMemberAsync`): those ids are part of keys (group members) or owned rows (payers, shares).

### 17.3 Accounting (the user's books)

| Shared operation (from the owner's books) | Copy in the linked user's books |
| --- | --- |
| Alice lent 50 to Pedro | Opening balances +50 · I owe [Alice] −50 |
| Alice paid a 60 dinner, split equally with Pedro | Opening balances +30 · I owe [Alice] −30 |
| Pedro paid Alice back 50 (settlement) | I owe [Alice] +50 · Opening balances −50 |
| Group hotel 100 paid by Alice, Pedro's share 50 | Opening balances +50 · I owe [Trip] −50 |

### 17.4 API

| Method | Route | Description |
| --- | --- | --- |
| POST | `/api/contacts/{id}/link` | Unused contact: linked now. Used contact: `pendingLinkRequestId` (a request) |
| GET | `/api/contacts/link-requests?includeClosed` | Requests I sent (`Outgoing`) or have to answer (`Incoming`) |
| POST / DELETE | `/api/contacts/link-requests/{id}/accept`, `/reject`; `DELETE /{id}` | Answer (target user) or cancel (owner); accepting returns `Applying`, then `Applied` or `Failed` with a reason |
| GET | `/api/debts/link-requests/{id}/preview` | Operations copied and balances per person or group |

Error codes: `contact.link_pending`, `contact.merged`, `link.answered`, `link.not_for_you`, `link.not_yours`.

### 17.5 Clients

* **Web:** *Link* is available on used contacts (the dialog explains the request; *Waiting for them to accept* badge and *Cancel request*). *Changes to review* lists link requests with the preview and Accept / Reject; the menu badge counts them; accepting waits for the result (toast). English and Spanish.
* **Console:** *Contacts* links used contacts (sends the request) and cancels requests; *Changes to review* shows incoming link requests with the preview first.

### 17.6 Verification

* **Unit:** request lifecycle (one pending, who may answer or cancel, answered once, failure frees the contact), merge into an existing contact, copies (loan, split, settlement, group expense and payment; every copy balances against Opening balances).
* **Integration:** loan + split with an unregistered contact, the person registers, link request, preview (−80), accept: the user's books owe 80 as two existing debts with the original dates and no income/expense; the owner sees one balance and the contact filter finds the old postings; the owner edits the old loan and the user confirms (both books change); the user settles from a wallet and both reach zero. Plus: reject and cancel; a closed period in the user's books makes it fail; merging into an existing contact for the user; a group member gets the group and their position.
* **Real stack** (copy of the development database, isolated vhost, API on 5219): migrations applied; Playwright (link request, preview, accept) and the full E2E suite; scripted console run; the ledger checked row by row (the owner's postings untouched, the user's copies new, the old person id kept as an alias).

### 17.7 Known limitations

* Copies have no wallet or category in the user's books (they are existing debts). Later edits of those operations post the full new version (including the user's share as an expense), like any shared change.
* A person can be merged into one user only, and unlinking is not supported.
* Refused when the user already appears as themselves in an operation or group with that contact (the merge would make them their own debtor).

---

## 18. Deployment (AWS)

Added on branch `feature/aws-deployment`. Goal: use the app from the internet (phone included) over HTTPS. One user for now, maybe some invited friends later. Everything is in `infra/terraform` (infrastructure), `infra/keycloak-prod` (Keycloak image and realm) and `.github/workflows` (CI and deploy).

> **Status:** kept for the future. The proof of concept runs on a single VPS instead (§19, cheaper and simpler), so this deploy is **only started by hand**; nothing deploys to AWS when `main` changes.

### 18.1 Decisions (asked before building)

| Topic | Decision |
| --- | --- |
| Where | AWS **eu-west-1 (Ireland)**: the cheapest European region with every service, ~40 ms from Spain. |
| Compute | **ECS Fargate**, ARM64 (cheaper), one task per service. No servers to patch. |
| Database | **RDS PostgreSQL 17, db.t4g.micro**. Aurora Serverless v2 was considered, but its auto-pause never kicks in here: every module's `OutboxProcessor` polls every 250 ms and Keycloak keeps connections open, so it would sit at its ~$50/month floor. Moving to Aurora later is a snapshot restore. |
| Messaging | **RabbitMQ in a container** (same broker as local and tests). SQS would save ~$8/month but needs a second MassTransit transport that the tests don't cover. Its data lives on **EFS**, so queued messages survive a new task. |
| Users | **Invite only**: no self-registration, no test users. You create users in the Keycloak admin console. |
| Data | Production **starts empty**. |
| Infrastructure | **Terraform**, applied by hand. State in S3 (lock file, no DynamoDB). |
| Deploys | **GitHub Actions**, started by hand (*Deploy (AWS)*): tests, images, migrations, rolling update. It used to run on every push to `main`; it became manual when the PoC moved to a VPS. |
| Domain | Bought in **Route 53**: the app at `ledger.<domain>`, Keycloak at `auth.<domain>`. |

Approximate cost: **$90–100 a month**. Fargate ~$35 (api and keycloak 0.5 vCPU / 1 GB, web and rabbitmq 0.25 vCPU / 0.5 GB), public IPv4 addresses ~$20 (AWS charges each one), load balancer ~$20, RDS ~$16 (free for the first 12 months on a new account), plus Route 53, CloudWatch, ECR and EFS (a few dollars). A budget alert emails you at 80 % of `monthly_budget_usd` (default $120) and when the forecast exceeds it.

### 18.2 Architecture

```text
Route 53  ledger.<domain>, auth.<domain> ──► ALB (HTTPS with ACM; HTTP redirects to HTTPS)
  ledger.<domain>  /api/*, /health ─► api       (Ledger.Api, ASPNETCORE_ENVIRONMENT=Production)
  ledger.<domain>  anything else   ─► web       (nginx: /en/, /es/, /config.json)
  auth.<domain>    /admin/*, /realms/master/* ─► keycloak, only from admin_cidrs (others get 403)
  auth.<domain>    anything else   ─► keycloak  (Keycloak 26, `start --optimized`)
Private DNS (Cloud Map): rabbitmq.ledger.internal:5672, keycloak.ledger.internal:8080
rabbitmq (no load balancer) + EFS at /var/lib/rabbitmq (node name fixed to rabbit@localhost)
RDS PostgreSQL (private subnets): databases "ledger" (module schemas) and "keycloak"
One-off tasks: db-init (creates the keycloak database if missing) and migrator, run by every deploy
```

* **No NAT gateway** (~$35/month): tasks run in public subnets with a public IP so they can pull images, and their security group accepts traffic only from the load balancer and from other tasks. RDS is private and only accepts the tasks.
* **Tokens**: Keycloak issues them for `https://auth.<domain>/realms/ledger` (its public URL). The API validates that issuer but downloads the signing keys from `http://keycloak.ledger.internal:8080` (`Authentication__MetadataAddress` + `KC_HOSTNAME_BACKCHANNEL_DYNAMIC`), the same pattern as `docker-compose.yml`.
* **Secrets**: generated by Terraform and stored in SSM Parameter Store (SecureString) under `/ledger/...`: database password and connection string, RabbitMQ password and URL, Keycloak bootstrap admin password. ECS injects them as environment variables. They are also in the Terraform state (S3, encrypted, private).
* **Connections**: db.t4g.micro allows about 80. The API's connection string has `Maximum Pool Size=30` and Keycloak uses `KC_DB_POOL_MAX_SIZE=15`.
* **Backups**: RDS automated backups, 7 days, point-in-time restore. Deletion protection on, final snapshot on destroy.

### 18.3 What changed in the app

| Area | Change |
| --- | --- |
| API image | `src/Ledger.Api/Dockerfile` has a `production` target (Release, no debugger, non-root). The default target is still the Debug + vsdbg image used by compose. |
| Images | Build stages run on the build machine's architecture (`--platform=$BUILDPLATFORM`, `dotnet publish -a $TARGETARCH`), so CI builds ARM64 images on an x64 runner without emulation. |
| Web | The Keycloak issuer is no longer built in: `AuthService` reads `/config.json` at startup. In development it is `public/config.json`; in the container nginx generates it from `KEYCLOAK_ISSUER`. `nginx.conf` became `nginx.conf.template` (`API_UPSTREAM`, `KEYCLOAK_ISSUER`, defaults in the Dockerfile). `requireHttps` is on when the issuer is HTTPS. |
| Keycloak | `infra/keycloak-prod`: optimized image for PostgreSQL with health checks (port 9000) and a production realm: same clients and mappers, no users, no self-registration, no password reset (no email yet), brute-force protection, `sslRequired: external`, no `ledger-scalar` (Scalar is development only). `ledger-web` redirect URIs come from `LEDGER_WEB_URL` (Keycloak replaces `${...}` when importing). |
| Console | No change: point it at production with `Api__BaseUrl=https://ledger.<domain> Keycloak__Authority=https://auth.<domain>/realms/ledger`. |

The realm is imported only when it doesn't exist. After the first start, change it in the admin console (or update the file and import it by hand).

### 18.4 First deployment

1. **AWS account**: create it, turn on MFA for the root user, and create an administrator user (IAM Identity Center or IAM) for the CLI (`aws configure` / `aws sso login`).
2. **Domain**: buy it in Route 53 (Registered domains). This creates its hosted zone.
3. **Bootstrap** (state bucket and the deploy role for GitHub):
   ```bash
   cd infra/terraform/bootstrap
   terraform init && terraform apply
   ```
   Keep `bootstrap/terraform.tfstate` safe (it is git-ignored). In GitHub, add the repository **variable** `AWS_DEPLOY_ROLE_ARN` = the `deploy_role_arn` output.
4. **Infrastructure**:
   ```bash
   cd infra/terraform
   cp terraform.tfvars.example terraform.tfvars    # domain, admin_cidrs (your IP), alert_email
   terraform init -backend-config="bucket=<state_bucket output>"
   terraform apply
   ```
   The services start without images and keep retrying until the first deploy pushes them. Confirm the budget alert email AWS sends you.
5. **First deploy**: *Actions → Deploy (AWS) → Run workflow* on `main`.
6. **Keycloak**: open `https://auth.<domain>/admin/` from an address in `admin_cidrs`. Log in as `admin` with the password from
   `aws ssm get-parameter --with-decryption --name /ledger/keycloak/admin-password --query Parameter.Value --output text`.
   Create a permanent admin user in the *master* realm, add OTP to it, log in with it and delete the bootstrap `admin`. Then, in realm **ledger**, create your user (*Users → Add user*, then *Credentials → Set password*).
7. Open `https://ledger.<domain>` (phone included) and log in.

**Inviting a friend**: in realm `ledger`, *Users → Add user*, set a **temporary** password and send it to them; Keycloak asks them to change it at their first login.

### 18.5 Every deploy

`.github/workflows/deploy-aws.yml`, run by hand from `main` (*Actions → Deploy (AWS) → Run workflow*; one at a time). The AWS role only trusts `main`, so run it from that branch:

1. Runs CI (`ci.yml`: build, unit, architecture and integration tests, web tests, EN/ES build). CI also runs on pull requests.
2. Assumes the AWS role through GitHub OIDC (no AWS keys in GitHub; the role only trusts `main`).
3. Builds the api (`--target production`), migrator, web and keycloak images for ARM64 and pushes them to ECR as `<commit sha>` and `latest`.
4. Runs **db-init** and then the **migrator** as one-off tasks; the deploy stops if either fails (their logs are printed).
5. Forces a new deployment of keycloak, api and web (they use `:latest`) and waits until they are stable. RabbitMQ (public image) is not restarted.

Terraform is never run by the pipeline. Changing the infrastructure (sizes, environment variables, new secrets) is `terraform plan` / `terraform apply` by hand. Because task definitions reference `:latest`, Terraform and deploys never undo each other.

**Rolling back**: point `latest` at an earlier commit and force a new deployment:
```bash
aws ecr batch-get-image --repository-name ledger/api --image-ids imageTag=<sha> --query 'images[0].imageManifest' --output text > manifest.json
aws ecr put-image --repository-name ledger/api --image-tag latest --image-manifest file://manifest.json
aws ecs update-service --cluster ledger --service api --force-new-deployment
```
Migrations are not rolled back automatically: write a new migration instead (Migrations.md).

### 18.6 Operating it

* **Logs**: CloudWatch log group `/ecs/ledger` (14 days), one stream prefix per service: `aws logs tail /ecs/ledger --follow --log-stream-name-prefix api`.
* **Shell in a task** (ECS Exec is on): `aws ecs execute-command --cluster ledger --task <id> --container api --interactive --command sh`.
* **Dead-lettered messages**: the management UI is not exposed. Use ECS Exec on the rabbitmq task: `rabbitmqctl list_queues name messages | grep _error`.
* **Restore**: RDS console → *Restore to point in time* creates a new instance; point the connection strings at it (or swap it in with Terraform).
* **Updating Keycloak**: change `KEYCLOAK_VERSION` in `infra/keycloak-prod/Dockerfile` and deploy.
* **Removing everything**: turn off `deletion_protection` on the database, then `terraform destroy` (a final snapshot is kept).

### 18.7 Verified before merging

* The production Keycloak image starts against a new PostgreSQL database, imports the realm with the `LEDGER_WEB_URL` redirect URIs, keeps the public issuer and serves its keys on the internal address.
* The production API image (`ASPNETCORE_ENVIRONMENT=Production`) on a scratch database and vhost: `/health` 200, `/api/...` 401 without a token and 200 with a token issued by that Keycloak (public issuer, keys downloaded internally), Scalar not exposed.
* The web image serves `/config.json` from `KEYCLOAK_ISSUER`; the migrator image cross-builds for another architecture; `terraform validate` passes for both stacks.
* Not verified yet: a real `terraform apply` and a full pipeline run (they need the AWS account and the domain).

---

## 19. Deployment (VPS)

Added on branch `feature/vps-deployment`. The proof of concept runs on **one server** with Docker Compose: cheaper and simpler than AWS (§18), which stays ready for later and is only deployed by hand. Files: `deploy/vps/` (stack, Caddy, scripts, cloud-init) and `.github/workflows/deploy-vps.yml`.

### 19.1 Decisions (asked before building)

| Topic | Decision |
| --- | --- |
| Server | **Hetzner CX23** (2 shared vCPU, 4 GB, 40 GB NVMe, x86-64), Ubuntu 24.04. Enough for one user plus a few friends: the stack uses ~1.5 GB, and 2 GB of swap is a safety net. If it gets tight, resize the server in the Hetzner console (same disk and IP). |
| Setup | **cloud-init** (`deploy/vps/cloud-init.yaml`, pasted when creating the server) plus the runbook below. |
| HTTPS | **Caddy** with automatic Let's Encrypt certificates, for `ledger.<domain>` and `auth.<domain>` (a domain you buy; DNS anywhere). |
| Images | Built by GitHub Actions for `linux/amd64` and pushed to **GitHub Container Registry**; the server only pulls. |
| Keycloak admin | Admin console and master realm only from **`ADMIN_CIDRS`** (Caddy answers 403 to everyone else). Invite only, as in §18. |
| Backups | **Hetzner server backups** (7 daily) **and** a nightly **`pg_dump`** of both databases, kept 14 days on the server and 30 days in **Hetzner Object Storage**. |
| AWS | `.github/workflows/deploy-aws.yml` only runs by hand; nothing deploys to AWS when `main` changes. |

Approximate cost (excluding VAT): CX23 €5.99 + backups (20 %) €1.20 + Object Storage ~€5 + IPv4 ~€0.50 + domain ~€1 ≈ **€14 a month**.

### 19.2 Architecture

```text
Internet ──► :80/:443 Caddy (Let's Encrypt; HTTP redirects to HTTPS)
  ledger.<domain>  ─► web (nginx: /en/, /es/, /config.json; proxies /api and /health to api:8080)
  auth.<domain>    /admin*, /realms/master* ─► keycloak, only from ADMIN_CIDRS (others: 403)
  auth.<domain>    anything else            ─► keycloak:8080
Internal network only: api, keycloak, postgres (databases "ledger" and "keycloak"), rabbitmq
Volumes: postgres_data, rabbitmq_data, caddy_data (certificates), caddy_config
```

* Same images as §18: the api `production` target, the migrator, the web image (`KEYCLOAK_ISSUER` → `/config.json`) and `infra/keycloak-prod` (realm imported on the first start, `LEDGER_WEB_URL` for the redirect URIs).
* Tokens are issued for `https://auth.<domain>/realms/ledger`; the API reads the signing keys from `http://keycloak:8080` (same pattern as `docker-compose.yml` locally).
* Only Caddy publishes ports. The firewall (ufw + Hetzner Cloud Firewall) allows 22, 80 and 443.
* Create **A records only** (no AAAA): Docker doesn't keep the client's IPv6 address, so IPv6 visitors would always fail the admin allowlist.
* RabbitMQ has a fixed host name, so its queues (in `rabbitmq_data`) survive a new container.
* Container logs are rotated (10 MB × 5 per container); the backup log is in `/var/log/ledger/backup.log`.
* Memory limits (for a 4 GB server): keycloak 1 GB (Java heap ~700 MB), api 768 MB, postgres 512 MB, rabbitmq 384 MB, web 128 MB. Check real use with `docker stats --no-stream`.

### 19.3 Files

| File | Purpose |
| --- | --- |
| `deploy/vps/docker-compose.yml` | The stack. Runs from `/opt/ledger` with `--env-file .env --env-file release.env`. |
| `deploy/vps/Caddyfile` | Hosts, HTTPS and the admin allowlist. |
| `deploy/vps/.env.example` | Settings and secrets; the real `.env` exists only on the server (mode 600). |
| `deploy/vps/initdb/01-keycloak.sql` | Creates the `keycloak` database the first time PostgreSQL starts. |
| `deploy/vps/deploy.sh` | `./deploy.sh <tag>`: writes `release.env`, pulls, starts postgres and rabbitmq, runs the migrator (if it fails, nothing else changes), starts the rest. |
| `deploy/vps/backup.sh` | Nightly backup (cron at 03:30 UTC from cloud-init). |
| `deploy/vps/import-dump.sh` | Replaces the `ledger` database with a dump from another environment and maps users to their Keycloak accounts on the server (§19.8). |
| `deploy/vps/link-user.sh` | Links one imported user to their Keycloak account later, before their first login (§19.8). |
| `deploy/vps/cloud-init.yaml` | Server setup: user `deploy` (SSH key only), no root or password logins, ufw, fail2ban, automatic security updates, Docker + Compose, rclone, 2 GB swap, `/opt/ledger`, the backup cron. |

### 19.4 First deployment

1. **Domain**: buy one (any registrar).
2. **SSH keys**: you need two key pairs: yours, and one for GitHub Actions (`ssh-keygen -t ed25519 -f ledger-deploy -N ""`).
3. **Server**: in Hetzner Cloud, create a **CX23**, Ubuntu 24.04, location Nuremberg/Falkenstein/Helsinki (the CX line is only sold in Germany and Finland).
   - Paste `deploy/vps/cloud-init.yaml` as *Cloud config* after replacing the two `ssh_authorized_keys` placeholders with the public keys.
   - Turn on **Backups**.
   - Add a **Firewall** allowing TCP 22, 80, 443 and UDP 443.
   - Wait for cloud-init to finish (`ssh deploy@<ip>`, then `cloud-init status --wait`).
4. **DNS**: A records `ledger.<domain>` and `auth.<domain>` → the server's IPv4 address (no AAAA records).
5. **Object Storage**: create a bucket (e.g. `ledger-backups`, private) in the same location, and S3 credentials for it.
6. **Settings on the server**:
   ```bash
   ssh deploy@<ip>
   cd /opt/ledger
   # copy deploy/vps/.env.example here as .env (the first deploy also copies it), then fill it in:
   nano .env && chmod 600 .env       # DOMAIN, ADMIN_CIDRS, passwords (openssl rand -hex 24), BACKUP_S3_*
   ```
   The passwords are only used to create the database, the broker user and the Keycloak bootstrap admin on the first start. Changing them later means changing them inside those services too.
7. **GitHub**: create the environment **`dev`** (*Settings → Environments*; the deploy job uses it) and add to it:
   - secrets: `VPS_HOST` (the server's IP or host name only), `VPS_SSH_KEY` (the whole private GitHub Actions key), `VPS_KNOWN_HOSTS` (`ssh-keyscan -t ed25519 <ip>`)
   - variable: `VPS_DOMAIN` (the bare domain, e.g. `example.com`)

   The job checks the four values first and stops with a clear message if one is missing. GitHub turns a missing secret into an empty string, which otherwise fails later with errors like `Could not resolve hostname`. Optional: under the environment's *Deployment branches*, allow only `main`.
8. **First deploy**: merge into `main` or run *Actions → Deploy (VPS) → Run workflow*. Caddy gets the certificates in the first minute.
9. **Keycloak**: open `https://auth.<domain>/admin/` from an address in `ADMIN_CIDRS` and log in as `admin` with `KEYCLOAK_ADMIN_PASSWORD`.
   - Create your own admin in the *master* realm, add OTP, log in with it and delete `admin`.
   - In realm **ledger**, create your user (*Users → Add user*, then *Credentials → Set password*).
10. Open `https://ledger.<domain>` (phone included).

**Inviting a friend**: realm `ledger` → *Users → Add user* with a **temporary** password; Keycloak asks them to change it at the first login.

### 19.5 Every deploy

`.github/workflows/deploy-vps.yml`, on every push to `main` (and by hand), one at a time:

1. CI (`ci.yml`): .NET build and every test, web unit tests and the EN/ES build.
2. Builds api, migrator, web and keycloak for `linux/amd64` and pushes them to `ghcr.io/<owner>/ledger-<name>` as `<commit sha>` and `latest`.
3. In the GitHub environment `dev` (its secrets, and the deployment history on the repository page), over SSH as `deploy`:
   - copies the stack files to `/opt/ledger`
   - logs in to ghcr.io with the job's token (sent through stdin) and runs `./deploy.sh <sha>`
   - logs out
4. Smoke check: `https://ledger.<domain>/health` must answer 200.

**Rolling back**: on the server, `cd /opt/ledger && ./deploy.sh <older sha>`. If the GHCR packages are private, log in first with a personal access token that has `read:packages`: `docker login ghcr.io -u <user>`. Migrations are not reversed (add a new migration instead; Migrations.md).

### 19.5b Passkeys and the installed app

* **Passkeys (Face ID):** after deploying §21, enable them once in the admin console (the realm is not re-imported). In realm `ledger`:
  1. *Authentication → Policies → Webauthn Passwordless Policy* → **Enable Passkeys** on, *Relying party entity name* "Ledger".
  2. *Required actions* → **Webauthn Register Passwordless** enabled, not default.
* **Installed app:** nothing to do on the server. Caddy serves HTTPS (needed by the service worker) and nginx sends the right cache headers (§21.3). After each deploy, open apps show "A new version is available — Reload".

### 19.6 Operating it

* **Status and logs**:
  ```bash
  cd /opt/ledger
  docker compose --env-file .env --env-file release.env ps
  docker compose --env-file .env --env-file release.env logs -f api
  ```
* **Backups**: `ls /opt/ledger/backups`, `tail /var/log/ledger/backup.log`, or run one now with `/opt/ledger/backup.sh`.
* **Restore** a dump. The example is `ledger`; `keycloak` works the same way. Stop the services that use the database first:
  ```bash
  C="docker compose --env-file .env --env-file release.env"
  $C stop api web
  $C exec -T postgres dropdb -U postgres --force ledger
  $C exec -T postgres createdb -U postgres ledger
  $C exec -T postgres pg_restore -U postgres -d ledger < backups/<stamp>/ledger.dump
  $C up -d --wait
  ```
  To get a dump from Object Storage: `rclone copy backups:<bucket>/<stamp> /opt/ledger/backups/<stamp>`, with the same `RCLONE_CONFIG_BACKUPS_*` variables `backup.sh` sets.
* **Whole server**: restore a Hetzner backup from the console. It contains everything, including the volumes.
* **A copy on your laptop** (users and data), and running it locally: §19.9.
* **Dead-lettered messages**: `docker exec ledger-rabbitmq-1 rabbitmqctl list_queues name messages | grep _error`.
* **Server updates**: security updates install automatically. Reboot from time to time (`sudo reboot`); the containers restart by themselves.
* **Moving to AWS later**: dump both databases, restore them into RDS, and follow §18 (and point the DNS at the load balancer).

### 19.7 Verified before merging

The production compose ran locally under a separate project name, with `DOMAIN=localhost` (Caddy's internal CA), using the four images built from this branch:

* **Containers**: postgres and rabbitmq started healthy, the migrator applied every module, and api and keycloak became healthy.
* **Web and API through Caddy**:
  * HTTP → HTTPS redirect, `/` → `/en/`
  * `/health` 200
  * `/config.json` returns the public issuer
* **Keycloak through Caddy**:
  * discovery issuer `https://auth.localhost/realms/ledger`
  * `/admin/` and `/realms/master/...` answer 403 outside `ADMIN_CIDRS`, and pass when allowed
* **Authentication**:
  * `/api/wallets` answers 401 without a token
  * with a token from that Keycloak: 200, and creating a wallet answered 201 (the module queues exist in RabbitMQ)
* **Backups**:
  * `backup.sh` dumped both databases, and the `ledger` dump restored into an empty database
  * the rclone commands uploaded and listed the dumps on a local S3 server
* **Static checks**: `cloud-init schema` is valid, actionlint and shellcheck are clean, and `docker compose config` is valid.
* **Not verified yet**: the real server, DNS, Let's Encrypt and a GitHub run. They need the domain and the server.

### 19.8 Bringing data from another environment (e.g. local)

Added on branch `feature/vps-import-local-data`, to move the real data kept in local development to the VPS.

**What has to change.** Every module stores data under the internal user id. Only `identity.users."ExternalId"` ties that id to a login: the Keycloak user id (`sub`). The local Keycloak and the server's are different, so the same person has another id on the server. The import copies the whole database and rewrites `ExternalId` for each person. Keycloak itself is not copied: people get new accounts (and passwords) on the server.

**Before you start**
* Both sides run the same version (or the server a newer one: the migrator runs after the import).
* Create the server's Keycloak users in realm `ledger`, with the **same emails** as locally, and note each one's **ID** (*Users → user → ID*). Do it only for the people who need to log in now. Others can be linked later, but **before their first login**.
* Nothing on the server's data is kept: whatever was created there (e.g. a test login) is replaced. It is saved first, see step 4.

**Steps**
1. Locally, stop the API so the data doesn't change, and check that nothing is waiting to be published:
   ```bash
   docker compose --profile full stop api        # or stop the API you run from the IDE
   for s in accounts contacts debts groups identity investments transactions wallets; do
     docker exec ledger-postgres psql -U postgres -d ledger -Atc "select '$s', count(*) from $s.integration_outbox where \"ProcessedAt\" is null"
   done                                          # every count must be 0
   docker exec ledger-postgres pg_dump -U postgres -Fc ledger > ledger.dump
   ```
2. Copy it to the server: `scp ledger.dump deploy@<server>:/opt/ledger/backups/`.
3. On the server, from `/opt/ledger`:
   ```bash
   ./import-dump.sh backups/ledger.dump \
     gastimastra@gmail.com=<keycloak id> sebastianmastra@gmail.com=<keycloak id>
   ```
   It asks you to type `import`. `--yes` skips the question.
4. What it does:
   1. checks the arguments and that the file is a `pg_dump -Fc` dump
   2. stops `api` and `web` (the site is down for a minute)
   3. refuses to go on if RabbitMQ still has queued messages
   4. saves the current database to `backups/pre-import-<stamp>/ledger.dump`
   5. restores the dump into a separate `ledger_import` database
   6. for each mapping, checks that exactly one user has that email and that the id isn't taken by someone else, then sets `ExternalId`
   7. lists the users, then replaces `ledger` with `ledger_import`
   8. runs the migrator and starts everything (the new API process has no cached user ids)

   **Any failure before the replacement leaves the server as it was.**
5. Log in as each mapped user and check the balances. Delete `backups/ledger.dump` (and the local `ledger.dump`) when you're happy: they contain everything.

**Linking someone later** (e.g. Sofia):
1. Create their Keycloak user.
2. Before they log in, run `./link-user.sh <email> <keycloak id>`.

Users from the dump that are never linked (e.g. the local test users alice and bob) stay in the data, but nobody can log in as them.

**If someone logged in before being linked**, the API created a second, empty user with the same email, and `link-user.sh` refuses (*There are 2 users with email …*). To fix it:
1. Run, inside `docker compose … exec postgres psql -U postgres -d ledger`:
   `UPDATE identity.users SET "ExternalId" = 'unlinked-' || "Id" WHERE "ExternalId" = '<keycloak id>';`
   That makes the empty user unreachable.
2. Run `link-user.sh` again.
3. Restart the API (`docker compose … restart api`): it caches user ids for 30 minutes.

The empty user stays in the data with the same email. If their contacts then look duplicated, tell the developer.

**Undo the import**: restore `backups/pre-import-<stamp>/ledger.dump` as in §19.6.

**Rehearsed before merging**, on a local copy of the production stack loaded with a read-only dump of the real local database:
* **Failed mapping** (unknown email): exit 3, the existing data untouched, the temporary database removed, the app back up.
* **Real import** (Gaston and Sebastian):
  * a VPS user that had logged in before the import ended up with their original internal id
  * the four wallets, their balances and the summary (wallets, owed to me, net) matched the local data
  * creating a wallet afterwards worked, and its events went through RabbitMQ
* **`link-user.sh`**: Sofia, linked before her first login, got her original id; a user who logged in first got the "2 users" refusal; invalid arguments were rejected.

### 19.9 Keeping a copy of the VPS on your laptop

A full copy, **Keycloak users and Ledger data**, that you can keep as a backup and run locally when needed. Running it **replaces** the local environment (section B), and section C goes back.

* **Two files are the whole copy.** The server's PostgreSQL holds two databases: `keycloak` (realms, users, password hashes, OTP, the master admin) and `ledger` (every module). `backup.sh` already dumps both.
* **No remapping.** Because both are copied, the Keycloak user ids stay the same and still match `identity.users."ExternalId"`. That's unlike §19.8, which needs remapping.
* **Local Keycloak needs a different setup.** It normally runs with an embedded H2 database inside the `ledger-keycloak` container. To run the copied users, section B starts another Keycloak of the **same version** on a local PostgreSQL database.

**A. Take the copy**

1. On the server, make a fresh dump (or use last night's folder):
   ```bash
   cd /opt/ledger && ./backup.sh            # prints the folder: backups/<stamp>
   ```
2. On the laptop:
   ```bash
   scp -r deploy@<server>:/opt/ledger/backups/<stamp> ~/ledger-vps-copy/
   ```
   You get `ledger.dump` and `keycloak.dump`.
3. Keep them safe:
   * They contain **everyone's data and password hashes**: keep them on an encrypted disk (FileVault) and delete old copies.
   * Never put them inside the repository (`*.dump` is git-ignored as a safety net).
   * Note the Keycloak version they come from (`KEYCLOAK_VERSION` in `infra/keycloak-prod/Dockerfile`, **26.7.4** today). Restoring needs the same one.

**B. Run it locally (replaces the local environment)**

1. **Save the local state** so you can come back:
   * Stop the API and the web app (IDE, or `docker compose --profile full stop api web`).
   * Check that RabbitMQ is idle (`docker exec ledger-rabbitmq rabbitmqctl list_queues name messages`, all 0).
   * Save the local data:
     ```bash
     docker exec ledger-postgres pg_dump -U postgres -Fc ledger > ~/ledger-local-$(date +%F).dump
     ```
   * The local Keycloak users live inside the `ledger-keycloak` container: **stop it, don't remove it** (`docker compose down` would lose them).
2. **Ledger data:**
   ```bash
   cd ~/ledger-vps-copy
   docker exec ledger-postgres dropdb -U postgres --force ledger
   docker exec ledger-postgres createdb -U postgres ledger
   docker exec -i ledger-postgres pg_restore -U postgres -d ledger --no-owner --no-privileges < ledger.dump
   dotnet run --project src/Ledger.Migrator     # from the repository; only applies something if local code is newer
   ```
3. **Keycloak users:**
   ```bash
   docker exec ledger-postgres dropdb -U postgres --if-exists --force keycloak
   docker exec ledger-postgres createdb -U postgres keycloak
   docker exec -i ledger-postgres pg_restore -U postgres -d keycloak --no-owner --no-privileges < keycloak.dump
   docker stop ledger-keycloak
   docker run -d --name ledger-keycloak-vps --network ledger_default -p 8080:8080 \
     quay.io/keycloak/keycloak:26.7.4 start-dev \
     --db=postgres --db-url=jdbc:postgresql://postgres:5432/keycloak \
     --db-username=postgres --db-password=postgres --hostname=http://localhost:8080
   ```
   It runs on the usual port, so the API and the web app need no changes.
4. **Let the local web app log in.** The copied realm only accepts `https://ledger.<domain>`.
   1. Open `http://localhost:8080/admin/` and log in with your **production admin**. It was copied with its password and OTP.
   2. Realm **ledger** → *Clients* → `ledger-web`. Add:
      * *Valid redirect URIs*: `http://localhost:4200/*`, `http://localhost:4201/*`
      * *Valid post logout redirect URIs*: the same two
      * *Web origins*: `http://localhost:4200`, `http://localhost:4201`
   3. Save.
5. **Start the API and the web app as usual,** and log in with your **production** user and password.
   * `alice` and `bob` don't exist in this copy, so the E2E tests won't log in.
   * Scalar's `ledger-scalar` client isn't in the production realm either: add it by hand if you need it.

**C. Back to the normal local environment**
```bash
docker rm -f ledger-keycloak-vps && docker start ledger-keycloak
docker exec ledger-postgres dropdb -U postgres --force ledger
docker exec ledger-postgres createdb -U postgres ledger
docker exec -i ledger-postgres pg_restore -U postgres -d ledger --no-owner --no-privileges < ~/ledger-local-<date>.dump
docker exec ledger-postgres dropdb -U postgres --force keycloak     # optional
```

**Good to know**
* Outbox rows of the dump that weren't published yet get published locally when the API starts. That's expected, and it doesn't reach the server.
* The copy is a snapshot: changes made locally never go back to the server. To move local data to the server, use §19.8.
* **Rehearsed before merging**:
  * A production-like stack produced the two dumps with `backup.sh` (one user who had logged in, with a wallet).
  * They were restored with these commands into a separate PostgreSQL, and a stock Keycloak 26.7.4 `start-dev` ran on it.
  * The issuer was the local URL. The copied password logged in, a wrong one was refused, and the token's `sub` matched the restored `ExternalId` (with its wallet).
  * The copied master admin logged in and added the localhost redirect URIs. After that, `http://localhost:4200/` got the login form and an unknown redirect was refused.

---

## 20. Partial payments

Added on branch `feature/partial-payments`, to the API, the web app and the console. Use case: my brother owes me 150 (older) and 50. He pays me 170: the 150 is paid and only shows in the history, and 30 are left of the 50. Later I find out he paid 130: I change the payment, and now 20 are left of the 150 and the 50 is untouched.

### 20.1 Decisions (asked before building)

| Topic | Decision |
| --- | --- |
| What a payment pays | The **oldest debts first**, by the date of the loan, split or existing debt that created them. The debts it covers become **Settled** and only show as history; the next one is **partially paid**. |
| Debts both ways | **Net first**: what we owe each other cancels out (oldest with oldest), then the money pays the payer's debts. |
| Paying more than what is owed | The **extra becomes a new debt** the other way, created by the payment (he owes me 200 and pays 250: now I owe him 50). |
| Scope | Debts **outside groups**. Groups keep paying their suggested payments (GroupPair, unchanged). |
| Editing the payment | Through the usual **change flow** (§15): reversal plus a new version, linked users confirm it. The new amount is applied again from the oldest debt. Only whoever recorded the payment can change its amount or wallet. |
| Editing a paid loan, split or existing debt | **Blocked** while it has payments (fully or partly): change or delete the payments first, as before with settled debts. |
| The old actions | *Settle* (one debt) and *Settle up* (a person) are replaced by **Record payment**, pre-filled with the whole balance (the same result as *Settle up*). |
| How it is shown | Debts show **what is left of the original** ("€20.00 of €150.00") with a *Partially paid* badge; balances count what is left. A payment lists the debts it paid; a debt lists its payments. A **preview** shows what a payment would pay before recording it. |

This replaces decision P6 (full settlement only).

### 20.2 Model

* **`DebtPayment`** (`debts.debt_payments`): how much of one debt a settlement paid (`SettlementOperationId`, `DebtId`, `Amount`). A debt can have several. Rows are never deleted: they count while their settlement is the current version, reserve the debt while it is being recorded (`Pending`), and stay as history when the version is replaced, deleted or refused.
* **`Debt.Paid`** (and `Remaining = Amount − Paid`, `IsPartiallyPaid`). `ApplyPayment` makes it Settled when nothing is left; `RemovePayment` reopens it. Group debts keep using `SettlementOperationId` (settled all at once when the group is even), and set `Paid = Amount` when settled.
* **`PaymentAllocation.Allocate(payer, payee, amount, openDebts)`** (domain, pure):
  1. offset: the oldest debt of each direction cancel out until one side is empty;
  2. pay: the money pays the debts the payer owes the payee, oldest first (by the operation's date, then creation time);
  3. extra: whatever is left is owed by the payee to the payer.
* A payment is a `Settlement` operation with scope **`Payment`**: `FromPersonId` = payer, `ToPersonId` = payee, `Total` = the amount, `WalletId` = the initiator's wallet. Its extra debt has the payment as its operation, so a new version supersedes it like any debt of an operation.
* **The ledger does not change.** Postings are per counterparty, not per debt, so allocations live only in Debts. Each user's leg (`LedgerLegs.ForPayment`) is: cash ±amount, Receivable/Payable reduced by what was paid in each direction, plus the extra as a new position. It balances (`cash + Σ positions = 0`).

| Example (my books) | Postings |
| --- | --- |
| He owes 150 + 50, pays 170 | Wallet +170 · Owed to me [him] −170 |
| He owes 200, I owe 30, he pays 100 | Wallet +100 · Owed to me [him] −130 · I owe [him] +30 |
| He owes 200, pays 250 | Wallet +250 · Owed to me [him] −200 · I owe [him] −50 |

### 20.3 Flows

```text
POST /api/debts/payments/preview { contactId, currency, direction, amount }   → lines (paid, left), extra, balance after
POST /api/debts/payments { contactId, currency, direction: TheyPaidMe|IPaidThem, amount, walletId, occurredAt?, description? }
  → refused while another payment between the two is being recorded or changed (payment.pending), or a debt has a pending change
  → Debts: allocate, store the Payment + its rows (+ the extra debt) ──LedgerPostingRequested──► Transactions
  ← Completed: each row applied to its debt (fully paid → Settled), the extra debt opened · Rejected: rows stop counting
PUT /api/debts/operations/{id} { amount?, walletId?, occurredAt, description, reason }   (a payment)
  → built again without the previous version (what it paid is available again, its extra debt left out); linked users confirm
  → applied: the previous version's rows are taken back, the new ones applied, the old extra debt superseded
DELETE /api/debts/operations/{id} → what it paid is owed again
GET /api/debts/{id}/payments → the payments of a debt · GET /api/debts/operations/{id} → allocations and extraAmount
```

`POST /api/debts/settlements` now only pays a group's suggested payment (`groupId`, `fromPersonId`, `toPersonId`, `currency`, `walletId?`).

### 20.4 Migration

`PartialPayments` (Debts) adds `debts.Paid` and `debt_payments`, and backfills them:

* every debt **outside groups** that a settlement paid (also one still being recorded) gets one payment row for its whole amount, and stops pointing at the settlement;
* `Paid = Amount` for Settled debts (group debts too).

Old settlements (scopes `Debt` and `Contact`) work like payments from then on: they can be deleted (the debts reopen) and their amount can be changed. Checked on a copy of the development database (2 settled debts → 2 rows for their full 62.00, no group debt touched, no settled debt without a row).

### 20.5 Clients

* **Web:** *Record a payment* (header) and *Record payment* per person (pre-filled with the balance) open `PaymentDialog`: person, currency, who paid, amount, wallet, date, description and a **live preview** (each debt paid, what is left, any extra and the balance afterwards). Editing a payment opens the same dialog with the amount (group settlements keep `SettlementEditDialog`). The debts list shows *left of the original*, *Partially paid*, a *Payments* button and a *Show paid debts* switch (history). Operation history lists what each version paid. English and Spanish.
* **Console:** Debts → *Record a payment (pays the oldest debts first)* with the preview and a confirmation; *Payments of a debt*; *Paid debts (history)*; the overview shows *Left* and *Of*. Editing a payment asks for the amount and shows the preview.

### 20.6 Verified

* **Unit:** the allocation (the brother example, the edit to 130, exact balance, netting both ways, overpayment, ordering of debts of the same moment, debts with nothing left, invalid amounts), `ApplyPayment`/`RemovePayment`, and that every leg balances (partial, exact, overpayment, with mutual debts; only registered people get a leg).
* **Integration (Testcontainers):** the brother flow (preview, record, partial and settled debts, payments of a debt, allocations, a paid loan blocked, edit to 130, delete reopens); mutual debts plus an overpayment with a linked user (10 cancel out, 30 pay, 50 extra), only the recorder may change the amount, the edit confirmed by the linked user leaves both even; a partial payment copied into a user's books when a contact is linked. The existing settlement tests now record payments.
* **Real stack** (copy of the development database, isolated vhost, API on 5219): the migration backfill; an old full settlement deleted (with the linked user's confirmation) reopens its debt; Playwright: the full E2E suite, including the brother scenario (record 170 → edit to 130 → delete) and paying an existing debt; a scripted console run (record a payment with its preview, the overview with partial debts, the payments of a debt).

---

## 21. Web app: grouped actions, detail pop-ups and installable app

Added on branch `feature/web-pwa-ux`, web only: **the API does not change**. The goal is to share Ledger as a mobile app and to make crowded screens lighter. Infrastructure changes: the Keycloak realms (passkeys) and the web image (nginx, service worker).

### 21.1 Decisions (asked before building)

| Topic | Decision |
| --- | --- |
| Rows with many actions | **Clicking a row opens a detail pop-up** with all its information and actions (and History); a **⋯ menu** on the row has the same actions for quick use. |
| Screens | Transactions, wallets, debts (people, debts, loans/splits/payments), groups, contacts, investments, categories. The dashboard stays as it was. |
| Filters | A **Filters** button (with the number of active filters) opens a **side panel** on desktop and a **bottom sheet** on phones. Active filters show as removable **chips**. |
| Headers with many buttons | One main button stays (Split an expense, Buy, Add expense); the rest go to a menu (**Add ▾**, **More ▾**, ⋯). |
| Installable app (PWA) | Manifest, icons, service worker, plus an **update prompt**, **Install app** in Settings (iPhone: "Share → Add to Home Screen"), an **offline notice** and **Remember me on this device**. |
| Login | **Optional passkeys** (Face ID, fingerprint), set up from Settings; the password keeps working. |
| App icon | Generated by `npm run icons -- <image>` from your image (a placeholder "L" until then). |

### 21.2 UI kit additions (`clients/web/src/app/ui`)

* **`ui-actions-menu`** (CDK menu): the "⋯" button with `{ label, run, icon?, danger?, hidden?, hint? }` items, or a text button with `[text]` ("Add ▾"). Clicks don't reach the row.
* **`uiRowAction`** (`(rowOpen)`): a row or list item opens its detail with click, Enter or Space; buttons, links and inputs inside it keep their own click. The element keeps its role (row, list item) and gets `aria-haspopup="dialog"`.
* **`DialogService.open(..., 'panel')`**: side panel (≥ 640 px) or bottom sheet (phones), with safe-area padding.
* **`ui-filter-chips`**: active filters with ✕ and "Clear all".
* **Toasts with an action** (`ToastService.withAction`), used by the update prompt.

The detail pop-ups close with the chosen action and the page runs its existing handler, so behaviour is the same as before: `TransactionDetailDialog`, `WalletDetailDialog`, `PersonDebtsDialog`, `DebtDetailDialog` (includes the payments of the debt), `OperationDetailDialog` (`OperationActions.open()` and `.menu()`, shared by Debts, Groups and the Transactions "Operation" action), `ContactDetailDialog`, `AssetDetailDialog`.

### 21.3 Installable app

| Piece | Where | Notes |
| --- | --- | --- |
| Manifest | `public/manifest.webmanifest` (copied into `/en/` and `/es/`) | `standalone`, `start_url`/`scope` `./`, theme colours, icons 192/512/maskable |
| Icons | `public/icons/`, `public/favicon.ico` | `npm run icons -- path/to/icon.png` (Playwright renders every size; maskable with padding; Apple icon on a solid background) |
| `index.html` | | manifest link, `theme-color` (light/dark), Apple tags, `viewport-fit=cover` |
| Service worker | `ngsw-config.json`, `public/sw.js`, `provideServiceWorker('sw.js')` | Built with `npm run build` (`ng build --service-worker=ngsw-config.json`; `angular.json` is not changed). Each language has its own worker, scoped to `/en/` or `/es/`. Not registered by `ng serve`. |
| What is cached | | The app shell (prefetch) and images (lazy). **No data groups: the API is never cached**; `/config.json` is always read from the network. |
| `sw.js` | | Angular's worker plus one rule: **requests to other origins (Keycloak) are left to the browser**. Angular's worker would re-send them itself; the login and token requests must go straight to Keycloak. |
| Update prompt | `core/pwa/app-updates.ts` | After a deploy the new version downloads in the background; a toast offers **Reload**. Checks when the app comes back to the foreground and every 30 minutes. |
| Install | `core/pwa/install-prompt.ts`, Settings | Chrome/Edge/Android: the browser's prompt from an *Install app* button. iPhone/iPad: instructions. Installed: "Ledger is installed". |
| Offline | `core/pwa/network.ts`, shell banner, `AuthService` | The installed app opens offline (cached shell, last `/config.json`, the stored login) and says so; data needs a connection; back online it reloads. |
| Safe areas | `pt-safe`/`pb-safe` (header, bottom bar, sheets) | Notch and home indicator in standalone mode. |

**Remember me on this device** (`core/auth/device-session.ts`, Settings): on by default in the installed app, off in a browser tab.

* **On:** the tokens live in `localStorage` and the app asks Keycloak for **`offline_access`**, a long-lived refresh token (offline session idle: 30 days by default). Reopening the app keeps you logged in.
* **Off:** `sessionStorage`, as before.
* **Turning it on:** logs in again in the background (Keycloak still has the session) and comes back to Settings.
* **Logging out:** revokes the offline token and clears both storages.
* **If Keycloak refuses the offline token** (the account lacks the `offline_access` role): the app turns the option off, logs in normally and says why. Users created in the admin console have it through the realm's default roles. The local test users `alice` and `bob` now get those roles too.

**Deployment** (`nginx.conf.template`): `Cache-Control: no-cache` for `index.html`, `sw.js`, `ngsw.json`, `ngsw-worker.js`, `safety-worker.js` and the manifest (served as `application/manifest+json`); hashed bundles (`main-…`, `chunk-…`, `styles-…`) are `public, max-age=31536000, immutable`. HTTPS is required for service workers (the VPS has it; `localhost` counts as secure locally). CI and the Docker image build with `npm run build`.

### 21.4 Optional passkeys (Face ID)

* **Realms** (`infra/keycloak` and `infra/keycloak-prod`): `webAuthnPolicyPasswordlessPasskeysEnabled: true` and the relying party name "Ledger". The `webauthn-register-passwordless` required action is enabled but **not default**, so passkeys stay optional.
* **Settings → Security**:
  * *Set up a passkey* redirects to Keycloak with `kc_action=webauthn-register-passwordless` and comes back to Settings ("Passkey ready").
  * *Manage passkeys and password* opens the Keycloak account page.
* **Login:** the Keycloak page offers the passkey (autofill on the username field, then Face ID or fingerprint). The password keeps working.
* **On your server:** the existing realm is **not re-imported**, so enable it once in the admin console of realm `ledger`:
  1. *Authentication → Policies → Webauthn Passwordless Policy*: turn on **Enable Passkeys**, set *Relying party entity name* to "Ledger", save.
  2. *Authentication → Required actions*: check that **Webauthn Register Passwordless** is enabled and **not** set as default action.

### 21.5 Verified

* **Unit (web):**
  * row action: opens on click, Enter and Space, not from inner buttons; the menu shows only visible actions and runs them
  * filter chips
  * Remember me: default by display mode, storage choice, moving and clearing the login
  * install prompt states; offline detection; iPhone/iPad detection
* **E2E (Playwright, against the production Docker image with its service worker):**
  * the full suite, updated for the ⋯ menus, detail pop-ups and filters panel
  * new: the phone layout (bottom sheet, chips, menus, detail)
  * new: the installable app (manifest type and headers, worker scope, opens offline with the notice)
* **Remember me:**
  * the token moves to `localStorage` with `offline_access`
  * a new browser with **no cookies** opens logged in
  * the offline token refreshes; logging out removes it and Keycloak rejects it afterwards
* **Update prompt:** a changed `ngsw.json` in the running container shows "A new version of Ledger is available" → Reload.
* **Passkeys** (scratch Keycloak 26.7.4, a fresh import of the realm, a virtual authenticator):
  * the app's `kc_action` registers a passkey and comes back with `success`
  * a login with only the passkey works, and the password still works
* **Builds:** EN + ES, plus the .NET suite (unchanged API).
