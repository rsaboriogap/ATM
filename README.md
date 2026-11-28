# ATM

A simple ATM system built with .NET 10: an ASP.NET Core Minimal API backed by SQLite (EF Core), and a Blazor WebAssembly client. Users can view accounts, deposit, withdraw, transfer between accounts and review transaction history.

Every balance change is recorded in an **append-only ledger**. Account balances are derived from that ledger, and the database itself rejects writes that would break it.

Deposits, withdrawals and transfers are **idempotent**: each request carries an `Idempotency-Key`, so a retry after a timeout or dropped connection is never applied twice.

For how the solution is designed, see the [Architecture overview](docs/architecture/architecture-overview.md).

## Tech stack

| Layer | Technology |
|---|---|
| API | ASP.NET Core Minimal APIs, OpenAPI + Swagger UI |
| Persistence | Entity Framework Core 10 + SQLite |
| Client | Blazor WebAssembly + Bootstrap |
| Tests | xUnit, Moq, bUnit, `Microsoft.AspNetCore.Mvc.Testing`, coverlet |

## Project structure

```
ATM.slnx
├── src/
│   ├── ATM.Domain/           Entities, money rules, domain exceptions
│   ├── ATM.Application/      Use cases (AtmService), DTOs, repository interfaces
│   ├── ATM.Infrastructure/   EF Core DbContext, migrations, ledger triggers, repositories, reconciliation
│   ├── ATM.Api/              Minimal API endpoints, error handling, CORS, Swagger
│   └── ATM.Client/           Blazor WebAssembly front end
├── tests/
│   ├── ATM.Tests/            Unit, integration and ACID tests for Domain/Application/Infrastructure/API
│   └── ATM.Client.Tests/     bUnit component and client service tests
├── Scripts/                  Setup.ps1 (local setup)
└── docs/architecture/        Architecture overview and design notes
```

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (`dotnet --list-sdks` should show a `10.0.x` entry)
- A trusted ASP.NET Core HTTPS development certificate
- Optional: Visual Studio 2026, or VS Code with the C# Dev Kit (Visual Studio 2022 doesn't officially support targeting .NET 10)

No database server is needed — SQLite is file-based and created automatically.

## Setup

### Quick setup (Windows PowerShell)

[Scripts/Setup.ps1](Scripts/Setup.ps1) runs every setup step below. It checks for the .NET 10 SDK, trusts the dev certificate, then restores and builds the solution. It can also run the tests and start both apps:

```powershell
git clone https://github.com/rsaboriogap/ATM.git
cd ATM
.\Scripts\Setup.ps1                  # check SDK, trust cert, restore, build
.\Scripts\Setup.ps1 -RunTests -Run   # ...then run tests and start the API + client
```

| Switch | Effect |
|---|---|
| `-Configuration Release` | Build in Release (default `Debug`) |
| `-SkipCertificate` | Skip the HTTPS dev-certificate step |
| `-ResetDatabase` | Delete `atm.db` so it's recreated and reseeded |
| `-RunTests` | Run all tests after building |
| `-Coverage` | Run tests with code coverage |
| `-Run` | Start the API and client in two new windows |

If script execution is blocked, run `powershell -ExecutionPolicy Bypass -File .\Scripts\Setup.ps1` instead.

To set up manually, follow the steps below.

### 1. Clone the repository

```bash
git clone https://github.com/rsaboriogap/ATM.git
cd ATM
```

### 2. Trust the HTTPS development certificate

The client calls the API over HTTPS, so the dev certificate must be trusted:

```bash
dotnet dev-certs https --trust
```

### 3. Restore and build

```bash
dotnet restore ATM.slnx
dotnet build ATM.slnx
```

## Running the application

The API and the client run as two separate processes. Start both using their **`https`** launch profiles — the client is configured to call `https://localhost:7102/`, and the API only allows CORS requests from the client's `https` profile origins.

### Option A — command line

In one terminal, start the API:

```bash
dotnet run --project src/ATM.Api --launch-profile https
```

In a second terminal, start the client:

```bash
dotnet run --project src/ATM.Client --launch-profile https
```

### Option B — Visual Studio

1. Open `ATM.slnx`.
2. Right-click the solution → **Configure Startup Projects…** → **Multiple startup projects**.
3. Set both `ATM.Api` and `ATM.Client` to **Start**, and choose the `https` profile for each.
4. Press **F5**.

