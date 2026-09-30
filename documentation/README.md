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
| P6 | How is a debt settled? | **Full settlement only**, both for one debt and for everything with a contact (P11). | No partial repayments. |
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

### 4.3 Debts: counterparty dimension

Postings on *Owed to me*, *I owe* and *Pending settlements* carry a `CounterpartyId`: a contact's **person id**, or a **group id** for group expenses. "How much does Pedro owe me?" is the sum of those postings for Pedro. The `counterparties` projection in Transactions provides names, and the contact filter of the history uses it.

### 4.4 Group expenses

Inside a group, each registered member's position is recorded **against the group** (the counterparty is the group id), not against individual people. This is what makes **simplified** payments representable in the books.

Example: raw debts A→B 10 and B→C 10 simplify to A pays C 10. With per-person counterparties, B's books would never be updated. Against the group: A pays 10 (reducing A's debt to the group), C receives 10 (reducing what the group owes C), and B was already even.

Pairwise who-owes-whom debts are still computed and stored in Debts, and shown in the group balances ("raw debts"). Once every member of a group is even in a currency, all its open debts in that currency are marked Settled.

### 4.5 People and person ids

* A registered user's **person id is their user id**.
* A non-registered contact gets a person id the first time it is added (its contact id). If somebody else's debt is mirrored to a linked user, the auto-created contact on their side reuses that person id.
* Linking a contact to a user **changes its person id** to the user id, so it is only allowed while the contact is **unused** (not in any debt or group). Otherwise, create a new contact with the user's email.

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

`POST /api/debts/settlements`, always a full settlement:

* `{ "scope": "Debt", "debtId", "walletId" }`: one debt outside groups.
* `{ "scope": "Contact", "contactId", "currency", "walletId" }`: every open debt with that person in that currency; money moves only for the net.
* `{ "scope": "GroupPair", "groupId", "fromPersonId", "toPersonId", "currency", "walletId?" }`: must match a suggested payment exactly. `walletId` is required if I am the payer or the receiver.

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
* Settlement is full only (P6). Partial repayments would need a debt balance.
* A contact can't be linked to a user once it's used (I8). Re-keying past debts would lift this.
* Deleting a wallet right after creating it relies on retries while the account is still being created.
* **Licensing:** MediatR 12 and MassTransit 8 are the last Apache versions. MassTransit 8 gets security fixes until the end of 2026; plan a move (to MassTransit 9 with a license, Wolverine, or plain RabbitMQ.Client, since the outbox and inbox are already ours).
* **Operations:** no outbox/inbox cleanup job yet, no dead-letter dashboard, no metrics or tracing. Adding OpenTelemetry and periodic cleanup of processed outbox and inbox rows is recommended.
* Production concerns not addressed in the MVP: HTTPS, secrets management, Keycloak in production mode, and running the migrator as a pipeline step (see Migrations.md).

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