### URLs

| App | URL |
|---|---|
| Blazor client | https://localhost:7101 |
| API | https://localhost:7102 |
| Swagger UI (Development only) | https://localhost:7102/swagger |
| OpenAPI document | https://localhost:7102/openapi/v1.json |

### Uncertain operations

If the client can't confirm whether an operation went through (timeout, lost connection, `503`), it shows a **Transaction status uncertain** alert and blocks new operations until the outcome is known:

- **Retry operation** — resends the same operation with the same idempotency key. It's safe: the server applies it at most once.
- **Refresh accounts** — reloads balances and history and discards the pending operation.

### Database

On startup the API applies EF Core migrations automatically and creates `atm.db` in the API's working directory. It seeds two accounts if they don't exist, each with an `OpeningBalance` ledger entry:

| Account | Opening balance |
|---|---|
| Checking | $1,000.00 |
| Savings | $1,000.00 |

Before it serves requests, the API also verifies the database and **refuses to start** if any check fails:

- SQLite runs in WAL mode with `synchronous = FULL` and foreign keys on, with no foreign-key violations.
- All seven append-only ledger triggers are present.
- Every account's stored balance matches its ledger, and the ledger passes the integrity checks (one opening entry per account, no sequence gaps, unbroken running balances, complete transfer pairs).

Inconsistencies are reported, never repaired automatically.

To reset the data, stop the API and delete `atm.db` (plus any `atm.db-shm` / `atm.db-wal` files). It will be recreated on the next run.

The database also holds an `IdempotencyRecords` table with one row per successful write, so retries can be recognized. Rows are kept indefinitely; deleting `atm.db` clears them too.

## Configuration

### API — `src/ATM.Api/appsettings*.json`

| Key | Default | Purpose |
|---|---|---|
| `ConnectionStrings:AtmDatabase` | `Data Source=atm.db;Foreign Keys=True;Default Timeout=30` | SQLite connection string |
| `Cors:AllowedOrigins` | `https://localhost:7101`, `http://localhost:5101` (Development) | Origins allowed to call the API |

### Client — `src/ATM.Client/wwwroot/appsettings*.json`

| Key | Default | Purpose |
|---|---|---|
| `Api:BaseUrl` | `https://localhost:7102/` | Base URL of the ATM API |
| `Api:TimeoutSeconds` | `30` | HTTP request timeout |

If you change the ports in either `launchSettings.json`, update `Api:BaseUrl` and `Cors:AllowedOrigins` to match.

CORS allows the `Content-Type`, `Accept` and `Idempotency-Key` request headers. Any client other than `ATM.Client` must send `Idempotency-Key` on every `POST`.

## API overview

All endpoints are under `/api`. Amounts are in USD, must be between `0.01` and `1,000,000`, and may have at most two decimal places. An account balance can't go below `0` or above `1,000,000,000`.

| Method | Route | Description |
|---|---|---|
| `GET` | `/api/accounts` | List all accounts |
| `GET` | `/api/accounts/{id}` | Get an account by ID |
| `POST` | `/api/accounts/{id}/deposits` | Deposit funds — body: `{ "amount": 100.00 }` |
| `POST` | `/api/accounts/{id}/withdrawals` | Withdraw funds — body: `{ "amount": 50.00 }` |
| `GET` | `/api/accounts/{id}/transactions` | Get an account's transaction history |
| `POST` | `/api/transfers` | Transfer funds — body: `{ "sourceAccountId": "…", "destinationAccountId": "…", "amount": 25.00 }` |

Transaction history is returned newest first and includes the account's `OpeningBalance` entry.

### Idempotency keys

The three `POST` endpoints require an `Idempotency-Key` header containing a GUID generated by the client, one per operation:

```http
POST /api/accounts/{id}/withdrawals
Content-Type: application/json
Idempotency-Key: 3f2b8c1e-6a0d-4b7e-9c55-2d1f0e8a7b64

{ "amount": 50.00 }
```

If a request's outcome is unknown (timeout, dropped connection, `503`), resend it **with the same key**. The server applies each key at most once:

- Same key and same request: returns the original `200` response without posting again. The balances in it are as of the original request.
- Same key and a different request: `422 idempotency_key_conflict`.
- Requests that fail (for example `409 insufficient_funds`) post nothing and don't use up the key, so a retry runs normally.

Errors are returned as RFC 7807 `ProblemDetails` with a machine-readable `code`:

| Status | Code | When |
|---|---|---|
| `400` | `invalid_amount` | Amount out of range or more than two decimals |
| `400` | `same_account_transfer` | Source and destination are the same account |
| `400` | `invalid_request` | Malformed request body, or an `Idempotency-Key` that isn't a GUID |
| `400` | `idempotency_key_required` | `Idempotency-Key` header missing or empty on a `POST` |
| `400` | `validation_error` | Required field missing or invalid (e.g. no `amount`) |
| `404` | `account_not_found` | Unknown account ID |
| `404` | `not_found` | Unknown route |
| `409` | `insufficient_funds` | Withdrawal or transfer exceeds the balance |
| `409` | `balance_limit_exceeded` | The resulting balance would exceed the maximum |
| `415` | `unsupported_media_type` | Body isn't JSON |
| `422` | `idempotency_key_conflict` | The `Idempotency-Key` was already used for a different request |
| `503` | `database_busy` | SQLite was locked; retry with the same `Idempotency-Key` |
| `500` | `internal_server_error` | Anything else |

You can try the endpoints from Swagger UI or with [src/ATM.Api/ATM.Api.http](src/ATM.Api/ATM.Api.http) in Visual Studio / VS Code (REST Client extension). Run its **Get accounts** request first; the others use the Checking account ID from that response.

## Running tests

Run all tests:

```bash
dotnet test ATM.slnx
```

Run a single test project:

```bash
dotnet test tests/ATM.Tests
dotnet test tests/ATM.Client.Tests
```

Integration tests use their own SQLite databases and don't touch `atm.db`, so the API doesn't need to be running.

The suites in `tests/ATM.Tests/Integration/ACID` cover atomicity, consistency (ledger triggers and reconciliation), isolation (concurrent operations), durability (data survives reopening the database) and idempotency (replays, conflicts, concurrent retries with the same key). For example:

```bash
dotnet test tests/ATM.Tests --filter "FullyQualifiedName~Integration.ACID"
dotnet test tests/ATM.Tests --filter "FullyQualifiedName~Idempotency"
```

### Code coverage

Collect coverage (Cobertura format) with coverlet:

```bash
dotnet test ATM.slnx --collect:"XPlat Code Coverage" --settings coverage.runsettings
```

Results are written to `tests/*/TestResults/<guid>/coverage.cobertura.xml`. To generate an HTML report, install [ReportGenerator](https://github.com/danielpalme/ReportGenerator):

```bash
dotnet tool install -g dotnet-reportgenerator-globaltool
reportgenerator -reports:"tests/**/TestResults/**/coverage.cobertura.xml" -targetdir:coverage-report -reporttypes:Html
```

## Database migrations

Migrations live in `src/ATM.Infrastructure/Persistence/Migrations`. To add one, install the EF Core CLI and run it with the API as the startup project:

```bash
dotnet tool install -g dotnet-ef
dotnet ef migrations add <MigrationName> \
  --project src/ATM.Infrastructure \
  --startup-project src/ATM.Api \
  --output-dir Persistence/Migrations
```

New migrations are applied automatically the next time the API starts.

Things to know about the ledger migrations:

- Migrations that touch the ledger don't support `Down` — rolling back would remove its protections.
- The ledger triggers are raw SQL, not part of the EF model. Any migration that rebuilds `Transactions` or `Accounts` (for example, some column changes in SQLite) drops them, so it must recreate them. Otherwise the API refuses to start.

## Troubleshooting

- **Client shows a network/CORS error** — make sure the API is running on `https://localhost:7102` and that both apps were started with the `https` profile. The client's `http` profile (port 5088) isn't in the API's allowed origins.
- **Browser warns about the certificate** — run `dotnet dev-certs https --trust` and restart the browser.
- **`503 Database temporarily unavailable`** — SQLite was locked by another writer. Retry the request with the **same** `Idempotency-Key`; it's applied at most once. In the client, use **Retry operation** on the "Transaction status uncertain" alert.
- **API fails at startup with `Financial ledger inconsistencies detected` or `Append-Only Ledger triggers are missing`** — the database was changed outside the app or by a migration that dropped the triggers. For local development, stop the API and delete `atm.db` (and `-shm` / `-wal`) to start fresh.
- **Port already in use** — stop the other process or change the port in `Properties/launchSettings.json`, then update the client/CORS settings as described under [Configuration](#configuration).
